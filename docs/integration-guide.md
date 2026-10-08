# Integration guide

From an empty project to a working sale, then everything you need to ship. Each
step says what to do, what you should see, and what it means when you see
something else.

> **Package 1.0.0.** This guide matches the first NuGet release of `AmwalPay.Ecr`.
> Pin `Version="[1.0.0]"`. Web Service inquiry supports lookup by STAN and by
> merchant reference (`/Ecr/Inquiry`); receipt fetch and auto-inquire after a
> lost answer remain LAN / USB only.

This guide is for .NET developers — WPF, WinForms, console, Windows Service,
classic .NET Framework till software, and ASP.NET web apps — integrating an Amwal
POS terminal into a till or a server. Two working samples, both on .NET 8, live in
one repository:
[`Windows-Native-Example`](https://github.com/amwal-pay/AmwalpayEcrWindowsExample/tree/main/Windows-Native-Example)
(a WPF till that follows this guide) and
[`AmwalECR-Web-Example`](https://github.com/amwal-pay/AmwalpayEcrWindowsExample/tree/main/AmwalECR-Web-Example)
(ASP.NET Core).

**Contents**

1. [Before you start](#1-before-you-start)
2. [Add the SDK](#2-add-the-sdk)
3. [Address a terminal](#3-address-a-terminal)
4. [Web Service ECR](#4-web-service-ecr) — [initialization](#initialization-process), caveat, support matrix
5. [Full examples — Wi‑Fi and Web Service](#5-full-examples--wifi-and-web-service) — [Wi‑Fi setup](#51-wifi--lan--setup) · [sale](#52-wifi--lan--sale) · [void](#53-wifi--lan--void) · [refund](#54-wifi--lan--refund) · [inquiry](#55-wifi--lan--inquiry-by-receipt-stan) · [by reference](#56-wifi--lan--inquiry-by-merchant-reference) · [receipt](#57-wifi--lan--e-receipt) · [WS setup](#58-web-service--setup) · [sale](#59-web-service--sale) · [void](#510-web-service--void) · [refund](#511-web-service--refund) · [inquiry](#512-web-service--inquiry-by-receipt-stan) · [by reference](#513-web-service--inquiry-by-merchant-reference)
6. [Your first sale](#6-your-first-sale)
7. [Handling the three outcomes](#7-handling-the-three-outcomes)
8. [Partial approval](#8-partial-approval)
9. [Void, refund, inquiry, receipt](#9-void-refund-inquiry-receipt)
10. [When the answer never comes](#10-when-the-answer-never-comes)
11. [Building your own form](#11-building-your-own-form)
12. [Threading and lifecycle](#12-threading-and-lifecycle)
13. [Logging](#13-logging)
14. [Testing without hardware](#14-testing-without-hardware)
15. [Going to production](#15-going-to-production)

---

## 1. Before you start

You need three things.

**A terminal in ECR mode.** Amwal's TMS decides this, not your application. Ask
whoever administers the terminal profile to set:

| Field | Value |
|---|---|
| `terminalMode` | `1` (ECR-attached) |
| `ecrMode` | `1` USB cable, `2` Wi-Fi, or `4` Web Service |

Until TMS sets `terminalMode: 1`, the terminal does not accept ECR requests.
With `ecrMode: 1` or `2` it serves the ECR protocol — on TCP port 9100 for Wi-Fi;
with `ecrMode: 4` it does not listen on TCP — the till uses HTTPS to the Hub
instead (see the table below).

| `ecrMode` | Transport | SDK client |
|---|---|---|
| `2` Wi-Fi | TCP to terminal IP, port 9100 | [EcrTerminal](api-reference.md#ecrterminal) via [EcrLink.Lan](api-reference.md#ecrlink) |
| `1` USB cable | A cable, no IP. **You supply the channel** — the package ships none | [EcrTerminal](api-reference.md#ecrterminal) via [EcrLink.UsbCable](api-reference.md#ecrlink) and your [IEcrChannel](api-reference.md#iecrchannel) |
| `4` Web Service | HTTPS to Amwal Hub | [EcrWebServiceTerminal](api-reference.md#ecrwebserviceterminal) via [EcrLink.WebService](api-reference.md#ecrlink) |
| `3` Bluetooth | Not served over TCP in this SDK | — |

**The same network (LAN only).** For Wi-Fi ECR the till and the terminal must be
able to reach each other on the LAN. The terminal is on Wi-Fi; the till can be on
Wi-Fi or on Ethernet, provided the route between them is open on TCP 9100 (see
[Windows firewall](troubleshooting.md#windows-network-and-host)). Web Service ECR
only needs internet access from the till.

**The terminal's address.** With `ecrMode: 2` the terminal shows its IP and port
under the card scheme logos on its idle screen. Read it off and store it against
the terminal's serial number.

> Store terminals **by serial number** and look the address up when you send. A
> terminal that reconnects to Wi-Fi can come back on a different IP, and the
> serial is the thing that does not change.

**Requirements.** The supported minimum is **.NET Framework 4.6.2** or **.NET 8**,
with **C# 9 or later** in your project (results are records, and `EcrConfig` uses
`init` properties). **4.7.2 or later is recommended** on .NET Framework, and **.NET 10
is the version to plan for** on modern .NET. That covers both Windows native apps
(WinForms, WPF, console, Windows Service) and web apps (ASP.NET Core, classic
ASP.NET). The package targets `netstandard2.0` and `net8.0`; there is no `net462`
build, so a 4.6.2 app uses the `netstandard2.0` one. Older runtimes may resolve and
compile but are not supported; the full matrix, and exactly what was and was not
verified, is in [Compatibility](compatibility.md#supported-platforms).

**Microsoft's end dates set ours.** .NET 8 and .NET 9 leave Microsoft support on
**Nov 10, 2026**, and .NET Framework 4.6.2 on **Jan 12, 2027**. After those dates
the supported minimum becomes .NET 10 and (unless the owners decide otherwise) .NET
Framework 4.7.2. If you are starting now, target .NET 10, or .NET Framework 4.7.2 or
4.8, and you will not have to move. The dated list is in the
[Support calendar](compatibility.md#support-calendar).

---

## 2. Add the SDK

The SDK is on **nuget.org**, so it resolves like any other public package — no
private feed, no credentials.

```powershell
dotnet add package AmwalPay.Ecr
```

or

```xml
<ItemGroup>
  <PackageReference Include="AmwalPay.Ecr" Version="[1.0.0]" />
</ItemGroup>
```

The current version is listed at <https://www.nuget.org/packages/AmwalPay.Ecr>.
**Pin it** — do not use a floating version such as `1.*`. Published versions never
change, so a fix arrives as a new version and you decide when to take it; a range
would let a payment client change behaviour without a commit on your side. The
square brackets mean "exactly this version"; a bare `Version="1.0.0"` means "1.0.0
or higher". Consider `<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>`
so the whole graph is pinned.

**Match the runtime to the support statement.** Target **.NET Framework 4.6.2 or
later** (4.7.2 or later recommended), or **.NET 8 or later**. .NET 8 and later use
the `net8.0` asset, which has no package dependencies. .NET Framework, 4.6.2
included, uses the `netstandard2.0` asset, which brings `System.Text.Json` 8.0.6 and
the `System.*` packages it depends on. A project that targets .NET Framework 4.6.1 or
lower is unsupported and gets "doesn't support net461" warnings from those
dependencies. The full dependency list is in
[Compatibility](compatibility.md#what-is-in-the-package).
`System.Text.Json` is an implementation detail: it is not on the public API except
in `EcrWireResponse.Data` and the return values of `EcrWebServiceRequestJson`, so it
does not clash with your own JSON library.

**Set the language version on .NET Framework projects.** SDK-style .NET Framework
projects (and classic projects) default to C# 7.3. The SDK's `EcrConfig` uses
init-only setters, so without a higher language version the build fails with
`CS8370` (C# 7.3) or `CS8400` (C# 8.0): "Feature 'init-only setters' is not
available … Please use language version 9.0 or greater". Add:

```xml
<PropertyGroup>
  <LangVersion>9.0</LangVersion>   <!-- or: latest -->
</PropertyGroup>
```

You need Visual Studio 2019 16.8+ or the .NET SDK 5+ to compile C# 9. Reading
results (`is EcrResult.Approved a`, `switch`) needs only C# 7; building an
`EcrConfig` with an object initializer, and `with` expressions, need 9.

**Two .NET Framework things to know up front.** Both are documented .NET
Framework behaviour, **not verified in this repository's test runs** (see
[Compatibility](compatibility.md#net-framework)):

- **Binding redirects.** The `netstandard2.0` asset needs redirects for the
  `System.*` facades. SDK-style projects generate them (`AutoGenerateBindingRedirects`);
  a classic `packages.config` project may need them by hand, in the *executable's*
  or web project's config. If the app fails with `FileLoadException`, see
  [troubleshooting](troubleshooting.md#could-not-load-file-or-assembly-fileloadexception-on-a-classic-net-framework-project).
- **TLS.** LAN ECR involves no TLS. Web Service ECR is HTTPS and needs TLS 1.2. An
  app that *targets* .NET Framework 4.7 or later uses the operating system's
  default versions; an app targeting a version before 4.7 (the 4.6.2 floor
  included) does not and needs
  `ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;` (see
  [§15](#web-service-on-classic-net-framework)).

---

## 3. Address a terminal

The SDK separates **how you reach a terminal** ([EcrLink](api-reference.md#ecrlink))
from **credentials and till settings** ([EcrConfig](api-reference.md#ecrconfig)).
Use [EcrSessions.Plan](api-reference.md#ecrsessions) to validate both before you
send — it returns issues rather than throwing, so your UI can show what is missing.

### Setup structure (same as the Android ECR-simulator)

Do **not** put a secure hash key on the terminal edit form. The simulator and the
Windows samples keep credentials in **global settings** and terminals as address
records only:

1. **Settings** — two mode-specific keys Amwal issued separately (plus SIT/UAT/PROD
   for Web Service): *Wi‑Fi/LAN secure hash key* and *Web Service secure hash key*.
2. **Register terminal** — name, serial, mode; Wi‑Fi → IP/port; Web Service →
   merchant/terminal IDs. No key field.
3. **Operate** — pick a terminal; the app calls `Plan` with
   `settings.SecureHashKeyFor(mode)` (one value), then `Open` / `SaleAsync` / ….

Seeing both key fields on the Terminals screen is intentional — they are different
secrets, not the same key entered twice. The transaction config card shows only
the key that applies to the selected mode.

### LAN / Wi‑Fi ECR

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

One instance addresses one terminal. It holds no connection between calls, so it
is cheap to keep and safe to reuse for the life of your application.

> `EcrConfig` validates as you assign: a `SecureHashKey` with a stray space, a
> `Port` of 0 or a `MinorUnitDigits` of 7 throws `ArgumentException` on the spot.
> When validating **typed** input in a settings UI, pass that string as
> `rawSecureHashKey` (and put an empty or already-valid key on `EcrConfig`) so a
> bad value becomes `plan.Issues` instead of an exception. From a validated store,
> omit `rawSecureHashKey` — it defaults to `config.SecureHashKey`. It is not a
> second secret.

**A plan is not ready without a key.** `Plan` adds *"Wi-Fi/LAN secure hash key is not
configured"* (or the Web Service equivalent) whenever the key is empty. See
[§14](#14-testing-without-hardware) if you need to talk to something that does not
sign.

**Probe before you pay.** `ProbeReachabilityAsync()` opens a TCP handshake and
returns whether the listener answered, plus the underlying error when it did not:

```csharp
EcrReachability probe = await terminal.ProbeReachabilityAsync();
if (!probe.Reachable)
{
    screen.Show($"{probe.Endpoint} — {probe.Error ?? "not reachable"}");
    return;
}
```

`IsReachableAsync()` is a convenience wrapper around `probe.Reachable`.

Everything else has a working default. To change the till's identity, the
currency, or the timeouts, set them on `EcrConfig`:

```csharp
new EcrConfig
{
    EcrId = "TILL-04",                            // appears on the terminal's records
    CurrencyCode = "512",                         // ISO 4217 numeric — 512 is OMR
    MinorUnitDigits = 3,                          // decimal places the currency has
    Port = 9100,
    ResponseTimeout = TimeSpan.FromSeconds(120),
}
```

`EcrConfig` is a record, so derive variants with `with`:
`config with { ResponseTimeout = TimeSpan.FromSeconds(60) }`.

> **`MinorUnitDigits` must match your currency.** It decides where the decimal
> point falls when the amount goes on the wire. Setting `840` (USD) while leaving
> `3` sends every amount ten times too large, and neither the SDK nor the
> terminal will object. See [`EcrConfig`](api-reference.md#ecrconfig).

### USB cable ECR — bring your own channel

**The C# package does not ship a USB channel.** The Android SDK's cable transport
uses Android's USB accessory mode, which has no Windows equivalent, and how a
Windows till talks to a terminal down a cable (a virtual COM port, a WinUSB
device, a vendor driver) is specific to your hardware. What the package gives you
is everything above the wire: `EcrLink.UsbCable`, `EcrSessions.UsbCableTerminal`,
`EcrSessions.Open(usbChannel: ...)`, the `IEcrChannel` interface, and `EcrFrames`
for the framing. Signing, nonces, answer checks, results and auto-inquiry work
exactly as on LAN.

```csharp
var plan = EcrSessions.Plan(
    link: new EcrLink.UsbCable(),
    config: new EcrConfig { SecureHashKey = secrets.LanKeyFor(serial) });   // same key as Wi-Fi

EcrTerminal terminal = EcrSessions.UsbCableTerminal(
    terminalSerial: "P653200085189",
    plan: plan,
    channel: new MyUsbChannel(pipe));
```

An `IEcrChannel` has to carry one framed request and bring back one framed answer.
A skeleton that sits on any `Stream` (a `SerialPort.BaseStream`, a WinUSB pipe
wrapper):

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

The rules a channel must keep, because the SDK's safety depends on them:

- **Frame with `EcrFrames`.** Do not write your own "two bytes then the body". One
  read is not one message: a transfer may carry several, or half of one — which is
  what `ReadBodyAsync` handles.
- **A silent terminal is `EcrChannelTimeout`.** Throw it when the answer does not
  arrive within the `timeout` you are given (that is `EcrConfig.ResponseTimeout`).
  It becomes `Failure.Timeout` — an unknown outcome, not a refusal.
- **After the request may have been written, a broken link is a lost connection.**
  The SDK classifies an `IOException` by its **message**: one containing `closed
  the connection` becomes `Failure.ConnectionLost` (outcome unknown, auto-inquiry
  runs); one containing `not valid JSON` or `empty message` becomes
  `Failure.Malformed`; **any other `IOException` becomes `Failure.Unreachable` —
  "nothing was sent, safe to retry."** So a channel that lets a raw `IOException`
  escape after the write has quietly told the till a payment did not happen. Wrap
  it as the skeleton does. Before anything is written (cable not plugged in),
  a plain `IOException` is right.
- **Do not decide anything.** No signing, no reading response codes, no retries.
- **Exceptions that are not `IOException`** are not converted into results; they
  propagate out of `SaleAsync`. Catch them in your channel if you want a result.

Then everything is identical to LAN. Alternatively let `EcrSessions.Open` build it
(next section) with `usbChannel: () => new MyUsbChannel(pipe)`.

### One call for every link: `EcrSessions.Open`

When your till supports several modes, `Open` builds the right client from a
ready plan and returns an [EcrOpenedSession](api-reference.md#ecropenedsession)
that exposes the same calls whichever transport is behind it:

```csharp
EcrOpenedSession session = EcrSessions.Open(
    terminalSerial: "P653200085189",
    plan: plan,                                   // must be ready, or Open throws ArgumentException
    usbChannel: () => new MyUsbChannel(pipe),     // only used for a USB plan
    logger: logger);

var result = await session.SaleAsync(1.234m, merchantReference: order.Number);
```

The session hides differences that matter, so read the
[matrix in §4](#lan-vs-web-service--what-the-sdk-supports-today) before relying on
it for anything beyond sale, void, refund and inquiry (STAN or by reference).
Receipt and reachability probes remain LAN / USB only.

### What the app owns vs what the SDK owns

| Your application | SDK |
|---|---|
| Terminal registry (serial, IP, mode, merchant/terminal IDs) | Transport choice, signing, wire format |
| Secure hash keys (from Amwal, per mode) | Validation via `EcrSessions.Plan()` |
| UI and credential storage | Hub URLs (`EcrEnvironment`), REST paths |
| Mapping stored records → `EcrLink` | Building `EcrTerminal` / `EcrWebServiceTerminal` |
| The USB channel, if you use a cable | Framing helpers (`EcrFrames`) for it |

Do not duplicate transport rules in the app — pass a link and config, let
`EcrSessions` validate and assemble the client.

---

## 4. Web Service ECR

When TMS sets `ecrMode: 4`, the till talks to the Amwal Hub over HTTPS instead
of TCP to the terminal's IP. The SDK resolves Hub URLs from
[`EcrEnvironment`](api-reference.md#ecrenvironment); your app only picks SIT, UAT,
or PROD and supplies backend IDs plus the **Web Service secure hash key** — which
is **not** the same secret as LAN ECR.

### Initialization process

Do this once per terminal (or whenever credentials change). Nothing is sent to the
Hub until the first `SaleAsync` / `VoidAsync` / `RefundAsync` / `InquireAsync`.

**A. Prerequisites (from Amwal / TMS)**

1. Terminal profile: `terminalMode: 1`, `ecrMode: 4`.
2. **Terminal serial** as registered in TMS.
3. **Merchant ID** and **terminal ID** (numeric).
4. **Environment**: SIT, UAT, or PROD.
5. **Web Service secure hash key** (even-length hex, at least 16 characters) —
   issued for REST, separate from the Wi‑Fi/LAN key.
6. On .NET Framework targeting a version **before 4.7**: enable TLS 1.2 at process
   start (`ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12`).

**B. Plan (validate without calling the Hub)**

```csharp
var plan = EcrSessions.Plan(
    link: new EcrLink.WebService(MerchantId: "13593", TerminalId: "101311"),
    config: new EcrConfig
    {
        SecureHashKey = secrets.WebServiceKey,   // from settings — not the Wi‑Fi/LAN key
        Environment = EcrEnvironment.UAT,
    });

if (!plan.IsReady)
{
    // Show plan.Issues — do not open a client yet
    screen.Show(string.Join("\n", plan.Issues));
    return;
}
// plan.ConnectionSummary → e.g. "UAT · merchant 13593 · terminal 101311" (no secrets)
// plan.Config.MerchantId / TerminalId were copied from the link
```

`Plan` may report:

| Issue | Cause |
|---|---|
| `Merchant ID is required for Web Service` / `Terminal ID is required…` | Blank IDs |
| `Merchant ID must be numeric` / `Terminal ID must be numeric` | Non-numeric IDs |
| `Secure hash key must be an even-length hex string of at least 16 characters` | Malformed key |
| `Web Service secure hash key is not configured` | Empty key after sanitization |

**C. Build a client**

```csharp
// Typed Web Service client
EcrWebServiceTerminal terminal = EcrSessions.WebServiceTerminal(
    terminalSerial: "P653200008251",
    plan: plan,
    logger: EcrLogger.From(message => Debug.WriteLine(message, "EcrWebService")));

// Or one session for every mode (LAN / USB / Web Service) — Open requires plan.IsReady
EcrOpenedSession session = EcrSessions.Open(
    terminalSerial: "P653200008251",
    plan: plan,
    logger: EcrLogger.From(message => Debug.WriteLine(message, "Ecr")));
```

`WebServiceTerminal` / `LanTerminal` do **not** re-check `IsReady` — only `Open`
does. Always gate on `plan.IsReady` yourself if you call the typed builders.

**D. First operation**

```csharp
switch (await terminal.SaleAsync(1.234m, merchantReference: order.Number))
{
    case EcrResult.Approved a: CompleteSale(a); break;
    case EcrResult.Declined d when d.NextStep == NextStep.InquireByMerchantReference:
        HandleUnknownOutcome(d); break;          // read the caveat below
    case EcrResult.Declined d: screen.Show(d.Reason); break;
    case EcrResult.Failed f:   HandleUnknownOutcome(f.Failure); break;
}
```

There is no reachability probe on Web Service (`ProbeReachabilityAsync` on an
opened session returns `null`). Store `merchantReference` **before** the call so
you can `InquireByReferenceAsync` after a lost answer.

**E. What the SDK sends (after init)**

| Item | Value |
|---|---|
| Hub base | From `EcrConfig.Environment` (SIT / UAT / PROD hosts below) |
| Paths | `/Ecr/Sale`, `/Ecr/Void`, `/Ecr/Refund`, `/Ecr/Inquiry` |
| Body | Hub contract fields including `messageType`, `requestDateTime` (= `transactionDateTime`) |
| Signature | Field `secureHashValue` under the Web Service key |

| Environment | Hub base |
|---|---|
| SIT | `https://test.amwalpg.com:25452` |
| UAT | `https://test.amwalpg.com:15452` |
| PROD | `https://pos.amwalpg.com` |

Web Service signing uses the same sorted `key=value&…` payload algorithm as LAN
ECR, but under the **Web Service key** Amwal issued for REST. Using the LAN key
produces valid-looking code that the Hub rejects.

### The Web Service caveat: a network error is a `Declined`

On LAN a lost answer is `EcrResult.Failed`. **On Web Service it is not.** The HTTP
layer turns every transport problem — a timeout, no network, a reset, an empty or
non-JSON reply — into a synthetic `success: false` envelope, and the SDK reads
`success: false` as a decline. Whenever the request may have reached the Hub, that
envelope carries `nextStep: INQUIRE_BY_MERCHANT_REFERENCE`, so:

- A timeout or no network on `SaleAsync` / `VoidAsync` / `RefundAsync` arrives as
  **`EcrResult.Declined` with `ResponseCode == "NO_RESPONSE"`**, the underlying
  error text in `Reason`, and **`NextStep == NextStep.InquireByMerchantReference`**.
- An empty body, an unreadable (non-JSON) body, or an HTTP 5xx with no readable
  message arrives the same way and carries the **same `NextStep`**, with the HTTP
  status as the `ResponseCode` (`502`, `504`, …). An HTTP 4xx with no readable
  message, and a real decline from the Hub, give `NextStep.None`.
- On `InquireAsync` a transport failure arrives as **`EcrInquiry.Failed` with
  `Failure.ConnectionLost`** — the lookup itself failed, still unknown, ask again.
  `EcrInquiry.NotFound` is a genuine "no such transaction" answer from the Hub.
- `EcrResult.Failed` occurs only when `MerchantId` / `TerminalId` on the config are
  not numeric (nothing was sent). `Failure.Timeout`, `Unreachable` and
  `Unauthenticated` are never produced on this transport.

**The reliable signal is `Declined.NextStep == NextStep.InquireByMerchantReference`
— inquire before retrying; it is not a refusal.** `ResponseCode == "NO_RESPONSE"`
is the common cause, but branch on `NextStep`, not on the code: the 5xx, empty and
unreadable cases carry the same `NextStep` under an HTTP status. This mirrors the
Android SDK.

```csharp
switch (await terminal.SaleAsync(amount, merchantReference: order.Number))
{
    case EcrResult.Approved a:
        CompleteSale(a);
        break;
    case EcrResult.Declined d when d.NextStep == NextStep.InquireByMerchantReference:
        HandleUnknownOutcome(d);                 // NO_RESPONSE, 5xx, empty or unreadable reply: inquire first
        break;
    case EcrResult.Declined d:
        screen.Show(d.Reason);                   // a real refusal
        break;
    case EcrResult.Failed f:
        HandleUnknownOutcome(f.Failure);
        break;
}

switch (await terminal.InquireAsync(receiptNumber: "215", transactionDate: "20260809"))
{
    case EcrInquiry.Found f:    Reconcile(f.Transaction);          break;
    case EcrInquiry.NotFound:   /* genuinely not found  */         break;
    case EcrInquiry.Failed:     /* lookup failed: still unknown, ask again */ break;
}
```

There is no **automatic** inquiry on Web Service after a lost sale — you resolve
it yourself. Prefer **`InquireByReferenceAsync(order.Number)`** with the
`merchantReference` you stored before the call (date may be blank). If you already
have the receipt number (STAN), use `InquireAsync(receiptNumber, transactionDate)`.
If both leave the outcome unknown, reconcile against the terminal's batch. Do not
resend the sale.

The whole request has one deadline of `ConnectTimeout + ResponseTimeout` (130 s by
default), not two.

### LAN vs Web Service — what the SDK supports today

| Capability | LAN / USB (`EcrTerminal`) | Web Service (`EcrWebServiceTerminal`) |
|---|---|---|
| Sale / void / refund | Yes | Yes |
| Inquiry by receipt (STAN + date) | Yes — `InquireAsync` | Yes — `InquireAsync` |
| Inquiry by merchant reference | Yes — `InquireByReferenceAsync` | Yes — sends `originalMerchantReference` (empty `stan`) |
| Receipt (e-receipt URL) | Yes — `ReceiptAsync` | **No** — no method; `EcrOpenedSession.ReceiptAsync` answers `EcrReceipt.Unavailable` |
| `originalTerminalId` (other till) | Yes — void/refund/inquiry/receipt | Yes on void/refund/inquiry (= request `terminalId`; other-till override not on the public API) |
| Reachability probe | Yes — `ProbeReachabilityAsync` | No — `EcrOpenedSession.ProbeReachabilityAsync` returns `null` |
| `RunAsync(type, …)` | Yes | **No** — use `SaleAsync` / `VoidAsync` / `RefundAsync` |
| Auto-inquire after unknown failure | Yes — `EcrConfig.AutoInquireOnFailure` (default `true`) | **No** |
| A lost answer is | `EcrResult.Failed` | `EcrResult.Declined` with `NextStep == InquireByMerchantReference` (usually `"NO_RESPONSE"`); inquiry: `EcrInquiry.Failed` / `ConnectionLost` |
| Request signing field | `secureHash` + `nonce` | `secureHashValue` (no nonce) |
| Response signature check | Yes (LAN) | No — Hub JSON envelope only |
| Timeouts | `ConnectTimeout` to connect, `ResponseTimeout` for the answer | One deadline: `ConnectTimeout + ResponseTimeout` |

Use separate signing keys per column. [EcrSessions.Plan](api-reference.md#ecrsessions)
validates the right credentials for each mode before you send.

Two more things about the Web Service client: it shares one static `HttpClient`
for the process and uses the machine's default proxy settings; and on classic
.NET Framework, Web Service needs TLS 1.2 (see
[troubleshooting](troubleshooting.md#web-service-ecr-on-net-framework-tls-12)).

---

## 5. Full examples — Wi‑Fi and Web Service

Copy-paste oriented. Each subsection is a complete flow for **one transport**.
Keys stay in app settings (two different secrets); terminal records hold only
addresses — same structure as the Android ECR-simulator. Narrative detail for
outcomes and recovery is in [§7](#7-handling-the-three-outcomes)–[§10](#10-when-the-answer-never-comes).

Shared assumptions in the snippets below:

```csharp
using System.Diagnostics;
using System.Globalization;
using AmwalPay.Ecr;

// From your secret store / settings — never hard-code in production
string wifiKey = secrets.WifiSecureHashKey;           // Wi‑Fi / USB / Bluetooth
string webServiceKey = secrets.WebServiceSecureHashKey; // Hub REST only
```

### 5.1 Wi‑Fi / LAN — setup

TMS: `terminalMode: 1`, `ecrMode: 2`. Till and terminal on the same LAN; terminal
shows IP:port under the card logos on the idle screen.

```csharp
EcrTerminal OpenWifiTerminal(
    string host,
    string serial,
    string wifiSecureHashKey,
    IEcrLogger? logger = null)
{
    var plan = EcrSessions.Plan(
        link: new EcrLink.Lan(Host: host, Port: 9100),
        config: new EcrConfig
        {
            SecureHashKey = wifiSecureHashKey,
            EcrId = "TILL-04",
            CurrencyCode = "512",       // OMR
            MinorUnitDigits = 3,
            ResponseTimeout = TimeSpan.FromSeconds(120),
        });

    if (!plan.IsReady)
        throw new InvalidOperationException(string.Join("\n", plan.Issues));

    return EcrSessions.LanTerminal(serial, plan, logger ?? EcrLogger.From(Debug.WriteLine));
}

// Usage
EcrTerminal wifi = OpenWifiTerminal(
    host: "192.168.1.50",
    serial: "P653200085189",
    wifiSecureHashKey: wifiKey);
```

Optional probe before any money-moving call (LAN only):

```csharp
if (!await wifi.IsReachableAsync())
{
    screen.Show("Terminal not reachable — check power, Wi‑Fi, and firewall (TCP 9100)");
    return;
}
```

### 5.2 Wi‑Fi / LAN — sale

```csharp
async Task WifiSaleAsync(EcrTerminal wifi, decimal amount, string orderNumber)
{
    // Store orderNumber before the call — needed if the answer is lost
    switch (await wifi.SaleAsync(amount, merchantReference: orderNumber))
    {
        case EcrResult.Approved a when a.PartialApproval:
            var requested = decimal.Parse(a.RequestedAmount, CultureInfo.InvariantCulture);
            var taken = decimal.Parse(a.Amount, CultureInfo.InvariantCulture);
            screen.Show($"Only {a.Amount} approved. Collect {requested - taken} another way.");
            CompleteSale(a);
            break;

        case EcrResult.Approved a:
            CompleteSale(a);   // a.Amount, a.Rrn, a.AuthCode, a.MaskedPan
            break;

        case EcrResult.Declined d when d.NextStep == NextStep.InquireByMerchantReference:
            HandleUnknownOutcome(orderNumber, d.Reason);   // inquire; do not retry
            break;

        case EcrResult.Declined d:
            screen.Show(d.Reason);
            break;

        case EcrResult.Failed f when f.Settled:
            Reconcile(((EcrInquiry.Found)f.Recovered!).Transaction);
            break;

        case EcrResult.Failed f:
            HandleUnknownOutcome(orderNumber, f.Failure.Message);
            break;
    }
}
```

### 5.3 Wi‑Fi / LAN — void

```csharp
async Task WifiVoidAsync(EcrTerminal wifi, string receiptNumber)
{
    switch (await wifi.VoidAsync(receiptNumber: receiptNumber))
    {
        case EcrResult.Approved a:
            // Do not print a.AuthCode on a void — it is a placeholder
            RecordVoid(a.Rrn, a.Amount);
            break;
        case EcrResult.Declined d:
            screen.Show(d.Reason);   // not found, outside void window, already voided, …
            break;
        case EcrResult.Failed f:
            HandleUnknownOutcome(merchantReference: "", f.Failure.Message);
            break;
    }
}

// Original was taken on another till (LAN only)
await wifi.VoidAsync(receiptNumber: "215", originalTerminalId: "31629");
```

### 5.4 Wi‑Fi / LAN — refund

```csharp
async Task WifiRefundAsync(
    EcrTerminal wifi,
    decimal amount,
    string receiptNumber,
    string transactionDate)   // yyyyMMdd — receipt numbers are unique per day
{
    // Cardholder must present the card
    switch (await wifi.RefundAsync(
        amount: amount,
        receiptNumber: receiptNumber,
        transactionDate: transactionDate))
    {
        case EcrResult.Approved a: CompleteRefund(a); break;
        case EcrResult.Declined d: screen.Show(d.Reason); break;
        case EcrResult.Failed f:   HandleUnknownOutcome("", f.Failure.Message); break;
    }
}
```

### 5.5 Wi‑Fi / LAN — inquiry by receipt (STAN)

```csharp
async Task WifiInquireByStanAsync(
    EcrTerminal wifi,
    string receiptNumber,
    string transactionDate)   // yyyyMMdd
{
    switch (await wifi.InquireAsync(
        receiptNumber: receiptNumber,
        transactionDate: transactionDate))
    {
        case EcrInquiry.Found found:
            var txn = found.Transaction;
            screen.Show($"{txn.Type} {txn.Status} {txn.Amount} {txn.Currency}");
            if (txn.CanVoid) EnableVoid();
            if (txn.CanRefund) EnableRefund();
            break;
        case EcrInquiry.NotFound n:
            screen.Show(n.Reason);
            break;
        case EcrInquiry.Failed f:
            screen.Show(f.Failure.Message);   // lookup failed — still unknown
            break;
    }
}
```

### 5.6 Wi‑Fi / LAN — inquiry by merchant reference

```csharp
async Task WifiInquireByReferenceAsync(EcrTerminal wifi, string orderNumber)
{
    switch (await wifi.InquireByReferenceAsync(
        originalReference: orderNumber,
        transactionDate: ""))   // blank = do not invent a day
    {
        case EcrInquiry.Found found:  Reconcile(found.Transaction); break;
        case EcrInquiry.NotFound:     /* nothing taken — safe to offer a new sale */ break;
        case EcrInquiry.Failed:       Escalate(); break;
    }
}
```

### 5.7 Wi‑Fi / LAN — e-receipt

```csharp
async Task WifiReceiptAsync(
    EcrTerminal wifi,
    string receiptNumber,
    string transactionDate)
{
    switch (await wifi.ReceiptAsync(
        receiptNumber: receiptNumber,
        transactionDate: transactionDate))
    {
        case EcrReceipt.Ready ready:         screen.ShowQrCode(ready.Url); break;
        case EcrReceipt.Unavailable missing: screen.Show(missing.Reason); break;
        case EcrReceipt.Failed failed:       screen.Show(failed.Failure.Message); break;
    }
}
```

---

### 5.8 Web Service — setup

TMS: `terminalMode: 1`, `ecrMode: 4`. No terminal IP — HTTPS to the Amwal Hub.
Use the **Web Service** key and numeric merchant/terminal IDs. On .NET Framework
before 4.7, enable TLS 1.2 once at process start.

```csharp
EcrWebServiceTerminal OpenWebServiceTerminal(
    string serial,
    string merchantId,
    string terminalId,
    string webServiceSecureHashKey,
    EcrEnvironment environment = EcrEnvironment.UAT,
    IEcrLogger? logger = null)
{
    var plan = EcrSessions.Plan(
        link: new EcrLink.WebService(MerchantId: merchantId, TerminalId: terminalId),
        config: new EcrConfig
        {
            SecureHashKey = webServiceSecureHashKey,   // not the Wi‑Fi key
            Environment = environment,                 // SIT / UAT / PROD → Hub URL
            EcrId = "TILL-04",
            CurrencyCode = "512",
            MinorUnitDigits = 3,
        });

    if (!plan.IsReady)
        throw new InvalidOperationException(string.Join("\n", plan.Issues));
    // plan.ConnectionSummary → e.g. "UAT · merchant 13593 · terminal 101311"

    return EcrSessions.WebServiceTerminal(
        serial,
        plan,
        logger ?? EcrLogger.From(msg => Debug.WriteLine(msg, "EcrWebService")));
}

// Usage
EcrWebServiceTerminal hub = OpenWebServiceTerminal(
    serial: "P653200008251",
    merchantId: "13593",
    terminalId: "101311",
    webServiceSecureHashKey: webServiceKey,
    environment: EcrEnvironment.UAT);

// There is no IsReachableAsync / ProbeReachabilityAsync on Web Service.
```

Hub hosts (SDK-internal): SIT `https://test.amwalpg.com:25452`, UAT
`https://test.amwalpg.com:15452`, PROD `https://pos.amwalpg.com`. Paths:
`/Ecr/Sale`, `/Ecr/Void`, `/Ecr/Refund`, `/Ecr/Inquiry`.

### 5.9 Web Service — sale

```csharp
async Task WebServiceSaleAsync(
    EcrWebServiceTerminal hub,
    decimal amount,
    string orderNumber)
{
    // Always pass and persist merchantReference before the call
    switch (await hub.SaleAsync(amount, merchantReference: orderNumber))
    {
        case EcrResult.Approved a when a.PartialApproval:
            CompleteSale(a);   // still collect the remainder
            break;

        case EcrResult.Approved a:
            CompleteSale(a);
            break;

        // Transport / unknown outcome — NOT a refusal. Inquire; do not resend.
        case EcrResult.Declined d when d.NextStep == NextStep.InquireByMerchantReference:
            HandleUnknownOutcome(orderNumber, d.Reason);   // often ResponseCode NO_RESPONSE
            break;

        case EcrResult.Declined d:
            screen.Show(d.Reason);   // real Hub refusal
            break;

        case EcrResult.Failed f:
            // Only non-numeric merchant/terminal IDs (nothing was sent)
            HandleUnknownOutcome(orderNumber, f.Failure.Message);
            break;
    }
}
```

### 5.10 Web Service — void

```csharp
async Task WebServiceVoidAsync(EcrWebServiceTerminal hub, string receiptNumber)
{
    switch (await hub.VoidAsync(receiptNumber: receiptNumber))
    {
        case EcrResult.Approved a:
            RecordVoid(a.Rrn, a.Amount);
            break;
        case EcrResult.Declined d when d.NextStep == NextStep.InquireByMerchantReference:
            HandleUnknownOutcome("", d.Reason);
            break;
        case EcrResult.Declined d:
            screen.Show(d.Reason);
            break;
        case EcrResult.Failed f:
            HandleUnknownOutcome("", f.Failure.Message);
            break;
    }
}
```

### 5.11 Web Service — refund

```csharp
async Task WebServiceRefundAsync(
    EcrWebServiceTerminal hub,
    decimal amount,
    string receiptNumber,
    string transactionDate)   // yyyyMMdd
{
    switch (await hub.RefundAsync(
        amount: amount,
        receiptNumber: receiptNumber,
        transactionDate: transactionDate))
    {
        case EcrResult.Approved a: CompleteRefund(a); break;
        case EcrResult.Declined d when d.NextStep == NextStep.InquireByMerchantReference:
            HandleUnknownOutcome("", d.Reason); break;
        case EcrResult.Declined d: screen.Show(d.Reason); break;
        case EcrResult.Failed f:   HandleUnknownOutcome("", f.Failure.Message); break;
    }
}
```

### 5.12 Web Service — inquiry by receipt (STAN)

```csharp
async Task WebServiceInquireByStanAsync(
    EcrWebServiceTerminal hub,
    string receiptNumber,
    string transactionDate)   // yyyyMMdd
{
    switch (await hub.InquireAsync(
        receiptNumber: receiptNumber,
        transactionDate: transactionDate))
    {
        case EcrInquiry.Found found:
            Reconcile(found.Transaction);
            break;
        case EcrInquiry.NotFound n:
            screen.Show(n.Reason);   // Hub said not found — not a transport failure
            break;
        case EcrInquiry.Failed f:
            // Lookup itself failed (often ConnectionLost) — still unknown; ask again
            screen.Show(f.Failure.Message);
            break;
    }
}
```

### 5.13 Web Service — inquiry by merchant reference

```csharp
async Task WebServiceInquireByReferenceAsync(
    EcrWebServiceTerminal hub,
    string orderNumber)
{
    // Prefer this after a lost sale. Date may be blank — do not invent "today".
    switch (await hub.InquireByReferenceAsync(
        originalReference: orderNumber,
        transactionDate: ""))
    {
        case EcrInquiry.Found found:  Reconcile(found.Transaction); break;
        case EcrInquiry.NotFound:     /* Hub has no match — still may need batch reconcile */ break;
        case EcrInquiry.Failed:       Escalate(); break;
    }
}
```

### 5.14 Web Service — what is not available

| Operation | Wi‑Fi | Web Service |
|---|---|---|
| `ReceiptAsync` / e-receipt QR | Yes | **No** — no method; opened session returns `EcrReceipt.Unavailable` |
| `IsReachableAsync` / probe | Yes | **No** |
| `originalTerminalId` on void/refund/inquiry | Yes | Not on the public Web Service API |
| Auto-inquire after lost sale | Yes (default) | **No** — call `InquireByReferenceAsync` yourself |

Mode-agnostic alternative after either setup: `EcrSessions.Open(serial, plan)` returns
an `EcrOpenedSession` with the same operation names; check `UsesLan` /
`UsesWebService` before probe, receipt, or `originalTerminalId`.

---

## 6. Your first sale

For complete, transport-specific snippets (setup + sale/void/refund/inquiry/receipt),
see [§5](#5-full-examples--wifi-and-web-service).

```csharp
async Task TakePaymentAsync(decimal amount)
{
    if (!await terminal.IsReachableAsync())
    {
        screen.Show("Terminal not reachable — check it is on and on this network");
        return;
    }

    switch (await terminal.SaleAsync(amount))
    {
        case EcrResult.Approved a:
            receipt.Print(amount: a.Amount, rrn: a.Rrn, auth: a.AuthCode);
            break;
        case EcrResult.Declined d:
            screen.Show(d.Reason);
            break;
        case EcrResult.Failed f:
            screen.Show(f.Failure.Message);
            break;
    }
}
```

`SaleAsync` completes when the cardholder has paid. That is normally a few seconds
and can be ninety; keep your progress UI up and do not impose a shorter timeout of
your own.

**Use `decimal`, never `double`.** `0.1 + 0.2` is not `0.3` in binary floating
point, and this is money. In C# the compiler helps:

```csharp
terminal.SaleAsync(1.234m);          // correct
terminal.SaleAsync(1.234);           // does not compile — a double literal
terminal.SaleAsync((decimal)price);  // compiles, and is already imprecise if price is a double
```

Parse text with the invariant culture, or `1.234` becomes `1,234` on a machine with
a European or Arabic regional setting:
`decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture)`. The
amounts you get back (`Approved.Amount`, `EcrTransaction.Amount`) are strings in
that same invariant format — parse them the same way, and never with the current
culture.

An amount with more decimal places than `MinorUnitDigits` is rounded, away from
zero at the midpoint, on its way to the wire (`1.2345m` goes out as `1.235`). Round
it yourself so your books and the terminal agree. The SDK does not check that an
amount is positive or within a limit; the terminal answers `13` for an invalid one.

**Check first (LAN / USB only).** `IsReachableAsync()` and `ProbeReachabilityAsync()`
apply to [EcrTerminal](api-reference.md#ecrterminal) only — not Web Service ECR.
They turn a wrong address or a terminal on another network into an immediate
answer rather than a wait the cardholder sits through. They prove the port is
open, not that the terminal is idle — a terminal already taking a payment answers
the probe and then rejects the request as busy.

---

## 7. Handling the three outcomes

`EcrResult` has three cases and the difference between the last two is the whole
game:

| | Meaning | What a till should do |
|---|---|---|
| `Approved` | Money was taken | Complete the sale |
| `Declined` | The terminal answered and refused | Ask for another payment method |
| `Failed` | **Nothing was learned** | Do not retry blindly — see §10 |

```csharp
switch (await terminal.SaleAsync(amount))
{
    case EcrResult.Approved a:
        a.Amount           // "1.234" — what was actually taken, major units
        a.ResponseCode     // "00"
        a.Rrn              // retrieval reference number
        a.AuthCode         // authorisation code; placeholder on a void
        a.MaskedPan        // "543173xxxx5785" — may be empty
        a.PartialApproval  // see §8
        a.Raw              // the terminal's full answer, as JSON text
        break;
    case EcrResult.Declined d:
        d.ResponseCode
        d.Reason
        d.NextStep         // None, or InquireByMerchantReference
        d.Raw
        break;
    case EcrResult.Failed f:
        f.Failure
        f.Recovered        // LAN auto-inquiry finding, when configured
        f.NextStep         // always InquireByMerchantReference
        f.Settled          // true when Recovered is Found
        break;
}
```

`Raw` is the terminal's answer verbatim, as text. It is there so you never lose a
field the SDK does not surface — log it with the transaction.

**A note on `switch`.** The result types are a closed set in spirit, but C# cannot
prove it: a `switch` *expression* over `EcrResult` needs a final discard arm
(`_ => throw new InvalidOperationException(...)`) or the compiler warns it is not
exhaustive. Handle all three cases explicitly and let the discard arm be the
"this SDK grew a fourth case" alarm.

**Do not decide from `ResponseCode`.** A partial approval that the operator then
voids carries the bank's own `00`. `EcrResult` already accounts for this: an
outcome that is not an approval never arrives as `Approved`.

**Some `Declined` results mean "ask", not "no".** Check `Declined.NextStep`: when it
is `NextStep.InquireByMerchantReference` the outcome is unknown — inquire, do not
retry. That is how the terminal reports `94` (this exact message was already
answered — the transaction probably exists) and `91` (the transaction did not
complete, or the terminal could not say), and how the SDK reports a Web Service
transport failure (`NO_RESPONSE`, 5xx, empty or unreadable reply). Treat `91` and
`94` the same way even if `NextStep` is absent.

### Signing what you send

Anything on the shop network can reach the terminal's port. Signing is what
separates your till from everything else on that network.

Amwal issues secrets **per terminal and per transport**. Put them in the config
and the SDK signs every **LAN request** and verifies **LAN responses** (when a
key is configured). Web Service requests are signed; Hub responses are read from
the JSON envelope without a local signature check.

```csharp
// LAN ECR — field secureHash, Wi‑Fi and USB cable key (from settings)
var lanPlan = EcrSessions.Plan(
    link: new EcrLink.Lan(Host: registered.IpAddress),
    config: new EcrConfig { SecureHashKey = secrets.LanKeyFor(registered.SerialNumber) });

// Web Service ECR — field secureHashValue, REST key (different secret from settings)
var wsPlan = EcrSessions.Plan(
    link: new EcrLink.WebService(MerchantId: registered.MerchantId, TerminalId: registered.TerminalId),
    config: new EcrConfig
    {
        SecureHashKey = secrets.WebServiceKey,
        Environment = EcrEnvironment.UAT,
    });
```

Both transports use the same **algorithm** — HMAC-SHA256 over a sorted
`key=value&…` string, key decoded from hex — but **different field names and
different secrets**. Using the LAN key against the Hub (or the Web Service key
over TCP) fails with authentication errors that look like network problems.

Validate keys with `EcrConfig.IsValidSecureHashKey()` in your settings UI before
persisting them.

Three things signing buys you, and one it does not:

- Nothing on the network can **issue a refund** on your terminal
- Nothing can **change an amount** in flight
- Nothing can **replay** a captured sale, and nothing can **fake an approval**
  back to your till
- It does **not** hide amounts from a passive listener — the traffic is
  authenticated, not encrypted. There is no card number in it to protect

Treat keys like passwords: not in your repository, not in a log, and not shared
between customers — and on Windows, encrypted at rest (see
[§15](#15-going-to-production)). If one leaks, ask Amwal to rotate it.

Until a terminal has been issued a key it accepts unsigned messages, so you can
configure this before or after Amwal switches it on without a flag day.

**If every call comes back `Failure.Unauthenticated`**, the key here and the key
on the terminal (or Hub) do not match — and the **wrong key type for the mode**
is the first thing to check.

### Naming the transaction as your system does

Every outcome carries a `MerchantReference`, and every operation takes one.

If your system already numbers what it sells — an order number, a basket id, a
ticket number — pass that, and the same string identifies the transaction in your
books, in the terminal's records and in any later inquiry. There is nothing to
map and nothing to reconcile by timestamp:

```csharp
var result = await terminal.SaleAsync(amount, merchantReference: order.Number);
// result.MerchantReference == order.Number
```

Leave it out and the SDK generates one, and reports it back on the result. Store
it with the sale either way — it is what names the transaction afterwards:

```csharp
var result = await terminal.SaleAsync(amount);
orders.Record(order.Id, reference: result.MerchantReference);
```

**If you pass a `CancellationToken` — or the process might die mid-sale — pass your
own reference and record it *before* the call.** A generated reference reaches you
only on the result, and a cancelled or crashed call has no result.

The reference is 1–32 characters of printable ASCII, without spaces and without
`&` or `=`. Anything else throws `ArgumentException` before a connection is
opened (from the `await`, since the methods are `async`), so a bad reference
fails at the till rather than at the terminal.

Uniqueness is yours to decide: the terminal carries the reference and does not
check it. Reusing one is allowed and is occasionally what you want — a retry of
the same order — but two genuinely different sales sharing a reference will be
indistinguishable in your own records later.

---

## 8. Partial approval

The bank may authorise **less** than was asked for. That is an approval, not a
refusal, and the customer still owes the difference:

```csharp
case EcrResult.Approved a:
    if (a.PartialApproval)
    {
        var requested = decimal.Parse(a.RequestedAmount, CultureInfo.InvariantCulture);
        var taken = decimal.Parse(a.Amount, CultureInfo.InvariantCulture);
        screen.Show($"Only {a.Amount} was approved. Collect {requested - taken} by other means.");
        // Do not release the goods until the balance is settled.
    }
    else
    {
        CompleteSale(a.Amount);
    }
    break;
```

`Amount` is **always** what was actually taken. Ignoring `PartialApproval` means
handing over goods for money you did not receive.

---

## 9. Void, refund, inquiry, receipt

Full Wi‑Fi and Web Service listings for each call are in
[§5](#5-full-examples--wifi-and-web-service). This section explains the rules;
§5 is the copy-paste form.

### Void — cancel a transaction in full

```csharp
var result = await terminal.VoidAsync(receiptNumber: "215");
```

No amount: a void returns exactly what the original took. No card is presented
for a transaction taken on the same terminal, so it completes in about a second.

Voids can be refused before anything reaches the backend — the original was not
found, it is outside the void window TMS configured, or it cannot be voided
(usually because it already was). Those arrive as `Declined` with the reason.

**Do not print `AuthCode` on a void.** It reverses an authorisation rather than
making one, so the terminal returns a placeholder; printing it reads as an
approval that never happened.

### Refund — return money against an earlier transaction

```csharp
var result = await terminal.RefundAsync(
    amount: 0.500m,
    receiptNumber: "208",
    transactionDate: "20260809");   // yyyyMMdd
```

The date is required because a receipt number is only unique within a terminal's
day. **The cardholder must present their card** to receive the money.

Whether the refund is allowed, and for how much, is the backend's decision. Do
not pre-validate it in your till; send it and report the answer.

### Inquiry — ask what became of a transaction

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
        screen.Show(failed.Failure.Message);
        break;
}
```

`Status` is the **transaction's** outcome, not the inquiry's — finding a declined
sale is a successful inquiry. `CanVoid` and `CanRefund` have already had the void
window and the backend's rules applied.

Safe to repeat, and answered even while the terminal is taking a payment.

**Inquiry by merchant reference (LAN, USB, and Web Service).** After a lost sale,
you often have the `MerchantReference` but not yet a receipt number:

```csharp
switch (await terminal.InquireByReferenceAsync(
    originalReference: order.Number,
    transactionDate: ""))                  // optional; yyyyMMdd narrows the day
{
    case EcrInquiry.Found found:    Reconcile(found.Transaction); break;
    case EcrInquiry.NotFound:       /* nothing was taken; safe to retry the sale */ break;
    case EcrInquiry.Failed:         Escalate(); break;   // lookup failed: still unknown
}
```

On Web Service the SDK posts to `/Ecr/Inquiry` with empty `stan` and
`originalMerchantReference` set (plus Hub-required `requestDateTime`). A blank
`transactionDate` is valid; supply `yyyyMMdd` when you know the day.

### Receipt — the e-receipt as a QR code (LAN / USB only)

```csharp
// EcrTerminal only — not available on EcrWebServiceTerminal
switch (await terminal.ReceiptAsync(receiptNumber: "215", transactionDate: "20260809"))
{
    case EcrReceipt.Ready ready:         screen.ShowQrCode(ready.Url); break;
    case EcrReceipt.Unavailable missing: screen.Show(missing.Reason); break;
    case EcrReceipt.Failed failed:       screen.Show(failed.Failure.Message); break;
}
```

The customer scans the code and reads the receipt on their own phone. Render the
QR locally (any .NET QR library that produces a bitmap or a `DrawingImage`) — it
then appears the moment the URL arrives and survives the network dropping
afterwards.

### Acting on another terminal's transaction (LAN / USB only)

On LAN ECR, a void, refund, inquiry or receipt can target a transaction taken on
a different till. [EcrWebServiceTerminal](api-reference.md#ecrwebserviceterminal)
does not expose `originalTerminalId` — and an `EcrOpenedSession` over Web Service
**ignores it without warning**, so a void meant for another till would be sent as
if for this one. Check `session.UsesLocalTerminal` before passing it.

```csharp
await terminal.VoidAsync(receiptNumber: "215", originalTerminalId: "31629");
```

Leave it out and the terminal assumes its own.

---

## 10. When the answer never comes

**This is the most important section in this guide.**

```csharp
case EcrResult.Failed f:
    switch (f.Failure)
    {
        case Failure.Unreachable:     /* nothing is listening: safe, nothing happened */ break;
        case Failure.Timeout:         /* UNKNOWN — the payment may have completed */     break;
        case Failure.ConnectionLost:  /* UNKNOWN — the payment may have completed */     break;
        case Failure.Malformed:       /* an answer the SDK could not read — UNKNOWN */   break;
        case Failure.Unauthenticated: /* an answer that could not be trusted — UNKNOWN */ break;
    }
    break;
```

`Failure.OutcomeUnknown` is `false` only for `Unreachable`. What each one means on
LAN / USB:

| | Meant by | Payment happened? |
|---|---|---|
| `Unreachable` | The connection was never made: refused, no route, unknown host, or **connect timeout** (`ConnectTimeout`, 10 s). Nothing was sent | **No** |
| `Timeout` | The request **was written** and no answer came within `ResponseTimeout` (120 s) | **Unknown** |
| `ConnectionLost` | The link reset, aborted or closed **after the request was written**, or part-way through the answer | **Unknown** |
| `Malformed` | An answer arrived that was empty or not JSON | **Unknown** |
| `Unauthenticated` | An answer arrived that was unsigned, signed with the wrong key, or answered a different request | **Unknown** |

The split between the first two is deliberate: a connect timeout means nothing was
sent, so there is nothing to reconcile and no auto-inquiry is run. Only a timeout
waiting for the *answer* counts as `Timeout`.

`Unreachable` is safe. The rest are **unknown outcomes**. The card flow on the
terminal does not depend on the socket staying up, so the terminal may have taken
the money and simply been unable to say so.

Never treat an unknown outcome as a decline. Resolve it:

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
            return null;    // still unknown — a human must look
    }
}
```

With `EcrConfig.AutoInquireOnFailure = true` (the default, **LAN / USB only**),
[EcrTerminal](api-reference.md#ecrterminal) follows an unknown failure with
`InquireByReferenceAsync()` using the sale's `MerchantReference` — one extra
connection — and attaches the finding to `EcrResult.Failed.Recovered`. Check
`f.Recovered` and `f.Settled` before prompting the operator to inquire manually.
`Settled` means the transaction was **found**; it can still be a declined sale, so
read `((EcrInquiry.Found)f.Recovered!).Transaction.Status`. After a `Timeout`, the
call can take up to a second `ResponseTimeout` (plus connect) on top of the first,
because the follow-up is itself a request. [EcrWebServiceTerminal](api-reference.md#ecrwebserviceterminal)
does not auto-inquire, and its network errors are not `Failed` at all — see
[§4](#4-web-service-ecr).

When you resolve manually:

- **By reference** (after a lost sale where you stored `MerchantReference`) —
  `InquireByReferenceAsync()` on either transport.
- **By receipt number** — `InquireAsync(receiptNumber, transactionDate)` on either
  transport, when you already have the STAN from a partial answer or the terminal
  display.

Retrying a sale blind is how a customer gets charged twice. Reconcile against the
terminal's batch if both routes still leave the outcome unknown.

### Make the unknown recoverable

An unknown outcome you cannot look up is just a loss. Two habits make it
recoverable, and both are cheap:

1. **Write the reference down before you send**, in something that survives a
   crash — a row in your till's database keyed by order. Pass it as
   `merchantReference`.
2. **On start-up (and after any unknown outcome), resolve what is still open** with
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

---

## 11. Building your own form

`EcrTransactionType` declares what each operation needs, so drive your UI from it
instead of repeating the rules. The flags are extension methods in
`EcrTransactionTypes`:

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
| `Sale` | ✔ | | | ✔ |
| `Void` | | ✔ | | ✔ |
| `Refund` | ✔ | ✔ | ✔ | ✔ |
| `Inquiry` | | ✔ | ✔ | |
| `Receipt` | | ✔ | ✔ | |

`RunAsync()` accepts only the types that move money. `Inquiry` and `Receipt` go
through `InquireAsync()` and `ReceiptAsync()`, because they answer with something
they *found* rather than something they performed — conflating the two is how a
lookup gets booked as a payment. `RunAsync` exists on `EcrTerminal` only.

`EcrTransactionTypes.MenuOptions` is the subset an operator picks from: sale,
void, refund, inquiry. `Receipt` is absent by design — a receipt follows a
transaction already on screen, so it belongs on that result rather than in a list
of operations.

**Validate only what you can answer.** Whether an amount is within the terminal's
limits, whether a transaction may be voided, whether a refund is allowed — all of
that needs the payment backend and the terminal's TMS configuration. Send the
request and report the answer.

---

## 12. Threading and lifecycle

Every call is an `async` method returning a `Task`. It never blocks the calling
thread on network I/O, and it uses `ConfigureAwait(false)` for its own awaits, so
it neither captures nor needs your synchronisation context. You do not need a
thread of your own, and you do not need `Task.Run`.

### WPF and WinForms

`await` the call straight from the UI thread. The code after the `await` resumes on
the UI thread, so you can touch controls and bound properties with no marshalling:

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
            // Still on the UI thread here.
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

In a code-behind event handler use `async void`, and put a `try/catch` around the
body — an exception escaping `async void` takes the application down:

```csharp
private async void PayButton_Click(object sender, RoutedEventArgs e)
{
    PayButton.IsEnabled = false;
    try { await _viewModel.PayAsync(_order); }
    catch (Exception ex) { ShowError(ex.Message); }
    finally { PayButton.IsEnabled = true; }
}
```

**Never block on it.** `SaleAsync(...).Result` or `.Wait()` will not deadlock —
the SDK never posts back to your context, and a regression test blocks from a
single-threaded context (the shape of a UI thread or a classic ASP.NET request) on
the LAN, recovery, timeout and Web Service paths — but the call takes as long as
the cardholder takes, and your window freezes for all of it. (That test uses a
purpose-built single-threaded context, not the real WinForms, WPF or ASP.NET
one.) Legacy code that must block will work; async all the way is still the
recommendation.

**Disable the Pay control while a sale is in flight.** The terminal serves one
transaction at a time (a second request gets `96`), and an impatient double click
is the commonest way a till starts two sales for one basket.

### What throws

Results carry every runtime outcome, so what escapes as an exception is a
programming error or your own cancellation:

| Exception | When |
|---|---|
| `ArgumentException` | A blank receipt number, an invalid `merchantReference`, or `RunAsync` given a type it does not run or a missing amount / receipt number. Raised on `await`, before a connection is opened |
| `OperationCanceledException` | The `CancellationToken` you passed was cancelled |
| `ArgumentNullException` / `ArgumentException` (from `Plan`, `Open`, `LanTerminal`, …) | A null link, config or plan; `Open` given a plan that is not ready; a `XxxTerminal` builder given a plan for another link |
| `InvalidOperationException` | `Open` on a USB plan with no `usbChannel` |

### Cancellation stops the wait, not the transaction

**Cancelling a `CancellationToken` stops the SDK waiting; it does not stop the
transaction.** The call throws `OperationCanceledException` — it does **not**
return a result — and the terminal carries on with the cardholder. Treat a
cancelled call exactly like `Failure.Timeout`: an unknown outcome. No
auto-inquiry runs on cancellation, so *you* resolve it, by the reference you
passed (§10):

```csharp
try
{
    var result = await terminal.SaleAsync(order.Total, merchantReference: order.Number, cancellationToken: cts.Token);
}
catch (OperationCanceledException)
{
    // The SDK stopped waiting; the terminal did not stop. Outcome UNKNOWN.
    // Resolve it with InquireByReferenceAsync(order.Number) before anything else.
}
```

So a "Cancel" button that cancels the token is honest only if it says "stop
waiting". If the operator really wants to abort the payment, that happens at the
terminal.

Keep the in-flight call somewhere that outlives the screen — a view model or a
service, not a `Window` or a `Form` — or closing the window mid-payment loses the
result. Hold `EcrTerminal` as a singleton per physical terminal.

### Concurrency

An `EcrTerminal` holds no connection and no mutable state, so it is safe to share
across threads. Each call opens its own connection. The *terminal* serves one
money-moving transaction at a time, so two concurrent sales will collide with `96`
on the second one. Inquiries and receipts are answered while busy.

### Console apps and services

Nothing special: `await` from `Main` (`static async Task Main`) or from a
`BackgroundService`. There is no synchronisation context to worry about. A
service stop is a cancellation — see [§15](#15-going-to-production).

### Running in a web app

ASP.NET Core (.NET 8+) and classic ASP.NET (.NET Framework 4.6.2+, 4.7.2+
recommended) use the same package: ASP.NET Core takes the dependency-free `net8.0`
asset, classic ASP.NET the `netstandard2.0` one (with the binding-redirect note in
[§2](#2-add-the-sdk)). Microsoft's end dates apply to the host you choose: .NET 8
and 9 on Nov 10, 2026, .NET Framework 4.6.2 on Jan 12, 2027, ASP.NET Core 2.3 on
.NET Framework on Apr 13, 2027; see the
[Support calendar](compatibility.md#support-calendar).

**Lifetime.** Neither client keeps state between calls, so a singleton per physical
terminal is right, and building one per request is also safe. LAN (`EcrTerminal`)
opens one TCP connection per call and keeps none; Web Service
(`EcrWebServiceTerminal`) posts through **one process-wide static `HttpClient`**, so
a new instance per request does not exhaust sockets. `EcrOpenedSession` wraps either
and behaves the same. This is from reading the source; it has not been load-tested.

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

// The endpoint.
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

For Web Service ECR, follow the [initialization process](#initialization-process)
(§4): plan with `EcrLink.WebService(...)` and the **Web Service key**, gate on
`plan.IsReady`, then register `EcrSessions.WebServiceTerminal(serial, plan)` (or
`EcrSessions.Open`).

**What a web app must still respect**

- **Keys come from a secret store** (user secrets, a vault, environment variables),
  never from source or a committed `appsettings.json`.
- **Do not pass `HttpContext.RequestAborted` to a money-moving call.** Cancelling
  stops the SDK waiting, not the payment. A closed tab must not turn a sale still
  running at the terminal into an unknown outcome nobody is watching. Pass your own
  `merchantReference` so you can inquire later, and write it down before the call.
- **The terminal serves one money-moving transaction at a time.** Two concurrent
  sales to the same terminal collide: the second gets `96`. Serialise sales per
  terminal in your app.
- **LAN ECR needs a network route from the server to the terminal** on TCP 9100. A
  server in a data centre cannot reach a terminal on a shop's Wi-Fi; use Web
  Service ECR there.
- **A lost answer is not a decline** — the same rule as everywhere (§10).

---

## 13. Logging

Silent by default — the SDK never writes to a log you did not choose. It has no
logging dependency: `IEcrLogger` is one method, `Debug(string)`, and
`EcrLogger.From(Action<string>)` wraps a delegate.

```csharp
// Debug output (Visual Studio Output window, DebugView)
var logger = EcrLogger.From(message => Debug.WriteLine(message, "Ecr"));

// Microsoft.Extensions.Logging
var logger = EcrLogger.From(message => ilogger.LogDebug("{EcrMessage}", message));

// Serilog
var logger = EcrLogger.From(message => Log.Debug("{EcrMessage}", message));

// Or implement the interface, e.g. to give it a category of its own
public sealed class EcrMelLogger : IEcrLogger
{
    private readonly ILogger _logger;
    public EcrMelLogger(ILogger<EcrTerminal> logger) => _logger = logger;
    public void Debug(string message) => _logger.LogDebug("{EcrMessage}", message);
}
```

Use separate loggers (or a prefix) for LAN and Web Service so mixed logs stay
readable:

```csharp
EcrSessions.LanTerminal("P653200085189", lanPlan,
    logger: EcrLogger.From(m => Debug.WriteLine(m, "Ecr")));
EcrSessions.WebServiceTerminal("P653200008251-Web", wsPlan,
    logger: EcrLogger.From(m => Debug.WriteLine(m, "EcrWebService")));
```

When signing is enabled, diagnostic output includes:

- The formula: `HMAC-SHA256(sorted key=value payload, Hex.parse(secret)) → uppercase hex`
- Whether a key is configured (`configured` / `not configured`) — no part of the secret itself: not its length, not any of its characters
- The payload string being signed (excluding the hash field itself)
- The computed hash value

Request and response payloads may contain masked PANs and transaction references.
They are safe from a PCI perspective but are still transaction records — store
them accordingly and never forward them to analytics or crash reporters unchanged.
In particular, log at `Debug` level and keep it out of production sinks unless you
mean it.

`EcrConfig.ToString()` redacts `SecureHashKey` as `<redacted>` if you log config
objects during debugging.

---

## 14. Testing without hardware

A stand-in listener that speaks the protocol ships in this repository
(`tools/fake_pos_server.py`, Python 3). Get it from a checkout, or:

```powershell
curl.exe -O https://raw.githubusercontent.com/amwal-pay/AmwalpayEcrWindows/main/tools/fake_pos_server.py

py fake_pos_server.py --port 9100                      # approves everything
py fake_pos_server.py --port 9100 --decline 51          # declines everything
py fake_pos_server.py --port 9100 --delay 8             # slow cardholder
py fake_pos_server.py --port 9100 --not-found           # inquiries answer 25, receipts refuse
```

(`curl.exe`, not `curl` — in Windows PowerShell 5, `curl` is an alias for
`Invoke-WebRequest`. `py` is the Python launcher; `python` works too if it is on
your `PATH`.)

Register a terminal in your till pointing at your machine's LAN IP (or
`127.0.0.1` when the fake server and the till are the same machine) and that port.
If the fake server runs on a **different** Windows PC, that PC's firewall needs an
inbound rule for TCP 9100 on the network profile in use — see
[troubleshooting](troubleshooting.md#windows-network-and-host).

`--delay` is the case worth testing and the one most often skipped: what your UI
does while it waits, and what happens when the operator walks away mid-sale.

### The signing caveat

**The fake server does not sign its answers and does not check your signatures.**
The SDK, when it has a `SecureHashKey`, requires every answer to be signed and to
echo the request's nonce — so against the fake server *every call comes back
`Failure.Unauthenticated`*. That is the SDK working correctly, and a free way to
see that failure path. To test the happy path, run **without** a key by building the
terminal directly:

```csharp
// Test only — no SecureHashKey, so nothing is signed and nothing is verified.
var terminal = new EcrTerminal(
    host: "127.0.0.1",
    serialNumber: "FAKE-1",
    config: new EcrConfig { ResponseTimeout = TimeSpan.FromSeconds(30) });
```

`EcrSessions.Plan` reports a missing key as an issue, so a plan for the fake server
is never ready; construct `EcrTerminal` yourself, in test code only. **Never ship
a till with an empty key** — it would accept unsigned answers from anything on
the network.

Also test, before you ship:

- **Terminal switched off** (stop the fake server) → `Failure.Unreachable`
- **Slow terminal** — `--delay 8` with `ResponseTimeout = TimeSpan.FromSeconds(3)` →
  `Failure.Timeout`. The auto-inquiry is delayed too, so `Recovered` will be an
  `EcrInquiry.Failed`. Confirm you do **not** book it as a decline
- **Link dropped mid-sale** — start a sale with `--delay 30`, then kill the fake
  server process (or turn the machine's Wi-Fi off) → `Failure.ConnectionLost`, and
  again confirm you do **not** book it as a decline
- **A wrong key** — configure any valid hex key against the fake server →
  `Failure.Unauthenticated`
- **Cancelling** — pass a `CancellationToken`, cancel during `--delay`, and confirm
  the till treats `OperationCanceledException` as an unknown outcome and resolves it
  by reference
- **A second request while one is running** → `96`, terminal busy *(real terminal only)*
- **A void of an unknown receipt number** → `25` *(real terminal; the fake server
  answers a void like a sale)*

---

## 15. Going to production

A checklist worth walking before the first live terminal:

- [ ] `MinorUnitDigits` matches the currency — check an amount on the wire
- [ ] Amounts are `decimal` end to end, parsed and formatted with `CultureInfo.InvariantCulture`
- [ ] `Approved` (the type) decides the outcome, not `ResponseCode`
- [ ] `PartialApproval` is handled, and goods are held back for the balance
- [ ] `Timeout` / `ConnectionLost` / `Unauthenticated` are never treated as declines
- [ ] On Web Service, a `Declined` with `NextStep == InquireByMerchantReference` (usually `NO_RESPONSE`) is treated as an unknown outcome, never as a refusal
- [ ] On LAN, check `EcrResult.Failed.Recovered` / `Settled` before retrying a sale
- [ ] LAN and Web Service keys are stored separately and mapped to the correct mode
- [ ] `SecureHashKey` comes from protected configuration rather than source code
- [ ] Keys are not written to any log, crash report or analytics event
- [ ] `EcrSessions.Plan()` is used (or equivalent validation) before sending
- [ ] `AuthCode` is not printed on a void
- [ ] Terminals are stored by serial number, address looked up at send time
- [ ] `result.Raw` is logged with each transaction for dispute resolution
- [ ] `result.MerchantReference` is stored with the sale, generated or your own — and written **before** the call when a token can cancel it
- [ ] The Pay control is disabled while a sale is in flight; the in-flight call lives in a view model or service, not a window
- [ ] No `.Result` / `.Wait()` on an ECR call
- [ ] TMS has the correct `terminalMode` and `ecrMode` on every terminal
- [ ] The till's clock and time zone are right (the request carries the till's local time)
- [ ] Someone other than the developer has run the failure cases in §14

### Storing keys with DPAPI

The two signing keys are payment credentials. Keep them out of source control and
plain-text config, and encrypt them at rest with the Windows Data Protection API.
`ProtectedData` is built into .NET Framework (reference `System.Security`); on
.NET 5+ add the `System.Security.Cryptography.ProtectedData` package. It is
Windows-only.

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

- **`CurrentUser`** ties the ciphertext to the Windows account, so it cannot be
  decrypted by another user or after the file is copied to another machine. Right
  for a till that runs as one cashier account.
- **`LocalMachine`** lets any process on that machine decrypt it. Use it only when
  the decrypting account is not fixed — and remember it protects against theft of
  the file, not against code running on the till.
- A Windows Service running as a different account (or `LocalSystem`) cannot read
  a `CurrentUser` blob written by an interactive account. Provision the key with
  the account the service runs as.
- Store the LAN key and the Web Service key as separate entries, and validate with
  `EcrConfig.IsValidSecureHashKey()` in your settings screen before saving.

### Running from a Windows Service

The SDK has no UI and no synchronisation context, so it works the same in a service
as in a console. Register the terminal once, hand it a logger, and take care of
lifecycle:

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

(`AddWindowsService` comes from `Microsoft.Extensions.Hosting.WindowsServices`.)
Points that bite in a service:

- **A stop request is a cancellation.** If `stoppingToken` fires mid-sale and you
  passed it to `SaleAsync`, you get `OperationCanceledException` and the terminal
  carries on. Do not pass the stopping token to a money-moving call unless you have
  the write-ahead journal from [§10](#make-the-unknown-recoverable) — and on start-up
  resolve whatever it still lists as open.
- **Run as an account you have planned DPAPI for** (above), not whatever the
  installer defaulted to.
- **The network profile matters even for services.** A service on a machine whose
  network is classed Public gets the Public firewall profile's rules — check
  `Get-NetConnectionProfile`.
- **Services start before the network is up.** A first `IsReachableAsync()` after a
  reboot can fail; retry the probe instead of concluding the address is wrong.

### Web Service on classic .NET Framework

Web Service ECR needs TLS 1.2. Hosts that target .NET Framework earlier than 4.7
(including a `netstandard2.0` consumer on such a host, and the declared 4.6.2 floor)
should enable it once, at start-up, before the first Web Service call. Apps that
target 4.7 or later use the operating system's defaults and need nothing. This is
documented .NET Framework behaviour, not verified in this repository's test runs.
Also note that Microsoft ends support for 4.6.2 on Jan 12, 2027, after which the
declared floor rises to 4.7.2 unless the owners decide otherwise (see the
[Support calendar](compatibility.md#support-calendar)):

```csharp
ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
```

---

**Next:** the [API reference](api-reference.md) for every type and method, the
[protocol](protocol.md) if you need the wire format, and
[troubleshooting](troubleshooting.md) when something does not behave.
