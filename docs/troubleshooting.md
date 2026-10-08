# Troubleshooting

Symptom first, then the cause and what to do about it.

---

## How the SDK reports a lost answer

Read this table first; most symptoms below are one of its rows. It is exact for
LAN / USB (`EcrTerminal`).

| Situation | What you get | Outcome | Auto-inquiry runs? |
|---|---|---|---|
| Connection refused, no route, host unknown | `Failed` / `Failure.Unreachable` | Nothing was sent | No |
| **Connect** timed out (`ConnectTimeout`, 10 s) | `Failed` / `Failure.Unreachable` (`Connection timed out after 10000 ms`) | Nothing was sent | No |
| Request written, no **answer** within `ResponseTimeout` (120 s) | `Failed` / `Failure.Timeout` | **Unknown** | Yes |
| Reset or abort after the request was written; closed part way through the answer | `Failed` / `Failure.ConnectionLost` | **Unknown** | Yes |
| An answer that is empty or not JSON | `Failed` / `Failure.Malformed` | **Unknown** | Yes |
| An answer that is unsigned, wrongly signed, or echoes another request's nonce | `Failed` / `Failure.Unauthenticated` | **Unknown** | Yes |
| Your `CancellationToken` was cancelled | `OperationCanceledException` — no result | **Unknown** | No |
| The terminal refused (`96`, `25`, `51`, …) | `Declined` | No money moved (except `91`, `94` — inquire) | — |

"Auto-inquiry" is `EcrConfig.AutoInquireOnFailure` (default `true`): one
`InquireByReferenceAsync` on a fresh connection, attached as `Failed.Recovered`. It
never runs when nothing was sent, and it is never used to resend a sale.

**Web Service (`EcrWebServiceTerminal`) is different.** A transport error —
timeout, no network, reset, empty or non-JSON reply, HTTP 5xx — arrives as
`EcrResult.Declined` with `NextStep == NextStep.InquireByMerchantReference` (the
`ResponseCode` is usually `NO_RESPONSE`, or the HTTP status such as `502`), and as
`EcrInquiry.Failed` / `Failure.ConnectionLost` on an inquiry. There is no
`EcrResult.Failed`, no auto-inquiry. **That is an unknown outcome, not a refusal.**
See [Web Service ECR](#web-service-ecr).

---

## Nothing connects

### `Failure.Unreachable` on every request

In order of how often it is the answer:

**0. Wrong transport for the mode.** If TMS set `ecrMode: 4` (Web Service), the
terminal is **not** listening on TCP port 9100. Use [EcrWebServiceTerminal](api-reference.md#ecrwebserviceterminal)
instead of [EcrTerminal](api-reference.md#ecrterminal). Probing `192.168.x.x:9100`
will always fail for a Web Service terminal.

**1. The terminal is not in ECR mode.** A terminal does not listen on port 9100
unless TMS says it should. This is the most common cause on a new deployment, and
it looks exactly like a network fault.

Check the profile: `terminalMode` must be `1`, and `ecrMode` must be `1`
(USB cable — no TCP listener), `2` (Wi-Fi), or `4` (Web Service — HTTPS, no TCP listener). With
`terminalMode: 2` the terminal is standalone and nothing accepts ECR requests.
With `ecrMode: 3` (Bluetooth) the TCP port is closed deliberately.

A terminal in ECR mode over Wi-Fi shows its IP and port under the card scheme
logos on its idle screen. **If that line is not there, the terminal is not in
Wi-Fi ECR mode** — that is the fastest check you can make from across a counter.

**2. Wrong address.** The terminal's IP changes when it reconnects to Wi-Fi or
moves to another access point. Read the current one off its idle screen. Store
terminals by serial number and look the address up at send time.

**3. Different networks.** A till on another VLAN, on a guest network, or behind a
VPN cannot see a terminal on the shop Wi-Fi. Both must be able to route to each
other. See [Windows: network and host](#windows-network-and-host).

**4. The terminal is not on its idle screen.** The listener runs from the payment
tab. A terminal sitting in a menu, or with the app closed, is not listening.

**5. The Windows machine is in the way.** Firewall policy, network profile, a VPN
adapter or antivirus. See [Windows: network and host](#windows-network-and-host).

### Reading the error text

`Failure.Unreachable.Message` and `EcrReachability.Error` carry the Windows
(Winsock) message. The ones you meet most:

| Message | Meaning |
|---|---|
| `No connection could be made because the target machine actively refused it` | The address answered and nothing is listening on that port — terminal not in ECR mode, wrong port, or the process is not running. **Refused is a fast answer** |
| `Connection timed out after 10000 ms` (or `3000 ms` for a probe) | Nothing answered. Wrong IP, different network, a firewall silently dropping the packets, or client isolation on the Wi-Fi |
| `A socket operation was attempted to an unreachable host` / `…unreachable network` | The till has no route to that subnet — wrong interface, VPN, or the till is not on the network |
| `No such host is known` | You gave a name, not an IP, and it did not resolve. Use the IP |

Refused points at the terminal; a timeout points at the network in between.

### Probe timed out (not refused)

`ProbeReachabilityAsync()` reports a timeout when nothing answered within
`config.ProbeTimeout` (default 3 seconds). Unlike `refused`, a timeout
often means the IP is wrong, the devices are on different networks, or **AP client
isolation** blocks device-to-device traffic on the same Wi‑Fi.

Checklist:

- POS app is in ECR mode and shows `[ECR] listening on …:9100` in its log
- Registered IP matches the address under the card logos on the POS idle screen
- Both devices are on the same subnet (not guest Wi‑Fi vs staff Wi‑Fi)
- From the till: `Test-NetConnection <ip> -Port 9100` — `TcpTestSucceeded : True`?
- When testing against a fake server on the same PC, use `127.0.0.1`

### It worked yesterday and not today

The terminal took a different IP from DHCP. Either reserve one on the router or
re-read it from the idle screen — this is the reason to key terminals by serial
number rather than address.

If the address is unchanged, look at the Windows side: a Windows Update or a
group-policy refresh can flip a network's profile to Public, and a VPN client
that started with the machine can take over the route.

---

## Windows: network and host

### Windows Defender Firewall

The till is the **connecting** side of LAN ECR, so it needs *outbound* TCP to the
terminal's IP on port 9100 (the default). Windows Defender Firewall allows outbound
by default, so on a stock machine there is nothing to do. It becomes the problem
when:

- Group policy or a hardening baseline sets **outbound: block** (sometimes on the
  Public profile only). Add an allow rule:

  ```powershell
  New-NetFirewallRule -DisplayName "Amwal ECR (till to terminal)" `
      -Direction Outbound -Protocol TCP -RemotePort 9100 `
      -RemoteAddress 192.168.1.50 -Action Allow
  ```

- You are running the **fake terminal** (or anything that *listens*) on a Windows
  PC. Inbound is blocked by default, so the till connecting to it times out.
  Allow the listener, on the profile in use:

  ```powershell
  New-NetFirewallRule -DisplayName "Fake ECR terminal" `
      -Direction Inbound -Protocol TCP -LocalPort 9100 -Action Allow -Profile Private
  ```

Test the real port, not `ping`. A `ping` that fails proves nothing — ICMP echo is
handled by different rules (and is off on the Public profile) — and a `ping` that
works proves nothing about port 9100:

```powershell
Test-NetConnection 192.168.1.50 -Port 9100
```

`TcpTestSucceeded : True` means the SDK can connect.

### Network profile: Public vs Private

Windows assigns each network a profile — **Public**, **Private** or **Domain** —
and applies a different firewall rule set to each. Public is the strictest:
network discovery and most inbound rules are off. A till (or a fake server) that
landed on a new Wi-Fi network is often classed Public without anyone noticing.

```powershell
Get-NetConnectionProfile
# NetworkCategory : Public  <- suspect
Set-NetConnectionProfile -InterfaceAlias "Wi-Fi" -NetworkCategory Private   # elevated
```

Only set Private on a network you trust — the shop's own network, not a guest or
public one. This applies to Windows Services too: a service inherits the machine's
profile.

### VPN adapters and the wrong route

A full-tunnel VPN sends everything, including the shop subnet, into the tunnel, and
the terminal at `192.168.1.50` becomes unreachable (usually a timeout). Machines with
several adapters — Wi-Fi plus Ethernet, Hyper-V or WSL virtual switches, Docker
Desktop, a VPN — can also have **overlapping subnets** (`172.x` and `192.168.x`
ranges are common to both a shop and Docker/WSL), so Windows picks the wrong
interface for the address. The SDK does not choose or bind an interface; it asks the
OS to connect.

Ask Windows which way it would go:

```powershell
Find-NetRoute -RemoteIPAddress 192.168.1.50
route print
```

Fix it with split tunnelling (exclude the shop subnet, or enable "allow local LAN
access" in the VPN client), by giving the till's shop interface the lower metric, or
by moving the virtualization subnet.

### Antivirus and endpoint protection

Endpoint-security suites sometimes block a new application's outbound connections
or inspect and drop traffic on unfamiliar ports. It looks like `Connection timed
out` (dropped) or an immediate `refused`/reset (actively blocked), and it can be
intermittent. Confirm by trying from PowerShell (`Test-NetConnection`) — if that
succeeds and your app does not, the security software is treating your executable
differently. Add your till's executable to the allow list. Do not turn the
protection off to "test".

### The clock and time zone (`63` with the right key)

The request carries the till's **local wall-clock time** (`transactionDateTime`,
`yyyyMMddHHmmss`), and a signed request more than five minutes from the terminal's
clock is refused with `63` — the same code as a bad key. A till whose time zone is
wrong, or whose clock is not syncing, fails this way with a perfectly good key.
Check `w32tm /query /status` and the time-zone setting.

### Running as a Windows Service

- **Startup ordering.** A service that starts at boot may run before the network is
  up; a first `IsReachableAsync()` can fail. Retry the probe with backoff instead
  of concluding the address is wrong.
- **DPAPI account.** A key encrypted with `DataProtectionScope.CurrentUser` by one
  account cannot be decrypted by the service's account (`CryptographicException`
  from `ProtectedData.Unprotect`). Provision the key as the account the service runs
  as, or use `LocalMachine` scope knowingly.
- **Stop means cancel.** A stop request cancels your `CancellationToken`, which
  stops the wait and leaves the terminal mid-payment. See
  [The result arrives after the screen has gone](#the-result-arrives-after-the-screen-has-gone).

---

## Web Service ECR

Setup checklist (prerequisites → `Plan` → `IsReady` → `WebServiceTerminal` /
`Open` → first call):
[initialization process](integration-guide.md#initialization-process).

### Every Hub call fails authentication

Three things to check, in order:

1. **Wrong key type** — the Web Service REST key is not the LAN/Wi‑Fi key. They
   are different secrets from Amwal.
2. **Wrong environment** — `EcrEnvironment` must match where the terminal and
   merchant are registered (SIT / UAT / PROD).
3. **Malformed key** — must be even-length hex, at least 16 characters. The SDK
   validates via `EcrConfig.IsValidSecureHashKey()`, and refuses to build a request
   body (throws `ArgumentException`) when a key cannot be decoded.

Enable logging (see [The SDK logs nothing](#the-sdk-logs-nothing)). Signing
diagnostics show the formula and whether a key is configured (`configured` /
`not configured`); no part of the secret is logged.

### `merchantId and terminalId must be set` / `Failure.Malformed` with an empty reference

Web Service ECR needs numeric backend IDs on [EcrLink.WebService](api-reference.md#ecrlink)
or [EcrConfig](api-reference.md#ecrconfig). The client returns
`EcrResult.Failed` (`Failure.Malformed`, empty `MerchantReference`) and sends
nothing when they are missing or not numbers. Use [EcrSessions.Plan](api-reference.md#ecrsessions)
to validate before sending — missing IDs appear in `plan.Issues`.

### A sale came back `Declined` with `NextStep == InquireByMerchantReference`

**Not a refusal.** The HTTP call did not complete — timeout, no network, connection
reset (`ResponseCode == "NO_RESPONSE"`), or the Hub answered with an empty body, an
unreadable body or an HTTP 5xx with no message (`ResponseCode` is the status, e.g.
`502`) — and the HTTP layer synthesised a `success:false` envelope, which the SDK
reads as a decline but marks with `nextStep: INQUIRE_BY_MERCHANT_REFERENCE`. The Hub
may have processed the sale. **Do not resend it.** `Declined.NextStep` is the
reliable signal; `NO_RESPONSE` is only the commonest cause. Call
`InquireByReferenceAsync` with the sale's `merchantReference` (date may be blank),
or `InquireAsync(receiptNumber, transactionDate)` if you already have the STAN
from the terminal's screen, receipt, or Hub back office. Reconcile against the
batch if the lookup still leaves the outcome unknown. `result.Raw` for a
synthesised answer is just
`{"success":false,"responseCode":"NO_RESPONSE","message":"…","nextStep":"INQUIRE_BY_MERCHANT_REFERENCE"}`.
A `Declined` with `NextStep.None` — a real Hub refusal, or an HTTP 4xx with no
readable message — is final.

### An inquiry on Web Service returned `Failed` / `ConnectionLost`

The inquiry's own request hit a transport failure (timeout, no network, empty or
unreadable reply, 5xx), so the lookup did not happen. That is not "not found" and
not a finding — it is still unknown. Ask again with the same
`InquireAsync` / `InquireByReferenceAsync` call. `EcrInquiry.NotFound` is a
genuine "no such transaction" answer from the Hub.

### Web Service ECR on .NET Framework: TLS 1.2

The Hub requires **TLS 1.2**. An app that **targets** a .NET Framework version earlier
than 4.7 (4.6.2 is the declared floor and is affected; it is the app's target that
counts, not this package's) — including older WPF/WinForms tills — may negotiate only
older protocols by default, and every
call fails as `NO_RESPONSE`. The `Reason` is the generic `An error occurred while
sending the request.`: the underlying "could not create SSL/TLS secure channel"
error is one level down and is not surfaced by the SDK. Enable TLS 1.2 once, at
start-up, before the first Web Service call:

```csharp
ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
```

On an app targeting a version before 4.7 this line is required; an app that targets
.NET Framework 4.7 or later, or .NET 5+, uses the operating system's defaults, which already include
TLS 1.2, and a very old Windows (7 / Server 2008 R2) needs its own TLS 1.2 update as
well. LAN ECR is plain TCP and is not affected. This is documented .NET Framework
behaviour (Microsoft's *Transport Layer Security (TLS) best practices with the .NET
Framework*), **not verified in this repository's test runs**. .NET Framework 4.6.2
to 4.7.1 is supported (4.6.2 is the declared floor, 4.7.2 recommended); see
[Compatibility](compatibility.md#supported-platforms). Below 4.6.2 it is unsupported.

### Corporate proxy

The Web Service client uses the process's default proxy settings and shares one
`HttpClient`; there is no SDK setting for a proxy. On .NET Framework, configure
`<system.net><defaultProxy>` in `app.config`; on .NET 5+, the system proxy is used,
or set `HttpClient.DefaultProxy` before the first Web Service call. A proxy that
requires credentials or intercepts TLS shows up as `NO_RESPONSE` or an HTTP `407`.

---

## Signing and keys

### Web Service inquiry by merchant reference

Use `InquireByReferenceAsync()` (or set `OriginalMerchantReference` on the
transaction-status request with an empty `stan`). The SDK sends
`originalMerchantReference` and leaves `stan` empty. For lookup by receipt
number, keep using `InquireAsync(receiptNumber, transactionDate)`.

### Auto-recovery after timeout is LAN-only

`EcrConfig.AutoInquireOnFailure` triggers `InquireByReferenceAsync()` on
[EcrTerminal](api-reference.md#ecrterminal) only. On Web Service, resolve unknown
outcomes yourself with `InquireByReferenceAsync` (preferred after a lost sale) or
`InquireAsync` when you have the STAN.

### LAN works but Web Service hash is rejected (or the reverse)

You almost certainly swapped the two keys. LAN TCP uses field `secureHash` with
the Wi‑Fi and USB cable secret; Hub REST uses `secureHashValue` with the Web Service
secret. Same algorithm, different field name and different bytes.

### Every LAN call is `Failure.Unauthenticated`

The key on the till and the key on the terminal do not match — or the answer is not
from the terminal at all. Check, in order: the key is the **Wi‑Fi/LAN** one (not the
Web Service one); it is the key issued for *this* terminal; the terminal actually has
a key (a terminal with no key answers unsigned, which a till with a key rejects).

Against the [fake terminal](#testing-against-the-fake-server-everything-is-approved)
this is expected: it does not sign, so any configured key fails.

### `63` from the terminal

The request was unsigned, signed with the wrong key, or the till's clock is too far
from the terminal's. Nothing was attempted, so there is nothing to reconcile. With a
correct key, look at the [clock and time zone](#the-clock-and-time-zone-63-with-the-right-key).

---

## Connections work, requests do not

### `96` — a transaction is already in progress

Something else is using the card reader: another till, or an operator who started
a transaction at the terminal itself. The terminal serves one transaction at a
time. Inside your own till, a double-clicked Pay button that fires two sales does
the same — disable the control while a sale is in flight.

Wait and retry. `IsReachableAsync()` will not tell you about this — it proves the port
is open, not that the terminal is free.

`96` is never returned for an inquiry or a receipt, which read and change nothing.

### `12` — unsupported operation

The `messageType` is not one this terminal serves, or the terminal cannot perform
it. Check spelling and case; the field is upper case.

### `13` — invalid amount

The amount was missing, zero, or not 12 digits. The SDK does not range-check the
amount you pass, so a `0m` or a negative `decimal` reaches the terminal as such.
If you are building the message yourself, check the zero padding.

### `25` — original transaction not found

The receipt number does not match a transaction on that terminal for that day.
Three things to check:

- **The date.** A receipt number is only unique within a terminal's day, so a
  refund, inquiry or receipt needs the transaction date as `yyyyMMdd`.
- **The terminal.** If the original was taken on a different till, pass
  `originalTerminalId`.
- **The number itself.** It is the receipt number printed on the receipt, not the
  RRN and not the transaction id.

### The response never arrives

Your read timed out. Before you shorten the timeout, check what you are actually
waiting for: a sale waits for the **cardholder**, and ninety seconds is not
unusual when someone is finding their card and entering a PIN. The SDK's default
`ResponseTimeout` is 120 seconds for that reason.

If you shorten it, you are choosing to abandon transactions that would have
succeeded — and you still have to resolve the unknown outcome.

After a `Timeout`, expect the call to take up to a second `ResponseTimeout` longer:
the auto-inquiry is a second request. Set `AutoInquireOnFailure = false` only if
your till runs its own reconciliation.

---

## Money problems

### Amounts are wrong by a factor of ten (or a thousand)

`MinorUnitDigits` does not match the currency. It decides where the decimal point
falls: OMR is `3`, USD is `2`, JPY is `0`. Nothing cross-checks it against
`CurrencyCode`, so a mismatch is silent.

Confirm with a log: 1.234 OMR must go out as `000000001234`.

### The amount is off by a cent or a baisa

An amount with more decimal places than `MinorUnitDigits` is rounded, away from zero
at the midpoint, on its way to the wire — `1.2345m` OMR goes out as `1.235`. Round
in your till (`Math.Round(amount, 3, MidpointRounding.AwayFromZero)`) so both sides
see the same figure.

### The amount is `1,234` or fails to parse

You are formatting or parsing with the current culture. `Approved.Amount` and
`EcrTransaction.Amount` are strings in **invariant** format (`1.234`); parse them
with `decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture)`. On a
machine whose regional format uses a comma decimal separator, culture-sensitive
parsing silently produces a different number.

### The amount shows as `000000000365`

You are displaying the wire value. `EcrResult.Approved.Amount` is already in
**major units** (`0.365`) — if you see the padded form, you are reading `Raw` or
building the display from the request rather than the result.

### The customer was charged but the till says declined

Almost always one of three things:

**You booked a `Failed` as a decline.** `Timeout` and `ConnectionLost` mean the
outcome is *unknown*, not refused. The terminal may have completed the payment
and been unable to say so.

**You booked a Web Service `Declined` with `NextStep == InquireByMerchantReference`
(usually `NO_RESPONSE`) as a decline.** That `Declined` is a network error in
disguise — see [Web Service ECR](#web-service-ecr).

**You decided from `ResponseCode`.** Use the result type. A partial approval that was
then voided carries the bank's own `00`; a client matching on the code books a
sale that was undone.

### The customer was charged twice

A retry after an unknown outcome. Never retry a `Timeout` or `ConnectionLost`
blind — call `InquireByReferenceAsync` with the sale's reference (or `InquireAsync`
for the receipt number), or reconcile against the terminal, and only then decide.
The terminal answers inquiries while busy precisely so this is possible.

Two other routes to the same outcome: a Pay button that started two sales, and a
cancelled `CancellationToken` treated as "the sale did not happen" — cancelling
stops the wait, not the payment.

### Goods went out for money that never arrived

A partial approval was treated as a full one. When `PartialApproval` is `true`,
`Amount` is what was taken and `RequestedAmount` is what was asked for — the
difference has to be collected by other means before the goods leave.

---

## Void and refund

### A void was refused

Three rules, all decided by the terminal and its backend before anything is sent:

- **Not found** — no transaction with that receipt number on that terminal.
- **Outside the void window** — TMS configures a cut-off (`voidIntervalTime`);
  after it, a transaction can only be refunded, not voided.
- **Not voidable** — most often because it already was.

The reason is in `Declined.Reason`. None of these reached the backend, so no
money moved.

Use `InquireAsync()` first if you want to know in advance: `CanVoid` has all three
rules already applied.

### The void receipt shows an authorisation code that was never issued

Do not print `AuthCode` on a void. A void reverses an authorisation rather than
making one, so the terminal returns a placeholder.

### A refund was refused but a void would have worked (or the reverse)

They are different operations. A void cancels a transaction from the current
batch, in full, with no card present. A refund returns money — possibly partially,
possibly days later — and the cardholder must present their card. After the void
window closes, only a refund is possible.

### A void or refund for another till's transaction hit this terminal instead

You passed `originalTerminalId` to an `EcrOpenedSession` that is on Web Service. That
transport has no such field, and the session drops the argument silently. Use
`session.UsesLocalTerminal` to guard it, or use LAN ECR for cross-till operations.

---

## Inquiry and receipt

### The inquiry says `approved: true` but the transaction was declined

Working as intended. `approved` on the envelope means the **lookup** succeeded.
What became of the transaction is `Transaction.Status`. An inquiry that finds a
declined sale is a successful inquiry — and `Failed.Settled` is true for one too.

### `EcrReceipt.Unavailable` with an empty reason

The backend published no URL for that transaction. The SDK reports
`Unavailable` whenever `receiptUrl` is empty, whatever the response code claims —
a receipt with no URL is not a receipt. Retry is harmless.

### `EcrReceipt.Unavailable` on Web Service

Receipt is LAN / USB only. `EcrWebServiceTerminal` has no receipt method, and
`EcrOpenedSession.ReceiptAsync` on Web Service returns
`Unavailable("Receipt fetch is only supported over Wi‑Fi and USB cable ECR")`
without contacting anything. Check `session.SupportsReceipt` first.

### The QR code will not scan

Render it locally from `EcrReceipt.Ready.Url` at a reasonable size (200 px or
more) with a quiet margin. Do not fetch an image of it: rendering locally means
it appears at once and survives the network dropping, which is exactly when a
customer is standing at the counter waiting.

---

## USB cable

### `Failure.Unreachable`, or a `Failure.ConnectionLost` that should have been one

The package ships no USB channel; the behaviour is whatever your `IEcrChannel` does.
The SDK classifies an `IOException` from a channel **by its message**: only one
containing `closed the connection` becomes `ConnectionLost` (outcome unknown,
auto-inquiry runs). Any other `IOException` becomes `Unreachable` — "nothing was
sent" — so a channel that lets a raw I/O error escape after the write is telling the
till a payment did not happen. Wrap it, as in the
[skeleton](integration-guide.md#usb-cable-ecr--bring-your-own-channel).

### `InvalidOperationException: USB cable sessions require a channel factory`

`EcrSessions.Open` on a `EcrLink.UsbCable` plan needs `usbChannel: () => …`. There is
no default.

---

## Build, restore and platform

Problems that show up at build or first launch rather than at the terminal. The
supported minimums are **.NET Framework 4.6.2** (4.7.2 recommended) and **.NET 8**,
with **C# 9 or later**; see [Compatibility](compatibility.md). Microsoft's end dates
(.NET 8 and 9 on Nov 10, 2026; .NET Framework 4.6.2 on Jan 12, 2027) change those
minimums: see the [Support calendar](compatibility.md#support-calendar).

### `CS8370` / `CS8400`: init-only setters are not available (and `with` does not compile)

```text
error CS8370: Feature 'init-only setters' is not available in C# 7.3.
              Please use language version 9.0 or greater
error CS8400: Feature 'init-only setters' is not available in C# 8.0.
              Please use language version 9.0 or greater
```

Your project's language version is too low. `EcrConfig` and other public types use
init-only setters, so an object initializer such as `new EcrConfig { ... }` needs C#
9, and so does a `with` expression. SDK-style **.NET Framework** projects default to
C# 7.3. Set the language version in the project file:

```xml
<PropertyGroup>
  <LangVersion>9.0</LangVersion>   <!-- or: latest -->
</PropertyGroup>
```

Use Visual Studio 2019 16.8+ or the .NET SDK 5+; an older compiler does not know C#
9 whatever you set. (Both errors above were reproduced; 9.0 compiles.)

### Build warning: "doesn't support net461 and has not been tested with it"

```text
… doesn't support net461 and has not been tested with it.
Consider upgrading your TargetFramework to net462 or later.
```

Emitted for `System.Text.Json`, `System.Text.Encodings.Web` and
`Microsoft.Bcl.AsyncInterfaces` when the project targets **.NET Framework 4.6.1**.
It comes from those dependencies, not from this SDK's code: the build succeeds, but
4.6.1 is **not supported** (Microsoft ended it on Apr 26, 2022, and `System.Text.Json`
8 does not support net461). Retarget to **4.6.2 or later**, preferably **4.7.2 or
later**. net462 and later emit no such warning.

### Build warning `NETSDK1138`: the target framework is out of support

A `net6.0` (or `net7.0`) project restores the `netstandard2.0` asset and compiles,
and the .NET SDK warns that the runtime is out of support. .NET 6 and 7 are *Works,
not supported* here; move to .NET 8 or later.

### Could not load file or assembly (`FileLoadException`) on a classic .NET Framework project

> **Expected from how .NET Framework works; not reproduced in this repository.** No
> .NET Framework host was available for the test runs, so the cause and fix below
> are the standard .NET Framework behaviour, not something we saw fail.

At start-up or the first call, on .NET Framework, you see `FileLoadException` with
"Could not load file or assembly 'System.Text.Json …' or one of its dependencies.
The located assembly's manifest definition does not match the assembly reference"
(or the same for `System.Memory`, `System.Buffers`, `System.Runtime.CompilerServices.Unsafe`,
`System.Threading.Tasks.Extensions`, `System.Numerics.Vectors`, `System.Text.Encodings.Web`
or `Microsoft.Bcl.AsyncInterfaces`).

**Cause.** The `netstandard2.0` asset depends on those assemblies, and .NET Framework
loads an exact assembly version unless a **binding redirect** in the *executable's*
`app.config` (or the web project's `web.config`) says a newer one is fine. A class
library's own config is not read.

**Fix.**

1. **SDK-style project:** make sure redirects are generated. It is the
   `AutoGenerateBindingRedirects` property, on by default for executables; set it
   explicitly (`<AutoGenerateBindingRedirects>true</AutoGenerateBindingRedirects>`) in
   the **application** project, not a library, and rebuild.
2. **Classic `packages.config` project:** run `Add-BindingRedirect` in the Visual
   Studio Package Manager Console for the application project, or add entries by
   hand. The assembly versions in the packages this SDK restores are, for example,
   `System.Text.Json` 8.0.0.0 and `System.Memory` 4.0.1.2 (assembly versions, which
   are not the NuGet package versions):

```xml
<runtime>
  <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
    <dependentAssembly>
      <assemblyIdentity name="System.Text.Json" publicKeyToken="cc7b13ffcd2ddd51" culture="neutral" />
      <bindingRedirect oldVersion="0.0.0.0-8.0.0.0" newVersion="8.0.0.0" />
    </dependentAssembly>
    <dependentAssembly>
      <assemblyIdentity name="System.Memory" publicKeyToken="cc7b13ffcd2ddd51" culture="neutral" />
      <bindingRedirect oldVersion="0.0.0.0-4.0.1.2" newVersion="4.0.1.2" />
    </dependentAssembly>
  </assemblyBinding>
</runtime>
```

Add one `dependentAssembly` per assembly the error names. Copy the exact version
from the DLL that landed in your output folder if it differs.

### My app targets 4.6.2 and support ends Jan 12, 2027

.NET Framework 4.6.2 is the declared floor, so the SDK supports it today. Microsoft
ends support for 4.6.2 on **Jan 12, 2027**; after that the floor rises to 4.7.2
unless the owners decide otherwise. Nothing in the package changes on that day and
there is no `net462` asset to lose: your app already uses the `netstandard2.0` build, which
4.7.2 uses too. What changes is that 4.6.2 stops being covered by our support
statement. Retarget to **4.7.2 or 4.8** (4.7.2 onward has no end date) before then;
it also removes the need for the TLS 1.2 line. The dated list is in the
[Support calendar](compatibility.md#support-calendar). The same applies to .NET 8 and
.NET 9 (Microsoft ends both on **Nov 10, 2026**; move to .NET 10).

### Web Service calls fail on .NET Framework before 4.7

Every call comes back as `NO_RESPONSE`. That is TLS 1.2 not being enabled for an app
that targets a version before 4.7 (4.6.2 included); see
[Web Service ECR on .NET Framework: TLS 1.2](#web-service-ecr-on-net-framework-tls-12).

### The window freezes during a sale

Something is blocking the UI thread on the call (`.Result`, `.Wait()`,
`GetAwaiter().GetResult()`). It will not deadlock — a regression test blocks from a
single-threaded context on the LAN, recovery, timeout and Web Service paths and they
all complete — but a sale takes
as long as the cardholder takes, so the window is frozen for all of it. `await` the
call instead. The test uses a purpose-built single-threaded context, not the real
WinForms, WPF or ASP.NET one.

---

## Development

### The SDK logs nothing

It is silent by default, so it never writes to a log you did not choose. Pass a
logger:

```csharp
// LAN
new EcrTerminal("…", serialNumber: "…", logger: EcrLogger.From(m => Debug.WriteLine(m, "Ecr")));

// Web Service
new EcrWebServiceTerminal("…", logger: EcrLogger.From(m => Debug.WriteLine(m, "EcrWebService")));
```

`Debug.WriteLine` only appears in a debugger or DebugView; in a service, write to a
file or the Event Log through your logging framework. Signing lines say only whether a key is
`configured`; no part of the secret is logged. Payload and hash values may still
appear — treat logs as transaction records.

### The result arrives after the screen has gone

Cancelling the `CancellationToken` stops the SDK waiting; it does **not** stop the
transaction. The call throws `OperationCanceledException` — there is no result — and
the terminal carries on with the cardholder. Hold the call in something that
outlives the screen — a view model or a service, not a `Window` or a `Form` — and
treat a cancelled call as an unknown outcome. Pass your **own** `merchantReference`
whenever you pass a token, so you can inquire by it afterwards.

### `ArgumentException` on the `await`

Argument errors are raised when you `await`, before a connection is opened: a blank
receipt number, a `merchantReference` that is too long or contains a space, `&` or
`=`, or `RunAsync` given a type it does not run. `EcrConfig` throws the moment you
assign a bad `SecureHashKey`, `Port` or `MinorUnitDigits`.

### Testing against the fake server, everything is approved

That is its default. It also does declines, slow cardholders and missing
transactions:

```powershell
py tools\fake_pos_server.py --port 9100 --decline 51
py tools\fake_pos_server.py --port 9100 --delay 8
py tools\fake_pos_server.py --port 9100 --not-found
```

It does not sign anything, so a terminal built with a `SecureHashKey` gets
`Failure.Unauthenticated` from it. Build the test terminal with no key — see the
[signing caveat](integration-guide.md#the-signing-caveat).

### Sending a request by hand

Hercules Setup Utility, **TCP Client** tab, with `$XX` hex escapes for the 2-byte
length header:

```
$00$77{"messageType":"SALE","merchantReference":"TEST01","terminalSerial":"TW1","amount":"000000001250","currencyCode":"512"}
```

`$00$77` is the body length — 119 bytes here. **Recount it if you change anything
but the digits**, or the terminal waits for bytes that never arrive. Amounts are
fixed-width, so swapping one 12-digit figure for another keeps the prefix
correct.

The message is unsigned, so it works against the fake server and a terminal that has
no key; a terminal with a key answers `63`.

The reply is framed too, so tick **HEX** display or ignore its first two bytes.

---

## Still stuck?

Collect these before asking for help — they answer most questions on their own:

1. `result.Raw` (or `inquiry.Raw` / `receipt.Raw`) — the terminal's exact answer
2. The SDK log for the exchange, which contains the request you sent
3. The terminal's `terminalMode` and `ecrMode` from its TMS profile
4. The terminal serial and the IP you were dialling
5. The output of `Test-NetConnection <ip> -Port 9100` and `Get-NetConnectionProfile`
   from the till
