using System.IO;
using System.Text;
using AmwalPay.Ecr.Internal;
using Xunit;

namespace AmwalPay.Ecr.Tests
{
    /// <summary>
    /// The framing is a contract with the terminal, which has its own copy of it.
    /// These tests pin the bytes, not just the behaviour.
    /// </summary>
    public class EcrMessageCodecTests
    {
        [Fact]
        public void APacketIsA2ByteBigEndianLengthFollowedByTheBody()
        {
            var packet = EcrMessageCodec.Encode(Message());

            var declared = ((packet[0] & 0xFF) << 8) | (packet[1] & 0xFF);
            Assert.Equal(packet.Length - 2, declared);

            var body = Encoding.UTF8.GetString(packet, 2, packet.Length - 2);
            Assert.StartsWith("{", body);
            Assert.EndsWith("}", body);
        }

        [Fact]
        public void ALengthOver255BytesUsesBothHeaderBytes()
        {
            // A body of 300 bytes is 0x012C: the high byte must not be dropped.
            var packet = EcrFrames.Wrap(new byte[300]);

            Assert.Equal(0x01, packet[0] & 0xFF);
            Assert.Equal(0x2C, packet[1] & 0xFF);
        }

        [Fact]
        public void ALargeMessageSurvivesTheRoundTrip()
        {
            var big = EcrMessage.Build(
                EcrTransactionType.Sale,
                new EcrConfig { EcrId = new string('E', 400) },
                "TW1",
                1.234m,
                "",
                "",
                "");

            var packet = EcrMessageCodec.Encode(big);
            Assert.True(packet[0] != 0, "the length needs its high byte");

            var decoded = EcrMessageCodec.Decode(new MemoryStream(packet));
            Assert.Equal(new string('E', 400), decoded.Str("ecrId"));
        }

        [Fact]
        public void WhatIsEncodedCanBeReadBack()
        {
            var packet = EcrMessageCodec.Encode(Message());

            var decoded = EcrMessageCodec.Decode(new MemoryStream(packet));

            Assert.Equal("SALE", decoded.Str("messageType"));
            Assert.Equal("000000001234", decoded.Str("amount"));
        }

        [Fact]
        public void AResponseIsReadUpToItsDeclaredLengthAndNoFurther()
        {
            // Anything after the framed message belongs to the next one and must be
            // left in the stream rather than swallowed.
            var first = Framed("""{"responseCode":"00"}""");
            var trailing = Encoding.UTF8.GetBytes("leftover");
            var stream = new MemoryStream(Concat(first, trailing));

            var decoded = EcrMessageCodec.Decode(stream);

            Assert.Equal("00", decoded.Str("responseCode"));
            Assert.Equal(trailing.Length, stream.Length - stream.Position);
        }

        [Fact]
        public void UnknownFieldsAreKeptRatherThanRejected()
        {
            // A newer terminal may send fields this SDK does not know about.
            var decoded = EcrMessageCodec.Decode(
                new MemoryStream(Framed("""{"responseCode":"00","somethingNew":"x"}""")));

            Assert.Equal("x", decoded.Str("somethingNew"));
        }

        [Fact]
        public void ATruncatedBodyIsReportedRatherThanReturnedHalfRead()
        {
            var header = new byte[] { 0x00, 0x40 }; // claims 64 bytes
            var body = Encoding.UTF8.GetBytes("""{"responseCode":"00"}""");

            var e = Assert.Throws<IOException>(() =>
                EcrMessageCodec.Decode(new MemoryStream(Concat(header, body))));
            Assert.Contains("closed the connection", e.Message);
        }

        [Fact]
        public void ABodyThatIsNotJsonIsReported()
        {
            var e = Assert.Throws<IOException>(() =>
                EcrMessageCodec.Decode(new MemoryStream(Framed("not json"))));
            Assert.Contains("not valid JSON", e.Message);
        }

        [Theory]
        [InlineData("null")]
        [InlineData("[1,2]")]
        [InlineData("\"text\"")]
        [InlineData("42")]
        [InlineData("")]
        public void ABodyThatIsValidJsonButNotAnObjectIsReported(string body)
        {
            // A terminal (or something squatting on its port) answering with a bare
            // null or an array is malformed, not a crash.
            var e = Assert.Throws<IOException>(() => EcrMessageCodec.Parse(Encoding.UTF8.GetBytes(body)));
            Assert.Contains("not valid JSON", e.Message);
        }

        [Theory]
        [InlineData("""{"a":1,"a":2}""")]
        [InlineData("""{"data":{"x":1,"x":2}}""")]
        [InlineData("""{"list":[{"x":1,"x":2}]}""")]
        public void ABodyRepeatingAKeyAnywhereInTheTreeIsReported(string body)
        {
            var e = Assert.Throws<IOException>(() => EcrMessageCodec.Parse(Encoding.UTF8.GetBytes(body)));
            Assert.Contains("not valid JSON", e.Message);
        }

        [Theory]
        [InlineData("""{"a":1,}""")]
        [InlineData("""{"a":1 /* note */}""")]
        [InlineData("{'a':1}")]
        public void LenientJsonIsNotAccepted(string body)
        {
            // Strict on purpose: what the terminal signs is what it sent, byte for byte.
            Assert.Throws<IOException>(() => EcrMessageCodec.Parse(Encoding.UTF8.GetBytes(body)));
        }

        [Fact]
        public void AZeroLengthIsReported()
        {
            var e = Assert.Throws<IOException>(() =>
                EcrMessageCodec.Decode(new MemoryStream(new byte[] { 0x00, 0x00 })));
            Assert.Contains("empty message", e.Message);
        }

        [Fact]
        public void ARequestOverTheFrameLimitIsRefusedNotTruncated()
        {
            var huge = EcrMessage.Build(
                EcrTransactionType.Sale,
                new EcrConfig { EcrId = new string('E', EcrFrames.MaxBodyBytes) },
                "TW1",
                1m,
                "",
                "",
                "");

            Assert.Throws<IOException>(() => EcrMessageCodec.Encode(huge));
        }

        private static byte[] Framed(string body) => EcrFrames.Wrap(Encoding.UTF8.GetBytes(body));

        private static byte[] Concat(byte[] first, byte[] second)
        {
            var all = new byte[first.Length + second.Length];
            System.Buffer.BlockCopy(first, 0, all, 0, first.Length);
            System.Buffer.BlockCopy(second, 0, all, first.Length, second.Length);
            return all;
        }

        private static EcrMessage Message() =>
            EcrMessage.Build(
                type: EcrTransactionType.Sale,
                config: new EcrConfig(),
                terminalSerial: "TW1",
                amount: 1.234m,
                originalStan: "",
                originalTerminalId: "",
                originalDate: "");
    }
}
