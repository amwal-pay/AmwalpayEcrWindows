using Xunit;

namespace AmwalPay.Ecr.Tests
{
    public class EcrWireResponseTests
    {
        [Fact]
        public void ParsesTheUnifiedEnvelope()
        {
            var envelope = EcrWireResponse.Parse(Wire.Obj(
                """
                {
                  "success": true,
                  "responseCode": "00",
                  "message": "Approved",
                  "data": {
                    "merchantReference": "EA5C14BDB0A2",
                    "amount": "0.111",
                    "cardMask": "543173xxxx5785"
                  },
                  "errorList": []
                }
                """));

            Assert.True(envelope.Success);
            Assert.Equal("EA5C14BDB0A2", envelope.MerchantReference);
            Assert.Equal("0.111", envelope.Data?["amount"]?.ToString());
            Assert.Empty(envelope.ErrorList);
        }

        [Fact]
        public void ReadsNonceAndSecureHashFromTheWireEnvelope()
        {
            var envelope = EcrWireResponse.Parse(Wire.Obj(
                """
                {
                  "success": true,
                  "responseCode": "00",
                  "message": "Approved",
                  "data": {
                    "merchantReference": "EA5C14BDB0A2",
                    "terminalSerial": "QQW"
                  },
                  "errorList": [],
                  "nonce": "82D83D6C05C1FBFA6AE72AE7C644D614",
                  "secureHash": "ABC123"
                }
                """));

            Assert.Equal("82D83D6C05C1FBFA6AE72AE7C644D614", envelope.Nonce);
            Assert.Equal("ABC123", envelope.SecureHash);
            Assert.Equal("QQW", envelope.TerminalSerial);
        }

        [Fact]
        public void NormalisesLegacyFlatAnswers()
        {
            var envelope = EcrWireResponse.Parse(Wire.Obj(
                """
                {
                  "responseCode": "00",
                  "responseMessage": "Approved",
                  "approved": true,
                  "amount": "000000001111"
                }
                """));

            Assert.True(envelope.Success);
            Assert.Equal("Approved", envelope.Message);
        }

        [Fact]
        public void NormalisesLegacyNestedEcrResponse()
        {
            var envelope = EcrWireResponse.Parse(Wire.Obj(
                """
                {
                  "approved": true,
                  "ecrResponse": {
                    "success": true,
                    "responseCode": "00",
                    "message": "Approved",
                    "data": { "rrn": "623807390109" },
                    "errorList": []
                  }
                }
                """));

            Assert.True(envelope.Success);
            Assert.Equal("623807390109", envelope.Data?["rrn"]?.ToString());
        }

        [Fact]
        public void ReadsErrorListFromTheEnvelope()
        {
            var envelope = EcrWireResponse.Parse(Wire.Obj(
                """
                {
                  "success": false,
                  "responseCode": "909",
                  "message": "Declined",
                  "data": null,
                  "errorList": ["Insufficient funds"]
                }
                """));

            Assert.False(envelope.Success);
            Assert.Equal("Declined", envelope.Message);
            Assert.Equal(new[] { "Insufficient funds" }, envelope.ErrorList);
            Assert.Equal("Insufficient funds", envelope.DisplayMessage);
        }

        [Fact]
        public void DisplayMessageFromRawPrefersErrorListOverWireMessage()
        {
            var message = EcrWireResponse.DisplayMessageFromRaw(
                """
                {
                  "success": false,
                  "responseCode": "909",
                  "message": "Declined",
                  "data": null,
                  "errorList": ["Insufficient funds"]
                }
                """,
                fallback: "Declined");

            Assert.Equal("Insufficient funds", message);
        }

        [Fact]
        public void MerchantReferenceFallsBackToLegacyMerchantReferenceId()
        {
            var envelope = EcrWireResponse.Parse(Wire.Obj(
                """
                {
                  "success": true,
                  "responseCode": "00",
                  "message": "Approved",
                  "data": {"merchantReferenceId": "LEGACY-1"},
                  "errorList": []
                }
                """));

            Assert.Equal("LEGACY-1", envelope.MerchantReference);
        }

        [Fact]
        public void MerchantReferencePrefersMerchantReferenceOverLegacyKeys()
        {
            var envelope = EcrWireResponse.Parse(Wire.Obj(
                """
                {
                  "success": true,
                  "responseCode": "00",
                  "message": "Approved",
                  "data": {
                    "merchantReference": "CURRENT",
                    "merchantReferenceId": "LEGACY",
                    "requestId": "OLDER"
                  },
                  "errorList": []
                }
                """));

            Assert.Equal("CURRENT", envelope.MerchantReference);
        }

        [Fact]
        public void MerchantReferenceFallsBackToRequestIdLast()
        {
            var envelope = EcrWireResponse.Parse(Wire.Obj(
                """{"success":true,"data":{"requestId":"OLDER"}}"""));

            Assert.Equal("OLDER", envelope.MerchantReference);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("[1]")]
        [InlineData("null")]
        public void DisplayMessageFromRawFallsBackWhenTheTextIsUnusable(string raw)
        {
            Assert.Equal("fallback", EcrWireResponse.DisplayMessageFromRaw(raw, "fallback"));
        }

        [Fact]
        public void DisplayMessageFromRawFallsBackWhenTheAnswerHasNoText()
        {
            Assert.Equal("fallback", EcrWireResponse.DisplayMessageFromRaw("""{"success":true}""", "fallback"));
        }

        [Fact]
        public void ErrorListEntriesAreTrimmedAndBlankOnesDropped()
        {
            var envelope = EcrWireResponse.Parse(Wire.Obj(
                """{"success":false,"errorList":["  a  ","","   ",{"x":1},"b"]}"""));

            Assert.Equal(new[] { "a", "b" }, envelope.ErrorList);
            Assert.Equal("a\nb", envelope.DisplayMessage);
        }

        [Fact]
        public void ASuccessFieldThatIsNotABooleanFallsBackToApprovedThenToTheResponseCode()
        {
            Assert.True(EcrWireResponse.Parse(Wire.Obj("""{"success":"yes","approved":true}""")).Success);
            Assert.True(EcrWireResponse.Parse(Wire.Obj("""{"success":"yes","responseCode":"00"}""")).Success);
            Assert.False(EcrWireResponse.Parse(Wire.Obj("""{"success":"yes","responseCode":"51"}""")).Success);
            Assert.False(EcrWireResponse.Parse(Wire.Obj("""{}""")).Success);
        }
    }
}
