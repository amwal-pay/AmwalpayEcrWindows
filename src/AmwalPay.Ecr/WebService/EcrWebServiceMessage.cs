using System;
using System.Globalization;
using System.Text.Json.Nodes;
using AmwalPay.Ecr.Internal;

namespace AmwalPay.Ecr.WebService
{
    /// <summary>
    /// Web Service ECR wire payload — fixed field set per operation, signed with
    /// <see cref="WebServiceSecureHash"/>. Wi‑Fi/LAN uses
    /// <see cref="SecureHash.Compose"/> instead; do not mix the two.
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
            string merchantReference = "",
            string transactionDateTime = "",
            string requestDateTime = "")
        {
            var reference = EcrMessage.ResolveMerchantReference(merchantReference);
            var wireDateTime = transactionDateTime.Length != 0 ? transactionDateTime : Timestamp();
            var body = new JsonObject
            {
                ["version"] = EcrMessage.ProtocolVersion,
                ["messageType"] = type.MessageType(),
                ["merchantReference"] = reference,
                ["terminalSerial"] = terminalSerial,
                ["currencyCode"] = currencyCode,
                ["transactionDateTime"] = wireDateTime,
                ["requestDateTime"] = requestDateTime.Length != 0 ? requestDateTime : wireDateTime,
                ["ecrId"] = ecrId,
            };

            switch (type)
            {
                case EcrTransactionType.Sale:
                    body["amount"] = Padded(amountMinorUnits);
                    break;
                case EcrTransactionType.Void:
                    body["stan"] = EcrMessage.PaddedStan(stan);
                    break;
                case EcrTransactionType.Refund:
                    body["amount"] = Padded(amountMinorUnits);
                    body["stan"] = EcrMessage.PaddedStan(stan);
                    body["originalTransactionDate"] = originalTransactionDate;
                    break;
                case EcrTransactionType.Inquiry:
                    body["stan"] = EcrMessage.PaddedStan(stan);
                    body["originalTransactionDate"] = originalTransactionDate;
                    break;
                default:
                    throw new InvalidOperationException("Receipt is not a Web Service operation");
            }

            body["merchantId"] = merchantId;
            body["terminalId"] = terminalId;

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
