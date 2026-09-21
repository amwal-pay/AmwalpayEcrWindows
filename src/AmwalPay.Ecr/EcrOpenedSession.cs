using System;
using System.Threading;
using System.Threading.Tasks;

namespace AmwalPay.Ecr
{
    /// <summary>
    /// One opened transport for a validated <see cref="EcrSessionPlan"/>.
    /// <para>
    /// Apps open this through <see cref="EcrSessions.Open"/> so LAN / USB / Web
    /// Service dispatch lives in one place — sale, inquiry, and recovery then share
    /// the same path.
    /// </para>
    /// </summary>
    public sealed class EcrOpenedSession
    {
        private readonly EcrTerminal? _local;
        private readonly EcrWebServiceTerminal? _web;

        internal EcrOpenedSession(EcrSessionPlan plan, EcrTerminal? local, EcrWebServiceTerminal? web)
        {
            Plan = plan;
            _local = local;
            _web = web;
        }

        public EcrSessionPlan Plan { get; }

        public bool UsesWebService => _web != null;

        public bool UsesLocalTerminal => _local != null;

        /// <summary>Receipt URLs exist only on the local (LAN / USB) protocol.</summary>
        public bool SupportsReceipt => _local != null;

        /// <summary>
        /// Reachability for local links, or <c>null</c> when Web Service — Hub HTTPS
        /// has no pre-flight probe equivalent.
        /// </summary>
        public async Task<EcrReachability?> ProbeReachabilityAsync(CancellationToken cancellationToken = default) =>
            _local == null ? null : await _local.ProbeReachabilityAsync(cancellationToken).ConfigureAwait(false);

        public Task<EcrResult> SaleAsync(decimal amount, string merchantReference = "", CancellationToken cancellationToken = default)
        {
            if (_local != null) return _local.SaleAsync(amount, merchantReference, cancellationToken);
            if (_web != null) return _web.SaleAsync(amount, merchantReference, cancellationToken);
            throw NoTransport();
        }

        public Task<EcrResult> VoidAsync(
            string receiptNumber,
            string originalTerminalId = "",
            string merchantReference = "",
            CancellationToken cancellationToken = default)
        {
            if (_local != null) return _local.VoidAsync(receiptNumber, originalTerminalId, merchantReference, cancellationToken);
            if (_web != null) return _web.VoidAsync(receiptNumber, merchantReference, cancellationToken);
            throw NoTransport();
        }

        public Task<EcrResult> RefundAsync(
            decimal amount,
            string receiptNumber,
            string transactionDate,
            string originalTerminalId = "",
            string merchantReference = "",
            CancellationToken cancellationToken = default)
        {
            if (_local != null)
            {
                return _local.RefundAsync(amount, receiptNumber, transactionDate, originalTerminalId, merchantReference, cancellationToken);
            }
            if (_web != null)
            {
                return _web.RefundAsync(amount, receiptNumber, transactionDate, merchantReference, cancellationToken);
            }
            throw NoTransport();
        }

        public Task<EcrInquiry> InquireAsync(
            string receiptNumber,
            string transactionDate,
            string originalTerminalId = "",
            CancellationToken cancellationToken = default)
        {
            if (_local != null) return _local.InquireAsync(receiptNumber, transactionDate, originalTerminalId, cancellationToken: cancellationToken);
            if (_web != null) return _web.InquireAsync(receiptNumber, transactionDate, cancellationToken: cancellationToken);
            throw NoTransport();
        }

        public Task<EcrInquiry> InquireByReferenceAsync(
            string originalReference,
            string transactionDate = "",
            string originalTerminalId = "",
            string merchantReference = "",
            CancellationToken cancellationToken = default)
        {
            if (_local != null)
            {
                return _local.InquireByReferenceAsync(originalReference, transactionDate, originalTerminalId, merchantReference, cancellationToken);
            }
            if (_web != null)
            {
                return _web.InquireByReferenceAsync(originalReference, transactionDate, merchantReference, cancellationToken);
            }
            throw NoTransport();
        }

        public Task<EcrReceipt> ReceiptAsync(
            string receiptNumber,
            string transactionDate,
            string originalTerminalId = "",
            CancellationToken cancellationToken = default)
        {
            if (_local == null)
            {
                return Task.FromResult<EcrReceipt>(new EcrReceipt.Unavailable(
                    MerchantReference: "",
                    Reason: "Receipt fetch is only supported over Wi‑Fi and USB cable ECR",
                    Raw: ""));
            }
            return _local.ReceiptAsync(receiptNumber, transactionDate, originalTerminalId, cancellationToken: cancellationToken);
        }

        private static InvalidOperationException NoTransport() => new InvalidOperationException("Session has no transport");
    }
}
