using System.IO;
using NUnit.Framework;
using System.Runtime.InteropServices;
using nom.tam.fits.IO;

namespace nom.tam.fits
{
    using System;
    using util;

    [TestFixture]
    public class FitsReaderTester
    {

        [Test]
        public void TestFits()
        {
            String file = Path.GetTempFileName();
            File.Copy("testdocs/ht1.fits", file, true);
            Fits f = new Fits(new FileStream(file, FileMode.Open), false);
            // Fits f = new Fits(new FileStream("E:\\CSharpFITSIO\\AISCHV3_228_13637_0001_sv09-fd-int.fits.gz",FileMode.Open),true);
            //Fits f = new Fits(new FileStream("E:\\CSharpFITSIO\\LAB-2.0kms.fits",FileMode.Open),false);
            // Fits f = new Fits("http://skyview.gsfc.nasa.gov/cgi-bin/images?position=180.0%2C8.0&survey=NEAT&pixels=300%2C300&sampler=Clip&size=0.3%2C0.3&projection=Tan&coordinates=J2000.0&return=FITS");
            // Fits f = new Fits(new FileStream("D:\\VOIndia\\Sample FITS files\\Previous\\swift_events.fits",FileMode.Open),false);

            //  String fits = "E:\\CSharpFITSIO\\AISCHV3_228_13637_0001_sv09-fd-int.fits.gz";
            //  Fits f = new Fits(fits);
            Console.Out.WriteLine("FitsReader called.");

            int i = 0;
            BasicHDU h;

            do
            {
                h = f.ReadHDU();
                if (h != null)
                {
                    if (i == 0)
                    {
                        Console.Out.WriteLine("\n\nPrimary header:\n");
                    }
                    else
                    {
                        Console.Out.WriteLine($"\n\nExtension {i}:\n");
                    }
                    i += 1;
                    h.Info();
                }
            } while (h != null);

        }
        [Test]
        public void TestReadBuffered()
        {
            String file = Path.GetTempFileName();
            File.Copy("testdocs/ht1.fits", file, true);
            BufferedFile bf = new BufferedFile(file, FileAccess.Read, FileShare.None);

            Header h = Header.ReadHeader(bf);
            long n = h.DataSize;
            int naxes = h.GetIntValue("NAXIS");
            int lastAxis = h.GetIntValue($"NAXIS{naxes}");
            HeaderCard hnew = new HeaderCard("NAXIS", naxes - 1, "this is header card with naxes");
            h.AddCard(hnew);
            float[] line = new float[h.DataSize];
            for (int i = 0; i < lastAxis; i += 1)
            {
                Console.Out.WriteLine("read");
                bf.Read(line);
            }




        }
    }

    /// <summary>
    /// FitsReader must read the image <see cref="Fits.ReadHDU"/> reads, with the values its typed array
    /// converts to: that is what lets a caller switch readers without a single plane changing. The old
    /// path is the reference in every test here.
    /// </summary>
    [TestFixture]
    public class FitsReaderTest
    {
        // Odd sizes on purpose: every row and every plane ends inside a vector, so the scalar tails run.
        private const int Width = 37, Height = 23;

        private string _dir = "";

        [SetUp]
        public void CreateDirectory()
        {
            _dir = Path.Combine(Path.GetTempPath(), $"fitsreader-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void DeleteDirectory() => Directory.Delete(_dir, recursive: true);

        [Test]
        public void EveryBitpixReadsWhatTheOldPathReads()
        {
            foreach (var (bzero, bscale) in new[] { (0.0, 1.0), (32768.0, 1.0), (-3.25, 0.5) })
            {
                AssertReadsLikeTheOldPath(Fill<byte>((y, x) => (byte)((y * 11) + x)), 8, bzero, bscale);
                AssertReadsLikeTheOldPath(Fill<short>((y, x) => (short)((y * 1301) - (x * 977))), 16, bzero, bscale);
                AssertReadsLikeTheOldPath(Fill<int>((y, x) => (y * 1_000_003) - (x * 77_777)), 32, bzero, bscale);
                AssertReadsLikeTheOldPath(Fill<long>((y, x) => ((long)y << 40) - (x * 123_456_789L)), 64, bzero, bscale);
                AssertReadsLikeTheOldPath(Fill<float>((y, x) => (y * 0.37f) - (x * 11.5f)), -32, bzero, bscale);
                AssertReadsLikeTheOldPath(Fill<double>((y, x) => (y * 1e-7) - (x * 3.25)), -64, bzero, bscale);
            }
        }

        [Test]
        public void ACubeReadsEveryPlaneInOrder()
        {
            var planes = new float[3][,];
            for (var c = 0; c < planes.Length; c++)
            {
                var channel = c;
                planes[c] = Fill<float>((y, x) => (channel * 1000f) + (y * Width) + x);
            }

            var path = Write(FitsWriter.ImageHeader(-32, Width, Height, planes.Length), writer =>
            {
                foreach (var plane in planes)
                {
                    writer.Write<float>(Flat(plane));
                }
            });

            Assert.That(FitsReader.TryOpen(path, out var reader), Is.True);
            using (reader)
            {
                Assert.That((reader.Width, reader.Height, reader.Planes), Is.EqualTo((Width, Height, planes.Length)));
                var plane = new float[Width * Height];
                for (var c = 0; c < planes.Length; c++)
                {
                    reader.ReadPlane(c, plane);
                    Assert.That(plane, Is.EqualTo(Flat(planes[c]).ToArray()), $"plane {c}");
                }
            }
        }

        [Test]
        public void TheImageIsFoundPastAnEmptyPrimaryAndATable()
        {
            // A table cannot be primary, so a table added first makes FITS.Lib write a placeholder primary
            // in front of it (BasicHDU.DummyHDU, the image of an empty array: NAXIS = 1, NAXIS1 = 0), then
            // the table, then the image as an IMAGE extension. The placeholder IS an image HDU, one that
            // holds no sample, so a walk that stops at the first image HDU with any axes reads nothing.
            var image = Fill<short>((y, x) => (short)((y * 300) + x));
            var fits = new Fits();
            fits.AddHDU(FitsFactory.HDUFactory(new object[] { new[] { 1, 2, 3 }, new[] { 1.5, 2.5, 3.5 } }));
            var imageHdu = FitsFactory.HDUFactory(image);
            imageHdu.Header.AddValue("BZERO", 32768.0, "");
            fits.AddHDU(imageHdu);
            var path = Path.Combine(_dir, "table-then-image.fits");
            using (var output = File.Create(path))
            {
                fits.Write(output);
            }

            using (var probe = new Fits(new BufferedFile(path, FileAccess.Read, FileShare.Read), false))
            {
                var primary = probe.ReadHDU();
                Assert.That(primary, Is.InstanceOf<ImageHDU>(), "the placeholder is an image HDU");
                Assert.That((primary.Header.GetIntValue("NAXIS"), primary.Header.GetIntValue("NAXIS1")), Is.EqualTo((1, 0)));
            }

            var old = ReadOldPath(path);
            Assert.That(old.Header.GetStringValue("XTENSION").Trim(), Is.EqualTo("IMAGE"), "the image is the extension");

            Assert.That(FitsReader.TryOpen(path, out var reader), Is.True);
            using (reader)
            {
                Assert.That(reader.Header.GetStringValue("XTENSION").Trim(), Is.EqualTo("IMAGE"));
                Assert.That(reader.DataOffset % FitsReader.BlockSize, Is.EqualTo(0));
                var plane = new float[Width * Height];
                reader.ReadPlane(0, plane);
                Assert.That(Bits(plane), Is.EqualTo(Bits(Expected(old, 0))));
            }
        }

        [Test]
        public void TheHeaderIsTheOneTheOldPathReadsAndSaysWhereItWas()
        {
            var image = Fill<short>((y, x) => (short)(y - x));
            var fits = new Fits();
            fits.AddHDU(FitsFactory.HDUFactory(new object[] { new[] { 7, 8 } }));
            var imageHdu = FitsFactory.HDUFactory(image);
            imageHdu.Header.AddValue("OBJECT", "NGC 7000", "the North America nebula");
            imageHdu.Header.AddValue("EXPTIME", 180.0, "seconds");
            fits.AddHDU(imageHdu);
            var path = Path.Combine(_dir, "header.fits");
            using (var output = File.Create(path))
            {
                fits.Write(output);
            }

            var old = ReadOldPath(path);
            Assert.That(FitsReader.TryOpen(path, out var reader), Is.True);
            using (reader)
            {
                Assert.That(reader.Hdu, Is.InstanceOf<ImageHDU>());
                Assert.That(reader.Header.NumberOfCards, Is.EqualTo(old.Header.NumberOfCards));
                for (var card = 0; card < old.Header.NumberOfCards; card++)
                {
                    Assert.That(reader.Header.GetCard(card), Is.EqualTo(old.Header.GetCard(card)), $"card {card}");
                }

                Assert.That(reader.Hdu.FileOffset, Is.EqualTo(old.FileOffset), "where the header starts in the file");
                Assert.That(reader.Hdu.FileOffset, Is.GreaterThan(0), "past the primary and the table");
            }
        }

        [Test]
        public void FloatSamplesWithNoScalingAreCopiedBitForBit()
        {
            // Multiplying by one and adding zero turns -0.0 into +0.0. The swap alone keeps every bit,
            // payload NaNs, infinities and subnormals included.
            var specials = new[]
            {
                -0.0f, float.NaN, BitConverter.Int32BitsToSingle(0x7FC00001), BitConverter.Int32BitsToSingle(unchecked((int)0xFFC12345)),
                float.PositiveInfinity, float.NegativeInfinity, float.Epsilon, -float.Epsilon, float.MaxValue, float.MinValue, 1.5f,
            };
            var image = Fill<float>((y, x) => specials[((y * Width) + x) % specials.Length]);
            var path = Write(FitsWriter.ImageHeader(-32, Width, Height), writer => writer.Write<float>(Flat(image)));

            Assert.That(FitsReader.TryOpen(path, out var reader), Is.True);
            using (reader)
            {
                var plane = new float[Width * Height];
                reader.ReadPlane(0, plane);
                Assert.That(Bits(plane), Is.EqualTo(Bits(Flat(image).ToArray())));
            }
        }

        [Test]
        public void APlaneOverSeveralBandsReadsTheSameAsTheOldPath()
        {
            // 1500 x 1000 shorts are 3 MB, a band and a half; 1024 x 1024 floats are 4 MB, exactly two.
            AssertReadsLikeTheOldPath(Fill<short>(1000, 1500, (y, x) => (short)((y * 31) ^ (x * 17))), 16, 32768.0, 1.0);
            AssertReadsLikeTheOldPath(Fill<float>(1024, 1024, (y, x) => (y * 0.5f) - x), -32, 0.0, 1.0);
        }

        [Test]
        public void ATileCompressedImageIsDeclinedRatherThanSkipped()
        {
            foreach (var name in new[] { "gzip1_i16", "gzip2_i32", "gzip1_f32", "hcompress_i16" })
            {
                var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "testdocs", "tilecompress", $"{name}.fz");
                Assert.That(FitsReader.TryOpen(path, out var reader), Is.False, name);
                Assert.That(reader, Is.Null);
            }

            // With tile compression off the old path sees a plain binary table, which is no image: the
            // walk skips it like any other table, and the file then holds none.
            var saved = FitsFactory.UseTileCompression;
            FitsFactory.UseTileCompression = false;
            try
            {
                var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "testdocs", "tilecompress", "gzip1_i16.fz");
                Assert.That(FitsReader.TryOpen(path, out _), Is.False);
            }
            finally
            {
                FitsFactory.UseTileCompression = saved;
            }
        }

        [Test]
        public void WhatItCannotReadIsDeclinedAndOnlyAMissingFileThrows()
        {
            var image = Fill<short>((y, x) => (short)(y + x));
            var whole = File.ReadAllBytes(Write(FitsWriter.ImageHeader(16, Width, Height), writer => writer.Write<short>(Flat(image))));

            AssertDeclined(Array.Empty<byte>(), "an empty file");
            AssertDeclined(new byte[FitsReader.BlockSize * 2], "zeros");
            var notFits = new byte[FitsReader.BlockSize];
            new Random(5).NextBytes(notFits);
            AssertDeclined(notFits, "not FITS");
            AssertDeclined(whole.AsSpan(0, FitsReader.BlockSize + 100).ToArray(), "data cut short");
            AssertDeclined(whole.AsSpan(0, 1000).ToArray(), "a header cut short");

            var headerOnly = new Fits();
            headerOnly.AddHDU(FitsFactory.HDUFactory(new object[] { new[] { 1 } }));
            var tableOnly = Path.Combine(_dir, "table-only.fits");
            using (var output = File.Create(tableOnly))
            {
                headerOnly.Write(output);
            }

            Assert.That(FitsReader.TryOpen(tableOnly, out _), Is.False, "no image in the file");
            Assert.Throws<FileNotFoundException>(() => FitsReader.TryOpen(Path.Combine(_dir, "missing.fits"), out _));
        }

        [Test]
        public void ReadPlaneRefusesAWrongDestinationOrPlane()
        {
            var image = Fill<short>((y, x) => (short)x);
            var path = Write(FitsWriter.ImageHeader(16, Width, Height), writer => writer.Write<short>(Flat(image)));
            Assert.That(FitsReader.TryOpen(path, out var reader), Is.True);
            using (reader)
            {
                Assert.Throws<ArgumentException>(() => reader.ReadPlane(0, new float[(Width * Height) - 1]));
                Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadPlane(1, new float[Width * Height]));
                Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadPlane(-1, new float[Width * Height]));
            }

            Assert.Throws<ObjectDisposedException>(() => reader.ReadPlane(0, new float[Width * Height]));
        }

        [Test]
        public void ReadingAPlaneAllocatesNoTypedArray()
        {
            const int width = 1500, height = 1000;
            var image = Fill<short>(height, width, (y, x) => (short)(x - y));
            var path = Write(FitsWriter.ImageHeader(16, width, height), writer => writer.Write<short>(Flat(image)));
            var plane = new float[width * height];

            Assert.That(FitsReader.TryOpen(path, out var warm), Is.True);
            using (warm)
            {
                warm.ReadPlane(0, plane);
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            Assert.That(FitsReader.TryOpen(path, out var reader), Is.True);
            using (reader)
            {
                reader.ReadPlane(0, plane);
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.LessThan(64 * 1024),
                $"an open and a plane read allocated {allocated:N0} bytes; the typed array alone is {width * height * 2:N0}");
        }

        [Test]
        public void PartialFitsReaderReadsTheSameValuesAsTheWholePlane()
        {
            foreach (var bitpix in new[] { 8, 16, 32, 64, -32, -64 })
            {
                var header = FitsWriter.ImageHeader(bitpix, Width, Height);
                header.AddValue("BZERO", 12.5, "");
                header.AddValue("BSCALE", 0.25, "");
                var path = Write(header, writer => WriteSamples(writer, bitpix));

                Assert.That(FitsReader.TryOpen(path, out var reader), Is.True);
                var plane = new float[Width * Height];
                using (reader)
                {
                    reader.ReadPlane(0, plane);
                }

                using var partial = new PartialFitsReader(path);
                var region = new float[11 * 7];
                partial.ReadRegion(5, 3, 11, 7, region);
                for (var r = 0; r < 7; r++)
                {
                    for (var c = 0; c < 11; c++)
                    {
                        Assert.That(BitConverter.SingleToInt32Bits(region[(r * 11) + c]),
                            Is.EqualTo(BitConverter.SingleToInt32Bits(plane[((3 + r) * Width) + 5 + c])), $"BITPIX {bitpix} at ({5 + c}, {3 + r})");
                    }
                }
            }
        }

        private void AssertReadsLikeTheOldPath<T>(T[,] image, int bitpix, double bzero, double bscale) where T : unmanaged
        {
            var header = FitsWriter.ImageHeader(bitpix, image.GetLength(1), image.GetLength(0));
            header.AddValue("BZERO", bzero, "");
            header.AddValue("BSCALE", bscale, "");
            var path = Write(header, writer => writer.Write<T>(Flat(image)));

            var old = ReadOldPath(path);
            Assert.That(FitsReader.TryOpen(path, out var reader), Is.True, $"BITPIX {bitpix}");
            using (reader)
            {
                Assert.That((reader.BitPix, reader.Width, reader.Height, reader.Planes), Is.EqualTo((bitpix, image.GetLength(1), image.GetLength(0), 1)));
                Assert.That((reader.BZero, reader.BScale), Is.EqualTo((old.BZero, old.BScale)));
                var plane = new float[image.Length];
                reader.ReadPlane(0, plane);
                Assert.That(Bits(plane), Is.EqualTo(Bits(Expected(old, 0))), $"BITPIX {bitpix}, BZERO {bzero}, BSCALE {bscale}");
            }
        }

        private void AssertDeclined(byte[] bytes, string what)
        {
            var path = Path.Combine(_dir, $"declined-{Guid.NewGuid():N}.fits");
            File.WriteAllBytes(path, bytes);
            Assert.That(FitsReader.TryOpen(path, out var reader), Is.False, what);
            Assert.That(reader, Is.Null, what);
        }

        /// <summary>
        /// The physical values of the old path's typed array, by the contract FitsReader states: single
        /// precision, the stored value converted first, multiplied then added, for 8, 16, 32 and -32; a
        /// float image with no scaling as its own bits; 64 and -64 in double, rounded once.
        /// </summary>
        private static float[] Expected(BasicHDU hdu, int plane)
        {
            var channel = ((ImageData)hdu.Data).GetChannel(plane);
            var zero = (float)hdu.BZero;
            var scale = (float)hdu.BScale;
            var result = new float[channel.Length];
            switch (channel)
            {
                case byte[,] a: Each(a, v => (scale * v) + zero); break;
                case short[,] a: Each(a, v => (scale * v) + zero); break;
                case int[,] a: Each(a, v => (scale * v) + zero); break;
                case float[,] a when scale == 1f && zero == 0f: Each(a, v => v); break;
                case float[,] a: Each(a, v => (scale * v) + zero); break;
                case long[,] a: Each(a, v => (float)(hdu.BZero + (hdu.BScale * v))); break;
                case double[,] a: Each(a, v => (float)(hdu.BZero + (hdu.BScale * v))); break;
                default: throw new InvalidOperationException(channel.GetType().Name);
            }

            return result;

            void Each<T>(T[,] source, Func<T, float> physical) where T : unmanaged
            {
                var flat = Flat(source);
                for (var i = 0; i < flat.Length; i++)
                {
                    result[i] = physical(flat[i]);
                }
            }
        }

        /// <summary>The first image HDU holding a sample, read by <see cref="Fits.ReadHDU"/>, its pixels
        /// loaded while the file is still open (a seekable stream defers them).</summary>
        private static BasicHDU ReadOldPath(string path)
        {
            using var fits = new Fits(new BufferedFile(path, FileAccess.Read, FileShare.Read), false);
            while (fits.ReadHDU() is { } hdu)
            {
                if (hdu is ImageHDU && hdu.Axes is { Length: > 0 } axes && Array.TrueForAll(axes, length => length > 0))
                {
                    Assert.That(((ImageData)hdu.Data).DataArray, Is.Not.Null, "the old path read the pixels");
                    return hdu;
                }
            }

            throw new InvalidDataException($"no image in {path}");
        }

        private string Write(Header header, Action<FitsWriter> writeData)
        {
            var path = Path.Combine(_dir, $"{Guid.NewGuid():N}.fits");
            using var writer = FitsWriter.CreateFile(path, header);
            writeData(writer);
            writer.Finish();
            return path;
        }

        private static void WriteSamples(FitsWriter writer, int bitpix)
        {
            switch (bitpix)
            {
                case 8: writer.Write<byte>(Flat(Fill<byte>((y, x) => (byte)((y * 9) + x)))); break;
                case 16: writer.Write<short>(Flat(Fill<short>((y, x) => (short)((y * 999) - x)))); break;
                case 32: writer.Write<int>(Flat(Fill<int>((y, x) => (y * 99_999) - x))); break;
                case 64: writer.Write<long>(Flat(Fill<long>((y, x) => ((long)y << 36) - x))); break;
                case -32: writer.Write<float>(Flat(Fill<float>((y, x) => (y * 0.1f) - x))); break;
                case -64: writer.Write<double>(Flat(Fill<double>((y, x) => (y * 0.01) - x))); break;
            }
        }

        private static T[,] Fill<T>(Func<int, int, T> value) => Fill(Height, Width, value);

        private static T[,] Fill<T>(int height, int width, Func<int, int, T> value)
        {
            var image = new T[height, width];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    image[y, x] = value(y, x);
                }
            }

            return image;
        }

        private static ReadOnlySpan<T> Flat<T>(T[,] image) => MemoryMarshal.CreateReadOnlySpan(ref image[0, 0], image.Length);

        private static int[] Bits(float[] values) => MemoryMarshal.Cast<float, int>(values.AsSpan()).ToArray();
    }
}
