using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using nom.tam.util;

namespace nom.tam.fits
{
    /// <summary> summary description for HeaderTest.</summary>
    [TestFixture]
    public class HeaderTest
    {
        /// <summary> Check out header manipulation.</summary>
        [Test]
        public void TestSimpleImages()
        {
            float[][] img = new float[300][];
            for (int i = 0; i < 300; i++)
                img[i] = new float[300];

            Fits f = new Fits();

            ImageHDU hdu = (ImageHDU)Fits.MakeHDU(img);

            BufferedFile bf = new BufferedFile("ht1.fits", FileAccess.ReadWrite, FileShare.ReadWrite);
            f.AddHDU(hdu);
            f.Write(bf);
            bf.Close();

            f = new Fits("ht1.fits");
            hdu = (ImageHDU)f.GetHDU(0);
            Header hdr = hdu.Header;

            Assertion.AssertEquals("NAXIS", 2, hdr.GetIntValue("NAXIS"));
            Assertion.AssertEquals("NAXIS1", 300, hdr.GetIntValue("NAXIS1"));
            Assertion.AssertEquals("NAXIS2", 300, hdr.GetIntValue("NAXIS2"));
            Assertion.AssertEquals("NAXIS2a", 300, hdr.GetIntValue("NAXIS2", -1));
            Assertion.AssertEquals("NAXIS3", -1, hdr.GetIntValue("NAXIS3", -1));

            Assertion.AssertEquals("BITPIX", -32, hdr.GetIntValue("BITPIX"));


            Cursor c = hdr.GetCursor();
            c.MoveNext();
            HeaderCard hc = (HeaderCard)((DictionaryEntry)c.Current).Value;
            Assertion.AssertEquals("SIMPLE_1", "SIMPLE", hc.Key);

            c.MoveNext();
            hc = (HeaderCard)((DictionaryEntry)c.Current).Value;
            Assertion.AssertEquals("BITPIX_2", "BITPIX", hc.Key);

            c.MoveNext();
            hc = (HeaderCard)((DictionaryEntry)c.Current).Value;
            Assertion.AssertEquals("NAXIS_3", "NAXIS", hc.Key);

            c.MoveNext();
            hc = (HeaderCard)((DictionaryEntry)c.Current).Value;
            Assertion.AssertEquals("NAXIS1_4", "NAXIS1", hc.Key);

            c.MoveNext();
            hc = (HeaderCard)((DictionaryEntry)c.Current).Value;
            Assertion.AssertEquals("NAXIS2_5", "NAXIS2", hc.Key);
            f.Close();
        }

        [Test]
        public void TestCursor()
        {
            Fits f = new Fits("testdocs/ht1.fits");
            ImageHDU hdu = (ImageHDU)f.GetHDU(0);
            Header hdr = hdu.Header;
            Cursor c = hdr.GetCursor();

            c.Key = "XXX";
            c.Add("CTYPE1", new HeaderCard("CTYPE1", "GLON-CAR", "Galactic Longitude"));
            c.Add("CTYPE2", new HeaderCard("CTYPE2", "GLAT-CAR", "Galactic Latitude"));

            c.Key = "CTYPE1";  // Move before CTYPE1
            c.Add("CRVAL1", new HeaderCard("CRVAL1", 0f, "Longitude at reference"));

            c.Key = "CTYPE2"; // Move before CTYPE2
            c.Add("CRVAL2", new HeaderCard("CRVAL2", -90f, "Latitude at reference"));

            c.Key = "CTYPE1";  // Just practicing moving around!!
            c.Add("CRPIX1", new HeaderCard("CRPIX1", 150.0, "Reference Pixel X"));

            c.Key = "CTYPE2";
            c.Add("CRPIX2", new HeaderCard("CRPIX2", 0f, "Reference pixel Y"));
            c.Add("INV2", new HeaderCard("INV2", true, "Invertible axis"));
            c.Add("SYM2", new HeaderCard("SYM2", "YZ SYMMETRIC", "Symmetries..."));

            Assertion.AssertEquals("CTYPE1", "GLON-CAR", hdr.GetStringValue("CTYPE1"));
            Assertion.AssertEquals("CRPIX2", 0f, hdr.GetDoubleValue("CRPIX2", -2f));


            c.Key = "CRVAL1";
            HeaderCard hc = (HeaderCard)((DictionaryEntry)c.Current).Value;
            c.MoveNext();
            Assertion.AssertEquals("CRVAL1_c", "CRVAL1", hc.Key);

            hc = (HeaderCard)((DictionaryEntry)c.Current).Value;
            c.MoveNext();
            Assertion.AssertEquals("CRPIX1_c", "CRPIX1", hc.Key);

            hc = (HeaderCard)((DictionaryEntry)c.Current).Value;
            c.MoveNext();
            Assertion.AssertEquals("CTYPE1_c", "CTYPE1", hc.Key);

            hc = (HeaderCard)((DictionaryEntry)c.Current).Value;
            c.MoveNext();
            Assertion.AssertEquals("CRVAL2_c", "CRVAL2", hc.Key);

            hc = (HeaderCard)((DictionaryEntry)c.Current).Value;
            c.MoveNext();
            Assertion.AssertEquals("CRPIX2_c", "CRPIX2", hc.Key);

            hc = (HeaderCard)((DictionaryEntry)c.Current).Value;
            c.MoveNext();
            Assertion.AssertEquals("INV2_c", "INV2", hc.Key);

            hc = (HeaderCard)((DictionaryEntry)c.Current).Value;
            c.MoveNext();
            Assertion.AssertEquals("SYM2_c", "SYM2", hc.Key);

            hc = (HeaderCard)((DictionaryEntry)c.Current).Value;
            c.MoveNext();
            Assertion.AssertEquals("CTYPE2_c", "CTYPE2", hc.Key);


            hdr.FindCard("CRPIX1");
            hdr.AddValue("INTVAL1", 1, "An integer value");
            hdr.AddValue("LOG1", true, "A true value");
            hdr.AddValue("LOGB1", false, "A false value");
            hdr.AddValue("FLT1", 1.34, "A float value");
            hdr.AddValue("FLT2", -1.234567890e-134, "A very long float");
            hdr.AddValue("COMMENT", null, "Comment after flt2");


            c.Key = "INTVAL1";

            hc = (HeaderCard)((DictionaryEntry)c.Current).Value;
            c.MoveNext();
            Assertion.AssertEquals("INTVAL1", "INTVAL1", hc.Key);

            hc = (HeaderCard)((DictionaryEntry)c.Current).Value;
            c.MoveNext();
            Assertion.AssertEquals("LOG1", "LOG1", hc.Key);

            hc = (HeaderCard)((DictionaryEntry)c.Current).Value;
            c.MoveNext();
            Assertion.AssertEquals("LOGB1", "LOGB1", hc.Key);

            hc = (HeaderCard)((DictionaryEntry)c.Current).Value;
            c.MoveNext();
            Assertion.AssertEquals("FLT1", "FLT1", hc.Key);

            hc = (HeaderCard)((DictionaryEntry)c.Current).Value;
            c.MoveNext();
            Assertion.AssertEquals("FLT2", "FLT2", hc.Key);

            c.MoveNext(); // Skip comment
            hc = (HeaderCard)((DictionaryEntry)c.Current).Value;
            c.MoveNext();
            Assertion.AssertEquals("CRPIX1x", "CRPIX1", hc.Key);

            Assertion.AssertEquals("FLT1", 1.34, hdr.GetDoubleValue("FLT1", 0));


            c.Key = "FLT1";
            c.Remove();
            Assertion.AssertEquals("FLT1", 0f, hdr.GetDoubleValue("FLT1", 0));


            c.Key = "LOGB1";
            hc = (HeaderCard)((DictionaryEntry)c.Current).Value;
            Assertion.AssertEquals("AftDel1", "LOGB1", hc.Key);
            c.MoveNext();

            hc = (HeaderCard)((DictionaryEntry)c.Current).Value;
            Assertion.AssertEquals("AftDel2", "FLT2", hc.Key);
            c.MoveNext();

            hc = (HeaderCard)((DictionaryEntry)c.Current).Value;
            Assertion.AssertEquals("AftDel3", "Comment after flt2", hc.Comment);
            c.MoveNext();

            f.Close();
        }

        [Test]
        public void TestBadHeader()
        {
            Fits f = new Fits("testdocs/ht1.fits");
            ImageHDU hdu = (ImageHDU)f.GetHDU(0);
            Header hdr = hdu.Header;
            Cursor c = hdr.GetCursor();

            c = hdr.GetCursor();
            c.MoveNext();
            c.MoveNext();
            c.Remove();
            bool thrown = false;
            try
            {
                hdr.Rewrite();
            }
            catch (Exception)
            {
                thrown = true;
            }

            Assertion.AssertEquals("BITPIX delete", true, thrown);
            f.Close();

        }

        [Test]
        public void TestRewrite()
        {

            // Should be rewriteable until we add enough cards to
            // start a new block.

            Fits f = new Fits("testdocs/ht1.fits");
            ImageHDU hdu = (ImageHDU)f.GetHDU(0);
            Header hdr = hdu.Header;
            Cursor c = hdr.GetCursor();
            c.MoveNext();

            int nc = hdr.NumberOfCards;
            int nb = (nc - 1) / 36;

            while (hdr.Rewriteable)
            {
                int nbx = (hdr.NumberOfCards - 1) / 36;
                Assertion.AssertEquals($"Rewrite:{nbx}", nb == nbx, hdr.Rewriteable);
                c.Add(new HeaderCard($"DUMMY{nbx}", null, null));
            }
            f.Close();
        }

        /// <summary>
        /// A header's stream that may be read at its end a few times and then refuses. A reader that keeps asking
        /// for more after the end fails at once with this stream's own exception, where over a plain stream it
        /// would spin and the test would never end. The refusal has to be made HERE and not in the stream beneath:
        /// <see cref="BufferedDataStream"/> answers any failure of the stream it wraps with "0 bytes read".
        /// </summary>
        private sealed class EndsOnlyAFewTimes : BufferedDataStream
        {
            private int _readsAtTheEnd;

            public EndsOnlyAFewTimes(byte[] bytes) : base(new MemoryStream(bytes, false))
            {
            }

            public override int Read(byte[] buf, int offset, int size)
            {
                int read = base.Read(buf, offset, size);
                if (read == 0 && size > 0 && ++_readsAtTheEnd > 8)
                {
                    throw new InvalidOperationException("read at the end of the stream again and again");
                }
                return read;
            }
        }

        private static byte[] Cards(int bytes, bool withEnd)
        {
            var text = new System.Text.StringBuilder();
            text.Append("SIMPLE  =                    T".PadRight(80));
            text.Append("BITPIX  =                   16".PadRight(80));
            text.Append("NAXIS   =                    0".PadRight(80));
            for (int i = 0; text.Length < bytes; i++)
            {
                text.Append($"CARD{i,-4}=                    {i % 10}".PadRight(80));
            }
            text.Length = bytes;
            if (withEnd)
            {
                text.Append("END".PadRight(80));
                text.Append(' ', (2880 - text.Length % 2880) % 2880);
            }
            return System.Text.Encoding.ASCII.GetBytes(text.ToString());
        }

        /// <summary>
        /// A header whose stream ends before its END card is a truncated file (tianwen#1529). It used to add the
        /// last card it had read again on every pass and never return. The lengths: cut after three cards, cut
        /// in the middle of a card, and cut at the end of a whole block, which is how a cache that held one
        /// block too few of a header cut it.
        /// </summary>
        [TestCase(240)]
        [TestCase(250)]
        [TestCase(2880)]
        public void AHeaderThatEndsBeforeItsEndCardIsATruncatedFile(int bytes)
        {
            Assert.Throws<TruncatedFileException>(() => Header.ReadHeader(new EndsOnlyAFewTimes(Cards(bytes, withEnd: false))));
        }

        /// <summary>The same cards with their END are a header, through the header's own read and through
        /// <see cref="Fits"/>; and a stream with no card at all is still the end of the file, not a truncated one.</summary>
        [Test]
        public void AHeaderWithItsEndCardIsReadAndAnEmptyStreamIsTheEndOfTheFile()
        {
            Header header = Header.ReadHeader(new EndsOnlyAFewTimes(Cards(240, withEnd: true)));
            Assert.That(header, Is.Not.Null);
            Assert.That(header.GetIntValue("BITPIX"), Is.EqualTo(16));

            BasicHDU hdu = new Fits(new MemoryStream(Cards(240, withEnd: true), false)).ReadHDUHeaderOnly();
            Assert.That(hdu, Is.Not.Null);
            Assert.That(hdu.Header.GetIntValue("NAXIS"), Is.EqualTo(0));

            Assert.That(Header.ReadHeader(new EndsOnlyAFewTimes(new byte[0])), Is.Null);
        }

    }
}
