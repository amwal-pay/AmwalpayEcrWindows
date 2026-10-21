#if AMWAL_ECR_WINFORMS
using AmwalPay.Ecr;
using AmwalPay.Ecr.Simulator.Data;

namespace AmwalPay.Ecr.Simulator.Ui;

public sealed class TerminalEditForm : Form
{
    private readonly TerminalRepository _repository;
    private readonly string? _originalSerial;

    private readonly TextBox _name = new();
    private readonly TextBox _serial = new();
    private readonly ComboBox _mode = new();
    private readonly TextBox _ip = new();
    private readonly TextBox _port = new();
    private readonly TextBox _merchantId = new();
    private readonly TextBox _terminalId = new();
    private readonly Label _hint = new();
    private readonly Label _error = new();

    private Panel _wifiPanel = null!;
    private Panel _webPanel = null!;

    public TerminalEditForm(TerminalRepository repository, Terminal? existing)
    {
        _repository = repository;
        _originalSerial = existing?.SerialNumber;

        Text = existing == null ? "Add terminal" : "Edit terminal";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(460, 480);
        Font = new Font("Segoe UI", 9f);
        Padding = new Padding(12);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 10,
            AutoSize = true,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(layout, 0, "Name", _name);
        AddRow(layout, 1, "Serial number", _serial);

        _mode.DropDownStyle = ComboBoxStyle.DropDownList;
        foreach (EcrMode m in Enum.GetValues<EcrMode>())
        {
            _mode.Items.Add(new ModeItem(m));
        }
        _mode.SelectedIndexChanged += (_, _) => RefreshModePanels();
        AddRow(layout, 2, "ECR mode", _mode);

        _wifiPanel = BuildWifiPanel();
        _webPanel = BuildWebPanel();
        layout.Controls.Add(_wifiPanel, 0, 3);
        layout.SetColumnSpan(_wifiPanel, 2);
        layout.Controls.Add(_webPanel, 0, 4);
        layout.SetColumnSpan(_webPanel, 2);

        _hint.AutoSize = true;
        _hint.ForeColor = Color.DimGray;
        _hint.MaximumSize = new Size(420, 0);
        layout.Controls.Add(_hint, 0, 5);
        layout.SetColumnSpan(_hint, 2);

        _error.AutoSize = true;
        _error.ForeColor = Color.Firebrick;
        _error.MaximumSize = new Size(420, 0);
        layout.Controls.Add(_error, 0, 6);
        layout.SetColumnSpan(_error, 2);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 40,
        };
        var save = new Button { Text = "Save", AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        save.Click += (_, _) => OnSave();
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);

        Controls.Add(layout);
        Controls.Add(buttons);
        AcceptButton = save;
        CancelButton = cancel;

        if (existing != null)
        {
            _name.Text = existing.Name;
            _serial.Text = existing.SerialNumber;
            SelectMode(existing.Mode);
            _ip.Text = existing.IpAddress;
            _port.Text = existing.Port > 0 ? existing.Port.ToString() : EcrConfig.DefaultPort.ToString();
            _merchantId.Text = existing.MerchantId;
            _terminalId.Text = existing.TerminalId;
        }
        else
        {
            SelectMode(EcrModes.Default);
            _port.Text = EcrConfig.DefaultPort.ToString();
        }

        RefreshModePanels();
    }

    private Panel BuildWifiPanel()
    {
        var panel = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(panel, 0, "IP address", _ip);
        AddRow(panel, 1, "Port", _port);
        return panel;
    }

    private Panel BuildWebPanel()
    {
        var panel = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(panel, 0, "Merchant ID", _merchantId);
        AddRow(panel, 1, "Terminal ID", _terminalId);
        return panel;
    }

    private static void AddRow(TableLayoutPanel table, int row, string label, Control field)
    {
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 8, 8, 0),
        }, 0, row);
        field.Dock = DockStyle.Fill;
        field.Margin = new Padding(0, 4, 0, 4);
        table.Controls.Add(field, 1, row);
    }

    private void SelectMode(EcrMode mode)
    {
        for (var i = 0; i < _mode.Items.Count; i++)
        {
            if (_mode.Items[i] is ModeItem item && item.Mode == mode)
            {
                _mode.SelectedIndex = i;
                return;
            }
        }
        _mode.SelectedIndex = 0;
    }

    private EcrMode SelectedMode =>
        _mode.SelectedItem is ModeItem item ? item.Mode : EcrModes.Default;

    private void RefreshModePanels()
    {
        var mode = SelectedMode;
        _wifiPanel.Visible = mode.IsIpBased() || mode == EcrMode.Bluetooth;
        _webPanel.Visible = mode == EcrMode.WebService;
        _hint.Text = mode.IsSupportedOnWindows()
            ? mode switch
            {
                EcrMode.Wifi => "Wi‑Fi: TCP to the terminal on the local network.",
                EcrMode.WebService => "Web Service: HTTPS to Amwal Hub (merchant + terminal IDs required).",
                _ => "",
            }
            : mode.UnsupportedReason();
        _hint.ForeColor = mode.IsSupportedOnWindows() ? Color.DimGray : Color.DarkOrange;
    }

    private void OnSave()
    {
        _error.Text = "";
        var mode = SelectedMode;
        var portText = _port.Text.Trim();
        var port = EcrConfig.DefaultPort;
        if (_wifiPanel.Visible)
        {
            if (!int.TryParse(portText, out port) || port is < 1 or > 65535)
            {
                _error.Text = $"Port must be between 1 and 65535 (default {EcrConfig.DefaultPort})";
                return;
            }
            if (mode.IsIpBased() && string.IsNullOrWhiteSpace(_ip.Text))
            {
                _error.Text = "IP address is required for Wi‑Fi";
                return;
            }
        }

        if (mode == EcrMode.WebService)
        {
            if (string.IsNullOrWhiteSpace(_merchantId.Text) || string.IsNullOrWhiteSpace(_terminalId.Text))
            {
                _error.Text = "Merchant ID and Terminal ID are required for Web Service";
                return;
            }
        }

        var terminal = new Terminal
        {
            Name = _name.Text,
            SerialNumber = _serial.Text,
            ModeValue = (int)mode,
            IpAddress = _wifiPanel.Visible ? _ip.Text.Trim() : "",
            Port = _wifiPanel.Visible ? port : 0,
            MerchantId = _webPanel.Visible ? _merchantId.Text.Trim() : "",
            TerminalId = _webPanel.Visible ? _terminalId.Text.Trim() : "",
        };

        var result = _repository.Save(terminal, _originalSerial);
        if (result is SaveResult.Failed failed)
        {
            _error.Text = failed.Message;
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed record ModeItem(EcrMode Mode)
    {
        public override string ToString() => Mode.Label();
    }
}
#endif
