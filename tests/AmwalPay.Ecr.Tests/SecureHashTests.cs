using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AmwalPay.Ecr.Internal;
using Xunit;

namespace AmwalPay.Ecr.Tests
{
    public class SecureHashTests
    {
        private static readonly EcrConfig Config = EcrTestConfigs.Lan;
        private static string Key => Config.SecureHashKey;
        private static string OtherKey => EcrTestConfigs.LanOther.SecureHashKey;

        // ── The agreement with the terminal ───────────────────────────────

        [Fact]
        public void FieldsAreSignedInSortedOrder()
        {
            var json = new JsonObject
            {
                ["messageType"] = "SALE",
                ["amount"] = "000000001234",
                ["version"] = 1,
            };

            Assert.Equal("amount=000000001234&messageType=SALE&version=1", SecureHash.Compose(json));
        }

        [Fact]
        public void TheOrderFieldsArriveInDoesNotChangeTheSignature()
        {
            // The two sides build their JSON independently, so the canonical form
            // has to be insensitive to insertion order or nothing would verify.
            var one = new JsonObject { ["b"] = "2", ["a"] = "1" };
            var other = new JsonObject { ["a"] = "1", ["b"] = "2" };

            Assert.Equal(SecureHash.Compose(one), SecureHash.Compose(other));
        }

        [Fact]
        public void TheSortIsOrdinalNotCultureAware()
        {
            // The terminal sorts by code unit. "B" < "a" ordinally; a culture
            // comparison would put "a" first and every signature would mismatch.
            var json = new JsonObject { ["a"] = "1", ["B"] = "2", ["_c"] = "3", ["Z"] = "4" };

            Assert.Equal("B=2&Z=4&_c=3&a=1", SecureHash.Compose(json));
        }

        [Fact]
        public void TheSignatureFieldIsNeverPartOfWhatIsSigned()
        {
            var unsigned = new JsonObject { ["amount"] = "1" };
            var signed = new JsonObject { ["amount"] = "1", [SecureHash.Field] = "WHATEVER" };

            Assert.Equal(SecureHash.Compose(unsigned), SecureHash.Compose(signed));
        }

        [Fact]
        public void NestedDataIsLeftOutOfWhatIsSigned()
        {
            // `data` passes the backend's own record through, and two JSON
            // libraries cannot be relied on to write it identically.
            var json = new JsonObject
            {
                ["amount"] = "1",
                ["data"] = new JsonObject { ["transactionId"] = "x" },
                ["list"] = new JsonArray("a", "b"),
                ["missing"] = null,
            };

            Assert.Equal("amount=1", SecureHash.Compose(json));
        }

        [Fact]
        public void BooleansAndNumbersAreSignedAsTheyAreWritten()
        {
            var json = new JsonObject
            {
                ["success"] = true,
                ["responseCode"] = "00",
            };

            Assert.Equal("responseCode=00&success=true", SecureHash.Compose(json));
        }

        // ── Signing and verifying ─────────────────────────────────────────

        [Fact]
        public void ASignatureVerifiesAgainstTheKeyThatMadeIt()
        {
            var signed = Signed(new JsonObject { ["amount"] = "000000001234" }, Key);

            Assert.True(SecureHash.Verify(signed, Key));
        }

        [Fact]
        public void ASignatureDoesNotVerifyAgainstAnotherKey()
        {
            var signed = Signed(new JsonObject { ["amount"] = "000000001234" }, Key);

            Assert.False(SecureHash.Verify(signed, OtherKey));
        }

        [Fact]
        public void ChangingTheAmountInvalidatesTheSignature()
        {
            // The whole point: an attacker on the network cannot rewrite a figure.
            var hash = SecureHash.Sign(SecureHash.Compose(new JsonObject { ["amount"] = "000000001234" }), Key);
            var tampered = new JsonObject { ["amount"] = "000000009999", [SecureHash.Field] = hash };

            Assert.False(SecureHash.Verify(tampered, Key));
        }

        [Fact]
        public void AnUnsignedMessageNeverVerifies()
        {
            Assert.False(SecureHash.Verify(new JsonObject { ["amount"] = "1" }, Key));
        }

        [Fact]
        public void ASignatureIsCaseInsensitiveHexButNotAnythingElse()
        {
            var signed = Signed(new JsonObject { ["amount"] = "1" }, Key);
            var lower = new JsonObject { ["amount"] = "1", [SecureHash.Field] = signed.Str(SecureHash.Field).ToLowerInvariant() };
            var truncated = new JsonObject { ["amount"] = "1", [SecureHash.Field] = signed.Str(SecureHash.Field).Substring(0, 62) };

            Assert.True(SecureHash.Verify(lower, Key));
            Assert.False(SecureHash.Verify(truncated, Key));
        }

        [Fact]
        public void AKeyThatIsNotHexIsRefusedRatherThanReadAsText()
        {
            // Reading it as text would have both sides agree on nothing and report
            // every message as tampered with, which is a miserable thing to debug.
            Assert.Throws<ArgumentException>(() => SecureHash.Sign("a=1", "not-hex!!"));
            Assert.Throws<ArgumentException>(() => SecureHash.Sign("a=1", "abc"));
            Assert.Throws<ArgumentException>(() => SecureHash.Sign("a=1", ""));
        }

        // ── Known answers, computed outside this code base ────────────────
        // Expected values come from Python's hmac/hashlib and from RFC 4231, so a
        // bug in the SDK cannot also be in the oracle.

        [Fact]
        public void KnownAnswerForASignedSaleUnderTheWifiKey()
        {
            const string payload = "amount=000000001234&messageType=SALE&version=1";

            Assert.Equal(
                "9425F9125C871197E5D0A6B88E4F9E6E29D1E0E49C96AA3D88A5E34D6FB9D45A",
                SecureHash.Sign(payload, EcrTestConfigs.SecureHashKeyEcrWifi));
            Assert.Equal(
                "0DBE81E20B691AF85CE3A70E4047D6B10DC40514674E59876DF8200745AEA220",
                SecureHash.Sign(payload, EcrTestConfigs.SecureHashKeyEcrWifiOther));
        }

        [Fact]
        public void KnownAnswerForASignedResponseEnvelope()
        {
            var envelope = new JsonObject
            {
                ["success"] = true,
                ["responseCode"] = "00",
                ["message"] = "Approved",
                ["nonce"] = "82D83D6C05C1FBFA6AE72AE7C644D614",
                ["data"] = new JsonObject { ["merchantReference"] = "EA5C14BDB0A2" },
                ["errorList"] = new JsonArray(),
            };

            var payload = SecureHash.Compose(envelope);

            Assert.Equal("message=Approved&nonce=82D83D6C05C1FBFA6AE72AE7C644D614&responseCode=00&success=true", payload);
            Assert.Equal(
                "0E060F03F6A461735CB6C9A82AF3D3AD1EAB983359C2B7ED3D58E0AE76C15A41",
                SecureHash.Sign(payload, Key));
        }

        [Fact]
        public void Rfc4231TestCase1()
        {
            // key = 20 bytes of 0x0b, data = "Hi There"
            Assert.Equal(
                "B0344C61D8DB38535CA8AFCEAF0BF12B881DC200C9833DA726E9376C2E32CFF7",
                SecureHash.Sign("Hi There", "0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b"));
        }

        [Fact]
        public void Rfc4231TestCase2()
        {
            // key = "Jefe" (4a656665), data = "what do ya want for nothing?"
            Assert.Equal(
                "5BDCC146BF60754E6A042426089575C75A003F089D2739839DEC58B964EC3843",
                SecureHash.Sign("what do ya want for nothing?", "4a656665"));
        }

        [Fact]
        public void TheKeyIsDecodedFromHexNotUsedAsText()
        {
            // 32 hex characters are 16 key bytes. Using the characters themselves
            // as the key would give a different, wrong answer.
            var asHex = SecureHash.Sign("a=1", EcrTestConfigs.SecureHashKeyEcrWifi);
            using var textKey = new HMACSHA256(Encoding.UTF8.GetBytes(EcrTestConfigs.SecureHashKeyEcrWifi));
            var asText = SecureHash.ToHex(textKey.ComputeHash(Encoding.UTF8.GetBytes("a=1")));
            using var hexKey = new HMACSHA256(new byte[] { 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA });
            var expected = SecureHash.ToHex(hexKey.ComputeHash(Encoding.UTF8.GetBytes("a=1")));

            Assert.Equal(expected, asHex);
            Assert.NotEqual(asText, asHex);
        }

        // ── Nonces ────────────────────────────────────────────────────────

        [Fact]
        public void NoncesDoNotRepeat()
        {
            var seen = new HashSet<string>();
            for (var i = 0; i < 500; i++) seen.Add(SecureHash.NewNonce());

            Assert.Equal(500, seen.Count);
        }

        [Fact]
        public void ANonceIs128BitsOfUppercaseHex()
        {
            var nonce = SecureHash.NewNonce();

            Assert.Equal(32, nonce.Length);
            Assert.Matches("^[0-9A-F]{32}$", nonce);
        }

        // ── What the message builder actually produces ────────────────────

        [Fact]
        public void AMessageIsUnsignedWhenNoKeyIsConfigured()
        {
            var json = Build(new EcrConfig()).Json;

            Assert.False(json.ContainsKey(SecureHash.Field), "nothing to sign with");
            Assert.False(json.ContainsKey("nonce"), "a nonce without a signature could just be changed");
        }

        [Fact]
        public void AMessageIsSignedAndCarriesANonceWhenAKeyIsConfigured()
        {
            var json = Build(Config).Json;

            Assert.True(json.ContainsKey(SecureHash.Field));
            Assert.Equal(32, json.Str("nonce").Length);
            Assert.True(SecureHash.Verify(json, Key));
        }

        [Fact]
        public void ASignedMessagesSignatureIsIndependentlyReproducible()
        {
            // Recompute the signature with the BCL directly from the wire text, as
            // the terminal would after parsing what arrived.
            var wire = EcrJson.Write(Build(Config).Json);
            var parsed = Wire.Obj(wire);
            var pairs = new List<string>();
            foreach (var entry in parsed)
            {
                if (entry.Key == SecureHash.Field) continue;
                pairs.Add(entry.Key + "=" + entry.Value!.ToString().Trim('"'));
            }
            pairs.Sort(string.CompareOrdinal);

            using var hmac = new HMACSHA256(new byte[]
            {
                0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA,
            });
            var expected = SecureHash.ToHex(hmac.ComputeHash(Encoding.UTF8.GetBytes(string.Join("&", pairs))));

            Assert.Equal(expected, parsed.Str(SecureHash.Field));
        }

        [Fact]
        public void TwoIdenticalSalesAreSignedDifferently()
        {
            // Because each carries its own nonce: otherwise a captured message
            // would be indistinguishable from a fresh one.
            var first = Build(Config).Json;
            var second = Build(Config).Json;

            Assert.NotEqual(first.Str(SecureHash.Field), second.Str(SecureHash.Field));
        }

        [Fact]
        public void TheNonceSentIsTheOneReportedForCheckingTheAnswer()
        {
            var message = Build(Config);

            Assert.Equal(message.Json.Str("nonce"), message.Nonce);
        }

        [Fact]
        public void AnUnsignedMessageReportsAnEmptyNonce()
        {
            Assert.Equal("", Build(new EcrConfig()).Nonce);
        }

        [Fact]
        public void ChangingAnyFieldOfASignedRequestBreaksIt()
        {
            var json = Build(Config).Json;
            var tampered = (JsonObject)json.DeepClone();
            tampered["amount"] = "000000999999";

            Assert.True(SecureHash.Verify(json, Key));
            Assert.False(SecureHash.Verify(tampered, Key));
        }

        // ── Configuration ─────────────────────────────────────────────────

        [Theory]
        [InlineData("nothex")]
        [InlineData("abc")]
        [InlineData("aabb")]
        public void AMalformedKeyIsRefusedWhenTheConfigIsBuilt(string key)
        {
            // At construction, not at the first sale on a shop floor.
            Assert.Throws<ArgumentException>(() => new EcrConfig { SecureHashKey = key });
        }

        [Fact]
        public void AnEmptyKeyIsAllowedAndMeansUnsigned()
        {
            Assert.False(new EcrConfig { SecureHashKey = "" }.SignsMessages);
            Assert.True(Config.SignsMessages);
        }

        private static JsonObject Signed(JsonObject body, string key)
        {
            body[SecureHash.Field] = SecureHash.Sign(SecureHash.Compose(body), key);
            return body;
        }

        private static EcrMessage Build(EcrConfig config) =>
            EcrMessage.Build(
                type: EcrTransactionType.Sale,
                config: config,
                terminalSerial: "TW1",
                amount: 1.234m,
                originalStan: "",
                originalTerminalId: "",
                originalDate: "");
    }
}
