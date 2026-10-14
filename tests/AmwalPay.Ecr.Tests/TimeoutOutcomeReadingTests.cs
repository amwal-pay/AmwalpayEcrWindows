using System.Text.Json.Nodes;
using AmwalPay.Ecr.Internal;
using Xunit;

namespace AmwalPay.Ecr.Tests
{
    /// <summary>
    /// Web Service timeouts arrive as a false envelope that tells the till to
    /// inquire — never as a decline the operator could treat as "no money moved".
    /// </summary>
    public class TimeoutOutcomeReadingTests
    {
        private static JsonObject Json(string text) => JsonNode.Parse(text)!.AsObject();

        [Fact]
        public void NoResponseWithInquireNextStepIsATimeoutFailedCarryingTheReference()
        {
            var result = Json(
                """
                {
                  "success": false,
                  "responseCode": "NO_RESPONSE",
                  "message": "timeout",
                  "nextStep": "INQUIRE_BY_MERCHANT_REFERENCE"
                }
                """).ToEcrResult("ORD-TIMEOUT-01", minorUnitDigits: 3);

            var failed = Assert.IsType<EcrResult.Failed>(result);
            Assert.Equal("ORD-TIMEOUT-01", failed.MerchantReference);
            Assert.IsType<Failure.Timeout>(failed.Failure);
            Assert.Equal("timeout", failed.Failure.Message);
            Assert.Equal(NextStep.InquireByMerchantReference, failed.NextStep);
        }

        [Fact]
        public void OrdinaryDeclineWithoutInquireNextStepStaysDeclined()
        {
            var result = Json(
                """
                {
                  "success": false,
                  "responseCode": "51",
                  "message": "Declined",
                  "nextStep": "NONE"
                }
                """).ToEcrResult("ORD-DECLINE-01", minorUnitDigits: 3);

            var declined = Assert.IsType<EcrResult.Declined>(result);
            Assert.Equal("51", declined.ResponseCode);
            Assert.Equal(NextStep.None, declined.NextStep);
        }
    }
}
