using AmwalPay.Ecr;
using AmwalPay.Ecr.Simulator.Ui;

namespace AmwalPay.Ecr.Simulator.Data;

/// <summary>
/// Opens SDK sessions for registered terminals — mirrors Android <c>TerminalSessions</c>.
/// </summary>
public sealed class TerminalSessions
{
    private readonly EcrSimulatorSettings _settings;
    private Action<string> _logSink;

    public TerminalSessions(EcrSimulatorSettings settings, Action<string>? logSink = null)
    {
        _settings = settings;
        _logSink = logSink ?? (_ => { });
        Logger = EcrLogger.From(message =>
        {
            System.Diagnostics.Debug.WriteLine(message);
            _logSink(message);
        });
    }

    public IEcrLogger Logger { get; }

    public void AttachLogSink(Action<string> sink) => _logSink = sink ?? (_ => { });

    public SelectedTerminalConfig Resolve(Terminal terminal) =>
        SelectedTerminalConfig.Resolve(
            terminal,
            _settings.Environment,
            _settings.SecureHashKeyFor(terminal.Mode));

    public EcrOpenedSession Open(Terminal terminal, SelectedTerminalConfig active)
    {
        if (!active.IsReady)
        {
            throw new InvalidOperationException(
                string.Join(System.Environment.NewLine, active.Issues));
        }

        return EcrSessions.Open(
            terminalSerial: terminal.SerialNumber,
            plan: active.Plan,
            usbChannel: null,
            logger: Logger);
    }
}
