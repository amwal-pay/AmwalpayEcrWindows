using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace AmwalPay.Ecr
{
    /// <summary>
    /// How bytes reach a terminal, and nothing else.
    /// <para>
    /// Signing a request, checking the answer, and deciding what a response code
    /// means all belong to <see cref="EcrTerminal"/> and are the same however the
    /// bytes travel. A channel only has to carry one framed request and bring back
    /// one framed answer.
    /// </para>
    /// That split is not tidiness. On the terminal side the same protections lived
    /// inside whichever transport was written first, and the second transport
    /// silently shipped without any of them. A channel cannot make that mistake
    /// because it is never asked to decide anything.
    /// </summary>
    public interface IEcrChannel
    {
        /// <summary>What to show an operator when this link fails.</summary>
        string Endpoint { get; }

        /// <summary>
        /// The address side of <see cref="EcrReachability"/>, for links that have
        /// one. A cable has no host and no port: it reports its own name and a port
        /// of zero rather than inventing an address.
        /// </summary>
        string Host { get; }

        /// <summary>Zero when the link is not a socket.</summary>
        int Port { get; }

        /// <summary>Whether the terminal can be reached, or why not.</summary>
        /// <returns>null when reachable, otherwise the reason.</returns>
        Task<string?> ProbeAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends one request body and returns the answer body.
        /// <para>
        /// Framing is the channel's to apply — see <see cref="EcrFrames"/> — because
        /// only the channel knows how its medium delivers bytes.
        /// </para>
        /// </summary>
        /// <exception cref="EcrChannelTimeout">
        /// The terminal did not answer in time. Not a refusal: the payment may have
        /// completed and the terminal been unable to say so.
        /// </exception>
        /// <exception cref="IOException">Anything else that broke the link.</exception>
        Task<byte[]> ExchangeAsync(byte[] body, TimeSpan timeout, CancellationToken cancellationToken = default);
    }

    /// <summary>The terminal did not answer in time. The transaction may still have run.</summary>
    public sealed class EcrChannelTimeout : IOException
    {
        public EcrChannelTimeout(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// The terminal over TCP, on the shop's Wi-Fi.
    /// <para>
    /// One connection carries one request and one answer, which is how POS ECR
    /// ports behave: connect, send, wait for the cardholder, read the result,
    /// disconnect.
    /// </para>
    /// </summary>
    public sealed class TcpEcrChannel : IEcrChannel
    {
        /// <summary>
        /// Connecting is a handshake on the local network; it either happens
        /// quickly or the address is wrong. The long wait belongs to the answer,
        /// which includes a cardholder tapping a card. Prefer passing
        /// <see cref="EcrConfig.ConnectTimeout"/> from <see cref="EcrTerminal"/>.
        /// </summary>
        public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(3);

        private readonly TimeSpan _connectTimeout;

        public TcpEcrChannel(string host, int port = EcrConfig.DefaultPort, TimeSpan? connectTimeout = null)
        {
            Host = host ?? throw new ArgumentNullException(nameof(host));
            Port = port;
            _connectTimeout = connectTimeout ?? DefaultConnectTimeout;
        }

        public string Host { get; }

        public int Port { get; }

        public string Endpoint => $"{Host}:{Port}";

        public async Task<string?> ProbeAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            using var client = new TcpClient();
            try
            {
                await ConnectAsync(client, timeout, cancellationToken).ConfigureAwait(false);
                return null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e) when (e is IOException || e is SocketException || e is ObjectDisposedException)
            {
                return Describe(e);
            }
        }

        public async Task<byte[]> ExchangeAsync(byte[] body, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            // Framed first: a request too large for a frame is a local error, and
            // must not be reported as a connection the terminal dropped (which
            // would make the outcome look unknown and trigger an inquiry) when
            // nothing was ever sent.
            var packet = EcrFrames.Wrap(body);

            using var client = new TcpClient { NoDelay = true };

            try
            {
                await ConnectAsync(client, _connectTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is SocketException || e is ObjectDisposedException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new IOException(Describe(e), e);
            }

            // From here the request may reach the terminal, so a failure is no
            // longer "nothing happened". The answer wait is its own budget: a
            // socket read has no deadline of its own here, so the client is closed
            // under the read when it runs out.
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            using var registration = linked.Token.Register(Close, client);

            try
            {
                var stream = client.GetStream();
                await stream.WriteAsync(packet, 0, packet.Length, linked.Token).ConfigureAwait(false);
                await stream.FlushAsync(linked.Token).ConfigureAwait(false);

                return await EcrFrames.ReadBodyAsync(stream, linked.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (IsLinkFailure(e))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (timeoutCts.IsCancellationRequested)
                {
                    throw new EcrChannelTimeout("the terminal did not answer in time");
                }
                if (e is IOException framing && IsFramingFailure(framing))
                {
                    throw;
                }

                // A reset or abort after the request went out: the terminal may
                // have acted on it, so report it as a lost connection rather than
                // as an address that could not be reached.
                throw new IOException($"Terminal closed the connection: {Describe(e)}", e);
            }
        }

        /// <summary>
        /// Connects, giving up after <paramref name="timeout"/>. A connect timeout
        /// is reported as an ordinary I/O failure, not <see cref="EcrChannelTimeout"/>:
        /// nothing was sent, so there is no outcome to be unsure about.
        /// </summary>
        private async Task ConnectAsync(TcpClient client, TimeSpan timeout, CancellationToken cancellationToken)
        {
            // Checked up front: a token that is already cancelled closes the client
            // the instant it is registered, and connecting a closed TcpClient throws
            // NullReferenceException rather than anything catchable as a link failure.
            cancellationToken.ThrowIfCancellationRequested();

            using var timeoutCts = new CancellationTokenSource(timeout <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            using var registration = linked.Token.Register(Close, client);

            try
            {
                await client.ConnectAsync(Host, Port).ConfigureAwait(false);
            }
            catch (Exception e) when (IsLinkFailure(e) || (e is NullReferenceException && linked.IsCancellationRequested))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (timeoutCts.IsCancellationRequested)
                {
                    throw new IOException($"Connection timed out after {timeout.TotalMilliseconds:0} ms");
                }
                throw;
            }
        }

        private static void Close(object? state)
        {
            try
            {
                ((TcpClient)state!).Close();
            }
            catch (Exception)
            {
                // Closing is how a wait is cut short; there is nothing to do if it is already closed.
            }
        }

        private static bool IsLinkFailure(Exception e) =>
            e is IOException || e is SocketException || e is ObjectDisposedException
            || e is InvalidOperationException || e is OperationCanceledException;

        private static bool IsFramingFailure(IOException e) =>
            e.Message.Contains("closed the connection") || e.Message.Contains("empty message");

        private static string Describe(Exception e)
        {
            var root = e is IOException && e.InnerException is SocketException inner ? inner : e;
            return string.IsNullOrEmpty(root.Message) ? root.GetType().Name : root.Message;
        }
    }
}
