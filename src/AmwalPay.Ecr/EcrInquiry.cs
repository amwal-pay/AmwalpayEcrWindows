namespace AmwalPay.Ecr
{
    /// <summary>
    /// The answer to "what became of this transaction".
    /// <para>
    /// Kept apart from <see cref="EcrResult"/> because it reports on a transaction
    /// rather than performing one: an inquiry that succeeds says nothing about
    /// whether money moved — that is <see cref="EcrTransaction.Status"/>.
    /// </para>
    /// </summary>
    /// <param name="MerchantReference">The reference the inquiry was sent with.</param>
    public abstract record EcrInquiry(string MerchantReference)
    {
        /// <summary>The transaction was found.</summary>
        /// <param name="Raw">The terminal's full answer as JSON text.</param>
        public sealed record Found(string MerchantReference, EcrTransaction Transaction, string Raw)
            : EcrInquiry(MerchantReference);

        /// <summary>
        /// No such transaction on that terminal for that day.
        /// <para>
        /// <paramref name="Reason"/> carries the backend's own words where it gave
        /// any — a receipt number from another terminal and one that never existed
        /// read alike from the till, and the terminal is the only side that can
        /// tell them apart.
        /// </para>
        /// </summary>
        public sealed record NotFound(string MerchantReference, string Reason, string Raw)
            : EcrInquiry(MerchantReference);

        /// <summary>
        /// The exchange itself failed, so nothing was learned. Unlike a failed sale
        /// this is harmless to retry: an inquiry changes nothing.
        /// </summary>
        public sealed record Failed(string MerchantReference, Failure Failure)
            : EcrInquiry(MerchantReference);
    }

    /// <summary>
    /// A transaction's e-receipt.
    /// <para>
    /// The receipt is published by the backend and identified by a URL; the till
    /// shows that URL as a QR code and the customer reads the receipt on their own
    /// phone. Nothing is printed, so a till with no printer can still hand one over.
    /// </para>
    /// </summary>
    public abstract record EcrReceipt(string MerchantReference)
    {
        /// <summary>The receipt is published. Put <see cref="Url"/> in a QR code as it stands.</summary>
        public sealed record Ready(string MerchantReference, string Url, string Raw)
            : EcrReceipt(MerchantReference);

        /// <summary>
        /// No receipt: the transaction was not found, or the backend would not
        /// publish one. Harmless — the transaction itself is unaffected, and asking
        /// again later is safe.
        /// </summary>
        public sealed record Unavailable(string MerchantReference, string Reason, string Raw)
            : EcrReceipt(MerchantReference);

        /// <summary>The exchange itself failed. Safe to retry: nothing was changed.</summary>
        public sealed record Failed(string MerchantReference, Failure Failure)
            : EcrReceipt(MerchantReference);
    }

    /// <summary>
    /// The answer to a request to put the terminal's receipt away.
    /// <para>
    /// Success means the terminal is idle again — equally true whether it had a
    /// receipt up or was already idle. Safe to repeat.
    /// </para>
    /// </summary>
    public abstract record EcrReceiptClosed(string MerchantReference)
    {
        /// <summary>The terminal is on its idle screen.</summary>
        public sealed record Idle(string MerchantReference, string Raw)
            : EcrReceiptClosed(MerchantReference);

        /// <summary>
        /// The terminal answered and refused (e.g. firmware too old for
        /// <c>CLOSE_RECEIPT</c>). Treat as "cannot be asked" and carry on.
        /// </summary>
        public sealed record Refused(
            string MerchantReference,
            string ResponseCode,
            string Reason,
            string Raw) : EcrReceiptClosed(MerchantReference);

        /// <summary>The exchange itself failed. Safe to retry: nothing was changed.</summary>
        public sealed record Failed(string MerchantReference, Failure Failure)
            : EcrReceiptClosed(MerchantReference);
    }

    /// <summary>A transaction as the terminal's backend records it.</summary>
    /// <param name="TransactionId">The backend's own identifier, unique across terminals and days.</param>
    /// <param name="Stan">The receipt number, unique only within a terminal's day.</param>
    /// <param name="Type"><c>Sale</c>, <c>Void</c>, <c>Refund</c> — as the backend names it.</param>
    /// <param name="Status">What became of it: <c>Approved</c>, <c>Declined</c>, <c>Voided</c>.</param>
    /// <param name="Amount">In major units, e.g. <c>1.234</c>. What was requested.</param>
    /// <param name="TotalAmount">Including tips and fees, where the transaction carried any.</param>
    /// <param name="TransactionTime">As the backend recorded it, e.g. <c>2026-08-09T12:41:07</c>.</param>
    /// <param name="MaskedPan">Masked, never the full number. May be empty.</param>
    /// <param name="IsRefunded">Whether this transaction has already been refunded.</param>
    /// <param name="CanVoid">
    /// Whether the terminal would accept a void for it now. Decided by the backend
    /// and the terminal's own void window, so a till showing a Void button can ask
    /// rather than guess.
    /// </param>
    /// <param name="CanRefund">Whether the terminal would accept a refund against it.</param>
    /// <param name="PartialApproval">
    /// Whether the issuer approved less than the sale asked for. When true,
    /// <paramref name="Amount"/> is what was requested and
    /// <paramref name="AuthorizedAmount"/> is what the cardholder was actually charged.
    /// </param>
    /// <param name="AuthorizedAmount">What was actually authorised. Equal to <paramref name="Amount"/> unless partially approved.</param>
    public sealed record EcrTransaction(
        string TransactionId,
        string Stan,
        string Type,
        string Status,
        string Amount,
        string TotalAmount,
        string Currency,
        string TransactionTime,
        string MaskedPan,
        string CardHolderName,
        string Rrn,
        string AuthCode,
        string BatchId,
        string TerminalId,
        bool IsRefunded,
        bool CanVoid,
        bool CanRefund,
        bool PartialApproval = false,
        string AuthorizedAmount = "");
}
