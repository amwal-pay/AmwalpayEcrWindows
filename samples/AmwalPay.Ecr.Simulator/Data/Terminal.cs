using System.Text.Json.Serialization;

namespace AmwalPay.Ecr.Simulator.Data;

/// <summary>A registered POS terminal — serial number is the unique key.</summary>
public sealed class Terminal
{
    public string SerialNumber { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>TMS <c>ecrMode</c> integer.</summary>
    [JsonPropertyName("ecrMode")]
    public int ModeValue { get; set; } = (int)EcrModes.Default;

    public string IpAddress { get; set; } = "";

    public int Port { get; set; }

    public string TerminalId { get; set; } = "";

    public string MerchantId { get; set; } = "";

    [JsonIgnore]
    public EcrMode Mode => EcrModes.FromValue(ModeValue);

    public override string ToString() =>
        string.IsNullOrWhiteSpace(Name) ? SerialNumber : $"{Name} ({SerialNumber})";

    public string ConnectionSummary() => Mode switch
    {
        EcrMode.WebService => BuildWebSummary(),
        EcrMode.Bluetooth => "Bluetooth",
        EcrMode.UsbCable => "USB cable",
        EcrMode.Wifi => IpAddress.Length != 0 && Port > 0
            ? $"{IpAddress}:{Port}"
            : IpAddress.Length != 0 ? IpAddress : Mode.Label(),
        _ => Mode.Label(),
    };

    private string BuildWebSummary()
    {
        var parts = new List<string>();
        if (MerchantId.Length != 0) parts.Add($"Merchant {MerchantId}");
        if (TerminalId.Length != 0) parts.Add($"Terminal {TerminalId}");
        return parts.Count == 0 ? "Web Service" : string.Join(" · ", parts);
    }
}
