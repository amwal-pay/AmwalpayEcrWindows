using AmwalPay.Ecr.Simulator.Data;
using AmwalPay.Ecr.Simulator.Ui;

namespace AmwalPay.Ecr.Simulator;

internal static class Program
{
#if AMWAL_ECR_WINFORMS
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var store = AppStore.Load();
        var repository = new TerminalRepository(store);
        var settings = new EcrSimulatorSettings(store);
        var sessions = new TerminalSessions(settings);

        Application.Run(new MainForm(repository, settings, sessions));
    }
#else
    private static async Task Main()
    {
        var store = AppStore.Load();
        var repository = new TerminalRepository(store);
        var settings = new EcrSimulatorSettings(store);
        var sessions = new TerminalSessions(settings);
        await new ConsoleHost(repository, settings, sessions).RunAsync().ConfigureAwait(false);
    }
#endif
}
