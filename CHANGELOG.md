# Changelog

Notable changes to `AmwalPay.Ecr`. The format is based on Keep a Changelog 1.1.0. A patch
release can add API or change behaviour (1.0.2 did both): pin an exact version and read
the entry before you upgrade.

## Unreleased

No existing member was removed. `EcrLink.UsbCable()` still compiles; it now carries `ComPort`.

### Changed

- **Action required.** `EcrTransactionType` gained `Settlement`. Affected: an exhaustive
  `switch` over the enum. Action: add a case. `RunAsync` rejects `Settlement` with
  `ArgumentException`, like `Inquiry`, `Receipt`, and `CloseReceipt`.
  `EcrTransactionTypes.MenuOptions` is unchanged.
- `EcrLink.UsbCable` carries `ComPort` (empty until a port is chosen). A cable session
  opens that COM port through `SerialEcrChannel`. The COM number can change the next
  time the cable is plugged in: store `SerialPortChoice.Selection` and open
  `SerialPortBinding.Port`. See
  [USB cable implementation](README.md#usb-cable-implementation).
- Plan and validation text says "Wi-Fi ECR" where 1.0.2 said "LAN ECR". `EcrLink.Lan`
  is unchanged.

### Added

- Settlement, which closes the current batch and prints the settlement receipt. Confirm
  with the cashier before calling: the close starts as soon as the request arrives. It
  uses the full response timeout, because a close can wait on pending reversals. Safe
  to repeat when the answer is lost: a batch that closed answers `NothingToSettle` the
  next time, and one that did not settles on the retry.
  - Wi-Fi / USB cable: `EcrTerminal.SettlementAsync(merchantReference)` sends
    `SETTLEMENT`.
  - Web Service: `EcrWebServiceTerminal.SettlementAsync(merchantReference)`.
  - Either transport: `EcrOpenedSession.SettlementAsync(merchantReference)`.
    `SupportsSettlement` is true on Wi-Fi, USB cable, and Web Service.
  - Result type `EcrSettlement` (`Settled`, `NothingToSettle`, `NotSettled`, `Failed`).
    Amounts on `Settled` are major units. `Totals` always has `SALE`, `REFUND`, `VOID`,
    `PRE_AUTH`, and `CAPTURE`, with a zero row where the terminal omitted one.
    `CloseReceiptAsync` puts the terminal's settlement summary away.
  - `EcrWebServiceSettleRequest` and `EcrWebServiceRequestJson.Settle`, for callers who
    build Hub requests themselves.
  - See [Settlement](README.md#settlement).
- USB cable as a Windows COM port ("MediaTek USB VCOM"), with the same
  framed messages and the Wi-Fi signing key. `SerialPorts.List` and `ListFor` list what
  is plugged in now. `SerialEcrChannel` opens that port for the call and leaves it
  closed between calls. `EcrSessions.Open` still requires `usbChannel` for a cable plan.
  Sale, void, refund, inquiry, e-receipt, close receipt, and settlement use the same
  session calls as Wi-Fi. `UsbAccessoryEcrChannel` is the alternate host path (WinUSB
  on Windows, libusb on macOS and Linux) for a terminal that speaks Android Open
  Accessory.
- Web Service reachability, separate from a sale. `EcrOpenedSession.CheckReachabilityAsync`
  and `EcrWebServiceTerminal.ProbeReachabilityAsync` on
  the probe budget, so a Hub that does not answer does not consume the sale timeout.
  `ProbeReachabilityAsync` on a Web Service session still returns null; Wi-Fi and USB
  keep using it before a sale, and it still does not prove the terminal is idle.
  `EcrWebServiceCheckReachabilityRequest` and
  `EcrWebServiceRequestJson.CheckReachability` are for callers who build Hub requests
  themselves. See [Reachability](README.md#reachability).

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
  [Resolve an unknown outcome](README.md#resolve-an-unknown-outcome).
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
    See [Close receipt](README.md#close-receipt) and
    [Open a session](README.md#ecropenedsession).
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
