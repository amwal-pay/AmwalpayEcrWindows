using AmwalPay.Ecr;
using AmwalPay.Ecr.Simulator.Data;

namespace AmwalPay.Ecr.Simulator.Ui;

public abstract record TransactionOutcome
{
    public sealed record InquiryDone(EcrInquiry Inquiry) : TransactionOutcome;
    public sealed record ResultDone(EcrResult Result) : TransactionOutcome;
    public sealed record Error(string Message) : TransactionOutcome;
}

/// <summary>Shared session open + operation dispatch for WinForms and console hosts.</summary>
public static class TransactionRunner
{
    public static async Task<TransactionOutcome> ExecuteAsync(
        TerminalSessions sessions,
        Terminal terminal,
        SelectedTerminalConfig active,
        TransactionRequest request,
        Action<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        if (!active.IsReady)
        {
            return new TransactionOutcome.Error(string.Join(Environment.NewLine, active.Issues));
        }

        status?.Invoke("Checking terminal…");
        EcrOpenedSession session;
        try
        {
            session = sessions.Open(terminal, active);
        }
        catch (Exception ex)
        {
            return new TransactionOutcome.Error(ex.Message);
        }

        var probe = await session.ProbeReachabilityAsync(cancellationToken).ConfigureAwait(false);
        if (probe is { Reachable: false })
        {
            return new TransactionOutcome.Error(
                $"Terminal not reachable at {probe.Endpoint}"
                + (string.IsNullOrEmpty(probe.Error) ? "" : $" — {probe.Error}"));
        }

        status?.Invoke("In progress — wait for the terminal…");
        try
        {
            if (request.Type == EcrTransactionType.Inquiry)
            {
                var inquiry = request.LooksUpByReference
                    ? await session.InquireByReferenceAsync(
                        request.OriginalReference,
                        request.TransactionDate,
                        request.OriginalTerminalId,
                        request.MerchantReference,
                        cancellationToken).ConfigureAwait(false)
                    : await session.InquireAsync(
                        request.ReceiptNumber,
                        request.TransactionDate,
                        request.OriginalTerminalId,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                return new TransactionOutcome.InquiryDone(inquiry);
            }

            var result = request.Type switch
            {
                EcrTransactionType.Sale => await session.SaleAsync(
                    request.Amount!.Value,
                    request.MerchantReference,
                    cancellationToken).ConfigureAwait(false),
                EcrTransactionType.Void => await session.VoidAsync(
                    request.ReceiptNumber,
                    request.OriginalTerminalId,
                    request.MerchantReference,
                    cancellationToken).ConfigureAwait(false),
                EcrTransactionType.Refund => await session.RefundAsync(
                    request.Amount!.Value,
                    request.ReceiptNumber,
                    request.TransactionDate,
                    request.OriginalTerminalId,
                    request.MerchantReference,
                    cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException($"Unsupported type {request.Type}"),
            };
            return new TransactionOutcome.ResultDone(result);
        }
        catch (Exception ex)
        {
            return new TransactionOutcome.Error(ex.Message);
        }
    }
}
