using System;
using AmwalPay.Ecr.Internal;
using Xunit;

namespace AmwalPay.Ecr.Tests
{
    public class EcrMessageTests
    {
        [Fact]
        public void AmountIsCarriedAs12DigitsOfMinorUnits()
        {
            Assert.Equal("000000001234", EcrMessage.MinorUnits(1.234m, 3));
            Assert.Equal("000000000001", EcrMessage.MinorUnits(0.001m, 3));
            Assert.Equal("000000961100", EcrMessage.MinorUnits(961.100m, 3));
        }

        [Fact]
        public void AmountHonoursTheCurrencysDecimalPlaces()
        {
            // USD has two, so 1.23 is 123 cents rather than 1230.
            Assert.Equal("000000000123", EcrMessage.MinorUnits(1.23m, 2));
        }

        [Fact]
        public void AmountIsRoundedHalfUpToTheCurrencysPrecision()
        {
            Assert.Equal("000000001235", EcrMessage.MinorUnits(1.2345m, 3));
            Assert.Equal("000000001234", EcrMessage.MinorUnits(1.2344m, 3));
            Assert.Equal("000000000101", EcrMessage.MinorUnits(1.005m, 2));
            Assert.Equal("000000001235", EcrMessage.MinorUnits(1234.5m, 0));
        }

        [Fact]
        public void AnAbsentAmountIsTwelveZeros()
        {
            Assert.Equal("000000000000", EcrMessage.MinorUnits(null, 3));
        }

        [Fact]
        public void ReceiptNumberIsPaddedToTheWidthTheTerminalStores()
        {
            Assert.Equal("000024", EcrMessage.PaddedStan("24"));
            Assert.Equal("000024", EcrMessage.PaddedStan("000024"));
            Assert.Equal("123456", EcrMessage.PaddedStan("123456"));
        }

        [Fact]
        public void ReceiptNumberIgnoresAnythingThatIsNotADigit()
        {
            Assert.Equal("000024", EcrMessage.PaddedStan(" 24 "));
            Assert.Equal("", EcrMessage.PaddedStan("abc"));
            Assert.Equal("", EcrMessage.PaddedStan("0000"));
        }

        [Fact]
        public void ASaleCarriesAnAmountAndNoOriginal()
        {
            var json = Build(EcrTransactionType.Sale, amount: 1.234m).Json;

            Assert.Equal("SALE", json.Str("messageType"));
            Assert.Equal("000000001234", json.Str("amount"));
            Assert.False(json.ContainsKey("stan"), "a sale names no original transaction");
        }

        [Fact]
        public void AVoidCarriesTheOriginalButNoAmount()
        {
            // The void returns exactly what the original took, which only the
            // terminal knows: sending an amount would invite disagreement.
            var json = Build(EcrTransactionType.Void, originalStan: "24").Json;

            Assert.Equal("VOID", json.Str("messageType"));
            Assert.Equal("000024", json.Str("stan"));
            Assert.False(json.ContainsKey("amount"), "a void takes the original's amount");
        }

        [Fact]
        public void ARefundCarriesAmountOriginalAndDate()
        {
            var json = Build(
                EcrTransactionType.Refund,
                amount: 0.500m,
                originalStan: "24",
                originalDate: "20260806").Json;

            Assert.Equal("000000000500", json.Str("amount"));
            Assert.Equal("000024", json.Str("stan"));
            Assert.Equal("20260806", json.Str("originalTransactionDate"));
        }

        [Fact]
        public void FieldsThatDoNotApplyAreLeftOutRatherThanSentEmpty()
        {
            // The terminal reads a field's presence as meaning the operation uses
            // it, so an empty value is not the same as an absent one.
            var json = Build(EcrTransactionType.Sale, amount: 1m).Json;

            Assert.False(json.ContainsKey("stan"));
            Assert.False(json.ContainsKey("originalTerminalId"));
            Assert.False(json.ContainsKey("originalTransactionDate"));
        }

        [Fact]
        public void EveryMessageStatesTheProtocolVersion()
        {
            var json = Build(EcrTransactionType.Sale, amount: 1m).Json;
            Assert.Equal("1", json.Str("version"));
        }

        [Fact]
        public void ARequestWithNoReferenceOfItsOwnIsGivenADistinctOne()
        {
            var first = Build(EcrTransactionType.Sale, amount: 1m);
            var second = Build(EcrTransactionType.Sale, amount: 1m);

            Assert.Equal(12, first.MerchantReference.Length);
            Assert.Matches("^[0-9A-F]{12}$", first.MerchantReference);
            Assert.NotEqual(first.MerchantReference, second.MerchantReference);
        }

        [Fact]
        public void TheCallersOwnReferenceIsSentAsGiven()
        {
            // A till that already numbers its orders passes that number, so the
            // same string names the sale in its books and on the terminal.
            var message = Build(EcrTransactionType.Sale, amount: 1m, merchantReference: "ORDER-2026-0817-001");

            Assert.Equal("ORDER-2026-0817-001", message.MerchantReference);
            Assert.Equal("ORDER-2026-0817-001", message.Json.Str("merchantReference"));
        }

        [Fact]
        public void AReferenceOfOnlyWhitespaceIsTreatedAsNoneGiven()
        {
            var message = Build(EcrTransactionType.Sale, amount: 1m, merchantReference: "   ");

            Assert.Equal(12, message.MerchantReference.Length);
        }

        [Fact]
        public void SurroundingWhitespaceIsTrimmedOffAReference()
        {
            var message = Build(EcrTransactionType.Sale, amount: 1m, merchantReference: "  ORDER-1  ");

            Assert.Equal("ORDER-1", message.MerchantReference);
        }

        [Fact]
        public void AReferenceLongerThanTheTerminalCarriesIsRefused()
        {
            Assert.Throws<ArgumentException>(() =>
                Build(EcrTransactionType.Sale, amount: 1m, merchantReference: new string('X', 33)));
        }

        [Fact]
        public void AReferenceOfExactlyTheMaximumLengthIsAccepted()
        {
            var message = Build(EcrTransactionType.Sale, amount: 1m, merchantReference: new string('X', 32));

            Assert.Equal(32, message.MerchantReference.Length);
        }

        [Theory]
        [InlineData("ORDER&1")]
        [InlineData("ORDER=1")]
        [InlineData("ORDER 1")]
        [InlineData("ORDER\n1")]
        [InlineData("ORDERé1")]
        public void AReferenceCarryingAHashSeparatorIsRefused(string reference)
        {
            // '&' and '=' separate fields when a message is signed; a reference
            // holding one could make two different messages hash alike.
            Assert.Throws<ArgumentException>(() =>
                Build(EcrTransactionType.Sale, amount: 1m, merchantReference: reference));
        }

        [Fact]
        public void AnInquiryCanNameItsOriginalByReferenceInsteadOfReceiptNumber()
        {
            // The only identifier a till still holds when the answer carrying the
            // receipt number never arrived.
            var json = Build(EcrTransactionType.Inquiry, originalReference: "ORD-88231").Json;

            Assert.Equal("ORD-88231", json.Str("originalMerchantReference"));
        }

        [Theory]
        [InlineData(EcrTransactionType.Void)]
        [InlineData(EcrTransactionType.Refund)]
        public void AnOperationThatMovesMoneyNeverNamesItsOriginalByReference(EcrTransactionType type)
        {
            // A void goes by the number printed on the receipt, because that is
            // what the operator standing at the terminal is reading.
            var json = Build(
                type,
                amount: 1m,
                originalStan: "24",
                originalDate: "20260817",
                originalReference: "ORD-88231").Json;

            Assert.False(json.ContainsKey("originalMerchantReference"), $"{type} carried a reference");
        }

        [Fact]
        public void TheReferenceLookedUpIsNotTheReferenceNamingTheInquiry()
        {
            var message = Build(
                EcrTransactionType.Inquiry,
                originalReference: "ORD-88231",
                merchantReference: "LOOKUP-1");

            Assert.Equal("LOOKUP-1", message.MerchantReference);
            Assert.Equal("ORD-88231", message.Json.Str("originalMerchantReference"));
        }

        [Fact]
        public void TheOriginalTerminalIsSentOnlyWhenNamedAndOnlyForOperationsOnAnEarlierTransaction()
        {
            var voidJson = Build(EcrTransactionType.Void, originalStan: "24", originalTerminalId: "31629").Json;
            var saleJson = Build(EcrTransactionType.Sale, amount: 1m, originalTerminalId: "31629").Json;

            Assert.Equal("31629", voidJson.Str("originalTerminalId"));
            Assert.False(saleJson.ContainsKey("originalTerminalId"));
        }

        [Fact]
        public void TheTimestampIsFourteenDigits()
        {
            var json = Build(EcrTransactionType.Sale, amount: 1m).Json;

            Assert.Matches("^[0-9]{14}$", json.Str("transactionDateTime"));
        }

        [Fact]
        public void TheConfigIdentityAndCurrencyAreCarried()
        {
            var json = EcrMessage.Build(
                EcrTransactionType.Sale,
                new EcrConfig { EcrId = "TILL-7", CurrencyCode = "840" },
                "TW9",
                1m,
                "",
                "",
                "").Json;

            Assert.Equal("TILL-7", json.Str("ecrId"));
            Assert.Equal("840", json.Str("currencyCode"));
            Assert.Equal("TW9", json.Str("terminalSerial"));
        }

        private static EcrMessage Build(
            EcrTransactionType type,
            decimal? amount = null,
            string originalStan = "",
            string originalTerminalId = "",
            string originalDate = "",
            string originalReference = "",
            string merchantReference = "") =>
            EcrMessage.Build(
                type: type,
                config: new EcrConfig(),
                terminalSerial: "TW1",
                amount: amount,
                originalStan: originalStan,
                originalTerminalId: originalTerminalId,
                originalDate: originalDate,
                originalReference: originalReference,
                merchantReference: merchantReference);
    }
}
