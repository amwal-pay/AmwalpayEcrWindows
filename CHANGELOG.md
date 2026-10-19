# Changelog

Notable changes to `AmwalPay.Ecr`. The format is based on Keep a Changelog 1.1.0. A patch
release can add API or change behaviour (1.0.2 did both): pin an exact version and read
the entry before you upgrade.

## 1.0.2 (2026-10-14)

No existing member changed signature or was removed.

### Changed

- **Action required.** An outcome the SDK cannot settle arrives as `EcrResult.Failed`
  (`Failure.Timeout` or `Failure.ConnectionLost`), not `Declined`. Web Service: a timeout,
  network error, empty or unreadable body, HTTP 5xx with nothing readable, or an Amwal Hub
  answer that asks for an inquiry. Wi-Fi / cable: a terminal answer that asks for an
  inquiry. 1.0.1 returned `EcrResult.Declined` with `NextStep.InquireByMerchantReference`
  for these; `Declined.NextStep` is always `None`. Affected: code that branches on
  `Declined.NextStep`, and a `Failed` branch that retries or reports a decline. Action:
  handle these cases in your `Failed` branch, remove the `Declined.NextStep` branches and
  reconcile by merchant reference; never resend. `Failed.Recovered` is null for these (no
  automatic inquiry), so the inquiry is yours. A `Declined` is a refusal: `NextStep` is
  `None`. See
  [Resolve an unknown outcome](docs/integration-guide.md#resolve-an-unknown-outcome).
- Web Service: `ResponseTimeout` from `EcrSessions.Plan` is 130 s (1.0.1: 120 s); a value
  of 120 s is raised to 130 s and any other value is kept. Through `Plan`, a call gives
  up after `ConnectTimeout` + `ResponseTimeout`: 140 s with the defaults (1.0.1: 130 s).
  Affected: a timeout or countdown sized to 1.0.1. Action: size it to that deadline; do
  not impose a shorter one.
- `EcrTransactionType` gained `CloseReceipt` (value 5). Affected: an exhaustive `switch`
  over the enum. Action: add a case. `RunAsync` rejects `CloseReceipt` with
  `ArgumentException`, like `Inquiry` and `Receipt`; `EcrTransactionTypes.MenuOptions` is
  unchanged.

### Added

- Close receipt, which asks the terminal to put its receipt away:
  - Wi-Fi / cable: `EcrTerminal.CloseReceiptAsync(merchantReference)`.
  - Web Service: `EcrWebServiceTerminal.CloseReceiptAsync(originalMerchantReference,
    merchantReference)`.
  - Either transport: `EcrOpenedSession.CloseReceiptAsync(merchantReference,
    originalMerchantReference)`. The argument order differs from `EcrWebServiceTerminal`
    and both are strings, so a swap compiles: pass them by name.
  - The SDK never sends close receipt by itself. Call it from your Close receipt action.
    See [Close receipt](docs/integration-guide.md#close-receipt) and
    [EcrOpenedSession](docs/api-reference.md#ecropenedsession).
- Result type `EcrReceiptClosed` (`Idle`, `Refused`, `Failed`).
- `EcrWebServiceCloseReceiptRequest` and `EcrWebServiceRequestJson.CloseReceipt`, for
  callers who build Hub requests themselves.
- `EcrConfig.WebServiceResponseTimeout` (130 s), applied by `EcrSessions.Plan`.

## 1.0.1 (2026-10-12)

### Changed

- Web Service: `EcrEnvironment.UAT` resolves to a different Hub address than in 1.0.0.
  Update an outbound allow-list entry for the UAT Hub (ask Amwal for the address) and
  retest UAT flows built on 1.0.0. No API changes.

## 1.0.0 (2026-10-08)

### Added

- First release: sale, void, refund and inquiry by receipt number or merchant reference
  over Wi-Fi, cable and Web Service; e-receipt URL over Wi-Fi / cable.
