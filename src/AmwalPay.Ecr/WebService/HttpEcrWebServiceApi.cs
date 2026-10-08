using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AmwalPay.Ecr.Internal;

namespace AmwalPay.Ecr.WebService
{
    /// <summary>POSTs signed JSON bodies to the Web Service ECR REST endpoints.</summary>
    internal sealed class HttpEcrWebServiceApi : IEcrWebServiceApi
    {
        // One client for the process: creating one per terminal exhausts sockets
        // under load. Deadlines are per request, below, so the client has none.
        private static readonly HttpClient Shared = CreateShared();

        private static HttpClient CreateShared()
        {
#if NET5_0_OR_GREATER
            // A single long-lived client keeps its pooled connections forever, so a server
            // that outlives a change of the Hub's address would keep talking to the old one.
            // Recycling connections re-resolves DNS. (.NET Framework has its own
            // ServicePoint lease and is left as it is.)
            return new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
            {
                Timeout = System.Threading.Timeout.InfiniteTimeSpan,
            };
#else
            return new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
#endif
        }

        private readonly EcrConfig _config;
        private readonly IEcrLogger _logger;
        private readonly HttpClient _http;

        public HttpEcrWebServiceApi(EcrConfig config, IEcrLogger logger, HttpClient? http = null)
        {
            _config = config;
            _logger = logger;
            _http = http ?? Shared;
            Routes = new EcrWebServiceRoutes(config);
        }

        public EcrWebServiceRoutes Routes { get; }

        public async Task<EcrWireResponse> SaleAsync(EcrWebServiceSaleRequest request, CancellationToken cancellationToken = default) =>
            EcrWireResponse.Parse(await PostJsonAsync(
                EcrWebServiceOperation.Sale,
                EcrWebServiceRequestJson.Sale(request, _config.SecureHashKey),
                cancellationToken).ConfigureAwait(false));

        public async Task<EcrWireResponse> VoidAsync(EcrWebServiceVoidRequest request, CancellationToken cancellationToken = default) =>
            EcrWireResponse.Parse(await PostJsonAsync(
                EcrWebServiceOperation.Void,
                EcrWebServiceRequestJson.Void(request, _config.SecureHashKey),
                cancellationToken).ConfigureAwait(false));

        public async Task<EcrWireResponse> RefundAsync(EcrWebServiceRefundRequest request, CancellationToken cancellationToken = default) =>
            EcrWireResponse.Parse(await PostJsonAsync(
                EcrWebServiceOperation.Refund,
                EcrWebServiceRequestJson.Refund(request, _config.SecureHashKey),
                cancellationToken).ConfigureAwait(false));

        public async Task<EcrWireResponse> TransactionStatusAsync(EcrWebServiceTransactionStatusRequest request, CancellationToken cancellationToken = default) =>
            EcrWireResponse.Parse(await PostJsonAsync(
                EcrWebServiceOperation.TransactionStatus,
                EcrWebServiceRequestJson.TransactionStatus(request, _config.SecureHashKey),
                cancellationToken).ConfigureAwait(false));

        /// <summary>
        /// Posts <paramref name="body"/> and returns the answer as JSON. Failures —
        /// no response, an empty body, invalid JSON, an HTTP error with nothing
        /// readable — come back as a <c>success: false</c> envelope rather than
        /// throwing, so callers read every outcome the same way.
        /// </summary>
        internal async Task<JsonObject> PostJsonAsync(
            EcrWebServiceOperation operation,
            JsonObject body,
            CancellationToken cancellationToken = default)
        {
            var clock = Stopwatch.StartNew();
            var payload = EcrJson.Write(body);
            var url = Routes.FullUrl(operation);

            // Request payload first so operators see what is about to leave.
            WebServiceHttpLog.Request(_logger, operation, url, _config.Environment, payload);
            LogSigning(body);

            // Connect and read budgets are separate in the terminal API; over
            // HttpClient they are one deadline covering both.
            using var timeoutCts = new CancellationTokenSource(_config.ConnectTimeout + _config.ResponseTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(payload, new UTF8Encoding(false), "application/json"),
                };
                request.Headers.Accept.ParseAdd("application/json");

                using var response = await _http.SendAsync(request, linked.Token).ConfigureAwait(false);
                var status = (int)response.StatusCode;
                var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var elapsedMs = clock.ElapsedMilliseconds;

                WebServiceHttpLog.Response(
                    _logger, operation, status, elapsedMs, string.IsNullOrWhiteSpace(text) ? "<empty>" : text);

                if (string.IsNullOrWhiteSpace(text))
                {
                    WebServiceHttpLog.Error(_logger, operation, $"Empty response ({response.ReasonPhrase})", elapsedMs);
                    return ErrorBody(status.ToString(), $"Empty response from Web Service ({response.ReasonPhrase})");
                }

                JsonObject parsed;
                try
                {
                    // Rejects null, arrays, bare values and repeated keys as well as
                    // invalid text: see EcrJson.ParseObject.
                    parsed = EcrJson.ParseObject(text);
                }
                catch (Exception e)
                {
                    WebServiceHttpLog.Error(_logger, operation, $"Invalid JSON: {e.Message}", elapsedMs);
                    return ErrorBody(status.ToString(), $"Response was not valid JSON: {e.Message}");
                }

                if (status < 200 || status > 299)
                {
                    var wire = EcrWireResponse.Parse(parsed);
                    if (wire.Message.Length != 0 || wire.ErrorList.Count != 0)
                    {
                        WebServiceHttpLog.Error(_logger, operation, $"HTTP {status} — {wire.DisplayMessage}", elapsedMs);
                        return parsed;
                    }
                    var reason = string.IsNullOrEmpty(response.ReasonPhrase) ? $"HTTP {status}" : response.ReasonPhrase!;
                    WebServiceHttpLog.Error(_logger, operation, reason, elapsedMs);
                    return ErrorBody(status.ToString(), reason, outcomeUnknown: status >= 500);
                }

                return parsed;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e) when (e is HttpRequestException || e is OperationCanceledException || e is System.IO.IOException)
            {
                var message = e is OperationCanceledException
                    ? $"Timed out after {EcrTerminal.FormatDuration(_config.ConnectTimeout + _config.ResponseTimeout)}"
                    : (string.IsNullOrEmpty(e.Message) ? "Web Service request failed" : e.Message);
                WebServiceHttpLog.Error(_logger, operation, message, clock.ElapsedMilliseconds);
                return ErrorBody("NO_RESPONSE", message);
            }
        }

        private void LogSigning(JsonObject body)
        {
            if (_config.SecureHashKey.Length == 0)
            {
                _logger.Debug($"[Web Service] calcHash skipped — no {WebServiceSecureHash.KeyLabel} configured");
                return;
            }
            var hash = body.Str(WebServiceSecureHash.Field);
            if (hash.Length == 0) return;
            WebServiceHttpLog.Signing(
                _logger,
                WebServiceSecureHash.KeyLabel,
                _config.SecureHashKey,
                WebServiceSecureHash.PayloadString(body),
                hash);
        }

        /// <summary>
        /// The <c>success: false</c> envelope stood in for an answer that never
        /// came. When the request may have reached the Hub — no response, an empty
        /// or unreadable body, a 5xx — the outcome is unknown, so the envelope
        /// says so with <c>nextStep</c>: a till must inquire, not treat it as a
        /// refusal and send the sale again.
        /// </summary>
        private static JsonObject ErrorBody(string responseCode, string message, bool outcomeUnknown = true)
        {
            var body = new JsonObject
            {
                ["success"] = false,
                ["responseCode"] = responseCode,
                ["message"] = message,
            };
            if (outcomeUnknown)
            {
                body["nextStep"] = UnknownOutcomeStep;
            }
            return body;
        }

        /// <summary>The wire value <see cref="NextStep.InquireByMerchantReference"/> is read from.</summary>
        internal const string UnknownOutcomeStep = "INQUIRE_BY_MERCHANT_REFERENCE";
    }
}
