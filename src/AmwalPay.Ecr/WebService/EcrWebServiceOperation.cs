using System;

namespace AmwalPay.Ecr.WebService
{
    /// <summary>Web Service ECR operations exposed as REST endpoints on the ECR API host.</summary>
    internal enum EcrWebServiceOperation
    {
        Sale,
        Void,
        Refund,
        TransactionStatus,
        CloseReceipt,
        Settle,
        CheckReachability,
    }

    internal static class EcrWebServiceOperations
    {
        public static string Path(this EcrWebServiceOperation operation) => operation switch
        {
            EcrWebServiceOperation.Sale => EcrWebServiceEndpoints.Sale,
            EcrWebServiceOperation.Void => EcrWebServiceEndpoints.Void,
            EcrWebServiceOperation.Refund => EcrWebServiceEndpoints.Refund,
            EcrWebServiceOperation.TransactionStatus => EcrWebServiceEndpoints.TransactionStatus,
            EcrWebServiceOperation.CloseReceipt => EcrWebServiceEndpoints.CloseReceipt,
            EcrWebServiceOperation.Settle => EcrWebServiceEndpoints.Settle,
            EcrWebServiceOperation.CheckReachability => EcrWebServiceEndpoints.CheckReachability,
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        public static string Label(this EcrWebServiceOperation operation) => operation switch
        {
            EcrWebServiceOperation.Sale => "Sale",
            EcrWebServiceOperation.Void => "Void",
            EcrWebServiceOperation.Refund => "Refund",
            EcrWebServiceOperation.TransactionStatus => "Transaction status",
            EcrWebServiceOperation.CloseReceipt => "Close receipt",
            EcrWebServiceOperation.Settle => "Settlement",
            EcrWebServiceOperation.CheckReachability => "Check reachability",
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        /// <summary>The REST operation for a transaction type, or null when it has none (receipts).</summary>
        public static EcrWebServiceOperation? From(EcrTransactionType type) => type switch
        {
            EcrTransactionType.Sale => EcrWebServiceOperation.Sale,
            EcrTransactionType.Void => EcrWebServiceOperation.Void,
            EcrTransactionType.Refund => EcrWebServiceOperation.Refund,
            EcrTransactionType.Inquiry => EcrWebServiceOperation.TransactionStatus,
            EcrTransactionType.CloseReceipt => EcrWebServiceOperation.CloseReceipt,
            EcrTransactionType.Receipt => null,
            EcrTransactionType.Settlement => EcrWebServiceOperation.Settle,
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
    }
}
