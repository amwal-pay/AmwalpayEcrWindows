#if AMWAL_ECR_WINFORMS
using System.Text;
using AmwalPay.Ecr;

namespace AmwalPay.Ecr.Simulator.Ui;

/// <summary>Shows Approved / Declined / Failed and Inquiry Found / NotFound / Failed.</summary>
public sealed class ResultForm : Form
{
    private ResultForm(string headline, string body, string? raw, Color accent)
    {
        Text = "Transaction result";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(520, 420);
        Font = new Font("Segoe UI", 9f);

        var title = new Label
        {
            Text = headline,
            Dock = DockStyle.Top,
            Height = 40,
            Font = new Font("Segoe UI Semibold", 14f),
            ForeColor = accent,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 8, 12, 0),
        };

        var bodyBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            Text = body,
            BackColor = SystemColors.Window,
        };

        var rawBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 8.5f),
            Dock = DockStyle.Fill,
            Text = raw ?? "",
            Visible = !string.IsNullOrWhiteSpace(raw),
        };

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var summaryTab = new TabPage("Summary");
        summaryTab.Controls.Add(bodyBox);
        tabs.TabPages.Add(summaryTab);
        if (rawBox.Visible)
        {
            var rawTab = new TabPage("Raw / wire JSON");
            rawTab.Controls.Add(rawBox);
            tabs.TabPages.Add(rawTab);
        }

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 48,
            Padding = new Padding(8),
        };
        var ok = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            AutoSize = true,
        };
        var copy = new Button
        {
            Text = "Copy raw",
            AutoSize = true,
            Enabled = rawBox.Visible,
        };
        copy.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(raw))
            {
                Clipboard.SetText(raw);
            }
        };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(copy);

        Controls.Add(tabs);
        Controls.Add(buttons);
        Controls.Add(title);
        AcceptButton = ok;
    }

    public static void ShowResult(IWin32Window owner, EcrResult result, EcrTransactionType type)
    {
        switch (result)
        {
            case EcrResult.Approved a:
                var headline = a.PartialApproval ? "Partially approved" : "Approved";
                var sb = new StringBuilder();
                sb.AppendLine($"Amount: {a.Amount}");
                if (a.PartialApproval)
                {
                    sb.AppendLine($"Only {a.Amount} of {a.RequestedAmount} was approved. Collect the difference by another means.");
                }
                if (type != EcrTransactionType.Void && a.AuthCode.Length != 0)
                {
                    sb.AppendLine($"Auth code: {a.AuthCode}");
                }
                if (a.Rrn.Length != 0) sb.AppendLine($"RRN: {a.Rrn}");
                if (a.MaskedPan.Length != 0) sb.AppendLine($"Card: {a.MaskedPan}");
                sb.AppendLine($"Merchant reference: {a.MerchantReference}");
                sb.AppendLine($"Response code: {a.ResponseCode}");
                using (var form = new ResultForm(headline, sb.ToString(), a.Raw, Color.FromArgb(0, 128, 0)))
                {
                    form.ShowDialog(owner);
                }
                break;

            case EcrResult.Declined d:
                using (var form = new ResultForm(
                           "Declined",
                           $"{d.Reason}\n\nResponse code: {d.ResponseCode}\nMerchant reference: {d.MerchantReference}",
                           d.Raw,
                           Color.Firebrick))
                {
                    form.ShowDialog(owner);
                }
                break;

            case EcrResult.Failed f:
                var failBody = new StringBuilder(f.Failure.Message);
                failBody.AppendLine();
                failBody.AppendLine();
                failBody.AppendLine($"Merchant reference: {f.MerchantReference}");
                if (f.Recovered is EcrInquiry.Found found)
                {
                    failBody.AppendLine();
                    failBody.AppendLine("Auto-inquiry recovered the transaction:");
                    failBody.AppendLine(ResultText.FormatTransaction(found.Transaction));
                }
                using (var form = new ResultForm("Failed", failBody.ToString(), null, Color.DarkOrange))
                {
                    form.ShowDialog(owner);
                }
                break;
        }
    }

    public static void ShowInquiry(IWin32Window owner, EcrInquiry inquiry)
    {
        switch (inquiry)
        {
            case EcrInquiry.Found f:
                using (var form = new ResultForm(
                           $"Found · {f.Transaction.Status}",
                           ResultText.FormatTransaction(f.Transaction) + $"\nMerchant reference: {f.MerchantReference}",
                           f.Raw,
                           Color.FromArgb(0, 128, 0)))
                {
                    form.ShowDialog(owner);
                }
                break;

            case EcrInquiry.NotFound n:
                using (var form = new ResultForm(
                           "Not found",
                           $"{n.Reason}\n\nMerchant reference: {n.MerchantReference}",
                           n.Raw,
                           Color.SteelBlue))
                {
                    form.ShowDialog(owner);
                }
                break;

            case EcrInquiry.Failed fail:
                using (var form = new ResultForm(
                           "Inquiry failed",
                           $"{fail.Failure.Message}\n\nMerchant reference: {fail.MerchantReference}",
                           null,
                           Color.DarkOrange))
                {
                    form.ShowDialog(owner);
                }
                break;
        }
    }
}
#endif
