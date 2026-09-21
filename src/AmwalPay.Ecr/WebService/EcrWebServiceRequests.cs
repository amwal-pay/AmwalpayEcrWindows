using System.Text.Json.Nodes;

namespace AmwalPay.Ecr.WebService
{
    /// <summary>Common correlation fields on every Web Service ECR call.</summary>
    public interface IEcrWebServiceRequest
    {
        long MerchantId { get; }

        long TerminalId { get; }

        string TerminalSerial { get; }

        string MerchantReference { get; }
    }

    /// <param name="AmountMinorUnits">Amount in minor units (OMR: 3 decimals, e.g. <c>111</c> → 0.111).</param>
    public sealed record EcrWebServiceSaleRequest(
        long MerchantId,
        long TerminalId,
        string TerminalSerial,
        long AmountMinorUnits,
        string MerchantReference = "",
        string CurrencyCode = "512",
        string EcrId = "ECR01",
        string TransactionDateTime = "") : IEcrWebServiceRequest;

    /// <param name="Stan">Receipt number (STAN) of the transaction to void.</param>
    public sealed record EcrWebServiceVoidRequest(
        long MerchantId,
        long TerminalId,
        string TerminalSerial,
        string Stan,
        string MerchantReference = "",
        string CurrencyCode = "512",
        string EcrId = "ECR01",
        string TransactionDateTime = "") : IEcrWebServiceRequest;

    /// <param name="OriginalTransactionDate">Original transaction date as <c>yyyyMMdd</c>.</param>
    public sealed record EcrWebServiceRefundRequest(
        long MerchantId,
        long TerminalId,
        string TerminalSerial,
        long AmountMinorUnits,
        string Stan,
        string OriginalTransactionDate,
        string MerchantReference = "",
        string CurrencyCode = "512",
        string EcrId = "ECR01",
        string TransactionDateTime = "") : IEcrWebServiceRequest;

    /// <param name="OriginalTransactionDate">Original transaction date as <c>yyyyMMdd</c>.</param>
    public sealed record EcrWebServiceTransactionStatusRequest(
        long MerchantId,
        long TerminalId,
        string TerminalSerial,
        string Stan,
        string OriginalTransactionDate,
        string MerchantReference = "",
        string CurrencyCode = "512",
        string EcrId = "ECR01",
        string TransactionDateTime = "") : IEcrWebServiceRequest;

    /// <summary>Builds and signs JSON bodies for Ecr Web Service endpoints.</summary>
    public static class EcrWebServiceRequestJson
    {
        public static JsonObject Sale(EcrWebServiceSaleRequest request, string secureHashKey = "") =>
            EcrWebServiceMessage.Build(
                type: EcrTransactionType.Sale,
                merchantId: request.MerchantId,
                terminalId: request.TerminalId,
                terminalSerial: request.TerminalSerial,
                secureHashKey: secureHashKey,
                currencyCode: request.CurrencyCode,
                ecrId: request.EcrId,
                amountMinorUnits: request.AmountMinorUnits,
                merchantReference: request.MerchantReference,
                transactionDateTime: request.TransactionDateTime,
                requestDateTime: request.TransactionDateTime).Json;

        public static JsonObject Void(EcrWebServiceVoidRequest request, string secureHashKey = "") =>
            EcrWebServiceMessage.Build(
                type: EcrTransactionType.Void,
                merchantId: request.MerchantId,
                terminalId: request.TerminalId,
                terminalSerial: request.TerminalSerial,
                secureHashKey: secureHashKey,
                currencyCode: request.CurrencyCode,
                ecrId: request.EcrId,
                stan: request.Stan,
                merchantReference: request.MerchantReference,
                transactionDateTime: request.TransactionDateTime,
                requestDateTime: request.TransactionDateTime).Json;

        public static JsonObject Refund(EcrWebServiceRefundRequest request, string secureHashKey = "") =>
            EcrWebServiceMessage.Build(
                type: EcrTransactionType.Refund,
                merchantId: request.MerchantId,
                terminalId: request.TerminalId,
                terminalSerial: request.TerminalSerial,
                secureHashKey: secureHashKey,
                currencyCode: request.CurrencyCode,
                ecrId: request.EcrId,
                amountMinorUnits: request.AmountMinorUnits,
                stan: request.Stan,
                originalTransactionDate: request.OriginalTransactionDate,
                merchantReference: request.MerchantReference,
                transactionDateTime: request.TransactionDateTime,
                requestDateTime: request.TransactionDateTime).Json;

        public static JsonObject TransactionStatus(EcrWebServiceTransactionStatusRequest request, string secureHashKey = "") =>
            EcrWebServiceMessage.Build(
                type: EcrTransactionType.Inquiry,
                merchantId: request.MerchantId,
                terminalId: request.TerminalId,
                terminalSerial: request.TerminalSerial,
                secureHashKey: secureHashKey,
                currencyCode: request.CurrencyCode,
                ecrId: request.EcrId,
                stan: request.Stan,
                originalTransactionDate: request.OriginalTransactionDate,
                merchantReference: request.MerchantReference,
                transactionDateTime: request.TransactionDateTime,
                requestDateTime: request.TransactionDateTime).Json;

        /// <inheritdoc cref="TransactionStatus"/>
        public static JsonObject Inquiry(EcrWebServiceTransactionStatusRequest request, string secureHashKey = "") =>
            TransactionStatus(request, secureHashKey);
    }
}
