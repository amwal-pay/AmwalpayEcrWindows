using System;

namespace AmwalPay.Ecr
{
    /// <summary>
    /// How this ECR identifies itself and how it talks to a terminal.
    /// <para>
    /// The defaults describe an Omani deployment on the standard port; a client in
    /// another currency or on another port overrides them.
    /// </para>
    /// <code>
    /// var config = new EcrConfig { EcrId = "TILL-01", SecureHashKey = "0123456789ABCDEF..." };
    /// var stricter = config with { ResponseTimeout = TimeSpan.FromSeconds(60) };
    /// </code>
    /// Invalid values are rejected the moment they are assigned, so a key with a
    /// stray space fails at start-up rather than on the shop floor as a decline
    /// the cashier cannot explain.
    /// </summary>
    public sealed record EcrConfig
    {
        /// <summary>The port Amwal POS terminals listen on for ECR requests.</summary>
        public const int DefaultPort = 9100;

        /// <summary>
        /// HTTP read timeout for Web Service REST calls (<c>/Ecr/Sale</c>, void,
        /// refund, inquiry). Longer than the LAN default: the Hub wait includes
        /// card present + PIN, and 120s was cutting sales off early.
        /// </summary>
        public static readonly TimeSpan WebServiceResponseTimeout = TimeSpan.FromSeconds(130);

        /// <summary>64 bits is the shortest key worth calling a secret.</summary>
        private const int MinSecretLength = 16;

        private int _minorUnitDigits = 3;
        private int _port = DefaultPort;
        private string _secureHashKey = "";

        /// <summary>
        /// Identifies this cash register to the terminal, and appears on the
        /// terminal's records of the transaction.
        /// </summary>
        public string EcrId { get; init; } = "ECR01";

        /// <summary>ISO 4217 numeric code, e.g. <c>512</c> for OMR.</summary>
        public string CurrencyCode { get; init; } = "512";

        /// <summary>
        /// Decimal places the currency has: 3 for OMR, 2 for USD. Amounts travel
        /// as minor units, so this decides how 1.234 is written.
        /// </summary>
        public int MinorUnitDigits
        {
            get => _minorUnitDigits;
            init
            {
                if (value < 0 || value > 4)
                {
                    throw new ArgumentException("minorUnitDigits must be between 0 and 4", nameof(MinorUnitDigits));
                }
                _minorUnitDigits = value;
            }
        }

        /// <summary>The terminal's ECR listener port.</summary>
        public int Port
        {
            get => _port;
            init
            {
                if (value < 1 || value > 65535)
                {
                    throw new ArgumentException("port must be between 1 and 65535", nameof(Port));
                }
                _port = value;
            }
        }

        /// <summary>How long to wait for the terminal to accept a connection.</summary>
        public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// How long to wait for the result once the request is sent. Generous by
        /// default: the cardholder has to present a card and may be asked for a PIN.
        /// </summary>
        public TimeSpan ResponseTimeout { get; init; } = TimeSpan.FromSeconds(120);

        /// <summary>
        /// How long <see cref="EcrTerminal.IsReachableAsync"/> waits. Short on
        /// purpose — a terminal on the same network answers a handshake in
        /// milliseconds or is not there at all.
        /// </summary>
        public TimeSpan ProbeTimeout { get; init; } = TimeSpan.FromSeconds(3);

        /// <summary>
        /// The secret this till shares with the terminal, as hex. <b>The app owns
        /// persistence and chooses which secret to pass</b> for the selected
        /// terminal mode (LAN vs Web Service use different secrets and signing
        /// formats, but both are supplied through this single field). LAN ECR signs
        /// sorted <c>key=value</c> pairs; Web Service ECR signs the same shape under
        /// <c>secureHashValue</c>, with HMAC-SHA256 under the hex-decoded key.
        /// </summary>
        public string SecureHashKey
        {
            get => _secureHashKey;
            init
            {
                var key = value ?? "";
                if (key.Length != 0 && !IsValidSecret(key))
                {
                    throw new ArgumentException(
                        "secureHashKey must be an even-length hex string of at least 16 " +
                        "characters, or empty to send unsigned messages",
                        nameof(SecureHashKey));
                }
                _secureHashKey = key;
            }
        }

        /// <summary>Backend merchant ID — used for Web Service / Hub ECR.</summary>
        public string MerchantId { get; init; } = "";

        /// <summary>Backend terminal ID — used for Web Service / Hub ECR.</summary>
        public string TerminalId { get; init; } = "";

        /// <summary>
        /// Hub deployment (SIT, UAT, PROD) for Web Service. Hub URLs are resolved
        /// inside the SDK and are not configurable by the app.
        /// </summary>
        public EcrEnvironment Environment { get; init; } = EcrEnvironments.Default;

        /// <summary>
        /// Whether a transaction whose answer never arrived is followed by an
        /// inquiry, so the till learns what actually happened instead of being left
        /// to guess.
        /// <para>
        /// On by default, and safe: an inquiry reads and nothing more, so repeating
        /// one changes nothing — unlike sending the sale again, which charges the
        /// cardholder twice and is never done automatically. The finding arrives on
        /// <see cref="EcrResult.Failed.Recovered"/>.
        /// </para>
        /// Turn it off only if the till runs its own reconciliation and would rather
        /// not have the extra round trip on a failure.
        /// </summary>
        public bool AutoInquireOnFailure { get; init; } = true;

        /// <summary>Whether this till signs what it sends.</summary>
        internal bool SignsMessages => _secureHashKey.Length != 0;

        /// <summary>Whether <paramref name="key"/> is acceptable for <see cref="SecureHashKey"/>.</summary>
        public static bool IsValidSecureHashKey(string? key) =>
            string.IsNullOrEmpty(key) || IsValidSecret(key!);

        internal static bool IsValidSecret(string key)
        {
            if (key.Length < MinSecretLength || key.Length % 2 != 0) return false;
            foreach (var c in key)
            {
                var hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        /// <summary>Never prints the secret.</summary>
        public override string ToString() =>
            $"EcrConfig(ecrId={EcrId}, port={Port}, environment={Environment}, " +
            $"merchantId={MerchantId}, terminalId={TerminalId}, " +
            $"secureHashKey={(_secureHashKey.Length == 0 ? "empty" : "<redacted>")})";
    }
}
