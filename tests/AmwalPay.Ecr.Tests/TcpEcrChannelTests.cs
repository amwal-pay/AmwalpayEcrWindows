using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AmwalPay.Ecr.Tests
{
    /// <summary>
    /// <see cref="TcpEcrChannel"/> against a real socket on the loopback
    /// interface. Everything else in the suite fakes the channel; this is the
    /// one place the operating system's TCP stack is on the other end.
    /// </summary>
    public class TcpEcrChannelTests
    {
        private static readonly TimeSpan Generous = TimeSpan.FromSeconds(10);

        private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

        // ── Framing ───────────────────────────────────────────────────────

        [Fact]
        public async Task ARequestIsFramedOnTheWireAndTheAnswerBodyIsReturned()
        {
            using var server = FakeTcpTerminal.Always(TcpReply.Answer("""{"responseCode":"00"}"""));
            var channel = new TcpEcrChannel("127.0.0.1", server.Port);

            var body = await channel.ExchangeAsync(Utf8("""{"messageType":"SALE"}"""), Generous);

            Assert.Equal("""{"responseCode":"00"}""", Encoding.UTF8.GetString(body));
            var raw = Assert.Single(server.RawRequests);
            Assert.Equal(new byte[] { 0x00, 0x16 }, new[] { raw[0], raw[1] });
            Assert.Equal("""{"messageType":"SALE"}""", Encoding.UTF8.GetString(raw, 2, raw.Length - 2));
        }

        [Fact]
        public async Task MessagesLongerThan255BytesRoundTripBothWays()
        {
            var request = "{\"pad\":\"" + new string('x', 400) + "\"}";
            var response = "{\"pad\":\"" + new string('y', 700) + "\"}";
            using var server = FakeTcpTerminal.Always(TcpReply.Answer(response));
            var channel = new TcpEcrChannel("127.0.0.1", server.Port);

            var body = await channel.ExchangeAsync(Utf8(request), Generous);

            Assert.Equal(response, Encoding.UTF8.GetString(body));
            Assert.Equal(request, Assert.Single(server.Requests));
        }

        [Fact]
        public async Task NonAsciiTextSurvivesTheTrip()
        {
            const string text = """{"message":"تمت الموافقة — café €"}""";
            using var server = FakeTcpTerminal.Always(TcpReply.Answer(text));
            var channel = new TcpEcrChannel("127.0.0.1", server.Port);

            var body = await channel.ExchangeAsync(Utf8(text), Generous);

            Assert.Equal(text, Encoding.UTF8.GetString(body));
            Assert.Equal(text, Assert.Single(server.Requests));
        }

        [Fact]
        public async Task AnAnswerThatArrivesInPiecesIsAssembled()
        {
            // The terminal is free to write the header and body in separate
            // segments; TCP gives a stream, not messages.
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var answer = Utf8("""{"responseCode":"00","approved":true}""");
                var serve = Task.Run(async () =>
                {
                    using var client = await listener.AcceptTcpClientAsync();
                    client.NoDelay = true;
                    var stream = client.GetStream();
                    var buffer = new byte[4096];
                    await stream.ReadAsync(buffer, 0, buffer.Length);

                    await stream.WriteAsync(new[] { (byte)(answer.Length >> 8) }, 0, 1);
                    await stream.FlushAsync();
                    await Task.Delay(50);
                    await stream.WriteAsync(new[] { (byte)(answer.Length & 0xFF) }, 0, 1);
                    await stream.FlushAsync();
                    await Task.Delay(50);
                    await stream.WriteAsync(answer, 0, 10);
                    await stream.FlushAsync();
                    await Task.Delay(50);
                    await stream.WriteAsync(answer, 10, answer.Length - 10);
                    await stream.FlushAsync();
                });

                var body = await new TcpEcrChannel("127.0.0.1", port).ExchangeAsync(Utf8("{}"), Generous);

                Assert.Equal("""{"responseCode":"00","approved":true}""", Encoding.UTF8.GetString(body));
                await serve;
            }
            finally
            {
                listener.Stop();
            }
        }

        [Fact]
        public async Task EachExchangeUsesItsOwnConnection()
        {
            using var server = FakeTcpTerminal.Always(TcpReply.Answer("""{"responseCode":"00"}"""));
            var channel = new TcpEcrChannel("127.0.0.1", server.Port);

            await channel.ExchangeAsync(Utf8("""{"n":1}"""), Generous);
            await channel.ExchangeAsync(Utf8("""{"n":2}"""), Generous);

            Assert.Equal(2, server.Connections);
            Assert.Equal(new[] { """{"n":1}""", """{"n":2}""" }, server.Requests);
        }

        [Fact]
        public async Task ARequestTooLargeForAFrameIsRefusedBeforeAnythingIsSent()
        {
            // Nothing can be sent, so this is a local error and must not read as a
            // connection the terminal dropped (which would trigger an inquiry).
            using var server = FakeTcpTerminal.Always(TcpReply.Answer("{}"));
            var channel = new TcpEcrChannel("127.0.0.1", server.Port);

            var e = await Assert.ThrowsAsync<IOException>(() =>
                channel.ExchangeAsync(new byte[EcrFrames.MaxBodyBytes + 1], Generous));

            Assert.Contains("does not fit a frame", e.Message);
            Assert.DoesNotContain("closed the connection", e.Message);
            Assert.Equal(0, server.Connections);
        }

        // ── Time ──────────────────────────────────────────────────────────

        [Fact]
        public async Task AnAnswerThatNeverComesIsAnEcrChannelTimeout()
        {
            using var server = FakeTcpTerminal.Always(TcpReply.Stall);
            var channel = new TcpEcrChannel("127.0.0.1", server.Port);
            var clock = Stopwatch.StartNew();

            var timeout = await Assert.ThrowsAsync<EcrChannelTimeout>(() =>
                channel.ExchangeAsync(Utf8("{}"), TimeSpan.FromMilliseconds(300)));

            Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(250), $"gave up early: {clock.Elapsed}");
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"gave up late: {clock.Elapsed}");
            Assert.Contains("did not answer in time", timeout.Message);
            Assert.IsAssignableFrom<IOException>(timeout);
        }

        [Fact]
        public async Task TheAnswerBudgetIsSeparateFromTheConnectBudget()
        {
            // The answer may take far longer than a connect ever should: a
            // cardholder is tapping a card. A short connect timeout must not cut
            // a slow answer short.
            using var server = new FakeTcpTerminal((_, __) =>
            {
                Thread.Sleep(600);
                return TcpReply.Answer("""{"responseCode":"00"}""");
            });
            var channel = new TcpEcrChannel("127.0.0.1", server.Port, TimeSpan.FromMilliseconds(200));

            var body = await channel.ExchangeAsync(Utf8("{}"), Generous);

            Assert.NotEmpty(body);
        }

        [Fact]
        public async Task ACallersCancellationWhileWaitingIsCancellationNotATimeout()
        {
            using var server = FakeTcpTerminal.Always(TcpReply.Stall);
            var channel = new TcpEcrChannel("127.0.0.1", server.Port);
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromMilliseconds(200));
            var clock = Stopwatch.StartNew();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                channel.ExchangeAsync(Utf8("{}"), Generous, cts.Token));

            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"cancellation was not prompt: {clock.Elapsed}");
        }

        [Fact]
        public async Task ATokenAlreadyCancelledNeverConnects()
        {
            using var server = FakeTcpTerminal.Always(TcpReply.Answer("{}"));
            var channel = new TcpEcrChannel("127.0.0.1", server.Port);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                channel.ExchangeAsync(Utf8("{}"), Generous, cts.Token));

            Assert.Empty(server.Requests);
        }

        // ── Failure to connect ────────────────────────────────────────────

        [Fact]
        public async Task ARefusedConnectionIsAnIoErrorNotATimeout()
        {
            var channel = new TcpEcrChannel("127.0.0.1", FakeTcpTerminal.ClosedPort(), TimeSpan.FromSeconds(2));

            var e = await Assert.ThrowsAnyAsync<IOException>(() => channel.ExchangeAsync(Utf8("{}"), Generous));

            Assert.IsNotType<EcrChannelTimeout>(e);
            Assert.IsType<SocketException>(e.InnerException);
            Assert.False(string.IsNullOrWhiteSpace(e.Message));
        }

        [Fact]
        public async Task AConnectThatTimesOutIsAnIoErrorNotAnEcrChannelTimeout()
        {
            // 192.0.2.0/24 is TEST-NET-1 (RFC 5737): never routable, so the connect
            // either hangs until our budget runs out or fails outright. Either way
            // nothing was sent and it must not read as "the terminal did not
            // answer".
            var channel = new TcpEcrChannel("192.0.2.1", 9100, TimeSpan.FromMilliseconds(300));
            var clock = Stopwatch.StartNew();

            var e = await Assert.ThrowsAnyAsync<IOException>(() => channel.ExchangeAsync(Utf8("{}"), Generous));

            Assert.IsNotType<EcrChannelTimeout>(e);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"connect budget ignored: {clock.Elapsed}");
        }

        // ── A terminal that misbehaves ────────────────────────────────────

        [Fact]
        public async Task AServerThatClosesWithoutAnAnswerIsAClosedConnection()
        {
            using var server = FakeTcpTerminal.Always(TcpReply.Drop);
            var channel = new TcpEcrChannel("127.0.0.1", server.Port);

            var e = await Assert.ThrowsAsync<IOException>(() => channel.ExchangeAsync(Utf8("{}"), Generous));

            Assert.Contains("closed the connection", e.Message);
            Assert.Contains("response header", e.Message);
        }

        [Fact]
        public async Task AServerThatClosesMidResponseIsAClosedConnection()
        {
            using var server = FakeTcpTerminal.Always(TcpReply.Truncated);
            var channel = new TcpEcrChannel("127.0.0.1", server.Port);

            var e = await Assert.ThrowsAsync<IOException>(() => channel.ExchangeAsync(Utf8("{}"), Generous));

            Assert.Contains("closed the connection", e.Message);
            Assert.Contains("response body", e.Message);
        }

        [Fact]
        public async Task AZeroLengthAnswerIsAnEmptyMessage()
        {
            using var server = FakeTcpTerminal.Always(TcpReply.Bytes(0x00, 0x00));
            var channel = new TcpEcrChannel("127.0.0.1", server.Port);

            var e = await Assert.ThrowsAsync<IOException>(() => channel.ExchangeAsync(Utf8("{}"), Generous));

            Assert.Contains("empty message", e.Message);
        }

        [Fact]
        public async Task AConnectionAbortedAfterTheRequestWasWrittenIsALostConnection()
        {
            // The request went out, so the terminal may have acted on it: this is
            // reported as a closed connection (outcome unknown), not as an address
            // that could not be reached.
            using var server = FakeTcpTerminal.Always(TcpReply.Reset);
            var channel = new TcpEcrChannel("127.0.0.1", server.Port);

            var e = await Assert.ThrowsAsync<IOException>(() => channel.ExchangeAsync(Utf8("{}"), Generous));

            Assert.IsNotType<EcrChannelTimeout>(e);
            Assert.StartsWith("Terminal closed the connection", e.Message);
        }

        // ── Probe ─────────────────────────────────────────────────────────

        [Fact]
        public async Task AListeningTerminalProbesAsReachableWithoutSendingAnything()
        {
            using var server = FakeTcpTerminal.Always(TcpReply.Answer("{}"));
            var channel = new TcpEcrChannel("127.0.0.1", server.Port);

            var problem = await channel.ProbeAsync(TimeSpan.FromSeconds(2));

            Assert.Null(problem);
            await Task.Delay(100);
            Assert.Empty(server.Requests);
        }

        [Fact]
        public async Task ARefusedProbeSaysWhy()
        {
            var channel = new TcpEcrChannel("127.0.0.1", FakeTcpTerminal.ClosedPort());

            var problem = await channel.ProbeAsync(TimeSpan.FromSeconds(2));

            Assert.False(string.IsNullOrWhiteSpace(problem));
        }

        [Fact]
        public async Task AProbeOfAnUnroutableAddressGivesUpWithinItsBudget()
        {
            var channel = new TcpEcrChannel("192.0.2.1", 9100);
            var clock = Stopwatch.StartNew();

            var problem = await channel.ProbeAsync(TimeSpan.FromMilliseconds(300));

            Assert.False(string.IsNullOrWhiteSpace(problem));
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"probe budget ignored: {clock.Elapsed}");
        }

        [Fact]
        public async Task ACancelledProbeIsCancellationNotAnUnreachableTerminal()
        {
            using var server = FakeTcpTerminal.Always(TcpReply.Answer("{}"));
            var channel = new TcpEcrChannel("127.0.0.1", server.Port);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                channel.ProbeAsync(TimeSpan.FromSeconds(30), cts.Token));
        }

        // ── Shape ─────────────────────────────────────────────────────────

        [Fact]
        public void TheChannelDescribesItsEndpoint()
        {
            var channel = new TcpEcrChannel("10.0.0.5", 9200);

            Assert.Equal("10.0.0.5", channel.Host);
            Assert.Equal(9200, channel.Port);
            Assert.Equal("10.0.0.5:9200", channel.Endpoint);
        }

        [Fact]
        public void ThePortDefaultsToTheTerminalsListener()
        {
            Assert.Equal(9100, new TcpEcrChannel("10.0.0.5").Port);
            Assert.Equal(TimeSpan.FromSeconds(3), TcpEcrChannel.DefaultConnectTimeout);
        }

        [Fact]
        public void AHostIsRequired()
        {
            Assert.Throws<ArgumentNullException>(() => new TcpEcrChannel(null!));
        }

        [Fact]
        public void ATerminalBuiltFromAHostUsesTheConfigsPort()
        {
            var terminal = new EcrTerminal("10.0.0.5", "TW1", new EcrConfig { Port = 9333 });

            Assert.NotNull(terminal);
        }
    }
}
