# API reference

Every public type in `AmwalPay.Ecr` and `AmwalPay.Ecr.WebService` for **package
1.0.0**. Everything else — the `Internal` namespace, the HTTP client, the routes,
the signing helpers — is `internal`, so this is the whole surface: nothing else
is visible to you, and nothing here changes without a version bump.

**Conventions**

- Every operation is `async` and returns a `Task<T>`. Names end in `Async`.
- Every asynchronous method takes a trailing `CancellationToken cancellationToken = default`.
  Cancelling stops the wait, **not** the transaction — see
  [Threading and lifecycle](integration-guide.md#12-threading-and-lifecycle).
- Amounts you send are `decimal`. Amounts you receive are `string`s in major units,
  invariant culture (`"1.234"`).
- Results, links, plans and config are `record`s, so they compare by value and
  support `with`. **Use C# 9 or later** (see [Compatibility](compatibility.md#c-9-or-later)).
- Nullable reference types are enabled; `?` in a signature means "may be null".
- Argument mistakes throw `ArgumentException` (raised on `await` for the `async`
  methods, before a connection is opened). Runtime problems become results.

**Contents**

- [EcrSessions](#ecrsessions) — validate config and build clients
- [EcrLink](#ecrlink) — how you reach a terminal
- [EcrSessionPlan](#ecrsessionplan) — validated plan for one terminal
- [EcrOpenedSession](#ecropenedsession) — one client over any link
- [EcrTerminal](#ecrterminal) — LAN / Wi‑Fi / USB client
- [EcrWebServiceTerminal](#ecrwebserviceterminal) — HTTPS REST client
- [EcrConfig](#ecrconfig) — identity, currency, timeouts, signing
- [EcrEnvironment](#ecrenvironment) — Hub deployment (Web Service)
- [Merchant reference](#merchant-reference) — naming a transaction as your system does
- [EcrTransactionType](#ecrtransactiontype) — what each operation needs
- [EcrResult](#ecrresult) — how a transaction ended
- [EcrInquiry](#ecrinquiry) / [EcrTransaction](#ecrtransaction) — what became of one
- [EcrReceipt](#ecrreceipt) — the e-receipt
- [Failure](#failure) — why an exchange did not complete
- [NextStep](#nextstep) — what to do after a decline or failure
- [EcrReachability](#ecrreachability) — probe result
- [IEcrChannel](#iecrchannel) / [TcpEcrChannel](#tcpecrchannel) / [EcrChannelTimeout](#ecrchanneltimeout) / [EcrFrames](#ecrframes) — transports
- [IEcrLogger and EcrLogger](#iecrlogger-and-ecrlogger) — diagnostics
- [EcrWireResponse](#ecrwireresponse) — the parsed response envelope
- [Web Service request types](#web-service-request-types) — `AmwalPay.Ecr.WebService`
- [Appendix: Kotlin → C# name map](#appendix-kotlin--c-name-map)

---

## EcrSessions

```csharp
public static class EcrSessions
```

Validates an [EcrLink](#ecrlink) and [EcrConfig](#ecrconfig), then builds the
right client. Intended so integrators do not duplicate transport rules, signing-key
labels, or merchant/terminal ID checks in application code.

### Plan

```csharp
public static EcrSessionPlan Plan(
    EcrLink link,
    EcrConfig config,
    string? rawSecureHashKey = null)
```

Validates without opening a connection. Returns an [EcrSessionPlan](#ecrsessionplan)
whose `Issues` list describes anything missing — blank IP, non-numeric merchant ID,
invalid hex key, empty signing key — rather than throwing.

Pass `rawSecureHashKey` when the user entered a key that might fail
`EcrConfig`'s property setter (odd length, non-hex). It defaults to
`config.SecureHashKey`. The plan reports the problem and sanitizes the key to
empty so you can still show other issues. The key is trimmed. This is the **same**
typed string, not a second secret — apps normally keep two mode-specific keys in
settings (Wi‑Fi/LAN vs Web Service) and pass only the one for the selected mode.

For [EcrLink.Lan](#ecrlink), the resolved config's `Port` comes from the link.
For [EcrLink.WebService](#ecrlink), `MerchantId` and `TerminalId` on the config
are taken from the link so they cannot drift.

**A missing key is always an issue**, for every link — `"Wi-Fi/LAN secure hash key
is not configured"` or `"Web Service secure hash key is not configured"` — so a
plan with no key is never ready. Build `EcrTerminal` directly if you deliberately
need an unsigned client (test code only).

**Throws** `ArgumentNullException` if `link` or `config` is null.

### Open

```csharp
public static EcrOpenedSession Open(
    string terminalSerial,
    EcrSessionPlan plan,
    Func<IEcrChannel>? usbChannel = null,
    IEcrLogger? logger = null)
```

Opens the transport `plan` describes — LAN, USB cable or Web Service — and returns
an [EcrOpenedSession](#ecropenedsession). One dispatch, so callers and recovery
paths cannot diverge on which client they build.

`usbChannel` is required when `plan.UsesUsbCable` and ignored otherwise. **The
package ships no USB channel** — the factory returns yours.

**Throws** `ArgumentException` if `plan.IsReady` is false;
`InvalidOperationException` for a USB plan with no factory (or a factory that
returns null).

### LanTerminal

```csharp
public static EcrTerminal LanTerminal(
    string terminalSerial,
    EcrSessionPlan plan,
    IEcrLogger? logger = null)
```

Builds a LAN client. `plan` must be for a LAN link (`plan.UsesLan == true`),
otherwise `ArgumentException`.

### UsbCableTerminal

```csharp
public static EcrTerminal UsbCableTerminal(
    string terminalSerial,
    EcrSessionPlan plan,
    IEcrChannel channel,
    IEcrLogger? logger = null)
```

Builds a client that talks through a channel you supply. `plan` must be for
`EcrLink.UsbCable`, otherwise `ArgumentException`. Everything above the channel is
identical to `LanTerminal`: the same signing, the same messages, the same answers.

### WebServiceTerminal

```csharp
public static EcrWebServiceTerminal WebServiceTerminal(
    string terminalSerial,
    EcrSessionPlan plan,
    IEcrLogger? logger = null)
```

Builds a Web Service client. `plan` must be for Web Service
(`plan.UsesWebService == true`), otherwise `ArgumentException`.

**Initialization order**

1. Collect serial, numeric merchant/terminal IDs, `EcrEnvironment`, and the
   **Web Service** secure hash key (not the LAN key).
2. `EcrSessions.Plan(new EcrLink.WebService(merchantId, terminalId), config, rawKey)`.
3. If `!plan.IsReady`, show `plan.Issues` and stop.
4. Call `WebServiceTerminal(serial, plan)` **or** `Open(serial, plan)` for a
   mode-agnostic [EcrOpenedSession](#ecropenedsession).
5. Call `SaleAsync` / `VoidAsync` / `RefundAsync` / `InquireAsync` /
   `InquireByReferenceAsync` — the Hub is contacted only then.

See the [integration guide §4](integration-guide.md#initialization-process) for
Hub URLs, TLS, and recovery after a lost answer.

> **`LanTerminal`, `UsbCableTerminal` and `WebServiceTerminal` do not check
> `plan.IsReady`; only `Open` does.** A plan that is not ready may carry an empty
> key, which builds an _unsigned_ terminal. Always test `plan.IsReady` first.

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

How this till reaches a POS terminal. The app supplies addressing; the SDK owns
protocol, signing, and transport. The hierarchy is closed — only these three
exist — so match on it with `switch`/`is`.

### EcrLink.Lan

Wi‑Fi / LAN — TCP to `Host`:`Port` (default 9100).

### EcrLink.UsbCable

A cable straight from the till to the terminal, carrying no IP. Nothing to
configure — so nothing to carry — because the cable is found by looking at what is
plugged in, by the [IEcrChannel](#iecrchannel) you supply. Construct it with
`new EcrLink.UsbCable()`.

**The C# package ships no USB channel.** Build the terminal with
[EcrSessions.UsbCableTerminal](#usbcableterminal) or
[EcrSessions.Open](#open), passing an `IEcrChannel` for your hardware, and frame
with [EcrFrames](#ecrframes).

### EcrLink.WebService

Hosted REST — HTTPS with JSON body signing. Hub URLs are resolved inside the SDK
from [EcrConfig.Environment](#ecrconfig); the app does not configure base URLs.
`MerchantId` and `TerminalId` must be numeric strings. Construct with
`new EcrLink.WebService(MerchantId: "13593", TerminalId: "101311")`.

`EcrSessions.Plan` copies those IDs onto the resolved [EcrConfig](#ecrconfig) and
requires a non-empty Web Service secure hash key before `IsReady` is true. Full
init steps: [integration guide](integration-guide.md#initialization-process).

---

## EcrSessionPlan

```csharp
public sealed record EcrSessionPlan(
    EcrLink Link,
    EcrConfig Config,
    IReadOnlyList<string>? Issues = null)
```

Validated SDK configuration for one terminal link, built by
[EcrSessions.Plan](#plan).

| Property                                           | Description                                                                                                                      |
| -------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------- |
| `IReadOnlyList<string> Issues`                     | What is wrong, or empty. Never null                                                                                              |
| `bool IsReady`                                     | `Issues.Count == 0` — safe to send                                                                                               |
| `bool UsesLan` / `UsesWebService` / `UsesUsbCable` | Which client to build                                                                                                            |
| `string SigningKeyLabel`                           | Human label for the missing-key issue — Wi‑Fi/LAN vs Web Service                                                                 |
| `bool SigningKeyConfigured`                        | Whether a non-empty key is present                                                                                               |
| `string ConnectionSummary`                         | Operator-facing summary with **no secrets** — e.g. `192.168.1.50:9100`, `USB cable`, or `UAT · merchant 13593 · terminal 101311` |

---

## EcrOpenedSession

```csharp
public sealed class EcrOpenedSession
```

One opened transport for a validated plan, from [EcrSessions.Open](#open). It
exposes one set of calls whichever transport is behind it, **but it is not a
lowest-common-denominator you can ignore** — several arguments only mean
something on one transport. It has no public constructor.

| Member                   | Description                                                         |
| ------------------------ | ------------------------------------------------------------------- |
| `EcrSessionPlan Plan`    | The plan it was opened from                                         |
| `bool UsesLocalTerminal` | LAN or USB cable (an `EcrTerminal` is behind it)                    |
| `bool UsesWebService`    | An `EcrWebServiceTerminal` is behind it                             |
| `bool SupportsReceipt`   | `UsesLocalTerminal` — receipt URLs exist only on the local protocol |

```csharp
public Task<EcrReachability?> ProbeReachabilityAsync(CancellationToken cancellationToken = default)

public Task<EcrResult> SaleAsync(decimal amount, string merchantReference = "", CancellationToken cancellationToken = default)

public Task<EcrResult> VoidAsync(string receiptNumber, string originalTerminalId = "", string merchantReference = "", CancellationToken cancellationToken = default)

public Task<EcrResult> RefundAsync(decimal amount, string receiptNumber, string transactionDate, string originalTerminalId = "", string merchantReference = "", CancellationToken cancellationToken = default)

public Task<EcrInquiry> InquireAsync(string receiptNumber, string transactionDate, string originalTerminalId = "", CancellationToken cancellationToken = default)

public Task<EcrInquiry> InquireByReferenceAsync(string originalReference, string transactionDate = "", string originalTerminalId = "", string merchantReference = "", CancellationToken cancellationToken = default)

public Task<EcrReceipt> ReceiptAsync(string receiptNumber, string transactionDate, string originalTerminalId = "", CancellationToken cancellationToken = default)
```

Differences to know:

- **`ProbeReachabilityAsync` returns `null` on Web Service** — there is no probe.
- **`originalTerminalId` is ignored on Web Service** (void, refund, inquire, by
  reference). It is passed through on LAN/USB. Check `UsesLocalTerminal` before
  relying on it.
- **`ReceiptAsync` on Web Service** does not call anything; it returns
  `EcrReceipt.Unavailable` with an empty `MerchantReference`, an empty `Raw` and
  the reason `"Receipt fetch is only supported over Wi‑Fi and USB cable ECR"`.
- **`InquireByReferenceAsync` on Web Service** reaches
  [EcrWebServiceTerminal.InquireByReferenceAsync](#inquirebyreferenceasync-1)
  and looks the transaction up by `originalMerchantReference` (empty `stan`).
- **`InquireAsync` and `ReceiptAsync` here take no `merchantReference`**, unlike
  `EcrTerminal`'s. The fourth positional argument is the `CancellationToken`, not a
  reference — use named arguments.
- There is no `RunAsync`. Use the typed methods.

---

## EcrTerminal

```csharp
public sealed class EcrTerminal
```

A POS terminal reachable over the network — or any link an [IEcrChannel](#iecrchannel)
can carry. One instance addresses one terminal; it holds no connection between
calls, so it is cheap to keep and safe to reuse and to share across threads.

### Constructors

```csharp
public EcrTerminal(
    IEcrChannel channel,
    string serialNumber = "",
    EcrConfig? config = null,
    IEcrLogger? logger = null)

public EcrTerminal(
    string host,
    string serialNumber = "",
    EcrConfig? config = null,
    IEcrLogger? logger = null)
```

The second builds a [TcpEcrChannel](#tcpecrchannel) for `host` using
`config.Port` and `config.ConnectTimeout`, and hands it to the first.

| Parameter      | Description                                                                                                                |
| -------------- | -------------------------------------------------------------------------------------------------------------------------- |
| `channel`      | How bytes reach the terminal. A socket, a USB cable, anything that can carry one request and bring back one answer         |
| `host`         | The terminal's address on the local network                                                                                |
| `serialNumber` | Identifies the terminal in the message. The terminal shows its own address on screen; this is what the operator registered |
| `config`       | Identity, currency, timeouts and signing key. Defaults to `new EcrConfig()`. See [EcrConfig](#ecrconfig)                   |
| `logger`       | Where diagnostics go. Silent by default                                                                                    |

Every method is asynchronous, never blocks the calling thread, and uses
`ConfigureAwait(false)` internally, so it is safe to `await` from a UI thread.

### IsReachableAsync

```csharp
public Task<bool> IsReachableAsync(CancellationToken cancellationToken = default)
```

Whether the terminal is listening. Equivalent to `ProbeReachabilityAsync().Reachable`.

### ProbeReachabilityAsync

```csharp
public Task<EcrReachability> ProbeReachabilityAsync(CancellationToken cancellationToken = default)
```

Asks the channel whether the terminal is there, before sending anything, waiting up
to `config.ProbeTimeout`. For TCP that is a handshake to `host:config.Port`.
Returns reachability plus the platform error message when the probe fails
(`connection refused`, timed out, …). Prefer this over `IsReachableAsync()` when
you need to show _why_ the till cannot reach the terminal.

```csharp
EcrReachability probe = await terminal.ProbeReachabilityAsync();
if (!probe.Reachable)
{
    screen.Show($"{probe.Endpoint} — {probe.Error}");
}
```

### SaleAsync

```csharp
public Task<EcrResult> SaleAsync(
    decimal amount,
    string merchantReference = "",
    CancellationToken cancellationToken = default)
```

Takes a payment. The cardholder presents their card at the terminal.

Completes when the terminal answers — normally seconds, occasionally the full
`ResponseTimeout`.

```csharp
var result = await terminal.SaleAsync(1.234m);

// Or, naming the sale with your own order number:
var named = await terminal.SaleAsync(1.234m, merchantReference: "ORD-88231");
```

| Parameter           | Description                                                                                                      |
| ------------------- | ---------------------------------------------------------------------------------------------------------------- |
| `amount`            | How much to take. Rounded to `config.MinorUnitDigits` places (away from zero at the midpoint). Not range-checked |
| `merchantReference` | Your own reference for this sale. See [Merchant reference](#merchant-reference)                                  |

> Use the `m` suffix: `1.234m`. A `double` does not compile as an argument, and a
> `double` cast to `decimal` is already imprecise before the SDK sees it.

### VoidAsync

```csharp
public Task<EcrResult> VoidAsync(
    string receiptNumber,
    string originalTerminalId = "",
    string merchantReference = "",
    CancellationToken cancellationToken = default)
```

Cancels an earlier transaction **in full**, by the receipt number printed on it.

No amount: a void returns exactly what the original took, and only the terminal
knows that figure. No card is presented for a transaction taken on the same
terminal — the terminal holds the original's details — so it completes in about
a second.

| Parameter            | Description                                                                                                                   |
| -------------------- | ----------------------------------------------------------------------------------------------------------------------------- |
| `receiptNumber`      | The original's receipt number. `"215"`; padding is applied for you                                                            |
| `originalTerminalId` | The terminal the original was taken on, when not this one                                                                     |
| `merchantReference`  | Your own reference for the **void**. It names the cancellation, not the transaction being cancelled — that is `receiptNumber` |

Refusals — original not found, outside the void window, not voidable — arrive as
`EcrResult.Declined` with the reason. Nothing reached the backend, so no money
moved.

**Throws** `ArgumentException` if `receiptNumber` is blank.

### RefundAsync

```csharp
public Task<EcrResult> RefundAsync(
    decimal amount,
    string receiptNumber,
    string transactionDate,
    string originalTerminalId = "",
    string merchantReference = "",
    CancellationToken cancellationToken = default)
```

Returns money against an earlier transaction, in full or in part. **The cardholder
presents their card** to receive it.

| Parameter            | Description                                                                                                    |
| -------------------- | -------------------------------------------------------------------------------------------------------------- |
| `amount`             | How much to return                                                                                             |
| `receiptNumber`      | The original's receipt number                                                                                  |
| `transactionDate`    | The day the original was taken, `yyyyMMdd`. Required — a receipt number is only unique within a terminal's day |
| `originalTerminalId` | The terminal the original was taken on, when not this one                                                      |
| `merchantReference`  | Your own reference for the **refund**                                                                          |

```csharp
await terminal.RefundAsync(0.500m, receiptNumber: "208", transactionDate: "20260809");
```

**Throws** `ArgumentException` if `receiptNumber` is blank.

### InquireAsync

```csharp
public Task<EcrInquiry> InquireAsync(
    string receiptNumber,
    string transactionDate,
    string originalTerminalId = "",
    string merchantReference = "",
    CancellationToken cancellationToken = default)
```

Asks what became of an earlier transaction, without touching it.

Nothing is authorised and no card is presented, so this is safe to repeat and the
terminal answers it **even while taking a payment** — which is when a till most
needs it. After a `Failure.Timeout`, this is how to find out what happened
instead of retrying and risking a second charge.

`merchantReference` names the _inquiry itself_, not the transaction — it is not a
way to look one up (that is `InquireByReferenceAsync`).

**Throws** `ArgumentException` if `receiptNumber` is blank.

### InquireByReferenceAsync

```csharp
public Task<EcrInquiry> InquireByReferenceAsync(
    string originalReference,
    string transactionDate = "",
    string originalTerminalId = "",
    string merchantReference = "",
    CancellationToken cancellationToken = default)
```

Looks up a transaction by the **merchant reference** from an earlier sale rather
than by receipt number. Useful after `Failure.Timeout`, `ConnectionLost` or
`Unauthenticated` when you stored `result.MerchantReference` but do not have a
receipt number — the receipt number arrives _in_ the answer, so a lost answer
leaves you without one.

`transactionDate` is optional here: a reference is not scoped to a day the way a
receipt number is. `merchantReference` names the inquiry itself; `originalReference`
is the transaction being looked up.

**Throws** `ArgumentException` if `originalReference` is blank (or breaks the
[reference rules](#merchant-reference)).

### ReceiptAsync

```csharp
public Task<EcrReceipt> ReceiptAsync(
    string receiptNumber,
    string transactionDate,
    string originalTerminalId = "",
    string merchantReference = "",
    CancellationToken cancellationToken = default)
```

Fetches an earlier transaction's e-receipt, as a URL to put in a QR code. The
customer scans it and reads the receipt on their own phone, so a till with no
printer can still hand one over.

Non-financial and reprintable — ask as often as you like.

**Throws** `ArgumentException` if `receiptNumber` is blank.

### RunAsync

```csharp
public Task<EcrResult> RunAsync(
    EcrTransactionType type,
    decimal? amount = null,
    string originalStan = "",
    string originalTerminalId = "",
    string originalDate = "",
    string merchantReference = "",
    CancellationToken cancellationToken = default)
```

Runs any operation that moves money. The typed methods above are the usual way
in; this is for a caller driving the type from its own UI.

**Throws** `ArgumentException` when the type needs something that is missing (an
amount, a receipt number), or when given `Inquiry` or `Receipt` — those answer with
something they found rather than something they performed, so they have their own
methods. (The exception message for those two says "is run with inquire()"; the
methods are `InquireAsync` and `ReceiptAsync`.)

### What the SDK does when an answer is lost

For `SaleAsync`, `VoidAsync`, `RefundAsync` and `RunAsync`, a lost exchange becomes
`EcrResult.Failed`. When [EcrConfig.AutoInquireOnFailure](#ecrconfig) is `true`, the
failure is one where `Failure.OutcomeUnknown` is true, and a reference exists, the
SDK then makes one `InquireByReferenceAsync` call and attaches the finding as
`Failed.Recovered`. A `CancellationToken` cancellation is never turned into a
result: it throws `OperationCanceledException` and no follow-up runs.

---

## EcrWebServiceTerminal

```csharp
public sealed class EcrWebServiceTerminal
{
    public EcrWebServiceTerminal(
        string terminalSerial,
        EcrConfig? config = null,
        IEcrLogger? logger = null)
}
```

Web Service ECR client — REST over HTTPS with JSON body signing. Use instead of
[EcrTerminal](#ecrterminal) when TMS sets `ecrMode: 4`. Prefer building via
[EcrSessions.WebServiceTerminal](#webserviceterminal) after
[Plan](#plan) reports `IsReady` — see
[initialization process](integration-guide.md#initialization-process).

Requires `config.MerchantId`, `config.TerminalId` (numeric), and the **Web Service
secure hash key** on `config.SecureHashKey`. Hub URLs come from
`config.Environment`; paths (`/Ecr/Sale`, `/Ecr/Void`, `/Ecr/Refund`,
`/Ecr/Inquiry`) are fixed inside the SDK. If the IDs are not numeric the
call returns `EcrResult.Failed` with `Failure.Malformed` and sends nothing.

```csharp
public Task<EcrResult> SaleAsync(
    decimal amount,
    string merchantReference = "",
    CancellationToken cancellationToken = default)

public Task<EcrResult> VoidAsync(
    string receiptNumber,
    string merchantReference = "",
    CancellationToken cancellationToken = default)

public Task<EcrResult> RefundAsync(
    decimal amount,
    string receiptNumber,
    string transactionDate,
    string merchantReference = "",
    CancellationToken cancellationToken = default)

public Task<EcrInquiry> InquireAsync(
    string receiptNumber,
    string transactionDate,
    string merchantReference = "",
    CancellationToken cancellationToken = default)

public Task<EcrInquiry> InquireByReferenceAsync(
    string originalReference,
    string transactionDate,
    string merchantReference = "",
    CancellationToken cancellationToken = default)
```

**The argument lists differ from `EcrTerminal`'s.** There is no
`originalTerminalId`, so the second positional argument of `VoidAsync` is a
`merchantReference`, not a terminal id: `webTerminal.VoidAsync("215", "31629")`
names the void `31629`. Use named arguments. `transactionDate` on
`InquireByReferenceAsync` is required here and optional on LAN.

| Method                                                   | LAN / USB | Web Service | Notes                                                   |
| -------------------------------------------------------- | --------- | ----------- | ------------------------------------------------------- |
| `SaleAsync(amount, merchantReference?)`                  | Yes       | Yes         |                                                         |
| `VoidAsync(receiptNumber, …)`                            | Yes       | Yes         | LAN: optional `originalTerminalId`, `merchantReference` |
| `RefundAsync(amount, receiptNumber, transactionDate, …)` | Yes       | Yes         | LAN: optional `originalTerminalId`                      |
| `InquireAsync(receiptNumber, transactionDate, …)`        | Yes       | Yes         | STAN required; LAN: optional `originalTerminalId`       |
| `InquireByReferenceAsync(originalReference, …)`          | Yes       | Yes         | Sends `originalMerchantReference`; `stan` empty         |
| `ReceiptAsync(receiptNumber, transactionDate, …)`        | Yes       | **None**    | LAN / USB only                                          |
| `ProbeReachabilityAsync()` / `IsReachableAsync()`        | Yes       | **None**    | TCP only                                                |
| `RunAsync(type, …)`                                      | Yes       | **None**    | Money-moving ops on LAN / USB only                      |

Returns the same [EcrResult](#ecrresult) / [EcrInquiry](#ecrinquiry) types where the
operation exists on both transports.

**Not on Web Service today:** `ReceiptAsync`, response signature verification,
and [EcrConfig.AutoInquireOnFailure](#ecrconfig) recovery. Void/refund/inquiry
send `originalTerminalId` (= request `terminalId`).

### Transport errors: read this before you branch on `Declined`

The HTTP layer never throws for a network problem. A timeout, no network, a
connection reset, an empty body, or a body that is not JSON comes back as a
synthesised `{ "success": false, "responseCode": …, "message": … }` envelope, and
the SDK reads `success: false` as a decline. Whenever the request **may have
reached the Hub**, that envelope also carries `"nextStep":
"INQUIRE_BY_MERCHANT_REFERENCE"`, so the result says the outcome is unknown:

| What happened                                               | `SaleAsync` / `VoidAsync` / `RefundAsync`                                                                       | `InquireAsync` / `InquireByReferenceAsync`        |
| ----------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------- | ------------------------------------------------- |
| Timeout, no network, reset                                  | `Declined`, `ResponseCode == "NO_RESPONSE"`, `Reason` = the error, **`NextStep == InquireByMerchantReference`** | `EcrInquiry.Failed` with `Failure.ConnectionLost` |
| Hub answered with an empty body, or a body that is not JSON | `Declined`, `ResponseCode` = the HTTP status (`502`, …), **`NextStep == InquireByMerchantReference`**           | `Failed` / `ConnectionLost`                       |
| HTTP 5xx with no readable `message` / `errorList`           | `Declined`, `ResponseCode` = the status, **`NextStep == InquireByMerchantReference`**                           | `Failed` / `ConnectionLost`                       |
| HTTP 4xx with no readable `message` / `errorList`           | `Declined`, `ResponseCode` = the status, `NextStep == None`                                                     | `NotFound` (no transaction record)                |
| Non-2xx with a readable JSON `message` / `errorList`        | `Declined` from the Hub's own answer, `NextStep == None` unless the Hub says otherwise                          | `NotFound` or `Found` per the body                |
| Hub answered normally                                       | `Approved` / `Declined` (`NextStep == None`)                                                                    | `Found` / `NotFound`                              |

**The reliable signal is `Declined.NextStep == NextStep.InquireByMerchantReference`:
that means unknown outcome — inquire before retrying — not a refusal.**
`ResponseCode == "NO_RESPONSE"` is the common cause (timeout, no network, broken
connection), but the empty, unreadable and 5xx cases carry the same `NextStep`
under an HTTP-status code, so branch on `NextStep`, not on the code. A genuine
`EcrInquiry.NotFound` is now a real answer from the Hub; a failed lookup arrives as
`EcrInquiry.Failed` (still unknown — ask again). The request has a single deadline
of `ConnectTimeout + ResponseTimeout`. Cancelling your token throws
`OperationCanceledException` as elsewhere.

### InquireByReferenceAsync

On Web Service the SDK POSTs to `/Ecr/Inquiry` with `originalMerchantReference`
set, empty `stan`, `originalTerminalId` = request `terminalId`, and Hub-required
`requestDateTime` (= `transactionDateTime`), matching the Kotlin ecr-sdk Hub
contract. `transactionDate` (`yyyyMMdd`) is optional — blank leaves the day
unfiltered; supply it when you know the original's day.

A found answer may carry the transaction under nested `data` and/or as Hub flat
fields at the top level; correlation-only `data` (merchant reference / serial
alone) is **not** treated as found. A transport failure on the lookup itself is
`EcrInquiry.Failed` / `ConnectionLost`, not `NotFound`.

Build via [EcrSessions.WebServiceTerminal](#webserviceterminal) after a ready
[EcrSessionPlan](#ecrsessionplan). The client shares one static `HttpClient` for the
process and uses the machine's default proxy settings.

### Low-level request builders (optional)

Advanced integrators can build signed JSON without the terminal classes — see
[Web Service request types](#web-service-request-types).

---

## Merchant reference

Every operation takes an optional `merchantReference`, and every outcome
reports one back.

Pass your own when your system already names the transaction — an order number,
a basket id, a ticket number. The same string then identifies it in your books,
in the terminal's records and in any later inquiry, so there is no mapping table
to keep and nothing to reconcile by timestamp.

```csharp
var result = await terminal.SaleAsync(1.234m, merchantReference: "ORD-88231");
Console.WriteLine(result.MerchantReference);   // ORD-88231
```

Leave it out and the SDK generates one — 12 uppercase hex characters — and
reports it on the result. Store that: it is what names the transaction
afterwards.

```csharp
var result = await terminal.SaleAsync(1.234m);
Console.WriteLine(result.MerchantReference);   // A1B2C3D4E5F6
```

|            |                                                                           |
| ---------- | ------------------------------------------------------------------------- |
| Length     | 1–32 characters                                                           |
| Characters | Printable ASCII, no spaces, and not `&` or `=`                            |
| Uniqueness | Yours to decide. The terminal carries the reference and does not check it |

Surrounding whitespace is trimmed, and a reference of nothing but whitespace
counts as none given. Anything else outside the rules above throws
`ArgumentException` before a connection is opened.

**Pass your own if a cancellation or a crash can interrupt the call** — a
generated reference reaches you only on a returned result.

> **Older name.** On the wire this field was called `requestId` until tills could
> supply their own reference. Both are now `merchantReference`; a terminal still
> accepts and still echoes the old name, so mixed versions interoperate.

---

## EcrConfig

```csharp
public sealed record EcrConfig
{
    public const int DefaultPort = 9100;

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

There is no constructor with parameters: set what you need with an object
initializer and derive variants with `with`. **C# 9 or later is required** to
assign the `init` properties.

```csharp
var config = new EcrConfig { EcrId = "TILL-01", SecureHashKey = "0123456789ABCDEF..." };
var stricter = config with { ResponseTimeout = TimeSpan.FromSeconds(60) };
```

The defaults describe an Omani deployment on the standard port.

| Property                    | Meaning                                                                                                                                                                                                                                                                         |
| --------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `EcrId`                     | Identifies this cash register to the terminal, and appears on the terminal's records                                                                                                                                                                                            |
| `CurrencyCode`              | ISO 4217 numeric. `512` is OMR                                                                                                                                                                                                                                                  |
| `MinorUnitDigits`           | Decimal places the currency has. Decides how `1.234` is written on the wire                                                                                                                                                                                                     |
| `Port`                      | The terminal's ECR listener port (LAN ECR)                                                                                                                                                                                                                                      |
| `ConnectTimeout`            | How long to wait for the terminal to accept a connection. A timeout here is `Failure.Unreachable` (nothing was sent). For Web Service it is added to `ResponseTimeout`                                                                                                          |
| `ResponseTimeout`           | How long to wait for the result once the request is sent. Generous by default — the cardholder has to present a card and may be asked for a PIN. A timeout here is `Failure.Timeout`                                                                                            |
| `ProbeTimeout`              | How long [ProbeReachabilityAsync](#probereachabilityasync) waits. Short on purpose                                                                                                                                                                                              |
| `SecureHashKey`             | Signing secret as hex, **supplied by the app** for the selected terminal mode. LAN and Web Service use different secrets and formats, but both are passed through this single property. Empty means unsigned                                                                    |
| `MerchantId` / `TerminalId` | Backend IDs for Web Service ECR. Usually merged from [EcrLink.WebService](#ecrlink) by [EcrSessions.Plan](#plan)                                                                                                                                                                |
| `Environment`               | Hub deployment for Web Service. URLs are SDK-internal                                                                                                                                                                                                                           |
| `AutoInquireOnFailure`      | When `true` (default), **[EcrTerminal](#ecrterminal) only** follows an unknown failure with `InquireByReferenceAsync(MerchantReference)` and attaches the finding to [EcrResult.Failed.Recovered](#ecrresultfailed). Ignored by [EcrWebServiceTerminal](#ecrwebserviceterminal) |

**Throws** `ArgumentException` **at assignment** if `MinorUnitDigits` is outside 0–4,
`Port` is outside 1–65535, or `SecureHashKey` is neither empty nor an
even-length hex string of at least 16 characters. Timeouts are not validated.

`ToString()` redacts a non-empty `SecureHashKey` as `<redacted>`.

### IsValidSecureHashKey

```csharp
public static bool IsValidSecureHashKey(string? key)
```

Whether `key` is acceptable for `SecureHashKey` — null, empty, or even-length hex of
at least 16 characters. Use in settings UI before persisting a key.

### Signing

```csharp
var terminal = new EcrTerminal(
    host: "192.168.1.50",
    serialNumber: "P653200085189",
    config: new EcrConfig { SecureHashKey = secureHashKeyFromProtectedStore });
```

The app persists secrets and chooses which one to put on `EcrConfig` for the
selected terminal mode. The SDK only signs and verifies with that value — it
does not store keys. On Windows, keep it encrypted at rest (DPAPI; see the
[guide](integration-guide.md#15-going-to-production)).

**LAN ECR** adds field `secureHash` and a per-request `nonce`, and requires the
answer to be signed and to echo the nonce.
**Web Service ECR** adds field `secureHashValue` to the JSON body. The signing
string is built the same way in both cases; only the field name and secret differ.

Amwal issues keys **per terminal and per transport** — delivered to the terminal
by TMS and to you separately. They are not values to invent or commit to a
repository.

**What you will see if the keys do not match:** every LAN call returns
`Failure.Unauthenticated`. Web Service calls fail at the Hub.

### MinorUnitDigits in detail

Amounts travel as a whole number of the currency's smallest unit:

| Currency                   | Digits | `1.234` goes out as         |
| -------------------------- | ------ | --------------------------- |
| OMR — 1 rial = 1000 baisa  | 3      | `000000001234`              |
| USD — 1 dollar = 100 cents | 2      | `000000000123` (for `1.23`) |

Nothing cross-checks it against `CurrencyCode`. Setting `840` (USD) while leaving
`3` sends every amount ten times too large and neither the SDK nor the terminal
will object.

Amounts you _receive_ are converted back, so `EcrResult.Approved.Amount` is
always in major units.

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

Deployment environment for Web Service ECR. Set it on [EcrConfig.Environment](#ecrconfig).
Hub hostnames and ports are **SDK-internal** and are not documented for integrators —
the app only selects `SIT`, `UAT`, or `PROD`.

|                                     |                                                                                                                     |
| ----------------------------------- | ------------------------------------------------------------------------------------------------------------------- |
| `EcrEnvironments.Default`           | `EcrEnvironment.SIT`                                                                                                |
| `EcrEnvironments.FromName(string?)` | Parses a persisted name, ignoring case and surrounding whitespace; unknown values (and null) fall back to `Default` |

Integrators do **not** configure REST hostnames — only this enum.

---

## EcrTransactionType

```csharp
public enum EcrTransactionType
{
    Sale, Void, Refund, Inquiry, Receipt
}
```

The flags live on the static class `EcrTransactionTypes` as extension methods:

```csharp
public static class EcrTransactionTypes
{
    public static IReadOnlyList<EcrTransactionType> MenuOptions { get; }

    public static string MessageType(this EcrTransactionType type)
    public static string DisplayName(this EcrTransactionType type)
    public static bool RequiresAmount(this EcrTransactionType type)
    public static bool RequiresOriginalStan(this EcrTransactionType type)
    public static bool RequiresOriginalDate(this EcrTransactionType type)
    public static bool MovesMoney(this EcrTransactionType type)
    public static bool AllowsOtherTerminal(this EcrTransactionType type)
}
```

|           | `RequiresAmount()` | `RequiresOriginalStan()` | `RequiresOriginalDate()` | `AllowsOtherTerminal()` | `MovesMoney()` |
| --------- | ------------------ | ------------------------ | ------------------------ | ----------------------- | -------------- |
| `Sale`    | ✔                  |                          |                          |                         | ✔              |
| `Void`    |                    | ✔                        |                          | ✔                       | ✔              |
| `Refund`  | ✔                  | ✔                        | ✔                        | ✔                       | ✔              |
| `Inquiry` |                    | ✔                        | ✔                        | ✔                       |                |
| `Receipt` |                    | ✔                        | ✔                        | ✔                       |                |

| Member                   | Description                                                                                                      |
| ------------------------ | ---------------------------------------------------------------------------------------------------------------- |
| `MessageType()`          | The value carried in the message's `messageType` field: `"SALE"`, `"VOID"`, `"REFUND"`, `"INQUIRY"`, `"RECEIPT"` |
| `DisplayName()`          | A human-readable name: `"Sale"`, …                                                                               |
| `RequiresAmount()`       | Whether the caller supplies an amount                                                                            |
| `RequiresOriginalStan()` | Whether the caller identifies an earlier transaction by receipt number                                           |
| `RequiresOriginalDate()` | Whether the caller supplies the original's date                                                                  |
| `AllowsOtherTerminal()`  | Whether the operation may target another terminal                                                                |
| `MovesMoney()`           | Whether the operation is financial                                                                               |

```csharp
EcrTransactionTypes.MenuOptions   // [Sale, Void, Refund, Inquiry]
```

The operations an operator picks from. `Receipt` is absent by design: a receipt
follows a transaction already on screen rather than being chosen from a list.

What the terminal will _accept_ is a separate question, answered by the terminal —
whether the original exists, whether it is within the void window, whether it may
be voided at all. None of that can be decided here; it needs the payment backend
and the terminal's TMS configuration.

---

## EcrResult

```csharp
public abstract record EcrResult(string MerchantReference)
```

How a transaction ended. The three cases are deliberately distinct because they
call for different things from a till. The compiler cannot prove a `switch`
expression over them exhaustive, so give it a final discard arm.

### EcrResult.Approved

```csharp
public sealed record Approved(
    string MerchantReference,
    string Amount,
    string ResponseCode,
    string Rrn,
    string AuthCode,
    string MaskedPan,
    bool PartialApproval,
    string RequestedAmount,
    string Raw) : EcrResult(MerchantReference)
```

Money was taken.

| Property          | Description                                                              |
| ----------------- | ------------------------------------------------------------------------ |
| `Amount`          | What was **actually taken**, in major units, e.g. `1.234`                |
| `ResponseCode`    | The backend's code, `00` on a plain approval                             |
| `Rrn`             | Retrieval reference number                                               |
| `AuthCode`        | Authorisation code. A placeholder on a void — do not print it            |
| `MaskedPan`       | Masked, never the full number: `543173xxxx5785`. May be empty            |
| `PartialApproval` | The bank authorised less than was asked for. **Not a refusal**           |
| `RequestedAmount` | What was asked for, when `PartialApproval` is set                        |
| `Raw`             | The terminal's full answer as JSON text, for anything not surfaced above |

`Raw` is text rather than a parsed tree so clients are not forced onto this SDK's
JSON library.

### EcrResult.Declined

```csharp
public sealed record Declined(
    string MerchantReference,
    string ResponseCode,
    string Reason,
    NextStep NextStep,
    string Raw) : EcrResult(MerchantReference)
```

The terminal answered and no money was taken. `Reason` carries the backend's own
words where it gave any — its error list in preference to its generic message.
`NextStep` hints whether the till should inquire by merchant reference
(`NextStep.InquireByMerchantReference`) or accept the decline as final
(`NextStep.None`).

**On Web Service, a transport error also arrives here** — usually `ResponseCode ==
"NO_RESPONSE"`, always with `NextStep == InquireByMerchantReference` — that is an
unknown outcome, not a refusal. Check `NextStep`.
See [EcrWebServiceTerminal](#transport-errors-read-this-before-you-branch-on-declined).
On any transport, the terminal's own codes `91` and `94` ask for an inquiry rather
than a retry.

### EcrResult.Failed

```csharp
public sealed record Failed(
    string MerchantReference,
    Failure Failure,
    EcrInquiry? Recovered = null) : EcrResult(MerchantReference)
{
    public NextStep NextStep { get; }
    public bool Settled { get; }
}
```

The exchange itself failed, so the outcome is unknown. A `Failure.Timeout` in
particular does **not** mean the transaction did not happen — see [Failure](#failure).

When [EcrConfig.AutoInquireOnFailure](#ecrconfig) is `true` (default) on **LAN /
USB**, [EcrTerminal](#ecrterminal) follows an unknown failure with
`InquireByReferenceAsync()` using the request's `MerchantReference`. If the terminal
answers, `Recovered` holds the finding. Properties on `Failed`:

| Property    | Description                                                                                                                                        |
| ----------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Failure`   | Why the exchange failed                                                                                                                            |
| `Recovered` | Follow-up inquiry result, or null                                                                                                                  |
| `NextStep`  | Always `NextStep.InquireByMerchantReference`                                                                                                       |
| `Settled`   | `true` when `Recovered is EcrInquiry.Found`. It means the transaction was **found** — read its `Status`; a found sale can still have been declined |

On Web Service, `EcrResult.Failed` occurs only for non-numeric `MerchantId` /
`TerminalId` (nothing was sent; `MerchantReference` is empty). A Web Service
transport failure is a `Declined` with `NextStep == InquireByMerchantReference`.

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

The answer to "what became of this transaction". Kept apart from `EcrResult`
because it reports on a transaction rather than performing one: an inquiry that
succeeds says nothing about whether money moved — that is
`EcrTransaction.Status`.

| Case       | Properties                                   | Meaning                                                                                                                                       |
| ---------- | -------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------- |
| `Found`    | `Transaction: EcrTransaction`, `Raw: string` | The transaction was found                                                                                                                     |
| `NotFound` | `Reason: string`, `Raw: string`              | No such transaction on that terminal for that day — a real answer                                                                             |
| `Failed`   | `Failure: Failure`                           | The exchange failed (on Web Service, a transport failure arrives as `Failure.ConnectionLost`). Harmless to retry — an inquiry changes nothing |

---

## EcrTransaction

```csharp
public sealed record EcrTransaction(
    string TransactionId,
    string Stan,
    string Type,
    string Status,
    string Amount,
    string TotalAmount,
    string Currency,
    string TransactionTime,
    string MaskedPan,
    string CardHolderName,
    string Rrn,
    string AuthCode,
    string BatchId,
    string TerminalId,
    bool IsRefunded,
    bool CanVoid,
    bool CanRefund,
    bool PartialApproval = false,
    string AuthorizedAmount = "")
```

A transaction as the terminal's backend records it.

| Property                                      | Description                                                                                        |
| --------------------------------------------- | -------------------------------------------------------------------------------------------------- |
| `TransactionId`                               | The backend's identifier, unique across terminals and days                                         |
| `Stan`                                        | The receipt number, unique only within a terminal's day                                            |
| `Type`                                        | `Purchase`, `Void`, `Refund` — as the backend names it                                             |
| `Status`                                      | What became of it: `Approved`, `Declined`, `Voided`                                                |
| `Amount` / `TotalAmount`                      | Major units, e.g. `0.258`. `Amount` is what was requested; `TotalAmount` includes tips and fees    |
| `Currency`                                    | `OMR`                                                                                              |
| `TransactionTime`                             | As the backend recorded it: `2026-08-09T16:58:16.804271`                                           |
| `MaskedPan` / `CardHolderName`                | Never the full number. May be empty                                                                |
| `Rrn` / `AuthCode` / `BatchId` / `TerminalId` |                                                                                                    |
| `IsRefunded`                                  | Whether it has already been refunded                                                               |
| `CanVoid` / `CanRefund`                       | What the terminal would still accept — the void window and the backend's rules are already applied |
| `PartialApproval`                             | The issuer approved less than the sale asked for                                                   |
| `AuthorizedAmount`                            | What was actually authorised. Equal to `Amount` unless partially approved                          |

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

| Case          | Properties                      | Meaning                                                                                                           |
| ------------- | ------------------------------- | ----------------------------------------------------------------------------------------------------------------- |
| `Ready`       | `Url: string`, `Raw: string`    | Put `Url` in a QR code as it stands                                                                               |
| `Unavailable` | `Reason: string`, `Raw: string` | Not found, or the backend would not publish one; also what `EcrOpenedSession.ReceiptAsync` returns on Web Service |
| `Failed`      | `Failure: Failure`              | The exchange failed. Safe to retry                                                                                |

---

## Failure

```csharp
public abstract record Failure(string Message)
{
    public bool OutcomeUnknown { get; }

    public sealed record Unreachable(string Message)
    public sealed record Timeout(string Message)
    public sealed record Malformed(string Message)
    public sealed record ConnectionLost(string Message)
    public sealed record Unauthenticated(string Message)
}
```

Why an exchange could not be completed. `OutcomeUnknown` is `false` only for
[Unreachable](#failure) — everything else means the request may have reached the
terminal and the payment outcome is not settled.

| Case              | Meaning                                                                                               | Did the payment happen?   |
| ----------------- | ----------------------------------------------------------------------------------------------------- | ------------------------- |
| `Unreachable`     | Nothing is listening: wrong address, terminal off, different network, or the **connect** timed out    | **No** — nothing was sent |
| `Timeout`         | The request was written but the terminal never answered within `ResponseTimeout`                      | **Unknown**               |
| `ConnectionLost`  | The connection reset, aborted or closed after the request was written, or part way through the answer | **Unknown**               |
| `Unauthenticated` | LAN: answer failed signature or nonce check                                                           | **Unknown**               |
| `Malformed`       | The answer could not be read (empty, or not JSON)                                                     | **Unknown**               |

> **`Timeout`, `ConnectionLost` and `Unauthenticated` do not mean the payment
> did not happen.** On LAN, the terminal may have completed it and been unable to
> say so. Treating any of them as a decline and retrying is how a customer gets
> charged twice. Resolve with `InquireAsync()` / `InquireByReferenceAsync()` on
> either transport, or reconcile against the terminal before retrying.

`Unauthenticated` on LAN almost always means a mismatched **Wi‑Fi/LAN** key. On
Web Service, authentication failures come from the Hub (wrong **Web Service** key
or environment) and reach you through the Hub's answer as a `Declined`, never as `Unauthenticated`.

`EcrWebServiceTerminal` never produces `Timeout`, `Unreachable` or `Unauthenticated`.
Its only uses of `Failure` are `Malformed` (non-numeric `MerchantId` / `TerminalId`)
and, on `InquireAsync` / `InquireByReferenceAsync`, `ConnectionLost` for an
inquiry whose own request hit a transport failure (`EcrInquiry.Failed`).

**A custom channel decides which of these it gets**: the SDK classifies an
`IOException` from an `IEcrChannel` by its message — `closed the connection` →
`ConnectionLost`; `not valid JSON` or `empty message` → `Malformed`; anything else
→ `Unreachable`. See [IEcrChannel](#iecrchannel).

---

## NextStep

```csharp
public enum NextStep
{
    None,
    InquireByMerchantReference,
}
```

What the till should do when an outcome cannot be acted on directly.

| Value                        | Meaning                                                                                                |
| ---------------------------- | ------------------------------------------------------------------------------------------------------ |
| `None`                       | The answer settles the matter                                                                          |
| `InquireByMerchantReference` | Look up the transaction by the reference this request was sent with — **never retry the sale instead** |

Exposed on [EcrResult.Declined](#ecrresultdeclined) (from the terminal when it
knows the outcome is uncertain) and on [EcrResult.Failed](#ecrresultfailed) (always
`InquireByMerchantReference`). An unrecognised wire value reads as `None`.

---

## EcrReachability

```csharp
public sealed record EcrReachability(
    bool Reachable,
    string Host,
    int Port,
    string? Error = null,
    string? Endpoint = null)
{
    public string Endpoint { get; init; }
}
```

Result of [EcrTerminal.ProbeReachabilityAsync](#probereachabilityasync). When
`Reachable` is `false`, `Error` carries the platform message (`connection refused`,
timed out, host unreachable, …).

`Endpoint` is what to show an operator: `host:port` for a socket, the channel's own
name for a link with no address (a cable fills `Host` and `Port` with what it has,
`Port` being 0). It defaults to `Host:Port` when `Port > 0`, otherwise `Host`.

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

How bytes reach a terminal, and nothing else. Signing the request, checking the
answer and reading response codes stay in [EcrTerminal](#ecrterminal) and are
identical however the bytes travel — a channel is never asked to decide
anything, so a new one cannot weaken what is already there.

| Member          | Description                                                                                                                                                       |
| --------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Endpoint`      | What to show an operator when this link fails                                                                                                                     |
| `Host` / `Port` | The address side of [EcrReachability](#ecrreachability). A cable has no host and no port: it reports its own name and a port of `0`                               |
| `ProbeAsync`    | Whether the terminal can be reached. Returns `null` when reachable, otherwise the reason                                                                          |
| `ExchangeAsync` | Sends one request body and returns the answer body. **Framing is the channel's to apply** — use [EcrFrames](#ecrframes). `timeout` is `EcrConfig.ResponseTimeout` |

`TcpEcrChannel` ships with the SDK. **There is no USB channel** — implement this
interface for your cable. A worked skeleton is in the
[integration guide](integration-guide.md#usb-cable-ecr--bring-your-own-channel).

**Timeouts are not failures.** Throw [EcrChannelTimeout](#ecrchanneltimeout) when
the terminal did not answer: the payment may have completed and the terminal been
unable to say so, which the caller handles differently from a decline.

**Exceptions from `ExchangeAsync` are classified by the SDK:**

| Thrown                                                                   | Becomes                                        |
| ------------------------------------------------------------------------ | ---------------------------------------------- |
| `EcrChannelTimeout`                                                      | `Failure.Timeout`                              |
| `IOException` whose message contains `closed the connection`             | `Failure.ConnectionLost`                       |
| `IOException` whose message contains `not valid JSON` or `empty message` | `Failure.Malformed`                            |
| any other `IOException`                                                  | **`Failure.Unreachable` — "nothing was sent"** |
| `OperationCanceledException` (your token)                                | rethrown, not a result                         |
| anything else                                                            | propagates out of the operation method         |

Once the request may have been written, wrap a broken link as
`new IOException("Terminal closed the connection: …")`, as `TcpEcrChannel` does —
otherwise a lost payment is reported as safe to retry. Before anything is written,
a plain `IOException` is right.

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

    public Task<string?> ProbeAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    public Task<byte[]> ExchangeAsync(byte[] body, TimeSpan timeout, CancellationToken cancellationToken = default)
}
```

The terminal over TCP. One connection carries one request and one answer:
connect, send, wait for the cardholder, read the result, disconnect. Normally you
do not construct it — `new EcrTerminal(host, …)` and `EcrSessions.LanTerminal` do,
using `EcrConfig.ConnectTimeout` (10 s), not this class's own 3 s default.

Failure behaviour, which `EcrTerminal` maps to `Failure`:

| Situation                                   | Thrown                                                              | Result           |
| ------------------------------------------- | ------------------------------------------------------------------- | ---------------- |
| Refused, no route, unknown host             | `IOException`                                                       | `Unreachable`    |
| Connect timeout                             | `IOException("Connection timed out after N ms")`                    | `Unreachable`    |
| Request written, no answer within `timeout` | `EcrChannelTimeout`                                                 | `Timeout`        |
| Reset/abort after the request was written   | `IOException("Terminal closed the connection: …")`                  | `ConnectionLost` |
| Closed while sending the header or body     | `IOException("Terminal closed the connection while sending the …")` | `ConnectionLost` |
| Empty message                               | `IOException("Terminal returned an empty message")`                 | `Malformed`      |
| Your token cancelled                        | `OperationCanceledException`                                        | rethrown         |

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

Two bytes of big-endian length, then UTF-8 JSON. Public so a channel outside
this library frames the same way — a second private implementation of "two
bytes then the body" is a second chance to get it subtly wrong. Note that one
read is not one message: a transfer may carry several, or half of one.

| Member                     | Description                                                                                                                         |
| -------------------------- | ----------------------------------------------------------------------------------------------------------------------------------- |
| `Wrap(body)`               | Puts the length header in front of `body`. Throws `IOException` if `body` is longer than `MaxBodyBytes`                             |
| `BodyLength(header)`       | The body length a 2-byte header announces                                                                                           |
| `ReadBody(input)`          | Reads exactly one message body, **blocking** until it arrives or the stream's own read timeout fires. Do not call it on a UI thread |
| `ReadBodyAsync(input, ct)` | Asynchronous `ReadBody`                                                                                                             |

`ReadBody` / `ReadBodyAsync` throw `IOException("Terminal closed the connection
while sending the …")` if the stream ends early and `IOException("Terminal returned
an empty message")` for a zero-length body; both are classified as described under
[IEcrChannel](#iecrchannel).

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

The SDK has no logging framework of its own, so a client can route this to
`Microsoft.Extensions.Logging`, Serilog, the Windows Event Log or a plain file, and
none inherits a dependency it did not ask for. `From(null)` throws
`ArgumentNullException`.

```csharp
EcrLogger.From(message => Debug.WriteLine(message, "Ecr"));                    // LAN
EcrLogger.From(message => Debug.WriteLine(message, "EcrWebService"));          // Web Service
EcrLogger.From(message => logger.LogDebug("{EcrMessage}", message));           // Microsoft.Extensions.Logging
EcrLogger.From(message => Log.Debug("{EcrMessage}", message));                 // Serilog
```

Messages include signing diagnostics. **No part of a secret is ever logged** — a key is
reported only as `configured` or `not configured`. Payload strings and hashes may still
appear; treat them as transaction records.

---

## EcrWireResponse

```csharp
public sealed record EcrWireResponse(
    bool Success,
    string ResponseCode,
    string Message,
    JsonObject? Data,
    IReadOnlyList<string> ErrorList,
    string Nonce = "",
    string SecureHash = "")
{
    public string MerchantReference { get; }
    public string TerminalSerial { get; }
    public string DisplayMessage { get; }

    public static EcrWireResponse Parse(JsonObject json)
    public static string DisplayMessageFromRaw(string raw, string fallback = "")
}
```

The unified ECR response envelope — `{ success, responseCode, message, data,
errorList, nonce?, secureHash? }` — read from the terminal's or Hub's JSON.
`JsonObject` is `System.Text.Json.Nodes.JsonObject`. You rarely need this: the
result types already carry the useful readings. It is handy for inspecting a `Raw`
string.

| Member                                 | Description                                                                                                              |
| -------------------------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| `Parse(JsonObject)`                    | Reads the wire JSON, including legacy flat shapes (`approved`, `responseMessage`) and answers nested under `ecrResponse` |
| `DisplayMessage`                       | User-facing text: `ErrorList` entries first (joined by newlines), then `Message`                                         |
| `MerchantReference`                    | From `Data` (`merchantReference`, then `merchantReferenceId`, then `requestId`); empty if none                           |
| `TerminalSerial`                       | `Data`'s `terminalSerial`, or empty                                                                                      |
| `DisplayMessageFromRaw(raw, fallback)` | `DisplayMessage` of a raw JSON string; `fallback` when it is empty or invalid                                            |

```csharp
var wire = EcrWireResponse.Parse((JsonObject)JsonNode.Parse(declined.Raw)!);   // declined: EcrResult.Declined
bool noResponse = wire.ResponseCode == "NO_RESPONSE";   // but branch on Declined.NextStep, not on this
```

---

## Web Service request types

Namespace `AmwalPay.Ecr.WebService`. Public, optional building blocks for advanced
integrators who want the signed JSON body without the terminal classes — mainly for
tests and custom HTTP clients. [EcrWebServiceTerminal](#ecrwebserviceterminal)
builds its requests internally and auto-fills timestamps.

```csharp
public interface IEcrWebServiceRequest
{
    long MerchantId { get; }
    long TerminalId { get; }
    string TerminalSerial { get; }
    string MerchantReference { get; }
}

public sealed record EcrWebServiceSaleRequest(
    long MerchantId,
    long TerminalId,
    string TerminalSerial,
    long AmountMinorUnits,
    string MerchantReference = "",
    string CurrencyCode = "512",
    string EcrId = "ECR01",
    string TransactionDateTime = "") : IEcrWebServiceRequest;

public sealed record EcrWebServiceVoidRequest(
    long MerchantId,
    long TerminalId,
    string TerminalSerial,
    string Stan,
    string MerchantReference = "",
    string CurrencyCode = "512",
    string EcrId = "ECR01",
    string TransactionDateTime = "") : IEcrWebServiceRequest;

public sealed record EcrWebServiceRefundRequest(
    long MerchantId,
    long TerminalId,
    string TerminalSerial,
    long AmountMinorUnits,
    string Stan,
    string OriginalTransactionDate,
    string MerchantReference = "",
    string CurrencyCode = "512",
    string EcrId = "ECR01",
    string TransactionDateTime = "") : IEcrWebServiceRequest;

public sealed record EcrWebServiceTransactionStatusRequest(
    long MerchantId,
    long TerminalId,
    string TerminalSerial,
    string Stan,
    string OriginalTransactionDate,
    string MerchantReference = "",
    string CurrencyCode = "512",
    string EcrId = "ECR01",
    string TransactionDateTime = "") : IEcrWebServiceRequest;

public static class EcrWebServiceRequestJson
{
    public static JsonObject Sale(EcrWebServiceSaleRequest request, string secureHashKey = "")
    public static JsonObject Void(EcrWebServiceVoidRequest request, string secureHashKey = "")
    public static JsonObject Refund(EcrWebServiceRefundRequest request, string secureHashKey = "")
    public static JsonObject TransactionStatus(EcrWebServiceTransactionStatusRequest request, string secureHashKey = "")
    public static JsonObject Inquiry(EcrWebServiceTransactionStatusRequest request, string secureHashKey = "")   // same as TransactionStatus
}
```

| Property                  | Description                                                  |
| ------------------------- | ------------------------------------------------------------ |
| `AmountMinorUnits`        | Amount in minor units (OMR: 3 decimals, e.g. `111` → 0.111)  |
| `Stan`                    | Receipt number of the original transaction                   |
| `OriginalTransactionDate` | Original transaction date as `yyyyMMdd`                      |
| `TransactionDateTime`     | `yyyyMMddHHmmss`; left empty, the current local time is used |

The positional order differs from the Android SDK's (`AmountMinorUnits` comes before
`MerchantReference` here) — use named arguments.

```csharp
var request = new EcrWebServiceSaleRequest(
    MerchantId: 13593,
    TerminalId: 101311,
    TerminalSerial: "P653200008251-Web",
    AmountMinorUnits: 1234,
    MerchantReference: "REF-1586");

JsonObject body = EcrWebServiceRequestJson.Sale(request, secureHashKey: webServiceKey);
// POST body.ToJsonString() to <Hub base>/Ecr/Sale
```

With a non-empty `secureHashKey` the body carries `secureHashValue`; with the
default empty key it is unsigned. A key that cannot be decoded throws
`ArgumentException`. The wire format and paths are in the
[protocol](protocol.md#9-web-service-rest).

---

## Appendix: Kotlin → C# name map

For teams that already use the Android SDK (`com.amwalpay.ecr`). The wire protocol
is identical; the .NET API follows .NET conventions.

| Kotlin                                                                                                                                                                                      | C#                                                                                                                                                                                                                        |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `package com.amwalpay.ecr`                                                                                                                                                                  | `namespace AmwalPay.Ecr` (Web Service request types: `AmwalPay.Ecr.WebService`)                                                                                                                                           |
| `suspend fun sale(amount, merchantReference)`                                                                                                                                               | `Task<EcrResult> SaleAsync(decimal amount, string merchantReference = "", CancellationToken cancellationToken = default)`                                                                                                 |
| `void`, `refund`, `inquire`, `inquireByReference`, `receipt`, `run`                                                                                                                         | `VoidAsync`, `RefundAsync`, `InquireAsync`, `InquireByReferenceAsync`, `ReceiptAsync`, `RunAsync`                                                                                                                         |
| `isReachable()`, `probeReachability()`                                                                                                                                                      | `IsReachableAsync()`, `ProbeReachabilityAsync()`                                                                                                                                                                          |
| `BigDecimal`                                                                                                                                                                                | `decimal`                                                                                                                                                                                                                 |
| `kotlin.time.Duration` (`120.seconds`)                                                                                                                                                      | `TimeSpan` (`TimeSpan.FromSeconds(120)`)                                                                                                                                                                                  |
| `Dispatchers.IO` / `io:` parameter                                                                                                                                                          | none — `await` it; no dispatcher parameter                                                                                                                                                                                |
| coroutine cancellation                                                                                                                                                                      | `CancellationToken` (throws `OperationCanceledException`)                                                                                                                                                                 |
| `IllegalArgumentException`                                                                                                                                                                  | `ArgumentException`                                                                                                                                                                                                       |
| `EcrTerminal(host, serialNumber, config, logger, io)`                                                                                                                                       | `new EcrTerminal(host, serialNumber, config, logger)`; also `new EcrTerminal(IEcrChannel channel, …)`                                                                                                                     |
| `EcrConfig(ecrId = …, secureHashKey = …)` (constructor)                                                                                                                                     | `new EcrConfig { EcrId = …, SecureHashKey = … }` (`init` properties)                                                                                                                                                      |
| `config.copy(responseTimeout = …)`                                                                                                                                                          | `config with { ResponseTimeout = … }`                                                                                                                                                                                     |
| `EcrConfig.DEFAULT_PORT`                                                                                                                                                                    | `EcrConfig.DefaultPort`                                                                                                                                                                                                   |
| `EcrConfig.isValidSecureHashKey(key)`                                                                                                                                                       | `EcrConfig.IsValidSecureHashKey(key)`                                                                                                                                                                                     |
| `ecrId`, `currencyCode`, `minorUnitDigits`, `port`, `connectTimeout`, `responseTimeout`, `probeTimeout`, `secureHashKey`, `merchantId`, `terminalId`, `environment`, `autoInquireOnFailure` | the same names in PascalCase: `EcrId`, `CurrencyCode`, `MinorUnitDigits`, `Port`, `ConnectTimeout`, `ResponseTimeout`, `ProbeTimeout`, `SecureHashKey`, `MerchantId`, `TerminalId`, `Environment`, `AutoInquireOnFailure` |
| `EcrSessions.plan(link, config, rawSecureHashKey)`                                                                                                                                          | `EcrSessions.Plan(link, config, rawSecureHashKey)`                                                                                                                                                                        |
| `EcrSessions.open(…, usbChannel, logger)`                                                                                                                                                   | `EcrSessions.Open(terminalSerial, plan, usbChannel, logger)`                                                                                                                                                              |
| `EcrSessions.lanTerminal` / `usbCableTerminal` / `webServiceTerminal`                                                                                                                       | `LanTerminal` / `UsbCableTerminal` / `WebServiceTerminal`                                                                                                                                                                 |
| `EcrLink.Lan(host, port)`                                                                                                                                                                   | `new EcrLink.Lan(Host, Port)`                                                                                                                                                                                             |
| `EcrLink.UsbCable` (object)                                                                                                                                                                 | `new EcrLink.UsbCable()`                                                                                                                                                                                                  |
| `EcrLink.WebService(merchantId, terminalId)`                                                                                                                                                | `new EcrLink.WebService(MerchantId, TerminalId)`                                                                                                                                                                          |
| `plan.isReady`, `plan.issues`, `usesLan`, `usesWebService`, `usesUsbCable`, `signingKeyLabel`, `signingKeyConfigured`, `connectionSummary`                                                  | `IsReady`, `Issues`, `UsesLan`, `UsesWebService`, `UsesUsbCable`, `SigningKeyLabel`, `SigningKeyConfigured`, `ConnectionSummary`                                                                                          |
| `EcrOpenedSession.probeReachability()` → `EcrReachability?`                                                                                                                                 | `ProbeReachabilityAsync()` → `Task<EcrReachability?>`                                                                                                                                                                     |
| `EcrOpenedSession.inquire(receiptNumber, transactionDate, originalTerminalId)`                                                                                                              | `InquireAsync(receiptNumber, transactionDate, originalTerminalId, cancellationToken)` (no `merchantReference`)                                                                                                            |
| `EcrEnvironment.DEFAULT`                                                                                                                                                                    | `EcrEnvironments.Default`                                                                                                                                                                                                 |
| `EcrEnvironment.fromName(name)`                                                                                                                                                             | `EcrEnvironments.FromName(name)`                                                                                                                                                                                          |
| `EcrTransactionType.SALE`, `VOID`, `REFUND`, `INQUIRY`, `RECEIPT`                                                                                                                           | `EcrTransactionType.Sale`, `Void`, `Refund`, `Inquiry`, `Receipt`                                                                                                                                                         |
| `type.messageType`, `displayName`, `requiresAmount`, `requiresOriginalStan`, `requiresOriginalDate`, `allowsOtherTerminal`, `movesMoney`                                                    | `type.MessageType()`, `DisplayName()`, `RequiresAmount()`, `RequiresOriginalStan()`, `RequiresOriginalDate()`, `AllowsOtherTerminal()`, `MovesMoney()` (extension methods)                                                |
| `EcrTransactionType.menuOptions`                                                                                                                                                            | `EcrTransactionTypes.MenuOptions`                                                                                                                                                                                         |
| `EcrResult.Approved(merchantReference, amount, …)` (`sealed interface`, data classes)                                                                                                       | `EcrResult.Approved(MerchantReference, Amount, …)` (abstract `record`, sealed nested records)                                                                                                                             |
| `when (result) { is EcrResult.Approved -> … }`                                                                                                                                              | `switch (result) { case EcrResult.Approved a: … }` or `result switch { EcrResult.Approved a => … }`                                                                                                                       |
| `result.recovered`, `result.settled`, `result.nextStep`                                                                                                                                     | `f.Recovered`, `f.Settled`, `f.NextStep`                                                                                                                                                                                  |
| `NextStep.NONE`, `NextStep.INQUIRE_BY_MERCHANT_REFERENCE`                                                                                                                                   | `NextStep.None`, `NextStep.InquireByMerchantReference`                                                                                                                                                                    |
| `Failure.Unreachable`, `Timeout`, `ConnectionLost`, `Malformed`, `Unauthenticated`                                                                                                          | same names, nested in `Failure`; `failure.outcomeUnknown` → `Failure.OutcomeUnknown`                                                                                                                                      |
| `EcrInquiry.Found` / `NotFound` / `Failed`; `EcrReceipt.Ready` / `Unavailable` / `Failed`                                                                                                   | same names; `Transaction`, `Raw`, `Reason`, `Url`, `Failure` in PascalCase                                                                                                                                                |
| `EcrTransaction.isRefunded`, `canVoid`, `canRefund`                                                                                                                                         | `IsRefunded`, `CanVoid`, `CanRefund`                                                                                                                                                                                      |
| `EcrReachability(reachable, host, port, error)`                                                                                                                                             | `EcrReachability(Reachable, Host, Port, Error, Endpoint)` — `Endpoint` added                                                                                                                                              |
| `EcrChannel { endpoint; probe(timeout): String?; exchange(body, timeout): ByteArray }`                                                                                                      | `IEcrChannel { Endpoint; Host; Port; ProbeAsync(timeout, ct): Task<string?>; ExchangeAsync(body, timeout, ct): Task<byte[]> }` — `Host` / `Port` added, methods async                                                     |
| `EcrChannelTimeout`                                                                                                                                                                         | `EcrChannelTimeout : IOException`                                                                                                                                                                                         |
| `EcrFrames.wrap`, `bodyLength`, `readBody(InputStream)`                                                                                                                                     | `EcrFrames.Wrap`, `BodyLength`, `ReadBody(Stream)`, plus `ReadBodyAsync`, `HeaderBytes`, `MaxBodyBytes`                                                                                                                   |
| `TcpEcrChannel`                                                                                                                                                                             | `TcpEcrChannel` (`new TcpEcrChannel(host, port, connectTimeout)`)                                                                                                                                                         |
| `UsbAccessoryEcrChannel` (app code, Android)                                                                                                                                                | none — supply your own `IEcrChannel`                                                                                                                                                                                      |
| `fun interface EcrLogger { fun debug(message) }`, `EcrLogger.None`, `EcrLogger { … }`                                                                                                       | `interface IEcrLogger { void Debug(string message); }`, `EcrLogger.None`, `EcrLogger.From(message => …)`                                                                                                                  |
| `EcrWireResponse`                                                                                                                                                                           | `EcrWireResponse` (same; `Data` is `System.Text.Json.Nodes.JsonObject?`)                                                                                                                                                  |
| `EcrWebServiceRequestJson.sale(request, key)`                                                                                                                                               | `EcrWebServiceRequestJson.Sale(request, secureHashKey)`                                                                                                                                                                   |
| `EcrWebServiceSaleRequest(merchantId, terminalId, terminalSerial, merchantReference, amountMinorUnits, …)`                                                                                  | `EcrWebServiceSaleRequest(MerchantId, TerminalId, TerminalSerial, AmountMinorUnits, MerchantReference, …)` — **order differs**                                                                                            |
| `EcrWebServiceRequest`                                                                                                                                                                      | `IEcrWebServiceRequest`                                                                                                                                                                                                   |
| Log tags (`Log.d("Ecr", …)`)                                                                                                                                                                | `Debug.WriteLine(message, "Ecr")` or your logging framework                                                                                                                                                               |
