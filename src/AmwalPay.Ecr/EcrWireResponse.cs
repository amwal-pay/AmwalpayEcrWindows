using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using AmwalPay.Ecr.Internal;

namespace AmwalPay.Ecr
{
    /// <summary>
    /// Unified ECR response envelope from the terminal:
    /// <c>{ success, responseCode, message, data, errorList, nonce?, secureHash? }</c>
    /// </summary>
    public sealed record EcrWireResponse(
        bool Success,
        string ResponseCode,
        string Message,
        JsonObject? Data,
        IReadOnlyList<string> ErrorList,
        string Nonce = "",
        string SecureHash = "")
    {
        public string MerchantReference
        {
            get
            {
                if (Data is null) return "";
                var reference = Data.Str("merchantReference");
                if (reference.Length != 0) return reference;
                var legacyId = Data.Str("merchantReferenceId");
                if (legacyId.Length != 0) return legacyId;
                return Data.Str("requestId");
            }
        }

        public string TerminalSerial => Data?.Str("terminalSerial") ?? "";

        /// <summary>User-facing text: <c>errorList</c> entries first, then <see cref="Message"/>.</summary>
        public string DisplayMessage
        {
            get
            {
                var errors = string.Join("\n", ErrorList);
                return errors.Length != 0 ? errors : Message;
            }
        }

        /// <summary>Parses the wire JSON, including legacy flat/nested shapes.</summary>
        public static EcrWireResponse Parse(JsonObject json) => EcrResponseEnvelope.Parse(json);

        /// <summary>
        /// Reads user-facing text from a raw JSON string: <c>errorList</c> first,
        /// then wire <c>message</c>. Returns <paramref name="fallback"/> when
        /// <paramref name="raw"/> is empty or invalid.
        /// </summary>
        public static string DisplayMessageFromRaw(string raw, string fallback = "")
        {
            if (string.IsNullOrEmpty(raw)) return fallback;
            try
            {
                var text = Parse((JsonObject)JsonNode.Parse(raw)!).DisplayMessage;
                return text.Length != 0 ? text : fallback;
            }
            catch (Exception)
            {
                return fallback;
            }
        }
    }
}
