namespace AmwalPay.Ecr.Internal
{
    /// <summary>Redaction helpers for SDK diagnostic output.</summary>
    internal static class EcrDiagnostics
    {
        /// <summary>
        /// Whether a signing secret is present — never length-adjacent hex slices
        /// of the secret itself.
        /// </summary>
        public static string SecretFingerprint(string hexSecret) =>
            hexSecret.Length == 0 ? "not configured" : "configured";
    }
}
