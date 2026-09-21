using System;
using System.Globalization;
using System.Text.Json.Nodes;

namespace AmwalPay.Ecr.Internal
{
    /// <summary>
    /// A request as it goes on the wire.
    /// <para>
    /// Fields that do not apply to the operation are left out entirely, so the
    /// terminal can read a field's presence as meaning the operation uses it.
    /// </para>
    /// </summary>
    internal sealed class EcrMessage
    {
        /// <summary>
        /// Bumped when the message format changes in a way older terminals cannot
        /// read. Present from the first release so a terminal can tell.
        /// </summary>
        public const int ProtocolVersion = 1;

        /// <summary>Width the terminal stores and looks up receipt numbers by.</summary>
        private const int StanDigits = 6;

        /// <summary>
        /// Longest reference the terminal will carry. Generous enough for the order
        /// numbers and GUIDs tills actually use, short enough that it cannot be used
        /// to push an unbounded string through the terminal.
        /// </summary>
        public const int MerchantReferenceMaxLength = 32;

        private EcrMessage(string merchantReference, JsonObject json)
        {
            MerchantReference = merchantReference;
            Json = json;
        }

        public string MerchantReference { get; }

        public JsonObject Json { get; }

        /// <summary>
        /// The nonce this request went out with, empty when unsigned. Kept so the
        /// answer can be checked against it: a response carrying a different nonce
        /// is an answer to some other request.
        /// </summary>
        public string Nonce => Json.Str("nonce");

        public static EcrMessage Build(
            EcrTransactionType type,
            EcrConfig config,
            string terminalSerial,
            decimal? amount,
            string originalStan,
            string originalTerminalId,
            string originalDate,
            string originalReference = "",
            string merchantReference = "")
        {
            var reference = ResolveMerchantReference(merchantReference);
            var body = new JsonObject
            {
                ["version"] = ProtocolVersion,
                ["messageType"] = type.MessageType(),
                ["merchantReference"] = reference,
                ["terminalSerial"] = terminalSerial,
                ["currencyCode"] = config.CurrencyCode,
                ["transactionDateTime"] = Timestamp(),
                ["ecrId"] = config.EcrId,
            };

            if (type.RequiresAmount())
            {
                body["amount"] = MinorUnits(amount, config.MinorUnitDigits);
            }
            if (type.RequiresOriginalStan() && !string.IsNullOrWhiteSpace(originalStan))
            {
                body["stan"] = PaddedStan(originalStan);
            }
            if (type.AllowsOtherTerminal() && !string.IsNullOrWhiteSpace(originalTerminalId))
            {
                body["originalTerminalId"] = originalTerminalId;
            }
            if (type.RequiresOriginalDate() && !string.IsNullOrWhiteSpace(originalDate))
            {
                body["originalTransactionDate"] = originalDate;
            }
            // Names the transaction being looked up, in place of its receipt
            // number. Only a read-only type may: acting on a transaction still goes
            // by the number on the printed receipt.
            if (!type.MovesMoney() && !string.IsNullOrWhiteSpace(originalReference))
            {
                body["originalMerchantReference"] = ResolveMerchantReference(originalReference);
            }

            // The nonce is what makes a captured message useless a second time: the
            // terminal remembers the ones it has seen and refuses a repeat. It is
            // only meaningful alongside a signature, since an attacker could
            // otherwise just change it.
            if (config.SignsMessages)
            {
                body["nonce"] = SecureHash.NewNonce();
                body[SecureHash.Field] = SecureHash.Sign(SecureHash.Compose(body), config.SecureHashKey);
            }

            return new EcrMessage(reference, body);
        }

        /// <summary>
        /// The reference to put on the message: the caller's own, or one made here
        /// when they did not supply one.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// The reference is too long or carries characters the wire format cannot
        /// represent unambiguously.
        /// </exception>
        public static string ResolveMerchantReference(string? supplied)
        {
            var trimmed = (supplied ?? "").Trim();
            if (trimmed.Length == 0) return NewReference();

            if (trimmed.Length > MerchantReferenceMaxLength)
            {
                throw new ArgumentException(
                    $"A merchant reference is at most {MerchantReferenceMaxLength} characters, was {trimmed.Length}");
            }
            foreach (var c in trimmed)
            {
                if (!IsAllowedInReference(c))
                {
                    throw new ArgumentException(
                        $"A merchant reference takes printable ASCII without spaces, '&' or '=': was \"{trimmed}\"");
                }
            }
            return trimmed;
        }

        // 0x21..0x7E is printable ASCII with the space (0x20) left out: a reference
        // is copied into logs and receipts, where a leading or doubled space is
        // invisible and turns two references into one. '&' and '=' are the
        // separators of a signed message's input.
        private static bool IsAllowedInReference(char c) =>
            c >= 0x21 && c <= 0x7E && c != '&' && c != '=';

        /// <summary>Amount in minor units, zero-padded to 12 digits (ISO 8583 field 4).</summary>
        public static string MinorUnits(decimal? amount, int digits)
        {
            if (amount == null) return new string('0', 12);
            var scaled = Math.Round(amount.Value, digits, MidpointRounding.AwayFromZero) * Pow10(digits);
            return decimal.Truncate(scaled).ToString(CultureInfo.InvariantCulture).PadLeft(12, '0');
        }

        internal static decimal Pow10(int digits)
        {
            var value = 1m;
            for (var i = 0; i < digits; i++) value *= 10m;
            return value;
        }

        /// <summary>The terminal stores receipt numbers padded; an operator types "24".</summary>
        public static string PaddedStan(string stan)
        {
            var digits = new System.Text.StringBuilder();
            foreach (var c in stan)
            {
                if (char.IsDigit(c)) digits.Append(c);
            }
            var trimmed = digits.ToString().TrimStart('0');
            return trimmed.Length == 0 ? "" : trimmed.PadLeft(StanDigits, '0');
        }

        internal static string Timestamp() =>
            DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

        private static string NewReference() =>
            Guid.NewGuid().ToString("N").Substring(0, 12).ToUpperInvariant();
    }
}
