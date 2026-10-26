using System.Collections.Generic;

namespace AmwalPay.Ecr
{
    /// <summary>One row of a settlement total: how many, and their amount in major units.</summary>
    public sealed record EcrSettlementTotal(int Count, string Amount);

    /// <summary>
    /// What a settlement came back as. The till already confirmed with the cashier;
    /// this is only the terminal's answer.
    /// </summary>
    public abstract record EcrSettlement
    {
        private EcrSettlement()
        {
        }

        /// <summary>The batch closed. Amounts are major units. Every total type is present.</summary>
        public sealed record Settled(
            string MerchantReference,
            string BatchId,
            string Currency,
            string StartTime,
            string EndTime,
            int TotalTransactions,
            string NetAmount,
            IReadOnlyDictionary<string, EcrSettlementTotal> Totals,
            string Raw) : EcrSettlement;

        /// <summary>The terminal is idle and the batch had nothing in it.</summary>
        public sealed record NothingToSettle(string MerchantReference, string Message, string Raw) : EcrSettlement;

        /// <summary>
        /// The batch is still open: the counts did not match, the session had
        /// expired, the terminal was busy, or this transport cannot settle.
        /// </summary>
        public sealed record NotSettled(
            string MerchantReference,
            string ResponseCode,
            string Status,
            string Reason,
            int? UploadedTransactions,
            string Raw) : EcrSettlement;

        /// <summary>The exchange failed. The batch may or may not have closed; repeating the call is safe.</summary>
        public sealed record Failed(string MerchantReference, Failure Failure) : EcrSettlement;
    }
}
