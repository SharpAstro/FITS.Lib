using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using nom.tam.fits.compression;

namespace nom.tam.fits
{
    /// <summary>
    /// Reading ONE tile of a tile-compressed image: every tile read alone must equal the
    /// same rectangle of the whole-image decode, bit for bit (so NaN payloads and -0
    /// compare exactly, not merely as numbers).
    /// </summary>
    [TestFixture]
    public class TileReadTest
    {
        private static string FixtureDir =>
            Path.Combine(TestContext.CurrentContext.TestDirectory, "testdocs", "tilecompress");

        /// <summary>Every .fz fixture the library decodes today (HCOMPRESS is not yet).</summary>
        private static IEnumerable<string> Decodable() =>
            Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "testdocs", "tilecompress"), "*.fz")
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .Where(n => !n.StartsWith("hcompress"))
                .OrderBy(n => n, StringComparer.Ordinal);

        [Test, TestCaseSource(nameof(Decodable))]
        public void EveryTileReadAloneEqualsItsRectangleOfTheWholeImage(string name)
        {
            var data = Open(name);
            var image = (Array)data.DataArray;          // whole-image decode
            Assert.That(data.TileCount, Is.GreaterThan(0));

            var covered = 0L;
            for (int n = 0; n < data.TileCount; n++)
            {
                var rect = data.GetTileRectangle(n);
                var bits = ReadTileBits(data, n, rect.PixelCount);
                var expected = RectangleBits(image, rect.Start, rect.Size);
                Assert.That(bits, Is.EqualTo(expected), $"{name}: tile {n}");
                covered += rect.PixelCount;
            }

            long total = 1;
            foreach (int d in ReadFitsAxes(data))
            {
                total *= d;
            }
            Assert.That(covered, Is.EqualTo(total), $"{name}: tiles must cover the image exactly once");
        }

        [Test]
        public void ReadingATileBeforeTheWholeImageGivesTheSameAnswer()
        {
            // The single-tile read must not depend on (or disturb) the whole-image decode.
            var first = Open("rice_f32_dither1_partial");
            var viaTile = ReadTileBits(first, 5, first.GetTileRectangle(5).PixelCount);
            Assert.That(first.DataArray, Is.Not.Null);

            var second = Open("rice_f32_dither1_partial");
            var rect = second.GetTileRectangle(5);
            Assert.That(viaTile, Is.EqualTo(RectangleBits((Array)second.DataArray, rect.Start, rect.Size)));
        }

        [Test]
        public void FixturesCoverPartialTilesOnBothEdgesAndBothDithers()
        {
            bool right = false, bottom = false, dither1 = false, dither2 = false;
            foreach (string name in Decodable())
            {
                var data = Open(name);
                var axes = ReadFitsAxes(data);
                var tile = data.TileDimensions;
                if (axes.Length >= 2)
                {
                    right |= axes[0] % tile[0] != 0;
                    bottom |= axes[1] % tile[1] != 0;
                }
                if (data.Quantization == QuantizationMethod.SubtractiveDither1)
                {
                    dither1 = true;
                }
                if (data.Quantization == QuantizationMethod.SubtractiveDither2)
                {
                    dither2 = true;
                }
            }
            Assert.That(right, Is.True, "a fixture with a partial tile on the right edge");
            Assert.That(bottom, Is.True, "a fixture with a partial tile on the bottom edge");
            Assert.That(dither1 && dither2, Is.True, "quantized fixtures with dither 1 and 2");

            // Partial tiles with the codecs and the dithered float, not just plain Rice.
            foreach (string name in new[]
                     {
                         "rice_f32_dither1_partial", "rice_f32_dither2_partial",
                         "gzip2_i16_partial", "plio_i16_partial",
                     })
            {
                var data = Open(name);
                var axes = ReadFitsAxes(data);
                var tile = data.TileDimensions;
                Assert.That(axes[0] % tile[0], Is.Not.Zero, name);
                Assert.That(axes[1] % tile[1], Is.Not.Zero, name);
            }
        }

        [Test]
        public void ReportsTheRectangleOfTheEdgeTiles()
        {
            var data = Open("rice_i16_partial_tiles");     // 70 x 50 pixels, 23 x 17 tiles
            Assert.That(data.TileCount, Is.EqualTo(4 * 3));

            var first = data.GetTileRectangle(0);
            Assert.That(first.Start, Is.EqualTo(new[] { 0, 0 }));
            Assert.That(first.Size, Is.EqualTo(new[] { 23, 17 }));

            var last = data.GetTileRectangle(11);
            Assert.That(last.Start, Is.EqualTo(new[] { 69, 34 }));
            Assert.That(last.Size, Is.EqualTo(new[] { 1, 16 }));
            Assert.That(last.PixelCount, Is.EqualTo(16));
        }

        [Test]
        public void RejectsATileIndexOutOfRange()
        {
            var data = Open("rice_i16_partial_tiles");
            var buffer = new short[23 * 17];
            Assert.Throws<ArgumentOutOfRangeException>(() => data.ReadTile(-1, buffer));
            Assert.Throws<ArgumentOutOfRangeException>(() => data.ReadTile(data.TileCount, buffer));
            Assert.Throws<ArgumentOutOfRangeException>(() => data.GetTileRectangle(data.TileCount));
        }

        [Test]
        public void RejectsABufferThatIsTooSmallOrOfTheWrongType()
        {
            var data = Open("rice_i16_partial_tiles");
            Assert.Throws<ArgumentException>(() => data.ReadTile(0, new short[23 * 17 - 1]));
            Assert.Throws<ArgumentException>(() => data.ReadTile(0, new float[23 * 17]));
        }

        [Test]
        public void KeepsTheUnsupportedCodecError()
        {
            var data = Open("hcompress_i16");
            var ex = Assert.Throws<FitsException>(() => data.ReadTile(0, new short[16 * 64]));
            Assert.That(ex.Message, Does.Contain("HCOMPRESS_1"));
        }

        // ------------------------------------------- deferred (file-backed) reads

        private static readonly string[] DeferredFixtures =
        {
            "rice_i16_partial_tiles", "gzip2_i16_partial", "plio_i16_partial",
            "rice_f32_dither1_partial", "rice_f32_dither2_partial", "rice_f32_nan_gzipfallback",
        };

        [Test, TestCaseSource(nameof(DeferredFixtures))]
        public void ReadTileOnADeferredFileUsesOnlyThatTilesBytes(string name)
        {
            var clean = Open(name);
            var image = (Array)clean.DataArray;
            int count = clean.TileCount;
            Assert.That(count, Is.GreaterThan(2));

            string temp = Path.Combine(Path.GetTempPath(), $"fits_tile_{Guid.NewGuid():N}.fz");
            try
            {
                foreach (int n in new[] { count / 2, count - 1 })
                {
                    File.Copy(Path.Combine(FixtureDir, name + ".fz"), temp, true);
                    CorruptEverythingButTile(temp, clean, n);

                    var fits = new Fits(temp);
                    try
                    {
                        fits.ReadHDU();
                        var data = ((CompressedImageHDU)fits.ReadHDU()).CompressedData;
                        var rect = data.GetTileRectangle(n);
                        var bits = ReadTileBits(data, n, rect.PixelCount);
                        Assert.That(bits, Is.EqualTo(RectangleBits(image, rect.Start, rect.Size)),
                            $"{name}: tile {n} from a file whose other tiles are garbage");
                    }
                    finally
                    {
                        fits.Close();
                    }
                }
            }
            finally
            {
                File.Delete(temp);
            }
        }

        /// <summary>Overwrite, in the file at <paramref name="path"/>, the heap bytes of every
        /// variable-length element and the fixed-column values of every row but
        /// <paramref name="keep"/>, leaving that row's own bytes intact.</summary>
        private static void CorruptEverythingButTile(string path, CompressedImageData data, int keep)
        {
            var h = data.CompressedHeader;
            int rowLen = h.GetIntValue("NAXIS1");
            int rows = h.GetIntValue("NAXIS2");
            int fields = h.GetIntValue("TFIELDS");
            long table = data.FileOffset;
            int theap = h.GetIntValue("THEAP", 0);
            long heap = table + (theap > 0 ? theap : (long)rowLen * rows);

            var bytes = File.ReadAllBytes(path);
            var kept = new List<(long start, long length)>();
            var garbage = new List<(long start, long length)>();

            int colOffset = 0;
            for (int c = 1; c <= fields; c++)
            {
                string tform = h.GetStringValue($"TFORM{c}").Trim();
                int i = 0;
                while (i < tform.Length && char.IsDigit(tform[i])) i++;
                int repeat = i == 0 ? 1 : int.Parse(tform.Substring(0, i));
                char type = tform[i];
                bool variable = type == 'P' || type == 'Q';
                int width = variable ? (type == 'P' ? 8 : 16) * repeat : repeat * TypeBytes(type);
                if (type == 'X') width = (repeat + 7) / 8;
                int elementBytes = variable ? TypeBytes(tform[i + 1]) : 0;

                for (int r = 0; r < rows; r++)
                {
                    long at = table + (long)r * rowLen + colOffset;
                    var list = r == keep ? kept : garbage;
                    if (variable)
                    {
                        long len, off;
                        if (type == 'P')
                        {
                            len = ReadBE(bytes, at, 4);
                            off = ReadBE(bytes, at + 4, 4);
                        }
                        else
                        {
                            len = ReadBE(bytes, at, 8);
                            off = ReadBE(bytes, at + 8, 8);
                        }
                        list.Add((heap + off, len * elementBytes));
                    }
                    else
                    {
                        list.Add((at, width));
                    }
                }
                colOffset += width;
            }

            var saved = kept.Select(k => (k.start, bytes.Skip((int)k.start).Take((int)k.length).ToArray())).ToList();
            foreach (var g in garbage)
            {
                for (long b = 0; b < g.length; b++)
                {
                    bytes[g.start + b] = 0xA5;
                }
            }
            foreach (var (start, original) in saved)
            {
                Array.Copy(original, 0, bytes, start, original.Length);
            }
            File.WriteAllBytes(path, bytes);
        }

        private static int TypeBytes(char type)
        {
            switch (type)
            {
                case 'I': return 2;
                case 'J': case 'E': return 4;
                case 'K': case 'D': case 'C': return 8;
                case 'M': return 16;
                default: return 1;
            }
        }

        private static long ReadBE(byte[] b, long at, int n)
        {
            long v = 0;
            for (int i = 0; i < n; i++) v = (v << 8) | b[at + i];
            return v;
        }

        // ---------------------------------------------------------------- helpers

        private static CompressedImageData Open(string name)
        {
            var fits = new Fits(Path.Combine(FixtureDir, name + ".fz"));
            fits.ReadHDU();                // the empty primary fpack always writes
            var hdu = (CompressedImageHDU)fits.ReadHDU();
            return hdu.CompressedData;
        }

        /// <summary>The image's axes in FITS order, NAXIS1 first.</summary>
        private static int[] ReadFitsAxes(CompressedImageData data)
        {
            var h = data.CompressedHeader;
            int n = h.GetIntValue("ZNAXIS", 0);
            return Enumerable.Range(1, n).Select(i => h.GetIntValue($"ZNAXIS{i}", 0)).ToArray();
        }

        private static ulong[] ReadTileBits(CompressedImageData data, int n, int pixels)
        {
            string type = data.CompressedHeader.GetIntValue("ZBITPIX").ToString();
            switch (type)
            {
                case "8": return Bits(Read<byte>(data, n, pixels));
                case "16": return Bits(Read<short>(data, n, pixels));
                case "32": return Bits(Read<int>(data, n, pixels));
                case "64": return Bits(Read<long>(data, n, pixels));
                case "-32": return Bits(Read<float>(data, n, pixels));
                default: return Bits(Read<double>(data, n, pixels));
            }
        }

        private static T[] Read<T>(CompressedImageData data, int n, int pixels) where T : unmanaged
        {
            var buffer = new T[pixels];
            Assert.That(data.ReadTile(n, buffer), Is.EqualTo(pixels));
            return buffer;
        }

        private static ulong[] Bits(Array values)
        {
            var bits = new ulong[values.Length];
            int i = 0;
            foreach (object v in values)
            {
                bits[i++] = BitsOf(v);
            }
            return bits;
        }

        private static ulong BitsOf(object v)
        {
            switch (v)
            {
                case float f: return (uint)BitConverter.SingleToInt32Bits(f);
                case double d: return (ulong)BitConverter.DoubleToInt64Bits(d);
                default: return unchecked((ulong)Convert.ToInt64(v));
            }
        }

        /// <summary>The raw bits of a rectangle of the decoded image, row-major with axis 1
        /// fastest. The image is a 1D array, a 2D [y, x] array, or an array of 2D planes
        /// (plane index over axes 3 and up, axis 3 fastest).</summary>
        private static ulong[] RectangleBits(Array image, int[] start, int[] size)
        {
            int nAxis = size.Length;
            int pixels = size.Aggregate(1, (a, b) => a * b);
            var bits = new ulong[pixels];

            var planes = new List<Array>();
            Collect(image, nAxis <= 2 ? 0 : nAxis - 2, planes);
            // Plane counts per axis above 2, needed to turn (axis 3.. position) into an index.
            var planeDims = new int[Math.Max(0, nAxis - 2)];
            if (nAxis > 2)
            {
                // Recover the extents from the nesting: outermost array is the LAST FITS axis.
                Array a = image;
                for (int k = nAxis - 1; k >= 2; k--)
                {
                    planeDims[k - 2] = a.Length;
                    a = (Array)a.GetValue(0);
                }
            }

            var pos = new int[nAxis];
            for (int i = 0; i < pixels; i++)
            {
                int plane = 0, mult = 1;
                for (int a = 2; a < nAxis; a++)
                {
                    plane += (start[a] + pos[a]) * mult;
                    mult *= planeDims[a - 2];
                }

                int x = start[0] + pos[0];
                object v;
                if (nAxis == 1)
                {
                    v = planes[0].GetValue(x);
                }
                else
                {
                    v = planes[plane].GetValue(start[1] + pos[1], x);
                }
                bits[i] = BitsOf(v);

                for (int a = 0; a < nAxis; a++)
                {
                    if (++pos[a] < size[a])
                    {
                        break;
                    }
                    pos[a] = 0;
                }
            }
            return bits;
        }

        private static void Collect(Array a, int depth, List<Array> into)
        {
            if (depth <= 0)
            {
                into.Add(a);
                return;
            }
            for (int i = 0; i < a.Length; i++)
            {
                Collect((Array)a.GetValue(i), depth - 1, into);
            }
        }
    }
}
