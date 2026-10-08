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
