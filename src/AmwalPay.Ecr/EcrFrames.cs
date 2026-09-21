using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AmwalPay.Ecr
{
    /// <summary>
    /// The wire framing, for anyone writing an <see cref="IEcrChannel"/>.
    /// <para>
    /// A message is a 2-byte big-endian length followed by that many bytes of
    /// UTF-8 JSON. A stream carries no message boundaries, so the header is what
    /// says where one message ends and the next begins.
    /// </para>
    /// Public because a channel outside this library — a USB cable, a Bluetooth
    /// socket — has to frame the same way, and a second private implementation
    /// of "two bytes then the body" is a second chance to get it subtly wrong.
    /// </summary>
    public static class EcrFrames
    {
        public const int HeaderBytes = 2;

        /// <summary>The header is two bytes, so nothing longer can be described.</summary>
        public const int MaxBodyBytes = 0xFFFF;

        /// <summary>Puts a length header in front of <paramref name="body"/>.</summary>
        public static byte[] Wrap(byte[] body)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (body.Length > MaxBodyBytes)
            {
                throw new IOException($"Message of {body.Length} bytes does not fit a frame");
            }

            var packet = new byte[HeaderBytes + body.Length];
            packet[0] = (byte)((body.Length >> 8) & 0xFF);
            packet[1] = (byte)(body.Length & 0xFF);
            Buffer.BlockCopy(body, 0, packet, HeaderBytes, body.Length);
            return packet;
        }

        /// <summary>The body length a header announces.</summary>
        public static int BodyLength(byte[] header)
        {
            if (header == null) throw new ArgumentNullException(nameof(header));
            return ((header[0] & 0xFF) << 8) | (header[1] & 0xFF);
        }

        /// <summary>
        /// Reads exactly one message body, blocking until it arrives or the
        /// stream's own read timeout fires.
        /// </summary>
        public static byte[] ReadBody(Stream input)
        {
            var header = new byte[HeaderBytes];
            ReadOrFail(input, header, "response header");
            var length = BodyLength(header);
            if (length <= 0) throw new IOException("Terminal returned an empty message");

            var body = new byte[length];
            ReadOrFail(input, body, "response body");
            return body;
        }

        /// <summary>Asynchronous <see cref="ReadBody"/>.</summary>
        public static async Task<byte[]> ReadBodyAsync(Stream input, CancellationToken cancellationToken = default)
        {
            var header = new byte[HeaderBytes];
            await ReadOrFailAsync(input, header, "response header", cancellationToken).ConfigureAwait(false);
            var length = BodyLength(header);
            if (length <= 0) throw new IOException("Terminal returned an empty message");

            var body = new byte[length];
            await ReadOrFailAsync(input, body, "response body", cancellationToken).ConfigureAwait(false);
            return body;
        }

        private static void ReadOrFail(Stream input, byte[] buffer, string what)
        {
            var read = 0;
            while (read < buffer.Length)
            {
                var n = input.Read(buffer, read, buffer.Length - read);
                if (n <= 0) throw Closed(what);
                read += n;
            }
        }

        private static async Task ReadOrFailAsync(Stream input, byte[] buffer, string what, CancellationToken ct)
        {
            var read = 0;
            while (read < buffer.Length)
            {
                var n = await input.ReadAsync(buffer, read, buffer.Length - read, ct).ConfigureAwait(false);
                if (n <= 0) throw Closed(what);
                read += n;
            }
        }

        private static IOException Closed(string what) =>
            new IOException($"Terminal closed the connection while sending the {what}");
    }
}
