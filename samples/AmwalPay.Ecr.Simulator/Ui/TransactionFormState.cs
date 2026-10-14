using System.Globalization;
using AmwalPay.Ecr;

namespace AmwalPay.Ecr.Simulator.Ui;

public sealed class FormErrors
{
    public string? Amount { get; init; }
    public string? ReceiptNumber { get; init; }
    public string? OriginalTerminalId { get; init; }
    public string? OriginalReference { get; init; }
    public string? MerchantReference { get; init; }

    public bool Any =>
        Amount != null
        || ReceiptNumber != null
        || OriginalTerminalId != null
        || OriginalReference != null
        || MerchantReference != null;
}

public sealed record TransactionRequest(
    EcrTransactionType Type,
    string TerminalSerial,
    decimal? Amount,
    string ReceiptNumber = "",
    string OriginalTerminalId = "",
    string TransactionDate = "",
    string OriginalReference = "",
    string MerchantReference = "")
{
    public bool LooksUpByReference => OriginalReference.Length != 0;
}

/// <summary>Operator input + validation — mirrors Android <c>TransactionFormState</c>.</summary>
public sealed class TransactionFormState
{
    public const int MerchantReferenceMaxLength = 32;
    private const string WireDateFormat = "yyyyMMdd";

    public EcrTransactionType Type { get; set; } = EcrTransactionType.Sale;
    public string AmountText { get; set; } = "";
    public string ReceiptNumber { get; set; } = "";
    public string OriginalTerminalId { get; set; } = "";
    public DateTime OriginalDate { get; set; } = DateTime.Today;
    public bool UseOtherTerminal { get; set; }
    public bool LookUpByReference { get; set; }
    public string OriginalReference { get; set; } = "";
    public string MerchantReference { get; set; } = "";
    public FormErrors Errors { get; set; } = new();

    public bool ShowOtherTerminalSwitch => Type.AllowsOtherTerminal();
    public bool ShowOriginalTerminal => Type.AllowsOtherTerminal() && UseOtherTerminal;
    public bool ShowReferenceSwitch => Type == EcrTransactionType.Inquiry;
    public bool ShowMerchantReference => Type == EcrTransactionType.Sale;
    public bool LooksUpByReference => ShowReferenceSwitch && LookUpByReference;

    public bool ShowReceiptNumber => Type.RequiresOriginalStan() && !LooksUpByReference;
    public bool ShowOriginalDate => Type.RequiresOriginalDate() && !LooksUpByReference;
    public bool ShowAmount => Type.RequiresAmount();

    public string OriginalDateOnWire => OriginalDate.ToString(WireDateFormat, CultureInfo.InvariantCulture);

    public (TransactionFormState State, TransactionRequest? Request) Validated(string terminalSerial)
    {
        decimal? amount = null;
        string? amountError = null;
        if (ShowAmount)
        {
            if (!decimal.TryParse(AmountText.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                && !decimal.TryParse(AmountText.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out parsed))
            {
                amountError = "Enter a valid amount";
            }
            else if (parsed == 0)
            {
                amountError = "Required field";
            }
            else if (parsed < AmountLimits.Min)
            {
                amountError = $"minimum amount allowed is {AmountLimits.Formatted(AmountLimits.Min)}";
            }
            else if (parsed > AmountLimits.Max)
            {
                amountError = $"maximum amount allowed is {AmountLimits.Formatted(AmountLimits.Max)}";
            }
            else
            {
                amount = Math.Round(parsed, AmountLimits.FractionDigits, MidpointRounding.AwayFromZero);
            }
        }

        var receiptError = ShowReceiptNumber && string.IsNullOrWhiteSpace(ReceiptNumber)
            ? "Enter the receipt number of the transaction to act on"
            : null;
        var referenceError = LooksUpByReference && string.IsNullOrWhiteSpace(OriginalReference)
            ? "Enter the reference the transaction was sent with"
            : null;
        var merchantRefError = ShowMerchantReference ? MerchantReferenceError(MerchantReference) : null;
        var terminalError = ShowOriginalTerminal && string.IsNullOrWhiteSpace(OriginalTerminalId)
            ? "Enter the terminal the original was taken on"
            : null;

        var errors = new FormErrors
        {
            Amount = amountError,
            ReceiptNumber = receiptError,
            OriginalTerminalId = terminalError,
            OriginalReference = referenceError,
            MerchantReference = merchantRefError,
        };

        if (errors.Any)
        {
            Errors = errors;
            return (this, null);
        }

        Errors = new FormErrors();
        var request = new TransactionRequest(
            Type: Type,
            TerminalSerial: terminalSerial,
            Amount: amount,
            ReceiptNumber: LooksUpByReference ? "" : ReceiptNumber.Trim(),
            OriginalTerminalId: ShowOriginalTerminal ? OriginalTerminalId.Trim() : "",
            TransactionDate: ShowOriginalDate ? OriginalDateOnWire : "",
            // By-reference inquiry: originalMerchantReference = what to look up;
            // merchantReference = this inquiry's own id (blank → SDK generates).
            // Never copy the original into merchantReference — that made both
            // wire fields the same and broke Hub correlation.
            OriginalReference: LooksUpByReference ? OriginalReference.Trim() : "",
            MerchantReference: LooksUpByReference
                ? ""
                : ShowMerchantReference ? MerchantReference.Trim() : "");

        return (this, request);
    }

    private static string? MerchantReferenceError(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed.Length > MerchantReferenceMaxLength)
        {
            return $"A merchant reference is at most {MerchantReferenceMaxLength} characters";
        }
        foreach (var c in trimmed)
        {
            if (c is ' ' or '&' or '=' || c is < (char)0x21 or > (char)0x7E)
            {
                return "Use printable ASCII without spaces, '&' or '='";
            }
        }
        return null;
    }
}
