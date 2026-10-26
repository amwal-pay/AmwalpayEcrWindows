using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AmwalPay.Ecr.Internal;
using AmwalPay.Ecr.WebService;

namespace AmwalPay.Ecr
{
    /// <summary>
    /// Web Service ECR client — REST over HTTPS with JSON body signing.
    /// <para>
    /// Use this instead of <see cref="EcrTerminal"/> when the terminal is
    /// configured for Web Service mode (<c>ecrMode: 4</c>).
    /// </para>
    /// </summary>
    public sealed class EcrWebServiceTerminal
    {
        private readonly string _terminalSerial;
        private readonly EcrConfig _config;
        private readonly IEcrLogger _logger;
        private readonly HttpEcrWebServiceApi _api;

        public EcrWebServiceTerminal(string terminalSerial, EcrConfig? config = null, IEcrLogger? logger = null)
            : this(terminalSerial, config, logger, null)
        {
        }

        internal EcrWebServiceTerminal(string terminalSerial, EcrConfig? config, IEcrLogger? logger, System.Net.Http.HttpClient? http)
        {
            _terminalSerial = terminalSerial ?? "";
            _config = config ?? new EcrConfig();
            _logger = logger ?? EcrLogger.None;
            _api = new HttpEcrWebServiceApi(_config, _logger, http);
        }

        public async Task<EcrResult> SaleAsync(
            decimal amount,
            string merchantReference = "",
            CancellationToken cancellationToken = default)
        {
            var ids = BackendIds();
            if (ids == null) return InvalidIds();
            var (reference, body) = BuildRequest(
                EcrTransactionType.Sale,
                ids.Value,
                merchantReference,
                amountMinorUnits: ToMinorUnits(amount, _config.MinorUnitDigits));
            var response = await _api.PostJsonAsync(EcrWebServiceOperation.Sale, body, cancellationToken).ConfigureAwait(false);
            return response.ToEcrResult(reference, _config.MinorUnitDigits);
        }

        public async Task<EcrResult> VoidAsync(
            string receiptNumber,
            string merchantReference = "",
            CancellationToken cancellationToken = default)
        {
            var ids = BackendIds();
            if (ids == null) return InvalidIds();
            var (reference, body) = BuildRequest(
                EcrTransactionType.Void,
                ids.Value,
                merchantReference,
                stan: receiptNumber);
            var response = await _api.PostJsonAsync(EcrWebServiceOperation.Void, body, cancellationToken).ConfigureAwait(false);
            return response.ToEcrResult(reference, _config.MinorUnitDigits);
        }

        public async Task<EcrResult> RefundAsync(
            decimal amount,
            string receiptNumber,
            string transactionDate,
            string merchantReference = "",
            CancellationToken cancellationToken = default)
        {
            var ids = BackendIds();
            if (ids == null) return InvalidIds();
            var (reference, body) = BuildRequest(
                EcrTransactionType.Refund,
                ids.Value,
                merchantReference,
                amountMinorUnits: ToMinorUnits(amount, _config.MinorUnitDigits),
                stan: receiptNumber,
                originalTransactionDate: transactionDate);
            var response = await _api.PostJsonAsync(EcrWebServiceOperation.Refund, body, cancellationToken).ConfigureAwait(false);
            return response.ToEcrResult(reference, _config.MinorUnitDigits);
        }

        public async Task<EcrInquiry> InquireAsync(
            string receiptNumber,
            string transactionDate,
            string merchantReference = "",
            CancellationToken cancellationToken = default)
        {
            var ids = BackendIds();
            if (ids == null)
            {
                return new EcrInquiry.Failed(
                    merchantReference,
                    new Failure.Malformed("merchantId and terminalId are required for Web Service ECR"));
            }
            var (reference, body) = BuildRequest(
                EcrTransactionType.Inquiry,
                ids.Value,
                merchantReference,
                stan: receiptNumber,
                originalTransactionDate: transactionDate);
            var response = await _api.PostJsonAsync(EcrWebServiceOperation.TransactionStatus, body, cancellationToken).ConfigureAwait(false);

            // A transport failure is synthesised as an answer with no data, which
            // would read as "not found" - and a till told nothing was taken may
            // charge again. It is a failed lookup, not a finding.
            if (response.Str("nextStep") == HttpEcrWebServiceApi.UnknownOutcomeStep && response["data"] is null)
            {
                var reason = response.Str("message");
                return new EcrInquiry.Failed(
                    reference,
                    new Failure.ConnectionLost(reason.Length != 0 ? reason : "The Web Service did not answer"));
            }
            return response.ToInquiry(reference, _config.MinorUnitDigits);
        }

        public async Task<EcrInquiry> InquireByReferenceAsync(
            string originalReference,
            string transactionDate,
            string merchantReference = "",
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(originalReference))
            {
                throw new ArgumentException(
                    "An inquiry by reference needs the original's reference",
                    nameof(originalReference));
            }
            var ids = BackendIds();
            if (ids == null)
            {
                return new EcrInquiry.Failed(
                    merchantReference,
                    new Failure.Malformed("merchantId and terminalId are required for Web Service ECR"));
            }
            var (reference, body) = BuildRequest(
                EcrTransactionType.Inquiry,
                ids.Value,
                merchantReference,
                stan: "",
                originalTransactionDate: transactionDate,
                originalMerchantReference: originalReference);
            var response = await _api.PostJsonAsync(EcrWebServiceOperation.TransactionStatus, body, cancellationToken).ConfigureAwait(false);

            if (response.Str("nextStep") == HttpEcrWebServiceApi.UnknownOutcomeStep && response["data"] is null)
            {
                var reason = response.Str("message");
                return new EcrInquiry.Failed(
                    reference,
                    new Failure.ConnectionLost(reason.Length != 0 ? reason : "The Web Service did not answer"));
            }
            return response.ToInquiry(reference, _config.MinorUnitDigits);
        }

        /// <summary>
        /// Posts <c>/Ecr/closeReceipt</c> for the sale / void / refund identified by
        /// <paramref name="originalMerchantReference"/>.
        /// <para>
        /// Call when the cashier taps Close receipt — the same explicit action as
        /// Wi‑Fi / USB  <c>CLOSE_RECEIPT</c>. Not invoked automatically by
        /// <see cref="SaleAsync"/> / <see cref="VoidAsync"/> / <see cref="RefundAsync"/>.
        /// </para>
        /// </summary>
        /// <param name="originalMerchantReference">The money-moving operation's reference.</param>
        /// <param name="merchantReference">This close request's own id (blank → generated).</param>
        public async Task<EcrReceiptClosed> CloseReceiptAsync(
            string originalMerchantReference,
            string merchantReference = "",
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(originalMerchantReference))
            {
                return new EcrReceiptClosed.Failed(
                    merchantReference,
                    new Failure.Malformed("Close receipt needs the original merchant reference"));
            }

            var ids = BackendIds();
            if (ids == null)
            {
                return new EcrReceiptClosed.Failed(
                    merchantReference,
                    new Failure.Malformed("merchantId and terminalId are required for Web Service ECR"));
            }

            var built = EcrWebServiceMessage.BuildCloseReceipt(
                merchantId: ids.Value.MerchantId,
                terminalId: ids.Value.TerminalId,
                terminalSerial: _terminalSerial,
                secureHashKey: _config.SecureHashKey,
                ecrId: _config.EcrId,
                merchantReference: merchantReference,
                originalMerchantReference: originalMerchantReference);
            WebServiceHttpLog.Operation(
                _logger,
                EcrTransactionType.CloseReceipt.MessageType(),
                built.Reference,
                _terminalSerial,
                ids.Value.MerchantId,
                ids.Value.TerminalId);
            var response = await _api.PostJsonAsync(
                EcrWebServiceOperation.CloseReceipt, built.Json, cancellationToken).ConfigureAwait(false);
            return response.ToReceiptClosed(built.Reference);
        }

        /// <summary>
        /// Posts <c>/Ecr/settle</c>. Confirm with the cashier before calling: the
        /// Hub starts the close as soon as the request arrives. The answer is read
        /// the same way as a Wi-Fi <c>SETTLEMENT</c>.
        /// </summary>
        /// <param name="merchantReference">This request's own id (blank → generated).</param>
        public async Task<EcrSettlement> SettlementAsync(
            string merchantReference = "",
            CancellationToken cancellationToken = default)
        {
            var ids = BackendIds();
            if (ids == null)
            {
                return new EcrSettlement.Failed(
                    merchantReference,
                    new Failure.Malformed("merchantId and terminalId are required for Web Service ECR"));
            }

            var built = Plain(
                ids.Value,
                merchantReference,
                EcrWebServiceOperation.Settle);
            var response = await _api.PostJsonAsync(
                EcrWebServiceOperation.Settle, built.Json, cancellationToken).ConfigureAwait(false);
            if (Lost(response))
            {
                var reason = response.Str("message");
                return new EcrSettlement.Failed(
                    built.Reference,
                    new Failure.ConnectionLost(reason.Length != 0 ? reason : "The Web Service did not answer"));
            }

            return response.ToSettlement(built.Reference, _config.MinorUnitDigits);
        }

        /// <summary>
        /// Posts <c>/Ecr/CheckReachability</c> with the probe budget, so a Hub that
        /// does not answer does not consume the sale's response timeout.
        /// </summary>
        public async Task<EcrReachability> ProbeReachabilityAsync(CancellationToken cancellationToken = default)
        {
            var origin = _api.Routes.HubOrigin();
            Uri uri;
            try
            {
                uri = new Uri(origin);
            }
            catch (UriFormatException)
            {
                return new EcrReachability(false, origin, 0, "The Web Service address is not a URL", origin);
            }

            var port = uri.IsDefaultPort ? (uri.Scheme == "https" ? 443 : 80) : uri.Port;
            var ids = BackendIds();
            if (ids == null)
            {
                return new EcrReachability(
                    false,
                    uri.Host,
                    port,
                    "merchantId and terminalId are required for Web Service ECR",
                    origin);
            }

            var built = Plain(ids.Value, "", EcrWebServiceOperation.CheckReachability);
            var response = await _api.PostJsonAsync(
                EcrWebServiceOperation.CheckReachability,
                built.Json,
                cancellationToken,
                deadline: _config.ProbeTimeout).ConfigureAwait(false);

            if (Lost(response) || !EcrResponseEnvelope.Parse(response).Success)
            {
                var reason = response.Str("message");
                if (reason.Length == 0) reason = EcrResponseEnvelope.Parse(response).DisplayMessage;
                return new EcrReachability(
                    false,
                    uri.Host,
                    port,
                    reason.Length != 0 ? reason : "The terminal is not reachable",
                    origin);
            }

            return new EcrReachability(true, uri.Host, port, null, origin);
        }

        private (string Reference, JsonObject Json) Plain(
            (long MerchantId, long TerminalId) ids,
            string merchantReference,
            EcrWebServiceOperation operation)
        {
            var built = EcrWebServiceMessage.BuildPlain(
                merchantId: ids.MerchantId,
                terminalId: ids.TerminalId,
                terminalSerial: _terminalSerial,
                secureHashKey: _config.SecureHashKey,
                currencyCode: _config.CurrencyCode,
                ecrId: _config.EcrId,
                merchantReference: merchantReference);
            WebServiceHttpLog.Operation(
                _logger,
                operation.Label(),
                built.Reference,
                _terminalSerial,
                ids.MerchantId,
                ids.TerminalId);
            return built;
        }

        private static bool Lost(JsonObject response) =>
            response.Str("nextStep") == HttpEcrWebServiceApi.UnknownOutcomeStep && response["data"] is null;

        private (string Reference, JsonObject Body) BuildRequest(
            EcrTransactionType type,
            (long MerchantId, long TerminalId) ids,
            string merchantReference = "",
            long? amountMinorUnits = null,
            string stan = "",
            string originalTransactionDate = "",
            string originalMerchantReference = "")
        {
            var built = EcrWebServiceMessage.Build(
                type: type,
                merchantId: ids.MerchantId,
                terminalId: ids.TerminalId,
                terminalSerial: _terminalSerial,
                secureHashKey: _config.SecureHashKey,
                amountMinorUnits: amountMinorUnits,
                stan: stan,
                originalTransactionDate: originalTransactionDate,
                originalMerchantReference: originalMerchantReference,
                merchantReference: merchantReference,
                currencyCode: _config.CurrencyCode,
                ecrId: _config.EcrId);
            WebServiceHttpLog.Operation(
                _logger, type.MessageType(), built.Reference, _terminalSerial, ids.MerchantId, ids.TerminalId);
            return (built.Reference, built.Json);
        }

        private (long MerchantId, long TerminalId)? BackendIds()
        {
            if (!long.TryParse(_config.MerchantId, out var merchantId)) return null;
            if (!long.TryParse(_config.TerminalId, out var terminalId)) return null;
            return (merchantId, terminalId);
        }

        private static EcrResult InvalidIds() =>
            new EcrResult.Failed(
                MerchantReference: "",
                Failure: new Failure.Malformed(
                    "merchantId and terminalId must be set on EcrConfig for Web Service ECR"));

        private static long ToMinorUnits(decimal amount, int digits) =>
            checked((long)(Math.Round(amount, digits, MidpointRounding.AwayFromZero) * EcrMessage.Pow10(digits)));
    }
}
