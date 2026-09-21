using System.Text.Json.Nodes;
using AmwalPay.Ecr.Internal;
using Xunit;

namespace AmwalPay.Ecr.Tests
{
    public class ResponseEnvelopeTests
    {
        private static readonly EcrConfig Config = EcrTestConfigs.Lan;
        private static string Key => Config.SecureHashKey;

        private static JsonObject SignedResponse(string nonce)
        {
            var body = new JsonObject
            {
                ["success"] = true,
                ["responseCode"] = "00",
                ["message"] = "Approved",
                ["data"] = new JsonObject
                {
                    ["merchantReference"] = "EA5C14BDB0A2",
                    ["terminalSerial"] = "QQW",
                    ["amount"] = "0.111",
                },
                ["errorList"] = new JsonArray(),
                ["nonce"] = nonce,
            };
            var hash = SecureHash.Sign(SecureHash.Compose(body), Key);
            body[SecureHash.Field] = hash;
            return body;
        }

        [Fact]
        public void TheUnifiedEnvelopeIsSignedFromTopLevelScalarsOnly()
        {
            var envelope = new JsonObject
            {
                ["success"] = true,
                ["responseCode"] = "00",
                ["message"] = "Approved",
                ["nonce"] = "ABC",
                ["data"] = new JsonObject
                {
                    ["merchantReference"] = "EA5C14BDB0A2",
                    ["terminalSerial"] = "QQW",
                },
                ["errorList"] = new JsonArray(),
            };

            Assert.Equal(
                "message=Approved&nonce=ABC&responseCode=00&success=true",
                SecureHash.Compose(envelope));
        }

        [Fact]
        public void EcrWireResponseReadsCorrelationFromData()
        {
            var envelope = EcrWireResponse.Parse(Wire.Obj(
                """
                {
                  "success": true,
                  "responseCode": "00",
                  "message": "Approved",
                  "data": {
                    "merchantReference": "EA5C14BDB0A2",
                    "terminalSerial": "QQW",
                    "amount": "0.111"
                  },
                  "errorList": [],
                  "nonce": "82D83D6C05C1FBFA6AE72AE7C644D614",
                  "secureHash": "E1979257BC406430CE91B6CD8F6EAC150386BF80E467838ECA7B970D96385FCE"
                }
                """));

            Assert.True(envelope.Success);
            Assert.Equal("EA5C14BDB0A2", envelope.MerchantReference);
            Assert.Equal("QQW", envelope.TerminalSerial);
            Assert.Equal("82D83D6C05C1FBFA6AE72AE7C644D614", envelope.Nonce);
            Assert.Equal(
                "E1979257BC406430CE91B6CD8F6EAC150386BF80E467838ECA7B970D96385FCE",
                envelope.SecureHash);
        }

        [Fact]
        public void ASignedAnswerVerifiesAndBindsToTheRequestNonce()
        {
            const string nonce = "82D83D6C05C1FBFA6AE72AE7C644D614";
            var answer = SignedResponse(nonce);

            Assert.True(SecureHash.Verify(answer, Key));
            Assert.Equal(nonce, answer.Str("nonce"));
        }

        [Fact]
        public void TamperingWithASignedTopLevelFieldInvalidatesTheSignature()
        {
            var answer = SignedResponse("82D83D6C05C1FBFA6AE72AE7C644D614");
            var tampered = (JsonObject)answer.DeepClone();
            tampered["message"] = "Declined";

            Assert.True(SecureHash.Verify(answer, Key));
            Assert.False(SecureHash.Verify(tampered, Key));
        }

        [Fact]
        public void ASignedAnswerSurvivesAWireRoundTrip()
        {
            // What the terminal signs is what we verify after the text has been
            // serialised and parsed again: numbers and booleans keep their spelling.
            var answer = SignedResponse("82D83D6C05C1FBFA6AE72AE7C644D614");

            var reparsed = Wire.Obj(EcrJson.Write(answer));

            Assert.True(SecureHash.Verify(reparsed, Key));
        }

        [Fact]
        public void ChangingTheNestedDataDoesNotInvalidateTheSignature()
        {
            // Documented limitation of the agreed format, pinned so it is not
            // mistaken for a regression: `data` is not covered by the signature.
            // The terminal-side contract is what protects `success`, `message` and
            // `nonce`, which are what the till acts on.
            var answer = SignedResponse("82D83D6C05C1FBFA6AE72AE7C644D614");
            var altered = (JsonObject)answer.DeepClone();
            ((JsonObject)altered["data"]!)["amount"] = "999.999";

            Assert.True(SecureHash.Verify(altered, Key));
        }
    }
}
