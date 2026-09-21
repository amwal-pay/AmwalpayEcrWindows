using System.Text.Json.Nodes;
using AmwalPay.Ecr.Internal;
using Xunit;

namespace AmwalPay.Ecr.Tests
{
    /// <summary>
    /// An inquiry reports on a transaction rather than performing one, so what it
    /// reads has to survive the trip intact: the payloads here are the terminal's
    /// real answers, field for field.
    /// </summary>
    public class InquiryReadingTests
    {
        private static JsonObject Json(string text) => Wire.Obj(text);

        /// <summary>What the terminal sends back for a transaction its backend knows.</summary>
        private static JsonObject Found() => Json(
            """
            {
              "success": true,
              "responseCode": "00",
              "message": "Transaction found",
              "data": {
                "merchantReference": "A1B2C3D4E5F6",
                "terminalSerial": "P653200085189",
                "transactionId": "e970c800-93f1-11f1-9485-e7dd858253ff",
                "stan": "000208",
                "systemTraceNr": "000208",
                "transactionType": "Purchase",
                "transactionTypeDisplayName": "Purchase",
                "status": "Approved",
                "amount": "0.258",
                "totalAmount": "0.258",
                "currency": "OMR",
                "currencyId": "512",
                "transactionTime": "2026-08-09T16:58:16.804271",
                "cardNumber": "543173******5785",
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
              },
              "errorList": []
            }
            """);

        private static EcrTransaction TransactionOf(JsonObject answer) =>
            Assert.IsType<EcrInquiry.Found>(answer.ToInquiry("R", 3)).Transaction;

        [Fact]
        public void AFoundTransactionIsReadFieldForField()
        {
            var inquiry = Found().ToInquiry("A1B2C3D4E5F6", 3);

            var transaction = Assert.IsType<EcrInquiry.Found>(inquiry).Transaction;
            Assert.Equal("e970c800-93f1-11f1-9485-e7dd858253ff", transaction.TransactionId);
            Assert.Equal("000208", transaction.Stan);
            Assert.Equal("Purchase", transaction.Type);
            Assert.Equal("Approved", transaction.Status);
            Assert.Equal("OMR", transaction.Currency);
            Assert.Equal("543173******5785", transaction.MaskedPan);
            Assert.Equal("00000003", transaction.BatchId);
            Assert.Equal("31629", transaction.TerminalId);
        }

        [Fact]
        public void TheBackendsAmountIsAlreadyInMajorUnitsAndIsNotShiftedAgain()
        {
            var transaction = TransactionOf(Found());

            Assert.Equal("0.258", transaction.Amount);
            Assert.Equal("0.258", transaction.TotalAmount);
        }

        [Fact]
        public void WhatTheTillMayStillDoWithItComesThroughAsItWasDecided()
        {
            var transaction = TransactionOf(Found());

            Assert.True(transaction.CanVoid);
            Assert.True(transaction.CanRefund);
            Assert.False(transaction.IsRefunded);
        }

        [Fact]
        public void AFieldTheBackendLeftNullReadsAsAbsentNotAsTheWordNull()
        {
            var transaction = TransactionOf(Found());

            Assert.Equal("", transaction.AuthCode);
            Assert.Equal("", transaction.CardHolderName);
        }

        [Fact]
        public void NothingFoundCarriesTheTerminalsReasonRatherThanAnEmptyRecord()
        {
            var answer = Json(
                """
                {
                  "success": false,
                  "responseCode": "25",
                  "message": "No transactions found for the provided STAN and Terminal",
                  "data": null,
                  "errorList": []
                }
                """);

            var inquiry = Assert.IsType<EcrInquiry.NotFound>(answer.ToInquiry("R", 3));
            Assert.Equal("No transactions found for the provided STAN and Terminal", inquiry.Reason);
        }

        [Fact]
        public void NothingFoundWithNoWordsGetsADefaultReason()
        {
            var inquiry = Assert.IsType<EcrInquiry.NotFound>(Json("""{"success":false,"data":null}""").ToInquiry("R", 3));

            Assert.Equal("Transaction not found", inquiry.Reason);
        }

        [Fact]
        public void AnAnswerClaimingSuccessWithoutATransactionIsNotTreatedAsFound()
        {
            var answer = Json("""{ "approved": true, "responseMessage": "Odd" }""");

            Assert.IsType<EcrInquiry.NotFound>(answer.ToInquiry("R", 3));
        }

        [Fact]
        public void AReceiptIsTheUrlTheTerminalPublishedReadyForAQrCode()
        {
            var answer = Json(
                """
                {
                  "responseCode": "00",
                  "responseMessage": "Receipt ready",
                  "approved": true,
                  "ecrResponse": {
                    "success": true,
                    "data": {
                      "receiptUrl": "https://test.amwalpg.com:25446/Transaction/DownloadReceipt?transactionId=318873a0",
                      "stan": "000215"
                    }
                  }
                }
                """);

            var receipt = Assert.IsType<EcrReceipt.Ready>(answer.ToReceipt("R"));
            Assert.Equal(
                "https://test.amwalpg.com:25446/Transaction/DownloadReceipt?transactionId=318873a0",
                receipt.Url);
        }

        [Fact]
        public void AReceiptTheBackendWouldNotPublishCarriesItsReasonNotAnEmptyUrl()
        {
            var answer = Json(
                """
                {
                  "responseCode": "91",
                  "responseMessage": "The receipt could not be generated",
                  "approved": false,
                  "ecrResponse": { "success": false, "data": null }
                }
                """);

            var receipt = Assert.IsType<EcrReceipt.Unavailable>(answer.ToReceipt("R"));
            Assert.Equal("The receipt could not be generated", receipt.Reason);
        }

        [Fact]
        public void ARecordWithNoStatusOfItsOwnTakesItFromTheEnvelope()
        {
            // A terminal replaying its own log of a sale answers with the sale's
            // fields, and "what became of it" was never one of them. A blank status
            // reads as a failure on a till, next to data that plainly is not one.
            var replayed = Json(
                """
                {
                  "responseCode": "00",
                  "responseMessage": "Approved",
                  "approved": true,
                  "amount": "000000001478",
                  "rrn": "622911288892",
                  "ecrResponse": {
                    "success": true,
                    "data": {
                      "transactionId": "20ff0b20-9a2c-11f1-9567-a1cfea74f694",
                      "systemTraceNr": "000149",
                      "actionCode": "00",
                      "amount": "1.478",
                      "transactionTypeDisplayName": "Purchase"
                    }
                  }
                }
                """);

            var inquiry = Assert.IsType<EcrInquiry.Found>(replayed.ToInquiry("R", 3));
            Assert.Equal("Approved", inquiry.Transaction.Status);
        }

        [Fact]
        public void AnAnswerTheTerminalReplayedFromItsOwnRecordReadsLikeAnyOther()
        {
            // The terminal answers a lookup in one shape whether it read the record
            // from the backend or from its own log of the sale: it translates
            // before it replies. This is that shape, so nothing here is special.
            var replayed = Json(
                """
                {
                  "responseCode": "00",
                  "approved": true,
                  "ecrResponse": {
                    "success": true,
                    "data": {
                      "stan": "000149",
                      "systemTraceNr": "000149",
                      "cardNumber": "543173xxxx5785",
                      "status": "Approved",
                      "amount": "1.478",
                      "rrn": "622911288892",
                      "transactionTime": "2026-08-17T15:09:53",
                      "canVoid": false,
                      "canRefund": false
                    }
                  }
                }
                """);

            var inquiry = Assert.IsType<EcrInquiry.Found>(replayed.ToInquiry("R", 3));
            Assert.Equal("000149", inquiry.Transaction.Stan);
            Assert.Equal("543173xxxx5785", inquiry.Transaction.MaskedPan);
            Assert.Equal("Approved", inquiry.Transaction.Status);
            Assert.False(inquiry.Transaction.CanVoid);
        }

        [Fact]
        public void ARefusedEnvelopeCarryingARecordIsFoundAndDeclined()
        {
            var refused = Json(
                """
                {
                  "responseCode": "51",
                  "responseMessage": "Declined",
                  "approved": false,
                  "ecrResponse": { "success": false, "data": { "amount": "1.478" } }
                }
                """);

            // success reports the transaction's outcome, not the lookup's, so a
            // refused envelope that still carries a record means the terminal found
            // the transaction and it was declined. Reading that as NotFound told a
            // till the terminal had never heard of a payment it knew all about, and
            // a till told that charges again.
            //
            // What is genuinely not found carries no data at all.
            var found = Assert.IsType<EcrInquiry.Found>(refused.ToInquiry("R", 3));
            Assert.Equal("Declined", found.Transaction.Status);
        }

        [Fact]
        public void APartialApprovalReportsBothAmountsAndSaysItWasPartial()
        {
            // Seen on terminal 33527: a sale for 961.100 the issuer cut to 50.000.
            // A till that shows `amount` here is showing money that never moved.
            var partial = Json(
                """
                {
                  "success": true,
                  "responseCode": "00",
                  "message": "Approved",
                  "data": {
                    "stan": "000106",
                    "status": "Approved",
                    "amount": 961.1,
                    "authorizeAmount": 50.0,
                    "isPartialApprove": true
                  }
                }
                """);

            var found = Assert.IsType<EcrInquiry.Found>(partial.ToInquiry("R", 3));

            Assert.True(found.Transaction.PartialApproval);
            Assert.Equal("961.1", found.Transaction.Amount);
            Assert.Equal("50.0", found.Transaction.AuthorizedAmount);
        }

        [Fact]
        public void AnOrdinaryApprovalIsNotReportedAsPartial()
        {
            var ordinary = Json(
                """
                {
                  "success": true,
                  "responseCode": "00",
                  "message": "Approved",
                  "data": { "stan": "000107", "status": "Approved", "amount": 1.0 }
                }
                """);

            var found = Assert.IsType<EcrInquiry.Found>(ordinary.ToInquiry("R", 3));

            Assert.False(found.Transaction.PartialApproval);
            Assert.Equal("1.0", found.Transaction.AuthorizedAmount);
        }

        [Fact]
        public void ADeclinedTransactionTheTerminalKnowsAboutIsFound()
        {
            // The case that sent a till to "Not Found" while the terminal had just
            // answered with the whole record.
            var declined = Json(
                """
                {
                  "success": false,
                  "responseCode": "51",
                  "message": "Declined",
                  "data": {
                    "stan": "000013",
                    "status": "Declined",
                    "amount": 961.1,
                    "rrn": "624408152411"
                  }
                }
                """);

            var found = Assert.IsType<EcrInquiry.Found>(declined.ToInquiry("R", 3));

            Assert.Equal("Declined", found.Transaction.Status);
            Assert.Equal("624408152411", found.Transaction.Rrn);
        }

        [Fact]
        public void AnAnswerWithNoRecordAtAllIsNotFound()
        {
            // The only thing that means not found: nothing came back to describe.
            var missing = Json(
                """
                {
                  "success": false,
                  "responseCode": "25",
                  "message": "Transaction not found",
                  "data": null
                }
                """);

            Assert.IsType<EcrInquiry.NotFound>(missing.ToInquiry("R", 3));
        }

        [Fact]
        public void TheAnswersOwnReferenceWinsOverTheOneTheInquiryWasSentWith()
        {
            var inquiry = Found().ToInquiry("SENT", 3);

            Assert.Equal("A1B2C3D4E5F6", inquiry.MerchantReference);
            Assert.Equal("SENT", Json("""{"data":null}""").ToInquiry("SENT", 3).MerchantReference);
        }

        [Fact]
        public void TheRawAnswerIsKeptForAnythingNotSurfaced()
        {
            var found = Assert.IsType<EcrInquiry.Found>(Found().ToInquiry("R", 3));

            Assert.Contains("\"transactionId\":\"e970c800-93f1-11f1-9485-e7dd858253ff\"", found.Raw);
        }

        [Fact]
        public void AFoundRecordWithNeitherAmountNorAuthorisedAmountFallsBackToTheEnvelopesMinorUnits()
        {
            var replayed = Json(
                """{"success":true,"amount":"000000001478","data":{"stan":"1","status":"Approved"}}""");

            var transaction = TransactionOf(replayed);

            Assert.Equal("1.478", transaction.Amount);
        }
    }
}
