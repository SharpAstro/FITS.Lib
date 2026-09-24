using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace nom.tam.fits
{
    /// <summary>
    /// FitsWriter must write the SAME bytes as <see cref="Fits.Write(System.IO.Stream)"/> for the same
    /// image and cards: that is what lets a caller switch writers without a single file changing. The
    /// old path is the reference in every test here.
    /// </summary>
    [TestFixture]
    public class FitsWriterTest
    {
        private const int Width = 37, Height = 23;

        [Test]
        public void EveryBitpixWritesTheSameBytesAsTheOldPath()
        {
            AssertSameBytes(Fill<byte>((y, x) => (byte)((y * 7) + x)), 8);
            AssertSameBytes(Fill<short>((y, x) => (short)((y * 1301) - (x * 977))), 16);
            AssertSameBytes(Fill<int>((y, x) => (y * 1_000_003) - (x * 77_777)), 32);
            AssertSameBytes(Fill<long>((y, x) => ((long)y << 40) - (x * 123_456_789L)), 64);
            AssertSameBytes(Fill<float>((y, x) => (y * 0.37f) - (x * 11.5f)), -32);
            AssertSameBytes(Fill<double>((y, x) => (y * 1e-7) - (x * 3.25)), -64);
        }

        [Test]
        public void ACubeOfPlanesWritesTheSameBytesAsTheOldPath()
        {
            var planes = new float[3][,];
            for (var c = 0; c < planes.Length; c++)
            {
                var channel = c;
                planes[c] = Fill<float>((y, x) => (channel * 1000f) + (y * Width) + x);
            }

            var reference = WriteOldPath(planes);

            var header = FitsWriter.ImageHeader(-32, Width, Height, planes.Length);
            AddCards(header);
            var written = WriteNewPath(header, writer =>
            {
                foreach (var plane in planes)
                {
                    writer.Write<float>(Flat(plane));
                }
            });

            Assert.That(written, Is.EqualTo(reference));
        }

        [Test]
        public void WritingInPlaceWritesTheSameBytesAndSwapsTheCallersSamples()
        {
            var image = Fill<short>((y, x) => (short)((y * 300) + x));
            var reference = WriteOldPath(image);

            var scratch = (short[,])image.Clone();
            var header = FitsWriter.ImageHeader(16, Width, Height);
            AddCards(header);
            var written = WriteNewPath(header, writer => writer.WriteInPlace(MemoryMarshal.CreateSpan(ref scratch[0, 0], scratch.Length)));

            Assert.That(written, Is.EqualTo(reference));
            if (BitConverter.IsLittleEndian)
            {
                Assert.That(scratch[1, 1], Is.EqualTo(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(image[1, 1])),
                    "the caller's span now holds big-endian samples");
            }
        }

        [Test]
        public void AnImageLargerThanOneBandWritesTheSameBytesInAnyNumberOfWrites()
        {
            // 1500 x 1000 floats is 6 MB, three bands of 2 MB. Written in one call, then row by row: the
            // band boundaries and the caller's boundaries must both be invisible in the output.
            const int width = 1500, height = 1000;
            var image = new float[height, width];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    image[y, x] = (y * 0.5f) + (x * 0.25f);
                }
            }

            var reference = WriteOldPath(image);

            var header = FitsWriter.ImageHeader(-32, width, height);
            AddCards(header);
            Assert.That(WriteNewPath(header, writer => writer.Write<float>(Flat(image))), Is.EqualTo(reference), "one call");

            header = FitsWriter.ImageHeader(-32, width, height);
            AddCards(header);
            Assert.That(WriteNewPath(header, writer =>
            {
                for (var y = 0; y < height; y++)
                {
                    writer.Write<float>(MemoryMarshal.CreateReadOnlySpan(ref image[y, 0], width));
                }
            }), Is.EqualTo(reference), "row by row");
        }

        [Test]
        public void AWriteOnlyGzipStreamIsWrittenStraightThrough()
        {
            // The old path wraps any stream in a BufferedDataStream, which builds a BinaryReader over
            // it, and a compressing GZipStream cannot be read: it throws before a byte is written.
            var image = Fill<short>((y, x) => (short)((y * 11) + x));
            var reference = WriteOldPath(image);

            var compressed = new MemoryStream();
            using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            using (var writer = new FitsWriter(gzip))
            {
                var header = FitsWriter.ImageHeader(16, Width, Height);
                AddCards(header);
                writer.WriteHeader(header);
                writer.Write<short>(Flat(image));
                writer.Finish();
            }

            compressed.Position = 0;
            using var inflated = new MemoryStream();
            using (var gunzip = new GZipStream(compressed, CompressionMode.Decompress))
            {
                gunzip.CopyTo(inflated);
            }

            Assert.That(inflated.ToArray(), Is.EqualTo(reference));
        }

        [Test]
        public void CreateFileWritesAPreallocatedFileOfExactlyTheRightLength()
        {
            var image = Fill<float>((y, x) => y - x);
            var reference = WriteOldPath(image);
            var path = Path.Combine(Path.GetTempPath(), $"fitswriter-{Guid.NewGuid():N}.fits");
            try
            {
                var header = FitsWriter.ImageHeader(-32, Width, Height);
                AddCards(header);
                using (var writer = FitsWriter.CreateFile(path, header))
                {
                    writer.Write<float>(Flat(image));
                    writer.Finish();
                }

                var bytes = File.ReadAllBytes(path);
                Assert.That(bytes.Length % FitsWriter.BlockSize, Is.EqualTo(0), "whole 2880-byte blocks");
                Assert.That(bytes, Is.EqualTo(reference));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void TheImageHeaderIsThePrimaryHeaderTheOldPathWrites()
        {
            // Making an HDU primary drops PCOUNT and GCOUNT: the old path writes that form, so the writer's
            // header must be it too, not the factory's raw one.
            var hdu = FitsFactory.HDUFactory(new short[Height, Width]);
            new Fits().AddHDU(hdu);
            var fromWriter = FitsWriter.ImageHeader(16, Width, Height);
            Assert.That(HeaderBytes(fromWriter), Is.EqualTo(HeaderBytes(hdu.Header)));
            Assert.That(fromWriter.ContainsKey("PCOUNT"), Is.False);
            Assert.That(fromWriter.ContainsKey("EXTEND"), Is.True);
        }

        [Test]
        public void AHeaderIsPlainAsciiAndTheSameWhateverTheCulture()
        {
            // The SIMPLE comment used to carry DateTime.Now, formatted in the CURRENT culture: a header's
            // bytes then depended on the second it was built (which made every comparison in this
            // fixture fail whenever a second ticked over between its two headers) and on the machine's
            // locale, and a culture whose AM/PM designators are not ASCII, as Korean's are, put bytes
            // outside the 32 to 126 FITS allows into every header written there.
            var korean = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            korean.DateTimeFormat.AMDesignator = "오전";
            korean.DateTimeFormat.PMDesignator = "오후";
            korean.DateTimeFormat.LongTimePattern = "tt h:mm:ss";

            var invariant = HeaderBytes(FitsWriter.ImageHeader(16, Width, Height));
            var saved = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = korean;
            try
            {
                var bytes = HeaderBytes(FitsWriter.ImageHeader(16, Width, Height));
                Assert.That(Array.FindAll(bytes, b => b is < 32 or > 126), Is.Empty, "printable ASCII only");
                Assert.That(bytes, Is.EqualTo(invariant), "the culture does not reach the header");
            }
            finally
            {
                CultureInfo.CurrentCulture = saved;
            }
        }

        [Test]
        public void MisuseIsRefusedBeforeABadFileExists()
        {
            using var stream = new MemoryStream();
            using var writer = new FitsWriter(stream, leaveOpen: true);
            Assert.Throws<InvalidOperationException>(() => writer.Write<short>(new short[4]), "data before the header");

            writer.WriteHeader(FitsWriter.ImageHeader(16, 2, 2));
            Assert.Throws<InvalidOperationException>(() => writer.WriteHeader(FitsWriter.ImageHeader(16, 2, 2)), "a second header");
            Assert.Throws<ArgumentException>(() => writer.Write<float>(new float[4]), "floats for BITPIX 16");
            Assert.Throws<ArgumentException>(() => writer.Write<ushort>(new ushort[4]), "FITS has no unsigned 16-bit type");
            Assert.Throws<InvalidOperationException>(() => writer.Write<short>(new short[5]), "more data than the header declares");

            writer.Write<short>(new short[3]);
            Assert.Throws<InvalidOperationException>(() => writer.Finish(), "one sample short");
            writer.Write<short>(new short[1]);
            writer.Finish();
            Assert.That(stream.Length % FitsWriter.BlockSize, Is.EqualTo(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => FitsWriter.ImageHeader(12, 2, 2), "no such BITPIX");
            Assert.Throws<ArgumentOutOfRangeException>(() => FitsWriter.ImageHeader(16, 2, 0), "an empty axis");
        }

        private static T[,] Fill<T>(Func<int, int, T> value)
        {
            var image = new T[Height, Width];
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    image[y, x] = value(y, x);
                }
            }

            return image;
        }

        private static ReadOnlySpan<T> Flat<T>(T[,] image) => MemoryMarshal.CreateReadOnlySpan(ref image[0, 0], image.Length);

        private static void AddCards(Header header)
        {
            header.AddValue("OBJECT", "M 42", "");
            header.AddValue("EXPTIME", 120.0, "seconds");
            header.AddValue("GAIN", 100, "");
        }

        private static byte[] WriteOldPath(object image)
        {
            var fits = new Fits();
            var hdu = FitsFactory.HDUFactory(image);
            AddCards(hdu.Header);
            fits.AddHDU(hdu);
            var output = new MemoryStream();
            fits.Write(output);
            return output.ToArray();
        }

        private static byte[] WriteNewPath(Header header, Action<FitsWriter> writeData)
        {
            var output = new MemoryStream();
            using (var writer = new FitsWriter(output, leaveOpen: true))
            {
                writer.WriteHeader(header);
                writeData(writer);
                writer.Finish();
            }

            return output.ToArray();
        }

        private static void AssertSameBytes<T>(T[,] image, int bitpix) where T : unmanaged
        {
            var reference = WriteOldPath(image);
            var header = FitsWriter.ImageHeader(bitpix, Width, Height);
            AddCards(header);
            var written = WriteNewPath(header, writer => writer.Write<T>(Flat(image)));
            Assert.That(written, Is.EqualTo(reference), $"BITPIX {bitpix}");
        }

        private static byte[] HeaderBytes(Header header)
        {
            var memory = new MemoryStream();
            var output = new nom.tam.util.BufferedDataStream(memory);
            header.Write(output);
            output.Flush();
            return memory.ToArray();
        }
    }
}
