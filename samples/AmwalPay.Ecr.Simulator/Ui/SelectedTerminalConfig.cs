using AmwalPay.Ecr;
using AmwalPay.Ecr.Simulator.Data;

namespace AmwalPay.Ecr.Simulator.Ui;

/// <summary>App wrapper around <see cref="EcrSessionPlan"/> for a registered terminal.</summary>
public sealed class SelectedTerminalConfig
{
    public SelectedTerminalConfig(Terminal terminal, EcrSessionPlan plan)
    {
        Terminal = terminal;
        Plan = plan;
    }

    public Terminal Terminal { get; }

    public EcrSessionPlan Plan { get; }

    public EcrMode Mode => Terminal.Mode;

    public EcrConfig EcrConfig => Plan.Config;

    public IReadOnlyList<string> Issues => Plan.Issues;

    public bool UsesWebService => Plan.UsesWebService;

    public bool UsesLan => Plan.UsesLan;

    public bool IsSupported => Mode.IsSupportedOnWindows();

    public bool IsReady => IsSupported && Plan.IsReady;

    public string HashKeyLabel => Plan.SigningKeyLabel;

    public bool HashKeyConfigured => Plan.SigningKeyConfigured;

    public string ConnectionSummary => Plan.ConnectionSummary;

    public static SelectedTerminalConfig Resolve(
        Terminal terminal,
        EcrEnvironment environment,
        string secureHashKey)
    {
        var issues = new List<string>();
        if (!terminal.Mode.IsSupportedOnWindows())
        {
            issues.Add(terminal.Mode.UnsupportedReason());
        }

        var link = ToLink(terminal);
        var trimmedKey = (secureHashKey ?? "").Trim();
        var requested = new EcrConfig
        {
            SecureHashKey = EcrConfig.IsValidSecureHashKey(trimmedKey) ? trimmedKey : "",
            MerchantId = terminal.MerchantId,
            TerminalId = terminal.TerminalId,
            Environment = environment,
            Port = terminal.Port is >= 1 and <= 65535 ? terminal.Port : EcrConfig.DefaultPort,
        };

        var plan = EcrSessions.Plan(link, requested, trimmedKey);
        if (issues.Count != 0)
        {
            plan = plan with { Issues = plan.Issues.Concat(issues).Distinct().ToList() };
        }

        return new SelectedTerminalConfig(terminal, plan);
    }

    private static EcrLink ToLink(Terminal terminal) => terminal.Mode switch
    {
        EcrMode.WebService => new EcrLink.WebService(terminal.MerchantId, terminal.TerminalId),
        EcrMode.UsbCable => new EcrLink.UsbCable(),
        EcrMode.Wifi => new EcrLink.Lan(
            terminal.IpAddress,
            terminal.Port is >= 1 and <= 65535 ? terminal.Port : EcrConfig.DefaultPort),
        EcrMode.Bluetooth => new EcrLink.Lan(
            terminal.IpAddress,
            terminal.Port is >= 1 and <= 65535 ? terminal.Port : EcrConfig.DefaultPort),
        _ => new EcrLink.Lan(terminal.IpAddress, EcrConfig.DefaultPort),
    };
}
