using System.Text;
using AmwalPay.Ecr;

namespace AmwalPay.Ecr.Simulator.Ui;

public static class ResultText
{
    public static string FormatTransaction(EcrTransaction t)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Status: {t.Status}");
        sb.AppendLine($"Type: {t.Type}");
        sb.AppendLine($"STAN / receipt: {t.Stan}");
        sb.AppendLine($"Amount: {t.Amount} {t.Currency}");
        if (t.PartialApproval) sb.AppendLine($"Authorized: {t.AuthorizedAmount}");
        if (t.Rrn.Length != 0) sb.AppendLine($"RRN: {t.Rrn}");
        if (t.AuthCode.Length != 0) sb.AppendLine($"Auth code: {t.AuthCode}");
        if (t.MaskedPan.Length != 0) sb.AppendLine($"Card: {t.MaskedPan}");
        if (t.TransactionTime.Length != 0) sb.AppendLine($"Time: {t.TransactionTime}");
        if (t.TerminalId.Length != 0) sb.AppendLine($"Terminal ID: {t.TerminalId}");
        sb.AppendLine($"Can void: {t.CanVoid} · Can refund: {t.CanRefund}");
        return sb.ToString();
    }

    public static string FormatResult(EcrResult result, EcrTransactionType type)
    {
        switch (result)
        {
            case EcrResult.Approved a:
                var sb = new StringBuilder();
                sb.AppendLine(a.PartialApproval ? "Partially approved" : "Approved");
                sb.AppendLine($"Amount: {a.Amount}");
                if (a.PartialApproval)
                {
                    sb.AppendLine($"Only {a.Amount} of {a.RequestedAmount} was approved.");
                }
                if (type != EcrTransactionType.Void && a.AuthCode.Length != 0)
                {
                    sb.AppendLine($"Auth code: {a.AuthCode}");
                }
                if (a.Rrn.Length != 0) sb.AppendLine($"RRN: {a.Rrn}");
                if (a.MaskedPan.Length != 0) sb.AppendLine($"Card: {a.MaskedPan}");
                sb.AppendLine($"Merchant reference: {a.MerchantReference}");
                sb.AppendLine($"Response code: {a.ResponseCode}");
                if (a.Raw.Length != 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("--- raw ---");
                    sb.AppendLine(a.Raw);
                }
                return sb.ToString();

            case EcrResult.Declined d:
                return $"Declined\n{d.Reason}\nResponse code: {d.ResponseCode}\nMerchant reference: {d.MerchantReference}"
                       + (d.Raw.Length == 0 ? "" : "\n\n--- raw ---\n" + d.Raw);

            case EcrResult.Failed f:
                var fail = new StringBuilder("Failed\n");
                fail.AppendLine(f.Failure.Message);
                fail.AppendLine($"Merchant reference: {f.MerchantReference}");
                if (f.Recovered is EcrInquiry.Found found)
                {
                    fail.AppendLine();
                    fail.AppendLine("Auto-inquiry recovered:");
                    fail.Append(FormatTransaction(found.Transaction));
                    if (found.Raw.Length != 0)
                    {
                        fail.AppendLine("--- raw ---");
                        fail.AppendLine(found.Raw);
                    }
                }
                return fail.ToString();

            default:
                return result.ToString() ?? "";
        }
    }

    public static string FormatInquiry(EcrInquiry inquiry) => inquiry switch
    {
        EcrInquiry.Found f =>
            $"Found · {f.Transaction.Status}\n{FormatTransaction(f.Transaction)}Merchant reference: {f.MerchantReference}"
            + (f.Raw.Length == 0 ? "" : "\n\n--- raw ---\n" + f.Raw),
        EcrInquiry.NotFound n =>
            $"Not found\n{n.Reason}\nMerchant reference: {n.MerchantReference}"
            + (n.Raw.Length == 0 ? "" : "\n\n--- raw ---\n" + n.Raw),
        EcrInquiry.Failed fail =>
            $"Inquiry failed\n{fail.Failure.Message}\nMerchant reference: {fail.MerchantReference}",
        _ => inquiry.ToString() ?? "",
    };
}
