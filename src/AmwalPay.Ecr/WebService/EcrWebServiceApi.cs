using System.Threading;
using System.Threading.Tasks;

namespace AmwalPay.Ecr.WebService
{
    /// <summary>
    /// Web Service ECR client — one method per REST operation. Hub URLs and
    /// operation paths are resolved inside the SDK from <see cref="EcrConfig.Environment"/>.
    /// </summary>
    internal interface IEcrWebServiceApi
    {
        EcrWebServiceRoutes Routes { get; }

        Task<EcrWireResponse> SaleAsync(EcrWebServiceSaleRequest request, CancellationToken cancellationToken = default);

        Task<EcrWireResponse> VoidAsync(EcrWebServiceVoidRequest request, CancellationToken cancellationToken = default);

        Task<EcrWireResponse> RefundAsync(EcrWebServiceRefundRequest request, CancellationToken cancellationToken = default);

        Task<EcrWireResponse> TransactionStatusAsync(EcrWebServiceTransactionStatusRequest request, CancellationToken cancellationToken = default);

        Task<EcrWireResponse> CloseReceiptAsync(EcrWebServiceCloseReceiptRequest request, CancellationToken cancellationToken = default);

        Task<EcrWireResponse> SettleAsync(EcrWebServiceSettleRequest request, CancellationToken cancellationToken = default);

        Task<EcrWireResponse> CheckReachabilityAsync(EcrWebServiceCheckReachabilityRequest request, CancellationToken cancellationToken = default);
    }
}
