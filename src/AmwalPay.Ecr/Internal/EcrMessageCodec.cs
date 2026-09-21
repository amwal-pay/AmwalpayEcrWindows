using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;

namespace AmwalPay.Ecr.Internal
{
    /// <summary>
    /// The wire format.
    /// <para>
    /// Framing: a 2-byte big-endian unsigned length header followed by that many
    /// bytes of UTF-8 JSON (see <see cref="EcrFrames"/>). This is the single place
    /// the JSON format lives. The terminal has one file that matches it; the two
    /// must change together.
    /// </para>
    /// </summary>
    internal static class EcrMessageCodec
    {
        /// <summary>Frames a message ready to write to the socket.</summary>
        public static byte[] Encode(EcrMessage message)
        {
            var body = Encoding.UTF8.GetBytes(EcrJson.Write(message.Json));
            if (body.Length > EcrFrames.MaxBodyBytes)
            {
                throw new IOException($"Request exceeds {EcrFrames.MaxBodyBytes} bytes");
            }
            return EcrFrames.Wrap(body);
        }

        /// <summary>
        /// One message body, as the object it claims to be. Separate from reading
        /// it off the wire, because a channel that is not a stream — a USB
        /// transfer, a Bluetooth packet — already holds the bytes by the time
        /// anyone wants them parsed.
        /// </summary>
        public static JsonObject Parse(byte[] body)
        {
            var payload = Encoding.UTF8.GetString(body);
            try
            {
                // Rejects null, arrays, bare values and repeated keys as well as
                // invalid text: see EcrJson.ParseObject.
                return EcrJson.ParseObject(payload);
            }
            catch (Exception e)
            {
                throw new IOException($"Terminal returned a message that is not valid JSON: {payload}", e);
            }
        }

        /// <summary>Reads exactly one framed message, blocking until it arrives.</summary>
        public static JsonObject Decode(Stream input) => Parse(EcrFrames.ReadBody(input));
    }
}
