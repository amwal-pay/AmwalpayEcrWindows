# Integration guide

From an empty project to a working sale, then what you need to ship it. For the
signature of every type, see the [API reference](api-reference.md); for the bytes
on the wire, the [protocol](protocol.md).

This guide is for .NET developers building a Windows till (WPF, WinForms, console,
Windows Service) or a web backend (ASP.NET Core, classic ASP.NET) against an Amwal
POS terminal. It matches package **1.0.2**.

**Contents**

1. [Before you start](#1-before-you-start)
2. [Add the SDK](#2-add-the-sdk)
3. [Connect to a terminal](#3-connect-to-a-terminal)
4. [Take a payment](#4-take-a-payment)
5. [Void, refund, inquiry, receipt, close receipt](#5-void-refund-inquiry-receipt-close-receipt)
   - [Copy-paste examples: Wi-Fi and Web Service](#5a-copy-paste-examples-wi-fi-and-web-service)
6. [Handling outcomes](#6-handling-outcomes)
7. [When the answer never comes](#7-when-the-answer-never-comes)
8. [LAN and Web Service compared](#8-lan-and-web-service-compared)
9. [Threading and lifecycle](#9-threading-and-lifecycle)
10. [Logging](#10-logging)
11. [Building your own form](#11-building-your-own-form)
12. [Testing without hardware](#12-testing-without-hardware)
13. [Going to production](#13-going-to-production)

---

## 1. Before you start

**A terminal in ECR mode.** Amwal's TMS decides this, not your application. The
terminal profile needs:

| Field | Value |
|---|---|
| `terminalMode` | `1` (ECR-attached) |
| `ecrMode` | `1` USB cable, `2` Wi-Fi, `4` Web Service |

Until `terminalMode` is `1` the terminal does not accept ECR requests. With
`ecrMode: 2` it listens on TCP port 9100; with `ecrMode: 4` it does not listen at all
and the till talks HTTPS to the Amwal Hub instead. Bluetooth (`ecrMode: 3`) is not
served by this SDK.

| `ecrMode` | Transport | SDK client | Link |
|---|---|---|---|
| `2` Wi-Fi | TCP to the terminal's IP, port 9100 | [EcrTerminal](api-reference.md#ecrterminal) | [EcrLink.Lan](api-reference.md#ecrlink) |
| `1` USB cable | A cable. **You supply the channel**; the package ships none | [EcrTerminal](api-reference.md#ecrterminal) | `EcrLink.UsbCable` + your [IEcrChannel](api-reference.md#iecrchannel) |
| `4` Web Service | HTTPS to the Amwal Hub | [EcrWebServiceTerminal](api-reference.md#ecrwebserviceterminal) | `EcrLink.WebService` |

**Network.** For Wi-Fi ECR the till must be able to route to the terminal on TCP 9100
(the till may be on Ethernet). See
[Windows network and host](troubleshooting.md#windows-network-and-host). Web Service
ECR only needs internet access from the till or server.

**The terminal's address.** With `ecrMode: 2` the terminal shows its IP and port
under the card scheme logos on its idle screen. Store terminals by **serial number**
and look the address up when you send; a terminal can come back from Wi-Fi on a
different IP.

**Runtime.** .NET Framework 4.6.2 or later (4.7.2 recommended) or .NET 8 or later,
with C# 9 or later. The full matrix and end-of-support dates are in
[Compatibility](compatibility.md#supported-platforms).

---

## 2. Add the SDK

```powershell
dotnet add package AmwalPay.Ecr --version 1.0.2
```

```xml
<PackageReference Include="AmwalPay.Ecr" Version="[1.0.2]" />
```

**Pin an exact version.** Published versions never change, so a fix arrives as a new
version and you decide when to take it. The brackets mean "exactly this version"; a
bare `Version="1.0.2"` means "1.0.2 or higher". Consider
`<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>`. 1.0.2 is the
version to pin once it is published; `CloseReceiptAsync` needs it.

**Language version.** Results are records and `EcrConfig` uses `init` properties, so
building a config or using `with` needs C# 9. SDK-style .NET Framework projects
default to C# 7.3 and fail with `CS8370` (C# 8 gives `CS8400`). Set:

```xml
<PropertyGroup>
  <LangVersion>9.0</LangVersion>   <!-- or: latest -->
</PropertyGroup>
```

You need Visual Studio 2019 16.8+ or the .NET SDK 5+. Reading results
(`is EcrResult.Approved a`, `switch`) needs only C# 7.

**On .NET Framework** the `netstandard2.0` asset brings `System.Text.Json` and its
dependencies, so the executable needs binding redirects (SDK-style projects generate
them), and Web Service ECR on an app targeting a version before 4.7 needs TLS 1.2
enabled (see [Web Service on classic .NET Framework](#web-service-on-classic-net-framework)).
Both are documented .NET Framework behaviour that this repository's tests did not
run; see [Compatibility](compatibility.md#net-framework).

---

## 3. Connect to a terminal

The SDK separates **how you reach a terminal** ([EcrLink](api-reference.md#ecrlink))
from **credentials and till settings** ([EcrConfig](api-reference.md#ecrconfig)).
[EcrSessions.Plan](api-reference.md#ecrsessions) validates both without sending
anything and returns issues instead of throwing, so your UI can show what is missing.

### Keys and settings

Amwal issues **two different secrets**: one for Wi-Fi/LAN and USB cable, one for Web
Service. Keep them in application settings, not on the terminal record, and pass the
one for the selected mode:

| Setting | Used when |
|---|---|
| Wi-Fi/LAN secure hash key | `ecrMode` 1 or 2 |
| Web Service secure hash key, plus SIT / UAT / PROD | `ecrMode` 4 |

| Terminal record (per device) | Fields |
|---|---|
| Wi-Fi | name, serial, IP, port |
| Web Service | name, serial, merchant ID, terminal ID |

Both keys go through the same property, `EcrConfig.SecureHashKey`. A key must be an
even-length hex string of at least 16 characters.

`EcrConfig` validates on assignment: a key with a stray space, a `Port` of 0 or a
`MinorUnitDigits` of 7 throws `ArgumentException` immediately. When validating text a
user typed, pass it as `rawSecureHashKey` to `Plan` (and put an empty or already-valid
key on the config) so a bad value becomes a `plan.Issues` entry. It is the same
secret, not a second one. `EcrConfig.IsValidSecureHashKey(key)` is available for
settings screens.

A plan is never ready without a key: `Plan` adds *"Wi-Fi/LAN secure hash key is not
configured"* (or the Web Service equivalent). See [section 12](#12-testing-without-hardware)
for the test-only way around that.

### Wi-Fi / LAN

```csharp
var plan = EcrSessions.Plan(
    link: new EcrLink.Lan(Host: "192.168.1.50", Port: 9100),
    config: new EcrConfig
    {
        SecureHashKey = secrets.LanKeyFor(serialNumber),
        EcrId = "TILL-04",
    });

if (!plan.IsReady)
{
    screen.Show(string.Join("\n", plan.Issues));
    return;
}

EcrTerminal terminal = EcrSessions.LanTerminal(
    terminalSerial: "P653200085189",
    plan: plan,
    logger: EcrLogger.From(message => Debug.WriteLine(message, "Ecr")));
```

One instance addresses one terminal, holds no connection between calls, and is safe
to keep for the life of the application.

Everything else has a default. Change the till's identity, currency or timeouts on
`EcrConfig`:

```csharp
new EcrConfig
{
    EcrId = "TILL-04",                      // appears on the terminal's records
    CurrencyCode = "512",                   // ISO 4217 numeric; 512 is OMR
    MinorUnitDigits = 3,                    // decimal places the currency has
    ConnectTimeout = TimeSpan.FromSeconds(10),
    ResponseTimeout = TimeSpan.FromSeconds(120),
}
```

`EcrConfig` is a record: derive variants with
`config with { ResponseTimeout = TimeSpan.FromSeconds(60) }`.

> **`MinorUnitDigits` must match your currency.** It decides where the decimal point
> falls on the wire. Setting `CurrencyCode = "840"` (USD) and leaving `3` sends every
> amount ten times too large, and neither the SDK nor the terminal objects.

**Probe before you pay (LAN / USB only).** `ProbeReachabilityAsync()` opens a TCP
handshake and returns whether the terminal answered, with the underlying error when
it did not:

```csharp
EcrReachability probe = await terminal.ProbeReachabilityAsync();
if (!probe.Reachable)
{
    screen.Show($"{probe.Endpoint} - {probe.Error ?? "not reachable"}");
    return;
}
```

`IsReachableAsync()` returns just `probe.Reachable`. The probe proves the port is
open, not that the terminal is idle.

### Web Service

Do this once per terminal, or whenever credentials change. Nothing is sent to the Hub
until the first operation.

**Prerequisites from Amwal / TMS:** the terminal profile (`terminalMode: 1`,
`ecrMode: 4`); the terminal serial as registered; numeric merchant ID and terminal ID;
the environment (SIT, UAT or PROD); the Web Service secure hash key (not the
Wi-Fi/LAN key). On .NET Framework targeting a version before 4.7, enable TLS 1.2.

```csharp
var plan = EcrSessions.Plan(
    link: new EcrLink.WebService(MerchantId: "13593", TerminalId: "101311"),
    config: new EcrConfig
    {
        SecureHashKey = secrets.WebServiceKey,     // not the Wi-Fi/LAN key
        Environment = EcrEnvironment.UAT,
    });

if (!plan.IsReady)
{
    screen.Show(string.Join("\n", plan.Issues));
    return;
}
// plan.ConnectionSummary: "UAT · merchant 13593 · terminal 101311" (no secrets)

EcrWebServiceTerminal terminal = EcrSessions.WebServiceTerminal(
    terminalSerial: "P653200008251",
    plan: plan,
    logger: EcrLogger.From(message => Debug.WriteLine(message, "EcrWebService")));
```

`Plan` copies the IDs from the link onto the resolved config. It may report:

| Issue | Cause |
|---|---|
| `Merchant ID is required for Web Service` / `Terminal ID is required for Web Service` | Blank IDs |
| `Merchant ID must be numeric` / `Terminal ID must be numeric` | Non-numeric IDs |
| `Secure hash key must be an even-length hex string of at least 16 characters` | Malformed key |
| `Web Service secure hash key is not configured` | Empty key |

`WebServiceTerminal`, `LanTerminal` and `UsbCableTerminal` do **not** re-check
`IsReady`; only `Open` does. Always gate on `plan.IsReady` yourself, because a plan
that is not ready can carry an empty key and build an unsigned client.

What the SDK sends:

| Item | Value |
|---|---|
| Hub base URL | Resolved inside the SDK from `EcrConfig.Environment`. Hostnames are not configurable and not published |
| Paths | `/Ecr/Sale`, `/Ecr/Void`, `/Ecr/Refund`, `/Ecr/Inquiry`, `/Ecr/closeReceipt` |
| Signature | Field `secureHashValue`, under the Web Service key (see [protocol](protocol.md#signing-web-service)) |
| Timeout | `ResponseTimeout` is 130 s; see below |

**Web Service timeouts.** When the config still has the 120 s `ResponseTimeout`
default, `Plan` raises it to `EcrConfig.WebServiceResponseTimeout` (130 s), because
the Hub's wait includes card presentation and PIN entry. A value you set to anything
else is kept as given. The HTTP request has a single deadline of
`ConnectTimeout + ResponseTimeout`, so 140 s with defaults. If you build
`EcrWebServiceTerminal` directly (bypassing `Plan`), set
`ResponseTimeout = EcrConfig.WebServiceResponseTimeout` yourself.

There is no reachability probe on Web Service. The client shares one process-wide
`HttpClient` and uses the machine's default proxy settings.

### USB cable: bring your own channel

The package ships no USB channel. How a Windows till talks down a cable (a virtual
COM port, WinUSB, a vendor driver) is specific to your hardware. The package gives you
everything above the wire: `EcrLink.UsbCable`, `EcrSessions.UsbCableTerminal`,
`EcrSessions.Open(usbChannel: ...)`, the `IEcrChannel` interface and `EcrFrames`.
Signing, answer checks, results and auto-inquiry work exactly as on LAN.

```csharp
var plan = EcrSessions.Plan(
    link: new EcrLink.UsbCable(),
    config: new EcrConfig { SecureHashKey = secrets.LanKeyFor(serial) });   // same key as Wi-Fi

EcrTerminal terminal = EcrSessions.UsbCableTerminal(
    terminalSerial: "P653200085189",
    plan: plan,
    channel: new MyUsbChannel(pipe));
```

A skeleton channel over any `Stream`:

```csharp
public sealed class MyUsbChannel : IEcrChannel
{
    private readonly Stream _pipe;
    public MyUsbChannel(Stream pipe) { _pipe = pipe; }

    public string Endpoint => "USB cable";   // what an operator sees when it fails
    public string Host => "USB cable";
    public int Port => 0;                    // not a socket

    public Task<string?> ProbeAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        // null when a terminal is there, otherwise the reason.
        return Task.FromResult<string?>(_pipe.CanWrite ? null : "cable not plugged in");
    }

    public async Task<byte[]> ExchangeAsync(byte[] body, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var packet = EcrFrames.Wrap(body);   // 2-byte big-endian length + body

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await _pipe.WriteAsync(packet, 0, packet.Length, linked.Token).ConfigureAwait(false);
            await _pipe.FlushAsync(linked.Token).ConfigureAwait(false);
            return await EcrFrames.ReadBodyAsync(_pipe, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new EcrChannelTimeout("the terminal did not answer in time");
        }
        catch (IOException e) when (!(e is EcrChannelTimeout))
        {
            throw new IOException("Terminal closed the connection: " + e.Message, e);
        }
    }
}
```

Rules the SDK's safety depends on:

- **Frame with `EcrFrames`.** One read is not one message: a transfer may carry
  several, or half of one.
- **A silent terminal is `EcrChannelTimeout`**, thrown when no answer arrives within
  the `timeout` you are given (`EcrConfig.ResponseTimeout`). It becomes
  `Failure.Timeout`: an unknown outcome, not a refusal.
- **After the request may have been written, a broken link must read as a lost
  connection.** The SDK classifies an `IOException` by its message: one containing
  `closed the connection` becomes `ConnectionLost`; `not valid JSON` or
  `empty message` becomes `Malformed`; **any other `IOException` becomes `Unreachable`
  ("nothing was sent, safe to retry")**. A channel that lets a raw `IOException`
  escape after the write tells the till a payment did not happen. Before anything is
  written, a plain `IOException` is right.
- **Do not decide anything**: no signing, no response codes, no retries.
- Exceptions that are not `IOException` propagate out of the operation method.

### One call for every link: EcrSessions.Open

When your till supports several modes, `Open` builds the right client from a ready
plan and returns an [EcrOpenedSession](api-reference.md#ecropenedsession) with the same
calls whichever transport is behind it:

```csharp
EcrOpenedSession session = EcrSessions.Open(
    terminalSerial: "P653200085189",
    plan: plan,                                   // must be ready, or Open throws ArgumentException
    usbChannel: () => new MyUsbChannel(pipe),     // only used for a USB plan
    logger: logger);

var result = await session.SaleAsync(1.234m, merchantReference: order.Number);
```

The session is not a lowest common denominator you can ignore. Receipt, probe and
`originalTerminalId` only work on LAN / USB; see [section 8](#8-lan-and-web-service-compared).

### Signing

Anything on the shop network can reach the terminal's port; signing separates your
till from everything else. With a key configured the SDK signs every LAN request and
verifies every LAN answer. Signing stops other devices issuing refunds, changing an
amount in flight, replaying a captured sale or faking an approval. It does not hide
amounts from a passive listener: traffic is authenticated, not encrypted, and
carries no card number.

Web Service requests are signed too; Hub answers are read from the JSON envelope
without a local signature check. Both transports use HMAC-SHA256 over a sorted
`key=value&...` string with the hex-decoded key, but different field names and
different secrets. Using the LAN key against the Hub, or the Web Service key over
TCP, fails in ways that look like network problems.

**If every LAN call returns `Failure.Unauthenticated`**, the key here and on the
terminal do not match; the wrong key type for the mode is the first thing to check.
A terminal that has not been issued a key accepts unsigned messages.

Treat keys like passwords: not in source, not in `appsettings.json`, not in a log;
encrypted at rest on Windows (see [DPAPI](#storing-keys-with-dpapi)); not shared
between customers. If one leaks, ask Amwal to rotate it.

---

## 4. Take a payment

```csharp
async Task TakePaymentAsync(Order order)
{
    // LAN / USB only: there is no probe on Web Service.
    if (terminal is EcrTerminal lan && !await lan.IsReachableAsync())
    {
        screen.Show("Terminal not reachable - check it is on and on this network");
        return;
    }

    // Store order.Number BEFORE the call: it is how you find the sale after a lost answer.
    switch (await terminal.SaleAsync(order.Total, merchantReference: order.Number))
    {
        case EcrResult.Approved a when a.PartialApproval:
            screen.Show($"Only {a.Amount} of {a.RequestedAmount} approved");
            break;
        case EcrResult.Approved a:
            CompleteSale(a.Amount, a.Rrn, a.AuthCode);
            break;
        case EcrResult.Declined d:
            screen.Show(d.Reason);
            break;
        case EcrResult.Failed f when f.Settled:
            Reconcile(((EcrInquiry.Found)f.Recovered!).Transaction);
            break;
        case EcrResult.Failed f:
            HandleUnknownOutcome(order.Number, f.Failure);   // not a decline: see section 7
            break;
    }
}
```

`SaleAsync` completes when the cardholder has paid: usually seconds, sometimes
ninety. Keep your progress UI up and do not impose a shorter timeout of your own.

**Use `decimal`, never `double`.** `terminal.SaleAsync(1.234m)` compiles;
`SaleAsync(1.234)` does not. Parse and format with `CultureInfo.InvariantCulture`:
`1.234` becomes `1,234` under a European or Arabic regional setting. The amounts you
get back (`Approved.Amount`, `EcrTransaction.Amount`) are strings in that invariant
format.

An amount with more decimal places than `MinorUnitDigits` is rounded, away from zero
at the midpoint (`1.2345m` goes out as `1.235`); round it yourself so your books and
the terminal agree. The SDK does not check that an amount is positive or within a
limit; the terminal answers `13` for an invalid one.

### Partial approval

The bank may authorise **less** than was asked for. That is an approval, not a
refusal, and the customer still owes the difference. `Amount` is always what was
actually taken; `RequestedAmount` is what was asked.

```csharp
case EcrResult.Approved a when a.PartialApproval:
    var requested = decimal.Parse(a.RequestedAmount, CultureInfo.InvariantCulture);
    var taken = decimal.Parse(a.Amount, CultureInfo.InvariantCulture);
    screen.Show($"Only {a.Amount} approved. Collect {requested - taken} another way.");
    // Do not release the goods until the balance is settled.
    break;
```

### Naming the transaction

Every operation takes an optional `merchantReference`, and every outcome carries one.
Pass your own order or basket number and the same string identifies the transaction
in your books, in the terminal's records and in any later inquiry. Leave it out and
the SDK generates one (12 uppercase hex characters) and returns it on the result;
store it either way.

**If you pass a `CancellationToken`, or the process might die mid-sale, pass your own
reference and record it before the call.** A generated reference reaches you only on
a result, and a cancelled or crashed call has none.

A reference is 1-32 printable ASCII characters, without spaces, `&` or `=`; anything
else throws `ArgumentException` from the `await`, before a connection is opened. The
terminal does not check uniqueness; reusing a reference is allowed (a retry of the
same order) but two different sales sharing one are indistinguishable later.

---

## 5. Void, refund, inquiry, receipt, close receipt

Examples use `terminal`. `EcrTerminal` and `EcrWebServiceTerminal` share method
names; differences are called out. Placeholders such as `screen` and `Reconcile` are
yours.

### Void: cancel a transaction in full

```csharp
switch (await terminal.VoidAsync(receiptNumber: "215"))
{
    case EcrResult.Approved a:
        RecordVoid(a.Rrn, a.Amount);   // do not print a.AuthCode: it is a placeholder on a void
        break;
    case EcrResult.Declined d:
        screen.Show(d.Reason);          // not found, outside the void window, already voided
        break;
    case EcrResult.Failed f:
        HandleUnknownOutcome("", f.Failure);
        break;
}
```

No amount: a void returns exactly what the original took, and no card is presented
for a transaction taken on the same terminal. Refusals arrive as `Declined` with the
reason; nothing reached the backend. The reference you pass names the void, not the
transaction being cancelled.

### Refund: return money against an earlier transaction

```csharp
var result = await terminal.RefundAsync(
    amount: 0.500m,
    receiptNumber: "208",
    transactionDate: "20260809");   // yyyyMMdd; a receipt number is only unique within a day
```

The cardholder must present their card. Whether the refund is allowed, and for how
much, is the backend's decision: do not pre-validate it.

### Inquiry: what became of a transaction

```csharp
switch (await terminal.InquireAsync(receiptNumber: "208", transactionDate: "20260809"))
{
    case EcrInquiry.Found found:
        EcrTransaction txn = found.Transaction;
        screen.Show($"{txn.Type} {txn.Status} {txn.Amount} {txn.Currency}");
        if (txn.CanVoid) EnableVoidButton();
        break;
    case EcrInquiry.NotFound notFound:
        screen.Show(notFound.Reason);
        break;
    case EcrInquiry.Failed failed:
        screen.Show(failed.Failure.Message);   // the lookup failed; still unknown
        break;
}
```

`Status` is the **transaction's** outcome, not the inquiry's: finding a declined sale
is a successful inquiry. `CanVoid` and `CanRefund` already include the void window
and the backend's rules. Inquiry is safe to repeat and is answered even while the
terminal is taking a payment.

**By merchant reference**, when you have the reference but no receipt number (the
usual case after a lost answer):

```csharp
// EcrTerminal (LAN / USB): transactionDate is optional
var lanFound = await terminal.InquireByReferenceAsync(originalReference: order.Number);

// EcrWebServiceTerminal: transactionDate is a required parameter; pass "" for "any day"
var hubFound = await hub.InquireByReferenceAsync(originalReference: order.Number, transactionDate: "");
```

`originalReference` is the transaction being looked up; the separate
`merchantReference` argument names the inquiry itself. On Web Service the SDK posts
`/Ecr/Inquiry` with empty `stan` and `originalMerchantReference` set.

### Receipt: the e-receipt as a QR code (LAN / USB only)

```csharp
switch (await terminal.ReceiptAsync(receiptNumber: "215", transactionDate: "20260809"))
{
    case EcrReceipt.Ready ready:         screen.ShowQrCode(ready.Url); break;
    case EcrReceipt.Unavailable missing: screen.Show(missing.Reason); break;
    case EcrReceipt.Failed failed:       screen.Show(failed.Failure.Message); break;
}
```

Render the QR locally so it appears at once and survives the network dropping.
`EcrWebServiceTerminal` has no receipt method; `EcrOpenedSession.ReceiptAsync` on
Web Service answers `EcrReceipt.Unavailable` without contacting anything.

### Close receipt (requires 1.0.2)

After a sale, void or refund the terminal shows a receipt screen. `CloseReceiptAsync`
asks it to put the receipt away and return to idle. Call it from your till's explicit
**Close receipt** action, not as a side effect of dismissing a dialog; the SDK never
sends it by itself.

```csharp
// LAN / USB
EcrReceiptClosed closed = await lanTerminal.CloseReceiptAsync();

// Web Service: the money operation's reference is required
EcrReceiptClosed closed = await hub.CloseReceiptAsync(originalMerchantReference: order.Number);

// Either transport, through a session. Note the argument order: this close
// request's own id first, then the original's.
EcrReceiptClosed closed = await session.CloseReceiptAsync(
    originalMerchantReference: order.Number);

switch (closed)
{
    case EcrReceiptClosed.Idle:                    break;                       // terminal is idle
    case EcrReceiptClosed.Refused r:  /* e.g. firmware too old: carry on */     break;
    case EcrReceiptClosed.Failed f:   /* exchange failed; safe to retry */     break;
}
```

Closing is harmless to repeat, whether or not the terminal had a receipt up. On Web
Service a missing original reference returns `EcrReceiptClosed.Failed`
(`Failure.Malformed`) and sends nothing; a transport error arrives as `Refused` with
`ResponseCode == "NO_RESPONSE"` (the Hub envelope is `success: false`). Neither moves
money, so treat `Refused` as "could not be asked" and carry on.

### Acting on another terminal's transaction (LAN / USB only)

On LAN a void, refund, inquiry or receipt can target a transaction taken on a
different till by passing `originalTerminalId`:

```csharp
await terminal.VoidAsync(receiptNumber: "215", originalTerminalId: "31629");
```

`EcrWebServiceTerminal` has no such parameter, and an `EcrOpenedSession` over Web
Service **ignores it without warning**, so a void meant for another till would be
sent as if for this one. Check `session.UsesLocalTerminal` before passing it.

---

## 5a. Copy-paste examples: Wi-Fi and Web Service

Every operation, once for each transport, written to be pasted and adapted. The
sections above explain the rules; this one is the code.

**Assumptions.** `lan` is an `EcrTerminal` and `web` is an `EcrWebServiceTerminal`,
built as in [section 3](#3-connect-to-a-terminal). `screen`, `CompleteSale`,
`Reconcile` and `HandleUnknownOutcome` stand for your own code; everything else is
the SDK. Only three things differ between the transports:

| | Wi-Fi / LAN (`EcrTerminal`) | Web Service (`EcrWebServiceTerminal`) |
|---|---|---|
| Void / refund of another terminal's transaction | `originalTerminalId:` | not available |
| Receipt (QR) | `ReceiptAsync` | not available |
| Lost answer | `Failed`, `Recovered` often set by the automatic inquiry | `Failed`, `Recovered` is always `null`: inquire yourself |
| Close receipt | `CloseReceiptAsync(merchantReference)` | `CloseReceiptAsync(originalMerchantReference, merchantReference)` |

Dates are `yyyyMMdd`. Amounts are `decimal`. Store your `merchantReference`
before any money-moving call: it is how you find the transaction after a lost
answer.

### Sale

**Wi-Fi / LAN**

```csharp
async Task WifiSaleAsync(EcrTerminal lan, decimal amount, string orderNumber)
{
    if (!await lan.IsReachableAsync())
    {
        screen.Show("Terminal not reachable");
        return;
    }

    switch (await lan.SaleAsync(amount, merchantReference: orderNumber))
    {
        case EcrResult.Approved a when a.PartialApproval:
            screen.Show($"Only {a.Amount} of {a.RequestedAmount} approved");
            CompleteSale(a);
            break;
        case EcrResult.Approved a:
            CompleteSale(a);                       // a.Amount, a.Rrn, a.AuthCode, a.MaskedPan
            break;
        case EcrResult.Declined d:
            screen.Show(d.Reason);                 // a real refusal: safe to offer another try
            break;
        case EcrResult.Failed f when f.Settled:
            Reconcile(((EcrInquiry.Found)f.Recovered!).Transaction);   // lost answer, found by auto-inquiry
            break;
        case EcrResult.Failed f:
            HandleUnknownOutcome(orderNumber, f.Failure);              // never retry the sale
            break;
    }
}
```

When the cashier taps Close receipt (never automatically):

```csharp
async Task WifiCloseReceiptAsync(EcrTerminal lan) =>
    ShowClosed(await lan.CloseReceiptAsync());   // ShowClosed: see "Close receipt" below
```

**Web Service**

```csharp
async Task WebServiceSaleAsync(EcrWebServiceTerminal web, decimal amount, string orderNumber)
{
    // No reachability probe on Web Service: the call itself finds out.
    switch (await web.SaleAsync(amount, merchantReference: orderNumber))
    {
        case EcrResult.Approved a when a.PartialApproval:
            screen.Show($"Only {a.Amount} of {a.RequestedAmount} approved");
            CompleteSale(a);
            break;
        case EcrResult.Approved a:
            CompleteSale(a);
            break;
        case EcrResult.Declined d:
            screen.Show(d.Reason);
            break;
        case EcrResult.Failed f:
            // Timeout or lost connection: the Hub may have taken the payment.
            // Recovered is always null here, so inquire by orderNumber (below).
            HandleUnknownOutcome(orderNumber, f.Failure);
            break;
    }
}
```

When the cashier taps Close receipt (never automatically), name the operation that
just finished:

```csharp
async Task WebServiceCloseReceiptAsync(EcrWebServiceTerminal web, string orderNumber) =>
    ShowClosed(await web.CloseReceiptAsync(originalMerchantReference: orderNumber));
```

### Void

`receiptNumber` is the STAN of the transaction to cancel, from the original
`Approved` / inquiry result.

**Wi-Fi / LAN**

```csharp
async Task WifiVoidAsync(EcrTerminal lan, string receiptNumber, string reference)
{
    switch (await lan.VoidAsync(receiptNumber, merchantReference: reference))
    {
        case EcrResult.Approved:
            screen.Show("Voided");
            break;
        case EcrResult.Declined d:
            screen.Show(d.Reason);                 // e.g. already settled: refund instead
            break;
        case EcrResult.Failed f:
            HandleUnknownOutcome(reference, f.Failure);
            break;
    }
}
```

When the cashier taps Close receipt (never automatically):

```csharp
async Task WifiCloseReceiptAsync(EcrTerminal lan) =>
    ShowClosed(await lan.CloseReceiptAsync());   // ShowClosed: see "Close receipt" below
```

When the original was taken on a different terminal, name it:

```csharp
await lan.VoidAsync(receiptNumber, originalTerminalId: "101311", merchantReference: reference);
```

**Web Service**

```csharp
async Task WebServiceVoidAsync(EcrWebServiceTerminal web, string receiptNumber, string reference)
{
    switch (await web.VoidAsync(receiptNumber, merchantReference: reference))
    {
        case EcrResult.Approved:
            screen.Show("Voided");
            break;
        case EcrResult.Declined d:
            screen.Show(d.Reason);
            break;
        case EcrResult.Failed f:
            HandleUnknownOutcome(reference, f.Failure);
            break;
    }
}
```

When the cashier taps Close receipt (never automatically), name the operation that
just finished:

```csharp
async Task WebServiceCloseReceiptAsync(EcrWebServiceTerminal web, string reference) =>
    ShowClosed(await web.CloseReceiptAsync(originalMerchantReference: reference));
```

### Refund

**Wi-Fi / LAN**

```csharp
async Task WifiRefundAsync(
    EcrTerminal lan, decimal amount, string receiptNumber, DateTime originalDay, string reference)
{
    var result = await lan.RefundAsync(
        amount,
        receiptNumber,
        transactionDate: originalDay.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
        merchantReference: reference);

    switch (result)
    {
        case EcrResult.Approved a:
            screen.Show($"Refunded {a.Amount}");
            break;
        case EcrResult.Declined d:
            screen.Show(d.Reason);
            break;
        case EcrResult.Failed f:
            HandleUnknownOutcome(reference, f.Failure);
            break;
    }
}
```

When the cashier taps Close receipt (never automatically):

```csharp
async Task WifiCloseReceiptAsync(EcrTerminal lan) =>
    ShowClosed(await lan.CloseReceiptAsync());   // ShowClosed: see "Close receipt" below
```

**Web Service**

```csharp
async Task WebServiceRefundAsync(
    EcrWebServiceTerminal web, decimal amount, string receiptNumber, DateTime originalDay, string reference)
{
    var result = await web.RefundAsync(
        amount,
        receiptNumber,
        transactionDate: originalDay.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
        merchantReference: reference);

    switch (result)
    {
        case EcrResult.Approved a:
            screen.Show($"Refunded {a.Amount}");
            break;
        case EcrResult.Declined d:
            screen.Show(d.Reason);
            break;
        case EcrResult.Failed f:
            HandleUnknownOutcome(reference, f.Failure);
            break;
    }
}
```

When the cashier taps Close receipt (never automatically), name the operation that
just finished:

```csharp
async Task WebServiceCloseReceiptAsync(EcrWebServiceTerminal web, string reference) =>
    ShowClosed(await web.CloseReceiptAsync(originalMerchantReference: reference));
```

### Inquiry by receipt number (STAN)

Use it when you hold the STAN: re-checking a transaction, or deciding whether it
can still be voided or refunded. It only reads, so repeat it freely.

**Wi-Fi / LAN**

```csharp
async Task WifiInquireByStanAsync(EcrTerminal lan, string receiptNumber, DateTime day)
{
    var inquiry = await lan.InquireAsync(
        receiptNumber,
        transactionDate: day.ToString("yyyyMMdd", CultureInfo.InvariantCulture));

    ShowInquiry(inquiry);
}
```

**Web Service**

```csharp
async Task WebServiceInquireByStanAsync(EcrWebServiceTerminal web, string receiptNumber, DateTime day)
{
    var inquiry = await web.InquireAsync(
        receiptNumber,
        transactionDate: day.ToString("yyyyMMdd", CultureInfo.InvariantCulture));

    ShowInquiry(inquiry);
}
```

**Reading the answer (same for both)**

```csharp
void ShowInquiry(EcrInquiry inquiry)
{
    switch (inquiry)
    {
        case EcrInquiry.Found f:
            var t = f.Transaction;                 // Status, Stan, Amount, AuthorizedAmount, CanVoid, CanRefund, ...
            screen.Show($"{t.Status}: {t.Amount} (receipt {t.Stan})");
            break;
        case EcrInquiry.NotFound n:
            screen.Show(n.Reason);                 // the terminal has no such transaction
            break;
        case EcrInquiry.Failed fail:
            screen.Show(fail.Failure.Message);     // the lookup did not complete: still unknown, ask again
            break;
    }
}
```

### Inquiry by merchant reference

The way out after a lost answer, because you still have your own reference
even when you never received the STAN.

**Wi-Fi / LAN**

```csharp
var inquiry = await lan.InquireByReferenceAsync(originalReference: orderNumber);
ShowInquiry(inquiry);
```

**Web Service**

```csharp
// A reference is not scoped to a day: leave transactionDate blank.
var inquiry = await web.InquireByReferenceAsync(originalReference: orderNumber, transactionDate: "");
ShowInquiry(inquiry);
```

Pass the sale's reference as `originalReference`. Leave `merchantReference`
blank: it is this inquiry's own id and the SDK generates one.

**Deciding after a lost answer (same for both)**

```csharp
async Task ResolveUnknownAsync(Func<Task<EcrInquiry>> ask, string orderNumber)
{
    switch (await ask())
    {
        case EcrInquiry.Found f when f.Transaction.Status == "Approved":
            CompleteSale(f.Transaction);           // it went through: do not sell again
            break;
        case EcrInquiry.Found:
            screen.Show("Declined: safe to try again");
            break;
        case EcrInquiry.NotFound:
            // Not proof that nothing was taken yet on every deployment: look again
            // before taking payment a second time.
            screen.Show("No record yet: check again before retrying");
            break;
        case EcrInquiry.Failed:
            screen.Show("Could not check: try again");   // still unknown
            break;
    }
}
```

### Receipt (QR code): Wi-Fi / LAN only

```csharp
async Task WifiReceiptAsync(EcrTerminal lan, string receiptNumber, DateTime day)
{
    var receipt = await lan.ReceiptAsync(
        receiptNumber,
        transactionDate: day.ToString("yyyyMMdd", CultureInfo.InvariantCulture));

    switch (receipt)
    {
        case EcrReceipt.Ready r:
            screen.ShowQrCode(r.Url);              // the customer scans it
            break;
        case EcrReceipt.Unavailable u:
            screen.Show(u.Reason);
            break;
        case EcrReceipt.Failed f:
            screen.Show(f.Failure.Message);
            break;
    }
}
```

Web Service has no receipt endpoint; `EcrOpenedSession.ReceiptAsync` returns
`Unavailable` for it.

### Close receipt (requires 1.0.2)

Call it from the cashier's own Close receipt action, not when a dialog is
dismissed and not automatically after a sale.

**Wi-Fi / LAN**

```csharp
var closed = await lan.CloseReceiptAsync();
ShowClosed(closed);
```

**Web Service**

```csharp
// originalMerchantReference: the reference of the sale / void / refund just finished.
var closed = await web.CloseReceiptAsync(originalMerchantReference: orderNumber);
ShowClosed(closed);
```

**Reading the answer (same for both)**

```csharp
void ShowClosed(EcrReceiptClosed closed)
{
    switch (closed)
    {
        case EcrReceiptClosed.Idle:
            screen.Show("Terminal is back on its idle screen");
            break;
        case EcrReceiptClosed.Refused r:
            screen.Show($"Terminal refused: {r.Reason}");
            break;
        case EcrReceiptClosed.Failed f:
            screen.Show($"Could not reach the terminal: {f.Failure.Message}");
            break;
    }
}
```

### One code path for both transports

If your till supports both, open the session once and call the same methods:

```csharp
EcrOpenedSession session = EcrSessions.Open(terminalSerial, plan, logger: logger);

var sale   = await session.SaleAsync(amount, merchantReference: orderNumber);
var lookup = await session.InquireByReferenceAsync(orderNumber);
var closed = await session.CloseReceiptAsync(originalMerchantReference: orderNumber);
```

`EcrOpenedSession` also has `VoidAsync`, `RefundAsync`, `InquireAsync` and
`ReceiptAsync` (the last answers `Unavailable` on Web Service). Working versions of
all of these, wired to a WPF till and an ASP.NET Core app, are in the
[example repository](https://github.com/amwal-pay/AmwalpayEcrWindowsExample).

---

## 6. Handling outcomes

Three result types, and the difference between the last two is the whole game.

| Result | Meaning | A till should |
|---|---|---|
| `Approved` | Money was taken. `Amount` is what was actually taken | Complete the sale. Store `MerchantReference`, `Rrn`, `AuthCode` (not on a void) and `Raw` |
| `Approved` with `PartialApproval` | The bank authorised less than `RequestedAmount` | Collect the balance before releasing goods |
| `Declined` | The terminal or Hub answered and refused; no money moved | Show `Reason`; offer another method. If `ResponseCode` is `91` or `94`, inquire first |
| `Failed` | **Nothing was learned.** The money may have moved | Never retry blindly: [section 7](#7-when-the-answer-never-comes) |
| `OperationCanceledException` (you cancelled) | The SDK stopped waiting; the terminal did not | Treat as unknown; inquire by reference |

What each case carries:

| Case | Properties |
|---|---|
| `Approved` | `Amount` (what was taken, major units, e.g. `"1.234"`), `ResponseCode` (`"00"`), `Rrn`, `AuthCode` (a placeholder on a void), `MaskedPan` (may be empty), `PartialApproval`, `RequestedAmount`, `Raw` (the terminal's full answer as JSON text; log it with the transaction) |
| `Declined` | `ResponseCode`, `Reason`, `NextStep`, `Raw` |
| `Failed` | `Failure` (why: [section 7](#7-when-the-answer-never-comes)), `Recovered` (LAN auto-inquiry finding, or `null`), `Settled` (true when `Recovered` is a `Found`), `NextStep` (always `InquireByMerchantReference`) |

A `switch` expression over `EcrResult` needs a final discard arm
(`_ => throw new InvalidOperationException(...)`), because C# cannot prove the set
closed; let it be the "this SDK grew a fourth case" alarm.

**Do not decide from `ResponseCode`.** A partial approval that the operator then
voids carries the bank's own `00`. The result type already accounts for this: an
outcome that is not an approval never arrives as `Approved`.

**`Declined.NextStep`.** The terminal can say it does not actually know the outcome
by sending `nextStep: INQUIRE_BY_MERCHANT_REFERENCE`. The SDK reads any such answer
as `Failed`, never `Declined`, so `Declined.NextStep` is `None` on every result this
SDK builds. Checking it is harmless; branching on `Failed` is what matters. The
terminal's own codes `91` (did not complete, or could not say) and `94` (this exact
message was already answered, so the transaction probably exists) arrive as
`Declined` when no `nextStep` accompanies them: inquire rather than retry.

### Other result types

| Type | Cases | Notes |
|---|---|---|
| `EcrInquiry` | `Found` / `NotFound` / `Failed` | `Found` is not paid: read `Transaction.Status`. `NotFound` is a real answer; on Web Service it is also what an HTTP 4xx with no readable message gives. `Failed` means the lookup itself did not happen; harmless to retry |
| `EcrReceipt` | `Ready` / `Unavailable` / `Failed` | `Unavailable` is harmless; asking again is safe |
| `EcrReceiptClosed` | `Idle` / `Refused` / `Failed` | See [close receipt](#close-receipt-requires-102) |

---

## 7. When the answer never comes

**This is the most important section in this guide.** A response that never arrives
is not a decline.

```csharp
case EcrResult.Failed f:
    switch (f.Failure)
    {
        case Failure.Unreachable:     /* nothing was sent: safe, nothing happened */        break;
        case Failure.Timeout:         /* UNKNOWN: the payment may have completed */         break;
        case Failure.ConnectionLost:  /* UNKNOWN: the payment may have completed */         break;
        case Failure.Malformed:       /* an answer the SDK could not read: UNKNOWN */       break;
        case Failure.Unauthenticated: /* an answer that could not be trusted: UNKNOWN */    break;
    }
    break;
```

`Failure.OutcomeUnknown` is false only for `Unreachable`. The card flow on the
terminal does not depend on the socket staying up, so the terminal may have taken the
money and been unable to say so.

| Failure | LAN / USB | Web Service | Payment happened? |
|---|---|---|---|
| `Unreachable` | Never connected: refused, no route, unknown host, or **connect** timeout. Nothing sent | Not produced | **No** |
| `Timeout` | Request written, no answer within `ResponseTimeout` (120 s) | No response, no network, reset, or HTTP error whose text says timeout (`responseCode` `NO_RESPONSE`); deadline `ConnectTimeout + ResponseTimeout` | **Unknown** |
| `ConnectionLost` | Link reset or closed after the request was written, or part-way through the answer | HTTP 5xx, empty body or non-JSON body from the Hub | **Unknown** |
| `Malformed` | Answer empty or not JSON | Only when `MerchantId` / `TerminalId` are not numeric: nothing was sent, `MerchantReference` is empty | **Unknown** on LAN; **No** on Web Service |
| `Unauthenticated` | Answer unsigned, wrongly signed, or answering another request | Not produced; Hub rejections arrive as `Declined` | **Unknown** |

On Web Service the Hub's own synthesised "no answer" envelope carries
`nextStep: INQUIRE_BY_MERCHANT_REFERENCE`, and the SDK turns it into `Failed`. An HTTP
4xx with no readable message, and any real Hub refusal, is a final `Declined`.

### Resolve it

```csharp
async Task<bool?> WasThePaymentTakenAsync(string merchantReference)
{
    // Safe to call as often as you like, and answered even mid-transaction.
    switch (await terminal.InquireByReferenceAsync(merchantReference))
    {
        case EcrInquiry.Found found:
            // Found is not paid: read the transaction's own status.
            return string.Equals(found.Transaction.Status, "Approved", StringComparison.OrdinalIgnoreCase);
        case EcrInquiry.NotFound:
            return false;   // it never happened on this terminal; retrying is safe
        default:
            return null;    // still unknown: a human must look
    }
}
```

- **LAN / USB** with `EcrConfig.AutoInquireOnFailure = true` (the default): after a
  `Failed` whose outcome is unknown, `EcrTerminal` makes one
  `InquireByReferenceAsync` on a fresh connection and attaches it to
  `Failed.Recovered`. Check `f.Recovered` and `f.Settled` before prompting the
  operator. `Settled` means the transaction was **found**; it can still be a declined
  sale, so read `((EcrInquiry.Found)f.Recovered!).Transaction.Status`. After a
  `Timeout` the call can take up to a second `ResponseTimeout` longer, because the
  follow-up is itself a request. It never runs when nothing was sent, and it is never
  used to resend the sale.
- **Web Service**: there is no auto-inquiry; `Recovered` is always `null`. Call
  `InquireByReferenceAsync` with the sale's reference yourself (or `InquireAsync` if
  you already have the receipt number).
- If both leave the outcome unknown, reconcile against the terminal's batch. Do not
  resend the sale.

### Make the unknown recoverable

An unknown outcome you cannot look up is just a loss. Two habits make it recoverable:

1. **Write the reference down before you send**, in something that survives a crash
   (a row keyed by order), and pass it as `merchantReference`.
2. **On start-up, and after any unknown outcome, resolve what is still open** with
   `InquireByReferenceAsync`.

```csharp
public async Task PayAsync(Order order)
{
    var reference = order.Number;              // your own, decided before the call
    journal.Begin(order.Id, reference);

    var result = await terminal.SaleAsync(order.Total, merchantReference: reference);

    // ... show the outcome ...

    if (result is not EcrResult.Failed { Settled: false })
    {
        journal.Complete(order.Id);            // only once the outcome is known
    }
}

async Task ResolveOpenSalesAsync()
{
    foreach (var (orderId, reference) in journal.Unresolved())
    {
        switch (await terminal.InquireByReferenceAsync(reference))
        {
            case EcrInquiry.Found found:
                Reconcile(found.Transaction);  // read Status before deciding
                journal.Complete(orderId);
                break;
            case EcrInquiry.NotFound:
                journal.Complete(orderId);     // it never reached the terminal
                break;
            case EcrInquiry.Failed:
                break;                         // still unknown; ask again later
        }
    }
}
```

(`terminal` here is an `EcrTerminal` or an `EcrOpenedSession`; on an
`EcrWebServiceTerminal` add `transactionDate: ""`.)

---

## 8. LAN and Web Service compared

| Capability | LAN / USB (`EcrTerminal`) | Web Service (`EcrWebServiceTerminal`) |
|---|---|---|
| Sale / void / refund | Yes | Yes |
| Inquiry by receipt (STAN + date) | `InquireAsync` | `InquireAsync` |
| Inquiry by merchant reference | `InquireByReferenceAsync` (date optional) | `InquireByReferenceAsync` (date parameter required; may be `""`) |
| Receipt (e-receipt URL) | `ReceiptAsync` | No method; session returns `EcrReceipt.Unavailable` |
| Close receipt (1.0.2) | `CloseReceiptAsync(merchantReference)` | `CloseReceiptAsync(originalMerchantReference, merchantReference)` |
| `originalTerminalId` | void / refund / inquiry / receipt | Not exposed; the session ignores it |
| Reachability probe | `ProbeReachabilityAsync` | None; session returns `null` |
| `RunAsync(type, ...)` | Yes | No |
| Auto-inquire after a lost answer | Yes (`AutoInquireOnFailure`, default `true`) | No |
| A lost answer arrives as | `Failed`: `Unreachable` / `Timeout` / `ConnectionLost` / `Malformed` / `Unauthenticated` | `Failed`: `Timeout` / `ConnectionLost`; inquiry: `EcrInquiry.Failed` with `ConnectionLost` |
| Request signing field | `secureHash` + `nonce` | `secureHashValue` (no nonce) |
| Answer signature check | Yes | No; Hub JSON envelope only |
| Default response timeout | 120 s (plus 10 s connect) | 130 s via `Plan` (plus 10 s connect, one deadline) |

Watch the argument lists: `EcrWebServiceTerminal.VoidAsync(receiptNumber,
merchantReference, ct)` has no `originalTerminalId`, so
`hub.VoidAsync("215", "31629")` names the void `31629`. Use named arguments.

---

## 9. Threading and lifecycle

Every call is an `async` method returning a `Task`. It never blocks the calling
thread on network I/O and uses `ConfigureAwait(false)` for its own awaits, so it
neither captures nor needs your synchronisation context. You do not need `Task.Run`.

### WPF and WinForms

`await` the call from the UI thread; the code after it resumes there.

```csharp
public sealed class TillViewModel
{
    private readonly EcrTerminal _terminal;
    private bool _busy;

    public TillViewModel(EcrTerminal terminal) { _terminal = terminal; }

    public async Task PayAsync(Order order)
    {
        if (_busy) return;                 // a double click must not start a second sale
        _busy = true;
        try
        {
            var result = await _terminal.SaleAsync(order.Total, merchantReference: order.Number);
            Status = result switch
            {
                EcrResult.Approved a when a.PartialApproval => $"Only {a.Amount} approved",
                EcrResult.Approved                          => "Approved",
                EcrResult.Declined d                        => d.Reason,
                EcrResult.Failed f when f.Settled           => "Outcome found - reconcile",
                EcrResult.Failed                            => "Outcome unknown - do not retry",
                _ => throw new InvalidOperationException("Unknown EcrResult"),
            };
        }
        finally
        {
            _busy = false;
        }
    }
}
```

In a code-behind handler use `async void` with a `try/catch` around the body; an
exception escaping `async void` takes the application down.

**Never block on it.** `SaleAsync(...).Result` or `.Wait()` will not deadlock (a
regression test blocks from a single-threaded context on the LAN, recovery, timeout
and Web Service paths), but the call takes as long as the cardholder takes and your
window freezes for all of it. Disable the Pay control while a sale is in flight: the
terminal serves one transaction at a time and a second request gets `96`.

### Cancellation stops the wait, not the transaction

Cancelling a `CancellationToken` stops the SDK waiting. The call throws
`OperationCanceledException` (it returns no result) and the terminal carries on with
the cardholder. Treat it exactly like `Failure.Timeout`: no auto-inquiry runs, so you
resolve it by the reference you passed.

```csharp
try
{
    var result = await terminal.SaleAsync(order.Total, merchantReference: order.Number, cancellationToken: cts.Token);
}
catch (OperationCanceledException)
{
    // The SDK stopped waiting; the terminal did not. Outcome UNKNOWN:
    // InquireByReferenceAsync(order.Number) before anything else.
}
```

A "Cancel" button that cancels the token is honest only if it says "stop waiting".
Aborting the payment happens at the terminal. Keep the in-flight call somewhere that
outlives the screen (a view model or service, not a `Window`), and hold one client
per physical terminal.

### What throws

Results carry every runtime outcome, so what escapes is a programming error or your
own cancellation:

| Exception | When |
|---|---|
| `ArgumentException` | A blank receipt number, an invalid `merchantReference`, a blank `originalReference`, or `RunAsync` given a type it does not run. Raised on `await`, before a connection is opened |
| `OperationCanceledException` | The `CancellationToken` you passed was cancelled |
| `ArgumentNullException` / `ArgumentException` (from `Plan`, `Open`, `LanTerminal`, ...) | A null link, config or plan; `Open` given a plan that is not ready; a builder given a plan for another link |
| `InvalidOperationException` | `Open` on a USB plan with no `usbChannel` |

### Concurrency

A client holds no connection and no mutable state, so it is safe to share across
threads; each LAN call opens its own TCP connection, and all Web Service clients
share one process-wide `HttpClient`. The *terminal* serves one money-moving
transaction at a time, so two concurrent sales collide with `96` on the second.
Inquiries and receipts are answered while busy. (This is from reading the source; it
has not been load-tested.)

### Running in a web app

ASP.NET Core (.NET 8+) and classic ASP.NET use the same package. Register one client
per physical terminal as a singleton (building one per request is also safe).

```csharp
// Program.cs (ASP.NET Core, .NET 8). Register once; nothing here is per-request.
builder.Services.AddSingleton<EcrTerminal>(sp =>
{
    var section = sp.GetRequiredService<IConfiguration>().GetSection("Ecr");
    var plan = EcrSessions.Plan(
        link: new EcrLink.Lan(Host: section["TerminalHost"] ?? ""),
        config: new EcrConfig { SecureHashKey = section["LanKey"] ?? "" });

    if (!plan.IsReady)   // fail at start-up, not on the first customer
        throw new InvalidOperationException("ECR is not configured: " + string.Join("; ", plan.Issues));

    var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("AmwalPay.Ecr");
    return EcrSessions.LanTerminal(
        terminalSerial: section["TerminalSerial"] ?? "",
        plan: plan,
        logger: EcrLogger.From(m => log.LogDebug("{EcrMessage}", m)));
});

app.MapPost("/sales", async (SaleRequest request, EcrTerminal terminal) =>
{
    // No HttpContext.RequestAborted here on purpose: a browser that disconnects
    // must not turn a payment that is still running into an unknown outcome.
    EcrResult result = await terminal.SaleAsync(request.Total, merchantReference: request.OrderNumber);

    return result switch
    {
        EcrResult.Approved a => Results.Ok(new { status = "approved", a.Rrn, a.AuthCode }),
        EcrResult.Declined d => Results.Ok(new { status = "declined", d.Reason }),
        EcrResult.Failed f   => Results.Ok(new { status = f.Settled ? "settled" : "unknown", f.Failure.Message }),
        _ => Results.StatusCode(500),
    };
});

record SaleRequest(decimal Total, string OrderNumber);
```

For Web Service, plan with `EcrLink.WebService(...)` and the Web Service key, gate on
`plan.IsReady`, and register `EcrSessions.WebServiceTerminal(serial, plan)`.

A web app must still:

- Take keys from a secret store (user secrets, a vault, environment variables).
- Never pass `HttpContext.RequestAborted` to a money-moving call, and write the
  `merchantReference` down before the call.
- Serialise sales per terminal; the second concurrent sale gets `96`.
- Use Web Service ECR when hosted away from the shop: a data-centre server cannot
  reach a terminal on a shop's Wi-Fi.

---

## 10. Logging

Silent by default. `IEcrLogger` is one method, `Debug(string)`;
`EcrLogger.From(Action<string>)` wraps a delegate.

```csharp
IEcrLogger debugOutput = EcrLogger.From(message => Debug.WriteLine(message, "Ecr"));
IEcrLogger mel = EcrLogger.From(message => ilogger.LogDebug("{EcrMessage}", message));   // Microsoft.Extensions.Logging
IEcrLogger serilog = EcrLogger.From(message => Log.Debug("{EcrMessage}", message));
```

When signing is enabled, diagnostics show the formula, whether a key is configured
(`configured` / `not configured`, never any part of the secret), the payload being
signed and the computed hash. Request and response payloads can contain masked PANs
and transaction references: log at `Debug`, keep them out of production sinks and
analytics, and treat them as transaction records. `EcrConfig.ToString()` redacts
`SecureHashKey`.

---

## 11. Building your own form

`EcrTransactionType` declares what each operation needs, so drive your UI from it.
The flags are extension methods in `EcrTransactionTypes`:

```csharp
var type = EcrTransactionType.Refund;

if (type.RequiresAmount())       ShowAmountField();
if (type.RequiresOriginalStan()) ShowReceiptNumberField();
if (type.RequiresOriginalDate()) ShowDateField();
if (type.AllowsOtherTerminal())  ShowOtherTerminalSwitch();

await terminal.RunAsync(type, amount: amount, originalStan: receipt, originalDate: date);
```

| | `RequiresAmount` | `RequiresOriginalStan` | `RequiresOriginalDate` | `MovesMoney` |
|---|---|---|---|---|
| `Sale` | yes | | | yes |
| `Void` | | yes | | yes |
| `Refund` | yes | yes | yes | yes |
| `Inquiry` | | yes | yes | |
| `Receipt` | | yes | yes | |
| `CloseReceipt` | | | | |

`RunAsync` (`EcrTerminal` only) accepts only types that move money; `Inquiry`,
`Receipt` and `CloseReceipt` go through their own methods because they answer with
something found or acknowledged rather than something performed.
`EcrTransactionTypes.MenuOptions` is the operator's list: sale, void, refund,
inquiry. Whether an amount is within limits or a transaction may be voided needs the
backend and TMS configuration, so send the request and report the answer.

---

## 12. Testing without hardware

A stand-in listener that speaks the LAN protocol ships in the repository as
`tools/fake_pos_server.py` (Python 3):

```powershell
curl.exe -O https://raw.githubusercontent.com/amwal-pay/AmwalpayEcrWindows/main/tools/fake_pos_server.py

py fake_pos_server.py --port 9100                      # approves everything
py fake_pos_server.py --port 9100 --decline 51         # declines everything
py fake_pos_server.py --port 9100 --delay 8            # slow cardholder
py fake_pos_server.py --port 9100 --not-found          # inquiries answer 25, receipts refuse
```

(`curl.exe`, not `curl`: in Windows PowerShell 5 `curl` aliases `Invoke-WebRequest`.)
Address a terminal at your machine's IP, or `127.0.0.1`, and that port. If it runs on
another PC, that PC needs an inbound firewall rule for TCP 9100; see
[troubleshooting](troubleshooting.md#windows-network-and-host). It speaks only the LAN
protocol; there is no Web Service fake.

**The fake server does not sign its answers.** With a `SecureHashKey` configured the
SDK rejects them, so every call comes back `Failure.Unauthenticated`. That is the SDK
working correctly. To test the happy path, build the terminal directly with no key,
in test code only:

```csharp
var terminal = new EcrTerminal(
    host: "127.0.0.1",
    serialNumber: "FAKE-1",
    config: new EcrConfig { ResponseTimeout = TimeSpan.FromSeconds(30) });
```

`Plan` reports a missing key as an issue, so a plan for the fake server is never
ready. **Never ship a till with an empty key.**

Also test, before you ship:

- **Terminal off** (stop the fake server): `Failure.Unreachable`.
- **Slow terminal**: `--delay 8` with `ResponseTimeout = 3 s` gives `Failure.Timeout`;
  the auto-inquiry also fails, so `Recovered` is an `EcrInquiry.Failed`. Confirm you
  do not book it as a decline.
- **Link dropped mid-sale**: `--delay 30`, then kill the server: `ConnectionLost`.
- **A wrong key**: any valid hex key against the fake server: `Unauthenticated`.
- **Cancelling** during `--delay`: the till treats `OperationCanceledException` as
  unknown and resolves it by reference.
- On a real terminal: a second request while one runs gives `96`; a void of an
  unknown receipt number gives `25`.

---

## 13. Going to production

- [ ] `MinorUnitDigits` matches the currency; check an amount on the wire
- [ ] Amounts are `decimal` end to end, parsed and formatted with `CultureInfo.InvariantCulture`
- [ ] The result type decides the outcome, not `ResponseCode`
- [ ] `PartialApproval` is handled and goods are held for the balance
- [ ] `Failed` is never treated as a decline; on LAN, `Recovered` / `Settled` is checked before any retry
- [ ] `MerchantReference` is stored with the sale and written **before** the call when a token can cancel it
- [ ] Open sales are resolved by reference on start-up
- [ ] LAN and Web Service keys are stored separately and mapped to the right mode
- [ ] Keys come from protected configuration and are never logged
- [ ] `EcrSessions.Plan()` and `plan.IsReady` gate every client
- [ ] `AuthCode` is not printed on a void
- [ ] Terminals are stored by serial number; address looked up at send time
- [ ] `result.Raw` is logged with each transaction for disputes
- [ ] The Pay control is disabled while a sale is in flight; no `.Result` / `.Wait()`
- [ ] TMS has the right `terminalMode` and `ecrMode` on every terminal
- [ ] The till's clock and time zone are right (requests carry the till's local time; the terminal refuses more than five minutes' skew with `63`)
- [ ] Someone other than the developer has run the failure cases in [section 12](#12-testing-without-hardware)

### Storing keys with DPAPI

Encrypt the signing keys at rest with the Windows Data Protection API.
`ProtectedData` is built into .NET Framework (reference `System.Security`); on .NET 5+
add the `System.Security.Cryptography.ProtectedData` package. It is Windows-only.

```csharp
public static class KeyStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MyTill.AmwalEcr");

    public static void Save(string path, string hexKey)
    {
        var cipher = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(hexKey), Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(path, cipher);
    }

    public static string Load(string path)
    {
        var plain = ProtectedData.Unprotect(
            File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plain);
    }
}
```

- `CurrentUser` ties the ciphertext to the Windows account. `LocalMachine` lets any
  process on the machine decrypt it; it protects against theft of the file, not
  against code running on the till.
- A Windows Service running as a different account (or `LocalSystem`) cannot read a
  `CurrentUser` blob written by an interactive account. Provision the key as the
  account the service runs as.
- Store the LAN key and the Web Service key as separate entries.

### Running from a Windows Service

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "Amwal Till");

builder.Services.AddSingleton<EcrTerminal>(sp =>
{
    var log = sp.GetRequiredService<ILogger<EcrTerminal>>();
    var config = new EcrConfig { SecureHashKey = KeyStore.Load(keyPath) };
    return new EcrTerminal(
        host: "192.168.1.50",
        serialNumber: "P653200085189",
        config: config,
        logger: EcrLogger.From(m => log.LogDebug("{EcrMessage}", m)));
});
builder.Services.AddHostedService<TillWorker>();
builder.Build().Run();
```

(`AddWindowsService` is in `Microsoft.Extensions.Hosting.WindowsServices`.)

- **A stop request is a cancellation.** If `stoppingToken` fires mid-sale and you
  passed it to `SaleAsync`, you get `OperationCanceledException` and the terminal
  carries on. Do not pass the stopping token to a money-moving call unless you have
  the journal from [section 7](#make-the-unknown-recoverable), and resolve what it
  lists as open on start-up.
- **Services start before the network is up.** A first probe after a reboot can
  fail; retry instead of concluding the address is wrong.
- **The network profile applies to services.** A Public profile applies the Public
  firewall rules; check `Get-NetConnectionProfile`.

### Web Service on classic .NET Framework

Web Service ECR needs TLS 1.2. An app that targets .NET Framework earlier than 4.7
(the 4.6.2 floor included) must enable it once at start-up, before the first Web
Service call; an app targeting 4.7 or later uses the operating system's defaults.
LAN ECR is plain TCP and unaffected.

```csharp
ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
```

---

**Next:** the [API reference](api-reference.md), the [protocol](protocol.md) if you
need the wire format, and [troubleshooting](troubleshooting.md) when something does
not behave.
