using System;
using System.Collections.Generic;

namespace AmwalPay.Ecr
{
    /// <summary>
    /// Operations a terminal can be asked to run.
    /// <para>
    /// Each type declares what the caller has to supply (see
    /// <see cref="EcrTransactionTypes"/>), so a client's own form can be driven
    /// from these flags rather than repeating the rules.
    /// </para>
    /// What the terminal will <em>accept</em> is a separate question, answered by
    /// the terminal: whether an original transaction exists, whether it is still
    /// within the void window, whether it may be voided at all.
    /// </summary>
    public enum EcrTransactionType
    {
        /// <summary>An ordinary purchase.</summary>
        Sale,

        /// <summary>
        /// Cancels an earlier transaction from the same batch, for its full amount.
        /// No card is presented: the terminal already holds the original's details.
        /// </summary>
        Void,

        /// <summary>
        /// Returns money against an earlier transaction, in full or in part.
        /// The cardholder presents their card to receive it.
        /// </summary>
        Refund,

        /// <summary>
        /// Asks what became of an earlier transaction. Reads only: no card is
        /// presented and no money moves, so it is safe to repeat and is answered
        /// even while the terminal is taking a payment.
        /// Run it with <see cref="EcrTerminal.InquireAsync"/>.
        /// </summary>
        Inquiry,

        /// <summary>
        /// Asks for a transaction's e-receipt. Non-financial and reprintable.
        /// Run it with <see cref="EcrTerminal.ReceiptAsync"/>. Not offered as a
        /// menu choice — a receipt follows a transaction the operator already has
        /// in front of them.
        /// </summary>
        Receipt,
    }

    public static class EcrTransactionTypes
    {
        /// <summary>
        /// The operations an operator picks from, in the order a till usually
        /// offers them. <see cref="EcrTransactionType.Receipt"/> is absent by design.
        /// </summary>
        public static IReadOnlyList<EcrTransactionType> MenuOptions { get; } = new[]
        {
            EcrTransactionType.Sale,
            EcrTransactionType.Void,
            EcrTransactionType.Refund,
            EcrTransactionType.Inquiry,
        };

        /// <summary>The value carried in the message's <c>messageType</c> field.</summary>
        public static string MessageType(this EcrTransactionType type) => type switch
        {
            EcrTransactionType.Sale => "SALE",
            EcrTransactionType.Void => "VOID",
            EcrTransactionType.Refund => "REFUND",
            EcrTransactionType.Inquiry => "INQUIRY",
            EcrTransactionType.Receipt => "RECEIPT",
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };

        /// <summary>A human-readable name, for a client that wants one.</summary>
        public static string DisplayName(this EcrTransactionType type) => type switch
        {
            EcrTransactionType.Sale => "Sale",
            EcrTransactionType.Void => "Void",
            EcrTransactionType.Refund => "Refund",
            EcrTransactionType.Inquiry => "Inquiry",
            EcrTransactionType.Receipt => "Receipt",
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };

        /// <summary>
        /// Whether the caller supplies an amount. Operations against an earlier
        /// transaction take the original's amount, which only the terminal knows.
        /// </summary>
        public static bool RequiresAmount(this EcrTransactionType type) =>
            type == EcrTransactionType.Sale || type == EcrTransactionType.Refund;

        /// <summary>
        /// Whether the caller identifies an earlier transaction by its receipt
        /// number (STAN). The terminal looks the original up by STAN and terminal.
        /// </summary>
        public static bool RequiresOriginalStan(this EcrTransactionType type) =>
            type != EcrTransactionType.Sale;

        /// <summary>
        /// Whether the caller supplies the date of the original transaction. A
        /// receipt number is only unique within a terminal's day.
        /// </summary>
        public static bool RequiresOriginalDate(this EcrTransactionType type) =>
            type == EcrTransactionType.Refund
            || type == EcrTransactionType.Inquiry
            || type == EcrTransactionType.Receipt;

        /// <summary>Whether the operation moves money. Reading a record does not.</summary>
        public static bool MovesMoney(this EcrTransactionType type) =>
            type == EcrTransactionType.Sale
            || type == EcrTransactionType.Void
            || type == EcrTransactionType.Refund;

        /// <summary>
        /// Whether the caller may name a different terminal to act on — only
        /// meaningful for an operation targeting an earlier transaction, which may
        /// have been taken on another till.
        /// </summary>
        public static bool AllowsOtherTerminal(this EcrTransactionType type) =>
            type.RequiresOriginalStan();
    }
}
