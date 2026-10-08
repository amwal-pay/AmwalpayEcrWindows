namespace AmwalPay.Ecr.Simulator.Ui;

public static class AmountLimits
{
    public static readonly decimal Min = 0.010m;
    public static readonly decimal Max = 100_000.000m;
    public const int FractionDigits = 3;

    public static string Formatted(decimal amount) =>
        amount.ToString($"F{FractionDigits}", System.Globalization.CultureInfo.InvariantCulture);
}
