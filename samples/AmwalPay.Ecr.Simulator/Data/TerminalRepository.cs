namespace AmwalPay.Ecr.Simulator.Data;

public abstract record SaveResult
{
    public sealed record Saved : SaveResult;

    public sealed record Failed(string Message) : SaveResult;
}

/// <summary>Persists registered terminals in the JSON <see cref="AppStore"/>.</summary>
public sealed class TerminalRepository
{
    private readonly AppStore _store;

    public TerminalRepository(AppStore store) => _store = store;

    public IReadOnlyList<Terminal> GetAll() => _store.Terminals.ToList();

    public Terminal? FindBySerial(string serialNumber) =>
        _store.Terminals.FirstOrDefault(t =>
            string.Equals(t.SerialNumber, serialNumber, StringComparison.OrdinalIgnoreCase));

    public SaveResult Save(Terminal terminal, string? originalSerial)
    {
        var serial = (terminal.SerialNumber ?? "").Trim();
        if (serial.Length == 0)
        {
            return new SaveResult.Failed("Serial number is required");
        }

        terminal.SerialNumber = serial;
        terminal.Name = (terminal.Name ?? "").Trim();
        terminal.IpAddress = (terminal.IpAddress ?? "").Trim();
        terminal.TerminalId = (terminal.TerminalId ?? "").Trim();
        terminal.MerchantId = (terminal.MerchantId ?? "").Trim();

        var serialChanged = !string.Equals(originalSerial, serial, StringComparison.OrdinalIgnoreCase);
        if (serialChanged && FindBySerial(serial) != null)
        {
            return new SaveResult.Failed("A terminal with this serial number already exists");
        }

        if (originalSerial == null)
        {
            _store.Terminals.Add(terminal);
        }
        else if (serialChanged)
        {
            _store.Terminals.RemoveAll(t =>
                string.Equals(t.SerialNumber, originalSerial, StringComparison.OrdinalIgnoreCase));
            _store.Terminals.Add(terminal);
        }
        else
        {
            var index = _store.Terminals.FindIndex(t =>
                string.Equals(t.SerialNumber, originalSerial, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                _store.Terminals.Add(terminal);
            }
            else
            {
                _store.Terminals[index] = terminal;
            }
        }

        _store.Save();
        return new SaveResult.Saved();
    }

    public void Delete(Terminal terminal)
    {
        _store.Terminals.RemoveAll(t =>
            string.Equals(t.SerialNumber, terminal.SerialNumber, StringComparison.OrdinalIgnoreCase));
        _store.Save();
    }
}
