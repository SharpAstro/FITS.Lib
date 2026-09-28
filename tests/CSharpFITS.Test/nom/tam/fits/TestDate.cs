using System;
using NUnit.Framework;

namespace nom.tam.fits
{
    [TestFixture]
    public class TestDate
    {
        [Test]
        public void DateTest()
        {
            Assertion.AssertEquals("t1", true, TestArg("20/09/79"));
            Assertion.AssertEquals("t2", true, TestArg("1997-07-25"));
            Assertion.AssertEquals("t3", true, TestArg("1987-06-05T04:03:02.01"));
            Assertion.AssertEquals("t4", true, TestArg("1998-03-10T16:58:34"));
            Assertion.AssertEquals("t5", true, TestArg(null));
            Assertion.AssertEquals("t6", true, TestArg("        "));

            Assertion.AssertEquals("t7", false, TestArg("20/09/"));
            Assertion.AssertEquals("t8", false, TestArg("/09/79"));
            Assertion.AssertEquals("t9", false, TestArg("09//79"));
            Assertion.AssertEquals("t10", false, TestArg("20/09/79/"));

            Assertion.AssertEquals("t11", false, TestArg("1997-07"));
            Assertion.AssertEquals("t12", false, TestArg("-07-25"));
            Assertion.AssertEquals("t13", false, TestArg("1997--07-25"));
            Assertion.AssertEquals("t14", false, TestArg("1997-07-25-"));

            Assertion.AssertEquals("t15", false, TestArg("5-Aug-1992"));
            Assertion.AssertEquals("t16", false, TestArg("28/02/91 16:32:00"));
            Assertion.AssertEquals("t17", false, TestArg("18-Feb-1993"));
            Assertion.AssertEquals("t18", false, TestArg("nn/nn/nn"));
        }

        bool TestArg(String arg)
        {
            try
            {
                FitsDate fd = new FitsDate(arg);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// The old-style DD/MM/YY form pads a single-digit day with a space, so a real card reads
        /// ' 2/07/96'. Trim() reduces that to seven characters and the parser used to demand eight,
        /// which rejected it -- and BasicHDU then turned the rejection into a NullReferenceException.
        /// This is not a synthetic edge case: DATE-OBS = ' 2/07/96' is what NASA's own reference
        /// sample FOCx38i0101t_c0f.fits carries.
        /// </summary>
        [Test]
        public void ShortOldStyleDatesParse()
        {
            Assertion.AssertEquals("padded day, as written on the card", true, TestArg(" 2/07/96"));
            Assertion.AssertEquals("one-digit day", true, TestArg("2/07/96"));
            Assertion.AssertEquals("one-digit day and month", true, TestArg("2/7/96"));
        }

        /// <summary>
        /// Asserting the VALUE, not merely that nothing threw. DateTest only distinguishes accepted
        /// from rejected, so it would pass just as happily if the parser read the day as the month.
        /// </summary>
        [Test]
        public void OldStyleDatesParseToTheRightDay()
        {
            Assertion.AssertEquals("padded", new DateTime(1996, 7, 2), new FitsDate(" 2/07/96").ToDate());
            Assertion.AssertEquals("unpadded", new DateTime(1996, 7, 2), new FitsDate("2/07/96").ToDate());
            Assertion.AssertEquals("both short", new DateTime(1996, 7, 2), new FitsDate("2/7/96").ToDate());
            Assertion.AssertEquals("two digit", new DateTime(1979, 9, 20), new FitsDate("20/09/79").ToDate());
        }

        /// <summary>
        /// The fraction of a second is written with its leading zeros. It used to be the bare millisecond count, so a frame
        /// 43 ms past the second was written ".43", which every reader, this one included, takes for 430 ms. Found in the
        /// darks TianWen wrote: six of 157 carried a two-digit fraction.
        /// </summary>
        [Test]
        public void TheFractionIsWrittenWithItsLeadingZeros()
        {
            var second = new DateTime(2026, 9, 26, 16, 19, 22);
            Assertion.AssertEquals("43 ms", "2026-09-26T16:19:22.043", FitsDate.GetFitsDateString(second.AddMilliseconds(43)));
            Assertion.AssertEquals("5 ms", "2026-09-26T16:19:22.005", FitsDate.GetFitsDateString(second.AddMilliseconds(5)));
            Assertion.AssertEquals("430 ms", "2026-09-26T16:19:22.430", FitsDate.GetFitsDateString(second.AddMilliseconds(430)));
            Assertion.AssertEquals("a whole second", "2026-09-26T16:19:22.000", FitsDate.GetFitsDateString(second));
            Assertion.AssertEquals("below a millisecond", "2026-09-26T16:19:22.035228", FitsDate.GetFitsDateString(second.AddTicks(352_280)));
        }

        /// <summary>Every tick a DateTime holds survives a write and a read, the milliseconds under 100 included.</summary>
        [Test]
        public void ADateSurvivesAWriteAndARead()
        {
            foreach (var ticks in new long[] { 0, 1, 50_000, 352_280, 430_000, 9_990_000, 9_999_999 })
            {
                var epoch = new DateTime(2024, 12, 15, 12, 33, 50).AddTicks(ticks);
                Assertion.AssertEquals($"{ticks} ticks", epoch, new FitsDate(FitsDate.GetFitsDateString(epoch)).ToDate());
            }
        }

        /// <summary>
        /// A fraction is read to the tick, as SharpCap writes it, and as a decimal (".5" is half a second); a digit past the
        /// seventh, below a tick, is dropped.
        /// </summary>
        [Test]
        public void AFractionIsReadToTheTick()
        {
            var second = new DateTime(2022, 9, 3, 12, 9, 53);
            Assertion.AssertEquals("SharpCap's", second.AddTicks(352_280), new FitsDate("2022-09-03T12:09:53.0352280").ToDate());
            Assertion.AssertEquals("a decimal", second.AddMilliseconds(500), new FitsDate("2022-09-03T12:09:53.5").ToDate());
            Assertion.AssertEquals("below a tick", second.AddTicks(352_280), new FitsDate("2022-09-03T12:09:53.03522809").ToDate());
            Assertion.AssertEquals("as text", "2022-09-03T12:09:53.035228", new FitsDate("2022-09-03T12:09:53.0352280").ToString());
            Assertion.AssertEquals("no digits", false, TestArg("2022-09-03T12:09:53."));
        }

        /// <summary>
        /// The relaxed length and separator guards must not start accepting the malformed strings
        /// DateTest already pins as rejected -- a day of zero characters, or a month of zero.
        /// </summary>
        [Test]
        public void RelaxingTheGuardsStillRejectsMissingComponents()
        {
            Assertion.AssertEquals("no day", false, TestArg("/09/79"));
            Assertion.AssertEquals("no month", false, TestArg("09//79"));
            Assertion.AssertEquals("no year", false, TestArg("20/09/"));
            Assertion.AssertEquals("trailing separator", false, TestArg("20/09/79/"));
            Assertion.AssertEquals("not numeric", false, TestArg("nn/nn/nn"));
        }

    }
}
