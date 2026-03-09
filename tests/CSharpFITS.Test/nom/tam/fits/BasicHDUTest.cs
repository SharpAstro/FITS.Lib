using System;
using System.IO;
using NUnit.Framework;
using nom.tam.util;

namespace nom.tam.fits
{
    [TestFixture]
    public class BasicHDUTest
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

        private Fits CreateSimpleImageFits()
        {
            var data = new float[,] { { 1.0f, 2.0f }, { 3.0f, 4.0f } };
            var f = new Fits();
            f.AddHDU(Fits.MakeHDU(data));
            return f;
        }

        private Fits WriteAndReopen(Fits f)
        {
            using var bf = new BufferedFile(_tempFile, FileAccess.ReadWrite, FileShare.ReadWrite);
            f.Write(bf);
            bf.Close();
            return new Fits(_tempFile);
        }

        [Test]
        public void TestBitPix()
        {
            using var f = WriteAndReopen(CreateSimpleImageFits());
            var hdu = f.ReadHDU();
            Assert.That(hdu.BitPix, Is.EqualTo(BasicHDU.BITPIX_FLOAT));
        }

        [Test]
        public void TestAxes()
        {
            using var f = WriteAndReopen(CreateSimpleImageFits());
            var hdu = f.ReadHDU();
            var axes = hdu.Axes;
            Assert.That(axes, Is.Not.Null);
            Assert.That(axes.Length, Is.EqualTo(2));
            Assert.That(axes[0], Is.EqualTo(2));
            Assert.That(axes[1], Is.EqualTo(2));
        }

        [Test]
        public void TestSize()
        {
            using var f = WriteAndReopen(CreateSimpleImageFits());
            var hdu = f.ReadHDU();
            Assert.That(hdu.Size, Is.GreaterThan(0));
        }

        [Test]
        public void TestFileOffset()
        {
            using var f = WriteAndReopen(CreateSimpleImageFits());
            var hdu = f.ReadHDU();
            Assert.That(hdu.FileOffset, Is.EqualTo(0));
        }

        [Test]
        public void TestKernel()
        {
            using var f = WriteAndReopen(CreateSimpleImageFits());
            var hdu = f.ReadHDU();
            var kernel = hdu.Kernel;
            Assert.That(kernel, Is.Not.Null);
            Assert.That(kernel, Is.InstanceOf<float[,]>());
        }

        [Test]
        public void TestHeaderAndData()
        {
            using var f = WriteAndReopen(CreateSimpleImageFits());
            var hdu = f.ReadHDU();
            Assert.That(hdu.Header, Is.Not.Null);
            Assert.That(hdu.Data, Is.Not.Null);
        }

        [Test]
        public void TestParameterAndGroupCount()
        {
            using var f = WriteAndReopen(CreateSimpleImageFits());
            var hdu = f.ReadHDU();
            Assert.That(hdu.ParameterCount, Is.EqualTo(0));
            Assert.That(hdu.GroupCount, Is.EqualTo(1));
        }

        [Test]
        public void TestBScaleAndBZero()
        {
            using var f = WriteAndReopen(CreateSimpleImageFits());
            var hdu = f.ReadHDU();
            Assert.That(hdu.BScale, Is.EqualTo(1.0));
            Assert.That(hdu.BZero, Is.EqualTo(0.0));
        }

        [Test]
        public void TestMetadataPropertiesDefaultNull()
        {
            using var f = WriteAndReopen(CreateSimpleImageFits());
            var hdu = f.ReadHDU();
            // These keywords aren't set, so they return null or default
            Assert.That(hdu.BUnit, Is.Null);
            Assert.That(hdu.Origin, Is.Null);
            Assert.That(hdu.Telescope, Is.Null);
            Assert.That(hdu.Instrument, Is.Null);
            Assert.That(hdu.Observer, Is.Null);
            Assert.That(hdu.Object, Is.Null);
            Assert.That(hdu.Author, Is.Null);
            Assert.That(hdu.Reference, Is.Null);
            Assert.That(hdu.Equinox, Is.EqualTo(-1.0));
            Assert.That(hdu.Epoch, Is.EqualTo(-1.0));
        }

        [Test]
        public void TestMetadataPropertiesWithValues()
        {
            var data = new float[,] { { 1.0f, 2.0f }, { 3.0f, 4.0f } };
            var src = new Fits();
            var hdu = Fits.MakeHDU(data);
            hdu.AddValue("BUNIT", "ADU", "Data units");
            hdu.AddValue("ORIGIN", "Test Lab", "Origin");
            hdu.AddValue("TELESCOP", "Hubble", "Telescope");
            hdu.AddValue("INSTRUME", "WFC3", "Instrument");
            hdu.AddValue("OBSERVER", "Jane Doe", "Observer");
            hdu.AddValue("OBJECT", "M31", "Object name");
            hdu.AddValue("AUTHOR", "John Smith", "Author");
            hdu.AddValue("REFERENC", "2024ApJ...1", "Reference");
            hdu.AddValue("EQUINOX", 2000.0, "Equinox");
            hdu.AddValue("EPOCH", 2000.0, "Epoch");
            hdu.AddValue("DATAMAX", 100.0, "Max value");
            hdu.AddValue("DATAMIN", 0.5, "Min value");
            hdu.AddValue("BSCALE", 2.0, "Scale");
            hdu.AddValue("BZERO", 10.0, "Zero");
            src.AddHDU(hdu);

            using var f = WriteAndReopen(src);
            var readHdu = f.ReadHDU();

            Assert.That(readHdu.BUnit, Is.EqualTo("ADU"));
            Assert.That(readHdu.Origin, Is.EqualTo("Test Lab"));
            Assert.That(readHdu.Telescope, Is.EqualTo("Hubble"));
            Assert.That(readHdu.Instrument, Is.EqualTo("WFC3"));
            Assert.That(readHdu.Observer, Is.EqualTo("Jane Doe"));
            Assert.That(readHdu.Object, Is.EqualTo("M31"));
            Assert.That(readHdu.Author, Is.EqualTo("John Smith"));
            Assert.That(readHdu.Reference, Is.EqualTo("2024ApJ...1"));
            Assert.That(readHdu.Equinox, Is.EqualTo(2000.0));
            Assert.That(readHdu.Epoch, Is.EqualTo(2000.0));
            Assert.That(readHdu.MaximumValue, Is.EqualTo(100.0));
            Assert.That(readHdu.MinimumValue, Is.EqualTo(0.5));
            Assert.That(readHdu.BScale, Is.EqualTo(2.0));
            Assert.That(readHdu.BZero, Is.EqualTo(10.0));
        }

        [Test]
        public void TestBlankValueThrowsWhenUndefined()
        {
            using var f = WriteAndReopen(CreateSimpleImageFits());
            var hdu = f.ReadHDU();
            Assert.Throws<FitsException>(() => { var _ = hdu.BlankValue; });
        }

        [Test]
        public void TestBlankValueWhenDefined()
        {
            var data = new int[,] { { 1, 2 }, { 3, -999 } };
            var src = new Fits();
            var hdu = Fits.MakeHDU(data);
            hdu.AddValue("BLANK", -999, "Blank value");
            src.AddHDU(hdu);

            using var f = WriteAndReopen(src);
            var readHdu = f.ReadHDU();
            Assert.That(readHdu.BlankValue, Is.EqualTo(-999));
        }

        [Test]
        public void TestDummyHDU()
        {
            var dummy = BasicHDU.DummyHDU;
            Assert.That(dummy, Is.Not.Null);
            Assert.That(dummy, Is.InstanceOf<ImageHDU>());
        }

        [Test]
        public void TestIsHeader()
        {
            // BasicHDU.IsHeader always returns false
            Assert.That(BasicHDU.IsHeader(new Header()), Is.False);
        }

        [Test]
        public void TestGetTrimmedString()
        {
            var data = new float[,] { { 1.0f } };
            var src = new Fits();
            var hdu = Fits.MakeHDU(data);
            hdu.AddValue("TESTKEY", "  padded value  ", "test");
            src.AddHDU(hdu);

            using var f = WriteAndReopen(src);
            var readHdu = f.ReadHDU();
            Assert.That(readHdu.GetTrimmedString("TESTKEY"), Is.EqualTo("padded value"));
            Assert.That(readHdu.GetTrimmedString("NOKEY"), Is.Null);
        }

        [Test]
        public void TestAddValueOverloads()
        {
            var data = new float[,] { { 1.0f } };
            var hdu = Fits.MakeHDU(data);
            hdu.AddValue("BOOLKEY", true, "bool test");
            hdu.AddValue("INTKEY", 42, "int test");
            hdu.AddValue("DBLKEY", 3.14, "double test");
            hdu.AddValue("STRKEY", "hello", "string test");

            Assert.That(hdu.Header.GetBooleanValue("BOOLKEY"), Is.True);
            Assert.That(hdu.Header.GetIntValue("INTKEY"), Is.EqualTo(42));
            Assert.That(hdu.Header.GetDoubleValue("DBLKEY"), Is.EqualTo(3.14).Within(0.001));
            Assert.That(hdu.Header.GetStringValue("STRKEY"), Is.EqualTo("hello"));
        }

        [Test]
        public void TestRewriteThrowsWhenNotRewriteable()
        {
            // An HDU created in memory (not from a stream) is not rewriteable
            var data = new float[,] { { 1.0f } };
            var hdu = Fits.MakeHDU(data);
            Assert.Throws<FitsException>(() => hdu.Rewrite());
        }

        [Test]
        public void TestWriteAndReadHDU()
        {
            var data = new double[,] { { 1.0, 2.0, 3.0 }, { 4.0, 5.0, 6.0 } };
            var src = new Fits();
            src.AddHDU(Fits.MakeHDU(data));

            using var f = WriteAndReopen(src);
            var hdu = f.ReadHDU();
            Assert.That(hdu, Is.Not.Null);
            Assert.That(hdu.BitPix, Is.EqualTo(BasicHDU.BITPIX_DOUBLE));
            var axes = hdu.Axes;
            Assert.That(axes[0], Is.EqualTo(2));
            Assert.That(axes[1], Is.EqualTo(3));
        }

        [Test]
        public void TestSkipData()
        {
            using (var src = WriteAndReopen(CreateSimpleImageFits()))
            {
                // just force close so the file is released
            }
            // Read header manually, then skip data
            using var bf = new BufferedFile(_tempFile, FileAccess.Read, FileShare.Read);
            var hdr = Header.ReadHeader(bf);
            Assert.That(hdr, Is.Not.Null);
            BasicHDU.SkipData(bf, hdr);
            // After skipping, position should be past the data
            Assert.That(bf.Position, Is.GreaterThan(0));
        }

        [Test]
        public void TestCreationDate()
        {
            var dateStr = FitsDate.FitsDateString;
            var src = new Fits();
            var hdu = Fits.MakeHDU(new float[,] { { 1.0f } });
            hdu.AddValue("DATE", dateStr, "Creation date");
            src.AddHDU(hdu);

            using var f = WriteAndReopen(src);
            var readHdu = f.ReadHDU();
            var date = readHdu.CreationDate;
            Assert.That(date.Year, Is.EqualTo(DateTime.Now.Year));
        }

        [Test]
        public void TestObservationDate()
        {
            var src = new Fits();
            var hdu = Fits.MakeHDU(new float[,] { { 1.0f } });
            hdu.AddValue("DATE-OBS", "2024-06-15T12:00:00", "Obs date");
            src.AddHDU(hdu);

            using var f = WriteAndReopen(src);
            var readHdu = f.ReadHDU();
            var date = readHdu.ObservationDate;
            Assert.That(date.Year, Is.EqualTo(2024));
            Assert.That(date.Month, Is.EqualTo(6));
            Assert.That(date.Day, Is.EqualTo(15));
        }

        [Test]
        public void TestBitPixConstants()
        {
            Assert.That(BasicHDU.BITPIX_BYTE, Is.EqualTo(8));
            Assert.That(BasicHDU.BITPIX_SHORT, Is.EqualTo(16));
            Assert.That(BasicHDU.BITPIX_INT, Is.EqualTo(32));
            Assert.That(BasicHDU.BITPIX_LONG, Is.EqualTo(64));
            Assert.That(BasicHDU.BITPIX_FLOAT, Is.EqualTo(-32));
            Assert.That(BasicHDU.BITPIX_DOUBLE, Is.EqualTo(-64));
        }

        [Test]
        public void TestMultipleHDUTypes()
        {
            var byteData = new byte[,] { { 1, 2 }, { 3, 4 } };
            var shortData = new short[,] { { 10, 20 }, { 30, 40 } };
            var intData = new int[,] { { 100, 200 }, { 300, 400 } };

            var src = new Fits();
            src.AddHDU(Fits.MakeHDU(byteData));
            src.AddHDU(Fits.MakeHDU(shortData));
            src.AddHDU(Fits.MakeHDU(intData));

            using var f = WriteAndReopen(src);
            var hdus = f.Read();
            Assert.That(hdus.Length, Is.EqualTo(3));
            Assert.That(hdus[0].BitPix, Is.EqualTo(BasicHDU.BITPIX_BYTE));
            Assert.That(hdus[1].BitPix, Is.EqualTo(BasicHDU.BITPIX_SHORT));
            Assert.That(hdus[2].BitPix, Is.EqualTo(BasicHDU.BITPIX_INT));
        }
    }
}
