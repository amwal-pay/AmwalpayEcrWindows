using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AmwalPay.Ecr.Internal;
using Xunit;

namespace AmwalPay.Ecr.Tests
{
    /// <summary>
    /// What <see cref="EcrTerminal"/> puts on the wire and what it will believe
    /// coming back, against a scripted channel. Nothing here touches a socket.
    /// </summary>
    public class EcrTerminalTests
    {
        private static readonly EcrConfig Signed = EcrTestConfigs.Lan with { AutoInquireOnFailure = false };
        private static readonly EcrConfig Unsigned = new EcrConfig { AutoInquireOnFailure = false };

        // ── Requests ──────────────────────────────────────────────────────

        [Fact]
        public async Task ASaleGoesOutAsAnUnsignedSaleMessageWhenNoKeyIsConfigured()
        {
            var channel = FakeChannel.Always("""{"responseCode":"00","approved":true}""");
            var terminal = new EcrTerminal(channel, "TW1", Unsigned);

            await terminal.SaleAsync(1.234m, "ORD-1");

            var sent = Wire.Obj(Assert.Single(channel.Requests));
            Assert.Equal("1", sent.Str("version"));
            Assert.Equal("SALE", sent.Str("messageType"));
            Assert.Equal("ORD-1", sent.Str("merchantReference"));
            Assert.Equal("TW1", sent.Str("terminalSerial"));
            Assert.Equal("512", sent.Str("currencyCode"));
            Assert.Equal("ECR01", sent.Str("ecrId"));
            Assert.Equal("000000001234", sent.Str("amount"));
            Assert.Matches("^[0-9]{14}$", sent.Str("transactionDateTime"));
            Assert.False(sent.ContainsKey("secureHash"));
            Assert.False(sent.ContainsKey("nonce"));
        }

        [Fact]
        public async Task AVoidNamesTheOriginalByPaddedReceiptNumberAndTheTerminalItWasTakenOn()
        {
            var channel = FakeChannel.Always("""{"responseCode":"00","approved":true}""");
            var terminal = new EcrTerminal(channel, "TW1", Unsigned);

            await terminal.VoidAsync("24", originalTerminalId: "31629", merchantReference: "VOID-1");

            var sent = Wire.Obj(Assert.Single(channel.Requests));
            Assert.Equal("VOID", sent.Str("messageType"));
            Assert.Equal("000024", sent.Str("stan"));
            Assert.Equal("31629", sent.Str("originalTerminalId"));
            Assert.Equal("VOID-1", sent.Str("merchantReference"));
            Assert.False(sent.ContainsKey("amount"));
        }

        [Fact]
        public async Task ARefundCarriesTheAmountTheOriginalAndItsDate()
        {
            var channel = FakeChannel.Always("""{"responseCode":"00","approved":true}""");
            var terminal = new EcrTerminal(channel, "TW1", Unsigned);

            await terminal.RefundAsync(0.5m, "24", "20260806");

            var sent = Wire.Obj(Assert.Single(channel.Requests));
            Assert.Equal("REFUND", sent.Str("messageType"));
            Assert.Equal("000000000500", sent.Str("amount"));
            Assert.Equal("000024", sent.Str("stan"));
            Assert.Equal("20260806", sent.Str("originalTransactionDate"));
        }

        [Fact]
        public async Task AnInquiryAndAReceiptRequestNameTheOriginalAndItsDate()
        {
            var channel = FakeChannel.Always("""{"success":true,"data":null}""");
            var terminal = new EcrTerminal(channel, "TW1", Unsigned);

            await terminal.InquireAsync("24", "20260806");
            await terminal.ReceiptAsync("24", "20260806");

            var inquiry = Wire.Obj(channel.Requests[0]);
            var receipt = Wire.Obj(channel.Requests[1]);
            Assert.Equal("INQUIRY", inquiry.Str("messageType"));
            Assert.Equal("RECEIPT", receipt.Str("messageType"));
            foreach (var sent in new[] { inquiry, receipt })
            {
                Assert.Equal("000024", sent.Str("stan"));
                Assert.Equal("20260806", sent.Str("originalTransactionDate"));
                Assert.False(sent.ContainsKey("amount"));
            }
        }

        [Fact]
        public async Task TheResponseTimeoutFromTheConfigIsHandedToTheChannel()
        {
            var channel = FakeChannel.Always("""{"responseCode":"00","approved":true}""");
            var terminal = new EcrTerminal(channel, "TW1", Unsigned with { ResponseTimeout = TimeSpan.FromSeconds(45) });

            await terminal.SaleAsync(1m);

            Assert.Equal(TimeSpan.FromSeconds(45), Assert.Single(channel.Timeouts));
        }

        [Fact]
        public async Task ACurrencyWithTwoDecimalPlacesIsWrittenInCents()
        {
            var channel = FakeChannel.Always("""{"responseCode":"00","approved":true}""");
            var terminal = new EcrTerminal(channel, "TW1", Unsigned with { CurrencyCode = "840", MinorUnitDigits = 2 });

            await terminal.SaleAsync(1.23m);

            var sent = Wire.Obj(Assert.Single(channel.Requests));
            Assert.Equal("000000000123", sent.Str("amount"));
            Assert.Equal("840", sent.Str("currencyCode"));
        }

        // ── Argument checks happen before anything is sent ────────────────

        [Fact]
        public async Task ABadReferenceIsRefusedBeforeAnythingIsSent()
        {
            var channel = FakeChannel.Always("{}");
            var terminal = new EcrTerminal(channel, "TW1", Unsigned);

            await Assert.ThrowsAsync<ArgumentException>(() => terminal.SaleAsync(1m, "has space"));
            await Assert.ThrowsAsync<ArgumentException>(() => terminal.SaleAsync(1m, new string('X', 33)));

            Assert.Empty(channel.Requests);
        }

        [Fact]
        public async Task RunAsyncRefusesWhatItCannotSendSafely()
        {
            var channel = FakeChannel.Always("{}");
            var terminal = new EcrTerminal(channel, "TW1", Unsigned);

            await Assert.ThrowsAsync<ArgumentException>(() => terminal.RunAsync(EcrTransactionType.Inquiry));
            await Assert.ThrowsAsync<ArgumentException>(() => terminal.RunAsync(EcrTransactionType.Receipt));
            await Assert.ThrowsAsync<ArgumentException>(() => terminal.RunAsync(EcrTransactionType.Sale));
            await Assert.ThrowsAsync<ArgumentException>(() => terminal.RunAsync(EcrTransactionType.Refund, amount: 1m));
            await Assert.ThrowsAsync<ArgumentException>(() => terminal.RunAsync(EcrTransactionType.Void, originalStan: "  "));
            await Assert.ThrowsAsync<ArgumentException>(() => terminal.VoidAsync(""));

            Assert.Empty(channel.Requests);
        }

        [Fact]
        public async Task AnInquiryOrReceiptWithoutAnIdentifierIsRefused()
        {
            var channel = FakeChannel.Always("{}");
            var terminal = new EcrTerminal(channel, "TW1", Unsigned);

            await Assert.ThrowsAsync<ArgumentException>(() => terminal.InquireAsync(" ", "20260806"));
            await Assert.ThrowsAsync<ArgumentException>(() => terminal.InquireByReferenceAsync(" "));
            await Assert.ThrowsAsync<ArgumentException>(() => terminal.ReceiptAsync("", "20260806"));

            Assert.Empty(channel.Requests);
        }

        [Fact]
        public void AChannelIsRequired()
        {
            Assert.Throws<ArgumentNullException>(() => new EcrTerminal((IEcrChannel)null!, "TW1"));
        }

        // ── Failures the channel can report ───────────────────────────────

        [Fact]
        public async Task ATimeoutIsATimeoutNotADecline()
        {
            var channel = new FakeChannel((_, __) => throw new EcrChannelTimeout("no answer"));
            var terminal = new EcrTerminal(channel, "TW1", Unsigned with { ResponseTimeout = TimeSpan.FromSeconds(90) });

            var failed = Assert.IsType<EcrResult.Failed>(await terminal.SaleAsync(1m));

            var timeout = Assert.IsType<Failure.Timeout>(failed.Failure);
            Assert.Contains("1m 30s", timeout.Message);
            Assert.Contains("inquire before retrying", timeout.Message);
            Assert.True(timeout.OutcomeUnknown);
        }

        [Theory]
        [InlineData(0, "0s")]
        [InlineData(30, "30s")]
        [InlineData(59, "59s")]
        [InlineData(60, "1m")]
        [InlineData(90, "1m 30s")]
        [InlineData(120, "2m")]
        public void DurationsAreFormattedForOperators(int seconds, string expected)
        {
            Assert.Equal(expected, EcrTerminal.FormatDuration(TimeSpan.FromSeconds(seconds)));
        }

        [Fact]
        public async Task ABodyThatIsNotJsonIsMalformedAndOutcomeUnknown()
        {
            var terminal = new EcrTerminal(FakeChannel.Always("<html>gateway error</html>"), "TW1", Unsigned);

            var failed = Assert.IsType<EcrResult.Failed>(await terminal.SaleAsync(1m));

            Assert.IsType<Failure.Malformed>(failed.Failure);
            Assert.True(failed.Failure.OutcomeUnknown);
        }

        [Theory]
        [InlineData("null")]
        [InlineData("[]")]
        [InlineData("\"approved\"")]
        public async Task AJsonAnswerThatIsNotAnObjectIsMalformedNotACrash(string body)
        {
            var terminal = new EcrTerminal(FakeChannel.Always(body), "TW1", Unsigned);

            var failed = Assert.IsType<EcrResult.Failed>(await terminal.SaleAsync(1m));

            Assert.IsType<Failure.Malformed>(failed.Failure);
        }

        [Theory]
        [InlineData("""{"success":false,"success":true,"responseCode":"00"}""")]
        [InlineData("""{"success":true,"data":{"amount":"1"},"data":null}""")]
        [InlineData("""{"success":true,"data":{"amount":"1","amount":"999"}}""")]
        public async Task AnAnswerRepeatingAKeyIsMalformedNotACrashAndNeverLastWinsSilently(string body)
        {
            // Two readings of one message is how a decline gets read as an approval.
            // The answer is refused whole, and it comes back as a result rather
            // than as an exception out of a payment call.
            var terminal = new EcrTerminal(FakeChannel.Always(body), "TW1", Unsigned);

            var failed = Assert.IsType<EcrResult.Failed>(await terminal.SaleAsync(1m));

            Assert.IsType<Failure.Malformed>(failed.Failure);
            Assert.True(failed.Failure.OutcomeUnknown);
        }

        [Fact]
        public async Task AnEmptyMessageFromTheChannelIsMalformed()
        {
            var channel = new FakeChannel((_, __) => throw new IOException("Terminal returned an empty message"));
            var terminal = new EcrTerminal(channel, "TW1", Unsigned);

            var failed = Assert.IsType<EcrResult.Failed>(await terminal.SaleAsync(1m));

            Assert.IsType<Failure.Malformed>(failed.Failure);
        }

        [Fact]
        public async Task InquiriesAndReceiptsReportTheSameFailuresAsSales()
        {
            var terminal = new EcrTerminal(FakeChannel.Always(null), "TW1", Unsigned);

            var inquiry = await terminal.InquireAsync("24", "20260806");
            var byReference = await terminal.InquireByReferenceAsync("ORD-1");
            var receipt = await terminal.ReceiptAsync("24", "20260806");

            Assert.IsType<Failure.ConnectionLost>(Assert.IsType<EcrInquiry.Failed>(inquiry).Failure);
            Assert.IsType<Failure.ConnectionLost>(Assert.IsType<EcrInquiry.Failed>(byReference).Failure);
            Assert.IsType<Failure.ConnectionLost>(Assert.IsType<EcrReceipt.Failed>(receipt).Failure);
        }

        [Fact]
        public async Task ADeclineThatSaysTheOutcomeIsUnknownAsksForAnInquiryNotARetry()
        {
            var terminal = new EcrTerminal(
                FakeChannel.Always(
                    """{"success":false,"responseCode":"91","message":"Issuer unavailable","nextStep":"INQUIRE_BY_MERCHANT_REFERENCE"}"""),
                "TW1",
                Unsigned);

            var declined = Assert.IsType<EcrResult.Declined>(await terminal.SaleAsync(1m));

            Assert.Equal(NextStep.InquireByMerchantReference, declined.NextStep);
        }

        [Theory]
        [InlineData("""{"success":false,"responseCode":"51","message":"Declined"}""")]
        [InlineData("""{"success":false,"responseCode":"51","message":"Declined","nextStep":"NONE"}""")]
        [InlineData("""{"success":false,"responseCode":"51","message":"Declined","nextStep":"SOMETHING_NEW"}""")]
        public async Task AnUnknownOrAbsentNextStepReadsAsNone(string answer)
        {
            var terminal = new EcrTerminal(FakeChannel.Always(answer), "TW1", Unsigned);

            var declined = Assert.IsType<EcrResult.Declined>(await terminal.SaleAsync(1m));

            Assert.Equal(NextStep.None, declined.NextStep);
        }

        [Fact]
        public async Task ADeclineWithNoWordsGetsADefaultReason()
        {
            var terminal = new EcrTerminal(FakeChannel.Always("""{"success":false,"responseCode":"05"}"""), "TW1", Unsigned);

            var declined = Assert.IsType<EcrResult.Declined>(await terminal.SaleAsync(1m));

            Assert.Equal("Declined", declined.Reason);
            Assert.Equal("05", declined.ResponseCode);
        }

        [Fact]
        public async Task APartialApprovalIsAnApprovalThatSaysSo()
        {
            var answer =
                """
                {"success":true,"responseCode":"00","message":"Approved",
                 "data":{"merchantReference":"ORD-1","amount":"961.100","authorizeAmount":"50.000","isPartialApprove":true}}
                """;
            var terminal = new EcrTerminal(FakeChannel.Always(answer), "TW1", Unsigned);

            var approved = Assert.IsType<EcrResult.Approved>(await terminal.SaleAsync(961.1m, "ORD-1"));

            Assert.True(approved.PartialApproval);
            Assert.Equal("50.000", approved.Amount);
            Assert.Equal("961.100", approved.RequestedAmount);
        }

        // ── Reachability ──────────────────────────────────────────────────

        [Fact]
        public async Task ReachabilityReportsWhyNotAndWhere()
        {
            var channel = FakeChannel.Always(null);
            channel.ProbeError = "Connection refused";
            var terminal = new EcrTerminal(channel, "TW1", Unsigned);

            var reachability = await terminal.ProbeReachabilityAsync();

            Assert.False(reachability.Reachable);
            Assert.Equal("Connection refused", reachability.Error);
            Assert.Equal("fake", reachability.Host);
            Assert.Equal(9100, reachability.Port);
            Assert.Equal("fake:9100", reachability.Endpoint);
            Assert.False(await terminal.IsReachableAsync());
        }

        [Fact]
        public async Task AReachableTerminalHasNoError()
        {
            var terminal = new EcrTerminal(FakeChannel.Always(null), "TW1", Unsigned);

            var reachability = await terminal.ProbeReachabilityAsync();

            Assert.True(reachability.Reachable);
            Assert.Null(reachability.Error);
            Assert.True(await terminal.IsReachableAsync());
        }

        [Fact]
        public void ReachabilityOfALinkWithNoPortShowsJustItsName()
        {
            Assert.Equal("USB cable", new EcrReachability(true, "USB cable", 0).Endpoint);
            Assert.Equal("10.0.0.5:9100", new EcrReachability(true, "10.0.0.5", 9100).Endpoint);
            Assert.Equal("custom", new EcrReachability(true, "10.0.0.5", 9100, Endpoint: "custom").Endpoint);
        }

        // ── Trust: signing the request, checking the answer ───────────────

        [Fact]
        public async Task ASignedRequestVerifiesWithTheTillsKeyAndCarriesANonce()
        {
            var channel = FakeChannel.Always(null);
            var terminal = new EcrTerminal(channel, "TW1", Signed);

            await terminal.SaleAsync(1.234m, "ORD-1");

            var sent = Wire.Obj(Assert.Single(channel.Requests));
            Assert.Equal(32, sent.Str("nonce").Length);
            Assert.True(SecureHash.Verify(sent, Signed.SecureHashKey));
        }

        [Fact]
        public async Task ASignedAnswerToThisRequestIsBelieved()
        {
            var channel = new FakeChannel((request, _) =>
                Wire.Signed(Wire.Approval(Wire.NonceOf(request)), Signed.SecureHashKey));
            var terminal = new EcrTerminal(channel, "TW1", Signed);

            var approved = Assert.IsType<EcrResult.Approved>(await terminal.SaleAsync(1.234m, "ORD-1"));

            Assert.Equal("1.234", approved.Amount);
            Assert.Equal("622908275947", approved.Rrn);
        }

        [Fact]
        public async Task AnUnsignedAnswerIsRejectedWhenAKeyIsConfigured()
        {
            // Anything on the network that answers on the terminal's port before
            // the terminal does could otherwise claim "approved" for a payment
            // that never happened.
            var channel = new FakeChannel((request, _) =>
                EcrJson.Write(Wire.Approval(Wire.NonceOf(request))));
            var terminal = new EcrTerminal(channel, "TW1", Signed);

            var failed = Assert.IsType<EcrResult.Failed>(await terminal.SaleAsync(1.234m, "ORD-1"));

            var rejection = Assert.IsType<Failure.Unauthenticated>(failed.Failure);
            Assert.Contains("not signed with this terminal's key", rejection.Message);
            Assert.True(rejection.OutcomeUnknown, "the terminal may well have taken the payment");
        }

        [Fact]
        public async Task AnAnswerSignedWithAnotherKeyIsRejected()
        {
            var channel = new FakeChannel((request, _) =>
                Wire.Signed(Wire.Approval(Wire.NonceOf(request)), EcrTestConfigs.SecureHashKeyEcrWifiOther));
            var terminal = new EcrTerminal(channel, "TW1", Signed);

            var failed = Assert.IsType<EcrResult.Failed>(await terminal.SaleAsync(1.234m, "ORD-1"));

            Assert.IsType<Failure.Unauthenticated>(failed.Failure);
        }

        [Fact]
        public async Task AnAnswerAlteredAfterSigningIsRejected()
        {
            var channel = new FakeChannel((request, _) =>
            {
                var body = Wire.Approval(Wire.NonceOf(request));
                var text = Wire.Signed(body, Signed.SecureHashKey);
                return text.Replace("\"success\":true", "\"success\":false");
            });
            var terminal = new EcrTerminal(channel, "TW1", Signed);

            var failed = Assert.IsType<EcrResult.Failed>(await terminal.SaleAsync(1.234m, "ORD-1"));

            Assert.IsType<Failure.Unauthenticated>(failed.Failure);
        }

        [Fact]
        public async Task ACorrectlySignedAnswerToAnEarlierRequestIsRejectedAsAReplay()
        {
            // The signature is valid, but it answers a different request: exactly
            // what a captured approval replayed onto a new sale looks like.
            var replayed = Wire.Signed(
                Wire.Approval("00000000000000000000000000000001"),
                Signed.SecureHashKey);
            var terminal = new EcrTerminal(FakeChannel.Always(replayed), "TW1", Signed);

            var failed = Assert.IsType<EcrResult.Failed>(await terminal.SaleAsync(1.234m, "ORD-1"));

            var rejection = Assert.IsType<Failure.Unauthenticated>(failed.Failure);
            Assert.Contains("belongs to a different request", rejection.Message);
            Assert.True(rejection.OutcomeUnknown);
        }

        [Fact]
        public async Task ASignedAnswerThatOmitsTheNonceIsRejected()
        {
            var noNonce = Wire.Approval("");
            noNonce.Remove("nonce");
            var terminal = new EcrTerminal(
                FakeChannel.Always(Wire.Signed(noNonce, Signed.SecureHashKey)),
                "TW1",
                Signed);

            var failed = Assert.IsType<EcrResult.Failed>(await terminal.SaleAsync(1.234m, "ORD-1"));

            Assert.IsType<Failure.Unauthenticated>(failed.Failure);
        }

        [Fact]
        public async Task AnUnsignedTillAcceptsAnUnsignedAnswerAndIgnoresAnyNonce()
        {
            var terminal = new EcrTerminal(
                FakeChannel.Always(EcrJson.Write(Wire.Approval("SOMETHING-ELSE"))),
                "TW1",
                Unsigned);

            Assert.IsType<EcrResult.Approved>(await terminal.SaleAsync(1.234m, "ORD-1"));
        }

        [Fact]
        public async Task ARejectedAnswerIsFollowedByAnInquiryBecauseThePaymentMayHaveHappened()
        {
            var config = Signed with { AutoInquireOnFailure = true };
            var channel = new FakeChannel((request, connection) =>
                connection == 0
                    ? EcrJson.Write(Wire.Approval(Wire.NonceOf(request)))
                    : Wire.Signed(Wire.FoundEnvelope(Wire.NonceOf(request)), config.SecureHashKey));
            var terminal = new EcrTerminal(channel, "TW1", config);

            var failed = Assert.IsType<EcrResult.Failed>(await terminal.SaleAsync(1.234m, "ORD-1"));

            Assert.IsType<Failure.Unauthenticated>(failed.Failure);
            Assert.IsType<EcrInquiry.Found>(failed.Recovered);
            Assert.True(failed.Settled);
            Assert.Equal(2, channel.Requests.Count);
            Assert.Equal("INQUIRY", Wire.Obj(channel.Requests[1]).Str("messageType"));
        }

        [Fact]
        public async Task InquiriesAndReceiptsAreAuthenticatedToo()
        {
            var terminal = new EcrTerminal(
                FakeChannel.Always(EcrJson.Write(Wire.FoundEnvelope("x"))),
                "TW1",
                Signed);

            var inquiry = await terminal.InquireAsync("24", "20260806");
            var receipt = await terminal.ReceiptAsync("24", "20260806");

            Assert.IsType<Failure.Unauthenticated>(Assert.IsType<EcrInquiry.Failed>(inquiry).Failure);
            Assert.IsType<Failure.Unauthenticated>(Assert.IsType<EcrReceipt.Failed>(receipt).Failure);
        }

        [Fact]
        public async Task ASignedInquiryAnswerIsBelieved()
        {
            var channel = new FakeChannel((request, _) =>
                Wire.Signed(Wire.FoundEnvelope(Wire.NonceOf(request)), Signed.SecureHashKey));
            var terminal = new EcrTerminal(channel, "TW1", Signed);

            var inquiry = await terminal.InquireAsync("24", "20260806");

            Assert.IsType<EcrInquiry.Found>(inquiry);
        }

        // ── Diagnostics ───────────────────────────────────────────────────

        [Fact]
        public async Task TheSecretNeverReachesTheLog()
        {
            var lines = new List<string>();
            var channel = new FakeChannel((request, _) =>
                Wire.Signed(Wire.Approval(Wire.NonceOf(request)), Signed.SecureHashKey));
            var terminal = new EcrTerminal(channel, "TW1", Signed, EcrLogger.From(lines.Add));

            await terminal.SaleAsync(1.234m, "ORD-1");

            Assert.NotEmpty(lines);
            var log = string.Join("\n", lines);
            Assert.DoesNotContain(Signed.SecureHashKey, log, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Sending SALE ORD-1 to fake:9100", log);
            Assert.Contains("response signature valid", log);
        }

        [Fact]
        public void ALoggerIsRequiredToHaveASink()
        {
            Assert.Throws<ArgumentNullException>(() => EcrLogger.From(null!));
            EcrLogger.None.Debug("discarded");
        }

        [Fact]
        public async Task ARejectedAnswerIsLoggedAsRejected()
        {
            var lines = new List<string>();
            var terminal = new EcrTerminal(
                FakeChannel.Always(EcrJson.Write(Wire.Approval("x"))),
                "TW1",
                Signed,
                EcrLogger.From(lines.Add));

            await terminal.SaleAsync(1m, "ORD-1");

            Assert.Contains(lines, line => line.StartsWith("Rejected the answer:", StringComparison.Ordinal));
        }
    }
}
