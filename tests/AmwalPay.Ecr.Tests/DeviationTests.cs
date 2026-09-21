using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace AmwalPay.Ecr.Tests
{
    /// <summary>
    /// The two places the C# port deliberately behaves differently from the
    /// Kotlin SDK, kept as named tests so the difference is a decision on record
    /// and not an accident someone "fixes" later.
    /// <list type="number">
    /// <item>
    /// A TCP <b>connect</b> timeout is an <see cref="IOException"/> and so
    /// <see cref="Failure.Unreachable"/> (outcome NOT unknown, no auto-inquiry).
    /// Kotlin turned it into a <see cref="Failure.Timeout"/>, which claims the
    /// request may have reached the terminal when nothing was ever sent.
    /// </item>
    /// <item>
    /// A socket reset or abort <b>after the request was written</b> is reported as
    /// "Terminal closed the connection: ..." and so
    /// <see cref="Failure.ConnectionLost"/> (outcome unknown, auto-inquiry runs).
    /// Kotlin let it fall through to <see cref="Failure.Unreachable"/>, which
    /// claims nothing was attempted and skips the inquiry, the one case where a
    /// till is most likely to charge a cardholder twice.
    /// </item>
    /// </list>
    /// </summary>
    public class DeviationTests
    {
        private const string Found =
            """
            {"success":true,"responseCode":"00","message":"Transaction found",
             "data":{"transactionId":"abc-123","stan":"000131","status":"Approved","amount":"1.234","rrn":"622908275947"},"errorList":[]}
            """;

        private static EcrConfig Config(int port = 9100, bool autoInquire = true) =>
            new EcrConfig
            {
                Port = port,
                ConnectTimeout = TimeSpan.FromMilliseconds(400),
                ResponseTimeout = TimeSpan.FromSeconds(5),
                AutoInquireOnFailure = autoInquire,
            };

        // ── 1. Connect timeout / refusal: Unreachable, nothing asked ──────

        [Fact]
        public async Task AConnectTimeoutSurfacedByTheChannelIsUnreachableAndNotAnUnknownOutcome()
        {
            var channel = new FakeChannel((_, __) => throw new IOException("Connection timed out after 3000 ms"));
            var terminal = new EcrTerminal(channel, "TW1", Config());

            var failed = Assert.IsType<EcrResult.Failed>(await terminal.SaleAsync(1.234m, "ORD-1"));

            var unreachable = Assert.IsType<Failure.Unreachable>(failed.Failure);
            Assert.Contains("Connection timed out after 3000 ms", unreachable.Message);
            Assert.False(unreachable.OutcomeUnknown);
            Assert.Null(failed.Recovered);
            Assert.False(failed.Settled);
            Assert.Single(channel.Requests);
        }

        [Fact]
        public async Task ARealConnectToAnUnroutableAddressIsUnreachableNotATimeoutAndNothingIsAskedTwice()
        {
            // TEST-NET-1 (RFC 5737): the connect either hangs until ConnectTimeout
            // or fails outright. Both must land on Unreachable.
            var terminal = new EcrTerminal("192.0.2.1", "TW1", Config());
            var clock = Stopwatch.StartNew();

            var failed = Assert.IsType<EcrResult.Failed>(await terminal.SaleAsync(1.234m, "ORD-1"));

            Assert.IsType<Failure.Unreachable>(failed.Failure);
            Assert.False(failed.Failure.OutcomeUnknown);
            Assert.Null(failed.Recovered);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"waited through a second attempt? {clock.Elapsed}");
        }

        [Fact]
        public async Task ARefusedConnectionOverRealTcpIsUnreachableAndNotAskedTwice()
        {
            var terminal = new EcrTerminal("127.0.0.1", "TW1", Config(FakeTcpTerminal.ClosedPort()));

            var failed = Assert.IsType<EcrResult.Failed>(await terminal.SaleAsync(1.234m, "ORD-1"));

            var unreachable = Assert.IsType<Failure.Unreachable>(failed.Failure);
            Assert.False(unreachable.OutcomeUnknown, "nothing was attempted");
            Assert.Null(failed.Recovered);
            Assert.Contains("(127.0.0.1:", unreachable.Message);
        }

        [Fact]
        public async Task AConnectTimeoutIsNeverReportedAsAResponseTimeout()
        {
            var terminal = new EcrTerminal("192.0.2.1", "TW1", Config());

            var inquiry = await terminal.InquireByReferenceAsync("ORD-1");

            var failed = Assert.IsType<EcrInquiry.Failed>(inquiry);
            Assert.IsNotType<Failure.Timeout>(failed.Failure);
        }

        // ── 2. Reset after the request was written: ConnectionLost + inquiry

        [Fact]
        public async Task AResetAfterTheRequestWentOutSurfacedByTheChannelIsConnectionLostAndInquires()
        {
            var channel = new FakeChannel((_, connection) => connection == 0
                ? throw new IOException("Terminal closed the connection: Connection reset by peer")
                : Found);
            var terminal = new EcrTerminal(channel, "TW1", Config());

            var failed = Assert.IsType<EcrResult.Failed>(await terminal.SaleAsync(1.234m, "ORD-1"));

            var lost = Assert.IsType<Failure.ConnectionLost>(failed.Failure);
            Assert.True(lost.OutcomeUnknown);
            Assert.IsType<EcrInquiry.Found>(failed.Recovered);
            Assert.True(failed.Settled);
            Assert.Equal(2, channel.Requests.Count);
        }

        [Fact]
        public async Task ARealSocketResetAfterTheRequestIsConnectionLostThenRecoveredByInquiry()
        {
            using var server = new FakeTcpTerminal((_, connection) =>
                connection == 0 ? TcpReply.Reset : TcpReply.Answer(Found));
            var terminal = new EcrTerminal("127.0.0.1", "TW1", Config(server.Port));

            var failed = Assert.IsType<EcrResult.Failed>(await terminal.SaleAsync(1.234m, "ORD-77"));

            var lost = Assert.IsType<Failure.ConnectionLost>(failed.Failure);
            Assert.StartsWith("Terminal closed the connection", lost.Message);
            Assert.True(lost.OutcomeUnknown);
            var recovered = Assert.IsType<EcrInquiry.Found>(failed.Recovered);
            Assert.Equal("Approved", recovered.Transaction.Status);
            Assert.True(failed.Settled);

            Assert.Equal(2, server.Requests.Count);
            Assert.Contains("\"messageType\":\"SALE\"", server.Requests[0]);
            Assert.Contains("\"messageType\":\"INQUIRY\"", server.Requests[1]);
            Assert.Contains("\"originalMerchantReference\":\"ORD-77\"", server.Requests[1]);
        }

        [Fact]
        public async Task ARealSocketResetWithNoRecordOnTheTerminalStaysUnknown()
        {
            using var server = new FakeTcpTerminal((_, connection) => connection == 0
                ? TcpReply.Reset
                : TcpReply.Answer("""{"responseCode":"25","responseMessage":"Not found","approved":false}"""));
            var terminal = new EcrTerminal("127.0.0.1", "TW1", Config(server.Port));

            var failed = Assert.IsType<EcrResult.Failed>(await terminal.SaleAsync(1.234m, "ORD-77"));

            Assert.IsType<Failure.ConnectionLost>(failed.Failure);
            Assert.IsType<EcrInquiry.NotFound>(failed.Recovered);
            Assert.False(failed.Settled);
        }

        [Fact]
        public async Task ARealSocketResetIsNotAnAddressProblem()
        {
            using var server = FakeTcpTerminal.Always(TcpReply.Reset);
            var terminal = new EcrTerminal("127.0.0.1", "TW1", Config(server.Port, autoInquire: false));

            var failed = Assert.IsType<EcrResult.Failed>(await terminal.SaleAsync(1.234m, "ORD-1"));

            Assert.IsNotType<Failure.Unreachable>(failed.Failure);
            Assert.True(failed.Failure.OutcomeUnknown);
            Assert.Single(server.Requests);
        }

        // ── Behaviour Kotlin already had, over real TCP, for the record ───

        [Fact]
        public async Task ARealServerThatDropsTheConnectionWithoutAnAnswerIsConnectionLostAndInquires()
        {
            using var server = new FakeTcpTerminal((_, connection) =>
                connection == 0 ? TcpReply.Drop : TcpReply.Answer(Found));
            var terminal = new EcrTerminal("127.0.0.1", "TW1", Config(server.Port));

            var failed = Assert.IsType<EcrResult.Failed>(await terminal.SaleAsync(1.234m, "ORD-1"));

            Assert.IsType<Failure.ConnectionLost>(failed.Failure);
            Assert.True(failed.Settled);
        }

        [Fact]
        public async Task ARealServerThatNeverAnswersIsATimeoutAndInquires()
        {
            using var server = new FakeTcpTerminal((_, connection) =>
                connection == 0 ? TcpReply.Stall : TcpReply.Answer(Found));
            var config = Config(server.Port) with { ResponseTimeout = TimeSpan.FromMilliseconds(400) };
            var terminal = new EcrTerminal("127.0.0.1", "TW1", config);

            var failed = Assert.IsType<EcrResult.Failed>(await terminal.SaleAsync(1.234m, "ORD-1"));

            Assert.IsType<Failure.Timeout>(failed.Failure);
            Assert.True(failed.Failure.OutcomeUnknown);
            Assert.True(failed.Settled);
        }

        [Fact]
        public async Task ARealServerAnsweringGarbageIsMalformed()
        {
            using var server = FakeTcpTerminal.Always(TcpReply.Answer("this is not json"));
            var terminal = new EcrTerminal("127.0.0.1", "TW1", Config(server.Port, autoInquire: false));

            var failed = Assert.IsType<EcrResult.Failed>(await terminal.SaleAsync(1.234m, "ORD-1"));

            Assert.IsType<Failure.Malformed>(failed.Failure);
        }

        [Fact]
        public async Task ARealSaleAndApprovalRoundTripOverTcp()
        {
            using var server = FakeTcpTerminal.Always(TcpReply.Answer(
                """{"success":true,"responseCode":"00","message":"Approved","data":{"merchantReference":"ORD-1","amount":"1.234","rrn":"1","authCode":"2"}}"""));
            var terminal = new EcrTerminal("127.0.0.1", "TW1", Config(server.Port));

            var approved = Assert.IsType<EcrResult.Approved>(await terminal.SaleAsync(1.234m, "ORD-1"));

            Assert.Equal("1.234", approved.Amount);
            Assert.Single(server.Requests);
            Assert.True(await terminal.IsReachableAsync());
        }
    }
}
