using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace AmwalPay.Ecr.Internal
{
    /// <summary>
    /// Message authentication for the ECR link.
    /// <para>
    /// A shared secret proves two things at once: that a request came from a till
    /// that holds the key, and that nothing on the network altered it on the way.
    /// Without it any device on the shop Wi-Fi can open the terminal's port and
    /// issue a refund, because the terminal has no other way to tell callers apart.
    /// </para>
    /// The construction deliberately matches the one the POS already uses to sign
    /// its backend traffic — sorted <c>key=value&amp;key=value</c>, HMAC-SHA256 — so
    /// both sides of this link and the backend link are the same shape.
    /// </summary>
    internal static class SecureHash
    {
        /// <summary>Field carrying the signature. Never part of what is signed.</summary>
        public const string Field = "secureHash";

        /// <summary>Log label for the Wi‑Fi/LAN signing key (distinct from Web Service).</summary>
        public const string KeyLabel = "Wi-Fi/LAN secure hash key";

        private const int NonceBytes = 16;

        /// <summary>
        /// The exact bytes both sides agree to sign.
        /// <list type="number">
        /// <item>Take every top-level entry whose value is a string, number or boolean.</item>
        /// <item>Drop nulls, nested objects and arrays.</item>
        /// <item>Drop <see cref="Field"/> itself.</item>
        /// <item>Sort what remains by key.</item>
        /// <item>Join as <c>key=value</c>, separated by <c>&amp;</c>.</item>
        /// </list>
        /// Nested values are left out on purpose: two JSON libraries cannot be
        /// relied on to serialise a nested object identically, and a signature that
        /// depended on key order inside a passthrough field would fail for reasons
        /// that have nothing to do with tampering.
        /// </summary>
        public static string Compose(JsonObject json) => EcrJson.Compose(json, Field);

        /// <summary>HMAC-SHA256 of <paramref name="message"/> under <paramref name="key"/>, as uppercase hex.</summary>
        public static string Sign(string message, string key)
        {
            using var hmac = new HMACSHA256(DecodeKey(key));
            return ToHex(hmac.ComputeHash(Encoding.UTF8.GetBytes(message)));
        }

        /// <summary>Whether <paramref name="json"/> carries a signature that matches <paramref name="key"/>.</summary>
        public static bool Verify(JsonObject json, string key)
        {
            var presented = json.Str(Field);
            if (presented.Length == 0) return false;
            return ConstantTimeEquals(presented, Sign(Compose(json), key));
        }

        /// <summary>A fresh 128-bit nonce, as uppercase hex.</summary>
        public static string NewNonce()
        {
            var bytes = new byte[NonceBytes];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }
            return ToHex(bytes);
        }

        /// <summary>
        /// The key as bytes. Hex, matching the convention the POS already uses for
        /// its backend secret — a 32-character key is 16 bytes, not 32. A key that
        /// is not hex is rejected rather than quietly read as text, because the two
        /// sides would then disagree about the bytes and every message would fail
        /// to verify with nothing to show why.
        /// </summary>
        internal static byte[] DecodeKey(string key, string name = "The ECR secret")
        {
            if (key.Length < 2 || key.Length % 2 != 0 || !IsHex(key))
            {
                throw new ArgumentException($"{name} must be an even-length hex string");
            }
            var bytes = new byte[key.Length / 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(key.Substring(i * 2, 2), 16);
            }
            return bytes;
        }

        internal static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) builder.Append(b.ToString("X2"));
            return builder.ToString();
        }

        private static bool IsHex(string text)
        {
            foreach (var c in text)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
            }
            return true;
        }

        /// <summary>
        /// Compares without leaking where two signatures first differ. Both are
        /// digests of the same length, so this is belt-and-braces rather than
        /// load-bearing — but a signature comparison is exactly the place the habit
        /// is worth keeping.
        /// </summary>
        private static bool ConstantTimeEquals(string a, string b)
        {
            var left = Encoding.UTF8.GetBytes(a.ToUpperInvariant());
            var right = Encoding.UTF8.GetBytes(b.ToUpperInvariant());
            var diff = left.Length ^ right.Length;
            for (var i = 0; i < left.Length && i < right.Length; i++)
            {
                diff |= left[i] ^ right[i];
            }
            return diff == 0;
        }
    }
}
