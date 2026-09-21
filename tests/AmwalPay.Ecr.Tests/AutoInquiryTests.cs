using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace AmwalPay.Ecr.Tests
{
    /// <summary>
    /// The recovery path, driven through scripted channels rather than a socket
    /// (the same scenarios also run over real TCP in <see cref="DeviationTests"/>).
    /// Returning null from the script is a connection dropped without a reply.
    /// </summary>
    public class AutoInquiryTests
    {
        private const string FoundTransaction =
            """
            {"success":true,"responseCode":"00","message":"Transaction found",
             "data":{
               "transactionId":"abc-123","stan":"000131","status":"Approved",
               "amount":"1.234","rrn":"622908275947","authCode":"751486",
               "transactionTypeDisplayName":"Purchase"},"errorList":[]}
            """;

        private static EcrTerminal Ecr(FakeChannel channel, bool autoInquire = true) =>
            new EcrTerminal(
                channel,
                "TW1",
                new EcrConfig
                {
                    ConnectTimeout = TimeSpan.FromSeconds(2),
                    ResponseTimeout = TimeSpan.FromSeconds(2),
                    AutoInquireOnFailure = autoInquire,
                });

        [Fact]
        public async Task ASaleWhoseAnswerNeverArrivesIsFollowedByAnInquiry()
        {
            // The terminal takes the payment and the connection dies before it can
            // say so. This is the case a till would otherwise resolve by charging
            // the cardholder a second time.
            var channel = new FakeChannel((_, connection) => connection == 0 ? null : FoundTransaction);

            var result = await Ecr(channel).SaleAsync(1.234m, merchantReference: "ORD-1");

            var failed = Assert.IsType<EcrResult.Failed>(result);
            Assert.IsType<Failure.ConnectionLost>(failed.Failure);
            Assert.True(failed.Settled, "but the outcome is no longer unknown");

            var found = Assert.IsType<EcrInquiry.Found>(failed.Recovered);
            Assert.Equal("Approved", found.Transaction.Status);
            Assert.Equal("622908275947", found.Transaction.Rrn);
        }

        [Fact]
        public async Task TheFollowUpAsksByTheReferenceTheSaleWasSentWith()
        {
            var channel = new FakeChannel((_, connection) => connection == 0 ? null : FoundTransaction);

            await Ecr(channel).SaleAsync(1.234m, merchantReference: "ORD-88231");

            Assert.Equal(2, channel.Requests.Count);
            Assert.Contains("\"messageType\":\"SALE\"", channel.Requests[0]);
            Assert.Contains("\"messageType\":\"INQUIRY\"", channel.Requests[1]);
            Assert.Contains(
                "\"originalMerchantReference\":\"ORD-88231\"",
                channel.Requests[1]);
        }

        [Fact]
        public async Task AnUnreachableTerminalIsNotAskedTwice()
        {
            // Nothing was sent, so there is nothing to ask about, and asking would
            // only make the caller wait through a second connection that fails for
            // the same reason.
            var channel = new FakeChannel((_, __) => throw new IOException("Connection refused"));

            var result = await Ecr(channel).SaleAsync(1.234m);

            var failed = Assert.IsType<EcrResult.Failed>(result);
            Assert.IsType<Failure.Unreachable>(failed.Failure);
            Assert.False(failed.Failure.OutcomeUnknown, "nothing was attempted");
            Assert.Null(failed.Recovered);
            Assert.Single(channel.Requests);
            Assert.Contains("Connection refused", failed.Failure.Message);
            Assert.Contains("(fake:9100)", failed.Failure.Message);
        }

        [Fact]
        public async Task ATransactionTheTerminalHasNoRecordOfStaysUnknown()
        {
            // NotFound is not proof that nothing happened: the terminal may have
            // been reset, or the record may have aged out.
            const string notFound = """{"responseCode":"25","responseMessage":"Not found","approved":false}""";
            var channel = new FakeChannel((_, connection) => connection == 0 ? null : notFound);

            var result = await Ecr(channel).SaleAsync(1.234m, merchantReference: "ORD-1");

            var failed = Assert.IsType<EcrResult.Failed>(result);
            Assert.IsType<EcrInquiry.NotFound>(failed.Recovered);
            Assert.False(failed.Settled, "still unknown, and still not a decline");
        }

        [Fact]
        public async Task AFollowUpThatFailsLeavesTheOutcomeExactlyAsUnknown()
        {
            // The terminal is gone entirely by the time the inquiry is attempted.
            var channel = FakeChannel.Always(null);

            var result = await Ecr(channel).SaleAsync(1.234m, merchantReference: "ORD-1");

            var failed = Assert.IsType<EcrResult.Failed>(result);
            Assert.False(failed.Settled);
            Assert.IsType<EcrInquiry.Failed>(failed.Recovered);
            Assert.Equal(2, channel.Requests.Count);
        }

        [Fact]
        public async Task ItCanBeTurnedOffForATillThatReconcilesItsOwnWay()
        {
            var channel = new FakeChannel((_, connection) => connection == 0 ? null : FoundTransaction);

            var result = await Ecr(channel, autoInquire: false)
                .SaleAsync(1.234m, merchantReference: "ORD-1");

            var failed = Assert.IsType<EcrResult.Failed>(result);
            Assert.Null(failed.Recovered);
            Assert.Single(channel.Requests);
        }

        [Fact]
        public async Task AnApprovedSaleIsNeverFollowedByAnything()
        {
            const string approved =
                """
                {"responseCode":"00","responseMessage":"Approved","approved":true,
                 "amount":"000000001234","rrn":"622908275947","authCode":"751486"}
                """;
            var channel = FakeChannel.Always(approved);

            var result = await Ecr(channel).SaleAsync(1.234m, merchantReference: "ORD-1");

            var ok = Assert.IsType<EcrResult.Approved>(result);
            Assert.Equal("1.234", ok.Amount);
            Assert.Single(channel.Requests);
        }

        [Fact]
        public async Task ADeclinedSaleIsNeverFollowedByAnything()
        {
            var channel = FakeChannel.Always(
                """{"responseCode":"51","responseMessage":"DECLINED","approved":false}""");

            var result = await Ecr(channel).SaleAsync(1.234m, merchantReference: "ORD-1");

            Assert.IsType<EcrResult.Declined>(result);
            Assert.Single(channel.Requests);
        }

        [Fact]
        public async Task AUnifiedSaleEnvelopeIsReadIntoAnApproval()
        {
            const string approvedSale =
                """
                {
                  "success": true,
                  "responseCode": "00",
                  "message": "Approved",
                  "data": {
                    "merchantReference": "EA5C14BDB0A2",
                    "systemTraceNr": "000008",
                    "rrn": "623807390109",
                    "authCode": "713984",
                    "amount": "0.111",
                    "authorizeAmount": "0.111",
                    "cardMask": "543173xxxx5785",
                    "transactionTypeDisplayName": "Purchase"
                  },
                  "errorList": []
                }
                """;

            var result = await Ecr(FakeChannel.Always(approvedSale)).SaleAsync(0.111m, merchantReference: "EA5C14BDB0A2");

            var approved = Assert.IsType<EcrResult.Approved>(result);
            Assert.Equal("0.111", approved.Amount);
            Assert.Equal("623807390109", approved.Rrn);
            Assert.Equal("713984", approved.AuthCode);
            Assert.Equal("543173xxxx5785", approved.MaskedPan);
        }

        [Fact]
        public async Task AUnifiedDeclinedEnvelopeUsesMessageAndResponseCode()
        {
            const string declined =
                """
                {
                  "success": false,
                  "responseCode": "909",
                  "message": "Declined",
                  "data": null,
                  "errorList": ["Insufficient funds"]
                }
                """;

            var result = await Ecr(FakeChannel.Always(declined)).SaleAsync(1.000m, merchantReference: "REF-1");

            var answer = Assert.IsType<EcrResult.Declined>(result);
            Assert.Equal("Insufficient funds", answer.Reason);
            Assert.Equal("909", answer.ResponseCode);
        }

        [Fact]
        public async Task AZeroAuthorizeAmountShowsAmountInTheResultPopup()
        {
            const string approvedSale =
                """
                {
                  "success": true,
                  "responseCode": "00",
                  "message": "Approved",
                  "data": {
                    "merchantReference": "REF-1",
                    "amount": "0.111",
                    "authorizeAmount": "0"
                  },
                  "errorList": []
                }
                """;

            var result = await Ecr(FakeChannel.Always(approvedSale)).SaleAsync(0.111m, merchantReference: "REF-1");

            Assert.Equal("0.111", Assert.IsType<EcrResult.Approved>(result).Amount);
        }

        [Fact]
        public async Task AnEmptyAuthorizeAmountShowsAmountInTheResultPopup()
        {
            const string approvedSale =
                """
                {
                  "success": true,
                  "responseCode": "00",
                  "message": "Approved",
                  "data": {
                    "merchantReference": "REF-1",
                    "amount": "1.478",
                    "authorizeAmount": ""
                  },
                  "errorList": []
                }
                """;

            var result = await Ecr(FakeChannel.Always(approvedSale)).SaleAsync(1.478m, merchantReference: "REF-1");

            Assert.Equal("1.478", Assert.IsType<EcrResult.Approved>(result).Amount);
        }

        // ── Beyond the Kotlin suite ───────────────────────────────────────

        [Fact]
        public async Task AResponseTimeoutIsFollowedByAnInquiryToo()
        {
            var channel = new FakeChannel((_, connection) =>
                connection == 0 ? throw new EcrChannelTimeout("no answer") : FoundTransaction);

            var result = await Ecr(channel).SaleAsync(1.234m, merchantReference: "ORD-1");

            var failed = Assert.IsType<EcrResult.Failed>(result);
            Assert.IsType<Failure.Timeout>(failed.Failure);
            Assert.True(failed.Failure.OutcomeUnknown);
            Assert.True(failed.Settled);
        }

        [Fact]
        public async Task AVoidAndARefundGetTheSameFollowUp()
        {
            var voidChannel = new FakeChannel((_, connection) => connection == 0 ? null : FoundTransaction);
            var refundChannel = new FakeChannel((_, connection) => connection == 0 ? null : FoundTransaction);

            var voided = await Ecr(voidChannel).VoidAsync("24", merchantReference: "VOID-1");
            var refunded = await Ecr(refundChannel).RefundAsync(0.5m, "24", "20260806", merchantReference: "REF-1");

            Assert.True(Assert.IsType<EcrResult.Failed>(voided).Settled);
            Assert.True(Assert.IsType<EcrResult.Failed>(refunded).Settled);
            Assert.Contains("\"originalMerchantReference\":\"VOID-1\"", voidChannel.Requests[1]);
            Assert.Contains("\"originalMerchantReference\":\"REF-1\"", refundChannel.Requests[1]);
        }

        [Fact]
        public async Task TheFollowUpIsAnInquiryNeverASecondSale()
        {
            var channel = new FakeChannel((_, connection) => connection == 0 ? null : FoundTransaction);

            await Ecr(channel).SaleAsync(1.234m, merchantReference: "ORD-1");

            Assert.Equal(2, channel.Requests.Count);
            Assert.DoesNotContain("\"amount\"", channel.Requests[1]);
            Assert.DoesNotContain("\"messageType\":\"SALE\"", channel.Requests[1]);
        }

        [Fact]
        public async Task ACallersCancellationDuringTheFollowUpIsNotSwallowed()
        {
            using var cts = new System.Threading.CancellationTokenSource();
            var channel = new FakeChannel((_, connection) =>
            {
                if (connection == 0) return null;
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                Ecr(channel).SaleAsync(1.234m, merchantReference: "ORD-1", cancellationToken: cts.Token));
        }

        [Fact]
        public async Task AnUnexpectedErrorInTheFollowUpLeavesTheOutcomeUnknownRatherThanEscaping()
        {
            var channel = new FakeChannel((_, connection) =>
                connection == 0 ? null : throw new InvalidOperationException("boom"));

            var result = await Ecr(channel).SaleAsync(1.234m, merchantReference: "ORD-1");

            var failed = Assert.IsType<EcrResult.Failed>(result);
            Assert.Null(failed.Recovered);
            Assert.False(failed.Settled);
        }

        [Fact]
        public async Task AFailedResultAlwaysSaysToInquireNeverToRetry()
        {
            var result = await Ecr(FakeChannel.Always(null), autoInquire: false).SaleAsync(1m);

            var failed = Assert.IsType<EcrResult.Failed>(result);
            Assert.Equal(NextStep.InquireByMerchantReference, failed.NextStep);
        }
    }
}
