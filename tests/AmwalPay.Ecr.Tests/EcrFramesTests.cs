using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AmwalPay.Ecr.Tests
{
    public class EcrFramesTests
    {
        [Fact]
        public void WrapPutsABigEndianLengthInFrontOfTheBody()
        {
            var packet = EcrFrames.Wrap(new byte[] { 1, 2, 3 });

            Assert.Equal(new byte[] { 0x00, 0x03, 1, 2, 3 }, packet);
        }

        [Fact]
        public void WrapOfAnEmptyBodyIsJustAZeroHeader()
        {
            Assert.Equal(new byte[] { 0x00, 0x00 }, EcrFrames.Wrap(Array.Empty<byte>()));
        }

        [Theory]
        [InlineData(255, 0x00, 0xFF)]
        [InlineData(256, 0x01, 0x00)]
        [InlineData(300, 0x01, 0x2C)]
        [InlineData(65535, 0xFF, 0xFF)]
        public void TheHeaderUsesBothBytesAtTheBoundaries(int length, int high, int low)
        {
            var packet = EcrFrames.Wrap(new byte[length]);

            Assert.Equal(length + EcrFrames.HeaderBytes, packet.Length);
            Assert.Equal(high, packet[0]);
            Assert.Equal(low, packet[1]);
            Assert.Equal(length, EcrFrames.BodyLength(new[] { packet[0], packet[1] }));
        }

        [Fact]
        public void ABodyTooLongForTheHeaderIsRefusedNotTruncated()
        {
            var e = Assert.Throws<IOException>(() => EcrFrames.Wrap(new byte[EcrFrames.MaxBodyBytes + 1]));

            Assert.Contains("does not fit a frame", e.Message);
        }

        [Fact]
        public void TheLimitsAreThoseOfATwoByteHeader()
        {
            Assert.Equal(2, EcrFrames.HeaderBytes);
            Assert.Equal(65535, EcrFrames.MaxBodyBytes);
        }

        [Fact]
        public void NullArgumentsAreRefused()
        {
            Assert.Throws<ArgumentNullException>(() => EcrFrames.Wrap(null!));
            Assert.Throws<ArgumentNullException>(() => EcrFrames.BodyLength(null!));
        }

        [Theory]
        [InlineData(0x00, 0x00, 0)]
        [InlineData(0x00, 0x40, 64)]
        [InlineData(0x80, 0x00, 32768)]
        [InlineData(0xFF, 0xFF, 65535)]
        public void BodyLengthReadsAnUnsignedBigEndianValue(int high, int low, int expected)
        {
            // Bytes above 0x7F are negative as sbyte in some languages; the length
            // must never come out negative.
            Assert.Equal(expected, EcrFrames.BodyLength(new[] { (byte)high, (byte)low }));
        }

        [Fact]
        public void ReadBodyReturnsExactlyOneMessageAndLeavesTheRest()
        {
            var stream = new MemoryStream(new byte[] { 0x00, 0x02, 0x41, 0x42, 0x00, 0x01, 0x43 });

            var first = EcrFrames.ReadBody(stream);

            Assert.Equal(new byte[] { 0x41, 0x42 }, first);
            Assert.Equal(3, stream.Length - stream.Position);
            Assert.Equal(new byte[] { 0x43 }, EcrFrames.ReadBody(stream));
        }

        [Fact]
        public async Task ReadBodyAsyncBehavesLikeReadBody()
        {
            var stream = new MemoryStream(EcrFrames.Wrap(Encoding.UTF8.GetBytes("héllo")));

            var body = await EcrFrames.ReadBodyAsync(stream);

            Assert.Equal("héllo", Encoding.UTF8.GetString(body));
        }

        [Fact]
        public void TheLengthCountsBytesNotCharacters()
        {
            var text = "é€"; // 2 + 3 bytes in UTF-8, 2 characters
            var packet = EcrFrames.Wrap(Encoding.UTF8.GetBytes(text));

            Assert.Equal(5, packet[1]);
            Assert.Equal(text, Encoding.UTF8.GetString(EcrFrames.ReadBody(new MemoryStream(packet))));
        }

        [Fact]
        public void AMessageDeliveredInDribsAndDrabsIsAssembled()
        {
            var packet = EcrFrames.Wrap(Encoding.UTF8.GetBytes("""{"responseCode":"00"}"""));

            var body = EcrFrames.ReadBody(new OneByteAtATime(packet));

            Assert.Equal("""{"responseCode":"00"}""", Encoding.UTF8.GetString(body));
        }

        [Fact]
        public async Task AMessageDeliveredInDribsAndDrabsIsAssembledAsync()
        {
            var packet = EcrFrames.Wrap(Encoding.UTF8.GetBytes("""{"responseCode":"00"}"""));

            var body = await EcrFrames.ReadBodyAsync(new OneByteAtATime(packet));

            Assert.Equal("""{"responseCode":"00"}""", Encoding.UTF8.GetString(body));
        }

        [Fact]
        public void AStreamThatEndsBeforeTheHeaderIsComplete()
        {
            var e = Assert.Throws<IOException>(() => EcrFrames.ReadBody(new MemoryStream(new byte[] { 0x00 })));

            Assert.Contains("closed the connection while sending the response header", e.Message);
        }

        [Fact]
        public void AStreamThatEndsBeforeAnyByteIsRead()
        {
            var e = Assert.Throws<IOException>(() => EcrFrames.ReadBody(new MemoryStream()));

            Assert.Contains("closed the connection while sending the response header", e.Message);
        }

        [Fact]
        public void AStreamThatEndsInsideTheBody()
        {
            var e = Assert.Throws<IOException>(() =>
                EcrFrames.ReadBody(new MemoryStream(new byte[] { 0x00, 0x08, 0x41, 0x42 })));

            Assert.Contains("closed the connection while sending the response body", e.Message);
        }

        [Fact]
        public async Task AStreamThatEndsInsideTheBodyAsync()
        {
            var e = await Assert.ThrowsAsync<IOException>(() =>
                EcrFrames.ReadBodyAsync(new MemoryStream(new byte[] { 0x00, 0x08, 0x41, 0x42 })));

            Assert.Contains("closed the connection while sending the response body", e.Message);
        }

        [Fact]
        public void AZeroLengthHeaderIsAnEmptyMessage()
        {
            var e = Assert.Throws<IOException>(() => EcrFrames.ReadBody(new MemoryStream(new byte[] { 0x00, 0x00 })));

            Assert.Contains("empty message", e.Message);
        }

        [Fact]
        public async Task AZeroLengthHeaderIsAnEmptyMessageAsync()
        {
            var e = await Assert.ThrowsAsync<IOException>(() =>
                EcrFrames.ReadBodyAsync(new MemoryStream(new byte[] { 0x00, 0x00 })));

            Assert.Contains("empty message", e.Message);
        }

        [Fact]
        public async Task ACancelledReadIsCancelledNotAFramingError()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                EcrFrames.ReadBodyAsync(new MemoryStream(EcrFrames.Wrap(new byte[4])), cts.Token));
        }

        /// <summary>A stream that yields one byte per read, like a slow radio link.</summary>
        private sealed class OneByteAtATime : MemoryStream
        {
            public OneByteAtATime(byte[] bytes) : base(bytes)
            {
            }

            public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, 1));

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                base.ReadAsync(buffer, offset, Math.Min(count, 1), cancellationToken);
        }
    }
}
