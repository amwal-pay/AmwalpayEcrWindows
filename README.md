# Amwal ECR SDK for .NET

`AmwalPay.Ecr` drives an Amwal POS terminal from a .NET till: sale, void, refund, inquiry, e-receipt, and close receipt. Wi-Fi and USB use the terminal's ECR port. Web Service uses HTTPS to the Amwal Hub. Card data stays on the terminal.

Package `AmwalPay.Ecr` **1.0.2**. Open one session, call the operation, then read the result.

## Install

- .NET Framework 4.6.2 or later, or .NET 8 or later. C# 9 or later. On an SDK-style .NET Framework project set `<LangVersion>9.0</LangVersion>`
- Web Service is HTTPS. On .NET Framework 4.6.2 the process must allow TLS 1.2.

```powershell
dotnet add package AmwalPay.Ecr --version 1.0.2
```

Pin the version. A patch release can add API.

## Open a session

<a id="ecropenedsession"></a>

Open one session for the terminal and keep it, every sale, void, refund, inquiry, e-receipt, and close receipt can use it.

### Wi-Fi implementation

Each terminal have IP and port will be display on idle screen use that IP and port to establish connection from till to send operations for each terminal, and the till and terminal should be on same wifi.

```csharp
using AmwalPay.Ecr;

EcrSessionPlan plan = EcrSessions.Plan(
    link: new EcrLink.Lan(Host: "192.168.1.50"),   // port defaults to 9100
    config: new EcrConfig { SecureHashKey = wifiKey });

if (!plan.IsReady)
{
    Show(string.Join("; ", plan.Issues));
    return;
}

EcrOpenedSession session = EcrSessions.Open("P653200085189", plan);
```

### Web Service implementation

```csharp
EcrSessionPlan plan = EcrSessions.Plan(
    link: new EcrLink.WebService(MerchantId: "13593", TerminalId: "101311"),
    config: new EcrConfig
    {
        SecureHashKey = webServiceKey,
        Environment = EcrEnvironment.UAT,    // SIT is the default; PROD for live
    });

EcrOpenedSession session = EcrSessions.Open("P653200008251", plan);
```

Hub addresses stay inside the SDK. `EcrEnvironment.FromName` reads `SIT`, `UAT`, and `PROD`, ignoring case. Any other name reads as `SIT`.

### USB cable implementation

The till is the USB host and the terminal is the accessory. There is no IP address. The terminal profile is USB cable, and the signing key is the Wi-Fi key. `UsbAccessoryEcrChannel` asks the terminal to enter Android accessory mode, then sends the same framed messages as Wi-Fi.

On Windows the till attaches the built-in WinUSB driver the first time the cable is used, and Windows asks for approval once. macOS and Linux open the cable with libusb. After the switch the terminal comes back as Google vendor `18D1`.

```csharp
EcrSessionPlan plan = EcrSessions.Plan(
    link: new EcrLink.UsbCable(),
    config: new EcrConfig { SecureHashKey = wifiKey });

EcrOpenedSession session = EcrSessions.Open(
    "P653200085189",
    plan,
    usbChannel: () => new UsbAccessoryEcrChannel());
```

## Operations

Amounts are `decimal` in major units (`1.234` with the `m` suffix). `merchantReference` names this attempt. Save it before the call. Leave it blank and the SDK generates one and returns it on the result.

### Sale

The cardholder presents the card on the terminal.

```csharp
string reference = "ORDER-1001-1";
Save(reference);

EcrResult result = await session.SaleAsync(1.234m, reference);
```

Read `result` with the switch in [Read the result](#read-the-result).

### Void

Cancels an earlier transaction for its full amount. No card. `receiptNumber` is the receipt number (STAN). `originalTerminalId` names another terminal when the original was not taken on this one. Web Service ignores it and uses the terminal in the plan. The void has its own merchant reference.

```csharp
EcrResult result = await session.VoidAsync(
    receiptNumber: stan,
    originalTerminalId: "",
    merchantReference: "ORDER-1001-V1");
```

### Refund

Returns some or all of an earlier transaction. The cardholder presents the card. `transactionDate` is the original day, `yyyyMMdd`.

```csharp
EcrResult result = await session.RefundAsync(
    amount: 1.000m,
    receiptNumber: stan,
    transactionDate: "20261019",
    originalTerminalId: "",
    merchantReference: "ORDER-1001-R1");
```

### Inquiry by receipt number

Reads a transaction. No card, no money. Safe to repeat.

```csharp
EcrInquiry inquiry = await session.InquireAsync(
    receiptNumber: stan,
    transactionDate: "20261019");
```

### Inquiry by merchant reference

Reads the attempt you named `originalReference`. Use this when the sale, void, or refund never answered: you still have the reference you sent, and you do not yet have a receipt number. `transactionDate` is optional. On Wi-Fi and USB, `originalTerminalId` names another terminal.

```csharp
EcrInquiry inquiry = await session.InquireByReferenceAsync("ORDER-1001-1");
```

### E-receipt

Wi-Fi and USB. Returns a URL for a QR code. Safe to repeat. `Ready` carries `Url`. `Unavailable` means there is no receipt to show. `Failed` means the exchange failed; repeat the call.

```csharp
EcrReceipt receipt = await session.ReceiptAsync(
    receiptNumber: stan,
    transactionDate: "20261019");

if (receipt is EcrReceipt.Ready ready)
    ShowQr(ready.Url);
```

On Web Service the same call returns `EcrReceipt.Unavailable`. `session.SupportsReceipt` is false there.

### Close receipt

<a id="close-receipt"></a>

Asks the terminal to leave the receipt screen and return to idle. Call it from the till's Close receipt action when the cashier is finished. Sale, void, refund, and dismissing a dialog do not send it. Safe to repeat: `Idle` means the terminal is idle, whether a receipt was up or not.

```csharp
EcrReceiptClosed closed = await session.CloseReceiptAsync(
    merchantReference: "",
    originalMerchantReference: "ORDER-1001-1");
```

`originalMerchantReference` is the sale, void, or refund you are dismissing. Web Service requires it. Wi-Fi and USB ignore it. Both session arguments are strings, so pass them by name.

| Call                                                                                    | Sale's reference          |
| --------------------------------------------------------------------------------------- | ------------------------- |
| `EcrOpenedSession.CloseReceiptAsync(merchantReference, originalMerchantReference)`      | Second argument           |
| `EcrWebServiceTerminal.CloseReceiptAsync(originalMerchantReference, merchantReference)` | First argument            |
| `EcrTerminal.CloseReceiptAsync(merchantReference)`                                      | None. Wi-Fi and USB only. |

```csharp
switch (closed)
{
    case EcrReceiptClosed.Idle:
        break;
    case EcrReceiptClosed.Refused refused:
        Show(refused.Reason);          // firmware has no CLOSE_RECEIPT; leave the receipt up
        break;
    case EcrReceiptClosed.Failed failed:
        Show(failed.Failure.Message);  // nothing changed; the call can be repeated
        break;
}
```

### Reachability

Wi-Fi and USB, before a sale. Proves the port or cable answers. It does not prove the terminal is idle.

```csharp
EcrReachability? probe = await session.ProbeReachabilityAsync();
if (probe is { Reachable: false })
    Show(probe.Error);
```

Web Service returns `null`. There is no Hub probe. `ProbeTimeout` is 3 seconds.

## Read the result

<a id="handle-the-result"></a>

`SaleAsync`, `VoidAsync`, and `RefundAsync` return `EcrResult`.

```csharp
switch (result)
{
    case EcrResult.Approved approved:
        Record(approved.MerchantReference, approved.Amount, approved.Rrn, approved.AuthCode);
        if (approved.PartialApproval)
            CollectBalance(approved.RequestedAmount, approved.Amount);
        break;

    case EcrResult.Declined declined:
        Show(declined.Reason);
        break;

    case EcrResult.Failed failed when failed.Settled && failed.Recovered is EcrInquiry.Found found:
        Apply(found.Transaction);
        break;

    case EcrResult.Failed failed when failed.Failure.OutcomeUnknown:
        MarkUnresolved(failed.MerchantReference);
        break;

    case EcrResult.Failed failed:
        Show(failed.Failure.Message);
        break;
}
```

| Result                                | Meaning                                                                                                                                                                |
| ------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Approved`                            | Money moved. `Amount` is what was taken or returned. `ResponseCode` is `00` on a plain approval. `MaskedPan` is masked.                                                |
| `Approved` with `PartialApproval`     | The issuer authorised less than you asked. `RequestedAmount` is what you asked. Collect the difference before you hand over the goods.                                 |
| `Declined`                            | The terminal answered and refused. `Reason` is the text to show. `NextStep` is `None`.                                                                                 |
| `Failed` and `Failure.Unreachable`    | Nothing was sent. Wi-Fi and USB only.                                                                                                                                  |
| `Failed` and `Failure.OutcomeUnknown` | The request went out and the answer did not settle it: timeout, connection lost, malformed answer, or a signature that does not match. The payment may have completed. |
| `Failed.Settled`                      | Wi-Fi or USB already inquired. `Recovered` is `EcrInquiry.Found`. Record that transaction.                                                                             |
| `ArgumentException`                   | The call was rejected before anything was sent. A merchant reference longer than 32 characters, or one that contains a space, `&`, or `=`, throws here.                |
| `OperationCanceledException`          | Your token cancelled the wait. The terminal may still complete a request that already left.                                                                            |

An answer whose `nextStep` is `INQUIRE_BY_MERCHANT_REFERENCE` arrives as `Failed` (`Timeout` or `ConnectionLost`). That includes a Web Service timeout, a lost HTTP call, an empty or unreadable body, and an HTTP 5xx with nothing readable. `Failure.OutcomeUnknown` is false only for `Unreachable`.

### Inquiry

| Result     | Meaning                                                                                                                                            |
| ---------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Found`    | `Transaction` is the record. `Status` is the outcome (`Approved`, `Declined`, `Voided`, or other text from the backend). Compare it ignoring case. |
| `NotFound` | This lookup returned no record. `Reason` is the backend's text when it sent any.                                                                   |
| `Failed`   | The lookup itself failed. Repeat the inquiry. An inquiry does not move money.                                                                      |

On `Found`, `Transaction.Stan` is the receipt number. `CanVoid` and `CanRefund` say whether the terminal would accept that operation now. When `PartialApproval` is set, `Amount` is what was asked and `AuthorizedAmount` is what was charged.

## Important notes

### Resolve an unknown outcome

<a id="resolve-an-unknown-outcome"></a>

`Failure.OutcomeUnknown` means the payment may have completed. Inquire by the merchant reference of that attempt. Charge the order again only after `Transaction.Status` is `Declined`, or after the terminal confirms the attempt did not complete.

1. Save the reference before `SaleAsync`, `VoidAsync`, or `RefundAsync`.
2. On Wi-Fi and USB, read `Failed.Recovered` first. With `AutoInquireOnFailure` left on (the default), the SDK has already called `InquireByReferenceAsync` once. `EcrInquiry.Found` settles the attempt. `NotFound`, `Failed`, or null leaves it unknown. Web Service sets `Recovered` to null.
3. Call `InquireByReferenceAsync` with the saved reference.
4. Read `Found.Transaction.Status`, ignoring case.
   - `Approved` — the attempt completed. On a void or refund, the void or refund completed.
   - `Declined` — it did not. A new attempt needs a new reference.
   - Any other value, including `Voided` and `Reversed` — leave the order unresolved.
5. `NotFound` after a lost answer means this lookup returned no record. The same answer follows a terminal reset, or a record that has aged out. Confirm on the terminal, or with Amwal, before you take payment again.
6. `EcrInquiry.Failed` means the lookup failed. Inquire again.
7. On startup, inquire every attempt still marked unresolved.

`Failure.Unreachable` means the connection was never made. There is no attempt to look up.

### Merchant reference

32 characters maximum, with space, `&`, and `=` excluded. The SDK trims the value. Use one reference per attempt; an order number plus an attempt suffix fits. A void and a refund each get a new reference.

### Receipt number

`Approved` does not carry it. Read `Transaction.Stan` from an inquiry by merchant reference, or read the printed receipt. The SDK sends the number padded to 6 digits. Pass that receipt's day as `yyyyMMdd` to refund, inquiry by receipt number, and e-receipt. A receipt number is unique on one terminal for one day.

### Amounts and currency

`CurrencyCode` defaults to `"512"` and `MinorUnitDigits` to `3` (OMR). Another currency needs both. `MinorUnitDigits` accepts 0 to 4. The SDK rounds half away from zero to that many places, then sends minor units.

### Timeouts

| Transport                  | Wait                                                                                                                                                                                             |
| -------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Wi-Fi                      | `ConnectTimeout` 10 s, then `ResponseTimeout` 120 s. A lost answer can add one more response wait for the automatic inquiry.                                                                     |
| USB                        | The till waits 120 seconds for the answer, the same as Wi-Fi.                                                                                                                                    |
| Web Service through `Plan` | A `ResponseTimeout` of 120 s becomes `EcrConfig.WebServiceResponseTimeout` (130 s). Any other value is kept. The HTTP deadline is `ConnectTimeout` + `ResponseTimeout`: 130 s with the defaults. |

`new EcrWebServiceTerminal(...)` skips the 130 s raise. Set `ResponseTimeout = EcrConfig.WebServiceResponseTimeout` on that config.

The default wait covers card present and PIN. Cancelling `CancellationToken` ends the wait in your process. A request that already reached the terminal can still complete.

### Keys

Even-length hex, at least 16 characters. Assigning an invalid key on `EcrConfig` throws `ArgumentException`. Pass operator-typed text as `rawSecureHashKey` to `Plan` and the same problem is listed in `Issues`.

`Open` requires a key. `new EcrTerminal(...)` and `new EcrWebServiceTerminal(...)` with an empty key send unsigned requests and skip the answer check.

Wi-Fi and USB sign the field `secureHash`. Web Service signs `secureHashValue`. The two secrets are issued separately. `plan.SigningKeyLabel` names the one this plan expects.

### Logging

```csharp
IEcrLogger logger = EcrLogger.From(line => Trace.WriteLine(line));
EcrOpenedSession session = EcrSessions.Open(serial, plan, logger: logger);
```

Logging is off until you pass a logger. `EcrLogger.None` discards every line. Each line is the request or the response: masked PAN, amount, and reference. An exception thrown by the sink escapes the call.

## When you build the message yourself

`EcrTransactionType` is `Sale`, `Void`, `Refund`, `Inquiry`, `Receipt`, `CloseReceipt`. `EcrTransactionTypes.MenuOptions` is the operator menu: sale, void, refund, inquiry. `RequiresAmount`, `RequiresOriginalStan`, `RequiresOriginalDate`, `MovesMoney`, and `AllowsOtherTerminal` say which fields that operation needs. `EcrTerminal.RunAsync` accepts only the three that move money.

`EcrWebServiceSaleRequest`, `EcrWebServiceVoidRequest`, `EcrWebServiceRefundRequest`, `EcrWebServiceTransactionStatusRequest`, and `EcrWebServiceCloseReceiptRequest` are for a caller that posts the JSON. `EcrWebServiceRequestJson` writes each body and signs it when you pass the key. The session methods do this themselves.
