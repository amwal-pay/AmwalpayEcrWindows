namespace AmwalPay.Ecr
{
    /// <summary>
    /// How a transaction ended.
    /// <para>
    /// The three outcomes are deliberately distinct, because they call for
    /// different things from a till: <see cref="Approved"/> means money moved,
    /// <see cref="Declined"/> means the terminal answered and refused, and
    /// <see cref="Failed"/> means nothing was learned at all — the last of which
    /// may still have taken the customer's money, and must never be treated as a
    /// refusal.
    /// </para>
    /// </summary>
    /// <param name="MerchantReference">
    /// The reference the transaction was sent with, so a caller can match the
    /// outcome both to what it sent and to its own record of the sale. The
    /// caller's own reference when one was given, otherwise the one the SDK
    /// generated. Either way it is worth storing: it is what names this
    /// transaction to the terminal afterwards.
    /// </param>
    public abstract record EcrResult(string MerchantReference)
    {
        /// <summary>Money was taken.</summary>
        /// <param name="Amount">The amount actually taken, in major units, e.g. <c>1.234</c>.</param>
        /// <param name="ResponseCode">The backend's code, <c>00</c> on a plain approval.</param>
        /// <param name="MaskedPan">Masked, never the full number: <c>543173xxxx5785</c>. May be empty.</param>
        /// <param name="PartialApproval">
        /// Set when the bank authorised less than was asked for. The difference
        /// has to be collected by other means — this is not a refusal.
        /// </param>
        /// <param name="RequestedAmount">What was asked for, when <paramref name="PartialApproval"/> is set.</param>
        /// <param name="Raw">The terminal's full answer as JSON text, for anything not surfaced above.</param>
        public sealed record Approved(
            string MerchantReference,
            string Amount,
            string ResponseCode,
            string Rrn,
            string AuthCode,
            string MaskedPan,
            bool PartialApproval,
            string RequestedAmount,
            string Raw) : EcrResult(MerchantReference);

        /// <summary>
        /// The terminal answered and no money was taken.
        /// <para>
        /// <paramref name="Reason"/> carries the backend's own words where it
        /// gave any — its error list in preference to its generic message.
        /// </para>
        /// </summary>
        /// <param name="NextStep">
        /// What to do about it. Usually <see cref="Ecr.NextStep.None"/> — a decline
        /// says plainly that no money moved — but the terminal can report that it
        /// does not actually know, and then this asks for an inquiry rather than a retry.
        /// </param>
        public sealed record Declined(
            string MerchantReference,
            string ResponseCode,
            string Reason,
            NextStep NextStep,
            string Raw) : EcrResult(MerchantReference);

        /// <summary>
        /// The exchange itself failed, so the outcome is unknown.
        /// <para>
        /// A <see cref="Failure.Timeout"/> in particular does <b>not</b> mean the
        /// transaction did not happen: the terminal may have completed it and been
        /// unable to say so. Reconcile before retrying.
        /// </para>
        /// </summary>
        /// <param name="Recovered">
        /// What the terminal said when asked afterwards, or null when it was not
        /// asked. The SDK follows a lost exchange with an inquiry by
        /// <see cref="EcrResult.MerchantReference"/> — see
        /// <see cref="EcrConfig.AutoInquireOnFailure"/>. <see cref="EcrInquiry.Found"/>
        /// settles it; anything else means it is still unknown.
        /// </param>
        public sealed record Failed(
            string MerchantReference,
            Failure Failure,
            EcrInquiry? Recovered = null) : EcrResult(MerchantReference)
        {
            /// <summary>
            /// Always <see cref="Ecr.NextStep.InquireByMerchantReference"/>. No
            /// answer arrived at all, so nothing here can say the transaction did
            /// not happen.
            /// </summary>
            public NextStep NextStep => NextStep.InquireByMerchantReference;

            /// <summary>
            /// Whether the follow-up actually found the transaction, so this is no
            /// longer an unknown outcome — only a delivery that failed.
            /// </summary>
            public bool Settled => Recovered is EcrInquiry.Found;
        }
    }

    /// <summary>
    /// What the till should do about an outcome it cannot act on directly.
    /// <para>
    /// Stated by the terminal rather than left for a till to infer from a response
    /// code, because the wrong inference is expensive: reading an unknown outcome
    /// as a refusal and sending the sale again charges the cardholder twice.
    /// </para>
    /// </summary>
    public enum NextStep
    {
        /// <summary>The answer settles the matter. Nothing further is needed.</summary>
        None,

        /// <summary>
        /// Ask what became of the transaction, quoting the reference this request
        /// was sent with. <b>Never retry instead.</b>
        /// </summary>
        InquireByMerchantReference,
    }

    internal static class NextSteps
    {
        /// <summary>
        /// Reads the wire value. Unknown values read as <see cref="NextStep.None"/>:
        /// a till must not act on a step this version does not understand.
        /// </summary>
        public static NextStep Of(string value) =>
            value == "INQUIRE_BY_MERCHANT_REFERENCE" ? NextStep.InquireByMerchantReference : NextStep.None;
    }

    /// <summary>Why an exchange could not be completed.</summary>
    /// <param name="Message">A human-readable account of the failure.</param>
    public abstract record Failure(string Message)
    {
        /// <summary>
        /// Whether the request reached the terminal, leaving the outcome unknown.
        /// <para>
        /// The one distinction that matters after a failure. <see cref="Unreachable"/>
        /// means no connection was ever made and nothing was attempted, so there is
        /// nothing to reconcile. Everything else means the request went out and the
        /// terminal may have acted on it, whatever came back — or did not.
        /// </para>
        /// </summary>
        public bool OutcomeUnknown => this is not Unreachable;

        /// <summary>Nothing is listening: wrong address, terminal off, or a different network.</summary>
        public sealed record Unreachable(string Message) : Failure(Message);

        /// <summary>The terminal accepted the request but never answered. The transaction may still have completed.</summary>
        public sealed record Timeout(string Message) : Failure(Message);

        /// <summary>The terminal answered with something this SDK could not read.</summary>
        public sealed record Malformed(string Message) : Failure(Message);

        /// <summary>The connection broke part way through.</summary>
        public sealed record ConnectionLost(string Message) : Failure(Message);

        /// <summary>
        /// The answer could not be shown to have come from the terminal.
        /// <para>
        /// Either it was not signed with this till's key, or it answered a
        /// different request. Both mean something else may have replied on the
        /// terminal's port — so the answer is discarded rather than believed.
        /// </para>
        /// Like <see cref="Timeout"/>, this is <b>not</b> a decline: the terminal
        /// may have taken the payment. Inquire before retrying. Seeing this
        /// repeatedly usually means the key on this till and the key on the
        /// terminal do not match.
        /// </summary>
        public sealed record Unauthenticated(string Message) : Failure(Message);
    }
}
