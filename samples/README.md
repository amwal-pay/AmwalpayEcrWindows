# Amwal ECR samples

Samples for **`AmwalPay.Ecr` 1.0.0**. For a shareable Windows till build, prefer the
published [AmwalECR-Windows-Example](https://github.com/amwal-pay/AmwalpayEcrWindowsExample)
repository (WPF + web).

## AmwalPay.Ecr.Simulator

Desktop sample that mirrors the Android **ECR-simulator** app: register terminals,
pick SIT/UAT/PROD, keep separate Wi‑Fi vs Web Service secure-hash keys, and run
Sale / Void / Refund / Inquiry (by STAN or merchant reference) through
`AmwalPay.Ecr` (`EcrSessions` → `EcrOpenedSession`).

### UI host

| Host SDK | UI |
|---|---|
| Windows + .NET **Windows Desktop** workload | **WinForms** (Transaction / Terminals / Settings tabs + log panel) |
| macOS / Linux Homebrew SDK (no WindowsDesktop) | **Interactive console** with the same flows |

The project detects `Microsoft.NET.Sdk.WindowsDesktop` at build time and defines
`AMWAL_ECR_WINFORMS` or `AMWAL_ECR_CONSOLE` accordingly.

### Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- On Windows: install the **.NET desktop development** workload (Visual Studio) or
  the Windows Desktop runtime/SDK so WinForms builds
- An Amwal POS terminal in Wi‑Fi or Web Service ECR mode (or the SDK fake terminal)

### Run

From the repo root:

```powershell
dotnet build AmwalECR.sln
dotnet run --project samples/AmwalPay.Ecr.Simulator
```

Or open `AmwalECR.sln` in Visual Studio, set **AmwalPay.Ecr.Simulator** as the
startup project, and press F5 (WinForms on Windows).

### First-time setup

1. **Settings** — choose Environment (SIT / UAT / PROD), set the Wi‑Fi / LAN
   secure-hash key and the Web Service secure-hash key.
2. **Terminals** — add a terminal:
   - **Wi‑Fi**: serial, IP, port (default `9100`)
   - **Web Service**: serial, merchant ID, terminal ID
3. **Transaction** — select the terminal, pick an operation, send.

Persistence: `%LocalAppData%\AmwalPay.Ecr.Simulator\settings.json`
(on macOS/Linux: `~/.local/share/AmwalPay.Ecr.Simulator/settings.json`).

### SDK log

WinForms: bottom panel + `Debug.WriteLine`.  
Console: lines prefixed with `[log]`, plus menu option “Show recent log”.

### Feature checklist vs Android ECR-simulator

| Feature | Android | This sample |
|---|---|---|
| Wi‑Fi / LAN | Yes | Yes |
| Web Service | Yes | Yes |
| Environment SIT/UAT/PROD | Yes | Yes |
| Separate LAN / WS hash keys | Yes | Yes |
| Sale / Void / Refund / Inquiry | Yes | Yes |
| Inquiry by STAN+date or merchant ref | Yes | Yes |
| Result + raw JSON | Yes | Yes |
| EcrSessions / EcrOpenedSession | Yes | Yes |
| USB cable | Yes | Stubbed — not supported on Windows |
| App to app | Yes | Stubbed — Android-only |
| Bluetooth | Listed | Stubbed — not supported |
| Sign-on / capability narrowing | Yes | Omitted (full menu) |
| Receipt QR / print | Yes | Omitted |
