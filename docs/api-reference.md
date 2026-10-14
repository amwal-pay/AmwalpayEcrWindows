# API reference

Every public type in `AmwalPay.Ecr` and `AmwalPay.Ecr.WebService`, for package
**1.0.2**. Everything else (the `Internal` namespace, the HTTP client, routes and
signing helpers) is `internal`. For how to use these together, see the
[integration guide](integration-guide.md).

**Conventions**

- Operations are `async` and return `Task<T>`; names end in `Async`.
- Every asynchronous method takes a trailing `CancellationToken cancellationToken = default`.
  Cancelling stops the wait, **not** the transaction; see
  [Threading and lifecycle](integration-guide.md#9-threading-and-lifecycle).
- Amounts you send are `decimal`. Amounts you receive are `string`s in major units,
  invariant culture (`"1.234"`).
- Results, links, plans and config are `record`s (value equality, `with`). Use
  **C# 9 or later**.
- Nullable reference types are enabled; `?` means "may be null".
- Argument mistakes throw `ArgumentException` (raised on `await`, before a connection
  is opened). Runtime problems become results.

**Contents**

- [EcrSessions](#ecrsessions)
- [EcrLink](#ecrlink)
- [EcrSessionPlan](#ecrsessionplan)
- [EcrOpenedSession](#ecropenedsession)
- [EcrTerminal](#ecrterminal)
- [EcrWebServiceTerminal](#ecrwebserviceterminal)
- [Merchant reference](#merchant-reference)
- [EcrConfig](#ecrconfig)
- [EcrEnvironment](#ecrenvironment)
- [EcrTransactionType](#ecrtransactiontype)
- [EcrResult](#ecrresult)
- [EcrInquiry](#ecrinquiry) and [EcrTransaction](#ecrtransaction)
- [EcrReceipt](#ecrreceipt)
- [EcrReceiptClosed](#ecrreceiptclosed)
- [Failure](#failure)
- [NextStep](#nextstep)
- [EcrReachability](#ecrreachability)
- [IEcrChannel](#iecrchannel), [TcpEcrChannel](#tcpecrchannel), [EcrChannelTimeout](#ecrchanneltimeout), [EcrFrames](#ecrframes)
- [IEcrLogger and EcrLogger](#iecrlogger-and-ecrlogger)
- [EcrWireResponse](#ecrwireresponse)
- [Web Service request types](#web-service-request-types)
- [Appendix: Android SDK name differences](#appendix-android-sdk-name-differences)

---

## EcrSessions

```csharp
public static class EcrSessions
```

Validates an [EcrLink](#ecrlink) and [EcrConfig](#ecrconfig), then builds the right
client, so apps do not duplicate transport rules, key labels or ID checks.

### Plan

```csharp
public static EcrSessionPlan Plan(
    EcrLink link,
    EcrConfig config,
    string? rawSecureHashKey = null)
```

Validates without opening a connection and returns an
[EcrSessionPlan](#ecrsessionplan) whose `Issues` list describes anything wrong
(blank IP, non-numeric merchant ID, malformed or missing key) instead of throwing.

- `rawSecureHashKey` is a key the user typed that might fail `EcrConfig`'s setter
  (odd length, non-hex). It defaults to `config.SecureHashKey`, is trimmed, and when
  invalid is reported as an issue and sanitised to empty. It is the same secret, not a
  second one.
- For [EcrLink.Lan](#ecrlink), the resolved config's `Port` comes from the link.
- For [EcrLink.WebService](#ecrlink), `MerchantId` and `TerminalId` are copied from
  the link, and a `ResponseTimeout` still at the 120 s default is raised to
  `EcrConfig.WebServiceResponseTimeout` (130 s). Any other value is kept.
- **A missing key is always an issue**, for every link (`"Wi-Fi/LAN secure hash key
  is not configured"` or `"Web Service secure hash key is not configured"`), so a
  plan with no key is never ready.

**Throws** `ArgumentNullException` if `link` or `config` is null.

### Open

```csharp
public static EcrOpenedSession Open(
    string terminalSerial,
    EcrSessionPlan plan,
    Func<IEcrChannel>? usbChannel = null,
    IEcrLogger? logger = null)
```

Opens the transport `plan` describes (LAN, USB cable or Web Service) and returns an
[EcrOpenedSession](#ecropenedsession). `usbChannel` is required for a USB plan and
ignored otherwise; the package ships no USB channel.

**Throws** `ArgumentException` if `plan.IsReady` is false; `InvalidOperationException`
for a USB plan with no factory (or a factory that returns null).

### LanTerminal, UsbCableTerminal, WebServiceTerminal

```csharp
public static EcrTerminal LanTerminal(string terminalSerial, EcrSessionPlan plan, IEcrLogger? logger = null)
public static EcrTerminal UsbCableTerminal(string terminalSerial, EcrSessionPlan plan, IEcrChannel channel, IEcrLogger? logger = null)
public static EcrWebServiceTerminal WebServiceTerminal(string terminalSerial, EcrSessionPlan plan, IEcrLogger? logger = null)
```

Each requires a plan for its own link (`UsesLan`, `UsesUsbCable`, `UsesWebService`),
otherwise `ArgumentException`. Above the channel, `UsbCableTerminal` is identical to
`LanTerminal`: the same signing, messages and answers.

> **None of these three check `plan.IsReady`; only `Open` does.** A plan that is not
> ready may carry an empty key, which builds an unsigned terminal. Test
> `plan.IsReady` first.

---

## EcrLink

```csharp
public abstract record EcrLink
{
    public sealed record Lan(string Host, int Port = EcrConfig.DefaultPort) : EcrLink;
    public sealed record UsbCable : EcrLink;
    public sealed record WebService(string MerchantId, string TerminalId) : EcrLink;
}
```

How this till reaches a terminal. The hierarchy is closed: only these three exist.

- **`Lan`**: TCP to `Host`:`Port` (default 9100).
- **`UsbCable`**: a cable with no address. Construct with `new EcrLink.UsbCable()` and
  build the terminal with [UsbCableTerminal](#lanterminal-usbcableterminal-webserviceterminal)
  or [Open](#open), passing an [IEcrChannel](#iecrchannel) for your hardware.
- **`WebService`**: HTTPS REST to the Amwal Hub. `MerchantId` and `TerminalId` must be
  numeric strings. Hub URLs come from [EcrConfig.Environment](#ecrconfig); the app
  does not configure them.

---

## EcrSessionPlan

```csharp
public sealed record EcrSessionPlan(
    EcrLink Link,
    EcrConfig Config,
    IReadOnlyList<string>? Issues = null)
```

Validated configuration for one terminal link, built by
[EcrSessions.Plan](#plan).

| Property | Description |
|---|---|
| `IReadOnlyList<string> Issues` | What is wrong, or empty. Never null |
| `bool IsReady` | `Issues.Count == 0` |
| `bool UsesLan` / `UsesWebService` / `UsesUsbCable` | Which client to build |
| `string SigningKeyLabel` | Label used in the missing-key issue: Wi-Fi/LAN vs Web Service |
| `bool SigningKeyConfigured` | Whether a non-empty key is present |
| `string ConnectionSummary` | Operator-facing, no secrets: `192.168.1.50:9100`, `USB cable`, or `UAT · merchant 13593 · terminal 101311` |

---

## EcrOpenedSession

```csharp
public sealed class EcrOpenedSession
```

One opened transport for a validated plan, from [EcrSessions.Open](#open). It has no
public constructor. It exposes one set of calls whichever transport is behind it, but
several arguments only mean something on one transport.

| Member | Description |
|---|---|
| `EcrSessionPlan Plan` | The plan it was opened from |
| `bool UsesLocalTerminal` | LAN or USB cable (an `EcrTerminal` is behind it) |
| `bool UsesWebService` | An `EcrWebServiceTerminal` is behind it |
| `bool SupportsReceipt` | Same as `UsesLocalTerminal`: receipt URLs exist only on the local protocol |

```csharp
public Task<EcrReachability?> ProbeReachabilityAsync(CancellationToken cancellationToken = default)
public Task<EcrResult> SaleAsync(decimal amount, string merchantReference = "", CancellationToken cancellationToken = default)
public Task<EcrResult> VoidAsync(string receiptNumber, string originalTerminalId = "", string merchantReference = "", CancellationToken cancellationToken = default)
public Task<EcrResult> RefundAsync(decimal amount, string receiptNumber, string transactionDate, string originalTerminalId = "", string merchantReference = "", CancellationToken cancellationToken = default)
public Task<EcrInquiry> InquireAsync(string receiptNumber, string transactionDate, string originalTerminalId = "", CancellationToken cancellationToken = default)
public Task<EcrInquiry> InquireByReferenceAsync(string originalReference, string transactionDate = "", string originalTerminalId = "", string merchantReference = "", CancellationToken cancellationToken = default)
public Task<EcrReceipt> ReceiptAsync(string receiptNumber, string transactionDate, string originalTerminalId = "", CancellationToken cancellationToken = default)
public Task<EcrReceiptClosed> CloseReceiptAsync(string merchantReference = "", string originalMerchantReference = "", CancellationToken cancellationToken = default)
```

Differences to know:

- **`ProbeReachabilityAsync` returns `null` on Web Service**; there is no probe.
- **`originalTerminalId` is ignored on Web Service** (void, refund, inquiry). Check
  `UsesLocalTerminal` before relying on it.
- **`ReceiptAsync` on Web Service** calls nothing and returns `EcrReceipt.Unavailable`
  with an empty `MerchantReference` and `Raw`, reason `"Receipt fetch is only
  supported over Wi-Fi and USB cable ECR"`.
- **`InquireByReferenceAsync` on Web Service** looks up by `originalMerchantReference`
  with an empty `stan`. `originalReference` is the transaction being looked up;
  `merchantReference` is the inquiry's own id, kept separate.
- **`InquireAsync` and `ReceiptAsync` take no `merchantReference`** here, unlike
  `EcrTerminal`'s. The fourth positional argument is the `CancellationToken`; use
  named arguments.
- **`CloseReceiptAsync`** (1.0.2): on LAN / USB it sends `CLOSE_RECEIPT` and
  `originalMerchantReference` is ignored. On Web Service it posts `/Ecr/closeReceipt`
  and needs `originalMerchantReference` (the sale / void / refund reference). If only
  `merchantReference` is given on Web Service it is treated as the original and the
  SDK generates a fresh id for the close request. If neither is given the result is
  `EcrReceiptClosed.Failed` with `Failure.Malformed` and nothing is sent. **The
  argument order differs from `EcrWebServiceTerminal.CloseReceiptAsync`**; use named
  arguments.
- There is no `RunAsync`.

---

## EcrTerminal

```csharp
public sealed class EcrTerminal
```

A terminal reachable over the network, or over any link an
[IEcrChannel](#iecrchannel) can carry. One instance addresses one terminal, holds no
connection between calls, and is safe to share across threads.

### Constructors

```csharp
public EcrTerminal(IEcrChannel channel, string serialNumber = "", EcrConfig? config = null, IEcrLogger? logger = null)
public EcrTerminal(string host, string serialNumber = "", EcrConfig? config = null, IEcrLogger? logger = null)
```

The second builds a [TcpEcrChannel](#tcpecrchannel) for `host` using `config.Port` and
`config.ConnectTimeout`. `serialNumber` is what the operator registered; the terminal
shows its own address on screen. `config` defaults to `new EcrConfig()`.

### IsReachableAsync and ProbeReachabilityAsync

```csharp
public Task<bool> IsReachableAsync(CancellationToken cancellationToken = default)
public Task<EcrReachability> ProbeReachabilityAsync(CancellationToken cancellationToken = default)
```

Ask the channel whether the terminal is there before sending anything, waiting up to
`config.ProbeTimeout`. For TCP that is a handshake to `host:config.Port`.
`ProbeReachabilityAsync` also returns the platform error when the probe fails. A probe
proves the port is open, not that the terminal is idle.

### SaleAsync

```csharp
public Task<EcrResult> SaleAsync(decimal amount, string merchantReference = "", CancellationToken cancellationToken = default)
```

Takes a payment; the cardholder presents their card. Completes when the terminal
answers: normally seconds, at most `ResponseTimeout`. `amount` is rounded to
`config.MinorUnitDigits` places (away from zero at the midpoint) and not
range-checked. Use the `m` suffix: `1.234m`.

### VoidAsync

```csharp
public Task<EcrResult> VoidAsync(string receiptNumber, string originalTerminalId = "", string merchantReference = "", CancellationToken cancellationToken = default)
```

Cancels an earlier transaction **in full**, by the receipt number printed on it. No
amount, and no card for a transaction taken on this terminal. `originalTerminalId` is
the terminal the original was taken on, when not this one. `merchantReference` names
the void, not the transaction cancelled. Refusals (not found, outside the void
window, not voidable) arrive as `Declined`.

**Throws** `ArgumentException` if `receiptNumber` is blank.

### RefundAsync

```csharp
public Task<EcrResult> RefundAsync(decimal amount, string receiptNumber, string transactionDate, string originalTerminalId = "", string merchantReference = "", CancellationToken cancellationToken = default)
```

Returns money against an earlier transaction, in full or in part. The cardholder
presents their card. `transactionDate` is the day the original was taken
(`yyyyMMdd`), required because a receipt number is only unique within a terminal's
day.

**Throws** `ArgumentException` if `receiptNumber` is blank.

### InquireAsync

```csharp
public Task<EcrInquiry> InquireAsync(string receiptNumber, string transactionDate, string originalTerminalId = "", string merchantReference = "", CancellationToken cancellationToken = default)
```

Asks what became of an earlier transaction without touching it. Safe to repeat and
answered **even while the terminal is taking a payment**. `merchantReference` names
the inquiry itself; it is not a way to look a transaction up.

**Throws** `ArgumentException` if `receiptNumber` is blank.

### InquireByReferenceAsync

```csharp
public Task<EcrInquiry> InquireByReferenceAsync(string originalReference, string transactionDate = "", string originalTerminalId = "", string merchantReference = "", CancellationToken cancellationToken = default)
```

Looks up a transaction by the merchant reference it was sent with. This is the answer
to an outcome you never received: a receipt number arrives *in* the terminal's answer,
so a lost answer leaves you without one. `transactionDate` is optional because a
reference is not scoped to a day.

**Throws** `ArgumentException` if `originalReference` is blank or breaks the
[reference rules](#merchant-reference).

### ReceiptAsync

```csharp
public Task<EcrReceipt> ReceiptAsync(string receiptNumber, string transactionDate, string originalTerminalId = "", string merchantReference = "", CancellationToken cancellationToken = default)
```

Fetches an earlier transaction's e-receipt as a URL for a QR code. Non-financial and
reprintable.

**Throws** `ArgumentException` if `receiptNumber` is blank.

### CloseReceiptAsync

```csharp
public Task<EcrReceiptClosed> CloseReceiptAsync(string merchantReference = "", CancellationToken cancellationToken = default)
```

*Requires 1.0.2.* Asks the terminal to put its receipt away and return to idle
(`CLOSE_RECEIPT`). Call it from the till's explicit Close receipt action, not as a
side effect of dismissing a dialog. A terminal too old to know the request answers
`EcrReceiptClosed.Refused`; treat that as "cannot be asked" and carry on. Safe to
repeat. `merchantReference` is the request's own id (generated when blank).

### RunAsync

```csharp
public Task<EcrResult> RunAsync(EcrTransactionType type, decimal? amount = null, string originalStan = "", string originalTerminalId = "", string originalDate = "", string merchantReference = "", CancellationToken cancellationToken = default)
```

Runs any operation that moves money, for a caller driving the type from its own UI.

**Throws** `ArgumentException` when the type needs something missing (an amount, a
receipt number), or when given `Inquiry`, `Receipt` or `CloseReceipt`, which answer
with something found or acknowledged and have their own methods. (The message for
those says "is run with inquire()".)

### When an answer is lost

For `SaleAsync`, `VoidAsync`, `RefundAsync` and `RunAsync`, a lost exchange becomes
`EcrResult.Failed`. When [EcrConfig.AutoInquireOnFailure](#ecrconfig) is `true`, the
failure has `Failure.OutcomeUnknown` and a reference exists, the SDK makes one
`InquireByReferenceAsync` and attaches the finding as `Failed.Recovered`. A
`CancellationToken` cancellation is never turned into a result: it throws
`OperationCanceledException` and no follow-up runs.

---

## EcrWebServiceTerminal

```csharp
public sealed class EcrWebServiceTerminal
{
    public EcrWebServiceTerminal(string terminalSerial, EcrConfig? config = null, IEcrLogger? logger = null)
}
```

Web Service ECR client: REST over HTTPS with JSON body signing, for terminals with
`ecrMode: 4`. Prefer [EcrSessions.WebServiceTerminal](#lanterminal-usbcableterminal-webserviceterminal)
after `Plan` reports `IsReady`. It needs numeric `config.MerchantId` and
`config.TerminalId` and the **Web Service** key on `config.SecureHashKey`; if the IDs
are not numeric, a call returns `Failed` with `Failure.Malformed` and sends nothing.
Building it directly does not apply the 130 s `ResponseTimeout` that `Plan` applies;
set `ResponseTimeout = EcrConfig.WebServiceResponseTimeout` yourself.

```csharp
public Task<EcrResult> SaleAsync(decimal amount, string merchantReference = "", CancellationToken cancellationToken = default)
public Task<EcrResult> VoidAsync(string receiptNumber, string merchantReference = "", CancellationToken cancellationToken = default)
public Task<EcrResult> RefundAsync(decimal amount, string receiptNumber, string transactionDate, string merchantReference = "", CancellationToken cancellationToken = default)
public Task<EcrInquiry> InquireAsync(string receiptNumber, string transactionDate, string merchantReference = "", CancellationToken cancellationToken = default)
public Task<EcrInquiry> InquireByReferenceAsync(string originalReference, string transactionDate, string merchantReference = "", CancellationToken cancellationToken = default)
public Task<EcrReceiptClosed> CloseReceiptAsync(string originalMerchantReference, string merchantReference = "", CancellationToken cancellationToken = default)
```

**The argument lists differ from `EcrTerminal`'s.** There is no `originalTerminalId`,
so the second positional argument of `VoidAsync` is a `merchantReference`:
`hub.VoidAsync("215", "31629")` names the void `31629`. Use named arguments.
`transactionDate` on `InquireByReferenceAsync` is required here (pass `""`), optional
on LAN. `CloseReceiptAsync` takes the original's reference **first**.

There is no `ReceiptAsync`, `RunAsync`, `IsReachableAsync` or
`ProbeReachabilityAsync`, no response signature verification, and no auto-inquiry
(`Failed.Recovered` is always `null`). Void, refund and inquiry send
`originalTerminalId` equal to the request's own `terminalId`.

### CloseReceiptAsync

*Requires 1.0.2.* Posts `/Ecr/closeReceipt` for the sale / void / refund named by
`originalMerchantReference`; `merchantReference` is this request's own id (generated
when blank). The body has no `messageType` or `currencyCode` and is signed like every
other Hub call (see [protocol](protocol.md#9-web-service-rest)). Never sent
automatically.

A blank `originalMerchantReference`, or non-numeric IDs, return
`EcrReceiptClosed.Failed` with `Failure.Malformed` and send nothing. Because a
transport error is read from a `success: false` envelope, it arrives as
`EcrReceiptClosed.Refused` with `ResponseCode == "NO_RESPONSE"`, not as `Failed`.

### How a Web Service answer maps to a result

The HTTP layer never throws for a network problem. A timeout, no network, a reset, an
empty body or a body that is not JSON comes back as a synthesised
`{ "success": false, "responseCode": ..., "message": ... }` envelope. When the request
may have reached the Hub, the envelope carries
`"nextStep": "INQUIRE_BY_MERCHANT_REFERENCE"`, and the SDK reads that as an unknown
outcome: `EcrResult.Failed`, never `Declined`.

| What happened | Sale / void / refund | Inquiry / inquiry by reference |
|---|---|---|
| Timeout, no network, reset (`NO_RESPONSE`) | `Failed`, `Failure.Timeout` | `EcrInquiry.Failed`, `Failure.ConnectionLost` |
| Empty or non-JSON body | `Failed`, `Failure.ConnectionLost` (`Timeout` if the text says timeout) | `Failed`, `ConnectionLost` |
| HTTP 5xx with no readable `message` / `errorList` | `Failed`, `ConnectionLost` (`Timeout` for e.g. 504 "Gateway Timeout") | `Failed`, `ConnectionLost` |
| HTTP 4xx with no readable `message` / `errorList` | `Declined`, `ResponseCode` = the status, `NextStep.None` | `NotFound` |
| Non-2xx with a readable JSON `message` / `errorList` | `Declined` from the Hub's own answer | `NotFound` or `Found` per the body |
| Hub answered normally | `Approved` / `Declined` | `Found` / `NotFound` |

Mapping rule for the first three rows: a synthesised envelope with
`responseCode == "NO_RESPONSE"` or text containing "timeout" becomes
`Failure.Timeout`; otherwise `Failure.ConnectionLost`. `EcrInquiry.NotFound` is a
real answer from the Hub; a failed lookup is `EcrInquiry.Failed` (still unknown, ask
again). The request has one deadline of `ConnectTimeout + ResponseTimeout`.

A found answer may carry the transaction nested under `data` or as flat fields at the
top level; `data` holding only a merchant reference or serial is not a found record.

---

## Merchant reference

Every operation takes an optional `merchantReference`, and every outcome reports one.
Pass your own order or basket number and the same string identifies the transaction
in your books, the terminal's records and any later inquiry. Leave it out and the SDK
generates one (12 uppercase hex characters, e.g. `A1B2C3D4E5F6`) and reports it on
the result; store it.

| | |
|---|---|
| Length | 1-32 characters |
| Characters | Printable ASCII, no spaces, not `&` or `=` |
| Uniqueness | Yours to decide; the terminal does not check it |

Surrounding whitespace is trimmed and an all-whitespace reference counts as none.
Anything else outside the rules throws `ArgumentException` before a connection is
opened. Pass your own if a cancellation or crash can interrupt the call.

On the wire this field was once called `requestId`; a terminal still accepts and
echoes the old name.

---

## EcrConfig

```csharp
public sealed record EcrConfig
{
    public const int DefaultPort = 9100;
    public static readonly TimeSpan WebServiceResponseTimeout;   // 130 seconds

    public string EcrId { get; init; }                   // "ECR01"
    public string CurrencyCode { get; init; }            // "512"
    public int MinorUnitDigits { get; init; }            // 3
    public int Port { get; init; }                       // 9100
    public TimeSpan ConnectTimeout { get; init; }        // 10 seconds
    public TimeSpan ResponseTimeout { get; init; }       // 120 seconds
    public TimeSpan ProbeTimeout { get; init; }          // 3 seconds
    public string SecureHashKey { get; init; }           // "" (unsigned)
    public string MerchantId { get; init; }              // ""
    public string TerminalId { get; init; }              // ""
    public EcrEnvironment Environment { get; init; }     // EcrEnvironments.Default (SIT)
    public bool AutoInquireOnFailure { get; init; }      // true

    public static bool IsValidSecureHashKey(string? key)
}
```

No parameterised constructor: use an object initializer and `with`. **C# 9 or later**
is required to assign `init` properties.

```csharp
var config = new EcrConfig { EcrId = "TILL-01", SecureHashKey = "0123456789ABCDEF..." };
var stricter = config with { ResponseTimeout = TimeSpan.FromSeconds(60) };
```

The defaults describe an Omani deployment on the standard port.

| Property | Meaning |
|---|---|
| `EcrId` | Identifies this cash register on the terminal's records |
| `CurrencyCode` | ISO 4217 numeric. `512` is OMR |
| `MinorUnitDigits` | Decimal places of the currency; decides how `1.234` is written on the wire |
| `Port` | The terminal's ECR listener port (LAN) |
| `ConnectTimeout` | Wait for the terminal to accept a connection. A timeout is `Failure.Unreachable` (nothing was sent). On Web Service it is added to `ResponseTimeout` |
| `ResponseTimeout` | Wait for the result once the request is sent. Generous because the cardholder has to present a card and may enter a PIN. A timeout is `Failure.Timeout`. For Web Service, [Plan](#plan) raises the 120 s default to `WebServiceResponseTimeout` (130 s) |
| `ProbeTimeout` | How long a probe waits |
| `SecureHashKey` | Signing secret as hex, supplied by the app for the selected mode. LAN and Web Service use different secrets and field names but both go through this property. Empty means unsigned (and a plan is not ready) |
| `MerchantId` / `TerminalId` | Backend IDs for Web Service. Normally merged from [EcrLink.WebService](#ecrlink) by `Plan` |
| `Environment` | Hub deployment for Web Service; URLs are SDK-internal |
| `AutoInquireOnFailure` | When `true`, **[EcrTerminal](#ecrterminal) only** follows an unknown failure with `InquireByReferenceAsync(MerchantReference)` and attaches the finding to `Failed.Recovered`. Ignored by `EcrWebServiceTerminal` |

**Throws** `ArgumentException` **at assignment** if `MinorUnitDigits` is outside 0-4,
`Port` outside 1-65535, or `SecureHashKey` is neither empty nor an even-length hex
string of at least 16 characters. Timeouts are not validated. `ToString()` redacts a
non-empty key as `<redacted>`.

`IsValidSecureHashKey(key)` is true for null, empty, or even-length hex of at least 16
characters; use it in settings UI before persisting a key.

### Signing

The app persists secrets and chooses which one goes on `EcrConfig`; the SDK only
signs and verifies with it. **LAN ECR** adds `secureHash` and a per-request `nonce`
and requires the answer to be signed and to echo the nonce. **Web Service ECR** adds
`secureHashValue` to the JSON body. Keys are issued per terminal and per transport.
Mismatched keys give `Failure.Unauthenticated` on every LAN call; Web Service calls
fail at the Hub. Details: [protocol](protocol.md#signing).

### MinorUnitDigits

Amounts travel as a whole number of the currency's smallest unit:

| Currency | Digits | Goes out as |
|---|---|---|
| OMR (1 rial = 1000 baisa) | 3 | `1.234` becomes `000000001234` |
| USD (1 dollar = 100 cents) | 2 | `1.23` becomes `000000000123` |

Nothing cross-checks the digits against `CurrencyCode`. Amounts you receive are
converted back to major units.

---

## EcrEnvironment

```csharp
public enum EcrEnvironment { SIT, UAT, PROD }

public static class EcrEnvironments
{
    public const EcrEnvironment Default = EcrEnvironment.SIT;
    public static EcrEnvironment FromName(string? name)
}
```

Deployment environment for Web Service ECR, set on `EcrConfig.Environment`. Hub
hostnames and ports are SDK-internal and not documented; the app only selects the
enum. `FromName` parses a persisted name, ignoring case and surrounding whitespace;
unknown values and null fall back to `Default`.

---

## EcrTransactionType

```csharp
public enum EcrTransactionType { Sale, Void, Refund, Inquiry, Receipt, CloseReceipt }
```

`CloseReceipt` was added in 1.0.2; an exhaustive `switch` over this enum needs a new
case. The flags are extension methods on the static class `EcrTransactionTypes`:

```csharp
public static IReadOnlyList<EcrTransactionType> MenuOptions { get; }   // Sale, Void, Refund, Inquiry

public static string MessageType(this EcrTransactionType type)   // "SALE", "VOID", "REFUND", "INQUIRY", "RECEIPT", "CLOSE_RECEIPT"
public static string DisplayName(this EcrTransactionType type)   // "Sale", ..., "Close receipt"
public static bool RequiresAmount(this EcrTransactionType type)
public static bool RequiresOriginalStan(this EcrTransactionType type)
public static bool RequiresOriginalDate(this EcrTransactionType type)
public static bool MovesMoney(this EcrTransactionType type)
public static bool AllowsOtherTerminal(this EcrTransactionType type)
```

| | `RequiresAmount` | `RequiresOriginalStan` | `RequiresOriginalDate` | `AllowsOtherTerminal` | `MovesMoney` |
|---|---|---|---|---|---|
| `Sale` | yes | | | | yes |
| `Void` | | yes | | yes | yes |
| `Refund` | yes | yes | yes | yes | yes |
| `Inquiry` | | yes | yes | yes | |
| `Receipt` | | yes | yes | yes | |
| `CloseReceipt` | | | | | |

`MenuOptions` is what an operator picks from; `Receipt` and `CloseReceipt` follow a
transaction already on screen. What the terminal will accept (original exists, within
the void window, voidable) is decided by the terminal, not here.

---

## EcrResult

```csharp
public abstract record EcrResult(string MerchantReference)
```

How a transaction ended. The three cases are distinct because they call for different
things from a till. C# cannot prove a `switch` expression exhaustive, so give it a
final discard arm.

### EcrResult.Approved

```csharp
public sealed record Approved(
    string MerchantReference, string Amount, string ResponseCode, string Rrn,
    string AuthCode, string MaskedPan, bool PartialApproval, string RequestedAmount,
    string Raw) : EcrResult(MerchantReference)
```

Money was taken.

| Property | Description |
|---|---|
| `Amount` | What was **actually taken**, major units, e.g. `1.234` |
| `ResponseCode` | The backend's code, `00` on a plain approval |
| `Rrn` | Retrieval reference number |
| `AuthCode` | Authorisation code. A placeholder on a void; do not print it |
| `MaskedPan` | Masked, never the full number (`543173xxxx5785`). May be empty |
| `PartialApproval` | The bank authorised less than asked. Not a refusal |
| `RequestedAmount` | What was asked for, when `PartialApproval` is set |
| `Raw` | The terminal's full answer as JSON text |

### EcrResult.Declined

```csharp
public sealed record Declined(
    string MerchantReference, string ResponseCode, string Reason,
    NextStep NextStep, string Raw) : EcrResult(MerchantReference)
```

The terminal or Hub answered and no money was taken. `Reason` carries the backend's
own words (its error list in preference to its generic message). Any answer carrying
`nextStep: INQUIRE_BY_MERCHANT_REFERENCE` is returned as [Failed](#ecrresultfailed),
so `NextStep` is `None` on every `Declined` this SDK builds; the terminal's codes `91`
and `94` arrive here and call for an inquiry rather than a retry.

### EcrResult.Failed

```csharp
public sealed record Failed(
    string MerchantReference, Failure Failure, EcrInquiry? Recovered = null)
    : EcrResult(MerchantReference)
{
    public NextStep NextStep { get; }   // always InquireByMerchantReference
    public bool Settled { get; }        // Recovered is EcrInquiry.Found
}
```

The exchange itself failed, so the outcome is unknown. A `Failure.Timeout` does not
mean the transaction did not happen. `Recovered` is the follow-up inquiry on LAN / USB
with `AutoInquireOnFailure`, otherwise `null` (always `null` on Web Service).
`Settled` means the transaction was **found**; read its `Status`, because a found sale
can still have been declined. A Web Service transport failure is a `Failed` with
`Failure.Timeout` or `Failure.ConnectionLost`; see
[how a Web Service answer maps to a result](#how-a-web-service-answer-maps-to-a-result).

---

## EcrInquiry

```csharp
public abstract record EcrInquiry(string MerchantReference)
{
    public sealed record Found(string MerchantReference, EcrTransaction Transaction, string Raw)
    public sealed record NotFound(string MerchantReference, string Reason, string Raw)
    public sealed record Failed(string MerchantReference, Failure Failure)
}
```

The answer to "what became of this transaction". It reports on a transaction rather
than performing one: a successful inquiry says nothing about whether money moved;
that is `EcrTransaction.Status`.

| Case | Meaning |
|---|---|
| `Found` | The transaction was found. Read `Transaction.Status` |
| `NotFound` | No such transaction on that terminal for that day; a real answer. `Reason` is the backend's words |
| `Failed` | The exchange failed, so nothing was learned. Harmless to retry; an inquiry changes nothing |

---

## EcrTransaction

```csharp
public sealed record EcrTransaction(
    string TransactionId, string Stan, string Type, string Status, string Amount,
    string TotalAmount, string Currency, string TransactionTime, string MaskedPan,
    string CardHolderName, string Rrn, string AuthCode, string BatchId, string TerminalId,
    bool IsRefunded, bool CanVoid, bool CanRefund,
    bool PartialApproval = false, string AuthorizedAmount = "")
```

A transaction as the terminal's backend records it.

| Property | Description |
|---|---|
| `TransactionId` | The backend's identifier, unique across terminals and days |
| `Stan` | The receipt number, unique only within a terminal's day |
| `Type` | As the backend names it (e.g. `Purchase`, `Void`, `Refund`) |
| `Status` | What became of it: `Approved`, `Declined`, `Voided` |
| `Amount` / `TotalAmount` | Major units. `Amount` is what was requested; `TotalAmount` includes tips and fees |
| `Currency` | e.g. `OMR` |
| `TransactionTime` | As the backend recorded it, e.g. `2026-08-09T16:58:16.804271` |
| `MaskedPan` / `CardHolderName` | Never the full number. May be empty |
| `Rrn` / `AuthCode` / `BatchId` / `TerminalId` | As recorded |
| `IsRefunded` | Already refunded |
| `CanVoid` / `CanRefund` | What the terminal would still accept; the void window and backend rules are already applied |
| `PartialApproval` | The issuer approved less than asked |
| `AuthorizedAmount` | What was actually authorised. Equal to `Amount` unless partially approved |

---

## EcrReceipt

```csharp
public abstract record EcrReceipt(string MerchantReference)
{
    public sealed record Ready(string MerchantReference, string Url, string Raw)
    public sealed record Unavailable(string MerchantReference, string Reason, string Raw)
    public sealed record Failed(string MerchantReference, Failure Failure)
}
```

| Case | Meaning |
|---|---|
| `Ready` | Put `Url` in a QR code as it stands |
| `Unavailable` | Not found, or the backend would not publish one (an empty `receiptUrl` is never a receipt); also what `EcrOpenedSession.ReceiptAsync` returns on Web Service. Harmless |
| `Failed` | The exchange failed. Safe to retry |

---

## EcrReceiptClosed

```csharp
public abstract record EcrReceiptClosed(string MerchantReference)
{
    public sealed record Idle(string MerchantReference, string Raw)
    public sealed record Refused(string MerchantReference, string ResponseCode, string Reason, string Raw)
    public sealed record Failed(string MerchantReference, Failure Failure)
}
```

The answer to `CloseReceiptAsync` (1.0.2). Success is read from the envelope alone.

| Case | Meaning |
|---|---|
| `Idle` | The terminal is on its idle screen, whether or not it had a receipt up. Safe to repeat |
| `Refused` | The terminal or Hub answered `success: false` (for example firmware too old for `CLOSE_RECEIPT`, or a Web Service transport error with `ResponseCode == "NO_RESPONSE"`). Treat as "cannot be asked" and carry on |
| `Failed` | The exchange failed, or Web Service was asked with no original reference. Nothing was changed; safe to retry |

---

## Failure

```csharp
public abstract record Failure(string Message)
{
    public bool OutcomeUnknown { get; }   // false only for Unreachable

    public sealed record Unreachable(string Message)
    public sealed record Timeout(string Message)
    public sealed record Malformed(string Message)
    public sealed record ConnectionLost(string Message)
    public sealed record Unauthenticated(string Message)
}
```

Why an exchange could not be completed.

| Case | Meaning | Payment happened? |
|---|---|---|
| `Unreachable` | Nothing is listening: wrong address, terminal off, different network, or the **connect** timed out | **No**; nothing was sent |
| `Timeout` | The request was written but no answer came within `ResponseTimeout` | **Unknown** |
| `ConnectionLost` | The connection reset, aborted or closed after the request was written, or part-way through the answer | **Unknown** |
| `Malformed` | The answer could not be read (empty, or not JSON). On Web Service: non-numeric `MerchantId` / `TerminalId`, nothing sent | **Unknown** (LAN) |
| `Unauthenticated` | LAN: the answer failed the signature or nonce check | **Unknown** |

`Timeout`, `ConnectionLost`, `Malformed` and `Unauthenticated` do not mean the payment
did not happen. Resolve with `InquireAsync` / `InquireByReferenceAsync`, or reconcile
against the terminal, before retrying.

`Unauthenticated` almost always means a mismatched Wi-Fi/LAN key. `EcrWebServiceTerminal`
never produces `Unreachable` or `Unauthenticated`; Hub authentication failures arrive
as `Declined`.

**A custom channel decides which of these it gets.** The SDK classifies an
`IOException` from an `IEcrChannel` by its message: `closed the connection` becomes
`ConnectionLost`; `not valid JSON` or `empty message` becomes `Malformed`; anything
else becomes `Unreachable`. See [IEcrChannel](#iecrchannel).

---

## NextStep

```csharp
public enum NextStep { None, InquireByMerchantReference }
```

| Value | Meaning |
|---|---|
| `None` | The answer settles the matter |
| `InquireByMerchantReference` | Look the transaction up by the reference this request was sent with. **Never retry the sale instead** |

Exposed on `Declined` (always `None` as built by this SDK) and on `Failed` (always
`InquireByMerchantReference`). An unrecognised wire value reads as `None`.

---

## EcrReachability

```csharp
public sealed record EcrReachability(bool Reachable, string Host, int Port, string? Error = null, string? Endpoint = null)
{
    public string Endpoint { get; init; }
}
```

Result of `EcrTerminal.ProbeReachabilityAsync`. When `Reachable` is false, `Error`
carries the platform message (connection refused, timed out, host unreachable).
`Endpoint` is what to show an operator: `host:port` for a socket, the channel's own
name for a link with no address (`Port` 0). It defaults to `Host:Port` when
`Port > 0`, otherwise `Host`.

---

## IEcrChannel

```csharp
public interface IEcrChannel
{
    string Endpoint { get; }
    string Host { get; }
    int Port { get; }

    Task<string?> ProbeAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
    Task<byte[]> ExchangeAsync(byte[] body, TimeSpan timeout, CancellationToken cancellationToken = default);
}
```

How bytes reach a terminal, and nothing else. Signing, answer checks and response
codes stay in `EcrTerminal`, so a new channel cannot weaken them.

| Member | Description |
|---|---|
| `Endpoint` | What to show an operator when this link fails |
| `Host` / `Port` | The address side of [EcrReachability](#ecrreachability); a cable reports its own name and port `0` |
| `ProbeAsync` | Returns `null` when reachable, otherwise the reason |
| `ExchangeAsync` | Sends one request body, returns one answer body. **Framing is the channel's to apply** with [EcrFrames](#ecrframes). `timeout` is `EcrConfig.ResponseTimeout` |

`TcpEcrChannel` ships with the SDK; there is no USB channel. A worked skeleton is in
the [integration guide](integration-guide.md#usb-cable-bring-your-own-channel).

**Throw [EcrChannelTimeout](#ecrchanneltimeout) when the terminal did not answer**:
the payment may have completed. Exceptions from `ExchangeAsync` are classified:

| Thrown | Becomes |
|---|---|
| `EcrChannelTimeout` | `Failure.Timeout` |
| `IOException` containing `closed the connection` | `Failure.ConnectionLost` |
| `IOException` containing `not valid JSON` or `empty message` | `Failure.Malformed` |
| any other `IOException` | **`Failure.Unreachable` ("nothing was sent")** |
| `OperationCanceledException` (your token) | rethrown, not a result |
| anything else | propagates out of the operation method |

Once the request may have been written, wrap a broken link as
`new IOException("Terminal closed the connection: ...")`, as `TcpEcrChannel` does;
otherwise a lost payment is reported as safe to retry.

---

## TcpEcrChannel

```csharp
public sealed class TcpEcrChannel : IEcrChannel
{
    public static readonly TimeSpan DefaultConnectTimeout;   // 3 seconds

    public TcpEcrChannel(string host, int port = EcrConfig.DefaultPort, TimeSpan? connectTimeout = null)

    public string Host { get; }
    public int Port { get; }
    public string Endpoint { get; }   // "host:port"
}
```

The terminal over TCP: one connection carries one request and one answer. Normally
you do not construct it: `new EcrTerminal(host, ...)` and `LanTerminal` do, using
`EcrConfig.ConnectTimeout` (10 s), not this class's 3 s default.

| Situation | Thrown | Result |
|---|---|---|
| Refused, no route, unknown host | `IOException` | `Unreachable` |
| Connect timeout | `IOException("Connection timed out after N ms")` | `Unreachable` |
| Request written, no answer within `timeout` | `EcrChannelTimeout` | `Timeout` |
| Reset or abort after the request was written | `IOException("Terminal closed the connection: ...")` | `ConnectionLost` |
| Closed while sending the header or body | `IOException("Terminal closed the connection while sending the ...")` | `ConnectionLost` |
| Empty message | `IOException("Terminal returned an empty message")` | `Malformed` |
| Your token cancelled | `OperationCanceledException` | rethrown |

---

## EcrChannelTimeout

```csharp
public sealed class EcrChannelTimeout : IOException
{
    public EcrChannelTimeout(string message)
}
```

The terminal did not answer in time. The transaction may still have run.

---

## EcrFrames

```csharp
public static class EcrFrames
{
    public const int HeaderBytes = 2;
    public const int MaxBodyBytes = 0xFFFF;

    public static byte[] Wrap(byte[] body)
    public static int BodyLength(byte[] header)
    public static byte[] ReadBody(Stream input)
    public static Task<byte[]> ReadBodyAsync(Stream input, CancellationToken cancellationToken = default)
}
```

Two bytes of big-endian length, then UTF-8 JSON. Public so an external channel frames
identically. One read is not one message: a transfer may carry several, or half of
one.

| Member | Description |
|---|---|
| `Wrap(body)` | Prepends the length header. Throws `IOException` if `body` exceeds `MaxBodyBytes` |
| `BodyLength(header)` | The body length a 2-byte header announces |
| `ReadBody(input)` | Reads exactly one body, **blocking**. Do not call it on a UI thread |
| `ReadBodyAsync(input, ct)` | Asynchronous `ReadBody` |

Both read methods throw `IOException` ("Terminal closed the connection while sending
the ...") if the stream ends early and ("Terminal returned an empty message") for a
zero-length body; both are classified as described under [IEcrChannel](#iecrchannel).

---

## IEcrLogger and EcrLogger

```csharp
public interface IEcrLogger
{
    void Debug(string message);
}

public static class EcrLogger
{
    public static IEcrLogger None { get; }                      // discards everything; the default
    public static IEcrLogger From(Action<string> sink)
}
```

No logging framework dependency: route to `Microsoft.Extensions.Logging`, Serilog, the
Event Log or a file. `From(null)` throws `ArgumentNullException`. Messages include
signing diagnostics; no part of a secret is ever logged (a key is reported only as
`configured` or `not configured`), but payloads and hashes may appear, so treat logs
as transaction records.

---

## EcrWireResponse

```csharp
public sealed record EcrWireResponse(
    bool Success, string ResponseCode, string Message, JsonObject? Data,
    IReadOnlyList<string> ErrorList, string Nonce = "", string SecureHash = "")
{
    public string MerchantReference { get; }
    public string TerminalSerial { get; }
    public string DisplayMessage { get; }

    public static EcrWireResponse Parse(JsonObject json)
    public static string DisplayMessageFromRaw(string raw, string fallback = "")
}
```

The unified response envelope `{ success, responseCode, message, data, errorList,
nonce?, secureHash? }` read from the terminal's or Hub's JSON (`JsonObject` is
`System.Text.Json.Nodes.JsonObject`). You rarely need it; it is handy for inspecting a
`Raw` string.

| Member | Description |
|---|---|
| `Parse(JsonObject)` | Reads the wire JSON, including legacy flat shapes (`approved`, `responseMessage`) and answers nested under `ecrResponse` |
| `DisplayMessage` | `ErrorList` entries first (joined by newlines), then `Message` |
| `MerchantReference` | From `Data` (`merchantReference`, then `merchantReferenceId`, then `requestId`); empty if none |
| `TerminalSerial` | `Data`'s `terminalSerial`, or empty |
| `DisplayMessageFromRaw(raw, fallback)` | `DisplayMessage` of a raw JSON string; `fallback` when empty or invalid |

---

## Web Service request types

Namespace `AmwalPay.Ecr.WebService`. Optional building blocks for the signed JSON
body without the terminal classes (tests, custom HTTP clients).
`EcrWebServiceTerminal` builds its requests internally and fills timestamps.

```csharp
public interface IEcrWebServiceRequest
{
    long MerchantId { get; }
    long TerminalId { get; }
    string TerminalSerial { get; }
    string MerchantReference { get; }
}

public sealed record EcrWebServiceSaleRequest(
    long MerchantId, long TerminalId, string TerminalSerial, long AmountMinorUnits,
    string MerchantReference = "", string CurrencyCode = "512", string EcrId = "ECR01",
    string TransactionDateTime = "") : IEcrWebServiceRequest;

public sealed record EcrWebServiceVoidRequest(
    long MerchantId, long TerminalId, string TerminalSerial, string Stan,
    string MerchantReference = "", string CurrencyCode = "512", string EcrId = "ECR01",
    string TransactionDateTime = "") : IEcrWebServiceRequest;

public sealed record EcrWebServiceRefundRequest(
    long MerchantId, long TerminalId, string TerminalSerial, long AmountMinorUnits,
    string Stan, string OriginalTransactionDate, string MerchantReference = "",
    string CurrencyCode = "512", string EcrId = "ECR01",
    string TransactionDateTime = "") : IEcrWebServiceRequest;

public sealed record EcrWebServiceTransactionStatusRequest(
    long MerchantId, long TerminalId, string TerminalSerial, string Stan = "",
    string OriginalTransactionDate = "", string MerchantReference = "",
    string CurrencyCode = "512", string EcrId = "ECR01", string TransactionDateTime = "",
    string OriginalMerchantReference = "") : IEcrWebServiceRequest;

public sealed record EcrWebServiceCloseReceiptRequest(
    long MerchantId, long TerminalId, string TerminalSerial, string OriginalMerchantReference,
    string MerchantReference = "", string EcrId = "ECR01",
    string TransactionDateTime = "") : IEcrWebServiceRequest;

public static class EcrWebServiceRequestJson
{
    public static JsonObject Sale(EcrWebServiceSaleRequest request, string secureHashKey = "")
    public static JsonObject Void(EcrWebServiceVoidRequest request, string secureHashKey = "")
    public static JsonObject Refund(EcrWebServiceRefundRequest request, string secureHashKey = "")
    public static JsonObject TransactionStatus(EcrWebServiceTransactionStatusRequest request, string secureHashKey = "")
    public static JsonObject Inquiry(EcrWebServiceTransactionStatusRequest request, string secureHashKey = "")   // same as TransactionStatus
    public static JsonObject CloseReceipt(EcrWebServiceCloseReceiptRequest request, string secureHashKey = "")   // 1.0.2
}
```

| Property | Description |
|---|---|
| `AmountMinorUnits` | Amount in minor units (OMR: 3 decimals, `111` is 0.111) |
| `Stan` | Receipt number of the original transaction |
| `OriginalTransactionDate` | `yyyyMMdd` |
| `OriginalMerchantReference` | Status request: set (with empty `Stan`) to inquire by reference. Close receipt: the sale / void / refund reference (required) |
| `TransactionDateTime` | `yyyyMMddHHmmss`; left empty, the current local time is used |

Record parameter order is positional; use named arguments.

```csharp
var request = new EcrWebServiceSaleRequest(
    MerchantId: 13593,
    TerminalId: 101311,
    TerminalSerial: "P653200008251",
    AmountMinorUnits: 1234,
    MerchantReference: "REF-1586");

JsonObject body = EcrWebServiceRequestJson.Sale(request, secureHashKey: webServiceKey);
// POST body.ToJsonString() to <Hub base>/Ecr/Sale
```

With a non-empty `secureHashKey` the body carries `secureHashValue`; with the default
empty key it is unsigned. A key that cannot be decoded throws `ArgumentException`.
`CloseReceipt` throws `ArgumentException` if `OriginalMerchantReference` is blank.
Field lists and paths: [protocol](protocol.md#9-web-service-rest).

---

## Appendix: Android SDK name differences

For teams that also use the Android SDK (`com.amwalpay.ecr`). The wire protocol is
identical; names follow .NET conventions (`camelCase` members become `PascalCase`,
`suspend fun sale` becomes `SaleAsync`, and so on). The non-obvious differences:

| Android (Kotlin) | .NET |
|---|---|
| `EcrConfig(...)` constructor and `config.copy(...)` | `new EcrConfig { ... }` and `config with { ... }` |
| `kotlin.time.Duration`, `BigDecimal` | `TimeSpan`, `decimal` |
| `io:` dispatcher parameter | none; `await` the call |
| Coroutine cancellation | `CancellationToken`; throws `OperationCanceledException` |
| `EcrOpenedSession.inquire(...)` | `InquireAsync(receiptNumber, transactionDate, originalTerminalId, cancellationToken)`; no `merchantReference` |
| `EcrReachability(reachable, host, port, error)` | Adds `Endpoint` |
| `EcrChannel` | `IEcrChannel` with `Host` / `Port` added and async methods |
| `UsbAccessoryEcrChannel` (app code) | None; supply your own `IEcrChannel` |
| `EcrFrames.wrap` / `readBody` | `Wrap`, `ReadBody`, plus `ReadBodyAsync`, `HeaderBytes`, `MaxBodyBytes` |
| `EcrEnvironment.DEFAULT` / `fromName` | `EcrEnvironments.Default` / `FromName` |
| Type flags as properties (`type.movesMoney`) | Extension methods (`type.MovesMoney()`) on `EcrTransactionTypes` |
| `EcrWebServiceSaleRequest(..., merchantReference, amountMinorUnits, ...)` | `AmountMinorUnits` comes **before** `MerchantReference` |
| `EcrWebServiceRequest` | `IEcrWebServiceRequest` |
