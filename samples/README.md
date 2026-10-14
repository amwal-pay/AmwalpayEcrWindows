# Amwal ECR samples

Samples for **`AmwalPay.Ecr` 1.0.2**. For a shareable Windows till (WPF) and a web
backend (ASP.NET Core), use the separate
[AmwalpayEcrWindowsExample](https://github.com/amwal-pay/AmwalpayEcrWindowsExample)
repository.

## AmwalPay.Ecr.Simulator

A desktop sample that registers terminals, picks SIT / UAT / PROD, keeps separate
Wi-Fi and Web Service secure-hash keys, and runs sale, void, refund, inquiry (by STAN
or merchant reference) and close receipt through `EcrSessions` and
`EcrOpenedSession`. Use it to exercise a terminal or the fake server without writing
code. It is not a template for production code.

### UI host

| Host SDK | UI |
|---|---|
| Windows with the .NET **Windows Desktop** workload | **WinForms**: Transaction / Terminals / Settings tabs and a log panel |
| macOS / Linux (no Windows Desktop SDK) | **Interactive console** with the same flows |

The project detects `Microsoft.NET.Sdk.WindowsDesktop` at build time and defines
`AMWAL_ECR_WINFORMS` or `AMWAL_ECR_CONSOLE`.

### Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- On Windows, the **.NET desktop development** workload so WinForms builds
- An Amwal POS terminal in Wi-Fi or Web Service ECR mode, or the fake server
  (`tools/fake_pos_server.py`; see
  [Testing without hardware](../docs/integration-guide.md#12-testing-without-hardware))

### Run

From the repository root:

```powershell
dotnet build AmwalECR.sln
dotnet run --project samples/AmwalPay.Ecr.Simulator
```

Or open `AmwalECR.sln` in Visual Studio, set **AmwalPay.Ecr.Simulator** as the
startup project and press F5.

### First-time setup

1. **Settings**: choose the environment (SIT / UAT / PROD) and enter the Wi-Fi/LAN
   secure-hash key and the Web Service secure-hash key.
2. **Terminals**: add a terminal.
   - Wi-Fi: serial, IP, port (default `9100`)
   - Web Service: serial, merchant ID, terminal ID
3. **Transaction**: select the terminal, pick an operation, send.

Settings are stored in `%LocalAppData%\AmwalPay.Ecr.Simulator\settings.json` (on
macOS / Linux, the platform's local application data folder). The file holds the
signing keys, so keep it out of source control.

### What it does and does not do

- Wi-Fi / LAN and Web Service; USB cable is stubbed (the package ships no USB channel).
- Shows the SDK log (WinForms: bottom panel and `Debug.WriteLine`; console: lines
  prefixed `[log]`, plus a "Show recent log" menu option) and the result with raw JSON.
- Shows an operation countdown (120 s Wi-Fi, 130 s Web Service) and a Cancel button
  that only stops waiting; a cancelled call is never reported as "cancelled".
- Shows an unknown outcome (`Failed`) as "Not completed", never as a decline, and a
  failed inquiry as a failure, not "Not found".
- Close receipt is its own button (Wi-Fi `CLOSE_RECEIPT`, Web Service
  `/Ecr/closeReceipt`), not tied to dismissing a dialog.
- No e-receipt QR or printing, no sign-on or capability narrowing.
