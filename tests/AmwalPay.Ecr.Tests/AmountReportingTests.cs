using AmwalPay.Ecr.Internal;
using Xunit;

namespace AmwalPay.Ecr.Tests
{
    /// <summary>
    /// Amounts go out as minor units and must come back as major ones. A caller
    /// that shows what it is given should never see <c>000000000365</c> where
    /// 0.365 was taken.
    /// </summary>
    public class AmountReportingTests
    {
        [Fact]
        public void AnAmountIsReportedInMajorUnits()
        {
            Assert.Equal("0.365", EcrWireReaders.MajorUnits("000000000365", 3));
            Assert.Equal("1.234", EcrWireReaders.MajorUnits("000000001234", 3));
            Assert.Equal("961.100", EcrWireReaders.MajorUnits("000000961100", 3));
        }

        [Fact]
        public void TheCurrencysDecimalPlacesDecideWhereThePointFalls()
        {
            Assert.Equal("1.23", EcrWireReaders.MajorUnits("000000000123", 2));
            Assert.Equal("123", EcrWireReaders.MajorUnits("000000000123", 0));
        }

        [Fact]
        public void ZeroReadsAsZeroNotAsThePaddingItArrivedIn()
        {
            Assert.Equal("0.000", EcrWireReaders.MajorUnits("000000000000", 3));
        }

        [Fact]
        public void AnAbsentAmountStaysAbsent()
        {
            Assert.Equal("", EcrWireReaders.MajorUnits("", 3));
        }

        [Fact]
        public void AnAmountThatIsNotMinorUnitsIsPassedThroughUntouched()
        {
            // A terminal that sends "1.234" is already reporting major units;
            // converting again would report 0.001234 and be silently wrong.
            Assert.Equal("1.234", EcrWireReaders.MajorUnits("1.234", 3));
        }

        [Theory]
        [InlineData("5", 3, "0.005")]
        [InlineData("000000000005", 3, "0.005")]
        [InlineData("-500", 2, "-5.00")]
        [InlineData("-5", 3, "-0.005")]
        [InlineData("99999999999999999999", 3, "99999999999999999.999")]
        public void SmallNegativeAndVeryLargeAmountsKeepEveryDigit(string minor, int digits, string expected)
        {
            // The C# reader parses with BigInteger; a value wider than long must
            // not lose precision on the way to text.
            Assert.Equal(expected, EcrWireReaders.MajorUnits(minor, digits));
        }
    }
}
