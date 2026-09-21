#!/usr/bin/env python3
"""Stand-in for the POS application's ECR listener.

Speaks the framing used by ecr-sdk (EcrMessageCodec): a 2-byte big-endian length
header followed by that many bytes of UTF-8 JSON.

    python tools/fake_pos_server.py --port 9100
    python tools/fake_pos_server.py --port 9100 --decline 51 --delay 3
    python tools/fake_pos_server.py --port 9100 --not-found
"""

import argparse
import json
import random
import socket
import socketserver
import time


def read_exactly(sock, count):
    buffer = b""
    while len(buffer) < count:
        chunk = sock.recv(count - len(buffer))
        if not chunk:
            raise ConnectionError("ECR closed the connection")
        buffer += chunk
    return buffer


class EcrHandler(socketserver.BaseRequestHandler):

    def handle(self):
        sock = self.request
        try:
            length = int.from_bytes(read_exactly(sock, 2), "big")
            request = json.loads(read_exactly(sock, length).decode("utf-8"))
        except (ConnectionError, ValueError) as error:
            print(f"[{self.client_address[0]}] bad request: {error}")
            return

        print(f"[{self.client_address[0]}] --> {json.dumps(request)}")

        if self.server.delay:
            time.sleep(self.server.delay)

        message_type = request.get("messageType")
        if message_type == "INQUIRY":
            response = self.inquiry_response(request)
        elif message_type == "RECEIPT":
            response = self.receipt_response(request)
        else:
            response = self.transaction_response(request)

        body = json.dumps(response).encode("utf-8")
        sock.sendall(len(body).to_bytes(2, "big") + body)
        print(f"[{self.client_address[0]}] <-- {json.dumps(response)}")

    def transaction_response(self, request):
        code = self.server.decline_code or "00"
        response = {
            "responseCode": code,
            "responseMessage": "APPROVED" if code == "00" else "DECLINED",
            "approved": code == "00",
            "requestId": request.get("requestId", ""),
            "terminalSerial": request.get("terminalSerial", ""),
            "amount": request.get("amount", ""),
        }
        if code == "00":
            response.update({
                "rrn": f"{random.randrange(10 ** 11, 10 ** 12)}",
                "authCode": f"{random.randrange(100000, 999999)}",
                "maskedPan": "483312******1234",
                "cardScheme": "VISA",
            })
        return response

    def receipt_response(self, request):
        """Publishes an e-receipt. --not-found refuses to produce one."""
        if self.server.inquiry_not_found:
            return {
                "responseCode": "91",
                "responseMessage": "The receipt could not be generated",
                "approved": False,
                "requestId": request.get("requestId", ""),
                "terminalSerial": request.get("terminalSerial", ""),
                "ecrResponse": {"success": False, "data": None},
            }

        return {
            "responseCode": "00",
            "responseMessage": "Receipt ready",
            "approved": True,
            "requestId": request.get("requestId", ""),
            "terminalSerial": request.get("terminalSerial", ""),
            "ecrResponse": {
                "success": True,
                "responseCode": "00",
                "message": "Receipt ready",
                "data": {
                    "receiptUrl": (
                        "https://example.invalid/receipt?stan="
                        + request.get("stan", "")
                    ),
                    "transactionId": "e970c800-93f1-11f1-9485-e7dd858253ff",
                    "stan": request.get("stan", ""),
                },
            },
        }

    def inquiry_response(self, request):
        """Answers a lookup. --not-found makes every receipt number unknown."""
        stan = request.get("stan", "")

        if self.server.inquiry_not_found:
            return {
                "responseCode": "25",
                "responseMessage": (
                    "No transactions found for the provided STAN and Terminal"
                ),
                "approved": False,
                "requestId": request.get("requestId", ""),
                "terminalSerial": request.get("terminalSerial", ""),
                "ecrResponse": {"success": False, "data": None},
            }

        return {
            "responseCode": "00",
            "responseMessage": "Transaction found",
            # Found, not paid: what became of the transaction is `status` below.
            "approved": True,
            "requestId": request.get("requestId", ""),
            "terminalSerial": request.get("terminalSerial", ""),
            "amount": "000000000258",
            "ecrResponse": {
                "success": True,
                "responseCode": "00",
                "message": "Transaction found",
                "data": {
                    "transactionId": "e970c800-93f1-11f1-9485-e7dd858253ff",
                    "stan": stan,
                    "transactionType": "Purchase",
                    "transactionTypeDisplayName": "Purchase",
                    "status": "Approved",
                    # The backend records major units, unlike the wire.
                    "amount": 0.258,
                    "totalAmount": 0.258,
                    "currency": "OMR",
                    "transactionTime": "2026-08-09T16:58:16.804271",
                    "cardNumber": "483312******1234",
                    "cardHolderName": None,
                    "rrn": "7862802964726844904806",
                    "authCode": None,
                    "batchId": "00000003",
                    "terminalId": request.get("originalTerminalId", "31629"),
                    "isRefunded": False,
                    "canVoid": True,
                    "canRefund": True,
                },
            },
        }


class EcrServer(socketserver.ThreadingTCPServer):
    allow_reuse_address = True

    def __init__(self, address, handler, decline_code, delay, inquiry_not_found):
        super().__init__(address, handler)
        self.decline_code = decline_code
        self.delay = delay
        self.inquiry_not_found = inquiry_not_found


def main():
    parser = argparse.ArgumentParser(description="Fake POS ECR listener")
    parser.add_argument("--host", default="0.0.0.0")
    parser.add_argument("--port", type=int, default=9100)
    parser.add_argument("--decline", dest="decline_code", default=None,
                        help="respond with this code instead of 00, e.g. 51")
    parser.add_argument("--delay", type=float, default=0,
                        help="seconds to wait before replying, to mimic card entry")
    parser.add_argument("--not-found", dest="inquiry_not_found", action="store_true",
                        help="answer every inquiry with 25, transaction not found")
    args = parser.parse_args()

    try:
        lan_ip = socket.gethostbyname(socket.gethostname())
    except OSError:
        # The machine's own hostname does not always resolve (VPNs, containers).
        lan_ip = "127.0.0.1"
    print(f"Listening on {args.host}:{args.port} (register {lan_ip}:{args.port} in the app)")

    with EcrServer((args.host, args.port), EcrHandler, args.decline_code,
                   args.delay, args.inquiry_not_found) as server:
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            print("\nStopped")


if __name__ == "__main__":
    main()
