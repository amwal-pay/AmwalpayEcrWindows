using System;
using System.Globalization;
using System.Text.Json.Nodes;
using AmwalPay.Ecr.Internal;

namespace AmwalPay.Ecr.WebService
{
    /// <summary>
    /// Web Service ECR wire payload — Hub contract field set per operation, signed
    /// with <see cref="WebServiceSecureHash"/> (HMAC over the JSON body). Wi‑Fi uses
    /// <see cref="SecureHash.Compose"/> instead; do not mix the two.
    /// <para>
    /// Hub path selects the operation; the body also carries <c>messageType</c> for
    /// parity with Wi-Fi / Hub correlation.
    /// </para>
    /// </summary>
    internal static class EcrWebServiceMessage
    {
        public static (string Reference, JsonObject Json) Build(
            EcrTransactionType type,
            long merchantId,
            long terminalId,
            string terminalSerial,
            string secureHashKey,
            string currencyCode = "512",
            string ecrId = "ECR01",
            long? amountMinorUnits = null,
            string stan = "",
            string originalTransactionDate = "",
            // Set for inquiry-by-reference; ignored for stan-based inquiry.
            string originalMerchantReference = "",
            string merchantReference = "",
            string transactionDateTime = "")
        {
            if (type == EcrTransactionType.CloseReceipt)
            {
                return BuildCloseReceipt(
                    merchantId: merchantId,
                    terminalId: terminalId,
                    terminalSerial: terminalSerial,
                    secureHashKey: secureHashKey,
                    ecrId: ecrId,
                    merchantReference: merchantReference,
                    originalMerchantReference: originalMerchantReference,
                    transactionDateTime: transactionDateTime);
            }

            var reference = EcrMessage.ResolveMerchantReference(merchantReference);
            var wireDateTime = transactionDateTime.Length != 0 ? transactionDateTime : Timestamp();
            var body = new JsonObject
            {
                ["version"] = EcrMessage.ProtocolVersion,
                ["messageType"] = type.MessageType(),
                ["merchantId"] = merchantId,
                ["terminalId"] = terminalId,
                ["terminalSerial"] = terminalSerial,
                ["ecrId"] = ecrId,
                ["merchantReference"] = reference,
                ["currencyCode"] = currencyCode,
                ["transactionDateTime"] = wireDateTime,
                // Hub validates this separately from transactionDateTime
                // (`RequestDateTimeIsRequired` when omitted).
                ["requestDateTime"] = wireDateTime,
            };

            switch (type)
            {
                case EcrTransactionType.Sale:
                    body["amount"] = Padded(amountMinorUnits);
                    break;
                case EcrTransactionType.Void:
                    body["stan"] = EcrMessage.PaddedStan(stan);
                    body["originalMerchantReference"] = reference;
                    body["originalTerminalId"] = terminalId;
                    break;
                case EcrTransactionType.Refund:
                    body["amount"] = Padded(amountMinorUnits);
                    body["stan"] = EcrMessage.PaddedStan(stan);
                    body["originalTransactionDate"] = originalTransactionDate;
                    body["originalMerchantReference"] = reference;
                    body["originalTerminalId"] = terminalId;
                    break;
                case EcrTransactionType.Inquiry:
                    {
                        var invoiceStan = EcrMessage.PaddedStan(stan);
                        var byStan = invoiceStan.Length != 0;
                        var byReference = !byStan && !string.IsNullOrWhiteSpace(originalMerchantReference);
                        body["stan"] = invoiceStan;
                        body["originalTransactionDate"] = originalTransactionDate;
                        // By STAN → empty originalMerchantReference; by reference → set it.
                        body["originalMerchantReference"] = byReference
                            ? EcrMessage.ResolveMerchantReference(originalMerchantReference)
                            : "";
                        body["originalTerminalId"] = terminalId;
                        break;
                    }
                case EcrTransactionType.Receipt:
                    throw new InvalidOperationException("Receipt is not a Web Service operation");
                default:
                    throw new InvalidOperationException($"{type} is not a Web Service operation");
            }

            var json = secureHashKey.Length == 0 ? body : WebServiceSecureHash.WithHash(body, secureHashKey);
            return (reference, json);
        }

        /// <summary>
        /// Hub <c>/Ecr/closeReceipt</c> body — no <c>messageType</c> / <c>currencyCode</c>.
        /// <paramref name="originalMerchantReference"/> is the sale / void / refund that
        /// just finished; <paramref name="merchantReference"/> is this close request's own id.
        /// </summary>
        public static (string Reference, JsonObject Json) BuildCloseReceipt(
            long merchantId,
            long terminalId,
            string terminalSerial,
            string secureHashKey,
            string ecrId = "ECR01",
            string merchantReference = "",
            string originalMerchantReference = "",
            string transactionDateTime = "")
        {
            if (string.IsNullOrWhiteSpace(originalMerchantReference))
            {
                throw new ArgumentException(
                    "Close receipt needs the original merchant reference",
                    nameof(originalMerchantReference));
            }

            var reference = EcrMessage.ResolveMerchantReference(merchantReference);
            var wireDateTime = transactionDateTime.Length != 0 ? transactionDateTime : Timestamp();
            var body = new JsonObject
            {
                ["version"] = EcrMessage.ProtocolVersion,
                ["merchantId"] = merchantId,
                ["terminalId"] = terminalId,
                ["terminalSerial"] = terminalSerial,
                ["ecrId"] = ecrId,
                ["merchantReference"] = reference,
                ["transactionDateTime"] = wireDateTime,
                ["requestDateTime"] = wireDateTime,
                ["originalMerchantReference"] = EcrMessage.ResolveMerchantReference(originalMerchantReference),
            };

            var json = secureHashKey.Length == 0 ? body : WebServiceSecureHash.WithHash(body, secureHashKey);
            return (reference, json);
        }

        private static string Padded(long? minorUnits)
        {
            if (minorUnits == null) throw new InvalidOperationException("This operation needs an amount");
            return minorUnits.Value.ToString(CultureInfo.InvariantCulture).PadLeft(12, '0');
        }

        private static string Timestamp() => EcrMessage.Timestamp();
    }
}
