using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AmwalPay.Ecr.Internal;
using Xunit;

namespace AmwalPay.Ecr.Tests
{
    /// <summary>
    /// The C# port serialises with System.Text.Json, which differs from
    /// kotlinx.serialization in two ways that reach the wire: how text is
    /// escaped, and how a number or boolean is read back as text for signing.
    /// Both are pinned here.
    /// </summary>
    public class WireEncodingTests
    {
        // ── What goes on the wire is plain UTF-8, not \u-escaped ──────────

        [Fact]
        public void NonAsciiTextIsWrittenAsItselfNotAsUnicodeEscapes()
        {
            var json = new JsonObject { ["terminalSerial"] = "Café ☕ تمت" };

            var text = EcrJson.Write(json);

            Assert.Equal("""{"terminalSerial":"Café ☕ تمت"}""", text);
            Assert.DoesNotContain("\\u", text);
        }

        [Fact]
        public void CharactersOutsideTheBasicMultilingualPlaneAreValidJsonEscapesThatRoundTrip()
        {
            // Known difference from the Kotlin SDK: System.Text.Json's relaxed
            // encoder covers the BMP only, so an emoji goes out as a surrogate-pair
            // escape instead of raw UTF-8. It is the same JSON value to the terminal
            // and the signature is composed from the value, not the spelling.
            var json = new JsonObject { ["ecrId"] = "till-\U0001F600" };

            var text = EcrJson.Write(json);

            Assert.Equal("till-\U0001F600", Wire.Obj(text).Str("ecrId"));
        }

        [Fact]
        public void AngleBracketsAmpersandAndPlusAreNotEscaped()
        {
            var json = new JsonObject { ["ecrId"] = "<till>&'+`" };

            var text = EcrJson.Write(json);

            Assert.Equal("""{"ecrId":"<till>&'+`"}""", text);
        }

        [Fact]
        public void QuotesBackslashesAndControlCharactersAreStillEscapedSoTheJsonIsValid()
        {
            var original = "say \"hi\" \\ done\n\tend";
            var json = new JsonObject { ["ecrId"] = original };

            var text = EcrJson.Write(json);

            Assert.Contains("\\\"hi\\\"", text);
            Assert.Contains("\\\\", text);
            Assert.Contains("\\n", text);
            Assert.Equal(original, Wire.Obj(text).Str("ecrId"));
        }

        [Fact]
        public async Task ARequestOnTheWireCarriesTheRawUtf8BytesOfNonAsciiFields()
        {
            var channel = FakeChannel.Always(null);
            var config = new EcrConfig { EcrId = "Tïll-<1>&", AutoInquireOnFailure = false };
            var terminal = new EcrTerminal(channel, "Café ☕", config);

            await terminal.SaleAsync(1m, "ORD-1");

            var request = Assert.Single(channel.Requests);
            Assert.Contains("\"terminalSerial\":\"Café ☕\"", request);
            Assert.Contains("\"ecrId\":\"Tïll-<1>&\"", request);
            Assert.DoesNotContain("\\u", request);
        }

        [Fact]
        public void TheBodyBytesAreUtf8WithoutABom()
        {
            var message = EcrMessage.Build(
                EcrTransactionType.Sale,
                new EcrConfig { EcrId = "É" },
                "TW1",
                1m,
                "",
                "",
                "");

            var packet = EcrMessageCodec.Encode(message);

            Assert.Equal((byte)'{', packet[2]);
            Assert.Contains(new byte[] { 0xC3, 0x89 }, Chunks(packet), new SequenceComparer());
        }

        [Fact]
        public async Task ASignatureStillVerifiesWhenTheFieldsCarryNonAsciiAndMarkup()
        {
            // Signing composes from the field's text, not from its escaped form; the
            // terminal parses the wire JSON and composes the same string.
            var channel = FakeChannel.Always(null);
            var config = EcrTestConfigs.Lan with { EcrId = "Tïll <&> \"1\"", AutoInquireOnFailure = false };
            var terminal = new EcrTerminal(channel, "Café ☕", config);

            await terminal.SaleAsync(1m, "ORD-1");

            var parsed = Wire.Obj(Assert.Single(channel.Requests));
            Assert.Equal("Tïll <&> \"1\"", parsed.Str("ecrId"));
            Assert.True(SecureHash.Verify(parsed, config.SecureHashKey));
        }

        [Fact]
        public void AnAnswerWithEscapedUnicodeIsReadAsTheTextItSpells()
        {
            // Written with explicit \\u escapes in the JSON text itself.
            var answer = Wire.Obj("{\"success\":false,\"message\":\"caf\\u00e9 \\u062a\\u0645\",\"responseCode\":\"05\"}");

            Assert.Equal("café تم", answer.Str("message"));
            Assert.Equal("café تم", EcrWireResponse.Parse(answer).Message);
        }

        [Fact]
        public void ASignatureOverEscapedUnicodeVerifiesAgainstTheDecodedText()
        {
            // The terminal may escape non-ASCII when it writes; it signs the text.
            var signature = SecureHash.Sign("message=café&nonce=N1", EcrTestConfigs.SecureHashKeyEcrWifi);
            var wire = "{\"message\":\"caf\\u00e9\",\"nonce\":\"N1\",\"secureHash\":\"" + signature + "\"}";

            Assert.True(SecureHash.Verify(Wire.Obj(wire), EcrTestConfigs.SecureHashKeyEcrWifi));
        }

        // ── Numbers and booleans are read back as their written text ──────

        [Fact]
        public void ANumberParsedFromTheWireIsSignedAsWritten()
        {
            var json = Wire.Obj("""{"version":1,"terminalId":31629,"amount":961.1,"authorizeAmount":50.0,"big":12345678901234567890}""");

            Assert.Equal(
                "amount=961.1&authorizeAmount=50.0&big=12345678901234567890&terminalId=31629&version=1",
                SecureHash.Compose(json));
        }

        [Fact]
        public void ANumberBuiltInCodeIsSignedTheSameAsOneParsedFromTheWire()
        {
            var built = new JsonObject { ["version"] = 1, ["merchantId"] = 8899L, ["terminalId"] = 999L, ["rate"] = 1.5m };
            var parsed = Wire.Obj("""{"version":1,"merchantId":8899,"terminalId":999,"rate":1.5}""");

            Assert.Equal("merchantId=8899&rate=1.5&terminalId=999&version=1", SecureHash.Compose(built));
            Assert.Equal(SecureHash.Compose(built), SecureHash.Compose(parsed));
        }

        [Fact]
        public void ABooleanIsSignedAsTrueOrFalseWhetherBuiltOrParsed()
        {
            var built = new JsonObject { ["success"] = true, ["approved"] = false };
            var parsed = Wire.Obj("""{"success":true,"approved":false}""");

            Assert.Equal("approved=false&success=true", SecureHash.Compose(built));
            Assert.Equal("approved=false&success=true", SecureHash.Compose(parsed));
        }

        [Fact]
        public void TheMessageBuilderPutsTheVersionInTheSigningStringAsVersionEqualsOne()
        {
            var message = EcrMessage.Build(
                EcrTransactionType.Sale,
                EcrTestConfigs.Lan,
                "TW1",
                1.234m,
                "",
                "",
                "",
                merchantReference: "REF1");

            var signed = SecureHash.Compose(message.Json);

            Assert.Contains("version=1", signed);
            Assert.Contains("amount=000000001234", signed);
            Assert.Contains("merchantReference=REF1", signed);
            Assert.DoesNotContain("secureHash", signed);
        }

        [Fact]
        public void ANumericAmountReadsBackWithoutAnyQuotesOrExponent()
        {
            var json = Wire.Obj("""{"a":0.258,"b":1E3,"c":-0.0,"d":"7"}""");

            Assert.Equal("0.258", json.Str("a"));
            Assert.Equal("1E3", json.Str("b"));
            Assert.Equal("-0.0", json.Str("c"));
            Assert.Equal("7", json.Str("d"));
        }

        [Fact]
        public void OnlyTheLiteralsTrueAndFalseAreBooleans()
        {
            var json = Wire.Obj("""{"a":true,"b":false,"c":"true","d":"false","e":1,"f":"yes","g":null,"h":{}}""");

            Assert.True(json.Flag("a"));
            Assert.False(json.Flag("b"));
            Assert.True(json.Flag("c"));
            Assert.False(json.Flag("d"));
            Assert.False(json.Flag("e"));
            Assert.False(json.Flag("f"));
            Assert.False(json.Flag("g"));
            Assert.False(json.Flag("h"));
            Assert.False(json.Flag("missing"));
            Assert.Null(json.StrictBool("e"));
            Assert.Null(json.StrictBool("missing"));
            Assert.Equal(false, json.StrictBool("b"));
        }

        [Fact]
        public void AJsonNullReadsAsAbsentNotAsTheWordNull()
        {
            var json = Wire.Obj("""{"authCode":null,"rrn":"1"}""");

            Assert.Equal("", json.Str("authCode"));
            Assert.Equal("", json.Str("missing"));
            Assert.Equal("1", json.Str("rrn"));
        }

        [Fact]
        public void ANestedValueReadAsTextIsItsJson()
        {
            var json = Wire.Obj("""{"data":{"a":1},"list":[1,2]}""");

            Assert.Equal("""{"a":1}""", json.Str("data"));
            Assert.Equal("[1,2]", json.Str("list"));
        }

        [Fact]
        public void ANullAndANestedValueAreNeverSigned()
        {
            var json = Wire.Obj("""{"a":"1","n":null,"o":{"x":1},"l":[1]}""");

            Assert.Equal("a=1", SecureHash.Compose(json));
        }

        // ── helpers ───────────────────────────────────────────────────────

        private static IEnumerable<byte[]> Chunks(byte[] packet)
        {
            // Every two-byte window, so a multi-byte character can be located.
            for (var i = 0; i + 1 < packet.Length; i++)
            {
                yield return new[] { packet[i], packet[i + 1] };
            }
        }

        private sealed class SequenceComparer : IEqualityComparer<byte[]>
        {
            public bool Equals(byte[]? x, byte[]? y) => x != null && y != null && x.AsSpan().SequenceEqual(y);

            public int GetHashCode(byte[] obj) => obj.Length;
        }
    }
}
