namespace AmwalPay.Ecr.WebService
{
    /// <summary>Fixed path suffixes for Web Service ECR REST operations.</summary>
    internal static class EcrWebServiceEndpoints
    {
        public const string Sale = "/Ecr/Sale";
        public const string Void = "/Ecr/Void";
        public const string Refund = "/Ecr/Refund";
        public const string TransactionStatus = "/Ecr/Inquiry";
        public const string CloseReceipt = "/Ecr/closeReceipt";
        public const string Settle = "/Ecr/settle";
        public const string CheckReachability = "/Ecr/CheckReachability";
    }
}
