# The ECR wire protocol

Everything an Amwal POS terminal and a cash register say to each other. The
protocol is language-agnostic. Read this if you are implementing a client on a
platform this package does not cover (iOS, Flutter, an embedded till), writing the
USB channel the package leaves to you, debugging on the wire, or writing a
terminal-side implementation.

If you are using the C# SDK (`AmwalPay.Ecr`) you do not need most of this; see the
[integration guide](integration-guide.md). Sections 3-8 describe **LAN / Wi-Fi TCP**
(`ecrMode` 1 or 2). Section 9 describes **Web Service REST** (`ecrMode` 4).

The C# sources are the reference for everything below:

| Concern                              | Source                                                                                                                                                                   |
| ------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Framing                              | [`EcrFrames.cs`](../src/AmwalPay.Ecr/EcrFrames.cs)                                                                                                                       |
| Request building (LAN)               | [`Internal/EcrMessage.cs`](../src/AmwalPay.Ecr/Internal/EcrMessage.cs)                                                                                                   |
| Codec and JSON reading               | [`Internal/EcrMessageCodec.cs`](../src/AmwalPay.Ecr/Internal/EcrMessageCodec.cs), [`Internal/EcrJson.cs`](../src/AmwalPay.Ecr/Internal/EcrJson.cs)                       |
| Signing (LAN)                        | [`Internal/SecureHash.cs`](../src/AmwalPay.Ecr/Internal/SecureHash.cs)                                                                                                   |
| Reading answers                      | [`Internal/EcrResponseEnvelope.cs`](../src/AmwalPay.Ecr/Internal/EcrResponseEnvelope.cs), [`Internal/EcrWireReaders.cs`](../src/AmwalPay.Ecr/Internal/EcrWireReaders.cs) |
| Transport, timeouts, failure mapping | [`EcrChannel.cs`](../src/AmwalPay.Ecr/EcrChannel.cs), [`EcrTerminal.cs`](../src/AmwalPay.Ecr/EcrTerminal.cs)                                                             |
| Web Service                          | [`WebService/`](../src/AmwalPay.Ecr/WebService/)                                                                                                                         |

---

## 1. Transport

|              |                                                                |
| ------------ | -------------------------------------------------------------- |
| Transport    | TCP over the local network                                     |
| Default port | `9100`                                                         |
| Encoding     | UTF-8 JSON                                                     |
| Framing      | 2-byte big-endian unsigned length header, then that many bytes |
| Connections  | One request and one response per connection, then close        |
| Concurrency  | The terminal serves one transaction at a time                  |

TCP is a byte stream and carries no message boundaries, so the length header is
what tells the reader where a message ends. A message is at most 65 535 bytes.

```
┌──────────┬──────────┬─────────────────────────────────┐
│ byte 0   │ byte 1   │ body                            │
│ len >> 8 │ len&0xFF │ UTF-8 JSON, `len` bytes         │
└──────────┴──────────┴─────────────────────────────────┘
```

A sale whose JSON body is 119 bytes, so the header is `0x00 0x77`:

```
00 77 7B 22 6D 65 73 73 61 67 65 54 79 70 65 22 3A 22 53 41 4C 45 22 …
      {  "  m  e  s  s  a  g  e  T  y  p  e  "  :  "  S  A  L  E  "  …
```

**The connection stays open until the terminal answers.** A sale takes as long
as the cardholder takes — presenting a card, entering a PIN, the backend
authorising. Ninety seconds is not unusual; the SDK's default read timeout
(`EcrConfig.ResponseTimeout`) is 120 seconds for that reason. Connecting is a
separate, short budget (`EcrConfig.ConnectTimeout`, 10 seconds): it either
happens quickly or the address is wrong.

---

## 2. Prerequisite: the terminal must be in ECR mode

A terminal does not listen on port 9100 by default. Its TMS profile decides:

| `terminalMode` | Meaning                                       | `ecrMode`                                               |
| -------------- | --------------------------------------------- | ------------------------------------------------------- |
| `1`            | ECR-attached — the terminal serves a till     | `1` USB cable, `2` Wi-Fi, `3` Bluetooth, `4` WebService |
| `2`            | Standalone — worked by hand at its own screen | `null`                                                  |

**The TCP listener opens only for `ecrMode` 2 (Wi-Fi)**, the one IP transport.
On Bluetooth the port stays closed. On Web Service (`ecrMode: 4`) the terminal
does not listen on TCP at all — the till uses HTTPS to the Amwal Hub instead
(see [§9 Web Service REST](#9-web-service-rest)).

On `ecrMode` 1 (USB cable) no port is opened either. The terminal waits on the
cable as a USB accessory, and the till reaches it as the USB host. A till still
pointed at an IP for that mode reaches nothing. The C# package ships no USB
channel; write one for your hardware and frame with `EcrFrames` (see the
[integration guide](integration-guide.md#usb-cable-bring-your-own-channel)).

Everything above the transport is identical either way: the same framing, the
same signing, the same messages and the same response codes. Only how the bytes
travel differs.

A terminal in ECR mode also hides its own transaction buttons — the till owns the
amount, and a sale keyed at the terminal would never reconcile against it — and
shows its IP address and port under the card scheme logos when the link is Wi-Fi,
so an operator can read it off and register the terminal on the till.

If connections are refused, check the profile before debugging the network.

---

## 3. Request

Every request carries the same envelope. Fields that do not apply to the
operation are **absent, not empty** — the terminal reads a field's presence as
meaning the operation uses it.

```json
{
  "version": 1,
  "messageType": "SALE",
  "merchantReference": "A1B2C3D4E5F6",
  "terminalSerial": "P653200085189",
  "currencyCode": "512",
  "transactionDateTime": "20260809171259",
  "ecrId": "ECR01",
  "amount": "000000001234"
}
```

| Field                       | Always                         | Description                                                                                                                                 |
| --------------------------- | ------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------- |
| `version`                   | ✔                              | Protocol version. `1` today. Bumped only for a change older terminals cannot read                                                           |
| `messageType`               | ✔                              | `SALE`, `VOID`, `REFUND`, `INQUIRY`, `RECEIPT`, `CLOSE_RECEIPT`                                                                              |
| `merchantReference`         | ✔                              | The till's own reference for this request, echoed in the response. See [Merchant reference](#merchant-reference)                            |
| `terminalSerial`            | ✔                              | The terminal being addressed, as registered on the till                                                                                     |
| `currencyCode`              | ✔                              | ISO 4217 numeric. `512` is OMR                                                                                                              |
| `transactionDateTime`       | ✔                              | `yyyyMMddHHmmss`, the till's clock                                                                                                          |
| `ecrId`                     | ✔                              | Identifies this till on the terminal's records                                                                                              |
| `amount`                    | Sale, refund                   | 12 digits, zero-padded, **minor units**                                                                                                     |
| `stan`                      | Void, refund, inquiry, receipt | 6 digits, zero-padded. The receipt number of the original                                                                                   |
| `originalTerminalId`        | Optional                       | The terminal the original was taken on, when not this one                                                                                   |
| `originalTransactionDate`   | Refund, inquiry, receipt       | `yyyyMMdd`. A receipt number is only unique within a terminal's day                                                                         |
| `originalMerchantReference` | Inquiry by reference           | The `merchantReference` the earlier transaction was sent with. Sent **instead of** `stan`, and only on `INQUIRY`; the date is then optional |
| `nonce`                     | When signed                    | 32 hex characters, used once. See [Signing](#signing)                                                                                       |
| `secureHash`                | When signed                    | HMAC-SHA256 over the rest of the message                                                                                                    |

### Signing

Anyone who can reach the terminal's port can send it a message. Signing is what
separates a request from your till from a request from anything else on the shop
network — and what stops a captured request being sent a second time.

Amwal issues a **secret per terminal**, delivered to the terminal by TMS and to
you separately. Configure it and the SDK signs everything it sends and checks
everything it receives. Leave it out and messages travel unsigned, exactly as
the link worked before signing existed.

```csharp
new EcrConfig { SecureHashKey = "<hex from your protected store for this terminal mode>" }
```

#### What is signed

1. Take every top-level field whose value is a string, number or boolean.
2. Drop nulls, nested objects and arrays.
3. Drop `secureHash` itself.
4. Sort by field name.
5. Join as `name=value`, separated by `&`.
6. HMAC-SHA256 under the secret, written as uppercase hex.

```
amount=000000001234&currencyCode=512&ecrId=ECR01&merchantReference=ORD-1&
messageType=SALE&nonce=9F86D081884C7D65...&terminalSerial=P653200085189&
transactionDateTime=20260817171259&version=1
```

Nested fields are excluded deliberately. `ecrResponse` passes the backend's own
record through untouched, and two JSON libraries cannot be relied on to write a
nested object identically — a signature depending on key order inside a
passthrough field would fail for reasons unrelated to tampering. Every field
either side acts on is a top-level scalar.

The key is **hex**: a 32-character key is 16 bytes. A key that is not hex is
rejected rather than read as text, because the two sides would then disagree
about the bytes and every message would fail with nothing to show why.

A complete reference in C# (the SDK's own is `SecureHash` in `Internal/SecureHash.cs`):

```csharp
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

// The string that gets signed: top-level scalar fields, minus the hash field, sorted by name.
static string Compose(JsonObject message, string hashField)
{
    var pairs = message
        .Where(p => p.Key != hashField && p.Value is JsonValue)
        .Select(p => (Name: p.Key, Value: Text((JsonValue)p.Value!)))
        .OrderBy(p => p.Name, StringComparer.Ordinal)        // by code unit, not by culture
        .Select(p => p.Name + "=" + p.Value);
    return string.Join("&", pairs);
}

// A string as itself; a number or boolean as JSON writes it (true, 512).
static string Text(JsonValue v) => v.TryGetValue<string>(out var s) ? s : v.ToJsonString();

static string Sign(string composed, string hexSecret)
{
    var key = Convert.FromHexString(hexSecret);              // .NET 5+; decode by hand on netstandard2.0
    using var hmac = new HMACSHA256(key);
    return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(composed)));   // uppercase hex
}

static string NewNonce()
{
    var bytes = new byte[16];
    using var rng = RandomNumberGenerator.Create();
    rng.GetBytes(bytes);
    return Convert.ToHexString(bytes);                       // 32 hex characters
}
```

To sign a LAN request: add `nonce`, compute `Sign(Compose(message, "secureHash"), key)`,
then add the result as `secureHash`. To verify a response: recompute over the answer
with the same `Compose`, compare in constant time, and check `nonce` equals the one
you sent. (Both `Compose` and `Sign` reproduce byte-for-byte what the SDK sends.)

#### Why the nonce

A signature proves a message came from a till holding the key and was not
altered. It does **not** prove the message is new — a captured message stays
validly signed for ever, and an attacker who cannot forge anything can still
send the same refund fifty times.

The nonce is what stops that: 128 random bits, inside the signature, and the
terminal refuses one it has already answered. `transactionDateTime` bounds how
long that has to be remembered — a message more than five minutes from the
terminal's clock is refused on its timestamp alone.

#### Responses are signed too

The terminal signs its answer and echoes the request's `nonce` inside that
signature. Check both. (The SDK does, and reports a failure as
`Failure.Unauthenticated` — never as a decline.) An answer is what decides whether goods leave the shop,
so anything on the network that replies on port 9100 before the terminal does
could otherwise claim `approved` for a payment that never happened — without
touching the payment backend at all.

#### Rollout

A terminal with no key accepts unsigned requests. A terminal with a key requires
them. Because the key comes from TMS, signing is switched on per terminal by
issuing one — no app release is involved on either side.

### Merchant reference

`merchantReference` is the till's own name for the request. A till whose system
already numbers its orders sends that number, so the same string identifies the
transaction in its books, in the terminal's records and in any later inquiry —
there is no mapping table to keep. A till with nothing to link to leaves it
blank, and the SDK generates 12 uppercase hex characters and reports them back on
the result.

|            |                                                                                        |
| ---------- | -------------------------------------------------------------------------------------- |
| Length     | 1–32 characters                                                                        |
| Characters | Printable ASCII, no spaces, and not `&` or `=`                                         |
| Uniqueness | The till's business, not the terminal's. The terminal carries it and does not check it |

`&` and `=` are excluded because a signed message builds its input as
`key=value&key=value`; a reference holding a separator could make two different
messages hash alike.

> **Older name.** This field was called `requestId` before tills could supply
> their own reference. The terminal still reads `requestId` on a request, and
> still sends it alongside `merchantReference` on a response, so an ECR built
> against the earlier documentation keeps working. New work should use
> `merchantReference`.

### Amounts

Amounts travel as a whole number of the currency's smallest unit, zero-padded to
12 digits — ISO 8583 field 4. The decimal point is implied by the currency:

| Currency                   | Minor unit digits | 1.234 goes out as         |
| -------------------------- | ----------------- | ------------------------- |
| OMR — 1 rial = 1000 baisa  | 3                 | `000000001234`            |
| USD — 1 dollar = 100 cents | 2                 | `000000000123` (for 1.23) |
| JPY — no subdivision       | 0                 | `000000000001` (for 1)    |

Nothing cross-checks the digits against `currencyCode`. Getting it wrong sends
every amount off by a factor of ten and neither side will object.

### Receipt numbers

`stan` is the receipt number printed on the original's receipt, zero-padded to 6
digits. An operator types `215`; the wire carries `000215`.

---

## 4. Response

```json
{
  "responseCode": "00",
  "responseMessage": "Approved",
  "approved": true,
  "merchantReference": "A1B2C3D4E5F6",
  "terminalSerial": "P653200085189",
  "amount": "000000000216",
  "rrn": "622113155340",
  "authCode": "517842",
  "maskedPan": "543173xxxx5785",
  "ecrResponse": {}
}
```

| Field               | Description                                                                                       |
| ------------------- | ------------------------------------------------------------------------------------------------- |
| `approved`          | **Authoritative.** Whether money moved                                                            |
| `responseCode`      | The backend's own code, or one the terminal generated. See §6                                     |
| `responseMessage`   | The backend's error list where it gave one, otherwise its message                                 |
| `merchantReference` | Echoed from the request untouched. Also sent as `requestId` for ECRs built against the older name |
| `amount`            | What was actually **settled**, in minor units                                                     |
| `rrn`               | Retrieval reference number                                                                        |
| `authCode`          | Authorisation code. Absent on a void — see below                                                  |
| `maskedPan`         | Masked card number. Never the full number                                                         |
| `partialApproval`   | Present and `true` only when the bank authorised less than asked                                  |
| `requestedAmount`   | What was asked for. Present only with `partialApproval`                                           |
| `ecrResponse`       | The backend's answer verbatim — everything above is a convenience reading of it                   |

### Read `approved`, never `responseCode`

A partial approval that the operator then voids comes back carrying the bank's
own `00`, because the bank _did_ approve before the void undid it. A client that
decides from the code alone will book that as a sale.

```
approved: false, responseCode: "00"   ← happened; not a sale
```

### A void has no authorisation of its own

It reverses one. The terminal returns a placeholder rather than a real code, so
leave it off a void's receipt — printing it reads as an approval that never took
place.

---

## 5. Operations

### 5.1 Sale

The cardholder presents their card at the terminal.

**Request**

```json
{
  "version": 1,
  "messageType": "SALE",
  "merchantReference": "A1B2C3D4E5F6",
  "terminalSerial": "P653200085189",
  "currencyCode": "512",
  "transactionDateTime": "20260809172826",
  "ecrId": "ECR01",
  "amount": "000000000216"
}
```

**Response — approved**

```json
{
  "responseCode": "00",
  "responseMessage": "Approved",
  "approved": true,
  "merchantReference": "A1B2C3D4E5F6",
  "terminalSerial": "P653200085189",
  "amount": "000000000216",
  "rrn": "622113155340",
  "authCode": "517842",
  "maskedPan": "543173xxxx5785",
  "ecrResponse": {
    "success": true,
    "responseCode": "00",
    "message": "Approved",
    "data": {
      "transactionId": "318873a0-93f6-11f1-94a1-29b67ff8c66c",
      "amount": "0.216",
      "systemTraceNr": "000215",
      "batchId": "00000003",
      "rrn": "622113155340",
      "authCode": "517842",
      "cardMask": "543173xxxx5785",
      "isPartialApprove": false
    },
    "errorList": []
  }
}
```

**Response — declined**

```json
{
  "responseCode": "909",
  "responseMessage": "Insufficient funds",
  "approved": false,
  "merchantReference": "A1B2C3D4E5F6",
  "amount": "000000000216",
  "ecrResponse": {
    "success": false,
    "responseCode": "909",
    "message": "A business exception occurred",
    "errorList": ["Insufficient funds"]
  }
}
```

`responseMessage` prefers the backend's `errorList` over its `message`, because
the message is often a generic string or an internal exception blob while the
list is the sentence a cashier can act on.

**Response — partial approval**

```json
{
  "responseCode": "00",
  "responseMessage": "Approved",
  "approved": true,
  "amount": "000000000500",
  "partialApproval": true,
  "requestedAmount": "000000002000",
  "rrn": "622113155340",
  "authCode": "517842"
}
```

2.000 was asked for and 0.500 was taken. This is an **approval**: the goods can
go out only once the remaining 1.500 is collected by other means.

### 5.2 Void

Cancels an earlier transaction in full. No amount — a void returns exactly what
the original took, and only the terminal knows that figure.

**No card is presented**: for a transaction taken on the same terminal, the
terminal already holds the card data, so a void completes in about a second.

**Request**

```json
{
  "version": 1,
  "messageType": "VOID",
  "merchantReference": "B2C3D4E5F6A1",
  "terminalSerial": "P653200085189",
  "currencyCode": "512",
  "transactionDateTime": "20260809173100",
  "ecrId": "ECR01",
  "stan": "000215"
}
```

Add `"originalTerminalId": "31629"` to void a transaction taken on another
terminal. That route asks the backend and reads the card again.

**Response — refused before anything was sent**

```json
{
  "responseCode": "25",
  "responseMessage": "The original transaction is outside the void window",
  "approved": false,
  "merchantReference": "B2C3D4E5F6A1",
  "ecrResponse": {
    "success": false,
    "responseCode": "25",
    "message": "The original transaction is outside the void window",
    "data": null
  }
}
```

Voids are refused by the terminal for three reasons — the original was not found,
it is outside the void window configured in TMS, or the backend says it cannot be
voided (most often because it already was). Nothing reached the backend, so no
money moved.

### 5.3 Refund

Returns money against an earlier transaction, in full or in part. **The cardholder
presents their card** to receive it.

```json
{
  "version": 1,
  "messageType": "REFUND",
  "merchantReference": "C3D4E5F6A1B2",
  "terminalSerial": "P653200085189",
  "currencyCode": "512",
  "transactionDateTime": "20260809172826",
  "ecrId": "ECR01",
  "amount": "000000000216",
  "stan": "000208",
  "originalTransactionDate": "20260809"
}
```

The response is shaped exactly like a sale's. Whether the refund is allowed, and
for how much, is the backend's decision — the terminal passes the answer back
without adding rules of its own.

### 5.4 Inquiry

Asks what became of an earlier transaction. **Reads only**: no card, no money, no
change of any kind — so it is safe to repeat, and the terminal answers it **even
while it is taking a payment**.

That is the point of it. After a timeout, the outcome is unknown; this is how a
till finds out instead of retrying and risking a second charge.

**Request**

```json
{
  "version": 1,
  "messageType": "INQUIRY",
  "merchantReference": "D4E5F6A1B2C3",
  "terminalSerial": "P653200085189",
  "currencyCode": "512",
  "transactionDateTime": "20260809171259",
  "ecrId": "ECR01",
  "stan": "000208",
  "originalTransactionDate": "20260809"
}
```

**Response — found**

```json
{
  "responseCode": "00",
  "responseMessage": "Transaction found",
  "approved": true,
  "merchantReference": "D4E5F6A1B2C3",
  "terminalSerial": "P653200085189",
  "amount": "000000000258",
  "rrn": "7862802964726844904806",
  "maskedPan": "543173******5785",
  "ecrResponse": {
    "success": true,
    "responseCode": "00",
    "message": "Transaction found",
    "data": {
      "transactionId": "e970c800-93f1-11f1-9485-e7dd858253ff",
      "systemTraceNr": "000208",
      "transactionType": "Purchase",
      "transactionTypeDisplayName": "Purchase",
      "status": "Approved",
      "amount": 0.258,
      "authorizeAmount": 0.258,
      "isPartialApprove": false,
      "currencyId": "512",
      "transactionTime": "20260809165816",
      "cardMask": "543173******5785",
      "cardHolderName": null,
      "rrn": "7862802964726844904806",
      "authCode": null,
      "batchId": "00000003",
      "terminalId": 31629,
      "merchantId": 13593,
      "isRefunded": false,
      "canVoid": true,
      "canRefund": true
    }
  }
}
```

Four things to note:

- **`approved: true` means _found_, not _paid_.** What became of the transaction
  is `data.status`. An inquiry that finds a declined sale succeeded.
- **The field names are a sale's.** An inquiry answers with `systemTraceNr`,
  `cardMask` and `currencyId` — the same names a sale answers with — so a till
  reads one vocabulary and not two. Writing your own client, also accept the
  older spellings `stan`, `cardNumber` and `currency` on this message: terminals
  on earlier builds still send them, and an unrecognised name reads as absent
  rather than as an error, so the field simply goes blank.
- **Amounts inside `data` are in major units** (`0.258`) — that is the backend's
  own record. The outer `amount` is the wire's minor-unit encoding.
- **`canVoid` / `canRefund`** have already had the void window and the backend's
  rules applied, so a till can enable its buttons from them rather than guessing.

**Web Service / Hub answers.** Hub and Socket.IO results may mirror transaction
fields at the top level as well as (or instead of) nested `data`. A lookup that
found nothing may still carry correlation keys under `data`
(`merchantReference`, `terminalSerial`) with no transaction markers — that is
*not found*, not an empty found record. Clients (including this SDK) treat a
response as found only when markers such as `systemTraceNr` / `stan`,
`transactionId`, `status`, `rrn`, or `amount` are present.

**Response — not found**

```json
{
  "responseCode": "25",
  "responseMessage": "No transactions found for the provided STAN and Terminal",
  "approved": false,
  "merchantReference": "D4E5F6A1B2C3",
  "ecrResponse": {
    "success": false,
    "responseCode": "25",
    "message": "No transactions found for the provided STAN and Terminal",
    "data": null,
    "errorList": ["No transactions found for the provided STAN and Terminal"]
  }
}
```

### 5.5 Receipt

Fetches a transaction's e-receipt as a URL. The till renders it as a QR code; the
customer scans it and reads the receipt on their own phone, so a till with no
printer can still hand one over.

Non-financial and reprintable — ask as often as you like, including while the
terminal is busy.

**Request**

```json
{
  "version": 1,
  "messageType": "RECEIPT",
  "merchantReference": "E5F6A1B2C3D4",
  "terminalSerial": "P653200085189",
  "currencyCode": "512",
  "transactionDateTime": "20260809173655",
  "ecrId": "ECR01",
  "stan": "000215",
  "originalTransactionDate": "20260809"
}
```

**Response**

```json
{
  "responseCode": "00",
  "responseMessage": "Receipt ready",
  "approved": true,
  "merchantReference": "E5F6A1B2C3D4",
  "amount": "000000000216",
  "ecrResponse": {
    "success": true,
    "responseCode": "00",
    "message": "Receipt ready",
    "data": {
      "receiptUrl": "https://example.com/receipts/318873a0-93f6-11f1-94a1-29b67ff8c66c",
      "transactionId": "318873a0-93f6-11f1-94a1-29b67ff8c66c",
      "systemTraceNr": "000215"
    }
  }
}
```

An empty or absent `receiptUrl` means no receipt, whatever the response code
says. A receipt with no URL is not a receipt.

### 5.6 Close receipt

Asks the terminal to put its receipt away and return to its idle screen. Not
financial, safe to repeat, and answered whether or not a receipt was up. Send it
from the till's explicit Close receipt action, not when a dialog is merely
dismissed. Needs SDK 1.0.2 (`CloseReceiptAsync`).

**Request**: the common envelope only, with `"messageType": "CLOSE_RECEIPT"`. No
`amount`, `stan` or `originalTransactionDate`.

```json
{
  "version": 1,
  "messageType": "CLOSE_RECEIPT",
  "merchantReference": "F6A1B2C3D4E5",
  "terminalSerial": "P653200085189",
  "currencyCode": "512",
  "transactionDateTime": "20260809173800",
  "ecrId": "ECR01"
}
```

**Response**: read `success` (or `approved`) only. `true` means the terminal is idle.
`false` means it refused (typically `12` on firmware that predates the message type). Treat a refusal as "cannot be asked" and carry on.

---

## 6. Response codes

The terminal answers some requests itself, before anything reaches the backend —
so for these, **no money moved**:

| Code | Meaning                                                          | Money moved |
| ---- | ---------------------------------------------------------------- | ----------- |
| `12` | Unsupported message type, or one this terminal cannot perform    | No          |
| `13` | The amount is not valid                                          | No          |
| `17` | Cancelled at the terminal                                        | No          |
| `25` | The original transaction was not found                           | No          |
| `63` | Unsigned, signed with the wrong key, or the clock is too far out | No          |
| `91` | The transaction did not complete, or the terminal could not say  | **Unknown** |
| `94` | Already answered — this nonce has been used                      | **Ask**     |
| `96` | A transaction is already running on this terminal                | No          |

`63` means nothing was attempted, so there is nothing to reconcile. Seeing it
repeatedly almost always means the key on the till and the key on the terminal
do not match.

`94` is not a plain refusal: it means the terminal has answered this exact
message before, so the transaction it names probably **exists**. Inquire rather
than retry.

Anything else is the backend's own code passed through untouched — `00`
approved, `51` declined, `909`, `05`, and so on. Prefer `approved` over all of
them.

`96` is never returned for an inquiry or a receipt: reading changes nothing, so
the terminal answers those while busy.

---

## 7. Failure, and the one rule that matters

A response that never arrives is **not** a decline.

If the socket times out or the connection breaks, the terminal may have completed
the payment and been unable to say so — the card flow does not depend on the
socket staying up. Treating that as a refusal and retrying is how a customer gets
charged twice.

```
Timeout / connection lost  →  outcome UNKNOWN
                           →  send an INQUIRY for the same receipt number
                           →  the answer tells you whether to retry
```

This is what the inquiry operation is for, and why the terminal answers it while
busy.

**Which failures are which.** Only one of them is safe:

| What happened                                                                                             | Outcome          | SDK failure       |
| --------------------------------------------------------------------------------------------------------- | ---------------- | ----------------- |
| The TCP connect was refused, had no route, or timed out — **before** any byte was written                 | Nothing happened | `Unreachable`     |
| The request was written and no answer arrived within the read timeout                                     | **Unknown**      | `Timeout`         |
| The connection was reset, aborted or closed after the request was written, or part way through the answer | **Unknown**      | `ConnectionLost`  |
| An answer arrived that was empty or not JSON                                                              | **Unknown**      | `Malformed`       |
| An answer arrived unsigned, signed with the wrong key, or echoing another request's nonce                 | **Unknown**      | `Unauthenticated` |

A connect timeout is _not_ a read timeout: no request was sent, so there is nothing
to reconcile and no inquiry to make.

---

## 8. Implementing a client

The whole protocol in order:

1. **Frame** — 2-byte big-endian length, then UTF-8 JSON.
2. **Connect** to the terminal's IP on port 9100. Fail fast if it refuses;
   nothing has happened yet.
3. **Write** the request, flush.
4. **Read** the 2-byte header, then exactly that many bytes. Block for up to
   ~120 s; do not treat slowness as failure.
5. **Parse** the JSON. Read `approved` first, then `responseCode` for reporting.
6. **Convert** `amount` back to major units using the currency's digits.
7. **Close** the connection. One exchange per connection.

To test without hardware, the stand-in listener in this repository
([`tools/fake_pos_server.py`](../tools/fake_pos_server.py)) speaks the LAN
protocol. It does **not** sign its answers, so a client with a key configured will
(correctly) reject them — see the
[integration guide](integration-guide.md#12-testing-without-hardware):

```powershell
py tools\fake_pos_server.py --port 9100
```

---

## 9. Web Service REST

When TMS sets `ecrMode: 4`, the till does not open TCP to the terminal. It posts
JSON to the Amwal Hub over HTTPS. The C# SDK resolves the Hub base URL from
`EcrEnvironment` (SIT / UAT / PROD). **Hostnames and ports are SDK-internal and
are not published**; integrators only set the environment enum. Path suffixes are
fixed:

| Operation                    | Path               |
| ---------------------------- | ------------------ |
| Sale                         | `/Ecr/Sale`        |
| Void                         | `/Ecr/Void`        |
| Refund                       | `/Ecr/Refund`      |
| Transaction status (inquiry) | `/Ecr/Inquiry`     |
| Close receipt                | `/Ecr/closeReceipt` |

Each request is a JSON object matching the Hub contract for that path, plus
`messageType` / `originalTerminalId` for Hub correlation (see
[`WebService/EcrWebServiceMessage.cs`](../src/AmwalPay.Ecr/WebService/EcrWebServiceMessage.cs)):

| Field                       | Sale                                      | Void                      | Refund                    | Inquiry                        |
| --------------------------- | ----------------------------------------- | ------------------------- | ------------------------- | ------------------------------ |
| `version`                   | ✔                                         | ✔                         | ✔                         | ✔                              |
| `messageType`               | ✔                                         | ✔                         | ✔                         | ✔                              |
| `merchantId`                | ✔                                         | ✔                         | ✔                         | ✔                              |
| `terminalId`                | ✔                                         | ✔                         | ✔                         | ✔                              |
| `terminalSerial`            | ✔                                         | ✔                         | ✔                         | ✔                              |
| `ecrId`                     | ✔                                         | ✔                         | ✔                         | ✔                              |
| `merchantReference`         | ✔                                         | ✔                         | ✔                         | ✔                              |
| `currencyCode`              | ✔                                         | ✔                         | ✔                         | ✔                              |
| `transactionDateTime`       | ✔ (auto)                                  | ✔                         | ✔                         | ✔                              |
| `requestDateTime`           | ✔ (= `transactionDateTime`; Hub-required) | ✔                         | ✔                         | ✔                              |
| `amount`                    | ✔ (12-digit padded)                       |                           | ✔ (12-digit padded)       |                                |
| `stan`                      |                                           | ✔ (6-digit padded)        | ✔ (6-digit padded)        | ✔ (empty when by reference)    |
| `originalTransactionDate`   |                                           |                           | ✔                         | ✔                              |
| `originalMerchantReference` |                                           | ✔ (= `merchantReference`) | ✔ (= `merchantReference`) | ✔ when by reference; else `""` |
| `originalTerminalId`        |                                           | ✔ (= `terminalId`)        | ✔ (= `terminalId`)        | ✔ (= `terminalId`)             |
| `secureHashValue`           | when keyed                                | when keyed                | when keyed                | when keyed                     |

Not sent on Web Service (but used on LAN TCP): `nonce`. There is no Web Service
`RECEIPT` operation in this SDK.

### Close receipt (`/Ecr/closeReceipt`)

Post from the till's explicit Close receipt action (the same action as LAN
`CLOSE_RECEIPT`), not automatically after a sale / void / refund. The body has **no
`messageType` and no `currencyCode`**; it names the money operation that just
finished in `originalMerchantReference`, and is signed exactly like every other Hub
call. Needs SDK 1.0.2.

| Field | Value |
|---|---|
| `version` | `1` |
| `merchantId`, `terminalId`, `terminalSerial`, `ecrId` | As on the other operations |
| `merchantReference` | This close request's own id (generated when blank) |
| `originalMerchantReference` | **Required.** The sale / void / refund's `merchantReference` |
| `transactionDateTime`, `requestDateTime` | `yyyyMMddHHmmss`, equal |
| `secureHashValue` | When keyed |

The answer is a Hub envelope; only `success` is read.

### Transport errors on Web Service

The C# HTTP layer never throws for a network problem. When there is no response, an
empty body, a body that is not JSON, or a non-2xx status with nothing readable, it
synthesises a Hub-shaped envelope and reads it like any other answer:

```json
{
  "success": false,
  "responseCode": "NO_RESPONSE",
  "message": "Timed out after 2m 20s",
  "nextStep": "INQUIRE_BY_MERCHANT_REFERENCE"
}
```

`responseCode` is `NO_RESPONSE` for a timeout, no network or a broken connection, and
the HTTP status (`"502"`, ...) for an empty or unreadable reply. When the request may
have reached the Hub (no response, an empty body, a non-JSON body, or an HTTP 5xx
with no readable message) the envelope carries
`nextStep: INQUIRE_BY_MERCHANT_REFERENCE`. An HTTP 4xx with no readable message is
synthesised the same way **without** `nextStep`.

The C# SDK reads an answer with `nextStep: INQUIRE_BY_MERCHANT_REFERENCE` as an
unknown outcome and returns `EcrResult.Failed`: `Failure.Timeout` when
`responseCode` is `NO_RESPONSE` or the message contains "timeout", otherwise
`Failure.ConnectionLost`. For a transaction-status request carrying `nextStep` and no
`data` it returns `EcrInquiry.Failed` (`ConnectionLost`) rather than _not found_. A
close-receipt request has no unknown-outcome type; the same envelope reads as
`EcrReceiptClosed.Refused`. **A synthesised envelope with `nextStep` is an unknown
outcome, not a refusal.** A client implementing this protocol should key on
`nextStep` and "ask before retrying".

The whole request has one deadline of `ConnectTimeout + ResponseTimeout`. The SDK's
`EcrSessions.Plan` raises a default 120 s `ResponseTimeout` to 130 s for Web Service
(`EcrConfig.WebServiceResponseTimeout`), so the deadline is 140 s with defaults: the
Hub's wait includes card presentation and PIN entry.

### Signing (Web Service)

Field name: **`secureHashValue`** (not `secureHash`).

Algorithm matches LAN ECR:

1. Take every top-level scalar field **except** `secureHashValue`.
2. Sort by field name.
3. Join as `name=value`, separated by `&`.
4. HMAC-SHA256 under the **Web Service secret** (hex-decoded), uppercase hex.

Example signing string (hosted sale):

```
amount=1000&currencyCode=512&merchantId=13593&merchantReference=REF-1586&requestDateTime=20260816155749&terminalId=101311
```

The Web Service key is **not** the LAN/Wi‑Fi key. Amwal issues them separately.

Platform reference (`calcHash`) — the C# equivalent is the `Sign` and `Compose`
functions in [§3](#what-is-signed), with `secureHashValue` as the excluded field:

```javascript
function calcHash(obj, secret) {
  const crypted = CryptoJS.HmacSHA256(obj, CryptoJS.enc.Hex.parse(secret));
  return CryptoJS.enc.Hex.stringify(crypted).toUpperCase();
}
```

There is no per-request `nonce` on Web Service — replay protection is the Hub's
responsibility. When a key is configured, the SDK refuses to send if the hash
cannot be computed (fail-closed).

Responses are JSON envelopes from the Hub (`success`, `responseCode`, `message`,
`data`, ...). The SDK maps them to the same [EcrResult](api-reference.md#ecrresult)
types as LAN ECR. It does not verify a response signature on this transport.

### What the integrator configures

| Setting                     | Owner                            |
| --------------------------- | -------------------------------- |
| `merchantId`, `terminalId`  | App (from terminal registration) |
| Web Service secure hash key | App (from Amwal)                 |
| `EcrEnvironment`            | App                              |
| Hub hostname, port, paths   | SDK (internal)                   |

See the [integration guide](integration-guide.md#web-service) for C# usage.
