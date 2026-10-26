using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using AmwalPay.Ecr.Internal;
using AmwalPay.Ecr.WebService;

namespace AmwalPay.Ecr
{
    /// <summary>
    /// Validated SDK configuration for one terminal link.
    /// <para>
    /// Built by <see cref="EcrSessions.Plan"/> so the app does not duplicate
    /// transport rules, signing-key labels, or <see cref="EcrConfig"/> invariants.
    /// </para>
    /// </summary>
    public sealed record EcrSessionPlan(EcrLink Link, EcrConfig Config, IReadOnlyList<string>? Issues = null)
    {
        /// <summary>What is wrong with this plan, or empty when it can be opened.</summary>
        public IReadOnlyList<string> Issues { get; init; } = Issues ?? Array.Empty<string>();

        public bool IsReady => Issues.Count == 0;

        public bool UsesLan => Link is EcrLink.Lan;

        public bool UsesWebService => Link is EcrLink.WebService;

        public bool UsesUsbCable => Link is EcrLink.UsbCable;

        public string SigningKeyLabel => EcrSessions.SigningKeyLabelFor(Link);

        public bool SigningKeyConfigured => Config.SecureHashKey.Length != 0;

        /// <summary>Operator-facing connection summary — no secrets.</summary>
        public string ConnectionSummary
        {
            get
            {
                switch (Link)
                {
                    case EcrLink.Lan lan:
                        return $"{lan.Host}:{Config.Port}";
                    case EcrLink.UsbCable cable:
                        return cable.ComPort.Length != 0 ? cable.ComPort : "USB cable";
                    case EcrLink.WebService web:
                        var summary = new StringBuilder(Config.Environment.ToString());
                        if (web.MerchantId.Length != 0) summary.Append(" · merchant ").Append(web.MerchantId);
                        if (web.TerminalId.Length != 0) summary.Append(" · terminal ").Append(web.TerminalId);
                        return summary.ToString();
                    default:
                        throw new InvalidOperationException($"Unsupported ECR link: {Link}");
                }
            }
        }
    }

    /// <summary>Resolves <see cref="EcrLink"/> + credentials into a validated <see cref="EcrSessionPlan"/> and clients.</summary>
    public static class EcrSessions
    {
        /// <summary>Shared signing-key labels for <see cref="EcrLink"/> — one definition for plan and validation.</summary>
        internal static string SigningKeyLabelFor(EcrLink link) => link switch
        {
            EcrLink.Lan _ => SecureHash.KeyLabel,
            EcrLink.UsbCable _ => SecureHash.KeyLabel,
            EcrLink.WebService _ => WebServiceSecureHash.KeyLabel,
            _ => throw new InvalidOperationException($"Unsupported ECR link: {link}"),
        };

        /// <summary>
        /// Validates <paramref name="link"/> and <paramref name="config"/> without
        /// opening a connection. Returns issues rather than throwing so the UI can
        /// show what is missing.
        /// </summary>
        /// <param name="rawSecureHashKey">The key as the operator typed it. Defaults to <see cref="EcrConfig.SecureHashKey"/>.</param>
        public static EcrSessionPlan Plan(EcrLink link, EcrConfig config, string? rawSecureHashKey = null)
        {
            if (link == null) throw new ArgumentNullException(nameof(link));
            if (config == null) throw new ArgumentNullException(nameof(config));

            var issues = new List<string>();
            var safe = SafeConfig(config, rawSecureHashKey ?? config.SecureHashKey, issues);

            switch (link)
            {
                case EcrLink.Lan lan:
                    ValidateLan(lan, issues);
                    break;
                case EcrLink.UsbCable _:
                    // Nothing to validate: a cable has no address to get wrong.
                    // Whether one is plugged in is answered by the probe, not by
                    // configuration.
                    break;
                case EcrLink.WebService web:
                    ValidateWebService(web, issues);
                    break;
            }

            var resolved = ResolveConfig(link, safe);

            if (resolved.SecureHashKey.Length == 0)
            {
                issues.Add($"{SigningKeyLabelFor(link)} is not configured");
            }

            return new EcrSessionPlan(link, resolved, issues.Distinct().ToList());
        }

        /// <summary>
        /// Opens the transport that <paramref name="plan"/> describes.
        /// <para>
        /// One dispatch for Wi-Fi / USB cable / Web Service so callers (and recovery
        /// paths) cannot diverge on which client they build.
        /// </para>
        /// </summary>
        /// <param name="usbChannel">Required when <see cref="EcrSessionPlan.UsesUsbCable"/>; ignored otherwise.</param>
        public static EcrOpenedSession Open(
            string terminalSerial,
            EcrSessionPlan plan,
            Func<IEcrChannel>? usbChannel = null,
            IEcrLogger? logger = null)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (!plan.IsReady)
            {
                throw new ArgumentException(
                    $"Cannot open a session with unresolved issues: {string.Join(", ", plan.Issues)}", nameof(plan));
            }
            logger ??= EcrLogger.None;

            if (plan.UsesUsbCable)
            {
                var channel = usbChannel?.Invoke()
                    ?? throw new InvalidOperationException("USB cable sessions require a channel factory");
                return new EcrOpenedSession(plan, UsbCableTerminal(terminalSerial, plan, channel, logger), null);
            }
            if (plan.UsesLan)
            {
                return new EcrOpenedSession(plan, LanTerminal(terminalSerial, plan, logger), null);
            }
            if (plan.UsesWebService)
            {
                return new EcrOpenedSession(plan, null, WebServiceTerminal(terminalSerial, plan, logger));
            }
            throw new InvalidOperationException($"Unsupported ECR link: {plan.Link}");
        }

        public static EcrTerminal LanTerminal(string terminalSerial, EcrSessionPlan plan, IEcrLogger? logger = null)
        {
            if (!plan.UsesLan) throw new ArgumentException("plan is not for Wi-Fi ECR", nameof(plan));
            var lan = (EcrLink.Lan)plan.Link;
            return new EcrTerminal(lan.Host, terminalSerial, plan.Config, logger ?? EcrLogger.None);
        }

        /// <summary>
        /// A terminal reached down a USB cable.
        /// <para>
        /// The channel comes from the caller because finding a cable is a platform
        /// job, and this library carries no native USB dependency on purpose.
        /// Everything above the channel is identical to <see cref="LanTerminal"/>:
        /// the same signing, the same messages, the same answers.
        /// </para>
        /// </summary>
        public static EcrTerminal UsbCableTerminal(
            string terminalSerial,
            EcrSessionPlan plan,
            IEcrChannel channel,
            IEcrLogger? logger = null)
        {
            if (!plan.UsesUsbCable) throw new ArgumentException("plan is not for USB cable ECR", nameof(plan));
            return new EcrTerminal(channel, terminalSerial, plan.Config, logger ?? EcrLogger.None);
        }

        public static EcrWebServiceTerminal WebServiceTerminal(string terminalSerial, EcrSessionPlan plan, IEcrLogger? logger = null)
        {
            if (!plan.UsesWebService) throw new ArgumentException("plan is not for Web Service ECR", nameof(plan));
            return new EcrWebServiceTerminal(terminalSerial, plan.Config, logger ?? EcrLogger.None);
        }

        private static EcrConfig SafeConfig(EcrConfig requested, string rawSecureHashKey, List<string> issues)
        {
            var key = (rawSecureHashKey ?? "").Trim();
            var keyValid = EcrConfig.IsValidSecureHashKey(key);
            if (key.Length != 0 && !keyValid)
            {
                issues.Add("Secure hash key must be an even-length hex string of at least 16 characters");
            }
            var port = Math.Min(Math.Max(requested.Port, 1), 65535);
            var sanitizedKey = key.Length != 0 && keyValid ? key : "";

            try
            {
                return requested with { Port = port, SecureHashKey = sanitizedKey };
            }
            catch (ArgumentException e)
            {
                issues.Add(string.IsNullOrEmpty(e.Message) ? "Invalid ECR configuration" : e.Message);
                return new EcrConfig
                {
                    EcrId = requested.EcrId,
                    Port = port,
                    SecureHashKey = "",
                    MerchantId = requested.MerchantId,
                    TerminalId = requested.TerminalId,
                    Environment = requested.Environment,
                };
            }
        }

        private static void ValidateLan(EcrLink.Lan link, List<string> issues)
        {
            if (string.IsNullOrWhiteSpace(link.Host)) issues.Add("IP address is required for Wi-Fi ECR");
            if (link.Port < 1 || link.Port > 65535)
            {
                issues.Add($"Port must be between 1 and 65535 (default {EcrConfig.DefaultPort})");
            }
        }

        private static void ValidateWebService(EcrLink.WebService link, List<string> issues)
        {
            if (string.IsNullOrWhiteSpace(link.MerchantId)) issues.Add("Merchant ID is required for Web Service");
            if (string.IsNullOrWhiteSpace(link.TerminalId)) issues.Add("Terminal ID is required for Web Service");
            if (!string.IsNullOrWhiteSpace(link.MerchantId) && !IsLong(link.MerchantId))
            {
                issues.Add("Merchant ID must be numeric");
            }
            if (!string.IsNullOrWhiteSpace(link.TerminalId) && !IsLong(link.TerminalId))
            {
                issues.Add("Terminal ID must be numeric");
            }
        }

        private static bool IsLong(string text) =>
            long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

        /// <summary>Merge transport addressing from <paramref name="link"/> into <paramref name="config"/> so callers cannot drift.</summary>
        private static EcrConfig ResolveConfig(EcrLink link, EcrConfig config)
        {
            switch (link)
            {
                case EcrLink.Lan lan:
                    // An out-of-range link port is already an issue; keep the
                    // config's own valid port rather than fail to build the plan.
                    return lan.Port >= 1 && lan.Port <= 65535 ? config with { Port = lan.Port } : config;
                case EcrLink.WebService web:
                    // Web Service HTTP read timeout — 130s unless the caller already
                    // overrode the Wi-Fi default (120s).
                    var responseTimeout = config.ResponseTimeout == TimeSpan.FromSeconds(120)
                        ? EcrConfig.WebServiceResponseTimeout
                        : config.ResponseTimeout;
                    return config with
                    {
                        MerchantId = web.MerchantId,
                        TerminalId = web.TerminalId,
                        ResponseTimeout = responseTimeout,
                    };
                default:
                    // Nothing to merge — a cable is not addressed, so the port in
                    // the config stays whatever it was and is simply never read.
                    return config;
            }
        }
    }
}
