using System;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AmwalPay.Ecr.Internal
{
    /// <summary>
    /// JSON helpers shared by the codec, signing and response readers.
    /// <para>
    /// Everything here reads a <see cref="JsonObject"/> the way the terminal's own
    /// code reads it: any scalar as text, JSON <c>null</c> as absent.
    /// </para>
    /// </summary>
    internal static class EcrJson
    {
        // Relaxed escaping keeps non-ASCII and '<' '>' '&' as themselves, so the
        // bytes on the wire are the plain UTF-8 JSON the terminal expects.
        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>
        /// Parses text that must be exactly one JSON object, or throws.
        /// <para>
        /// Stricter than <c>JsonNode.Parse</c>
        /// on purpose. A JSON <c>null</c> parses to a null node rather than failing;
        /// an array or bare value is valid JSON but not a message; and a repeated key
        /// only throws later, when the object is first read, which would surface as an
        /// unrelated exception out of a payment call. All three are rejected here, at
        /// the parse, where the caller already turns any failure into "malformed".
        /// </para>
        /// </summary>
        public static JsonObject ParseObject(string text)
        {
            var root = JsonNode.Parse(text) as JsonObject
                ?? throw new InvalidOperationException("The text is not a JSON object");
            Materialize(root);
            return root;
        }

        // Objects are read lazily; walking every node forces the duplicate-key
        // check now, on the whole tree, instead of on first access.
        private static void Materialize(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var entry in obj) Materialize(entry.Value);
                    break;
                case JsonArray array:
                    foreach (var item in array) Materialize(item);
                    break;
            }
        }

        /// <summary>Compact JSON text.</summary>
        public static string Write(JsonNode node) => node.ToJsonString(WriteOptions);

        /// <summary>The text of a scalar: a string unquoted, a number or boolean as written.</summary>
        public static string Content(JsonValue value) =>
            value.TryGetValue<string>(out var text) ? text : value.ToJsonString(WriteOptions);

        /// <summary>
        /// Reads a string field, whatever JSON type it arrived as. A field the
        /// terminal sent as JSON <c>null</c> reads as absent rather than as the
        /// text "null".
        /// </summary>
        public static string Str(this JsonObject json, string key)
        {
            if (!json.TryGetPropertyValue(key, out var value) || value is null) return "";
            return value is JsonValue scalar ? Content(scalar) : value.ToJsonString(WriteOptions);
        }

        /// <summary>A boolean field: only the literal <c>true</c>/<c>false</c> count, anything else is false.</summary>
        public static bool Flag(this JsonObject json, string key) => StrictBool(json, key) ?? false;

        /// <summary>The field as a strict boolean, or null when absent or not <c>true</c>/<c>false</c>.</summary>
        public static bool? StrictBool(this JsonObject json, string key)
        {
            if (!json.TryGetPropertyValue(key, out var value) || !(value is JsonValue scalar)) return null;
            switch (Content(scalar))
            {
                case "true": return true;
                case "false": return false;
                default: return null;
            }
        }

        /// <summary>Whether the value counts toward a signature: a string, number or boolean.</summary>
        public static bool IsScalar(this JsonNode? node) => node is JsonValue;

        /// <summary>The scalar entries of <paramref name="json"/> whose key is not <paramref name="excluded"/>, sorted by key, as <c>key=value&amp;key=value</c>.</summary>
        public static string Compose(JsonObject json, string excluded)
        {
            var pairs = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>();
            foreach (var entry in json)
            {
                if (entry.Key == excluded || !entry.Value.IsScalar()) continue;
                pairs.Add(new System.Collections.Generic.KeyValuePair<string, string>(entry.Key, Content((JsonValue)entry.Value!)));
            }
            // Ordinal: the terminal sorts by code unit, not by culture.
            pairs.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return string.Join("&", pairs.ConvertAll(p => p.Key + "=" + p.Value));
        }
    }
}
