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
    }

    internal static class EcrWebServiceOperations
    {
        public static string Path(this EcrWebServiceOperation operation) => operation switch
        {
            EcrWebServiceOperation.Sale => EcrWebServiceEndpoints.Sale,
            EcrWebServiceOperation.Void => EcrWebServiceEndpoints.Void,
            EcrWebServiceOperation.Refund => EcrWebServiceEndpoints.Refund,
            EcrWebServiceOperation.TransactionStatus => EcrWebServiceEndpoints.TransactionStatus,
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        public static string Label(this EcrWebServiceOperation operation) => operation switch
        {
            EcrWebServiceOperation.Sale => "Sale",
            EcrWebServiceOperation.Void => "Void",
            EcrWebServiceOperation.Refund => "Refund",
            EcrWebServiceOperation.TransactionStatus => "Transaction status",
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        /// <summary>The REST operation for a transaction type, or null when it has none (receipts).</summary>
        public static EcrWebServiceOperation? From(EcrTransactionType type) => type switch
        {
            EcrTransactionType.Sale => EcrWebServiceOperation.Sale,
            EcrTransactionType.Void => EcrWebServiceOperation.Void,
            EcrTransactionType.Refund => EcrWebServiceOperation.Refund,
            EcrTransactionType.Inquiry => EcrWebServiceOperation.TransactionStatus,
            EcrTransactionType.Receipt => null,
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
    }
}
