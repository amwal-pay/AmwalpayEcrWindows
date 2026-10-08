using System.Text.Json;
using System.Text.Json.Serialization;

namespace AmwalPay.Ecr.Simulator.Data;

/// <summary>JSON persistence under %LocalAppData%/AmwalPay.Ecr.Simulator.</summary>
public sealed class AppStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private readonly object _gate = new();

    public AppStore(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public List<Terminal> Terminals { get; set; } = new();

    public string Environment { get; set; } = AmwalPay.Ecr.EcrEnvironments.Default.ToString();

    public string WifiSecureHashKey { get; set; } = "";

    public string WebServiceSecureHashKey { get; set; } = "";

    public static AppStore Load()
    {
        var path = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "AmwalPay.Ecr.Simulator",
            "settings.json");

        if (!File.Exists(path))
        {
            return new AppStore(path);
        }

        try
        {
            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<AppStoreDto>(json, JsonOptions) ?? new AppStoreDto();
            return new AppStore(path)
            {
                Terminals = loaded.Terminals ?? new List<Terminal>(),
                Environment = loaded.Environment ?? AmwalPay.Ecr.EcrEnvironments.Default.ToString(),
                WifiSecureHashKey = loaded.WifiSecureHashKey ?? "",
                WebServiceSecureHashKey = loaded.WebServiceSecureHashKey ?? "",
            };
        }
        catch
        {
            return new AppStore(path);
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            var dto = new AppStoreDto
            {
                Terminals = Terminals,
                Environment = Environment,
                WifiSecureHashKey = WifiSecureHashKey,
                WebServiceSecureHashKey = WebServiceSecureHashKey,
            };
            File.WriteAllText(_path, JsonSerializer.Serialize(dto, JsonOptions));
        }
    }

    public string PathOnDisk => _path;

    private sealed class AppStoreDto
    {
        public List<Terminal>? Terminals { get; set; }
        public string? Environment { get; set; }
        public string? WifiSecureHashKey { get; set; }
        public string? WebServiceSecureHashKey { get; set; }
    }
}
