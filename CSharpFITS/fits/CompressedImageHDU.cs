namespace nom.tam.fits
{
    using System;
    using System.Collections;
    using nom.tam.fits.compression;

    /// <summary>
    /// A tile-compressed image, as written by <c>fpack</c> (conventionally with a
    /// <c>.fz</c> extension) and by Siril, SharpCap, astropy and the survey archives.
    /// <para>
    /// On disk such an image is a binary table whose rows hold the compressed tiles,
    /// with the real image header carried alongside in <c>Z</c>-prefixed keywords.
    /// This class presents it as what it actually is: an <see cref="ImageHDU"/>. Its
    /// <see cref="BasicHDU.Header"/> is the translated IMAGE header, so
    /// <c>BITPIX</c>, <c>NAXIS</c>, <c>NAXISn</c> and every metadata card read exactly
    /// as they would from an uncompressed file, and <c>GetChannel</c> / <c>Tiler</c> /
    /// <c>Data.DataArray</c> behave identically. A reader that already handles FITS
    /// images therefore needs no changes to handle <c>.fz</c> files.
    /// </para>
    /// <para>
    /// Reading is supported for <c>RICE_1</c>, <c>GZIP_1</c>, <c>GZIP_2</c>,
    /// <c>PLIO_1</c> and <c>NOCOMPRESS</c>. <c>HCOMPRESS_1</c> is recognised and
    /// reported rather than silently mis-decoded. Writing compresses nothing: the HDU
    /// writes out as the plain image extension it presents, which makes a read plus a
    /// write a funpack.
    /// </para>
    /// <para>
    /// The compressed HDU is always an extension, so the translated header always says
    /// <c>XTENSION = 'IMAGE'</c> even where <c>ZSIMPLE</c> records that the image was
    /// the primary array of the original file; that is a question of which slot the
    /// HDU occupies in a file, not of what the image is.
    /// </para>
    /// </summary>
    public class CompressedImageHDU : ImageHDU
    {
        /// <summary>The header as it appears on disk: the binary table of compressed
        /// tiles, with the <c>Z</c>-prefixed keywords the translation consumed. Use it
        /// to inspect the compression itself; <see cref="BasicHDU.Header"/> is the
        /// image header the rest of the library works with.</summary>
        public Header CompressedHeader { get; }

        /// <summary>The algorithm the tiles were compressed with.</summary>
        public TileCompressionType CompressionType => CompressedData.CompressionType;

        /// <summary>How a floating-point image was quantized onto integers, if at all.
        /// Anything other than <see cref="QuantizationMethod.None"/> on a float image
        /// means the stored values are approximations of the originals.</summary>
        public QuantizationMethod Quantization => CompressedData.Quantization;

        /// <summary>The tile dimensions, in FITS axis order.</summary>
        public int[] TileDimensions => CompressedData.TileDimensions;

        /// <summary>The data, typed.</summary>
        public CompressedImageData CompressedData => (CompressedImageData)myData;

        internal CompressedImageHDU(Header compressedHeader, Data data)
            : base(TranslateHeader(compressedHeader), data)
        {
            CompressedHeader = compressedHeader;
        }

        /// <summary>Does this header describe a tile-compressed image?</summary>
        public new static bool IsHeader(Header hdr)
            => TileCompressionParameters.IsCompressedImageHeader(hdr);

        /// <summary>Create the data object for a tile-compressed image header.</summary>
        public new static Data ManufactureData(Header hdr) => new CompressedImageData(hdr);

        /// <summary>Build the IMAGE header the compressed one stands for: the image's
        /// own structural keywords recovered from their <c>Z</c>-prefixed counterparts,
        /// followed by every card that is genuinely about the image rather than about
        /// the table it was packed into.</summary>
        internal static Header TranslateHeader(Header compressed)
        {
            var p = TileCompressionParameters.Parse(compressed);
            var image = new Header();

            // The structural cards first, in the order the standard requires and
            // Header.CheckBeginning enforces.
            image.AddValue("XTENSION", "IMAGE", "Image extension");
            image.AddValue("BITPIX", p.BitPix, "Bits per data value");
            image.AddValue("NAXIS", p.Dims.Length, "Number of data axes");
            for (int i = 0; i < p.Dims.Length; i++)
            {
                image.AddValue($"NAXIS{i + 1}", p.Dims[i], null);
            }
            image.AddValue("PCOUNT", compressed.GetIntValue("ZPCOUNT", 0), "No extra parameters");
            image.AddValue("GCOUNT", compressed.GetIntValue("ZGCOUNT", 1), "One group");

            // Then everything else, in the order it was written, so a header keeps
            // reading the way its author laid it out.
            var cursor = compressed.GetCursor();
            while (cursor.MoveNext())
            {
                if (!(((DictionaryEntry)cursor.Current).Value is HeaderCard card))
                {
                    continue;
                }

                string key = card.Key;

                // COMMENT / HISTORY and other keyless cards have no structural meaning
                // and belong to the image just as much as to the table.
                if (!card.KeyValuePair)
                {
                    image.AddLine(card);
                    continue;
                }

                string translated = TranslateKey(key);
                if (translated == null)
                {
                    continue;
                }

                if (translated == key)
                {
                    image.AddLine(card);
                }
                else
                {
                    // Re-key by splicing the new keyword into the card's own 80
                    // character image, so the value keeps its exact formatting --
                    // quoted or not, padded as written -- rather than being rebuilt
                    // from a string and guessing at its type.
                    string written = card.ToString();
                    image.AddLine(new HeaderCard(translated.PadRight(8) + written.Substring(8)));
                }
            }

            return image;
        }

        /// <summary>Decide what becomes of one card of the compressed header: kept
        /// under its own key, kept under the key it stands for, or dropped.</summary>
        /// <returns>The key to write it under, or null to drop the card.</returns>
        private static string TranslateKey(string key)
        {
            switch (key)
            {
                // Structural cards, already emitted above from their Z counterparts.
                case "XTENSION":
                case "BITPIX":
                case "NAXIS":
                case "PCOUNT":
                case "GCOUNT":
                case "TFIELDS":
                case "THEAP":
                case "ZIMAGE":
                case "ZBITPIX":
                case "ZNAXIS":
                case "ZPCOUNT":
                case "ZGCOUNT":
                case "ZTHEAP":

                // How the image was compressed: consumed by the decoder, and untrue of
                // the decompressed image.
                case "ZCMPTYPE":
                case "ZQUANTIZ":
                case "ZDITHER0":
                case "ZMASKCMP":
                case "ZBLANK":
                case "ZSCALE":
                case "ZZERO":

                // Structure of the ORIGINAL file rather than of this image: which slot
                // it occupied and whether that file allowed extensions.
                case "ZSIMPLE":
                case "ZTENSION":
                case "ZEXTEND":
                case "ZBLOCKED":

                // Checksums of the compressed HDU, meaningless for the image; the
                // image's own travel as ZHECKSUM / ZDATASUM below.
                case "CHECKSUM":
                case "DATASUM":
                    return null;

                case "ZHECKSUM":
                    return "CHECKSUM";
                case "ZDATASUM":
                    return "DATASUM";
            }

            // The indexed families: NAXISn, ZNAXISn, ZTILEn, the ZNAMEn/ZVALn settings,
            // and the binary table's per-column keywords.
            if (HasIndexedPrefix(key, "NAXIS") || HasIndexedPrefix(key, "ZNAXIS")
                || HasIndexedPrefix(key, "ZTILE") || HasIndexedPrefix(key, "ZNAME")
                || HasIndexedPrefix(key, "ZVAL")
                || HasIndexedPrefix(key, "TTYPE") || HasIndexedPrefix(key, "TFORM")
                || HasIndexedPrefix(key, "TUNIT") || HasIndexedPrefix(key, "TSCAL")
                || HasIndexedPrefix(key, "TZERO") || HasIndexedPrefix(key, "TNULL")
                || HasIndexedPrefix(key, "TDIM") || HasIndexedPrefix(key, "TDISP"))
            {
                return null;
            }

            return key;
        }

        /// <summary>Is this key the given prefix followed by a column or axis number?
        /// Matching the digits matters: TDIM3 is a table keyword, but a hypothetical
        /// TDIMENSN is not.</summary>
        private static bool HasIndexedPrefix(string key, string prefix)
        {
            if (key.Length <= prefix.Length || !key.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            for (int i = prefix.Length; i < key.Length; i++)
            {
                if (key[i] < '0' || key[i] > '9')
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Print out some information about this HDU.</summary>
        public override void Info()
        {
            Console.Out.WriteLine($"  Tile-compressed image ({CompressionType})");
            base.Info();
        }
    }
}
