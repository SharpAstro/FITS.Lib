namespace nom.tam.fits
{
    using System;
    using System.Collections.Generic;
    using nom.tam.fits.compression;
    using nom.tam.image;
    using nom.tam.util;

    /// <summary>
    /// The data of a tile-compressed image: on disk a binary table of compressed
    /// tiles, in memory the ordinary multi-dimensional array an <see cref="ImageData"/>
    /// always is.
    /// <para>
    /// Decompression is deferred exactly the way an uncompressed image's read is: the
    /// table is skipped over when the HDU is read and the tiles are only decoded, and
    /// the image only allocated, when <see cref="DataArray"/> is first asked for. A
    /// header-only scan therefore costs nothing here either.
    /// </para>
    /// <para>
    /// The on-disk table is read through an ordinary <see cref="BinaryTable"/>, so the
    /// variable-length columns and the heap that hold the tiles need no special
    /// handling; this class is only concerned with turning the tiles back into pixels.
    /// </para>
    /// </summary>
    public class CompressedImageData : ImageData
    {
        /// <summary>The header as it appears on disk, describing the binary table of
        /// compressed tiles rather than the image. <see cref="CompressedImageHDU"/>
        /// presents the translated image header instead.</summary>
        public Header CompressedHeader { get; }

        /// <summary>The algorithm the tiles were compressed with.</summary>
        public TileCompressionType CompressionType => _p.CompressionType;

        /// <summary>How a floating-point image was quantized onto integers, if at all.</summary>
        public QuantizationMethod Quantization => _p.Quantization;

        /// <summary>The tile dimensions, in FITS axis order.</summary>
        public int[] TileDimensions => (int[])_p.TileDims.Clone();

        private readonly TileCompressionParameters _p;
        private readonly BinaryTable _table;

        // Column numbers within the table, or -1 when the column is absent.
        private int _colCompressed = -1;
        private int _colGzip = -1;
        private int _colUncompressed = -1;
        private int _colZScale = -1;
        private int _colZZero = -1;
        private int _colNullMask = -1;

        // ZSCALE/ZZERO as keywords rather than columns: one linear scaling for the
        // whole image, which the convention allows and some writers use.
        private readonly bool _constantScaling;
        private readonly double _constZScale;
        private readonly double _constZZero;

        // Whether Read has run. A header-only scan never calls it, and asking the
        // table for data it was never pointed at would throw rather than answer the
        // "no pixels were read" that an uncompressed image reports as a null array.
        private bool _readable;

        // Per-tile scratch, allocated once and reused. Exactly one of these is used
        // for a given image, chosen by BITPIX.
        private int[] _ints;
        private long[] _longs;
        private double[] _values;

        internal CompressedImageData(Header hdr) : base()
        {
            // The parameterless base constructor stands for an empty image; undo its
            // zero-length array so DataArray still means "not yet decompressed".
            dataArray = null;

            CompressedHeader = hdr;
            _p = TileCompressionParameters.Parse(hdr);
            _table = new BinaryTable(hdr);

            ResolveColumns(hdr);

            _constZScale = hdr.GetDoubleValue("ZSCALE", double.NaN);
            _constZZero = hdr.GetDoubleValue("ZZERO", double.NaN);
            _constantScaling = hdr.ContainsKey("ZSCALE") && hdr.ContainsKey("ZZERO");

            // Quantization only applies to a floating-point image, and only when the
            // scaling is actually recorded; without it the tiles hold the values
            // themselves and the round trip is lossless.
            _p.IsQuantizedFloat = _p.BitPix < 0
                                  && (_colZScale >= 0 || _constantScaling);

            dataDescription = new ArrayDesc(this, ReversedDims(_p.Dims), BaseType(_p.BitPix));
            byteSize = _p.PixelCount * (Math.Abs(_p.BitPix) / 8);
            if (_p.Dims.Length == 0)
            {
                byteSize = 0;
            }
        }

        private void ResolveColumns(Header hdr)
        {
            int fields = hdr.GetIntValue("TFIELDS", 0);
            for (int i = 1; i <= fields; i++)
            {
                string name = hdr.GetStringValue($"TTYPE{i}");
                if (name == null)
                {
                    continue;
                }

                switch (name.Trim().ToUpperInvariant())
                {
                    case "COMPRESSED_DATA": _colCompressed = i - 1; break;
                    case "GZIP_COMPRESSED_DATA": _colGzip = i - 1; break;
                    case "UNCOMPRESSED_DATA": _colUncompressed = i - 1; break;
                    case "ZSCALE": _colZScale = i - 1; break;
                    case "ZZERO": _colZZero = i - 1; break;
                    case "NULL_PIXEL_MASK": _colNullMask = i - 1; break;
                }
            }
        }

        private static int[] ReversedDims(int[] fitsDims)
        {
            var dims = new int[fitsDims.Length];
            for (int i = 0; i < fitsDims.Length; i++)
            {
                dims[fitsDims.Length - i - 1] = fitsDims[i];
            }
            return dims;
        }

        private static Type BaseType(int bitpix)
        {
            switch (bitpix)
            {
                case 8: return typeof(byte);
                case 16: return typeof(short);
                case 32: return typeof(int);
                case 64: return typeof(long);
                case -32: return typeof(float);
                case -64: return typeof(double);
                default: throw new FitsException($"Invalid ZBITPIX: {bitpix}");
            }
        }

        /// <summary>The decompressed image. The tiles are decoded on the first call and
        /// the result kept, so this is cheap thereafter.</summary>
        public override Object DataArray
        {
            get
            {
                if (dataArray == null && _readable && _p.Dims.Length > 0)
                {
                    dataArray = Decompress();

                    // Give the image the same tiler an uncompressed one gets, so
                    // sub-image extraction works here too. It reads from memory,
                    // the image now being decompressed in full.
                    tiler = new ImageDataTiler(this, null, 0, dataDescription);
                }

                return dataArray;
            }
        }

        /// <summary>The tiler for this image. Asking for it decompresses the image, since
        /// a tile of it cannot be produced without the pixels; an uncompressed image can
        /// seek to the region instead, which is the one thing tiling a compressed image
        /// cannot do.</summary>
        public override ImageTiler Tiler
        {
            get
            {
                Object generatedAux = DataArray;
                return base.Tiler;
            }
        }

        /// <summary>Read the table of compressed tiles. On a seekable stream this only
        /// notes where the data are and skips them, matching how an uncompressed image
        /// defers its own read.</summary>
        public override void Read(ArrayDataIO i)
        {
            SetFileOffset(i);
            _table.Read(i);
            _readable = true;
        }

        /// <summary>Write the image out UNCOMPRESSED, as the plain image extension this
        /// HDU presents itself as. Re-compressing on write is not supported, so a read
        /// followed by a write is a funpack rather than a round trip.</summary>
        public override void Write(ArrayDataIO o)
        {
            if (dataArray == null)
            {
                // Materialise before delegating: the base class would otherwise take
                // a null array to mean "write an empty image of the right shape" and
                // silently emit zeros.
                Object generatedAux = DataArray;
            }

            base.Write(o);
        }

        private Array Decompress()
        {
            if (_p.CompressionType == TileCompressionType.HCompress)
            {
                throw new FitsException(
                    "HCOMPRESS_1 tile compression is not supported yet; "
                    + "unpack the file with funpack, or use RICE_1, GZIP_1, GZIP_2, PLIO_1 or NOCOMPRESS");
            }

            if (_p.CompressionType == TileCompressionType.Unknown)
            {
                throw new FitsException(
                    $"Tile compression algorithm ZCMPTYPE = '{_p.CompressionName}' is not supported");
            }

            if (_p.IsQuantizedFloat && _p.Quantization == QuantizationMethod.Unknown)
            {
                throw new FitsException(
                    $"Quantization method ZQUANTIZ = '{_p.QuantizationName}' is not supported");
            }

            if (_colNullMask >= 0)
            {
                throw new FitsException(
                    "Tile-compressed images with a NULL_PIXEL_MASK column are not supported yet; "
                    + "the undefined pixels could not be reported correctly");
            }

            if (_colCompressed < 0 && _colGzip < 0 && _colUncompressed < 0)
            {
                throw new FitsException(
                    "Compressed image has no COMPRESSED_DATA, GZIP_COMPRESSED_DATA or UNCOMPRESSED_DATA column");
            }

            if (_table.NRows < _p.TileCount)
            {
                throw new FitsException(
                    $"Compressed image declares {_p.TileCount} tiles but the table has {_table.NRows} rows");
            }

            // Touch the table so the rows and heap are in memory before the tile loop.
            Object generatedAux = _table.DataArray;

            int nAxis = _p.Dims.Length;
            Array image = ArrayFuncs.NewRectangularInstance(dataDescription.type, dataDescription.dims);

            var planes = new List<Array>();
            CollectPlanes(image, nAxis <= 2 ? 0 : nAxis - 2, planes);

            AllocateTileBuffers();

            var tileDims = new int[nAxis];
            var tileStart = new int[nAxis];
            var index = new int[nAxis];

            for (int t = 0; t < _p.TileCount; t++)
            {
                int pixels = _p.TileGeometry(t, tileDims, tileStart);
                if (pixels <= 0)
                {
                    continue;
                }

                DecodeTile(t, pixels);

                // Copy the tile out one axis-1 run at a time: contiguous in both the
                // tile and the image, so the general N-dimensional case costs no more
                // than a row loop.
                Array.Clear(index, 0, index.Length);
                int rowLength = tileDims[0];
                int rows = pixels / rowLength;

                for (int r = 0; r < rows; r++)
                {
                    int plane = 0;
                    int mult = 1;
                    for (int a = 2; a < nAxis; a++)
                    {
                        plane += (tileStart[a] + index[a]) * mult;
                        mult *= _p.Dims[a];
                    }

                    int y = nAxis >= 2 ? tileStart[1] + index[1] : 0;
                    StoreRow(planes[plane], y, tileStart[0], rowLength, r * rowLength, nAxis);

                    for (int a = 1; a < nAxis; a++)
                    {
                        if (++index[a] < tileDims[a])
                        {
                            break;
                        }
                        index[a] = 0;
                    }
                }
            }

            _ints = null;
            _longs = null;
            _values = null;

            return image;
        }

        private void AllocateTileBuffers()
        {
            int n = _p.MaxTilePixels;
            if (_p.BitPix < 0)
            {
                _values = new double[n];
                _ints = new int[n];      // the quantized integers the codec produces
            }
            else if (_p.BitPix == 64)
            {
                _longs = new long[n];
            }
            else
            {
                _ints = new int[n];
            }
        }

        private static void CollectPlanes(Array a, int depth, List<Array> into)
        {
            if (depth <= 0)
            {
                into.Add(a);
                return;
            }

            for (int i = 0; i < a.Length; i++)
            {
                CollectPlanes((Array)a.GetValue(i), depth - 1, into);
            }
        }

        /// <summary>Decode tile <paramref name="tileIndex"/> into the scratch buffers.</summary>
        private void DecodeTile(int tileIndex, int pixels)
        {
            // A tile whose COMPRESSED_DATA is empty was not compressible: its values
            // were gzipped verbatim into the fallback column instead. That is a normal
            // outcome for a smooth tile, not an error.
            byte[] gzipped = _colGzip >= 0 ? GetTileBytes(tileIndex, _colGzip) : null;
            if (gzipped != null && gzipped.Length > 0)
            {
                int elementSize = Math.Abs(_p.BitPix) / 8;
                var raw = GzipCodec.Gunzip(gzipped, 0, gzipped.Length, pixels * elementSize);
                ReadRawValues(raw, _p.BitPix, pixels);
                return;
            }

            if (_colUncompressed >= 0)
            {
                Object stored = _table.GetElement(tileIndex, _colUncompressed);
                if (stored is Array storedArray && storedArray.Length > 0)
                {
                    ReadStoredValues(storedArray, pixels);
                    return;
                }
            }

            if (_colCompressed < 0)
            {
                throw new FitsException($"Tile {tileIndex} has no compressed data and no fallback");
            }

            // PLIO carries its line list as 16-bit words rather than bytes.
            if (_p.CompressionType == TileCompressionType.Plio)
            {
                int[] list = GetTileWords(tileIndex, _colCompressed);
                PlioCodec.Decompress(list, _ints, pixels);
                ApplyQuantization(tileIndex, pixels);
                return;
            }

            byte[] tile = GetTileBytes(tileIndex, _colCompressed);
            if (tile == null || tile.Length == 0)
            {
                throw new FitsException(
                    $"Tile {tileIndex} is empty and the file carries no uncompressed fallback for it");
            }

            // The width of one value in the compressed tile: four bytes for the
            // integers a quantized float image was turned into, the image's own width
            // otherwise.
            int tileBitPix = _p.IsQuantizedFloat ? 32 : _p.BitPix;
            int tileElementSize = _p.IsQuantizedFloat ? 4 : Math.Abs(_p.BitPix) / 8;

            switch (_p.CompressionType)
            {
                case TileCompressionType.Rice:
                    RiceCodec.Decompress(tile, 0, tile.Length, _ints, pixels, _p.BlockSize, _p.BytePix);
                    break;

                case TileCompressionType.Gzip1:
                {
                    var raw = GzipCodec.Gunzip(tile, 0, tile.Length, pixels * tileElementSize);
                    ReadRawValues(raw, tileBitPix, pixels);
                    break;
                }

                case TileCompressionType.Gzip2:
                {
                    var raw = GzipCodec.Gunzip(tile, 0, tile.Length, pixels * tileElementSize);
                    raw = GzipCodec.Unshuffle(raw, tileElementSize);
                    ReadRawValues(raw, tileBitPix, pixels);
                    break;
                }

                case TileCompressionType.None:
                    ReadRawValues(tile, tileBitPix, pixels);
                    break;

                default:
                    throw new FitsException(
                        $"Tile compression algorithm '{_p.CompressionName}' is not supported");
            }

            ApplyQuantization(tileIndex, pixels);
        }

        /// <summary>Turn the codec's integers into the values they stand for: sign
        /// interpretation for an integer image, dequantization for a float one.</summary>
        private void ApplyQuantization(int tileIndex, int pixels)
        {
            if (_p.BitPix < 0)
            {
                if (!_p.IsQuantizedFloat)
                {
                    // Lossless float tiles arrive as values already, via ReadRawValues.
                    return;
                }

                double scale = _constantScaling ? _constZScale : GetTileDouble(tileIndex, _colZScale, 1.0);
                double zero = _constantScaling ? _constZZero : GetTileDouble(tileIndex, _colZZero, 0.0);

                Quantizer.Unquantize(tileIndex, _ints, pixels, scale, zero,
                                     _p.Quantization, _p.DitherSeed,
                                     _p.HasBlank, _p.Blank, _values);
                return;
            }

            // Integer images: the codec produced the unsigned bit pattern, so give it
            // the sign the image's own width implies.
            switch (_p.BitPix)
            {
                case 16:
                    for (int i = 0; i < pixels; i++) _ints[i] = (short)_ints[i];
                    break;
                case 8:
                    for (int i = 0; i < pixels; i++) _ints[i] = (byte)_ints[i];
                    break;
            }
        }

        private byte[] GetTileBytes(int tileIndex, int column)
        {
            Object element = _table.GetElement(tileIndex, column);
            switch (element)
            {
                case null: return null;
                case byte[] b: return b;
                case sbyte[] sb:
                {
                    var b = new byte[sb.Length];
                    Buffer.BlockCopy(sb, 0, b, 0, sb.Length);
                    return b;
                }
                default:
                    throw new FitsException(
                        $"Compressed tile column holds {element.GetType().Name}, expected a byte array");
            }
        }

        private int[] GetTileWords(int tileIndex, int column)
        {
            Object element = _table.GetElement(tileIndex, column);
            switch (element)
            {
                case short[] s:
                {
                    var words = new int[s.Length];
                    for (int i = 0; i < s.Length; i++)
                    {
                        words[i] = s[i] & 0xFFFF;
                    }
                    return words;
                }
                case int[] ints:
                    return ints;
                case byte[] bytes:
                {
                    // A PLIO list written as bytes: the words are big-endian pairs.
                    var words = new int[bytes.Length / 2];
                    for (int i = 0; i < words.Length; i++)
                    {
                        words[i] = (bytes[2 * i] << 8) | bytes[2 * i + 1];
                    }
                    return words;
                }
                default:
                    throw new FitsException(
                        $"PLIO tile column holds {(element == null ? "nothing" : element.GetType().Name)}, "
                        + "expected 16-bit words");
            }
        }

        private double GetTileDouble(int tileIndex, int column, double fallback)
        {
            if (column < 0)
            {
                return fallback;
            }

            Object element = _table.GetElement(tileIndex, column);
            switch (element)
            {
                case double d: return d;
                case double[] da when da.Length > 0: return da[0];
                case float f: return f;
                case float[] fa when fa.Length > 0: return fa[0];
                default: return fallback;
            }
        }

        /// <summary>Read a tile's values out of a big-endian byte buffer, which is how
        /// both the gzip fallback and NOCOMPRESS store them.</summary>
        private unsafe void ReadRawValues(byte[] raw, int bitpix, int pixels)
        {
            switch (bitpix)
            {
                case 8:
                    for (int i = 0; i < pixels; i++)
                    {
                        _ints[i] = raw[i];
                    }
                    break;

                case 16:
                    for (int i = 0, o = 0; i < pixels; i++, o += 2)
                    {
                        _ints[i] = (short)((raw[o] << 8) | raw[o + 1]);
                    }
                    break;

                case 32:
                    for (int i = 0, o = 0; i < pixels; i++, o += 4)
                    {
                        _ints[i] = (raw[o] << 24) | (raw[o + 1] << 16) | (raw[o + 2] << 8) | raw[o + 3];
                    }
                    break;

                case 64:
                    for (int i = 0, o = 0; i < pixels; i++, o += 8)
                    {
                        _longs[i] = ReadLong(raw, o);
                    }
                    break;

                case -32:
                    for (int i = 0, o = 0; i < pixels; i++, o += 4)
                    {
                        int bits = (raw[o] << 24) | (raw[o + 1] << 16) | (raw[o + 2] << 8) | raw[o + 3];
                        unsafe
                        {
                            // Reinterpret rather than round-trip through a byte[]:
                            // netstandard2.0 has no Int32BitsToSingle, and allocating
                            // two arrays per pixel is not an option on the read path.
                            _values[i] = *(float*)&bits;
                        }
                    }
                    break;

                case -64:
                    for (int i = 0, o = 0; i < pixels; i++, o += 8)
                    {
                        _values[i] = BitConverter.Int64BitsToDouble(ReadLong(raw, o));
                    }
                    break;

                default:
                    throw new FitsException($"Cannot read raw tile values for BITPIX {bitpix}");
            }
        }

        private static long ReadLong(byte[] raw, int o)
        {
            return ((long)raw[o] << 56) | ((long)raw[o + 1] << 48) | ((long)raw[o + 2] << 40)
                   | ((long)raw[o + 3] << 32) | ((long)raw[o + 4] << 24) | ((long)raw[o + 5] << 16)
                   | ((long)raw[o + 6] << 8) | raw[o + 7];
        }

        /// <summary>Read a tile from an UNCOMPRESSED_DATA column, whose elements are
        /// already typed values rather than bytes.</summary>
        private void ReadStoredValues(Array stored, int pixels)
        {
            switch (stored)
            {
                case float[] f:
                    for (int i = 0; i < pixels; i++) _values[i] = f[i];
                    break;
                case double[] d:
                    for (int i = 0; i < pixels; i++) _values[i] = d[i];
                    break;
                case short[] s:
                    for (int i = 0; i < pixels; i++) _ints[i] = s[i];
                    break;
                case int[] n:
                    for (int i = 0; i < pixels; i++) _ints[i] = n[i];
                    break;
                case byte[] b:
                    for (int i = 0; i < pixels; i++) _ints[i] = b[i];
                    break;
                case long[] l:
                    for (int i = 0; i < pixels; i++) _longs[i] = l[i];
                    break;
                default:
                    throw new FitsException(
                        $"UNCOMPRESSED_DATA column holds {stored.GetType().Name}, which is not a pixel type");
            }
        }

        private void StoreRow(Array plane, int y, int x0, int length, int srcOffset, int nAxis)
        {
            switch (plane)
            {
                case byte[,] p:
                    for (int k = 0; k < length; k++) p[y, x0 + k] = (byte)_ints[srcOffset + k];
                    break;
                case short[,] p:
                    for (int k = 0; k < length; k++) p[y, x0 + k] = (short)_ints[srcOffset + k];
                    break;
                case int[,] p:
                    for (int k = 0; k < length; k++) p[y, x0 + k] = _ints[srcOffset + k];
                    break;
                case long[,] p:
                    for (int k = 0; k < length; k++) p[y, x0 + k] = _longs[srcOffset + k];
                    break;
                case float[,] p:
                    for (int k = 0; k < length; k++) p[y, x0 + k] = (float)_values[srcOffset + k];
                    break;
                case double[,] p:
                    for (int k = 0; k < length; k++) p[y, x0 + k] = _values[srcOffset + k];
                    break;

                // A one-dimensional image has a single row and no y.
                case byte[] p:
                    for (int k = 0; k < length; k++) p[x0 + k] = (byte)_ints[srcOffset + k];
                    break;
                case short[] p:
                    for (int k = 0; k < length; k++) p[x0 + k] = (short)_ints[srcOffset + k];
                    break;
                case int[] p:
                    for (int k = 0; k < length; k++) p[x0 + k] = _ints[srcOffset + k];
                    break;
                case long[] p:
                    for (int k = 0; k < length; k++) p[x0 + k] = _longs[srcOffset + k];
                    break;
                case float[] p:
                    for (int k = 0; k < length; k++) p[x0 + k] = (float)_values[srcOffset + k];
                    break;
                case double[] p:
                    for (int k = 0; k < length; k++) p[x0 + k] = _values[srcOffset + k];
                    break;

                default:
                    throw new FitsException(
                        $"Cannot store decompressed pixels into a {plane.GetType().Name}");
            }
        }
    }
}
