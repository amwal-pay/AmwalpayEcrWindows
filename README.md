# Amwal ECR SDK for .NET

`AmwalPay.Ecr` drives an Amwal POS terminal from a .NET till: sale, void, refund, inquiry, e-receipt, and close receipt. Wi-Fi and USB use the terminal's ECR port. Web Service uses HTTPS to the Amwal Hub. Card data stays on the terminal.

Package `AmwalPay.Ecr` **1.0.2**. Apache-2.0.

## Install

```powershell
dotnet add package AmwalPay.Ecr --version 1.0.2
```

.NET Framework 4.6.2 or later, or .NET 8 or later. C# 9 or later (`EcrConfig` uses init-only properties). On an SDK-style .NET Framework project add `<LangVersion>9.0</LangVersion>`.

## Open a session

`EcrSessions.Plan` checks the link and the key and returns what is missing. `EcrSessions.Open` is the call that refuses an incomplete plan.

```csharp
using AmwalPay.Ecr;

EcrSessionPlan plan = EcrSessions.Plan(
    link: new EcrLink.Lan(Host: "192.168.1.50"),
    config: new EcrConfig { SecureHashKey = wifiKey });

if (!plan.IsReady)
{
    Show(string.Join("; ", plan.Issues));
    return;
}

EcrOpenedSession session = EcrSessions.Open("P653200085189", plan);
```

Web Service uses a different key, plus the numeric merchant id and terminal id. `Environment` defaults to `SIT`; set `PROD` for live.

```csharp
EcrSessionPlan plan = EcrSessions.Plan(
    link: new EcrLink.WebService(MerchantId: "13593", TerminalId: "101311"),
    config: new EcrConfig
    {
        SecureHashKey = webServiceKey,
        Environment = EcrEnvironment.UAT,
    });

EcrOpenedSession session = EcrSessions.Open("P653200008251", plan);
```

USB is the same session. Plan with `new EcrLink.UsbCable()` and pass your `IEcrChannel` into `Open`. The cable key is the Wi-Fi key.

## Features

Each call below is on `EcrOpenedSession`. Store the merchant reference before you call. Samples for every feature, and how to read each result, are in the [integration guide](https://github.com/amwal-pay/AmwalpayEcrWindows/blob/main/docs/integration-guide.md).

| Feature | Call | Where it works |
|---|---|---|
| Sale | `SaleAsync(amount, merchantReference)` | Wi-Fi, USB, Web Service |
| Void | `VoidAsync(receiptNumber, originalTerminalId, merchantReference)` | All. `originalTerminalId` is Wi-Fi / USB |
| Refund | `RefundAsync(amount, receiptNumber, transactionDate, originalTerminalId, merchantReference)` | All. Date is `yyyyMMdd` |
| Inquiry by receipt number | `InquireAsync(receiptNumber, transactionDate, originalTerminalId)` | All |
| Inquiry by merchant reference | `InquireByReferenceAsync(originalReference)` | All. This is how you settle a lost answer |
| E-receipt URL | `ReceiptAsync(receiptNumber, transactionDate, originalTerminalId)` | Wi-Fi and USB. Put `Url` in a QR code |
| Close receipt | `CloseReceiptAsync(merchantReference, originalMerchantReference)` | All. Your button. Web Service needs the sale's reference |
| Reachability | `ProbeReachabilityAsync()` | Wi-Fi and USB. Web Service returns `null` |

```csharp
string reference = "ORDER-1001-1";
Save(reference);

EcrResult result = await session.SaleAsync(1.234m, reference);

switch (result)
{
    case EcrResult.Approved approved:
        Record(approved.Amount, approved.Rrn, approved.AuthCode);
        if (approved.PartialApproval)
            CollectBalance(approved.RequestedAmount, approved.Amount);
        break;
    case EcrResult.Declined declined:
        Show(declined.Reason);
        break;
    case EcrResult.Failed failed when failed.Settled && failed.Recovered is EcrInquiry.Found found:
        RecordFrom(found.Transaction);
        break;
    case EcrResult.Failed failed when failed.Failure.OutcomeUnknown:
        MarkUnresolved(failed.MerchantReference);
        break;
    case EcrResult.Failed failed:
        Show(failed.Failure.Message);
        break;
}
```

## Important notes

**Lost answer.** `Failed` with `Failure.OutcomeUnknown` means the terminal may have completed the payment. Call `InquireByReferenceAsync` with the reference you stored before the call. A second sale charges the cardholder again. `Failure.Unreachable` means the connection was never made. The full sequence is [Resolve an unknown outcome](https://github.com/amwal-pay/AmwalpayEcrWindows/blob/main/docs/integration-guide.md#resolve-an-unknown-outcome).

**Wi-Fi and USB inquire once.** `AutoInquireOnFailure` defaults to `true` for sale, void, and refund. Read `Failed.Recovered`. `EcrInquiry.Found` settles the attempt (`Failed.Settled`). Web Service leaves `Recovered` null.

**`NotFound` is a missing record.** After a lost answer, confirm the attempt on the terminal before you take payment again.

**One reference per attempt.** At most 32 characters: printable ASCII, no space, `&`, or `=`. Save it before the call. A void and a refund each get a new reference. A blank reference is generated onto the result; inquire by it only after you have stored that value.

**Amounts.** `decimal` in major units. OMR defaults are `CurrencyCode` `"512"` and `MinorUnitDigits` `3`. Set both for any other currency. On `PartialApproval`, `Amount` is what the card was charged and `RequestedAmount` is what you asked.

**Wait for the cardholder.** Wi-Fi waits 10 s to connect, then 120 s for the answer. USB passes 120 s to your channel. Web Service through `Plan` raises a 120 s `ResponseTimeout` to 130 s, and the HTTP deadline is connect plus response (140 s). Cancelling the token ends your wait. The payment can still complete.

**Keys.** Wi-Fi and USB share one key. Web Service uses another, issued per terminal. Even-length hex, at least 16 characters. `Open` refuses a plan whose key is missing or invalid.

**Close receipt.** Call it from your Close receipt action, after the cashier has finished with the result. On `EcrOpenedSession`, the sale's reference is `originalMerchantReference` (second argument). On `EcrWebServiceTerminal` that argument is first. Pass both by name.

**Logging.** Off until you pass `EcrLogger.From`. Each line is the request or the response, including the masked PAN.

## Documentation

- [Integration guide](https://github.com/amwal-pay/AmwalpayEcrWindows/blob/main/docs/integration-guide.md) — each feature, with the call and the result.
- [Public types](https://github.com/amwal-pay/AmwalpayEcrWindows/blob/main/docs/api-reference.md) — what to construct. Member detail is the XML documentation in the package.
- [Changelog](https://github.com/amwal-pay/AmwalpayEcrWindows/blob/main/CHANGELOG.md)
- [Sample application](https://github.com/amwal-pay/AmwalpayEcrWindows/blob/main/samples/README.md)
