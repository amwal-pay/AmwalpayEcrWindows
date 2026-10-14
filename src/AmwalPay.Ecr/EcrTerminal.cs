using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AmwalPay.Ecr.Internal;

namespace AmwalPay.Ecr
{
    /// <summary>
    /// A POS terminal reachable over the network.
    /// <para>
    /// One instance addresses one terminal; it holds no connection between calls,
    /// so it is cheap to keep and safe to reuse.
    /// </para>
    /// <code>
    /// var terminal = new EcrTerminal("192.168.1.50", serialNumber: "TW1234567890");
    ///
    /// switch (await terminal.SaleAsync(1.234m))
    /// {
    ///     case EcrResult.Approved a: Console.WriteLine($"Took {a.Amount}, RRN {a.Rrn}"); break;
    ///     case EcrResult.Declined d: Console.WriteLine($"Refused: {d.Reason}"); break;
    ///     case EcrResult.Failed f:   Console.WriteLine($"No answer: {f.Failure.Message}"); break;
    /// }
    /// </code>
    /// Every call is asynchronous and never blocks the calling thread, so it is
    /// safe to <c>await</c> from a UI thread.
    /// </summary>
    public sealed class EcrTerminal
    {
        private readonly IEcrChannel _channel;
        private readonly string _serialNumber;
        private readonly EcrConfig _config;
        private readonly IEcrLogger _logger;

        /// <param name="channel">
        /// How bytes reach the terminal. A socket over Wi-Fi, a USB cable, or
        /// anything else that can carry one request and bring back one answer —
        /// none of which changes a single line of what is above the channel.
        /// </param>
        /// <param name="serialNumber">
        /// Identifies the terminal in the message. The terminal reports its own
        /// address on screen; this is what the operator registered.
        /// </param>
        public EcrTerminal(
            IEcrChannel channel,
            string serialNumber = "",
            EcrConfig? config = null,
            IEcrLogger? logger = null)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _serialNumber = serialNumber ?? "";
            _config = config ?? new EcrConfig();
            _logger = logger ?? EcrLogger.None;
        }

        /// <summary>
        /// The terminal at an address on the local network. Builds a
        /// <see cref="TcpEcrChannel"/> and hands it to the constructor above.
        /// </summary>
        public EcrTerminal(
            string host,
            string serialNumber = "",
            EcrConfig? config = null,
            IEcrLogger? logger = null)
            : this(
                new TcpEcrChannel(host, (config ?? new EcrConfig()).Port, (config ?? new EcrConfig()).ConnectTimeout),
                serialNumber,
                config,
                logger)
        {
        }

        /// <summary>
        /// Whether the terminal is listening.
        /// <para>
        /// Worth calling before a sale: it turns a wrong address or a terminal on
        /// another network into an immediate answer, rather than a failure a
        /// cardholder waits through. It proves the port is open, not that the
        /// terminal is idle — a terminal already taking a payment answers too.
        /// </para>
        /// </summary>
        /// <seealso cref="ProbeReachabilityAsync"/>
        public async Task<bool> IsReachableAsync(CancellationToken cancellationToken = default) =>
            (await ProbeReachabilityAsync(cancellationToken).ConfigureAwait(false)).Reachable;

        /// <summary>
        /// Asks the channel whether the terminal is there, before sending anything.
        /// <see cref="EcrReachability.Error"/> carries whatever the link reported —
        /// connection refused, a timeout, a cable that is not plugged in — so
        /// callers can show why the till cannot reach the terminal rather than only
        /// that it cannot.
        /// </summary>
        public async Task<EcrReachability> ProbeReachabilityAsync(CancellationToken cancellationToken = default)
        {
            var problem = await _channel.ProbeAsync(_config.ProbeTimeout, cancellationToken).ConfigureAwait(false);
            if (problem != null)
            {
                _logger.Debug($"Probe failed for {_channel.Endpoint} — {problem}");
            }
            return new EcrReachability(
                Reachable: problem == null,
                Host: _channel.Host,
                Port: _channel.Port,
                Error: problem,
                Endpoint: _channel.Endpoint);
        }

        /// <summary>Takes a payment. The cardholder presents their card at the terminal.</summary>
        /// <param name="merchantReference">
        /// The till's own reference for this sale — an order number, a basket id,
        /// whatever the caller's system already uses to name it. Left out, the SDK
        /// generates one and reports it on the result.
        /// </param>
        public Task<EcrResult> SaleAsync(
            decimal amount,
            string merchantReference = "",
            CancellationToken cancellationToken = default) =>
            RunAsync(
                EcrTransactionType.Sale,
                amount: amount,
                merchantReference: merchantReference,
                cancellationToken: cancellationToken);

        /// <summary>
        /// Cancels an earlier transaction in full, by the receipt number printed on
        /// it. No amount: a void returns exactly what the original took.
        /// </summary>
        /// <param name="originalTerminalId">The terminal the original was taken on, when it was not this one.</param>
        /// <param name="merchantReference">
        /// The till's own reference for the void. This names the cancellation, not
        /// the transaction being cancelled — that one is named by <paramref name="receiptNumber"/>.
        /// </param>
        public Task<EcrResult> VoidAsync(
            string receiptNumber,
            string originalTerminalId = "",
            string merchantReference = "",
            CancellationToken cancellationToken = default) =>
            RunAsync(
                EcrTransactionType.Void,
                originalStan: receiptNumber,
                originalTerminalId: originalTerminalId,
                merchantReference: merchantReference,
                cancellationToken: cancellationToken);

        /// <summary>Returns money against an earlier transaction, in full or in part.</summary>
        /// <param name="transactionDate">The day the original was taken, as <c>yyyyMMdd</c>. A receipt number is only unique within a terminal's day.</param>
        /// <param name="merchantReference">The till's own reference for the refund.</param>
        public Task<EcrResult> RefundAsync(
            decimal amount,
            string receiptNumber,
            string transactionDate,
            string originalTerminalId = "",
            string merchantReference = "",
            CancellationToken cancellationToken = default) =>
            RunAsync(
                EcrTransactionType.Refund,
                amount: amount,
                originalStan: receiptNumber,
                originalTerminalId: originalTerminalId,
                originalDate: transactionDate,
                merchantReference: merchantReference,
                cancellationToken: cancellationToken);

        /// <summary>
        /// Asks what became of an earlier transaction, without touching it.
        /// <para>
        /// Nothing is authorised and no card is presented, so this is safe to repeat
        /// and the terminal answers it even while taking a payment — which is when a
        /// till most needs it: after a timeout, the outcome is unknown, and this is
        /// how to find out rather than retry and risk charging twice.
        /// </para>
        /// </summary>
        /// <param name="transactionDate">The day the original was taken, as <c>yyyyMMdd</c>.</param>
        /// <param name="originalTerminalId">The terminal it was taken on, when not this one.</param>
        /// <param name="merchantReference">The till's own reference for the inquiry itself. Not a way to look a transaction up.</param>
        public async Task<EcrInquiry> InquireAsync(
            string receiptNumber,
            string transactionDate,
            string originalTerminalId = "",
            string merchantReference = "",
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(receiptNumber))
            {
                throw new ArgumentException("An inquiry needs a receipt number", nameof(receiptNumber));
            }

            var message = Build(
                EcrTransactionType.Inquiry,
                originalStan: receiptNumber,
                originalTerminalId: originalTerminalId,
                originalDate: transactionDate,
                merchantReference: merchantReference);

            var answer = await ExchangeAsync(message, cancellationToken).ConfigureAwait(false);
            return answer.Failure != null
                ? new EcrInquiry.Failed(message.MerchantReference, answer.Failure)
                : answer.Json!.ToInquiry(message.MerchantReference, _config.MinorUnitDigits);
        }

        /// <summary>
        /// Asks what became of the transaction this till named
        /// <paramref name="originalReference"/>, without touching it.
        /// <para>
        /// This is the answer to an outcome you never received. A receipt number
        /// arrives <em>in</em> the terminal's answer, so after a
        /// <see cref="Failure.Timeout"/>, a <see cref="Failure.ConnectionLost"/> or a
        /// <see cref="Failure.Unauthenticated"/> there is no receipt number to
        /// quote — but the reference you sent the request with is still yours.
        /// </para>
        /// <code>
        /// var result = await terminal.SaleAsync(amount, merchantReference: order.Number);
        /// if (result is EcrResult.Failed)
        /// {
        ///     // Never retry here — find out first.
        ///     switch (await terminal.InquireByReferenceAsync(order.Number))
        ///     {
        ///         case EcrInquiry.Found f:    Reconcile(f.Transaction); break;
        ///         case EcrInquiry.NotFound:   /* nothing was taken; now it is safe */ break;
        ///         case EcrInquiry.Failed:     /* still unknown; ask again later */ break;
        ///     }
        /// }
        /// </code>
        /// </summary>
        /// <param name="originalReference">The reference the earlier transaction was sent with — not a reference for this inquiry.</param>
        /// <param name="transactionDate">The day the original was taken, as <c>yyyyMMdd</c>. Optional: a reference is not scoped to a day the way a receipt number is.</param>
        /// <param name="originalTerminalId">The terminal it was taken on, when not this one.</param>
        /// <exception cref="ArgumentException"><paramref name="originalReference"/> is blank.</exception>
        public async Task<EcrInquiry> InquireByReferenceAsync(
            string originalReference,
            string transactionDate = "",
            string originalTerminalId = "",
            string merchantReference = "",
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(originalReference))
            {
                throw new ArgumentException(
                    "An inquiry by reference needs the original's reference", nameof(originalReference));
            }

            var message = Build(
                EcrTransactionType.Inquiry,
                originalTerminalId: originalTerminalId,
                originalDate: transactionDate,
                originalReference: originalReference,
                merchantReference: merchantReference);

            var answer = await ExchangeAsync(message, cancellationToken).ConfigureAwait(false);
            return answer.Failure != null
                ? new EcrInquiry.Failed(message.MerchantReference, answer.Failure)
                : answer.Json!.ToInquiry(message.MerchantReference, _config.MinorUnitDigits);
        }

        /// <summary>
        /// Fetches an earlier transaction's e-receipt.
        /// <para>
        /// Answers with a URL to put in a QR code: the customer scans it and reads
        /// the receipt on their own phone, so a till with no printer can still hand
        /// one over. Non-financial and reprintable — ask as often as you like.
        /// </para>
        /// </summary>
        public async Task<EcrReceipt> ReceiptAsync(
            string receiptNumber,
            string transactionDate,
            string originalTerminalId = "",
            string merchantReference = "",
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(receiptNumber))
            {
                throw new ArgumentException("A receipt needs a receipt number", nameof(receiptNumber));
            }

            var message = Build(
                EcrTransactionType.Receipt,
                originalStan: receiptNumber,
                originalTerminalId: originalTerminalId,
                originalDate: transactionDate,
                merchantReference: merchantReference);

            var answer = await ExchangeAsync(message, cancellationToken).ConfigureAwait(false);
            return answer.Failure != null
                ? new EcrReceipt.Failed(message.MerchantReference, answer.Failure)
                : answer.Json!.ToReceipt(message.MerchantReference);
        }

        /// <summary>
        /// Asks the terminal to put its receipt away and go back to its idle screen.
        /// <para>
        /// Call from the till’s Close receipt action when the cashier is finished
        /// with the result — not as a side effect of dismissing the dialog.
        /// </para>
        /// A terminal too old to know the request answers
        /// <see cref="EcrReceiptClosed.Refused"/>. Treat that as "cannot be asked"
        /// and carry on; the receipt stays up and somebody presses back, as it did
        /// before.
        /// </summary>
        /// <param name="merchantReference">The till's own reference for the request.</param>
        public async Task<EcrReceiptClosed> CloseReceiptAsync(
            string merchantReference = "",
            CancellationToken cancellationToken = default)
        {
            var message = Build(
                EcrTransactionType.CloseReceipt,
                merchantReference: merchantReference);

            var answer = await ExchangeAsync(message, cancellationToken).ConfigureAwait(false);
            return answer.Failure != null
                ? new EcrReceiptClosed.Failed(message.MerchantReference, answer.Failure)
                : answer.Json!.ToReceiptClosed(message.MerchantReference);
        }

        /// <summary>
        /// Runs any operation that moves money. The typed methods above are the
        /// usual way in; this is for a caller driving the type from its own UI.
        /// <see cref="EcrTransactionType.Inquiry"/> is not accepted here — it
        /// reports on a transaction rather than performing one, and so answers with
        /// <see cref="EcrInquiry"/> through <see cref="InquireAsync"/>.
        /// </summary>
        public async Task<EcrResult> RunAsync(
            EcrTransactionType type,
            decimal? amount = null,
            string originalStan = "",
            string originalTerminalId = "",
            string originalDate = "",
            string merchantReference = "",
            CancellationToken cancellationToken = default)
        {
            if (!type.MovesMoney())
            {
                throw new ArgumentException($"{type.DisplayName()} is run with inquire()", nameof(type));
            }
            if (type.RequiresAmount() && amount == null)
            {
                throw new ArgumentException($"{type.DisplayName()} needs an amount", nameof(amount));
            }
            if (type.RequiresOriginalStan() && string.IsNullOrWhiteSpace(originalStan))
            {
                throw new ArgumentException(
                    $"{type.DisplayName()} needs the original's receipt number", nameof(originalStan));
            }

            var message = Build(
                type: type,
                amount: amount,
                originalStan: originalStan,
                originalTerminalId: originalTerminalId,
                originalDate: originalDate,
                merchantReference: merchantReference);

            var answer = await ExchangeAsync(message, cancellationToken).ConfigureAwait(false);
            return answer.Failure != null
                ? await LostAsync(message.MerchantReference, answer.Failure, cancellationToken).ConfigureAwait(false)
                : answer.Json!.ToEcrResult(message.MerchantReference, _config.MinorUnitDigits);
        }

        /// <summary>
        /// Turns a lost exchange into a result, asking the terminal what became of
        /// the transaction where that can be answered.
        /// <para>
        /// The request went out and no answer came back, which is the one situation
        /// a till cannot resolve on its own — and the thing it reaches for instead
        /// is sending the sale again, which charges the cardholder twice. So the
        /// question is asked here, once, on a fresh connection.
        /// </para>
        /// The finding is attached rather than substituted: a
        /// <see cref="EcrResult.Failed"/> whose <see cref="EcrResult.Failed.Recovered"/>
        /// is <see cref="EcrInquiry.Found"/> still says the exchange failed, because
        /// it did. What changed is that the outcome is no longer unknown.
        /// </summary>
        private async Task<EcrResult> LostAsync(string reference, Failure failure, CancellationToken cancellationToken)
        {
            var plain = new EcrResult.Failed(reference, failure);

            if (!_config.AutoInquireOnFailure) return plain;
            // Nothing was sent, so there is nothing to ask about — and asking would
            // only make the caller wait through a second connection that will fail
            // for the same reason the first one did.
            if (!failure.OutcomeUnknown) return plain;
            if (reference.Length == 0) return plain;

            _logger.Debug($"Lost the answer for {reference} — asking what became of it");

            // An inquiry reads and nothing more, so this cannot itself move money
            // however it goes. A failure here leaves the outcome exactly as unknown
            // as it already was.
            EcrInquiry? found;
            try
            {
                found = await InquireByReferenceAsync(reference, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                found = null;
            }

            return plain with { Recovered = found };
        }

        private EcrMessage Build(
            EcrTransactionType type,
            decimal? amount = null,
            string originalStan = "",
            string originalTerminalId = "",
            string originalDate = "",
            string originalReference = "",
            string merchantReference = "")
        {
            var message = EcrMessage.Build(
                type: type,
                config: _config,
                terminalSerial: _serialNumber,
                amount: amount,
                originalStan: originalStan,
                originalTerminalId: originalTerminalId,
                originalDate: originalDate,
                originalReference: originalReference,
                merchantReference: merchantReference);

            _logger.Debug($"Sending {type.MessageType()} {message.MerchantReference} to {_channel.Endpoint}");
            _logger.Debug(EcrJson.Write(message.Json));
            LogRequestSigning(message);
            return message;
        }

        private void LogRequestSigning(EcrMessage message)
        {
            if (!_config.SignsMessages)
            {
                return;
            }
            var hash = message.Json.Str(SecureHash.Field);
            if (hash.Length == 0) return;
            EcrLanLog.Signing(
                _logger,
                SecureHash.KeyLabel,
                _config.SecureHashKey,
                SecureHash.Compose(message.Json),
                hash,
                message.Nonce);
        }

        /// <summary>One request, one answer — or why there was none.</summary>
        private sealed class Answer
        {
            public JsonObject? Json { get; init; }

            public Failure? Failure { get; init; }
        }

        private async Task<Answer> ExchangeAsync(EcrMessage message, CancellationToken cancellationToken)
        {
            var body = Encoding.UTF8.GetBytes(EcrJson.Write(message.Json));

            try
            {
                var reply = await _channel.ExchangeAsync(body, _config.ResponseTimeout, cancellationToken).ConfigureAwait(false);
                var answer = EcrMessageCodec.Parse(reply);
                _logger.Debug($"Received {EcrJson.Write(answer)}");

                var rejection = Untrustworthy(answer, message);
                if (rejection != null)
                {
                    _logger.Debug($"Rejected the answer: {rejection.Message}");
                    return new Answer { Failure = rejection };
                }
                return new Answer { Json = answer };
            }
            catch (EcrChannelTimeout)
            {
                // Not a refusal: the terminal may have completed the payment and
                // been unable to say so.
                return new Answer
                {
                    Failure = new Failure.Timeout(
                        $"The terminal did not answer within {FormatDuration(_config.ResponseTimeout)}. " +
                        "The transaction may still have completed — inquire before retrying."),
                };
            }
            catch (IOException e)
            {
                return new Answer { Failure = ToFailure(e, _channel.Endpoint) };
            }
        }

        /// <summary>
        /// Why this answer must not be believed, or null when it can be.
        /// <para>
        /// Checking the answer matters as much as signing the request. An answer is
        /// what decides whether the till hands over the goods, so anything on the
        /// network that replies on the terminal's port before the terminal does
        /// could otherwise claim <c>approved</c> for a payment that never happened —
        /// without ever touching the payment backend.
        /// </para>
        /// The outcome is <see cref="Failure.Unauthenticated"/> rather than a
        /// decline, because a rejected answer says nothing about what the terminal
        /// did. The transaction may well have completed; it simply cannot be
        /// confirmed from here.
        /// </summary>
        private Failure? Untrustworthy(JsonObject answer, EcrMessage sent)
        {
            if (!_config.SignsMessages) return null;

            var presented = answer.Str(SecureHash.Field);
            var verified = SecureHash.Verify(answer, _config.SecureHashKey);
            if (_config.SecureHashKey.Length != 0 && presented.Length != 0)
            {
                EcrLanLog.VerifyResponse(
                    _logger,
                    SecureHash.KeyLabel,
                    _config.SecureHashKey,
                    SecureHash.Compose(answer),
                    presented,
                    verified);
            }

            if (!verified)
            {
                return new Failure.Unauthenticated("The answer was not signed with this terminal's key.");
            }

            // Binds the answer to this request. A correctly signed answer to an
            // earlier transaction, replayed onto this connection, fails here.
            var echoed = answer.Str("nonce");
            if (echoed != sent.Nonce)
            {
                return new Failure.Unauthenticated(
                    "The answer belongs to a different request. The transaction " +
                    "may still have completed — inquire before retrying.");
            }

            return null;
        }

        private static Failure ToFailure(IOException e, string endpoint)
        {
            var reason = string.IsNullOrEmpty(e.Message) ? e.GetType().Name : e.Message;
            if (reason.Contains("not valid JSON") || reason.Contains("empty message"))
            {
                return new Failure.Malformed(reason);
            }
            if (reason.Contains("closed the connection"))
            {
                return new Failure.ConnectionLost(reason);
            }
            return new Failure.Unreachable($"{reason} ({endpoint})");
        }

        internal static string FormatDuration(TimeSpan duration)
        {
            var total = (long)Math.Round(duration.TotalSeconds);
            if (total < 60) return $"{total}s";
            var minutes = total / 60;
            var seconds = total % 60;
            return seconds == 0 ? $"{minutes}m" : $"{minutes}m {seconds}s";
        }
    }

    /// <summary>Result of <see cref="EcrTerminal.ProbeReachabilityAsync"/>.</summary>
    /// <param name="Endpoint">
    /// What to show an operator: an address and port over Wi-Fi, the cable
    /// otherwise. A link with no address fills <paramref name="Host"/> and
    /// <paramref name="Port"/> with what it has and puts the readable form here.
    /// </param>
    public sealed record EcrReachability(
        bool Reachable,
        string Host,
        int Port,
        string? Error = null,
        string? Endpoint = null)
    {
        public string Endpoint { get; init; } = Endpoint ?? (Port > 0 ? $"{Host}:{Port}" : Host);
    }
}
