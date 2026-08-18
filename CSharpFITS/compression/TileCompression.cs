namespace nom.tam.fits.compression
{
    using System;
    using System.Collections.Generic;

    /// <summary>The tile compression algorithm named by the <c>ZCMPTYPE</c> keyword.</summary>
    public enum TileCompressionType
    {
        /// <summary><c>RICE_1</c>: Rice coding of pixel differences. The default of
        /// <c>fpack</c> and of essentially every astronomical writer.</summary>
        Rice = 0,

        /// <summary><c>GZIP_1</c>: deflate over the tile's bytes.</summary>
        Gzip1 = 1,

        /// <summary><c>GZIP_2</c>: deflate over a byte-shuffled copy of the tile.</summary>
        Gzip2 = 2,

        /// <summary><c>PLIO_1</c>: the IRAF pixel-list run encoding, for masks.</summary>
        Plio = 3,

        /// <summary><c>HCOMPRESS_1</c>: the H-transform plus quadtree coder.
        /// Recognised but not yet decoded.</summary>
        HCompress = 4,

        /// <summary><c>NOCOMPRESS</c>: the tile bytes are stored verbatim.</summary>
        None = 5,

        /// <summary>A <c>ZCMPTYPE</c> this library does not know. The header still
        /// reads -- the metadata is not encoded in any of this -- and only asking for
        /// the pixels reports the algorithm by name.</summary>
        Unknown = -1,
    }

    /// <summary>
    /// The compression half of a tile-compressed image header: everything the
    /// <c>Z</c>-prefixed keywords say about how the image was taken apart, parsed once
    /// so the decoder never has to consult the header again.
    /// </summary>
    internal sealed class TileCompressionParameters
    {
        /// <summary>The image's BITPIX, from <c>ZBITPIX</c>.</summary>
        internal int BitPix { get; private set; }

        /// <summary>The image's dimensions, in FITS axis order, so
        /// <c>Dims[0]</c> is <c>ZNAXIS1</c> and varies fastest.</summary>
        internal int[] Dims { get; private set; }

        /// <summary>The tile dimensions, in the same order as <see cref="Dims"/>.</summary>
        internal int[] TileDims { get; private set; }

        /// <summary>Number of tiles along each axis.</summary>
        internal int[] TileCounts { get; private set; }

        /// <summary>Total number of tiles, which is also the number of table rows.</summary>
        internal int TileCount { get; private set; }

        internal TileCompressionType CompressionType { get; private set; }

        /// <summary>The raw <c>ZCMPTYPE</c> string, kept for error messages so an
        /// unsupported algorithm can name itself.</summary>
        internal string CompressionName { get; private set; }

        internal QuantizationMethod Quantization { get; private set; }

        /// <summary>The raw <c>ZQUANTIZ</c> string, kept so an unknown method can name
        /// itself.</summary>
        internal string QuantizationName { get; private set; }

        internal int DitherSeed { get; private set; }

        /// <summary><c>BLOCKSIZE</c> for Rice.</summary>
        internal int BlockSize { get; private set; }

        /// <summary><c>BYTEPIX</c> for Rice: the width of one value in the tile.</summary>
        internal int BytePix { get; private set; }

        /// <summary>Whether <c>ZBLANK</c> declares an undefined-pixel value.</summary>
        internal bool HasBlank { get; private set; }

        internal int Blank { get; private set; }

        /// <summary>The <c>ZMASKCMP</c> keyword, naming the algorithm used for a
        /// <c>NULL_PIXEL_MASK</c> column.</summary>
        internal string MaskCompressionName { get; private set; }

        /// <summary>True when the image is floating point AND was quantized onto
        /// integers, which is what makes ZSCALE/ZZERO meaningful.</summary>
        internal bool IsQuantizedFloat { get; set; }

        /// <summary>Number of pixels in a whole (unclipped) tile.</summary>
        internal int MaxTilePixels { get; private set; }

        /// <summary>Total pixels in the image.</summary>
        internal long PixelCount { get; private set; }

        /// <summary>True when the header says this is a tile-compressed image.</summary>
        internal static bool IsCompressedImageHeader(Header hdr)
        {
            if (hdr == null)
            {
                return false;
            }

            string xtension = hdr.GetStringValue("XTENSION");
            if (xtension == null || !xtension.Trim().Equals("BINTABLE", StringComparison.Ordinal))
            {
                return false;
            }

            return hdr.GetBooleanValue("ZIMAGE", false);
        }

        internal static TileCompressionParameters Parse(Header hdr)
        {
            var p = new TileCompressionParameters();

            p.BitPix = hdr.GetIntValue("ZBITPIX", 0);
            switch (p.BitPix)
            {
                case 8:
                case 16:
                case 32:
                case 64:
                case -32:
                case -64:
                    break;
                default:
                    throw new FitsException($"Invalid ZBITPIX in compressed image: {p.BitPix}");
            }

            int nAxis = hdr.GetIntValue("ZNAXIS", 0);
            if (nAxis < 0 || nAxis > 999)
            {
                throw new FitsException($"Invalid ZNAXIS in compressed image: {nAxis}");
            }

            p.Dims = new int[nAxis];
            p.TileDims = new int[nAxis];
            p.TileCounts = new int[nAxis];
            p.TileCount = 1;
            p.MaxTilePixels = 1;
            p.PixelCount = 1;

            for (int i = 0; i < nAxis; i++)
            {
                int dim = hdr.GetIntValue($"ZNAXIS{i + 1}", 0);
                if (dim < 0)
                {
                    throw new FitsException($"Invalid ZNAXIS{i + 1} in compressed image: {dim}");
                }

                // The convention's default tiling is one row: the whole of axis 1, and
                // a single element of every axis above it.
                int tile = hdr.GetIntValue($"ZTILE{i + 1}", i == 0 ? dim : 1);
                if (tile <= 0)
                {
                    tile = i == 0 ? dim : 1;
                }
                if (tile > dim)
                {
                    tile = dim;
                }

                p.Dims[i] = dim;
                p.TileDims[i] = tile;
                p.TileCounts[i] = tile > 0 ? (dim + tile - 1) / tile : 0;
                p.TileCount *= p.TileCounts[i];
                p.MaxTilePixels *= tile;
                p.PixelCount *= dim;
            }

            p.CompressionName = (hdr.GetStringValue("ZCMPTYPE") ?? "RICE_1").Trim();
            p.CompressionType = ParseCompressionType(p.CompressionName);

            p.QuantizationName = (hdr.GetStringValue("ZQUANTIZ") ?? "NO_DITHER").Trim();
            p.Quantization = ParseQuantization(hdr.GetStringValue("ZQUANTIZ"));
            p.DitherSeed = hdr.GetIntValue("ZDITHER0", 0);

            // Rice parameters travel as ZNAMEn/ZVALn pairs rather than as named
            // keywords, so that one mechanism serves every algorithm's settings.
            p.BlockSize = 32;
            p.BytePix = 4;
            foreach (var kv in ReadCompressionSettings(hdr))
            {
                if (kv.Key.Equals("BLOCKSIZE", StringComparison.OrdinalIgnoreCase))
                {
                    p.BlockSize = (int)kv.Value;
                }
                else if (kv.Key.Equals("BYTEPIX", StringComparison.OrdinalIgnoreCase))
                {
                    p.BytePix = (int)kv.Value;
                }
            }

            p.HasBlank = hdr.ContainsKey("ZBLANK");
            p.Blank = hdr.GetIntValue("ZBLANK", Quantizer.NullValue);
            p.MaskCompressionName = hdr.GetStringValue("ZMASKCMP");

            return p;
        }

        /// <summary>The <c>ZNAMEn</c>/<c>ZVALn</c> algorithm settings, in order.</summary>
        internal static IEnumerable<KeyValuePair<string, double>> ReadCompressionSettings(Header hdr)
        {
            for (int n = 1; ; n++)
            {
                string name = hdr.GetStringValue($"ZNAME{n}");
                if (name == null)
                {
                    yield break;
                }
                yield return new KeyValuePair<string, double>(
                    name.Trim(), hdr.GetDoubleValue($"ZVAL{n}", 0.0));
            }
        }

        private static TileCompressionType ParseCompressionType(string name)
        {
            switch (name.ToUpperInvariant())
            {
                case "RICE_1":
                case "RICE_ONE":       // the name cfitsio also accepts
                    return TileCompressionType.Rice;
                case "GZIP_1":
                    return TileCompressionType.Gzip1;
                case "GZIP_2":
                    return TileCompressionType.Gzip2;
                case "PLIO_1":
                    return TileCompressionType.Plio;
                case "HCOMPRESS_1":
                    return TileCompressionType.HCompress;
                case "NOCOMPRESS":
                    return TileCompressionType.None;
                default:
                    // Not recognising the codec is not a reason to fail the header: the
                    // metadata is readable regardless, and only the pixels are lost.
                    return TileCompressionType.Unknown;
            }
        }

        private static QuantizationMethod ParseQuantization(string value)
        {
            if (value == null)
            {
                return QuantizationMethod.NoDither;
            }

            switch (value.Trim().ToUpperInvariant())
            {
                case "NO_DITHER":
                    return QuantizationMethod.NoDither;
                case "SUBTRACTIVE_DITHER_1":
                    return QuantizationMethod.SubtractiveDither1;
                case "SUBTRACTIVE_DITHER_2":
                    return QuantizationMethod.SubtractiveDither2;
                default:
                    return QuantizationMethod.Unknown;
            }
        }

        /// <summary>The dimensions of the tile at <paramref name="tileIndex"/>, clipped
        /// where it runs off the edge of the image, and its origin. Tiles are numbered
        /// in row-major order with axis 1 varying fastest, which is also the order of
        /// the rows in the binary table.</summary>
        internal int TileGeometry(int tileIndex, int[] tileDims, int[] tileStart)
        {
            int pixels = 1;
            int remaining = tileIndex;
            for (int a = 0; a < Dims.Length; a++)
            {
                int which = remaining % TileCounts[a];
                remaining /= TileCounts[a];

                int start = which * TileDims[a];
                int size = TileDims[a];
                if (start + size > Dims[a])
                {
                    size = Dims[a] - start;
                }

                tileStart[a] = start;
                tileDims[a] = size;
                pixels *= size;
            }
            return pixels;
        }
    }
}
