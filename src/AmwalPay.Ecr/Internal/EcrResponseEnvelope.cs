using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace AmwalPay.Ecr.Internal
{
    /// <summary>
    /// Reads the unified ECR response envelope:
    /// <c>{ success, responseCode, message, data, errorList, nonce?, secureHash? }</c>
    /// <para>
    /// Legacy answers that still nest the envelope under <c>ecrResponse</c>, or
    /// carry flat <c>approved</c> / <c>responseMessage</c> fields, are normalised here.
    /// </para>
    /// </summary>
    internal static class EcrResponseEnvelope
    {
        private const string Approved = "00";

        public static EcrWireResponse Parse(JsonObject json) =>
            new EcrWireResponse(
                Success: IsSuccess(json),
                ResponseCode: ResponseCode(json),
                Message: WireMessage(json),
                Data: Data(json),
                ErrorList: ErrorList(json),
                Nonce: json.Str("nonce"),
                SecureHash: json.Str(SecureHash.Field));

        public static JsonObject Root(JsonObject json)
        {
            if (json.ContainsKey("success") || json.ContainsKey("data")) return json;
            return json["ecrResponse"] as JsonObject ?? json;
        }

        public static bool IsSuccess(JsonObject json)
        {
            var success = Root(json).StrictBool("success");
            if (success.HasValue) return success.Value;
            var approved = json.StrictBool("approved");
            if (approved.HasValue) return approved.Value;
            return ResponseCode(json) == Approved;
        }

        public static string ResponseCode(JsonObject json)
        {
            var code = Root(json).Str("responseCode");
            return code.Length != 0 ? code : json.Str("responseCode");
        }

        /// <summary>Top-level <c>message</c> / legacy <c>responseMessage</c> from the wire payload.</summary>
        public static string WireMessage(JsonObject json)
        {
            var message = Root(json).Str("message");
            return message.Length != 0 ? message : json.Str("responseMessage");
        }

        /// <summary>User-facing text: <c>errorList</c> entries first, then wire message.</summary>
        public static string DisplayMessage(JsonObject json)
        {
            var root = Root(json);
            var errors = ErrorList(root);
            if (errors.Count == 0) errors = ErrorList(json);
            return errors.Count != 0 ? string.Join("\n", errors) : WireMessage(json);
        }

        public static JsonObject? Data(JsonObject json) => Root(json)["data"] as JsonObject;

        public static IReadOnlyList<string> ErrorList(JsonObject json)
        {
            var raw = Root(json)["errorList"] ?? json["errorList"];
            var result = new List<string>();
            if (!(raw is JsonArray array)) return result;
            foreach (var element in array)
            {
                if (!(element is JsonValue scalar)) continue;
                var text = EcrJson.Content(scalar).Trim();
                if (text.Length != 0) result.Add(text);
            }
            return result;
        }
    }
}
