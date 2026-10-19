namespace AmwalPay.Ecr.Simulator.Data;

/// <summary>TMS <c>ecrMode</c> values — mirrors the Android ECR-simulator.</summary>
public enum EcrMode
{
    UsbCable = 1,
    Wifi = 2,
    Bluetooth = 3,
    WebService = 4,
}

public static class EcrModes
{
    public const EcrMode Default = EcrMode.Wifi;

    public static EcrMode FromValue(int value) =>
        Enum.IsDefined(typeof(EcrMode), value) ? (EcrMode)value : Default;

    public static string Label(this EcrMode mode) => mode switch
    {
        EcrMode.UsbCable => "USB Cable",
        EcrMode.Wifi => "Wi‑Fi",
        EcrMode.Bluetooth => "Bluetooth",
        EcrMode.WebService => "Web Service",
        _ => mode.ToString(),
    };

    public static bool IsIpBased(this EcrMode mode) => mode == EcrMode.Wifi;

    /// <summary>Modes this Windows sample can actually open a session for.</summary>
    public static bool IsSupportedOnWindows(this EcrMode mode) =>
        mode is EcrMode.Wifi or EcrMode.WebService;

    public static string UnsupportedReason(this EcrMode mode) => mode switch
    {
        EcrMode.UsbCable => "USB cable ECR is not supported on Windows in this sample (no native USB accessory channel).",
        EcrMode.Bluetooth => "Bluetooth ECR is not supported on Windows in this sample.",
        _ => $"{mode.Label()} is not supported on Windows in this sample.",
    };
}
