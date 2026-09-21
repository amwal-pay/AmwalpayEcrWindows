namespace AmwalPay.Ecr.Internal
{
    /// <summary>Structured request/response logging for Wi‑Fi / LAN ECR TCP calls.</summary>
    internal static class EcrLanLog
    {
        private const string Prefix = "[Wi-Fi/LAN]";

        /// <summary>
        /// Logs outgoing request signing so the simulator can confirm the Wi‑Fi/LAN
        /// key — not the Web Service key — is used.
        /// </summary>
        public static void Signing(
            IEcrLogger logger,
            string keyLabel,
            string secretKey,
            string payloadToSign,
            string secureHash,
            string nonce = "")
        {
            logger.Debug($"{Prefix} sign: HMAC-SHA256(sorted key=value payload, Hex.parse(secret)) → uppercase hex");
            logger.Debug($"{Prefix} signing key ({keyLabel}): {EcrDiagnostics.SecretFingerprint(secretKey)}");
            logger.Debug($"{Prefix} payload to sign (excl. secureHash): {payloadToSign}");
            if (nonce.Length != 0)
            {
                logger.Debug($"{Prefix} nonce: {nonce}");
            }
            logger.Debug($"{Prefix} secureHash: {secureHash}");
        }

        /// <summary>Logs response signature verification.</summary>
        public static void VerifyResponse(
            IEcrLogger logger,
            string keyLabel,
            string secretKey,
            string payloadToSign,
            string secureHash,
            bool verified)
        {
            logger.Debug($"{Prefix} verify: HMAC-SHA256(sorted key=value payload, Hex.parse(secret)) → uppercase hex");
            logger.Debug($"{Prefix} verify key ({keyLabel}): {EcrDiagnostics.SecretFingerprint(secretKey)}");
            logger.Debug($"{Prefix} payload to verify (excl. secureHash): {payloadToSign}");
            logger.Debug($"{Prefix} secureHash: {secureHash}");
            logger.Debug(verified
                ? $"{Prefix} ✓ response signature valid"
                : $"{Prefix} ✗ response signature invalid");
        }
    }
}
