using System;
using System.Collections.Generic;
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
                var nextStep = NextSteps.Of(json.Str("nextStep"));
                // Web Service / Hub synthesise timeouts as a false envelope with
                // nextStep INQUIRE_BY_MERCHANT_REFERENCE (and often responseCode
                // NO_RESPONSE). That is not a decline — money may have moved — so it
                // must surface as Failed and push the till to inquire by reference.
                if (nextStep == NextStep.InquireByMerchantReference)
                {
                    var reason = envelope.DisplayMessage.Length != 0
                        ? envelope.DisplayMessage
                        : "timeout";
                    var code = envelope.ResponseCode.Trim().ToUpperInvariant();
                    Failure failure =
                        code == "NO_RESPONSE" ||
                        reason.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0
                            ? new Failure.Timeout(reason)
                            : new Failure.ConnectionLost(reason);
                    return new EcrResult.Failed(
                        MerchantReference: resolvedReference,
                        Failure: failure);
                }

                var declinedReason = envelope.DisplayMessage;
                return new EcrResult.Declined(
                    MerchantReference: resolvedReference,
                    ResponseCode: envelope.ResponseCode,
                    Reason: declinedReason.Length != 0 ? declinedReason : "Declined",
                    NextStep: nextStep,
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
        /// <para>
        /// Hub / Socket.IO answers may also mirror the record at the top level
        /// (PDF flat fields) and still stamp correlation-only keys under
        /// <c>data</c> when nothing was found — so "found" means transaction
        /// markers are present, not merely that <c>data</c> is a non-null object.
        /// </para>
        /// </summary>
        public static EcrInquiry ToInquiry(this JsonObject json, string merchantReference, int minorUnitDigits)
        {
            var envelope = EcrResponseEnvelope.Parse(json);
            var data = InquiryRecord(envelope.Data, json);
            var resolvedReference = Or(
                data?.Str("merchantReference") ?? "",
                Or(envelope.MerchantReference, merchantReference));

            // Whether a transaction came back, not whether it succeeded.
            //
            // `success` reports the *transaction's* outcome - a declined sale
            // answers false - so reading it here would turn every declined
            // transaction the terminal did find into "not found". A till told that
            // cannot tell a payment that was refused from one the terminal has
            // never heard of, and the second of those invites it to charge again.
            //
            // The payload separates them: a lookup that found nothing carries no
            // transaction record, while one that found a declined sale carries
            // the whole record - status, RRN, amount.
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

        /// <summary>
        /// Nested <c>data</c> when it names a transaction; otherwise top-level
        /// Hub flat fields. Correlation-only <c>data</c> (merchantReference /
        /// terminalSerial) is not a found record.
        /// </summary>
        private static JsonObject? InquiryRecord(JsonObject? nested, JsonObject json)
        {
            if (HasTransactionRecord(nested)) return nested;
            var root = EcrResponseEnvelope.Root(json);
            return HasTransactionRecord(root) ? root : null;
        }

        private static bool HasTransactionRecord(JsonObject? data)
        {
            if (data is null) return false;
            return NonEmpty(data, "systemTraceNr")
                || NonEmpty(data, "stan")
                || NonEmpty(data, "transactionId")
                || NonEmpty(data, "status")
                || NonEmpty(data, "rrn")
                || NonEmpty(data, "authCode")
                || NonEmpty(data, "amount")
                || NonEmpty(data, "authorizeAmount")
                || NonEmpty(data, "transactionType")
                || NonEmpty(data, "transactionTypeDisplayName");
        }

        private static bool NonEmpty(JsonObject data, string key) => data.Str(key).Length != 0;

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
        /// Reads the answer to a request to put the receipt away.
        /// Success is taken from the envelope only — the terminal reports that it
        /// is idle, whether or not it had a receipt up.
        /// </summary>
        public static EcrReceiptClosed ToReceiptClosed(this JsonObject json, string merchantReference)
        {
            var envelope = EcrResponseEnvelope.Parse(json);
            var resolvedReference = Or(envelope.MerchantReference, merchantReference);

            if (!envelope.Success)
            {
                var reason = envelope.DisplayMessage;
                return new EcrReceiptClosed.Refused(
                    MerchantReference: resolvedReference,
                    ResponseCode: envelope.ResponseCode,
                    Reason: reason.Length != 0 ? reason : "The terminal refused to close its receipt",
                    Raw: EcrJson.Write(json));
            }

            return new EcrReceiptClosed.Idle(resolvedReference, EcrJson.Write(json));
        }

        /// <summary>The types a settlement reports totals for, in receipt order.</summary>
        private static readonly string[] SettlementTotalTypes =
        {
            "SALE", "REFUND", "VOID", "PRE_AUTH", "CAPTURE",
        };

        /// <summary>
        /// Reads the answer to a settlement.
        /// <para>
        /// <c>settlementStatus</c> decides the branch, not the envelope alone: an
        /// empty batch is a success with nothing in it, and a refusal before the
        /// settlement started — busy, not idle, a terminal too old to know it —
        /// carries no status at all.
        /// </para>
        /// </summary>
        public static EcrSettlement ToSettlement(this JsonObject json, string merchantReference, int minorUnitDigits)
        {
            var envelope = EcrResponseEnvelope.Parse(json);
            var data = envelope.Data;
            var resolvedReference = Or(envelope.MerchantReference, merchantReference);
            var status = data?.Str("settlementStatus") ?? "";
            var raw = EcrJson.Write(json);

            if (envelope.Success && status == "NO_TRANSACTIONS")
            {
                var message = envelope.DisplayMessage;
                return new EcrSettlement.NothingToSettle(
                    resolvedReference,
                    message.Length != 0 ? message : "There are no transactions to settle",
                    raw);
            }

            if (!envelope.Success || data == null || status != "SETTLED")
            {
                var reason = envelope.DisplayMessage;
                return new EcrSettlement.NotSettled(
                    MerchantReference: resolvedReference,
                    ResponseCode: envelope.ResponseCode,
                    Status: status,
                    Reason: reason.Length != 0 ? reason : "The batch was not settled",
                    UploadedTransactions: WholeNumber(data, "uploadedTransactions"),
                    Raw: raw);
            }

            var totalsNode = data["totals"] as JsonObject;
            var totals = new Dictionary<string, EcrSettlementTotal>(SettlementTotalTypes.Length);
            foreach (var type in SettlementTotalTypes)
            {
                var row = totalsNode?[type] as JsonObject;
                totals[type] = row == null
                    ? new EcrSettlementTotal(0, MajorUnits("0", minorUnitDigits))
                    : new EcrSettlementTotal(WholeNumber(row, "count") ?? 0, MajorUnits(row.Str("amount"), minorUnitDigits));
            }

            return new EcrSettlement.Settled(
                MerchantReference: resolvedReference,
                BatchId: data.Str("batchId"),
                Currency: data.Str("currency"),
                StartTime: data.Str("startTime"),
                EndTime: data.Str("endTime"),
                TotalTransactions: WholeNumber(data, "totalTransactions") ?? 0,
                NetAmount: MajorUnits(data.Str("netAmount"), minorUnitDigits),
                Totals: totals,
                Raw: raw);
        }

        private static int? WholeNumber(JsonObject? json, string key)
        {
            if (json == null) return null;
            var text = json.Str(key);
            return int.TryParse(text, out var value) ? value : null;
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
