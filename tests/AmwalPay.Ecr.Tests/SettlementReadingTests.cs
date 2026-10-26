using System.Linq;
using AmwalPay.Ecr.Internal;
using Xunit;

namespace AmwalPay.Ecr.Tests
{
    /// <summary>
    /// A settlement answers in four shapes — closed with totals, nothing to close,
    /// left open, refused before it started — and a till decides what to tell the
    /// cashier from which one it got.
    /// </summary>
    public class SettlementReadingTests
    {
        [Fact]
        public void AClosedBatchReadsItsTotalsInMajorUnits()
        {
            var answer = Wire.Obj(
                """
                {
                  "success": true,
                  "responseCode": "00",
                  "message": "Settlement successful",
                  "data": {
                    "merchantReference": "SET01",
                    "settlementStatus": "SETTLED",
                    "batchId": "00000007",
                    "currency": "OMR",
                    "startTime": "20261020080000",
                    "endTime": "20261020130509",
                    "totalTransactions": 4,
                    "netAmount": "000000012500",
                    "totals": {
                      "SALE": { "count": 3, "amount": "000000014750" },
                      "REFUND": { "count": 1, "amount": "000000002250" },
                      "VOID": { "count": 0, "amount": "000000000000" },
                      "PRE_AUTH": { "count": 0, "amount": "000000000000" },
                      "CAPTURE": { "count": 0, "amount": "000000000000" }
                    }
                  }
                }
                """);

            var settled = Assert.IsType<EcrSettlement.Settled>(answer.ToSettlement("SET01", 3));
            Assert.Equal("SET01", settled.MerchantReference);
            Assert.Equal("00000007", settled.BatchId);
            Assert.Equal("OMR", settled.Currency);
            Assert.Equal("20261020130509", settled.EndTime);
            Assert.Equal(4, settled.TotalTransactions);
            Assert.Equal("12.500", settled.NetAmount);
            Assert.Equal(new EcrSettlementTotal(3, "14.750"), settled.Totals["SALE"]);
            Assert.Equal(new EcrSettlementTotal(1, "2.250"), settled.Totals["REFUND"]);
            Assert.Equal(new EcrSettlementTotal(0, "0.000"), settled.Totals["VOID"]);
        }

        [Fact]
        public void AMissingRowReadsAsZeroSoEveryTypeIsPresent()
        {
            var answer = Wire.Obj(
                """
                {
                  "success": true,
                  "responseCode": "00",
                  "message": "Settlement successful",
                  "data": {
                    "settlementStatus": "SETTLED",
                    "batchId": "00000008",
                    "totalTransactions": 1,
                    "netAmount": "000000001000",
                    "totals": { "SALE": { "count": 1, "amount": "000000001000" } }
                  }
                }
                """);

            var settled = Assert.IsType<EcrSettlement.Settled>(answer.ToSettlement("SET02", 3));
            Assert.Equal(new[] { "SALE", "REFUND", "VOID", "PRE_AUTH", "CAPTURE" }, settled.Totals.Keys.ToArray());
            Assert.Equal(new EcrSettlementTotal(0, "0.000"), settled.Totals["CAPTURE"]);
        }

        [Fact]
        public void AnEmptyBatchIsNothingToSettleNotAFailure()
        {
            var answer = Wire.Obj(
                """
                {
                  "success": true,
                  "responseCode": "00",
                  "message": "There are no transactions to settle",
                  "data": { "settlementStatus": "NO_TRANSACTIONS" }
                }
                """);

            var nothing = Assert.IsType<EcrSettlement.NothingToSettle>(answer.ToSettlement("SET03", 3));
            Assert.Equal("There are no transactions to settle", nothing.Message);
        }

        [Fact]
        public void AMismatchLeavesTheBatchOpenAndSaysHowManyWentUp()
        {
            var answer = Wire.Obj(
                """
                {
                  "success": false,
                  "responseCode": "91",
                  "message": "Counts differ",
                  "data": { "settlementStatus": "MISMATCH", "uploadedTransactions": 9 }
                }
                """);

            var open = Assert.IsType<EcrSettlement.NotSettled>(answer.ToSettlement("SET04", 3));
            Assert.Equal("MISMATCH", open.Status);
            Assert.Equal("91", open.ResponseCode);
            Assert.Equal(9, open.UploadedTransactions);
        }

        [Fact]
        public void ABusyTerminalRefusesWithNoSettlementStatus()
        {
            var answer = Wire.Obj(
                """
                {
                  "success": false,
                  "responseCode": "96",
                  "message": "A transaction is already in progress on this terminal"
                }
                """);

            var open = Assert.IsType<EcrSettlement.NotSettled>(answer.ToSettlement("SET05", 3));
            Assert.Equal("96", open.ResponseCode);
            Assert.Equal("", open.Status);
            Assert.Null(open.UploadedTransactions);
        }
    }
}
