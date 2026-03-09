using System;
using System.IO;
using NUnit.Framework;
using nom.tam.util;

namespace nom.tam.fits
{
    [TestFixture]
    public class FitsTest
    {
        private string _tempFile;

        [SetUp]
        public void SetUp()
        {
            _tempFile = Path.GetTempFileName();
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_tempFile))
                File.Delete(_tempFile);
        }

        private void WriteSampleFits()
        {
            var f = new Fits();
            f.AddHDU(Fits.MakeHDU(new float[,] { { 1f, 2f }, { 3f, 4f } }));
            f.AddHDU(Fits.MakeHDU(new int[,] { { 10, 20 }, { 30, 40 } }));
            using var bf = new BufferedFile(_tempFile, FileAccess.ReadWrite, FileShare.ReadWrite);
            f.Write(bf);
        }

        [Test]
        public void TestEmptyFits()
        {
            using var f = new Fits();
            Assert.That(f.NumberOfHDUs, Is.EqualTo(0));
        }

        [Test]
        public void TestVersion()
        {
            Assert.That(Fits.Version(), Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void TestReadHDUReturnsNullWhenNoStream()
        {
            using var f = new Fits();
            Assert.That(f.ReadHDU(), Is.Null);
        }

        [Test]
        public void TestReadAllHDUs()
        {
            WriteSampleFits();
            using var f = new Fits(_tempFile);
            var hdus = f.Read();
            Assert.That(hdus, Is.Not.Null);
            Assert.That(hdus.Length, Is.EqualTo(2));
        }

        [Test]
        public void TestSize()
        {
            WriteSampleFits();
            using var f = new Fits(_tempFile);
            Assert.That(f.Size(), Is.EqualTo(2));
        }

        [Test]
        public void TestCurrentSize()
        {
            WriteSampleFits();
            using var f = new Fits(_tempFile);
            Assert.That(f.CurrentSize(), Is.EqualTo(0));
            f.ReadHDU();
            Assert.That(f.CurrentSize(), Is.EqualTo(1));
        }

        [Test]
        public void TestGetHDU()
        {
            WriteSampleFits();
            using var f = new Fits(_tempFile);
            var hdu0 = f.GetHDU(0);
            Assert.That(hdu0, Is.Not.Null);
            Assert.That(hdu0.BitPix, Is.EqualTo(BasicHDU.BITPIX_FLOAT));

            var hdu1 = f.GetHDU(1);
            Assert.That(hdu1, Is.Not.Null);
            Assert.That(hdu1.BitPix, Is.EqualTo(BasicHDU.BITPIX_INT));

            // Beyond end returns null
            var hdu2 = f.GetHDU(2);
            Assert.That(hdu2, Is.Null);
        }

        [Test]
        public void TestSkipHDU()
        {
            WriteSampleFits();
            using var f = new Fits(_tempFile);
            f.SkipHDU();
            var hdu = f.ReadHDU();
            // After skipping the first HDU, we should read the second
            Assert.That(hdu, Is.Not.Null);
            Assert.That(hdu.BitPix, Is.EqualTo(BasicHDU.BITPIX_INT));
        }

        [Test]
        public void TestSkipMultipleHDUs()
        {
            WriteSampleFits();
            using var f = new Fits(_tempFile);
            f.SkipHDU(2);
            var hdu = f.ReadHDU();
            Assert.That(hdu, Is.Null);
        }

        [Test]
        public void TestDeleteHDU()
        {
            using var f = new Fits();
            f.AddHDU(Fits.MakeHDU(new float[,] { { 1f } }));
            f.AddHDU(Fits.MakeHDU(new int[,] { { 2 } }));
            f.AddHDU(Fits.MakeHDU(new double[,] { { 3.0 } }));
            Assert.That(f.NumberOfHDUs, Is.EqualTo(3));

            f.DeleteHDU(1);
            Assert.That(f.NumberOfHDUs, Is.EqualTo(2));
        }

        [Test]
        public void TestDeleteFirstHDU()
        {
            using var f = new Fits();
            f.AddHDU(Fits.MakeHDU(new float[,] { { 1f } }));
            f.AddHDU(Fits.MakeHDU(new int[,] { { 2 } }));
            f.DeleteHDU(0);
            Assert.That(f.NumberOfHDUs, Is.EqualTo(1));
        }

        [Test]
        public void TestDeleteHDUOutOfRange()
        {
            using var f = new Fits();
            f.AddHDU(Fits.MakeHDU(new float[,] { { 1f } }));
            Assert.Throws<FitsException>(() => f.DeleteHDU(5));
            Assert.Throws<FitsException>(() => f.DeleteHDU(-1));
        }

        [Test]
        public void TestInsertHDUAtInvalidLocation()
        {
            using var f = new Fits();
            Assert.Throws<FitsException>(() =>
                f.InsertHDU(Fits.MakeHDU(new float[,] { { 1f } }), 5));
        }

        [Test]
        public void TestInsertHDUNull()
        {
            using var f = new Fits();
            f.InsertHDU(null, 0);
            Assert.That(f.NumberOfHDUs, Is.EqualTo(0));
        }

        [Test]
        public void TestAddHDU()
        {
            using var f = new Fits();
            f.AddHDU(Fits.MakeHDU(new float[,] { { 1f } }));
            Assert.That(f.NumberOfHDUs, Is.EqualTo(1));
            f.AddHDU(Fits.MakeHDU(new int[,] { { 2 } }));
            Assert.That(f.NumberOfHDUs, Is.EqualTo(2));
        }

        [Test]
        public void TestWriteToStream()
        {
            using var f = new Fits();
            f.AddHDU(Fits.MakeHDU(new float[,] { { 1f, 2f }, { 3f, 4f } }));

            // Write(Stream) wraps and closes the stream, so check file size instead
            using var bf = new BufferedFile(_tempFile, FileAccess.ReadWrite, FileShare.ReadWrite);
            f.Write(bf);
            var length = bf.Position;
            Assert.That(length, Is.GreaterThan(0));
            Assert.That(length % 2880, Is.EqualTo(0));
        }

        [Test]
        public void TestReadFromStream()
        {
            // Write to file, then read back via stream
            WriteSampleFits();
            var bytes = File.ReadAllBytes(_tempFile);
            using var ms = new MemoryStream(bytes);
            using var f = new Fits(ms);
            var hdus = f.Read();
            Assert.That(hdus, Is.Not.Null);
            Assert.That(hdus.Length, Is.EqualTo(2));
        }

        [Test]
        public void TestOpenWithFileAccess()
        {
            WriteSampleFits();
            using var f = new Fits(_tempFile, FileAccess.Read);
            var hdus = f.Read();
            Assert.That(hdus.Length, Is.EqualTo(2));
        }

        [Test]
        public void TestNullFilenameThrows()
        {
            Assert.Throws<NullReferenceException>(() => new Fits((string)null));
        }

        [Test]
        public void TestNonExistentFileThrows()
        {
            Assert.Throws<FitsException>(() => new Fits("/nonexistent/path/file.fits"));
        }

        [Test]
        public void TestNullStreamThrows()
        {
            Assert.Throws<FitsException>(() => new Fits((Stream)null, false));
        }

        [Test]
        public void TestMakeHDUFromHeader()
        {
            var data = new float[,] { { 1f } };
            var original = Fits.MakeHDU(data);
            var hdu = Fits.MakeHDU(original.Header);
            Assert.That(hdu, Is.Not.Null);
        }

        [Test]
        public void TestMakeHDUFromData()
        {
            var imgData = new ImageData(new float[,] { { 1f, 2f } });
            var hdu = Fits.MakeHDU(imgData);
            Assert.That(hdu, Is.Not.Null);
            Assert.That(hdu, Is.InstanceOf<ImageHDU>());
        }

        [Test]
        public void TestStreamProperty()
        {
            WriteSampleFits();
            using var f = new Fits(_tempFile);
            Assert.That(f.Stream, Is.Not.Null);
        }

        [Test]
        public void TestTempDirectory()
        {
            var original = Fits.TempDirectory;
            Assert.That(original, Is.Not.Null);
            Fits.TempDirectory = "/tmp/test";
            Assert.That(Fits.TempDirectory, Is.EqualTo("/tmp/test"));
            Fits.TempDirectory = original; // restore
        }

        [Test]
        public void TestSetChecksum()
        {
            using var f = new Fits();
            var data = new float[,] { { 1f, 2f }, { 3f, 4f } };
            var hdu = Fits.MakeHDU(data);
            f.AddHDU(hdu);

            Fits.SetChecksum(hdu);
            var checksum = hdu.Header.GetStringValue("CHECKSUM");
            Assert.That(checksum, Is.Not.Null);
            Assert.That(checksum.Length, Is.EqualTo(16));
        }

        [Test]
        public void TestSetChecksumAll()
        {
            using var f = new Fits();
            f.AddHDU(Fits.MakeHDU(new float[,] { { 1f } }));
            f.AddHDU(Fits.MakeHDU(new int[,] { { 2 } }));
            f.SetChecksum();

            for (int i = 0; i < f.NumberOfHDUs; i++)
            {
                var hdu = f.GetHDU(i);
                Assert.That(hdu.Header.GetStringValue("CHECKSUM"), Is.Not.Null);
            }
        }

        [Test]
        public void TestRoundTripPreservesData()
        {
            var original = new double[,] { { 1.1, 2.2, 3.3 }, { 4.4, 5.5, 6.6 } };
            var src = new Fits();
            src.AddHDU(Fits.MakeHDU(original));

            using var bf = new BufferedFile(_tempFile, FileAccess.ReadWrite, FileShare.ReadWrite);
            src.Write(bf);
            bf.Close();

            using var f = new Fits(_tempFile);
            var hdu = f.ReadHDU();
            var result = (double[,])hdu.Kernel;
            Assert.That(result[0, 0], Is.EqualTo(1.1).Within(1e-10));
            Assert.That(result[1, 2], Is.EqualTo(6.6).Within(1e-10));
        }

        [Test]
        public void TestFileInfoConstructor()
        {
            WriteSampleFits();
            using var f = new Fits(new FileInfo(_tempFile));
            var hdus = f.Read();
            Assert.That(hdus.Length, Is.EqualTo(2));
        }

        [Test]
        public void TestReadViaReadStream()
        {
            WriteSampleFits();
            using var ms = new MemoryStream(File.ReadAllBytes(_tempFile));
            using var f = new Fits();
            f.Read(ms);
            Assert.That(f.NumberOfHDUs, Is.EqualTo(2));
        }
    }
}
