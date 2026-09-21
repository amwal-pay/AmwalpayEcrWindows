using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AmwalPay.Ecr.Internal;
using System.Text.Json.Nodes;

namespace AmwalPay.Ecr.Tests
{
    /// <summary>
    /// A scripted <see cref="IEcrChannel"/>, so the terminal logic can be driven
    /// without a socket.
    /// <para>
    /// The script sees each request as text plus the zero-based number of the
    /// exchange, and returns the answer body. Returning <c>null</c> is a
    /// connection dropped without a reply, which is how a Wi-Fi failure looks
    /// from the till's side; the script may also throw any exception a real
    /// channel could.
    /// </para>
    /// </summary>
    internal sealed class FakeChannel : IEcrChannel
    {
        public const string DroppedMessage = "Terminal closed the connection while sending the response header";

        private readonly Func<string, int, string?> _script;
        private readonly List<string> _requests = new List<string>();
        private readonly List<TimeSpan> _timeouts = new List<TimeSpan>();

        public FakeChannel(Func<string, int, string?> script)
        {
            _script = script;
        }

        /// <summary>Answers every request the same way.</summary>
        public static FakeChannel Always(string? answer) => new FakeChannel((_, __) => answer);

        public string Endpoint => "fake:9100";

        public string Host => "fake";

        public int Port => 9100;

        /// <summary>What <see cref="ProbeAsync"/> reports; null means reachable.</summary>
        public string? ProbeError { get; set; }

        /// <summary>Every request body received, in order.</summary>
        public IReadOnlyList<string> Requests
        {
            get
            {
                lock (_requests)
                {
                    return _requests.ToArray();
                }
            }
        }

        /// <summary>The response timeout each exchange was given.</summary>
        public IReadOnlyList<TimeSpan> Timeouts
        {
            get
            {
                lock (_requests)
                {
                    return _timeouts.ToArray();
                }
            }
        }

        public Task<string?> ProbeAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProbeError);

        public Task<byte[]> ExchangeAsync(byte[] body, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            int connection;
            var request = Encoding.UTF8.GetString(body);
            lock (_requests)
            {
                connection = _requests.Count;
                _requests.Add(request);
                _timeouts.Add(timeout);
            }

            try
            {
                var reply = _script(request, connection);
                if (reply == null) throw new IOException(DroppedMessage);
                return Task.FromResult(Encoding.UTF8.GetBytes(reply));
            }
            catch (Exception e)
            {
                return Task.FromException<byte[]>(e);
            }
        }
    }

    /// <summary>Helpers for building the terminal's answers.</summary>
    internal static class Wire
    {
        public static JsonObject Obj(string text) => (JsonObject)JsonNode.Parse(text)!;

        /// <summary>The nonce the till put on a request; empty when it sent none.</summary>
        public static string NonceOf(string requestJson) => Obj(requestJson).Str("nonce");

        /// <summary>Signs <paramref name="body"/> the way the terminal does and returns the wire text.</summary>
        public static string Signed(JsonObject body, string key)
        {
            body[SecureHash.Field] = SecureHash.Sign(SecureHash.Compose(body), key);
            return EcrJson.Write(body);
        }

        /// <summary>A unified approval envelope echoing <paramref name="nonce"/>.</summary>
        public static JsonObject Approval(string nonce, string reference = "ORD-1") =>
            new JsonObject
            {
                ["success"] = true,
                ["responseCode"] = "00",
                ["message"] = "Approved",
                ["data"] = new JsonObject
                {
                    ["merchantReference"] = reference,
                    ["terminalSerial"] = "TW1",
                    ["amount"] = "1.234",
                    ["rrn"] = "622908275947",
                    ["authCode"] = "751486",
                },
                ["errorList"] = new JsonArray(),
                ["nonce"] = nonce,
            };

        /// <summary>A found-transaction envelope echoing <paramref name="nonce"/>.</summary>
        public static JsonObject FoundEnvelope(string nonce) =>
            new JsonObject
            {
                ["success"] = true,
                ["responseCode"] = "00",
                ["message"] = "Transaction found",
                ["data"] = new JsonObject
                {
                    ["transactionId"] = "abc-123",
                    ["stan"] = "000131",
                    ["status"] = "Approved",
                    ["amount"] = "1.234",
                    ["rrn"] = "622908275947",
                },
                ["errorList"] = new JsonArray(),
                ["nonce"] = nonce,
            };
    }
}
