# Amwal ECR SDK for .NET

`AmwalPay.Ecr` lets a Windows or .NET application drive an Amwal POS terminal.
Your application sends the amount; the terminal reads the card, talks to the
payment backend and answers. No card data passes through your application: at
most you receive a masked PAN.

You need an Amwal POS terminal that Amwal's TMS has put into ECR mode (Wi-Fi or
Web Service). Without hardware, develop against the
[fake terminal](docs/integration-guide.md#12-testing-without-hardware).

| | |
|---|---|
| Package | `AmwalPay.Ecr` on nuget.org. Version to pin: **1.0.2** |
| Runtimes | .NET Framework 4.6.2 or later (4.7.2+ recommended); .NET 8 or later |
| Language | C# 9 or later |
| Transports | Wi-Fi / LAN (TCP), Web Service (HTTPS to the Amwal Hub), USB cable (you supply the channel) |

Platform support, dates and what was verified: [Compatibility](docs/compatibility.md).

## Install

```powershell
dotnet add package AmwalPay.Ecr --version 1.0.2
```

```xml
<PackageReference Include="AmwalPay.Ecr" Version="[1.0.2]" />
```

Pin an exact version (the square brackets), not a range: a payment client should
not change behaviour without a commit on your side. 1.0.2 is the version to pin
once it is published; NuGet currently lists only 1.0.0 and 1.0.1, and
`CloseReceiptAsync` needs 1.0.2. See the [changelog](CHANGELOG.md).

SDK-style .NET Framework projects default to C# 7.3 and fail with `CS8370`; set
`<LangVersion>9.0</LangVersion>`. Other build and host notes (binding redirects,
TLS 1.2): [Compatibility](docs/compatibility.md#net-framework).

## Quickstart: Wi-Fi / LAN

The terminal's TMS profile must have `terminalMode: 1` and `ecrMode: 2`; it shows
its IP and port on the idle screen.

```csharp
using AmwalPay.Ecr;

var plan = EcrSessions.Plan(
    link: new EcrLink.Lan(Host: "192.168.1.50"),
    config: new EcrConfig { SecureHashKey = lanKey });          // Wi-Fi/LAN key from Amwal

if (!plan.IsReady) { screen.Show(string.Join("\n", plan.Issues)); return; }

EcrTerminal terminal = EcrSessions.LanTerminal("P653200085189", plan);

switch (await terminal.SaleAsync(1.234m, merchantReference: "ORDER-1001"))
{
    case EcrResult.Approved a: receipt.Print(a.Rrn, a.AuthCode); break;
    case EcrResult.Declined d: screen.Show(d.Reason); break;
    case EcrResult.Failed f:   screen.Show(f.Failure.Message); break;   // unknown outcome, NOT a decline
}
```

## Quickstart: Web Service

`ecrMode: 4`. The till calls the Amwal Hub over HTTPS; there is no terminal IP.
You need the terminal serial, numeric merchant and terminal IDs, the environment
(SIT, UAT or PROD) and the Web Service key, which is a different secret from the
Wi-Fi/LAN key.

```csharp
using AmwalPay.Ecr;

var plan = EcrSessions.Plan(
    link: new EcrLink.WebService(MerchantId: "13593", TerminalId: "101311"),
    config: new EcrConfig
    {
        SecureHashKey = webServiceKey,             // not the Wi-Fi/LAN key
        Environment = EcrEnvironment.UAT,
    });

if (!plan.IsReady) { screen.Show(string.Join("\n", plan.Issues)); return; }

EcrWebServiceTerminal terminal = EcrSessions.WebServiceTerminal("P653200008251", plan);

switch (await terminal.SaleAsync(1.234m, merchantReference: "ORDER-1001"))
{
    case EcrResult.Approved a: receipt.Print(a.Rrn, a.AuthCode); break;
    case EcrResult.Declined d: screen.Show(d.Reason); break;
    case EcrResult.Failed f:   screen.Show(f.Failure.Message); break;   // unknown outcome, NOT a decline
}
```

## The one rule

A response that never arrives is not a decline. If a call returns
`EcrResult.Failed` (other than `Failure.Unreachable`), or throws
`OperationCanceledException` because you cancelled, the terminal may have taken the
money and been unable to say so. Never send the sale again. Look the transaction up
by the `merchantReference` you sent, or reconcile against the terminal:

```csharp
switch (await terminal.InquireByReferenceAsync("ORDER-1001"))   // LAN; on Web Service also pass transactionDate: ""
{
    case EcrInquiry.Found f:  /* read f.Transaction.Status; it may be Declined */ break;
    case EcrInquiry.NotFound: /* the terminal has no such transaction */          break;
    case EcrInquiry.Failed:   /* lookup failed: still unknown, ask again */       break;
}
```

On LAN the SDK makes that inquiry for you and attaches it to `Failed.Recovered`; on
Web Service you make it yourself. Details:
[When the answer never comes](docs/integration-guide.md#7-when-the-answer-never-comes).

## Documentation

| Document | Use it for |
|---|---|
| [Integration guide](docs/integration-guide.md) | Zero to a working, safe sale; outcome handling; production checklist. Start here |
| [API reference](docs/api-reference.md) | Every public type and method |
| [Wire protocol](docs/protocol.md) | LAN framing, signing, Web Service REST bodies, response codes |
| [Troubleshooting](docs/troubleshooting.md) | Symptom, cause, fix |
| [Compatibility](docs/compatibility.md) | Supported platforms, support calendar, verification evidence |
| [Changelog](CHANGELOG.md) | What changed per version, upgrade notes |
| [Simulator sample](samples/README.md) | In-repo desktop/console app that exercises every operation |

Two larger samples (WPF till and ASP.NET Core) are in the separate
[AmwalpayEcrWindowsExample](https://github.com/amwal-pay/AmwalpayEcrWindowsExample)
repository.

## Support

When raising an issue, include `result.Raw` (the terminal's exact answer), the SDK
log for the exchange, and the terminal's `terminalMode` / `ecrMode`. For a build or
platform problem add the target framework and the exact error text.
