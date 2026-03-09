using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using NUnit.Framework;

namespace nom.tam.fits
{
    [TestFixture]
    public class LargeImageTest
    {
        private const string TestFile = "testdocs/LDN1089_singleFrame.fits";
        private const string ExpectedHash = "7C431CB224BBAED51742338F0652AD1AEF9A601A9418A602E4AE49597EBE0B00";

        [Test]
        public void TestLoadLargeFloatImage()
        {
            if (!File.Exists(TestFile))
                Assert.Ignore("Large test file not available");

            var fits = new Fits(TestFile);
            var hdus = fits.Read();

            Assert.That(hdus, Is.Not.Null);
            Assert.That(hdus.Length, Is.EqualTo(1));
            Assert.That(hdus[0], Is.InstanceOf<ImageHDU>());

            var header = hdus[0].Header;
            Assert.That(header.GetIntValue("BITPIX"), Is.EqualTo(-32));
            Assert.That(header.GetIntValue("NAXIS"), Is.EqualTo(3));
            Assert.That(header.GetIntValue("NAXIS1"), Is.EqualTo(6248));
            Assert.That(header.GetIntValue("NAXIS2"), Is.EqualTo(4176));
            Assert.That(header.GetIntValue("NAXIS3"), Is.EqualTo(3));

            var data = hdus[0].Data.DataArray;
            Assert.That(data, Is.Not.Null);

            string hash = ComputeImageHash(data);
            Assert.That(hash, Is.EqualTo(ExpectedHash), "Image data hash mismatch - data corruption detected");

            fits.Close();
        }

        private static string ComputeImageHash(object data)
        {
            using var sha256 = SHA256.Create();
            HashArrayRecursive(sha256, data);
            sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return Convert.ToHexString(sha256.Hash!);
        }

        private static void HashArrayRecursive(SHA256 sha256, object o)
        {
            if (o is float[] floatArr)
            {
                var bytes = MemoryMarshal.AsBytes(floatArr.AsSpan());
                sha256.TransformBlock(bytes.ToArray(), 0, bytes.Length, null, 0);
            }
            else if (o is Array arr && arr.Rank > 1 && arr.GetType().GetElementType() == typeof(float))
            {
                // Rectangular multi-dimensional float array: hash via BlockCopy to flat buffer
                var flat = new float[arr.Length];
                Buffer.BlockCopy(arr, 0, flat, 0, arr.Length * sizeof(float));
                var bytes = MemoryMarshal.AsBytes(flat.AsSpan());
                sha256.TransformBlock(bytes.ToArray(), 0, bytes.Length, null, 0);
            }
            else if (o is Array jarr)
            {
                foreach (var element in jarr)
                {
                    if (element != null)
                        HashArrayRecursive(sha256, element);
                }
            }
        }
    }
}
