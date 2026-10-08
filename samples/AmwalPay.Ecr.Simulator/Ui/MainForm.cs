#if AMWAL_ECR_WINFORMS
using AmwalPay.Ecr;
using AmwalPay.Ecr.Simulator.Data;

namespace AmwalPay.Ecr.Simulator.Ui;

public sealed class MainForm : Form
{
    private readonly TerminalRepository _repository;
    private readonly EcrSimulatorSettings _settings;
    private readonly TerminalSessions _sessions;

    private readonly ComboBox _terminalCombo = new();
    private readonly ComboBox _typeCombo = new();
    private readonly ComboBox _environmentCombo = new();
    private readonly TextBox _amount = new();
    private readonly TextBox _receipt = new();
    private readonly TextBox _originalReference = new();
    private readonly TextBox _merchantReference = new();
    private readonly TextBox _originalTerminalId = new();
    private readonly DateTimePicker _originalDate = new();
    private readonly CheckBox _lookUpByReference = new();
    private readonly CheckBox _useOtherTerminal = new();
    private readonly Label _configSummary = new();
    private readonly Label _fieldError = new();
    private readonly Label _status = new();
    private readonly Button _submit = new();
    private readonly TextBox _wifiKey = new();
    private readonly TextBox _webKey = new();
    private readonly TextBox _log = new();
    private readonly ListView _terminalsList = new();

    private readonly Label _amountLabel = new();
    private readonly Label _receiptLabel = new();
    private readonly Label _referenceLabel = new();
    private readonly Label _merchantRefLabel = new();
    private readonly Label _dateLabel = new();
    private readonly Label _otherTerminalLabel = new();

    private bool _busy;

    public MainForm(
        TerminalRepository repository,
        EcrSimulatorSettings settings,
        TerminalSessions sessions)
    {
        _repository = repository;
        _settings = settings;
        _sessions = sessions;

        Text = "Amwal ECR Simulator (Windows)";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(960, 680);
        Size = new Size(1100, 760);
        Font = new Font("Segoe UI", 9f);

        _sessions.AttachLogSink(AppendLog);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildTransactionTab());
        tabs.TabPages.Add(BuildTerminalsTab());
        tabs.TabPages.Add(BuildSettingsTab());

        var logPanel = BuildLogPanel();

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 520,
            Panel1MinSize = 280,
            Panel2MinSize = 120,
        };
        split.Panel1.Controls.Add(tabs);
        split.Panel2.Controls.Add(logPanel);

        Controls.Add(split);

        Load += (_, _) =>
        {
            ReloadSettingsFields();
            ReloadTerminals();
            RefreshFormVisibility();
        };
    }

    private TabPage BuildTransactionTab()
    {
        var page = new TabPage("Transaction");
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(12),
            AutoScroll = true,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var row = 0;
        AddLabeled(root, row++, "Terminal", _terminalCombo);
        _terminalCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _terminalCombo.SelectedIndexChanged += (_, _) => OnTerminalSelectionChanged();

        AddLabeled(root, row++, "Operation", _typeCombo);
        _typeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        foreach (var type in EcrTransactionTypes.MenuOptions)
        {
            _typeCombo.Items.Add(new TypeItem(type));
        }
        _typeCombo.SelectedIndex = 0;
        _typeCombo.SelectedIndexChanged += (_, _) => RefreshFormVisibility();

        _configSummary.AutoSize = true;
        _configSummary.MaximumSize = new Size(700, 0);
        _configSummary.ForeColor = Color.DimGray;
        root.Controls.Add(_configSummary, 0, row);
        root.SetColumnSpan(_configSummary, 2);
        row++;

        _amountLabel.Text = "Amount (OMR)";
        AddLabeled(root, row++, _amountLabel, _amount);

        _lookUpByReference.Text = "Look up by merchant reference (not STAN)";
        _lookUpByReference.AutoSize = true;
        _lookUpByReference.CheckedChanged += (_, _) => RefreshFormVisibility();
        root.Controls.Add(_lookUpByReference, 0, row);
        root.SetColumnSpan(_lookUpByReference, 2);
        row++;

        _receiptLabel.Text = "Receipt / STAN";
        AddLabeled(root, row++, _receiptLabel, _receipt);

        _referenceLabel.Text = "Original reference";
        AddLabeled(root, row++, _referenceLabel, _originalReference);

        _dateLabel.Text = "Original date";
        _originalDate.Format = DateTimePickerFormat.Short;
        _originalDate.Value = DateTime.Today;
        AddLabeled(root, row++, _dateLabel, _originalDate);

        _merchantRefLabel.Text = "Merchant reference";
        AddLabeled(root, row++, _merchantRefLabel, _merchantReference);

        _useOtherTerminal.Text = "Original was on another terminal";
        _useOtherTerminal.AutoSize = true;
        _useOtherTerminal.CheckedChanged += (_, _) => RefreshFormVisibility();
        root.Controls.Add(_useOtherTerminal, 0, row);
        root.SetColumnSpan(_useOtherTerminal, 2);
        row++;

        _otherTerminalLabel.Text = "Original terminal ID";
        AddLabeled(root, row++, _otherTerminalLabel, _originalTerminalId);

        _fieldError.ForeColor = Color.Firebrick;
        _fieldError.AutoSize = true;
        _fieldError.MaximumSize = new Size(700, 0);
        root.Controls.Add(_fieldError, 0, row);
        root.SetColumnSpan(_fieldError, 2);
        row++;

        _status.AutoSize = true;
        _status.ForeColor = Color.DimGray;
        root.Controls.Add(_status, 0, row);
        root.SetColumnSpan(_status, 2);
        row++;

        _submit.Text = "Send";
        _submit.AutoSize = true;
        _submit.Click += async (_, _) => await SubmitAsync();
        root.Controls.Add(_submit, 1, row);

        page.Controls.Add(root);
        return page;
    }

    private TabPage BuildTerminalsTab()
    {
        var page = new TabPage("Terminals");
        _terminalsList.Dock = DockStyle.Fill;
        _terminalsList.View = View.Details;
        _terminalsList.FullRowSelect = true;
        _terminalsList.MultiSelect = false;
        _terminalsList.Columns.Add("Name", 160);
        _terminalsList.Columns.Add("Serial", 160);
        _terminalsList.Columns.Add("Mode", 100);
        _terminalsList.Columns.Add("Connection", 280);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            Padding = new Padding(8),
        };
        var add = new Button { Text = "Add", AutoSize = true };
        var edit = new Button { Text = "Edit", AutoSize = true };
        var delete = new Button { Text = "Delete", AutoSize = true };
        add.Click += (_, _) => EditTerminal(null);
        edit.Click += (_, _) =>
        {
            if (SelectedListTerminal() is { } t) EditTerminal(t);
        };
        delete.Click += (_, _) =>
        {
            if (SelectedListTerminal() is not { } t) return;
            if (MessageBox.Show(this, $"Delete {t}?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                != DialogResult.Yes)
            {
                return;
            }
            _repository.Delete(t);
            ReloadTerminals();
        };
        buttons.Controls.Add(add);
        buttons.Controls.Add(edit);
        buttons.Controls.Add(delete);

        page.Controls.Add(_terminalsList);
        page.Controls.Add(buttons);
        return page;
    }

    private TabPage BuildSettingsTab()
    {
        var page = new TabPage("Settings");
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(12),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _environmentCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        foreach (EcrEnvironment env in Enum.GetValues<EcrEnvironment>())
        {
            _environmentCombo.Items.Add(env);
        }
        AddLabeled(root, 0, "Environment", _environmentCombo);

        _wifiKey.UseSystemPasswordChar = true;
        AddLabeled(root, 1, "Wi‑Fi / LAN secure hash key", _wifiKey);

        _webKey.UseSystemPasswordChar = true;
        AddLabeled(root, 2, "Web Service secure hash key", _webKey);

        var showKeys = new CheckBox { Text = "Show keys", AutoSize = true };
        showKeys.CheckedChanged += (_, _) =>
        {
            _wifiKey.UseSystemPasswordChar = !showKeys.Checked;
            _webKey.UseSystemPasswordChar = !showKeys.Checked;
        };
        root.Controls.Add(showKeys, 1, 3);

        var save = new Button { Text = "Save settings", AutoSize = true };
        save.Click += (_, _) =>
        {
            _settings.Environment = (EcrEnvironment)(_environmentCombo.SelectedItem ?? EcrEnvironment.SIT);
            _settings.WifiSecureHashKey = _wifiKey.Text;
            _settings.WebServiceSecureHashKey = _webKey.Text;
            OnTerminalSelectionChanged();
            AppendLog("Settings saved.");
            MessageBox.Show(this, "Settings saved.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        root.Controls.Add(save, 1, 4);

        var pathHint = new Label
        {
            AutoSize = true,
            ForeColor = Color.DimGray,
            MaximumSize = new Size(700, 0),
            Text = "Terminals and keys are stored in %LocalAppData%\\AmwalPay.Ecr.Simulator\\settings.json",
        };
        root.Controls.Add(pathHint, 0, 5);
        root.SetColumnSpan(pathHint, 2);

        page.Controls.Add(root);
        return page;
    }

    private Control BuildLogPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        var header = new Label
        {
            Text = "SDK log (EcrLogger → Debug + this panel)",
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI Semibold", 9f),
        };
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Both;
        _log.WordWrap = false;
        _log.Dock = DockStyle.Fill;
        _log.Font = new Font("Consolas", 8.5f);
        var clear = new Button { Text = "Clear log", Dock = DockStyle.Bottom, Height = 30 };
        clear.Click += (_, _) => _log.Clear();
        panel.Controls.Add(_log);
        panel.Controls.Add(clear);
        panel.Controls.Add(header);
        return panel;
    }

    private static void AddLabeled(TableLayoutPanel table, int row, string label, Control field) =>
        AddLabeled(table, row, new Label { Text = label, AutoSize = true }, field);

    private static void AddLabeled(TableLayoutPanel table, int row, Label label, Control field)
    {
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        label.Anchor = AnchorStyles.Left;
        label.Margin = new Padding(0, 8, 8, 0);
        field.Dock = DockStyle.Fill;
        field.Margin = new Padding(0, 4, 0, 4);
        table.Controls.Add(label, 0, row);
        table.Controls.Add(field, 1, row);
    }

    private void ReloadSettingsFields()
    {
        _environmentCombo.SelectedItem = _settings.Environment;
        _wifiKey.Text = _settings.WifiSecureHashKey;
        _webKey.Text = _settings.WebServiceSecureHashKey;
    }

    private void ReloadTerminals()
    {
        var selectedSerial = (_terminalCombo.SelectedItem as TerminalItem)?.Terminal.SerialNumber;
        var terminals = _repository.GetAll();

        _terminalCombo.Items.Clear();
        foreach (var t in terminals)
        {
            _terminalCombo.Items.Add(new TerminalItem(t));
        }

        _terminalsList.Items.Clear();
        foreach (var t in terminals)
        {
            var item = new ListViewItem(new[]
            {
                t.Name,
                t.SerialNumber,
                t.Mode.Label(),
                t.ConnectionSummary(),
            })
            {
                Tag = t,
            };
            if (!t.Mode.IsSupportedOnWindows())
            {
                item.ForeColor = Color.DarkOrange;
            }
            _terminalsList.Items.Add(item);
        }

        if (_terminalCombo.Items.Count == 0)
        {
            _terminalCombo.SelectedIndex = -1;
            _configSummary.Text = "No terminals registered — add one on the Terminals tab.";
            return;
        }

        var match = -1;
        for (var i = 0; i < _terminalCombo.Items.Count; i++)
        {
            if (_terminalCombo.Items[i] is TerminalItem ti
                && string.Equals(ti.Terminal.SerialNumber, selectedSerial, StringComparison.OrdinalIgnoreCase))
            {
                match = i;
                break;
            }
        }
        _terminalCombo.SelectedIndex = match >= 0 ? match : 0;
        OnTerminalSelectionChanged();
    }

    private Terminal? SelectedListTerminal() =>
        _terminalsList.SelectedItems.Count == 0 ? null : _terminalsList.SelectedItems[0].Tag as Terminal;

    private Terminal? SelectedTerminal() =>
        (_terminalCombo.SelectedItem as TerminalItem)?.Terminal;

    private void EditTerminal(Terminal? existing)
    {
        using var form = new TerminalEditForm(_repository, existing);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            ReloadTerminals();
        }
    }

    private void OnTerminalSelectionChanged()
    {
        var terminal = SelectedTerminal();
        if (terminal == null)
        {
            _configSummary.Text = "Select a terminal.";
            return;
        }

        var config = _sessions.Resolve(terminal);
        var lines = new List<string>
        {
            $"{config.Mode.Label()} · {config.ConnectionSummary}",
            $"Signing: {config.HashKeyLabel} — {(config.HashKeyConfigured ? "configured" : "missing")}",
            $"Environment: {_settings.Environment}",
        };
        if (!config.IsReady)
        {
            lines.Add("Not ready:");
            lines.AddRange(config.Issues.Select(i => "• " + i));
            _configSummary.ForeColor = Color.DarkOrange;
        }
        else
        {
            lines.Add("Ready to send.");
            _configSummary.ForeColor = Color.ForestGreen;
        }
        _configSummary.Text = string.Join(Environment.NewLine, lines);
    }

    private void RefreshFormVisibility()
    {
        var type = (_typeCombo.SelectedItem as TypeItem)?.Type ?? EcrTransactionType.Sale;
        var form = new TransactionFormState
        {
            Type = type,
            LookUpByReference = _lookUpByReference.Checked,
            UseOtherTerminal = _useOtherTerminal.Checked,
        };

        _lookUpByReference.Visible = form.ShowReferenceSwitch;
        _amount.Visible = form.ShowAmount;
        _amountLabel.Visible = form.ShowAmount;
        _receipt.Visible = form.ShowReceiptNumber;
        _receiptLabel.Visible = form.ShowReceiptNumber;
        _originalReference.Visible = form.LooksUpByReference;
        _referenceLabel.Visible = form.LooksUpByReference;
        _originalDate.Visible = form.ShowOriginalDate;
        _dateLabel.Visible = form.ShowOriginalDate;
        _merchantReference.Visible = form.ShowMerchantReference;
        _merchantRefLabel.Visible = form.ShowMerchantReference;
        _useOtherTerminal.Visible = form.ShowOtherTerminalSwitch;
        _originalTerminalId.Visible = form.ShowOriginalTerminal;
        _otherTerminalLabel.Visible = form.ShowOriginalTerminal;
    }

    private async Task SubmitAsync()
    {
        if (_busy) return;
        var terminal = SelectedTerminal();
        if (terminal == null)
        {
            MessageBox.Show(this, "Register a terminal first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var type = (_typeCombo.SelectedItem as TypeItem)?.Type ?? EcrTransactionType.Sale;
        var form = new TransactionFormState
        {
            Type = type,
            AmountText = _amount.Text,
            ReceiptNumber = _receipt.Text,
            OriginalTerminalId = _originalTerminalId.Text,
            OriginalDate = _originalDate.Value.Date,
            UseOtherTerminal = _useOtherTerminal.Checked,
            LookUpByReference = _lookUpByReference.Checked,
            OriginalReference = _originalReference.Text,
            MerchantReference = _merchantReference.Text,
        };

        var (_, request) = form.Validated(terminal.SerialNumber);
        if (request == null)
        {
            _fieldError.Text = string.Join(Environment.NewLine, new[]
            {
                form.Errors.Amount,
                form.Errors.ReceiptNumber,
                form.Errors.OriginalReference,
                form.Errors.MerchantReference,
                form.Errors.OriginalTerminalId,
            }.Where(s => s != null)!);
            return;
        }
        _fieldError.Text = "";

        var active = _sessions.Resolve(terminal);
        if (!active.IsReady)
        {
            MessageBox.Show(
                this,
                string.Join(Environment.NewLine, active.Issues),
                "Terminal not ready",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        SetBusy(true, "Checking terminal…");
        try
        {
            var outcome = await TransactionRunner.ExecuteAsync(
                _sessions,
                terminal,
                active,
                request,
                status => SetBusy(true, status)).ConfigureAwait(true);

            switch (outcome)
            {
                case TransactionOutcome.InquiryDone inquired:
                    ResultForm.ShowInquiry(this, inquired.Inquiry);
                    break;
                case TransactionOutcome.ResultDone completed:
                    if (completed.Result is EcrResult.Failed failed
                        && failed.Recovered is EcrInquiry.Found recovered)
                    {
                        ResultForm.ShowInquiry(this, recovered);
                    }
                    else
                    {
                        ResultForm.ShowResult(this, completed.Result, request.Type);
                    }
                    break;
                case TransactionOutcome.Error err:
                    MessageBox.Show(this, err.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    break;
            }
        }
        catch (Exception ex)
        {
            AppendLog("ERROR: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Transaction failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false, "Idle");
        }
    }

    private void SetBusy(bool busy, string status)
    {
        _busy = busy;
        _submit.Enabled = !busy;
        _status.Text = status;
        UseWaitCursor = busy;
    }

    private void AppendLog(string message)
    {
        void Write()
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            if (_log.TextLength > 0) _log.AppendText(Environment.NewLine);
            _log.AppendText(line);
        }

        if (IsDisposed) return;
        if (InvokeRequired)
        {
            BeginInvoke(Write);
        }
        else
        {
            Write();
        }
    }

    private sealed record TerminalItem(Terminal Terminal)
    {
        public override string ToString() => Terminal.ToString();
    }

    private sealed record TypeItem(EcrTransactionType Type)
    {
        public override string ToString() => Type.DisplayName();
    }
}
#endif
