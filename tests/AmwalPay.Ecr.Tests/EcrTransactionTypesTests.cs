using System;
using System.Linq;
using Xunit;

namespace AmwalPay.Ecr.Tests
{
    /// <summary>
    /// The flags a client's own form is driven from. They must match the
    /// terminal's rules exactly, so each is pinned per operation.
    /// </summary>
    public class EcrTransactionTypesTests
    {
        //                          type                          wire        display    amount stan   date   money  otherTerminal
        [Theory]
        [InlineData(EcrTransactionType.Sale,         "SALE",          "Sale",          true,  false, false, true,  false)]
        [InlineData(EcrTransactionType.Void,         "VOID",          "Void",          false, true,  false, true,  true)]
        [InlineData(EcrTransactionType.Refund,       "REFUND",        "Refund",        true,  true,  true,  true,  true)]
        [InlineData(EcrTransactionType.Inquiry,      "INQUIRY",       "Inquiry",       false, true,  true,  false, true)]
        [InlineData(EcrTransactionType.Receipt,      "RECEIPT",       "Receipt",       false, true,  true,  false, true)]
        [InlineData(EcrTransactionType.CloseReceipt, "CLOSE_RECEIPT", "Close receipt", false, false, false, false, false)]
        public void EachOperationDeclaresWhatTheCallerMustSupply(
            EcrTransactionType type,
            string wire,
            string display,
            bool requiresAmount,
            bool requiresStan,
            bool requiresDate,
            bool movesMoney,
            bool allowsOtherTerminal)
        {
            Assert.Equal(wire, type.MessageType());
            Assert.Equal(display, type.DisplayName());
            Assert.Equal(requiresAmount, type.RequiresAmount());
            Assert.Equal(requiresStan, type.RequiresOriginalStan());
            Assert.Equal(requiresDate, type.RequiresOriginalDate());
            Assert.Equal(movesMoney, type.MovesMoney());
            Assert.Equal(allowsOtherTerminal, type.AllowsOtherTerminal());
        }

        [Fact]
        public void EveryDefinedTypeIsCoveredByThePinnedTable()
        {
            // If an operation is added, this fails until its flags are pinned above.
            Assert.Equal(6, Enum.GetValues(typeof(EcrTransactionType)).Length);
        }

        [Fact]
        public void TheMenuOffersTheFourOperationsAnOperatorPicksInOrder()
        {
            Assert.Equal(
                new[]
                {
                    EcrTransactionType.Sale,
                    EcrTransactionType.Void,
                    EcrTransactionType.Refund,
                    EcrTransactionType.Inquiry,
                },
                EcrTransactionTypes.MenuOptions.ToArray());
        }

        [Fact]
        public void ReceiptAndCloseReceiptAreNeverOfferedAsMenuChoices()
        {
            Assert.DoesNotContain(EcrTransactionType.Receipt, EcrTransactionTypes.MenuOptions);
            Assert.DoesNotContain(EcrTransactionType.CloseReceipt, EcrTransactionTypes.MenuOptions);
        }

        [Fact]
        public void OnlyOperationsThatReadAreSafeToRepeat()
        {
            var reads = Enum.GetValues(typeof(EcrTransactionType))
                .Cast<EcrTransactionType>()
                .Where(type => !type.MovesMoney())
                .ToArray();

            Assert.Equal(
                new[]
                {
                    EcrTransactionType.Inquiry,
                    EcrTransactionType.Receipt,
                    EcrTransactionType.CloseReceipt,
                },
                reads);
        }

        [Fact]
        public void AnUndefinedValueIsRejectedRatherThanGivenAWireName()
        {
            var bogus = (EcrTransactionType)99;

            Assert.Throws<ArgumentOutOfRangeException>(() => bogus.MessageType());
            Assert.Throws<ArgumentOutOfRangeException>(() => bogus.DisplayName());
        }

        [Fact]
        public void TheNextStepWireValuesAreUnchanged()
        {
            Assert.Equal(NextStep.None, NextSteps.Of("NONE"));
            Assert.Equal(NextStep.None, NextSteps.Of(""));
            Assert.Equal(NextStep.None, NextSteps.Of("inquire_by_merchant_reference"));
            Assert.Equal(NextStep.InquireByMerchantReference, NextSteps.Of("INQUIRE_BY_MERCHANT_REFERENCE"));
        }

        [Fact]
        public void OnlyUnreachableMeansNothingWasSent()
        {
            Assert.False(new Failure.Unreachable("x").OutcomeUnknown);
            Assert.True(new Failure.Timeout("x").OutcomeUnknown);
            Assert.True(new Failure.Malformed("x").OutcomeUnknown);
            Assert.True(new Failure.ConnectionLost("x").OutcomeUnknown);
            Assert.True(new Failure.Unauthenticated("x").OutcomeUnknown);
        }
    }
}
