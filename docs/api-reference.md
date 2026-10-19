# Public types

What you construct. How to call each feature is the [integration guide](integration-guide.md). Parameters, defaults, and exceptions are the XML documentation shipped in the package.

## Session

<a id="ecropenedsession"></a>

| Type | Role |
|---|---|
| `EcrLink.Lan` | Wi-Fi. Host, and port (default `9100`). |
| `EcrLink.UsbCable` | USB. No address. You supply `IEcrChannel`. |
| `EcrLink.WebService` | Hub. Numeric `MerchantId` and `TerminalId`. |
| `EcrConfig` | Key, currency, timeouts, `Environment`, `EcrId`, `AutoInquireOnFailure`. |
| `EcrEnvironment` | `SIT` (default), `UAT`, `PROD`. |
| `EcrSessions` | `Plan`, `Open`, `LanTerminal`, `UsbCableTerminal`, `WebServiceTerminal`. |
| `EcrSessionPlan` | `IsReady`, `Issues`, `UsesLan`, `UsesUsbCable`, `UsesWebService`, `ConnectionSummary`. |
| `EcrOpenedSession` | Sale, void, refund, inquiry, e-receipt, close receipt, probe. |

`EcrTerminal` is the Wi-Fi and USB client. `EcrWebServiceTerminal` is the Hub client. `Open` builds the one the plan names. Use the session unless you already hold a specific client.

## Results

| Type | Values |
|---|---|
| `EcrResult` | `Approved`, `Declined`, `Failed` |
| `Failure` | `Unreachable`, `Timeout`, `ConnectionLost`, `Malformed`, `Unauthenticated`. `OutcomeUnknown` is false only for `Unreachable`. |
| `NextStep` | `None`, `InquireByMerchantReference`. On `Failed` it is always `InquireByMerchantReference`. On `Declined` it is `None`. |
| `EcrInquiry` | `Found` (`EcrTransaction`), `NotFound`, `Failed` |
| `EcrReceipt` | `Ready` (`Url`), `Unavailable`, `Failed` |
| `EcrReceiptClosed` | `Idle`, `Refused`, `Failed` |
| `EcrReachability` | `Reachable`, `Endpoint`, `Error` |

## Operations

`EcrTransactionType`: `Sale`, `Void`, `Refund`, `Inquiry`, `Receipt`, `CloseReceipt`.

`EcrTransactionTypes` exposes `MenuOptions` (sale, void, refund, inquiry), `MessageType`, `DisplayName`, `RequiresAmount`, `RequiresOriginalStan`, `RequiresOriginalDate`, `MovesMoney`, `AllowsOtherTerminal`.

`EcrTerminal.RunAsync` accepts only the three that move money. Inquiry, e-receipt, and close receipt have their own methods.

## Channel

Implement `IEcrChannel` for USB. Frame with `EcrFrames`. `TcpEcrChannel` is the Wi-Fi socket `EcrTerminal` opens for you. `EcrChannelTimeout` means the terminal took the request and did not answer.

## Logging

`IEcrLogger` has `Debug(string)`. `EcrLogger.None` is the default. `EcrLogger.From` wraps a delegate.

## Web Service request bodies

`EcrWebServiceSaleRequest`, `EcrWebServiceVoidRequest`, `EcrWebServiceRefundRequest`, `EcrWebServiceTransactionStatusRequest`, and `EcrWebServiceCloseReceiptRequest` are for a caller that builds the JSON. `EcrWebServiceRequestJson` writes each body and signs it when you pass the key. The terminal and session methods do this themselves.
