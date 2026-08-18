using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using nom.tam.fits.compression;
using nom.tam.util;

namespace nom.tam.fits
{
    /// <summary>
    /// Reading tile-compressed (<c>.fz</c>) images.
    /// <para>
    /// Every fixture is a pair: a <c>.fz</c> file and a <c>.ref.fits</c> holding the
    /// array cfitsio decodes it to, both written by <c>tools/make-tilecompress-fixtures.py</c>.
    /// The reference is the oracle, so these tests check this library against the
    /// implementation every other tool in the field uses rather than against itself.
    /// </para>
    /// <para>
    /// Integer images, PLIO masks and unquantized float images are LOSSLESS, so their
    /// comparison is exact. A quantized float image is not -- but both sides decode the
    /// same integers with the same dither, so they may still only differ in the last
    /// bit or two of the float, which is what the ULP bound pins.
    /// </para>
    /// </summary>
    [TestFixture]
    public class TileCompressionTest
    {
        private static string FixtureDir =>
            Path.Combine(TestContext.CurrentContext.TestDirectory, "testdocs", "tilecompress");

        /// <summary>Fixtures whose round trip is exact.</summary>
        private static readonly string[] Lossless =
        {
            "rice_i16",
            "rice_i32",
            "rice_u8",
            "rice_i16_partial_tiles",
            "rice_cube",
            "rice_i16_noise",
            "rice_i16_metadata",
            "gzip1_i16",
            "gzip2_i32",
            "gzip1_f32",
            "gzip2_f32",
            "plio_i16_mask",
            "rice_f32_nan_gzipfallback",
        };

        /// <summary>Fixtures quantized onto integers, so lossy against the ORIGINAL --
        /// but not against the reference, which was decoded from the same integers.</summary>
        private static readonly string[] Quantized =
        {
            "rice_f32_dither1",
            "rice_f32_dither2",
            "rice_f32_nodither",
            "rice_f32_nan",
        };

        [TearDown]
        public void ResetFactory()
        {
            FitsFactory.UseTileCompression = true;
        }

        [Test, TestCaseSource(nameof(Lossless))]
        public void DecompressesExactly(string name)
        {
            var (actual, expected) = ReadPair(name);
            AssertSameShape(actual, expected, name);

            int worst = MaxUlpDifference(actual, expected, out int at);
            Assert.That(worst, Is.Zero,
                $"{name}: pixel {at} differs by {worst} ulps, but this fixture is losslessly compressed");
        }

        [Test, TestCaseSource(nameof(Quantized))]
        public void DecompressesToWithinAnUlpOfCfitsio(string name)
        {
            var (actual, expected) = ReadPair(name);
            AssertSameShape(actual, expected, name);

            // Both implementations dequantize in double and narrow to the stored type,
            // so the only slack is the double rounding of that last operation.
            int worst = MaxUlpDifference(actual, expected, out int at);
            Assert.That(worst, Is.LessThanOrEqualTo(2),
                $"{name}: pixel {at} differs from the cfitsio reference by {worst} ulps");
        }

        [Test]
        public void DecompressesADoubleImageToWithinRoundingOfCfitsio()
        {
            var (actual, expected) = ReadPair("rice_f64_dither1");
            AssertSameShape(actual, expected, "rice_f64_dither1");

            // Dequantization is value = (quantized - dither + 0.5) * ZSCALE + ZZERO, and
            // cfitsio picks a ZZERO that puts the integers near -2^31, so that sum
            // cancels catastrophically: the result is ~1e-11 of the magnitudes going
            // into it. At float32 the difference disappears in the narrowing, which is
            // why the float fixtures agree to a ulp; at float64 it survives, and each
            // implementation's own association of the arithmetic decides the last bits.
            // A relative 1e-9 is still seven orders of magnitude below one quantization
            // step, so a genuinely wrong dither or scale could not hide under it.
            double worst = MaxRelativeDifference(actual, expected, out int at);
            Assert.That(worst, Is.LessThanOrEqualTo(1e-9),
                $"pixel {at} differs from the cfitsio reference by {worst:E3} relative");
        }

        [Test]
        public void ReadsTheCompressedImageAsAnImageHdu()
        {
            var hdu = ReadCompressed("rice_i16");

            Assert.That(hdu, Is.InstanceOf<CompressedImageHDU>());
            Assert.That(hdu, Is.InstanceOf<ImageHDU>(), "a compressed image must read as an image");
            Assert.That(hdu.Data, Is.InstanceOf<ImageData>());
            Assert.That(hdu.BitPix, Is.EqualTo(16));
            Assert.That(hdu.Axes, Is.EqualTo(new[] { 64, 96 }));
        }

        [Test]
        public void ReportsTheCompressionItUsed()
        {
            var rice = (CompressedImageHDU)ReadCompressed("rice_f32_dither2");
            Assert.That(rice.CompressionType, Is.EqualTo(TileCompressionType.Rice));
            Assert.That(rice.Quantization, Is.EqualTo(QuantizationMethod.SubtractiveDither2));
            Assert.That(rice.TileDimensions, Is.EqualTo(new[] { 64, 8 }));

            var plio = (CompressedImageHDU)ReadCompressed("plio_i16_mask");
            Assert.That(plio.CompressionType, Is.EqualTo(TileCompressionType.Plio));

            var gzip = (CompressedImageHDU)ReadCompressed("gzip2_f32");
            Assert.That(gzip.CompressionType, Is.EqualTo(TileCompressionType.Gzip2));
        }

        [Test]
        public void TranslatesTheHeaderIntoAnImageHeader()
        {
            var hdu = ReadCompressed("rice_i16_metadata");
            Header h = hdu.Header;

            // The structural cards say what the IMAGE is, in the order the standard wants.
            Assert.That(h.GetStringValue("XTENSION"), Is.EqualTo("IMAGE"));
            Assert.That(h.GetIntValue("BITPIX"), Is.EqualTo(16));
            Assert.That(h.GetIntValue("NAXIS"), Is.EqualTo(2));
            Assert.That(h.GetIntValue("NAXIS1"), Is.EqualTo(16));
            Assert.That(h.GetIntValue("NAXIS2"), Is.EqualTo(16));
            Assert.That(h.GetIntValue("PCOUNT"), Is.EqualTo(0));
            Assert.That(h.GetIntValue("GCOUNT"), Is.EqualTo(1));

            // The image's own metadata survives untouched -- this is the whole point of
            // the translation, since it is what a consumer actually reads.
            Assert.That(h.GetStringValue("OBJECT"), Is.EqualTo("Bubble Nebula"));
            Assert.That(h.GetDoubleValue("EXPTIME"), Is.EqualTo(150.0));
            Assert.That(h.GetStringValue("INSTRUME"), Is.EqualTo("AA585CTEC"));
            Assert.That(h.GetStringValue("ROWORDER"), Is.EqualTo("TOP-DOWN"));
            Assert.That(h.GetIntValue("STACKCNT"), Is.EqualTo(163));
            Assert.That(h.GetDoubleValue("CCD-TEMP"), Is.EqualTo(-9.9).Within(1e-9));
            Assert.That(h.GetStringValue("BAYERPAT"), Is.EqualTo("RGGB"));

            // ...and the comment cards with it.
            string dump = DumpHeader(h);
            Assert.That(dump, Does.Contain("winsorized sigma clipping"));
            Assert.That(dump, Does.Contain("must survive translation"));

            // The compression keywords have done their job and are gone, along with the
            // binary table's own structure.
            foreach (string gone in new[]
                     {
                         "ZIMAGE", "ZBITPIX", "ZNAXIS", "ZNAXIS1", "ZNAXIS2", "ZTILE1", "ZTILE2",
                         "ZCMPTYPE", "ZNAME1", "ZVAL1", "ZNAME2", "ZVAL2", "ZPCOUNT", "ZGCOUNT",
                         "ZTENSION", "TFIELDS", "TTYPE1", "TFORM1",
                     })
            {
                Assert.That(h.ContainsKey(gone), Is.False, $"{gone} should not survive translation");
            }
        }

        [Test]
        public void KeepsTheCompressedHeaderAvailable()
        {
            var hdu = (CompressedImageHDU)ReadCompressed("rice_i16");

            Assert.That(hdu.CompressedHeader.GetStringValue("XTENSION"), Is.EqualTo("BINTABLE"));
            Assert.That(hdu.CompressedHeader.GetStringValue("ZCMPTYPE"), Is.EqualTo("RICE_1"));
            Assert.That(hdu.CompressedHeader.GetBooleanValue("ZIMAGE"), Is.True);
        }

        [Test]
        public void ExposesChannelsOfACompressedCube()
        {
            var hdu = (CompressedImageHDU)ReadCompressed("rice_cube");

            Assert.That(hdu.Axes, Is.EqualTo(new[] { 3, 24, 40 }));
            Assert.That(hdu.ChannelCount, Is.EqualTo(3));

            var expected = (ImageHDU)ReadReference("rice_cube");
            for (int c = 0; c < 3; c++)
            {
                Assert.That(hdu.GetChannel(c), Is.InstanceOf<short[,]>());
                Assert.That(hdu.GetChannel(c), Is.EqualTo(expected.GetChannel(c)), $"channel {c}");
            }
        }

        [Test]
        public void TilesACompressedImageLikeAnyOther()
        {
            var hdu = (CompressedImageHDU)ReadCompressed("rice_i16");
            var full = (short[,])((ImageHDU)ReadReference("rice_i16")).GetChannel(0);

            // A 5x7 sub-image starting at (10, 20), through the same tiler an
            // uncompressed image offers.
            var tile = new short[5 * 7];
            hdu.Tiler.GetTile(tile, new[] { 10, 20 }, new[] { 5, 7 });

            for (int y = 0; y < 5; y++)
            {
                for (int x = 0; x < 7; x++)
                {
                    Assert.That(tile[y * 7 + x], Is.EqualTo(full[10 + y, 20 + x]), $"({y},{x})");
                }
            }
        }

        [Test]
        public void HeaderOnlyReadsDoNotDecompress()
        {
            var fits = new Fits(Path.Combine(FixtureDir, "rice_i16.fz"));
            fits.ReadHDUHeaderOnly();                      // the empty primary
            var hdu = fits.ReadHDUHeaderOnly();

            Assert.That(hdu, Is.InstanceOf<CompressedImageHDU>());
            Assert.That(hdu.Header.GetIntValue("NAXIS1"), Is.EqualTo(96),
                "the translated header must be available without touching the data");
            Assert.That(hdu.Data.DataArray, Is.Null, "no pixels should have been read");
        }

        [Test]
        public void CanBeAskedForTheRawBinaryTableInstead()
        {
            FitsFactory.UseTileCompression = false;

            var hdu = ReadCompressed("rice_i16");

            Assert.That(hdu, Is.InstanceOf<BinaryTableHDU>());
            Assert.That(hdu.Header.GetStringValue("XTENSION"), Is.EqualTo("BINTABLE"));
        }

        [Test]
        public void WritingACompressedImageFunpacksIt()
        {
            var hdu = ReadCompressed("rice_f32_dither1");
            string path = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                                       $"funpacked_{Guid.NewGuid():N}.fits");
            try
            {
                var output = new Fits();
                output.AddHDU(hdu);
                using (var bf = new BufferedFile(path, FileAccess.ReadWrite, FileShare.ReadWrite))
                {
                    output.Write(bf);
                    bf.Flush();
                }

                var reread = new Fits(path);
                var image = reread.ReadHDU();
                var written = Flatten(image);
                reread.Close();

                Assert.That(image, Is.InstanceOf<ImageHDU>());
                Assert.That(image.BitPix, Is.EqualTo(-32));
                Assert.That(image.Axes, Is.EqualTo(new[] { 64, 64 }));

                int worst = MaxUlpDifference(written, Flatten(ReadReference("rice_f32_dither1")),
                                             out int at);
                Assert.That(worst, Is.Zero, $"pixel {at} changed by {worst} ulps on the way out");
            }
            finally
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch (IOException)
                {
                    // The reader may still hold the handle; the temp file is harmless.
                }
            }
        }

        [Test]
        public void NamesAnAlgorithmItCannotDecode()
        {
            var hdu = ReadCompressed("hcompress_i16");

            // Recognising the file is not the same as being able to decode it, and the
            // difference has to be stated rather than guessed at.
            Assert.That(((CompressedImageHDU)hdu).CompressionType,
                        Is.EqualTo(TileCompressionType.HCompress));

            var ex = Assert.Throws<FitsException>(() => { var _ = hdu.Data.DataArray; });
            Assert.That(ex.Message, Does.Contain("HCOMPRESS_1"));
            Assert.That(ex.Message, Does.Contain("funpack"));
        }

        [Test]
        public void PreservesExactZerosUnderDitherTwo()
        {
            // Subtractive dither 2 exists precisely so that a pixel recorded as zero
            // comes back as zero rather than as a dithered near-zero; the fixture has a
            // 4x4 patch of them.
            var data = (float[,])Flatten(ReadCompressed("rice_f32_dither2")).Values;

            for (int y = 10; y < 14; y++)
            {
                for (int x = 10; x < 14; x++)
                {
                    Assert.That(data[y, x], Is.EqualTo(0.0f), $"({y},{x}) should be exactly zero");
                }
            }
        }

        [Test]
        public void RecoversUndefinedPixelsAsNaN()
        {
            var data = (float[,])Flatten(ReadCompressed("rice_f32_nan")).Values;

            for (int y = 20; y < 24; y++)
            {
                for (int x = 30; x < 36; x++)
                {
                    Assert.That(float.IsNaN(data[y, x]), Is.True, $"({y},{x}) should be NaN");
                }
            }
            Assert.That(float.IsNaN(data[0, 0]), Is.False, "only the flagged pixels are undefined");
        }

        // ---------------------------------------------------------------- helpers

        private static BasicHDU ReadCompressed(string name)
        {
            var fits = new Fits(Path.Combine(FixtureDir, name + ".fz"));
            fits.ReadHDU();               // the empty primary HDU fpack always writes
            var hdu = fits.ReadHDU();
            Assert.That(hdu, Is.Not.Null, $"{name}.fz has no second HDU");
            return hdu;
        }

        private static BasicHDU ReadReference(string name)
        {
            var fits = new Fits(Path.Combine(FixtureDir, name + ".ref.fits"));
            return fits.ReadHDU();
        }

        private static (Flat Actual, Flat Expected) ReadPair(string name)
            => (Flatten(ReadCompressed(name)), Flatten(ReadReference(name)));

        /// <summary>An image reduced to its channels, so two of them can be compared
        /// without caring how many axes they have.</summary>
        private sealed class Flat
        {
            internal Array Values;              // the channel array, for direct indexing
            internal readonly List<Array> Channels = new List<Array>();
            internal int[] Axes;
        }

        private static Flat Flatten(BasicHDU hdu)
        {
            var image = (ImageHDU)hdu;
            var flat = new Flat { Axes = hdu.Axes };
            for (int c = 0; c < image.ChannelCount; c++)
            {
                flat.Channels.Add(image.GetChannel(c));
            }
            flat.Values = flat.Channels.Count > 0 ? flat.Channels[0] : null;
            return flat;
        }

        private static void AssertSameShape(Flat actual, Flat expected, string name)
        {
            Assert.That(actual.Axes, Is.EqualTo(expected.Axes), $"{name}: axes");
            Assert.That(actual.Channels.Count, Is.EqualTo(expected.Channels.Count), $"{name}: channels");
        }

        /// <summary>The largest difference between two images, measured in
        /// representable values ("ulps") rather than in an absolute epsilon, so the
        /// bound means the same thing at every magnitude.</summary>
        private static int MaxUlpDifference(Flat actual, Flat expected, out int worstIndex)
        {
            int worst = 0;
            worstIndex = -1;
            int flatIndex = 0;

            for (int c = 0; c < actual.Channels.Count; c++)
            {
                IEnumerator a = actual.Channels[c].GetEnumerator();
                IEnumerator b = expected.Channels[c].GetEnumerator();
                while (a.MoveNext() && b.MoveNext())
                {
                    int diff = UlpDifference(a.Current, b.Current);
                    if (diff > worst)
                    {
                        worst = diff;
                        worstIndex = flatIndex;
                    }
                    flatIndex++;
                }
            }

            return worst;
        }

        /// <summary>The largest difference between two images relative to the values
        /// themselves, for comparisons where counting representable values would be
        /// measuring the arithmetic rather than the decode.</summary>
        private static double MaxRelativeDifference(Flat actual, Flat expected, out int worstIndex)
        {
            double worst = 0;
            worstIndex = -1;
            int flatIndex = 0;

            for (int c = 0; c < actual.Channels.Count; c++)
            {
                IEnumerator a = actual.Channels[c].GetEnumerator();
                IEnumerator b = expected.Channels[c].GetEnumerator();
                while (a.MoveNext() && b.MoveNext())
                {
                    double va = Convert.ToDouble(a.Current);
                    double vb = Convert.ToDouble(b.Current);
                    double scale = Math.Max(Math.Abs(va), Math.Abs(vb));
                    double diff = scale > 0 ? Math.Abs(va - vb) / scale : Math.Abs(va - vb);
                    if (diff > worst)
                    {
                        worst = diff;
                        worstIndex = flatIndex;
                    }
                    flatIndex++;
                }
            }

            return worst;
        }

        private static int UlpDifference(object a, object b)
        {
            switch (a)
            {
                case float fa:
                {
                    float fb = (float)b;
                    if (float.IsNaN(fa) || float.IsNaN(fb))
                    {
                        return float.IsNaN(fa) && float.IsNaN(fb) ? 0 : int.MaxValue;
                    }
                    long ia = Ordered(BitConverter.SingleToInt32Bits(fa) & 0x7FFFFFFFL,
                                      BitConverter.SingleToInt32Bits(fa) < 0);
                    long ib = Ordered(BitConverter.SingleToInt32Bits(fb) & 0x7FFFFFFFL,
                                      BitConverter.SingleToInt32Bits(fb) < 0);
                    return (int)Math.Min(int.MaxValue, Math.Abs(ia - ib));
                }
                case double da:
                {
                    double db = (double)b;
                    if (double.IsNaN(da) || double.IsNaN(db))
                    {
                        return double.IsNaN(da) && double.IsNaN(db) ? 0 : int.MaxValue;
                    }
                    long ia = Ordered(BitConverter.DoubleToInt64Bits(da) & long.MaxValue,
                                      BitConverter.DoubleToInt64Bits(da) < 0);
                    long ib = Ordered(BitConverter.DoubleToInt64Bits(db) & long.MaxValue,
                                      BitConverter.DoubleToInt64Bits(db) < 0);
                    return (int)Math.Min(int.MaxValue, Math.Abs(ia - ib));
                }
                default:
                    // Integer images: any difference at all is a difference.
                    return Convert.ToInt64(a) == Convert.ToInt64(b) ? 0 : int.MaxValue;
            }
        }

        /// <summary>Map a float's sign-magnitude bits onto a monotonically ordered
        /// integer, so that subtracting two of them counts the representable values
        /// between them. Negative zero orders onto positive zero, which is what
        /// comparing them as numbers means.</summary>
        private static long Ordered(long magnitude, bool negative)
            => negative ? -magnitude : magnitude;

        private static string DumpHeader(Header h)
        {
            using (var sw = new StringWriter())
            {
                h.DumpHeader(sw);
                return sw.ToString();
            }
        }
    }
}
