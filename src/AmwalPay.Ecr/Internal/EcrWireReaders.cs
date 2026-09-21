using System;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;

namespace AmwalPay.Ecr.Internal
{
    /// <summary>Reads the terminal's answer into the SDK's result types.</summary>
    internal static class EcrWireReaders
    {
        /// <summary>Reads the terminal's answer into one of the three outcomes.</summary>
        public static EcrResult ToEcrResult(this JsonObject json, string merchantReference, int minorUnitDigits)
        {
            var envelope = EcrResponseEnvelope.Parse(json);
            var data = envelope.Data;
            var resolvedReference = Or(envelope.MerchantReference, merchantReference);

            if (!envelope.Success)
            {
                var reason = envelope.DisplayMessage;
                return new EcrResult.Declined(
                    MerchantReference: resolvedReference,
                    ResponseCode: envelope.ResponseCode,
                    Reason: reason.Length != 0 ? reason : "Declined",
                    NextStep: NextSteps.Of(json.Str("nextStep")),
                    Raw: EcrJson.Write(json));
            }

            var amountMajor = SettledAmountMajor(data, minorUnitDigits, json);

            var requestedMajor = data != null && data.Str("amount").Length != 0
                ? data.Str("amount")
                : MajorUnits(json.Str("requestedAmount"), minorUnitDigits);

            return new EcrResult.Approved(
                MerchantReference: resolvedReference,
                Amount: amountMajor,
                ResponseCode: envelope.ResponseCode,
                Rrn: FirstNonEmpty(data?.Str("rrn"), json.Str("rrn")),
                AuthCode: FirstNonEmpty(data?.Str("authCode"), json.Str("authCode")),
                MaskedPan: FirstNonEmpty(data?.Str("cardMask"), data?.Str("maskedPan"), json.Str("maskedPan")),
                PartialApproval: data != null ? data.Flag("isPartialApprove") : json.Flag("partialApproval"),
                RequestedAmount: requestedMajor,
                Raw: EcrJson.Write(json));
        }

        /// <summary>
        /// Reads the terminal's answer to an inquiry. The transaction sits under
        /// <c>data</c>; the outer envelope describes the lookup.
        /// </summary>
        public static EcrInquiry ToInquiry(this JsonObject json, string merchantReference, int minorUnitDigits)
        {
            var envelope = EcrResponseEnvelope.Parse(json);
            var data = envelope.Data;
            var resolvedReference = Or(envelope.MerchantReference, merchantReference);

            // Whether a transaction came back, not whether it succeeded.
            //
            // `success` reports the *transaction's* outcome - a declined sale
            // answers false - so reading it here would turn every declined
            // transaction the terminal did find into "not found". A till told that
            // cannot tell a payment that was refused from one the terminal has
            // never heard of, and the second of those invites it to charge again.
            //
            // The payload separates them: a lookup that found nothing carries no
            // data at all, while one that found a declined sale carries the whole
            // record - status, RRN, amount.
            if (data is null)
            {
                var reason = envelope.DisplayMessage;
                return new EcrInquiry.NotFound(
                    MerchantReference: resolvedReference,
                    Reason: reason.Length != 0 ? reason : "Transaction not found",
                    Raw: EcrJson.Write(json));
            }

            return new EcrInquiry.Found(
                MerchantReference: resolvedReference,
                Transaction: new EcrTransaction(
                    TransactionId: data.Str("transactionId"),
                    Stan: Or(data.Str("stan"), data.Str("systemTraceNr")),
                    Type: Or(data.Str("transactionTypeDisplayName"), data.Str("transactionType")),
                    Status: Or(data.Str("status"), envelope.Success ? "Approved" : "Declined"),
                    // Contract: Amount is what was requested; AuthorizedAmount is
                    // what settled. Do not use SettledAmountMajor here — that
                    // prefers authorizeAmount and would overwrite the requested figure.
                    Amount: Or(data.Str("amount"), SettledAmountMajor(data, minorUnitDigits, json)),
                    TotalAmount: data.Str("totalAmount"),
                    Currency: Or(data.Str("currency"), data.Str("currencyId")),
                    TransactionTime: data.Str("transactionTime"),
                    MaskedPan: Or(data.Str("cardMask"), data.Str("cardNumber")),
                    CardHolderName: data.Str("cardHolderName"),
                    Rrn: data.Str("rrn"),
                    AuthCode: data.Str("authCode"),
                    BatchId: data.Str("batchId"),
                    TerminalId: data.Str("terminalId"),
                    IsRefunded: data.Flag("isRefunded"),
                    CanVoid: data.Flag("canVoid"),
                    CanRefund: data.Flag("canRefund"),
                    // Reported rather than inferred from the two amounts, so a
                    // till does not have to compare them to notice the cardholder
                    // was charged less than the sale asked for.
                    PartialApproval: data.Flag("isPartialApprove"),
                    AuthorizedAmount: Or(data.Str("authorizeAmount"), data.Str("amount"))),
                Raw: EcrJson.Write(json));
        }

        /// <summary>Reads the terminal's answer to a receipt request.</summary>
        public static EcrReceipt ToReceipt(this JsonObject json, string merchantReference)
        {
            var envelope = EcrResponseEnvelope.Parse(json);
            var resolvedReference = Or(envelope.MerchantReference, merchantReference);
            var url = envelope.Data?.Str("receiptUrl") ?? "";

            if (url.Length == 0)
            {
                var reason = envelope.DisplayMessage;
                return new EcrReceipt.Unavailable(
                    MerchantReference: resolvedReference,
                    Reason: reason.Length != 0 ? reason : "The receipt could not be generated",
                    Raw: EcrJson.Write(json));
            }

            return new EcrReceipt.Ready(resolvedReference, url, EcrJson.Write(json));
        }

        /// <summary>
        /// The amount actually settled, in major units. Uses <c>authorizeAmount</c>
        /// when it is greater than zero; otherwise <c>amount</c>. Voids and other
        /// non-financial answers often carry <c>authorizeAmount</c> as zero while
        /// <c>amount</c> holds the figure to show.
        /// </summary>
        public static string SettledAmountMajor(JsonObject? data, int minorUnitDigits, JsonObject root)
        {
            if (data != null)
            {
                var authorized = ParseDecimal(data.Str("authorizeAmount")) ?? 0m;
                if (authorized > 0m) return data.Str("authorizeAmount");
                var amount = data.Str("amount");
                if (amount.Length != 0) return amount;
            }
            return MajorUnits(root.Str("amount"), minorUnitDigits);
        }

        /// <summary>
        /// Amounts travel as minor units — <c>000000000365</c> — and are reported in
        /// major units, so a caller can show what it is given without knowing the
        /// convention.
        /// </summary>
        public static string MajorUnits(string minorUnits, int digits)
        {
            if (minorUnits.Length == 0) return "";
            if (!BigInteger.TryParse(minorUnits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
            {
                return minorUnits;
            }

            var negative = value.Sign < 0;
            var text = BigInteger.Abs(value).ToString(CultureInfo.InvariantCulture);
            if (digits > 0)
            {
                text = text.PadLeft(digits + 1, '0');
                text = text.Substring(0, text.Length - digits) + "." + text.Substring(text.Length - digits);
            }
            return negative && value != BigInteger.Zero ? "-" + text : text;
        }

        private static decimal? ParseDecimal(string text) =>
            decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : (decimal?)null;

        private static string Or(string value, string fallback) => value.Length != 0 ? value : fallback;

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrEmpty(value)) return value!;
            }
            return "";
        }
    }
}
