using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AmwalPay.Ecr.Internal;

namespace AmwalPay.Ecr.WebService
{
    /// <summary>
    /// Web Service ECR request signing.
    /// <para>
    /// Platform <c>calcHash(obj, secret)</c> — <c>obj</c> is <b>not</b> the raw JSON
    /// body. It is the sorted <c>key=value&amp;key=value</c> string built from every
    /// top-level scalar in the request <b>except</b> <see cref="Field"/>, the same
    /// rules as the LAN <c>SecureHash.Compose</c> and the POS
    /// <c>SecureHashInterceptor.composeData</c>.
    /// </para>
    /// <code>
    /// function calcHash(obj, secret) {
    ///   const crypted = CryptoJS.HmacSHA256(obj, CryptoJS.enc.Hex.parse(secret))
    ///   return CryptoJS.enc.Hex.stringify(crypted).toUpperCase();
    /// }
    /// </code>
    /// </summary>
    internal static class WebServiceSecureHash
    {
        /// <summary>Field added to signed Web Service request bodies.</summary>
        public const string Field = "secureHashValue";

        /// <summary>Log label for the Web Service signing key (distinct from Wi‑Fi/LAN).</summary>
        public const string KeyLabel = "Web Service secure hash key";

        /// <summary>
        /// HMAC-SHA256 of <paramref name="message"/> under hex-decoded
        /// <paramref name="secret"/>, as uppercase hex. Matches <c>calcHash(obj, secret)</c>
        /// — returns an empty string on any error.
        /// </summary>
        public static string CalcHash(string message, string secret)
        {
            if (secret.Length == 0) return "";
            try
            {
                using var hmac = new HMACSHA256(SecureHash.DecodeKey(secret, "The Web Service secret"));
                return SecureHash.ToHex(hmac.ComputeHash(Encoding.UTF8.GetBytes(message)));
            }
            catch (Exception)
            {
                return "";
            }
        }

        public static string Sign(string payload, string secretKey) => CalcHash(payload, secretKey);

        /// <summary>Signs <paramref name="body"/> after building the sorted <c>key=value</c> string.</summary>
        public static string Sign(JsonObject body, string secretKey) => CalcHash(Compose(body), secretKey);

        /// <summary>Returns <paramref name="body"/> with <see cref="Field"/> set from <paramref name="secretKey"/>.</summary>
        public static JsonObject WithHash(JsonObject body, string secretKey)
        {
            if (secretKey.Length == 0) return body;
            var hash = CalcHash(Compose(body), secretKey);
            if (hash.Length == 0)
            {
                throw new ArgumentException(
                    "Web Service secureHashValue could not be computed — check the secure hash key");
            }
            var signed = (JsonObject)body.DeepClone();
            signed.Remove(Field);
            signed[Field] = hash;
            return signed;
        }

        /// <summary>Sorted <c>key=value&amp;…</c> signing string (excludes <see cref="Field"/>).</summary>
        public static string PayloadString(JsonObject body) => Compose(body);

        /// <summary>
        /// The exact bytes both sides agree to sign. Same rules as LAN
        /// <c>SecureHash.Compose</c>, but excluding <see cref="Field"/> instead of
        /// <c>secureHash</c>.
        /// </summary>
        public static string Compose(JsonObject json) => EcrJson.Compose(json, Field);
    }
}
