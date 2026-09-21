using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AmwalPay.Ecr.Tests
{
    /// <summary>What a <see cref="FakeTcpTerminal"/> does with one connection.</summary>
    internal sealed class TcpReply
    {
        public enum Kind
        {
            Answer,
            Bytes,
            Drop,
            Reset,
            Stall,
            Truncated,
        }

        private TcpReply(Kind kind, byte[]? bytes = null)
        {
            Type = kind;
            Payload = bytes;
        }

        public Kind Type { get; }

        public byte[]? Payload { get; }

        /// <summary>A well-formed framed answer.</summary>
        public static TcpReply Answer(string json) => new TcpReply(Kind.Answer, Encoding.UTF8.GetBytes(json));

        /// <summary>Exactly these bytes, framing included, then a clean close.</summary>
        public static TcpReply Bytes(params byte[] bytes) => new TcpReply(Kind.Bytes, bytes);

        /// <summary>Close without saying anything (a FIN).</summary>
        public static TcpReply Drop { get; } = new TcpReply(Kind.Drop);

        /// <summary>Abort the connection (a RST), as a crashing terminal or a NAT would.</summary>
        public static TcpReply Reset { get; } = new TcpReply(Kind.Reset);

        /// <summary>Accept, read the request, and never answer.</summary>
        public static TcpReply Stall { get; } = new TcpReply(Kind.Stall);

        /// <summary>Promise 64 bytes, send 10, close.</summary>
        public static TcpReply Truncated { get; } = new TcpReply(Kind.Truncated);
    }

    /// <summary>
    /// A stand-in terminal on a real loopback socket, for the tests that exist
    /// to prove what <see cref="TcpEcrChannel"/> does with real TCP.
    /// <para>
    /// Every connection is served on its own task so a stalled one cannot block
    /// the connection that follows it. The script sees the request text and the
    /// zero-based number of the connection.
    /// </para>
    /// </summary>
    internal sealed class FakeTcpTerminal : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly Func<string, int, TcpReply> _script;
        private readonly List<string> _requests = new List<string>();
        private readonly List<byte[]> _rawRequests = new List<byte[]>();
        private readonly Task _acceptLoop;
        private int _connections;

        public FakeTcpTerminal(Func<string, int, TcpReply> script)
        {
            _script = script;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _acceptLoop = Task.Run(AcceptLoop);
        }

        public static FakeTcpTerminal Always(TcpReply reply) => new FakeTcpTerminal((_, __) => reply);

        public int Port { get; }

        /// <summary>How many connections were accepted.</summary>
        public int Connections => Volatile.Read(ref _connections);

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

        /// <summary>Every request as it arrived on the wire, header included.</summary>
        public IReadOnlyList<byte[]> RawRequests
        {
            get
            {
                lock (_requests)
                {
                    return _rawRequests.ToArray();
                }
            }
        }

        /// <summary>A port nothing is listening on: bound, noted, released.</summary>
        public static int ClosedPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private async Task AcceptLoop()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;
                }

                var connection = Interlocked.Increment(ref _connections) - 1;
                _ = Task.Run(() => Serve(client, connection));
            }
        }

        private async Task Serve(TcpClient client, int connection)
        {
            try
            {
                var stream = client.GetStream();
                var header = new byte[2];
                await ReadExactly(stream, header).ConfigureAwait(false);
                var length = (header[0] << 8) | header[1];
                var body = new byte[length];
                await ReadExactly(stream, body).ConfigureAwait(false);

                var request = Encoding.UTF8.GetString(body);
                lock (_requests)
                {
                    _requests.Add(request);
                    _rawRequests.Add(Concat(header, body));
                }

                var reply = _script(request, connection);
                switch (reply.Type)
                {
                    case TcpReply.Kind.Answer:
                    {
                        var bytes = reply.Payload!;
                        var packet = Concat(new[] { (byte)(bytes.Length >> 8), (byte)(bytes.Length & 0xFF) }, bytes);
                        await stream.WriteAsync(packet, 0, packet.Length).ConfigureAwait(false);
                        await stream.FlushAsync().ConfigureAwait(false);
                        break;
                    }
                    case TcpReply.Kind.Bytes:
                        await stream.WriteAsync(reply.Payload!, 0, reply.Payload!.Length).ConfigureAwait(false);
                        await stream.FlushAsync().ConfigureAwait(false);
                        break;
                    case TcpReply.Kind.Truncated:
                    {
                        var partial = Concat(new byte[] { 0x00, 0x40 }, new byte[10]);
                        await stream.WriteAsync(partial, 0, partial.Length).ConfigureAwait(false);
                        await stream.FlushAsync().ConfigureAwait(false);
                        break;
                    }
                    case TcpReply.Kind.Reset:
                        client.Client.LingerState = new LingerOption(true, 0);
                        break;
                    case TcpReply.Kind.Stall:
                        try
                        {
                            await Task.Delay(Timeout.Infinite, _stop.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                        }
                        break;
                    case TcpReply.Kind.Drop:
                        break;
                }
            }
            catch (Exception)
            {
                // The client going away first is a legitimate way for a test to end.
            }
            finally
            {
                client.Close();
            }
        }

        private static async Task ReadExactly(NetworkStream stream, byte[] buffer)
        {
            var read = 0;
            while (read < buffer.Length)
            {
                var n = await stream.ReadAsync(buffer, read, buffer.Length - read).ConfigureAwait(false);
                if (n <= 0) throw new EndOfStreamException();
                read += n;
            }
        }

        private static byte[] Concat(byte[] first, byte[] second)
        {
            var all = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, all, 0, first.Length);
            Buffer.BlockCopy(second, 0, all, first.Length, second.Length);
            return all;
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            try
            {
                _acceptLoop.Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception)
            {
            }
            _stop.Dispose();
        }
    }
}
