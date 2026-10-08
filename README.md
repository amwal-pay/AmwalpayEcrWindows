# Amwal ECR SDK for .NET

> **Version 1.0.0 — first public package.** This is the initial NuGet release of
> `AmwalPay.Ecr`. Pin an exact version (`Version="[1.0.0]"`), treat wire and API
> contracts as stable for this major, and read the release notes before any later
> upgrade. Feedback on first integrations is welcome via your Amwal contact.

Drive an **Amwal POS terminal** from your own Windows or .NET application. Your
application asks for a payment; the terminal reads the card, talks to the payment
backend, and answers. **No card data passes through your application** — you
receive a masked PAN at most, so integrating does not pull your app into PCI scope
the way handling card numbers would.

**You need an Amwal POS terminal** that Amwal's TMS has put into ECR mode (Wi-Fi
or Web Service). The package does nothing without one; the
[fake terminal](#testing-without-hardware) lets you develop without hardware.

**Supported platforms:** .NET Framework 4.6.2 or later (4.7.2 or later recommended),
and .NET 8 or later (WinForms, WPF, console, Windows Service, ASP.NET Core, classic
ASP.NET). C# 9 or later. Microsoft ends support for .NET 8 and 9 on Nov 10, 2026 and
for .NET Framework 4.6.2 on Jan 12, 2027; see [Supported platforms](#supported-platforms).

```csharp
using AmwalPay.Ecr;

var terminal = new EcrTerminal("192.168.1.50", serialNumber: "P653200085189",
    config: new EcrConfig { SecureHashKey = lanKey });

switch (await terminal.SaleAsync(1.234m, merchantReference: "ORDER-1001"))
{
    case EcrResult.Approved a: receipt.Print(a.Rrn, a.AuthCode); break;
    case EcrResult.Declined d: screen.Show(d.Reason); break;
    case EcrResult.Failed f:   screen.Show(f.Failure.Message); break;   // unknown outcome, NOT a decline
}
```

---

## Supported platforms

**Supported minimum: .NET Framework 4.6.2 and .NET 8.** One package serves both
native and web apps. It carries a `netstandard2.0` asset, which every .NET Framework
app gets (there is **no `net462` asset**), and a zero-dependency `net8.0` asset.

| Platform | Asset used | Status | Verified how |
|---|---|---|---|
| .NET 10 | `net8.0` | **Supported** until Microsoft ends it, Nov 14, 2028 | Compiled, and run: a real sale against a signed fake terminal returned `Approved` |
| .NET 8, .NET 9 | `net8.0` | **Supported for now.** Microsoft ends both on **Nov 10, 2026**; the supported minimum then becomes .NET 10 | .NET 8 compiled and run as above; .NET 9 uses the same asset, not built separately |
| .NET Framework 4.7.2, 4.8, 4.8.1 | `netstandard2.0` | **Supported. Recommended** | Compiled. Not run on .NET Framework; the full test suite passes on this same assembly, run on the .NET 8 runtime |
| .NET Framework 4.6.2, 4.7, 4.7.1 | `netstandard2.0` | **Supported: the declared floor.** Microsoft ends 4.6.2 on **Jan 12, 2027**; the floor then rises to 4.7.2 unless the owners decide otherwise | 4.6.2 compiled (a consumer, and the SDK's own source against the 4.6.2 reference assemblies, 0 errors), not run; 4.7 and 4.7.1 not built |
| .NET 6, .NET 7 | `netstandard2.0` | Works, not supported | Out of Microsoft support. .NET 6 compiled, not run; .NET 7 not built |
| .NET Framework 4.6.1 and lower | none | **Unsupported** | Out of support since Apr 26, 2022, and `System.Text.Json` 8 does not support net461: 4.6.1 builds with warnings to that effect |
| .NET Core 2.0 to 3.1, .NET 5, Mono, Xamarin, UWP, Unity, Native AOT, trimming | n/a | **Unsupported** | Not tested |

- **No .NET Framework build has been executed** by us: support rests on compiling,
  plus the test suite passing on the same assembly under .NET 8. Windows CI legs for
  `net462` and `net472` are defined but **not yet run**, and .NET Framework 4.x updates
  in place, so a runner with 4.8 cannot prove 4.6.2's own runtime. Smoke-test on a
  machine at the version you target.
- OS-neutral managed code, no Windows-only requirement. No USB cable channel is shipped.

The evidence, dated support calendar, dependency lists and .NET Framework specifics
are in [Compatibility](https://github.com/amwal-pay/AmwalpayEcrWindows/blob/main/docs/compatibility.md).

---

## Install

Published to **nuget.org** as `AmwalPay.Ecr`. No private feed, no credentials.

```powershell
dotnet add package AmwalPay.Ecr
```

or in your project file (use the version shown at the top of this page):

```xml
<ItemGroup>
  <PackageReference Include="AmwalPay.Ecr" Version="[1.0.0]" />
</ItemGroup>
```

**Use C# 9 or later.** `EcrConfig` is configured with `init` properties and
results are records. SDK-style projects on .NET 5+ already compile with C# 9. An
**SDK-style .NET Framework project defaults to C# 7.3** and fails with `CS8370`
(C# 8 gives `CS8400`) until you set:

```xml
<PropertyGroup>
  <LangVersion>9.0</LangVersion>   <!-- or: latest -->
</PropertyGroup>
```

That needs Visual Studio 2019 16.8+ or the .NET SDK 5+. Pattern matching on
results needs only C# 7; object-initialising `EcrConfig` and `with` need 9.

**.NET Framework specifics.** On .NET Framework (4.6.2 included) the package pulls in
`System.Text.Json` 8.0.6 and its `System.*` dependencies, so the app needs binding
redirects (SDK-style projects generate them; `packages.config` projects may need
them by hand). Web Service ECR (HTTPS) on an app targeting a version **before 4.7**
needs TLS 1.2 switched on. Both are documented .NET Framework behaviour, not
something our test runs exercised; see [Compatibility](https://github.com/amwal-pay/AmwalpayEcrWindows/blob/main/docs/compatibility.md#net-framework).

### Upgrading

**Pin a version; never a range.** Published versions are immutable — a fix means a
new version, and you upgrade by changing the number. That is deliberate: a payment
client should not change behaviour because someone else published something. A bare
`Version="1.0.0"` means "1.0.0 or higher" to NuGet; write `Version="[1.0.0]"` to
require exactly it, and consider `RestorePackagesWithLockFile`.

Versioning is `MAJOR.MINOR.PATCH` over two public surfaces: the .NET API and the
JSON on the wire (the wire binds harder; a terminal updates on its own schedule).
Patch: fixes and internals, safe to take. Minor: new methods or optional fields,
nothing you call changes. Major: removed or renamed API, or a changed wire-field
meaning; read the release notes, the terminal may need updating too.

---

## What's in 1.0.0

| Area | Included |
|---|---|
| Transports | LAN TCP (`ecrMode: 2`); Web Service HTTPS (`ecrMode: 4`); USB cable only if you supply `IEcrChannel` |
| Operations | Sale, void, refund, inquiry (STAN or merchant reference), receipt URL (LAN / USB only) |
| Web Service inquiry | POST `/Ecr/Inquiry` with `requestDateTime`, by-STAN or by-`originalMerchantReference` |
| Outcomes | `Approved` / `Declined` / `Failed`; inquiry `Found` / `NotFound` / `Failed` |
| Recovery | LAN auto-inquire on lost answer; Web Service: branch on `NextStep.InquireByMerchantReference`, then inquire yourself |
| Platforms | `netstandard2.0` + `net8.0` — see [Compatibility](docs/compatibility.md) |

**Not in 1.0.0:** a built-in USB driver, Web Service e-receipt, auto-inquire on Web
Service, Bluetooth ECR, Native AOT / trimming support.

## Documentation

| | |
|---|---|
| **[Integration guide](https://github.com/amwal-pay/AmwalpayEcrWindows/blob/main/docs/integration-guide.md)** | From an empty project to a working sale, then everything needed to ship. **Start here.** Includes [full Wi‑Fi and Web Service examples](https://github.com/amwal-pay/AmwalpayEcrWindows/blob/main/docs/integration-guide.md#5-full-examples--wifi-and-web-service) (setup + each operation) and [Web Service initialization](https://github.com/amwal-pay/AmwalpayEcrWindows/blob/main/docs/integration-guide.md#initialization-process). |
| **[Compatibility](https://github.com/amwal-pay/AmwalpayEcrWindows/blob/main/docs/compatibility.md)** | Supported platforms, what was and was not verified, C# version, .NET Framework and web hosting |
| **[API reference](https://github.com/amwal-pay/AmwalpayEcrWindows/blob/main/docs/api-reference.md)** | Every public type and method, with parameters, returns and examples |
| **[Wire protocol](https://github.com/amwal-pay/AmwalpayEcrWindows/blob/main/docs/protocol.md)** | The bytes on the socket. For other platforms, debugging, or terminal-side work |
| **[Troubleshooting](https://github.com/amwal-pay/AmwalpayEcrWindows/blob/main/docs/troubleshooting.md)** | Symptom, cause, fix |

Working samples, both on .NET 8 (support ends Nov 10, 2026; they should move to .NET 10):
[Windows-Native-Example](https://github.com/amwal-pay/AmwalpayEcrWindowsExample/tree/main/Windows-Native-Example) (WPF till)
and [AmwalECR-Web-Example](https://github.com/amwal-pay/AmwalpayEcrWindowsExample/tree/main/AmwalECR-Web-Example) (ASP.NET Core).

**In-repo simulator** (mirrors the Android ECR-simulator): see
[samples/README.md](samples/README.md). WinForms on Windows Desktop SDK; interactive
console elsewhere. `dotnet run --project samples/AmwalPay.Ecr.Simulator`.

---

## Overview

An **ECR** — Electronic Cash Register — is the till the cashier works at. When a
terminal is ECR-attached, the till owns the amount and the terminal owns the card:
the cashier rings up the basket, the till sends the total, and the customer pays at
the terminal. Nobody keys the amount twice, so the till's records and the payment
records always agree.

```text
  +--------------+   TCP :9100, JSON       +--------------+   HTTPS   +---------+
  |  Your till   | ----- or USB cable ---> | Amwal POS    | --------> | Payment |
  |  (this SDK)  | <---------------------- | terminal     | <-------- | backend |
  +--------------+   LAN (2) / cable (1)   +--------------+           +---------+
        |                                         ^
        |  HTTPS REST, JSON                       |  card, PIN
        +-----------------------------------------+
                   Web Service ECR (ecrMode: 4)       cardholder
```

The SDK is the left-hand box: framing, transport, timeouts, signing, and turning
the terminal's answer into types you can act on. It has no UI and no opinion about
how your till looks.

**Three transports, one API shape.** **LAN ECR** (`ecrMode: 2`) is TCP to the
terminal's IP on port 9100; your till can be on Wi-Fi or Ethernet as long as it can
route to the terminal. **USB cable ECR** (`ecrMode: 1`) carries the identical
messages down a cable, but the package does **not** ship a USB channel — Android's
USB accessory mode has no Windows equivalent — so you supply an `IEcrChannel`
(`EcrLink.UsbCable`, `EcrFrames`); everything above the channel is unchanged.
**Web Service ECR** (`ecrMode: 4`) uses HTTPS to the Amwal Hub, signed with a
separate key. `EcrSessions` validates credentials and builds the right client.

---

## Features (1.0.0)

**Five operations.** **Sale** and **Refund** present a card. **Void** cancels an
earlier transaction in full (the terminal holds the original). **Inquiry** asks
what became of one (by receipt number / STAN, or by merchant reference), and
**Receipt** fetches its e-receipt as a URL for a QR code; both only read. Receipt
is LAN / USB only. Web Service offers sale, void, refund, and inquiry (STAN or
by reference) over HTTPS to the Hub (`/Ecr/Inquiry`). See the
[support matrix](https://github.com/amwal-pay/AmwalpayEcrWindows/blob/main/docs/integration-guide.md#lan-vs-web-service--what-the-sdk-supports-today).

**Outcomes you cannot misread.** `Approved`, `Declined` and `Failed` are separate
types, so "the terminal refused" and "we never found out" cannot be confused — the
mistake that charges a customer twice.

**Partial approval handled.** When the bank authorises less than was asked for, you
get the approval *and* both figures, so goods are not released for money that never
arrived.

**Inquiry while busy.** The terminal answers a lookup even mid-payment, which is
what makes an unknown outcome recoverable rather than a guess. On LAN the SDK
follows a lost answer with an automatic inquiry by merchant reference. On Web
Service you resolve manually with `InquireByReferenceAsync` (or `InquireAsync`
when you already have the STAN).

**Probe and plan.** `IsReachableAsync()` and `ProbeReachabilityAsync()` turn a wrong
address into an immediate answer. `EcrSessions.Plan` validates addressing, IDs and
keys before you connect and returns readable issues rather than throwing.

**Dual signing keys.** LAN and USB cable ECR share one secret; Web Service uses
**a different one**. The SDK picks the algorithm for the link; your app supplies
the right key per mode.

**Currency-aware amounts.** Amounts are `decimal`, sent as minor units and returned
as major units: `1.234m` in, `1.234` out. A `double` will not even compile.

**Async all the way, silent by default.** Every call returns a `Task` and is safe to
`await` from a UI thread; `CancellationToken` is honoured (it stops the wait, not
the transaction). Nothing is logged unless you pass an `IEcrLogger`, and secrets
are never logged in full.

---

## Quickstart

**1. The terminal must be in ECR mode.** Amwal's TMS sets `terminalMode: 1` and
`ecrMode` `1` (USB), `2` (Wi-Fi) or `4` (Web Service); Wi-Fi terminals show their
address under the card logos on the idle screen.

### Setup structure (same as the Android ECR-simulator)

Amwal issues **two different secrets**. Store them in **app settings**, not on each
terminal record — then pick one when you plan:

| Settings (global) | Used when |
|---|---|
| Wi‑Fi/LAN secure hash key | `ecrMode` 1 / 2 / 3 (USB, Wi‑Fi, Bluetooth) |
| Web Service secure hash key + SIT/UAT/PROD | `ecrMode` 4 |

| Terminal registry (per device) | No signing key |
|---|---|
| Wi‑Fi | name, serial, IP, port |
| Web Service | name, serial, merchant ID, terminal ID |

Operations: select a terminal → `Plan` with `secureHashKeyFor(mode)` → `Open` /
`SaleAsync` / … The active-config card shows **one** key label (the mode in use).

`Plan`'s optional `rawSecureHashKey` is the same typed string when you want a
malformed key as a plan issue instead of an exception — it is **not** a second secret.
From a validated store, pass the key only on `EcrConfig`.

### Wi‑Fi / LAN initialization

```csharp
using AmwalPay.Ecr;

var plan = EcrSessions.Plan(
    link: new EcrLink.Lan(Host: "192.168.1.50"),
    config: new EcrConfig { SecureHashKey = secrets.LanKeyFor(serialNumber) });

if (!plan.IsReady)
{
    screen.Show(string.Join("\n", plan.Issues));
    return;
}

EcrTerminal terminal = EcrSessions.LanTerminal(
    terminalSerial: "P653200085189",
    plan: plan);
```

For USB cable ECR, plan with `new EcrLink.UsbCable()` and build the terminal with
`EcrSessions.UsbCableTerminal(...)`, passing an `IEcrChannel` **that you provide**.

### Web Service initialization (`ecrMode: 4`)

Web Service does **not** use the terminal's LAN IP. The till calls the Amwal Hub
over HTTPS; the Hub reaches the POS. You need from Amwal (or TMS):

| You supply | Notes |
|---|---|
| Terminal serial | Same serial TMS registered (e.g. `P653200008251`) |
| Merchant ID + terminal ID | Numeric strings; go on `EcrLink.WebService` |
| Environment | `SIT`, `UAT`, or `PROD` — SDK picks the Hub base URL |
| **Web Service** secure hash key | Hex, even length, ≥ 16 chars — **not** the Wi‑Fi/LAN key |

```csharp
using AmwalPay.Ecr;

// 1) Plan — validates IDs and the Web Service key; does not call the Hub yet.
var plan = EcrSessions.Plan(
    link: new EcrLink.WebService(MerchantId: "13593", TerminalId: "101311"),
    config: new EcrConfig
    {
        SecureHashKey = secrets.WebServiceKey,   // Web Service key only
        Environment = EcrEnvironment.UAT,        // SIT / UAT / PROD
    });

if (!plan.IsReady)
{
    // Typical issues: "Merchant ID is required", "must be numeric",
    // "Web Service secure hash key is not configured"
    screen.Show(string.Join("\n", plan.Issues));
    return;
}

// 2a) Typed client — or 2b) EcrSessions.Open for a mode-agnostic session.
EcrWebServiceTerminal terminal = EcrSessions.WebServiceTerminal(
    terminalSerial: "P653200008251",
    plan: plan,
    logger: EcrLogger.From(msg => Debug.WriteLine(msg)));

// 2b) Same plan, one API shape for LAN / USB / Web Service:
// EcrOpenedSession session = EcrSessions.Open("P653200008251", plan, logger: logger);
```

Hub hostnames are resolved inside the SDK from `Environment` (SIT / UAT / PROD) —
not configured or published for the app. Paths are fixed (`/Ecr/Sale`, `/Ecr/Void`,
`/Ecr/Refund`, `/Ecr/Inquiry`). Requests carry Hub-required `requestDateTime` and
are signed with `secureHashValue`.

On .NET Framework **before 4.7**, enable TLS 1.2 once at start-up before the first
call: `ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;`.

Full checklist, recovery after a lost answer, and the support matrix:
[Web Service ECR](https://github.com/amwal-pay/AmwalpayEcrWindows/blob/main/docs/integration-guide.md#4-web-service-ecr).

### Take a payment

```csharp
async Task TakePaymentAsync(Order order)
{
    // LAN only — on Web Service ProbeReachabilityAsync / IsReachableAsync are unavailable
    // (EcrOpenedSession.ProbeReachabilityAsync returns null). Skip or gate on UsesLan.
    if (terminal is EcrTerminal lan && !await lan.IsReachableAsync())
    {
        screen.Show("Terminal not reachable");
        return;
    }

    // merchantReference is optional; without it the SDK generates one.
    // Store it before the call so you can InquireByReferenceAsync after a lost answer.
    switch (await terminal.SaleAsync(order.Total, merchantReference: order.Number))
    {
        case EcrResult.Approved a when a.PartialApproval:
            screen.Show($"Only {a.Amount} of {a.RequestedAmount} approved");
            break;

        case EcrResult.Approved a:
            CompleteSale(a.Amount, a.Rrn, a.AuthCode);
            break;

        case EcrResult.Declined d when d.NextStep == NextStep.InquireByMerchantReference:
            HandleUnknownOutcome(d);   // Web Service transport / unknown outcome — inquire, do not retry
            break;

        case EcrResult.Declined d:
            screen.Show(d.Reason);
            break;

        case EcrResult.Failed f when f.Settled:
            Reconcile(((EcrInquiry.Found)f.Recovered!).Transaction);
            break;

        case EcrResult.Failed f:
            HandleUnknownOutcome(f.Failure);   // not a decline
            break;
    }
}
```

Amounts are `decimal`: write `1.234m`; `SaleAsync(1.234)` does not compile, which is
the point. Clients hold no connection between calls; keep one per terminal. The
[integration guide](https://github.com/amwal-pay/AmwalpayEcrWindows/blob/main/docs/integration-guide.md)
takes it from here.

---

## The one rule

A response that never arrives is **not** a decline.

```csharp
case EcrResult.Failed f:
    switch (f.Failure)
    {
        case Failure.Unreachable:     /* nothing was sent; safe */                  break;
        case Failure.Timeout:         /* UNKNOWN — check f.Recovered (LAN) */       break;
        case Failure.ConnectionLost:  /* UNKNOWN */                                 break;
        case Failure.Malformed:       /* UNKNOWN */                                 break;
        case Failure.Unauthenticated: /* UNKNOWN on LAN; key mismatch if every call */ break;
    }
    if (f.Settled)
    {
        // LAN: AutoInquireOnFailure found the transaction — read f.Recovered
    }
    break;
```

The card flow does not depend on the socket staying up, so the terminal may have
taken the money and been unable to say so. Retrying that is how a customer gets
charged twice. Resolve it with `InquireAsync` / `InquireByReferenceAsync` — which
the terminal answers even while busy — or reconcile against the terminal.

On **Web Service** a network error on a money-moving call arrives as `Declined`
(usually `ResponseCode == "NO_RESPONSE"`) with
**`NextStep == NextStep.InquireByMerchantReference`**. That `NextStep` is the
reliable signal: an unknown outcome, not a refusal — call
`InquireByReferenceAsync` with the sale's `merchantReference` (or
`InquireAsync` if you have the STAN) before retrying. See
[the integration guide](https://github.com/amwal-pay/AmwalpayEcrWindows/blob/main/docs/integration-guide.md#10-when-the-answer-never-comes).

---

## Windows

**UI threads.** Every call is `async` and safe to `await` from a WPF or WinForms UI
thread. The SDK uses `ConfigureAwait(false)` internally, so it never captures your
synchronisation context; your own `await` resumes on the UI thread as usual.
Blocking on it with `.Result` or `.Wait()` **does not deadlock** — a regression test
blocks from a simulated single-threaded UI context on the LAN, recovery, timeout
and Web Service paths — but a sale takes as long as the cardholder takes, so your
window freezes for all of it. Await instead.

**Windows Firewall and network profile.** The till is the connecting side, so the
default rules let LAN ECR out on TCP 9100. Trouble comes from a Public network
profile, outbound-deny policy or antivirus, or a VPN taking the route to the
terminal's subnet. A failed `ping` proves nothing; test the real thing:

```powershell
Test-NetConnection 192.168.1.50 -Port 9100
Get-NetConnectionProfile          # Public? Set-NetConnectionProfile -NetworkCategory Private
```

**Storing keys.** The signing keys are secrets: not in source, not in
`appsettings.json`. Encrypt them at rest with DPAPI
(`ProtectedData`, `DataProtectionScope.CurrentUser`), and keep the LAN key and the
Web Service key as separate entries. See
[Storing keys with DPAPI](https://github.com/amwal-pay/AmwalpayEcrWindows/blob/main/docs/integration-guide.md#storing-keys-with-dpapi)
and [troubleshooting](https://github.com/amwal-pay/AmwalpayEcrWindows/blob/main/docs/troubleshooting.md#windows-network-and-host).

**Running from a Windows Service.** Fine; the SDK has no UI and needs no
synchronisation context. A stop request is a cancellation: it stops your wait,
**not** the payment, so write the sale's `merchantReference` down before you call
`SaleAsync` and inquire on the next start. See the
[integration guide](https://github.com/amwal-pay/AmwalpayEcrWindows/blob/main/docs/integration-guide.md#15-going-to-production).

**Old .NET Framework and TLS.** LAN ECR is plain TCP, no TLS. Web Service ECR is
HTTPS to the Hub and needs TLS 1.2; an app that targets .NET Framework earlier than 4.7
(the 4.6.2 floor included) must enable it before the first call:
`ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;`. Documented
.NET Framework behaviour, not verified in this repository's test runs.

---

## Web apps

ASP.NET Core (.NET 8+) and classic ASP.NET (.NET Framework 4.6.2+) both work; ASP.NET Core
2.3 on .NET Framework (Microsoft ends it Apr 13, 2027) follows the .NET Framework floor.

- **Register one `EcrTerminal` per physical terminal as a singleton**, or build one
  per request; both are safe. LAN ECR opens one TCP connection per call and keeps
  none; Web Service ECR shares one process-wide `HttpClient`.
- **LAN ECR needs a route from the server to the terminal** on TCP 9100. A
  cloud-hosted app cannot reach a terminal on a shop's Wi-Fi; use Web Service ECR.
- **Never pass `HttpContext.RequestAborted` to a sale.** Cancelling stops the wait,
  not the payment; a closed browser tab must not create an unknown outcome.
- **The terminal serves one sale at a time**; a concurrent second sale gets `96`.

A complete registration and endpoint:
[integration guide](https://github.com/amwal-pay/AmwalpayEcrWindows/blob/main/docs/integration-guide.md#running-in-a-web-app).

---

## Testing without hardware

A stand-in listener that speaks the protocol lives in the repository as
[`tools/fake_pos_server.py`](https://github.com/amwal-pay/AmwalpayEcrWindows/blob/main/tools/fake_pos_server.py)
(Python 3). Address a terminal at your machine's LAN IP (or `127.0.0.1`) and that
port:

```powershell
py tools\fake_pos_server.py --port 9100                  # approves everything
py tools\fake_pos_server.py --port 9100 --decline 51      # declines everything
py tools\fake_pos_server.py --port 9100 --delay 8         # slow cardholder
py tools\fake_pos_server.py --port 9100 --not-found       # inquiries answer 25
```

**It does not sign its answers.** With a `SecureHashKey` configured the SDK rejects
them as `Failure.Unauthenticated`, as it should. To use the fake server, build the
terminal with **no key**, in test code only (`EcrSessions.Plan` reports a missing
key as an issue, so it cannot be used for this). **Never ship a till without a key.**

```csharp
var terminal = new EcrTerminal("127.0.0.1", serialNumber: "FAKE-1");
```

`--delay` mimics a cardholder, which is the case worth testing: what your UI does
while it waits, and what happens if the user walks away.

---

## Support

When raising an issue, include `result.Raw` (the terminal's exact answer), the SDK
log for the exchange, and the terminal's `terminalMode` / `ecrMode`. For a platform
problem, add the target framework and exact error text. See
[troubleshooting](https://github.com/amwal-pay/AmwalpayEcrWindows/blob/main/docs/troubleshooting.md).
