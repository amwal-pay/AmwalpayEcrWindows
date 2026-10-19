# AmwalPay.Ecr.Simulator

A sample for `AmwalPay.Ecr`. It runs sale, void, refund, inquiry, and close receipt through `EcrSessions` and `EcrOpenedSession`. It is a demo of the calls, not the production handling in the [README](../README.md#resolve-an-unknown-outcome).

## Run

Requires the .NET 8 SDK. It opens a WinForms window where the Windows Desktop SDK is present, and a console menu otherwise. From the repository root:

```powershell
dotnet run --project samples/AmwalPay.Ecr.Simulator
```

## First-time setup

1. Settings: pick SIT, UAT, or PROD, and enter the Wi-Fi key and the Web Service key.
2. Terminals: add a Wi-Fi terminal (serial, IP, port; 9100 by default) or a Web Service terminal (serial, merchant id, terminal id).
3. Transaction: select the terminal, choose an operation, and send.

## Limits

- Wi-Fi and Web Service. A terminal saved as cable cannot be used.
- OMR amounts from 0.010 to 100,000.000. No e-receipt.
- Terminals and keys are stored unencrypted in `%LocalAppData%\AmwalPay.Ecr.Simulator\settings.json`.
