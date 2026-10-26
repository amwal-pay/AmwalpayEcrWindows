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
    /// <param name="OriginalMerchantReference">
    /// When set (and <paramref name="Stan"/> is empty), inquiry looks up by this
    /// reference rather than by receipt number.
    /// </param>
    public sealed record EcrWebServiceTransactionStatusRequest(
        long MerchantId,
        long TerminalId,
        string TerminalSerial,
        string Stan = "",
        string OriginalTransactionDate = "",
        string MerchantReference = "",
        string CurrencyCode = "512",
        string EcrId = "ECR01",
        string TransactionDateTime = "",
        string OriginalMerchantReference = "") : IEcrWebServiceRequest;

    /// <summary>
    /// Hub <c>/Ecr/settle</c>. The path selects the operation; the body is the
    /// shared identity payload (no <c>messageType</c>, no amount).
    /// </summary>
    public sealed record EcrWebServiceSettleRequest(
        long MerchantId,
        long TerminalId,
        string TerminalSerial,
        string MerchantReference = "",
        string CurrencyCode = "512",
        string EcrId = "ECR01",
        string TransactionDateTime = "") : IEcrWebServiceRequest;

    /// <summary>
    /// Hub <c>/Ecr/CheckReachability</c>. Same body as
    /// <see cref="EcrWebServiceSettleRequest"/>.
    /// </summary>
    public sealed record EcrWebServiceCheckReachabilityRequest(
        long MerchantId,
        long TerminalId,
        string TerminalSerial,
        string MerchantReference = "",
        string CurrencyCode = "512",
        string EcrId = "ECR01",
        string TransactionDateTime = "") : IEcrWebServiceRequest;

    /// <summary>
    /// Hub <c>/Ecr/closeReceipt</c> — send from the till’s Close receipt action.
    /// <para>
    /// <paramref name="OriginalMerchantReference"/> is the money-moving operation's
    /// reference; <paramref name="MerchantReference"/> is this close request's own id
    /// (blank → SDK generates).
    /// </para>
    /// </summary>
    public sealed record EcrWebServiceCloseReceiptRequest(
        long MerchantId,
        long TerminalId,
        string TerminalSerial,
        string OriginalMerchantReference,
        string MerchantReference = "",
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
                transactionDateTime: request.TransactionDateTime).Json;

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
                transactionDateTime: request.TransactionDateTime).Json;

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
                transactionDateTime: request.TransactionDateTime).Json;

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
                originalMerchantReference: request.OriginalMerchantReference,
                merchantReference: request.MerchantReference,
                transactionDateTime: request.TransactionDateTime).Json;

        /// <inheritdoc cref="TransactionStatus"/>
        public static JsonObject Inquiry(EcrWebServiceTransactionStatusRequest request, string secureHashKey = "") =>
            TransactionStatus(request, secureHashKey);

        public static JsonObject Settle(EcrWebServiceSettleRequest request, string secureHashKey = "") =>
            EcrWebServiceMessage.BuildPlain(
                merchantId: request.MerchantId,
                terminalId: request.TerminalId,
                terminalSerial: request.TerminalSerial,
                secureHashKey: secureHashKey,
                currencyCode: request.CurrencyCode,
                ecrId: request.EcrId,
                merchantReference: request.MerchantReference,
                transactionDateTime: request.TransactionDateTime).Json;

        public static JsonObject CheckReachability(EcrWebServiceCheckReachabilityRequest request, string secureHashKey = "") =>
            EcrWebServiceMessage.BuildPlain(
                merchantId: request.MerchantId,
                terminalId: request.TerminalId,
                terminalSerial: request.TerminalSerial,
                secureHashKey: secureHashKey,
                currencyCode: request.CurrencyCode,
                ecrId: request.EcrId,
                merchantReference: request.MerchantReference,
                transactionDateTime: request.TransactionDateTime).Json;

        public static JsonObject CloseReceipt(EcrWebServiceCloseReceiptRequest request, string secureHashKey = "") =>
            EcrWebServiceMessage.BuildCloseReceipt(
                merchantId: request.MerchantId,
                terminalId: request.TerminalId,
                terminalSerial: request.TerminalSerial,
                secureHashKey: secureHashKey,
                ecrId: request.EcrId,
                merchantReference: request.MerchantReference,
                originalMerchantReference: request.OriginalMerchantReference,
                transactionDateTime: request.TransactionDateTime).Json;
    }
}
