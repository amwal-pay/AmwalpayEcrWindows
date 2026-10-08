using AmwalPay.Ecr;

namespace AmwalPay.Ecr.Simulator.Data;

/// <summary>
/// App-owned signing and Web Service settings — mirrors Android <c>EcrSimulatorSettings</c>.
/// Secrets live in the JSON store for this sample (not Keystore).
/// </summary>
public sealed class EcrSimulatorSettings
{
    private readonly AppStore _store;

    public EcrSimulatorSettings(AppStore store) => _store = store;

    public EcrEnvironment Environment
    {
        get => EcrEnvironments.FromName(_store.Environment);
        set
        {
            _store.Environment = value.ToString();
            _store.Save();
        }
    }

    /// <summary>LAN signing key — Wi‑Fi (and USB/Bluetooth on Android).</summary>
    public string WifiSecureHashKey
    {
        get => _store.WifiSecureHashKey;
        set
        {
            _store.WifiSecureHashKey = (value ?? "").Trim();
            _store.Save();
        }
    }

    /// <summary>Web Service REST signing key.</summary>
    public string WebServiceSecureHashKey
    {
        get => _store.WebServiceSecureHashKey;
        set
        {
            _store.WebServiceSecureHashKey = (value ?? "").Trim();
            _store.Save();
        }
    }

    public string SecureHashKeyFor(EcrMode mode) => mode switch
    {
        EcrMode.WebService => WebServiceSecureHashKey,
        _ => WifiSecureHashKey,
    };
}
