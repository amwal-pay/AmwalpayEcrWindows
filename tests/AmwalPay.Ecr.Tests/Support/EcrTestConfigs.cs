namespace AmwalPay.Ecr.Tests
{
    /// <summary>
    /// Shared <see cref="EcrConfig"/> values for unit tests.
    /// <para>
    /// Secrets are synthetic placeholders that happen to be valid hex, so
    /// <see cref="EcrConfig"/> accepts them. Never commit real Amwal keys.
    /// </para>
    /// </summary>
    internal static class EcrTestConfigs
    {
        /// <summary>Wi-Fi / LAN / USB cable signing placeholder.</summary>
        public const string SecureHashKeyEcrWifi = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        /// <summary>Alternate Wi-Fi placeholder, for wrong-key verification cases.</summary>
        public const string SecureHashKeyEcrWifiOther = "cccccccccccccccccccccccccccccccc";

        /// <summary>Web Service REST signing placeholder.</summary>
        public const string SecureHashKeyWebService = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        public static EcrConfig Lan { get; } = new EcrConfig { SecureHashKey = SecureHashKeyEcrWifi };

        public static EcrConfig LanOther { get; } = new EcrConfig { SecureHashKey = SecureHashKeyEcrWifiOther };

        public static EcrConfig WebService { get; } = new EcrConfig
        {
            MerchantId = "13593",
            TerminalId = "742001",
            Environment = EcrEnvironment.UAT,
            SecureHashKey = SecureHashKeyWebService,
        };
    }
}
