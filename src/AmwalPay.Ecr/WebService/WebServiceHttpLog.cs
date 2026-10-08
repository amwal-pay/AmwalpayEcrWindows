using System;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using AmwalPay.Ecr.Internal;

namespace AmwalPay.Ecr.WebService
{
    /// <summary>Structured request/response logging for Web Service ECR HTTP calls.</summary>
    internal static class WebServiceHttpLog
    {
        private const string Prefix = "[Web Service]";

        private static readonly JsonSerializerOptions PrettyOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public static void Request(IEcrLogger logger, EcrWebServiceOperation operation, string url, EcrEnvironment environment, string payload)
        {
            logger.Debug($"{Prefix} ========== REQUEST ==========");
            logger.Debug($"{Prefix} {operation.Label().ToUpperInvariant()} POST {url} (env={environment})");
            LogPayload(logger, "REQUEST PAYLOAD", payload);
        }

        /// <summary>
        /// Logs the <c>calcHash</c> inputs and output so the simulator can confirm
        /// the Web Service key — not the Wi‑Fi/LAN key — is used.
        /// </summary>
        public static void Signing(IEcrLogger logger, string keyLabel, string secretKey, string payloadToSign, string secureHashValue)
        {
            logger.Debug($"{Prefix} calcHash: HMAC-SHA256(sorted key=value payload, Hex.parse(secret)) → uppercase hex");
            logger.Debug($"{Prefix} signing key ({keyLabel}): {EcrDiagnostics.SecretFingerprint(secretKey)}");
            logger.Debug($"{Prefix} payload to sign (excl. secureHashValue): {payloadToSign}");
            logger.Debug($"{Prefix} secureHashValue: {secureHashValue}");
        }

        public static void Response(IEcrLogger logger, EcrWebServiceOperation operation, int httpStatus, long elapsedMs, string body)
        {
            logger.Debug($"{Prefix} ========== RESPONSE ==========");
            logger.Debug($"{Prefix} {operation.Label().ToUpperInvariant()} HTTP {httpStatus} ({elapsedMs}ms)");
            LogPayload(logger, "RESPONSE PAYLOAD", body);
        }

        public static void Error(IEcrLogger logger, EcrWebServiceOperation operation, string message, long? elapsedMs = null)
        {
            var timing = elapsedMs.HasValue ? $" ({elapsedMs.Value}ms)" : "";
            logger.Debug($"{Prefix} ✗ {operation.Label().ToUpperInvariant()}{timing} — {message}");
        }

        public static void Operation(IEcrLogger logger, string messageType, string merchantReference, string terminalSerial, long merchantId, long terminalId)
        {
            logger.Debug(
                $"{Prefix} sending {messageType} ref={merchantReference} " +
                $"terminal={terminalSerial} merchantId={merchantId} terminalId={terminalId}");
        }

        private static void LogPayload(IEcrLogger logger, string label, string raw)
        {
            var body = string.IsNullOrWhiteSpace(raw) ? "<empty>" : raw;
            var formatted = PrettyPrintJson(body);
            logger.Debug($"{Prefix} {label}:");
            foreach (var line in formatted.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                logger.Debug($"{Prefix}   {line}");
            }
        }

        private static string PrettyPrintJson(string raw)
        {
            if (raw == "<empty>") return raw;
            try
            {
                var node = JsonNode.Parse(raw);
                return node == null ? raw : node.ToJsonString(PrettyOptions);
            }
            catch
            {
                return raw;
            }
        }
    }
}
