using System;
using System.IO;
using NUnit.Framework;
using nom.tam.util;

namespace nom.tam.fits
{
    [TestFixture]
    public class ChannelApiTest
    {
        private const string TestFile = "channel_test.fits";

        [OneTimeSetUp]
        public void CreateTestFile()
        {
            // 1D: double[100]
            var img1d = new double[100];
            for (int i = 0; i < 100; i++)
                img1d[i] = i * 1.5;

            // 2D: float[20,30] (rectangular)
            var img2d = new float[20, 30];
            for (int i = 0; i < 20; i++)
                for (int j = 0; j < 30; j++)
                    img2d[i, j] = i + j;

            // 3D: written as int[4][10][15] jagged (FITS stores as 3D)
            var img3d = new int[4][][];
            for (int i = 0; i < 4; i++)
            {
                img3d[i] = new int[10][];
                for (int j = 0; j < 10; j++)
                {
                    img3d[i][j] = new int[15];
                    for (int k = 0; k < 15; k++)
                        img3d[i][j][k] = i * 100 + j * 10 + k;
                }
            }

            var f = new Fits();
            f.AddHDU(Fits.MakeHDU(img1d));
            f.AddHDU(Fits.MakeHDU(img2d));
            f.AddHDU(Fits.MakeHDU(img3d));

            var bf = new BufferedFile(TestFile, FileAccess.ReadWrite, FileShare.ReadWrite);
            f.Write(bf);
            bf.Flush();
            bf.Close();
        }

        [OneTimeTearDown]
        public void Cleanup()
        {
            try
            {
                if (File.Exists(TestFile))
                    File.Delete(TestFile);
            }
            catch (IOException)
            {
                // File may still be held open by Fits reader; ignore
            }
        }

        private BasicHDU[] ReadHDUs()
        {
            var f = new Fits(TestFile);
            return f.Read();
        }

        // --- ChannelCount tests ---

        [Test]
        public void ChannelCount_1D_ReturnsOne()
        {
            var hdus = ReadHDUs();
            var hdu = (ImageHDU)hdus[0];
            Assert.AreEqual(1, hdu.ChannelCount);
        }

        [Test]
        public void ChannelCount_2D_ReturnsOne()
        {
            var hdus = ReadHDUs();
            var hdu = (ImageHDU)hdus[1];
            Assert.AreEqual(1, hdu.ChannelCount);
        }

        [Test]
        public void ChannelCount_3D_ReturnsChannelCount()
        {
            var hdus = ReadHDUs();
            var hdu = (ImageHDU)hdus[2];
            Assert.AreEqual(4, hdu.ChannelCount);
        }

        // --- GetChannel tests for 1D ---

        [Test]
        public void GetChannel_1D_Index0_ReturnsFullArray()
        {
            var hdus = ReadHDUs();
            var hdu = (ImageHDU)hdus[0];
            var channel = hdu.GetChannel(0);

            Assert.IsInstanceOf<double[]>(channel);
            var arr = (double[])channel;
            Assert.AreEqual(100, arr.Length);
            Assert.AreEqual(0.0, arr[0], 1e-10);
            Assert.AreEqual(1.5, arr[1], 1e-10);
            Assert.AreEqual(99 * 1.5, arr[99], 1e-10);
        }

        [Test]
        public void GetChannel_1D_InvalidIndex_Throws()
        {
            var hdus = ReadHDUs();
            var hdu = (ImageHDU)hdus[0];
            Assert.Throws<IndexOutOfRangeException>(() => hdu.GetChannel(1));
            Assert.Throws<IndexOutOfRangeException>(() => hdu.GetChannel(-1));
        }

        // --- GetChannel tests for 2D ---

        [Test]
        public void GetChannel_2D_Index0_ReturnsFullArray()
        {
            var hdus = ReadHDUs();
            var hdu = (ImageHDU)hdus[1];
            var channel = hdu.GetChannel(0);

            Assert.IsInstanceOf<float[,]>(channel);
            var arr = (float[,])channel;
            Assert.AreEqual(20, arr.GetLength(0));
            Assert.AreEqual(30, arr.GetLength(1));
            Assert.AreEqual(0f, arr[0, 0]);
            Assert.AreEqual(19f + 29f, arr[19, 29]);
        }

        [Test]
        public void GetChannel_2D_InvalidIndex_Throws()
        {
            var hdus = ReadHDUs();
            var hdu = (ImageHDU)hdus[1];
            Assert.Throws<IndexOutOfRangeException>(() => hdu.GetChannel(1));
            Assert.Throws<IndexOutOfRangeException>(() => hdu.GetChannel(-1));
        }

        // --- GetChannel tests for 3D ---

        [Test]
        public void GetChannel_3D_ReturnsCorrectChannels()
        {
            var hdus = ReadHDUs();
            var hdu = (ImageHDU)hdus[2];

            for (int ch = 0; ch < 4; ch++)
            {
                var channel = hdu.GetChannel(ch);
                Assert.IsInstanceOf<int[,]>(channel);
                var arr = (int[,])channel;
                Assert.AreEqual(10, arr.GetLength(0));
                Assert.AreEqual(15, arr.GetLength(1));

                // Verify data values
                for (int j = 0; j < 10; j++)
                    for (int k = 0; k < 15; k++)
                        Assert.AreEqual(ch * 100 + j * 10 + k, arr[j, k],
                            $"Mismatch at channel={ch}, j={j}, k={k}");
            }
        }

        [Test]
        public void GetChannel_3D_InvalidIndex_Throws()
        {
            var hdus = ReadHDUs();
            var hdu = (ImageHDU)hdus[2];
            Assert.Throws<IndexOutOfRangeException>(() => hdu.GetChannel(4));
            Assert.Throws<IndexOutOfRangeException>(() => hdu.GetChannel(-1));
        }

        // --- ImageData direct access ---

        [Test]
        public void ImageData_ChannelCount_MatchesHDU()
        {
            var hdus = ReadHDUs();
            for (int i = 0; i < 3; i++)
            {
                var hdu = (ImageHDU)hdus[i];
                var imageData = (ImageData)hdu.Data;
                Assert.AreEqual(hdu.ChannelCount, imageData.ChannelCount);
            }
        }

        // --- NAXIS verification ---

        [Test]
        public void NAXIS_MatchesDimensionality()
        {
            var hdus = ReadHDUs();
            var expectedNaxis = new[] { 1, 2, 3 };

            for (int i = 0; i < 3; i++)
            {
                var hdu = (ImageHDU)hdus[i];
                Assert.AreEqual(expectedNaxis[i], hdu.Axes.Length,
                    $"HDU[{i}] Axes.Length mismatch");
                Assert.AreEqual(expectedNaxis[i], hdu.Header.GetIntValue("NAXIS"),
                    $"HDU[{i}] NAXIS header mismatch");
            }
        }
    }
}
