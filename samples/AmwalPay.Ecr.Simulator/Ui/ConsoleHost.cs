using System.Globalization;
using AmwalPay.Ecr;
using AmwalPay.Ecr.Simulator.Data;

namespace AmwalPay.Ecr.Simulator.Ui;

/// <summary>
/// Interactive console host used when WinForms / Windows Desktop SDK is unavailable.
/// Same data layer and <see cref="TransactionRunner"/> as the WinForms UI.
/// </summary>
public sealed class ConsoleHost
{
    private readonly TerminalRepository _repository;
    private readonly EcrSimulatorSettings _settings;
    private readonly TerminalSessions _sessions;
    private readonly List<string> _log = new();

    public ConsoleHost(
        TerminalRepository repository,
        EcrSimulatorSettings settings,
        TerminalSessions sessions)
    {
        _repository = repository;
        _settings = settings;
        _sessions = sessions;
        _sessions.AttachLogSink(line =>
        {
            lock (_log)
            {
                _log.Add($"[{DateTime.Now:HH:mm:ss}] {line}");
            }
            Console.WriteLine("  [log] " + line);
        });
    }

    public async Task RunAsync()
    {
        Console.WriteLine("Amwal ECR Simulator (console)");
        Console.WriteLine("WinForms UI builds on Windows with the .NET Windows Desktop SDK.");
        Console.WriteLine();

        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("1) Settings (environment + hash keys)");
            Console.WriteLine("2) List terminals");
            Console.WriteLine("3) Add / edit terminal");
            Console.WriteLine("4) Delete terminal");
            Console.WriteLine("5) Run transaction");
            Console.WriteLine("6) Show recent log");
            Console.WriteLine("0) Exit");
            Console.Write("> ");
            var choice = Console.ReadLine()?.Trim();
            switch (choice)
            {
                case "1": EditSettings(); break;
                case "2": ListTerminals(); break;
                case "3": EditTerminal(); break;
                case "4": DeleteTerminal(); break;
                case "5": await RunTransactionAsync().ConfigureAwait(false); break;
                case "6": ShowLog(); break;
                case "0": return;
                default: Console.WriteLine("Unknown option."); break;
            }
        }
    }

    private void EditSettings()
    {
        Console.WriteLine($"Current environment: {_settings.Environment}");
        Console.Write("Environment [SIT/UAT/PROD, Enter=keep]: ");
        var env = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(env))
        {
            _settings.Environment = EcrEnvironments.FromName(env);
        }

        Console.Write("Wi‑Fi / LAN secure hash key (Enter=keep): ");
        var wifi = Console.ReadLine();
        if (wifi != null && wifi.Length > 0) _settings.WifiSecureHashKey = wifi;

        Console.Write("Web Service secure hash key (Enter=keep): ");
        var web = Console.ReadLine();
        if (web != null && web.Length > 0) _settings.WebServiceSecureHashKey = web;

        Console.WriteLine($"Saved. Environment={_settings.Environment}");
    }

    private void ListTerminals()
    {
        var terminals = _repository.GetAll();
        if (terminals.Count == 0)
        {
            Console.WriteLine("(none)");
            return;
        }
        foreach (var t in terminals)
        {
            var config = _sessions.Resolve(t);
            var ready = config.IsReady ? "ready" : "not ready";
            Console.WriteLine($"- {t} | {t.Mode.Label()} | {t.ConnectionSummary()} | {ready}");
            if (!config.IsReady)
            {
                foreach (var issue in config.Issues) Console.WriteLine($"    • {issue}");
            }
        }
    }

    private void EditTerminal()
    {
        Console.Write("Serial to edit (blank = add new): ");
        var serial = Console.ReadLine()?.Trim();
        Terminal? existing = string.IsNullOrEmpty(serial) ? null : _repository.FindBySerial(serial!);
        var original = existing?.SerialNumber;

        var name = Prompt("Name", existing?.Name ?? "");
        var newSerial = Prompt("Serial number", existing?.SerialNumber ?? "");
        Console.WriteLine("Modes: 1=USB 2=Wi‑Fi 3=Bluetooth 4=Web Service 5=App-to-app");
        Console.WriteLine("Supported here: 2 (Wi‑Fi), 4 (Web Service). Others are stored but not opened.");
        var modeVal = PromptInt("Mode", existing?.ModeValue ?? (int)EcrModes.Default);
        var mode = EcrModes.FromValue(modeVal);

        var ip = "";
        var port = 0;
        var merchantId = "";
        var terminalId = "";
        if (mode.IsIpBased() || mode == EcrMode.Bluetooth)
        {
            ip = Prompt("IP address", existing?.IpAddress ?? "");
            port = PromptInt("Port", existing?.Port > 0 ? existing!.Port : EcrConfig.DefaultPort);
        }
        if (mode == EcrMode.WebService)
        {
            merchantId = Prompt("Merchant ID", existing?.MerchantId ?? "");
            terminalId = Prompt("Terminal ID", existing?.TerminalId ?? "");
        }

        var terminal = new Terminal
        {
            Name = name,
            SerialNumber = newSerial,
            ModeValue = (int)mode,
            IpAddress = ip,
            Port = port,
            MerchantId = merchantId,
            TerminalId = terminalId,
        };
        var result = _repository.Save(terminal, original);
        Console.WriteLine(result is SaveResult.Failed f ? $"Failed: {f.Message}" : "Saved.");
    }

    private void DeleteTerminal()
    {
        Console.Write("Serial to delete: ");
        var serial = Console.ReadLine()?.Trim() ?? "";
        var t = _repository.FindBySerial(serial);
        if (t == null)
        {
            Console.WriteLine("Not found.");
            return;
        }
        _repository.Delete(t);
        Console.WriteLine("Deleted.");
    }

    private async Task RunTransactionAsync()
    {
        var terminals = _repository.GetAll();
        if (terminals.Count == 0)
        {
            Console.WriteLine("No terminals — add one first.");
            return;
        }
        for (var i = 0; i < terminals.Count; i++)
        {
            Console.WriteLine($"  {i + 1}) {terminals[i]}");
        }
        var index = PromptInt("Terminal #", 1) - 1;
        if (index < 0 || index >= terminals.Count)
        {
            Console.WriteLine("Invalid selection.");
            return;
        }
        var terminal = terminals[index];
        var active = _sessions.Resolve(terminal);
        Console.WriteLine($"{active.Mode.Label()} · {active.ConnectionSummary}");
        if (!active.IsReady)
        {
            foreach (var issue in active.Issues) Console.WriteLine("• " + issue);
            return;
        }

        Console.WriteLine("1=Sale 2=Void 3=Refund 4=Inquiry");
        var typeChoice = PromptInt("Operation", 1);
        var type = typeChoice switch
        {
            2 => EcrTransactionType.Void,
            3 => EcrTransactionType.Refund,
            4 => EcrTransactionType.Inquiry,
            _ => EcrTransactionType.Sale,
        };

        var form = new TransactionFormState { Type = type };
        if (form.ShowAmount) form.AmountText = Prompt("Amount", "1.000");
        if (type == EcrTransactionType.Inquiry)
        {
            Console.Write("Look up by merchant reference? [y/N]: ");
            form.LookUpByReference = (Console.ReadLine() ?? "").Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase);
        }
        if (form.ShowReceiptNumber) form.ReceiptNumber = Prompt("Receipt / STAN", "");
        if (form.LooksUpByReference) form.OriginalReference = Prompt("Original merchant reference", "");
        if (form.ShowOriginalDate)
        {
            var dateText = Prompt("Original date yyyyMMdd", DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            if (DateTime.TryParseExact(dateText, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            {
                form.OriginalDate = d;
            }
        }
        if (form.ShowMerchantReference) form.MerchantReference = Prompt("Merchant reference (optional)", "");
        if (form.ShowOtherTerminalSwitch)
        {
            Console.Write("Original on another terminal? [y/N]: ");
            form.UseOtherTerminal = (Console.ReadLine() ?? "").Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase);
            if (form.ShowOriginalTerminal) form.OriginalTerminalId = Prompt("Original terminal ID", "");
        }

        var (_, request) = form.Validated(terminal.SerialNumber);
        if (request == null)
        {
            Console.WriteLine("Validation failed:");
            if (form.Errors.Amount != null) Console.WriteLine("  " + form.Errors.Amount);
            if (form.Errors.ReceiptNumber != null) Console.WriteLine("  " + form.Errors.ReceiptNumber);
            if (form.Errors.OriginalReference != null) Console.WriteLine("  " + form.Errors.OriginalReference);
            if (form.Errors.MerchantReference != null) Console.WriteLine("  " + form.Errors.MerchantReference);
            if (form.Errors.OriginalTerminalId != null) Console.WriteLine("  " + form.Errors.OriginalTerminalId);
            return;
        }

        var outcome = await TransactionRunner.ExecuteAsync(
            _sessions, terminal, active, request, Console.WriteLine).ConfigureAwait(false);

        switch (outcome)
        {
            case TransactionOutcome.InquiryDone i:
                Console.WriteLine(ResultText.FormatInquiry(i.Inquiry));
                break;
            case TransactionOutcome.ResultDone r:
                if (r.Result is EcrResult.Failed failed && failed.Recovered is EcrInquiry.Found found)
                {
                    Console.WriteLine(ResultText.FormatInquiry(found));
                }
                else
                {
                    Console.WriteLine(ResultText.FormatResult(r.Result, request.Type));
                }
                break;
            case TransactionOutcome.Error e:
                Console.WriteLine("Error: " + e.Message);
                break;
        }
    }

    private void ShowLog()
    {
        lock (_log)
        {
            if (_log.Count == 0)
            {
                Console.WriteLine("(empty)");
                return;
            }
            foreach (var line in _log.TakeLast(80)) Console.WriteLine(line);
        }
    }

    private static string Prompt(string label, string current)
    {
        Console.Write(string.IsNullOrEmpty(current) ? $"{label}: " : $"{label} [{current}]: ");
        var value = Console.ReadLine();
        return string.IsNullOrWhiteSpace(value) ? current : value.Trim();
    }

    private static int PromptInt(string label, int current)
    {
        Console.Write($"{label} [{current}]: ");
        var value = Console.ReadLine();
        return int.TryParse(value, out var n) ? n : current;
    }
}
