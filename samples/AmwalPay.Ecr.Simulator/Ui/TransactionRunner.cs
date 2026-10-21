using AmwalPay.Ecr;
using AmwalPay.Ecr.Simulator.Data;

namespace AmwalPay.Ecr.Simulator.Ui;

public abstract record TransactionOutcome
{
    public sealed record InquiryDone(EcrInquiry Inquiry) : TransactionOutcome;
    public sealed record ResultDone(EcrResult Result) : TransactionOutcome;

    /// <summary>
    /// Not completed. <paramref name="InquirableReference"/> is set when the request
    /// went out and no answer came back: the transaction may have completed, so the
    /// only safe next step is an inquiry by that reference — never a second sale.
    /// </summary>
    public sealed record Error(string Message, string? InquirableReference = null) : TransactionOutcome;
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

        // Made here rather than left to the SDK so it is known even if the wait is
        // cancelled: a cancelled call returns no result to read it from.
        var reference = request.Type.MovesMoney() && string.IsNullOrWhiteSpace(request.MerchantReference)
            ? Guid.NewGuid().ToString("N")[..12].ToUpperInvariant()
            : request.MerchantReference;

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
                // A failed lookup is not a finding: the outcome is still unknown.
                return inquiry is EcrInquiry.Failed failedInquiry
                    ? new TransactionOutcome.Error(
                        failedInquiry.Failure.Message,
                        request.LooksUpByReference ? request.OriginalReference : null)
                    : new TransactionOutcome.InquiryDone(inquiry);
            }

            var result = request.Type switch
            {
                EcrTransactionType.Sale => await session.SaleAsync(
                    request.Amount!.Value,
                    reference,
                    cancellationToken).ConfigureAwait(false),
                EcrTransactionType.Void => await session.VoidAsync(
                    request.ReceiptNumber,
                    request.OriginalTerminalId,
                    reference,
                    cancellationToken).ConfigureAwait(false),
                EcrTransactionType.Refund => await session.RefundAsync(
                    request.Amount!.Value,
                    request.ReceiptNumber,
                    request.TransactionDate,
                    request.OriginalTerminalId,
                    reference,
                    cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException($"Unsupported type {request.Type}"),
            };
            return Classify(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stopping the wait does not stop the terminal: if the request already
            // reached it, a sale can still complete. Never reported as "cancelled".
            return request.Type.MovesMoney()
                ? new TransactionOutcome.Error(
                    "You stopped waiting for the terminal. This does not cancel the transaction: " +
                    "if the request had already reached the terminal, it may still complete.",
                    reference)
                : new TransactionOutcome.Error("Operation cancelled");
        }
        catch (Exception ex)
        {
            return new TransactionOutcome.Error(ex.Message);
        }
    }

    /// <summary>
    /// Reads what the SDK returned the way the Android ECR-simulator does: an
    /// unknown outcome is "Not completed", not a decline, and an auto-recovered
    /// transaction is shown as the inquiry it is.
    /// </summary>
    public static TransactionOutcome Classify(EcrResult result)
    {
        switch (result)
        {
            case EcrResult.Failed { Recovered: EcrInquiry.Found found }:
                return new TransactionOutcome.InquiryDone(found);

            case EcrResult.Failed failed:
                return new TransactionOutcome.Error(
                    failed.Failure.Message,
                    failed.Failure.OutcomeUnknown && failed.MerchantReference.Length != 0
                        ? failed.MerchantReference
                        : null);

            // Web Service timeouts / lost answers: the request may have reached the
            // Hub, so this is not a refusal and must not be retried as a new sale.
            case EcrResult.Declined d when
                d.NextStep == NextStep.InquireByMerchantReference ||
                string.Equals(d.ResponseCode, "NO_RESPONSE", StringComparison.OrdinalIgnoreCase):
                return new TransactionOutcome.Error(
                    d.Reason,
                    d.MerchantReference.Length != 0 ? d.MerchantReference : null);

            default:
                return new TransactionOutcome.ResultDone(result);
        }
    }

    /// <summary>
    /// Whether the selected terminal can be asked to put its receipt away:
    /// Wi‑Fi or Web Service (same explicit action as Android's Close receipt).
    /// </summary>
    public static bool CanCloseReceipt(Terminal terminal) =>
        terminal.Mode.IsIpBased() || terminal.Mode == EcrMode.WebService;

    /// <summary>
    /// Tells the terminal the cashier has finished. Wi‑Fi:  <c>CLOSE_RECEIPT</c>.
    /// Web Service: <c>/Ecr/closeReceipt</c> with the sale / void / refund reference
    /// as <c>originalMerchantReference</c>. Not sent on dialog dismiss. Never throws.
    /// </summary>
    public static async Task<string> CloseReceiptAsync(
        TerminalSessions sessions,
        Terminal terminal,
        SelectedTerminalConfig active,
        string originalMerchantReference,
        CancellationToken cancellationToken = default)
    {
        if (!CanCloseReceipt(terminal)) return "Close receipt is not available for this terminal mode";
        if (!active.IsReady) return string.Join(Environment.NewLine, active.Issues);
        if (active.UsesWebService && string.IsNullOrWhiteSpace(originalMerchantReference))
        {
            return "Close receipt needs the sale / void / refund reference";
        }

        try
        {
            var closed = await sessions.Open(terminal, active).CloseReceiptAsync(
                originalMerchantReference: originalMerchantReference,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return closed switch
            {
                EcrReceiptClosed.Idle => "Terminal is back on its idle screen",
                EcrReceiptClosed.Refused r => $"Terminal refused: {r.Reason}",
                EcrReceiptClosed.Failed f => $"Could not reach the terminal: {f.Failure.Message}",
                _ => "Could not close the receipt",
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"Could not close the receipt: {ex.Message}";
        }
    }

    /// <summary>The configured response budget in whole seconds — 130 on Web Service.</summary>
    public static int ResponseTimeoutSeconds(SelectedTerminalConfig active) =>
        Math.Max(1, (int)active.EcrConfig.ResponseTimeout.TotalSeconds);
}
