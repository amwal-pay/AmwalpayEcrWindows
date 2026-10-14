# Changelog

All notable changes to `AmwalPay.Ecr`. Versions 1.0.0 and 1.0.1 are not itemised here.

## 1.0.2

### Added

- **Close receipt.** `CloseReceiptAsync` asks the terminal to put its receipt away and
  return to idle, on both transports:
  - LAN / USB: `EcrTerminal.CloseReceiptAsync(merchantReference)` sends
    `CLOSE_RECEIPT`.
  - Web Service: `EcrWebServiceTerminal.CloseReceiptAsync(originalMerchantReference,
    merchantReference)` posts `/Ecr/closeReceipt`. The body has no `messageType` or
    `currencyCode`, names the sale / void / refund in `originalMerchantReference`, and
    is signed like the other Hub calls.
  - Either transport: `EcrOpenedSession.CloseReceiptAsync(merchantReference,
    originalMerchantReference)`. Note the argument order differs from
    `EcrWebServiceTerminal`.
  - Result type `EcrReceiptClosed` (`Idle` / `Refused` / `Failed`), new
    `EcrTransactionType.CloseReceipt`, and `EcrWebServiceCloseReceiptRequest` /
    `EcrWebServiceRequestJson.CloseReceipt` for callers building bodies themselves.
  - Call it from the till's explicit Close receipt action. The SDK never sends it from
    `SaleAsync` / `VoidAsync` / `RefundAsync`, nor on dialog dismiss.

### Upgrading from 1.0.1

Existing calls keep their names and signatures; the additions above are the only API
changes. Two things to check:

- `EcrTransactionType` has a new member, `CloseReceipt`. A `switch` over the enum that
  must be exhaustive needs a case; `RunAsync` rejects it like `Inquiry` and `Receipt`.
- Handling of a lost Web Service answer: it is an `EcrResult.Failed` with
  `Failure.Timeout` or `Failure.ConnectionLost`, and `Failed.Recovered` is always
  `null` (there is no auto-inquiry on Web Service). Earlier documentation described
  this as a `Declined` with `NextStep.InquireByMerchantReference`; code that handles
  `Failed` as an unknown outcome is correct for both. Web Service `ResponseTimeout` is
  130 s when set through `EcrSessions.Plan`.

