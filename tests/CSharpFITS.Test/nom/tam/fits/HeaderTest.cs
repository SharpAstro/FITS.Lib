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
        /// A header's stream for these tests. It may be read at its end a few times and then refuses: a reader that
        /// keeps asking for more after the end fails at once with this stream's own exception, where over a plain
        /// stream it would spin and the test would never end. The refusal has to be made HERE and not in the stream
        /// beneath: <see cref="BufferedDataStream"/> answers any failure of the stream it wraps with "0 bytes read".
        /// It can also hand its bytes over a few at a time, as a pipe does, and sit on a stream that cannot seek.
        /// </summary>
        private sealed class TestStream : BufferedDataStream
        {
            private readonly int _chunk;
            private int _left;
            private int _readsAtTheEnd;

            public TestStream(byte[] bytes, int chunk = int.MaxValue, bool seekable = true)
                : base(seekable ? new MemoryStream(bytes, false) : new NoSeek(bytes))
            {
                _chunk = chunk;
                _left = bytes.Length;
            }

            public override int Read(byte[] buf, int offset, int size)
            {
                // The stream beneath gives all it is asked for or nothing, so a short tail is asked for by its size.
                int take = Math.Min(Math.Min(size, _chunk), _left);
                int read = take > 0 ? base.Read(buf, offset, take) : 0;
                _left -= read;
                if (read == 0 && size > 0 && ++_readsAtTheEnd > 8)
                {
                    throw new InvalidOperationException("read at the end of the stream again and again");
                }
                return read;
            }
        }

        /// <summary>Bytes that can only be read forward, and that refuse to be read at their end again and again.</summary>
        private sealed class NoSeek : Stream
        {
            private readonly MemoryStream _bytes;
            private int _readsAtTheEnd;

            public NoSeek(byte[] bytes)
            {
                _bytes = new MemoryStream(bytes, false);
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public override int Read(byte[] buffer, int offset, int count)
            {
                int read = _bytes.Read(buffer, offset, count);
                if (read == 0 && count > 0 && ++_readsAtTheEnd > 8)
                {
                    throw new InvalidOperationException("read at the end of the stream again and again");
                }
                return read;
            }

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        /// <summary><paramref name="bytes"/> of cards, the first three a header's own; with its END card and the
        /// padding of its last block when <paramref name="withEnd"/>, and <paramref name="padding"/> says how much of that.</summary>
        private static byte[] Cards(int bytes, bool withEnd, int padding = int.MaxValue, string first = "SIMPLE  =                    T")
        {
            var text = new System.Text.StringBuilder();
            text.Append(first.PadRight(80));
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
                text.Append(' ', Math.Min(padding, (2880 - text.Length % 2880) % 2880));
            }
            return System.Text.Encoding.ASCII.GetBytes(text.ToString());
        }

        /// <summary>
        /// A header whose stream ends before its END card is a truncated file (tianwen#1529). It used to add the
        /// last card it had read again on every pass and never return. Cut after three cards, and at the end of a
        /// whole block, which is how a cache that held one block too few of a header cut it.
        /// </summary>
        [TestCase(240)]
        [TestCase(2880)]
        public void AHeaderThatEndsBeforeItsEndCardIsATruncatedFile(int bytes)
        {
            Assert.Throws<TruncatedFileException>(() => Header.ReadHeader(new TestStream(Cards(bytes, withEnd: false))));
        }

        /// <summary>
        /// Cut in the MIDDLE of a card. Over a stream that gives all it is asked for or nothing this is the case
        /// above (the ten bytes are lost and the read returns nothing), so the stream here hands over what it has:
        /// ten bytes of the fourth card arrive, and then the end.
        /// </summary>
        [Test]
        public void AHeaderCutInTheMiddleOfACardIsATruncatedFile()
        {
            Assert.Throws<TruncatedFileException>(() => Header.ReadHeader(new TestStream(Cards(250, withEnd: false), chunk: 80)));
        }

        /// <summary>The same cards with their END are a header, through the header's own read and through
        /// <see cref="Fits"/>; and a stream with no card at all is still the end of the file, not a truncated one.</summary>
        [Test]
        public void AHeaderWithItsEndCardIsReadAndAnEmptyStreamIsTheEndOfTheFile()
        {
            Header header = Header.ReadHeader(new TestStream(Cards(240, withEnd: true)));
            Assert.That(header, Is.Not.Null);
            Assert.That(header.GetIntValue("BITPIX"), Is.EqualTo(16));

            BasicHDU hdu = new Fits(new MemoryStream(Cards(240, withEnd: true), false)).ReadHDUHeaderOnly();
            Assert.That(hdu, Is.Not.Null);
            Assert.That(hdu.Header.GetIntValue("NAXIS"), Is.EqualTo(0));

            Assert.That(Header.ReadHeader(new TestStream(new byte[0])), Is.Null);
        }

        /// <summary>
        /// A stream that cannot seek is read through the padding of the header's last block. One that ended inside
        /// that padding was read for ever: the loop waited for a read to return -1, and the end of a stream is 0.
        /// </summary>
        [Test]
        public void AHeaderWhoseLastBlockIsCutShortOnAStreamThatCannotSeekIsATruncatedFile()
        {
            byte[] cut = Cards(240, withEnd: true, padding: 100);
            Assert.Throws<TruncatedFileException>(() => Header.ReadHeader(new TestStream(cut, seekable: false)));

            // The whole block is read to its end, and the next header starts where it should.
            byte[] whole = Cards(240, withEnd: true);
            byte[] two = new byte[whole.Length * 2];
            whole.CopyTo(two, 0);
            Cards(240, withEnd: true, first: "XTENSION= 'IMAGE   '").CopyTo(two, whole.Length);
            var stream = new TestStream(two, seekable: false);
            Assert.That(Header.ReadHeader(stream).GetIntValue("BITPIX"), Is.EqualTo(16));
            Assert.That(Header.ReadHeader(stream).GetStringValue("XTENSION"), Is.EqualTo("IMAGE"));
        }

        /// <summary>
        /// A header's cards are counted as cards, not as reads. A stream that hands a card over in pieces was
        /// counted once per piece, the padding was worked out for a header that long, and the header after it was
        /// looked for in the wrong place.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void AHeaderReadInPiecesLeavesTheStreamAtItsNextBlock(bool seekable)
        {
            byte[] whole = Cards(240, withEnd: true);
            byte[] two = new byte[whole.Length * 2];
            whole.CopyTo(two, 0);
            Cards(240, withEnd: true, first: "XTENSION= 'IMAGE   '").CopyTo(two, whole.Length);

            var stream = new TestStream(two, chunk: 50, seekable: seekable);
            Assert.That(Header.ReadHeader(stream).GetIntValue("BITPIX"), Is.EqualTo(16));
            Assert.That(Header.ReadHeader(stream).GetStringValue("XTENSION"), Is.EqualTo("IMAGE"));
        }

        /// <summary>
        /// A skip on a stream that cannot seek is made by reading, and ends at the end of the stream with how far
        /// it got. It waited for a read to return -1 and never returned.
        /// </summary>
        [Test]
        public void ASkipPastTheEndOfAStreamThatCannotSeekReturnsHowFarItGot()
        {
            var stream = new BufferedDataStream(new NoSeek(new byte[3000]));
            Assert.That(stream.Seek(2880), Is.EqualTo(2880));
            Assert.That(stream.Seek(2880), Is.EqualTo(120), "only 120 bytes were left");
            Assert.That(stream.Seek(2880), Is.EqualTo(0));
        }

        /// <summary>
        /// What <see cref="Fits.Read"/> does with a file whose LAST header is cut: it throws, as the Java original
        /// does and as a cut data block does, and does not pass it off as junk after the last HDU (that tolerance is
        /// for bytes that are no header at all). The HDUs before the cut have been read and stay reachable.
        /// </summary>
        [Test]
        public void AFileWhoseLastHeaderIsCutThrowsFromReadAndKeepsTheHdusBeforeIt()
        {
            byte[] whole = Cards(240, withEnd: true);
            byte[] cut = Cards(240, withEnd: false, first: "XTENSION= 'IMAGE   '");
            byte[] file = new byte[whole.Length + cut.Length];
            whole.CopyTo(file, 0);
            cut.CopyTo(file, whole.Length);

            var fits = new Fits(new MemoryStream(file, false));
            Assert.Throws<TruncatedFileException>(() => fits.Read());
            Assert.That(fits.NumberOfHDUs, Is.EqualTo(1));
            Assert.That(fits.GetHDU(0).Header.GetIntValue("BITPIX"), Is.EqualTo(16));
        }

    }
}
