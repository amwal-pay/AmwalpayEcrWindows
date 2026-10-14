using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AmwalPay.Ecr.Tests.WebService
{
    /// <summary>
    /// A Web Service transport failure is synthesised as an answer with no data.
    /// Read naively it is a decline (sale) or "not found" (inquiry) - both invite a
    /// till to charge again for a payment that may have gone through.
    /// </summary>
    public class UnknownOutcomeTests
    {
        private sealed class Stub : HttpMessageHandler
        {
            private readonly Func<HttpResponseMessage> _respond;

            public Stub(Func<HttpResponseMessage> respond) => _respond = respond;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
                Task.FromResult(_respond());
        }

        private static EcrWebServiceTerminal Terminal(Func<HttpResponseMessage> respond) =>
            new EcrWebServiceTerminal("TW1", EcrTestConfigs.WebService, EcrLogger.None, new HttpClient(new Stub(respond)));

        private static HttpResponseMessage Throw() => throw new HttpRequestException("network down");

        private static HttpResponseMessage Reply(HttpStatusCode status, string body) =>
            new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        [Fact]
        public async Task ASaleThatGotNoAnswerAsksForAnInquiryNotARetry()
        {
            var result = await Terminal(Throw).SaleAsync(1.000m);

            var failed = Assert.IsType<EcrResult.Failed>(result);
            Assert.IsType<Failure.Timeout>(failed.Failure);
            Assert.Equal(NextStep.InquireByMerchantReference, failed.NextStep);
        }

        [Fact]
        public async Task AnEmptyReplyToASaleAlsoLeavesTheOutcomeUnknown()
        {
            var result = await Terminal(() => Reply(HttpStatusCode.OK, "")).SaleAsync(1.000m);

            var failed = Assert.IsType<EcrResult.Failed>(result);
            Assert.Equal(NextStep.InquireByMerchantReference, failed.NextStep);
        }

        [Fact]
        public async Task AnOutageDuringAnInquiryIsAFailedLookupNotNotFound()
        {
            var inquiry = await Terminal(Throw).InquireAsync("215", "20260826");

            var failed = Assert.IsType<EcrInquiry.Failed>(inquiry);
            Assert.IsType<Failure.ConnectionLost>(failed.Failure);
        }

        [Fact]
        public async Task ARealDeclineIsStillADeclineWithNoFollowUp()
        {
            var body = """{"success":false,"responseCode":"51","message":"Insufficient funds"}""";
            var result = await Terminal(() => Reply(HttpStatusCode.OK, body)).SaleAsync(1.000m);

            var declined = Assert.IsType<EcrResult.Declined>(result);
            Assert.Equal("51", declined.ResponseCode);
            Assert.Equal(NextStep.None, declined.NextStep);
        }

        [Fact]
        public async Task ARealNotFoundIsStillNotFound()
        {
            var body = """{"success":false,"responseCode":"25","message":"Transaction not found"}""";
            var inquiry = await Terminal(() => Reply(HttpStatusCode.OK, body)).InquireAsync("215", "20260826");

            Assert.IsType<EcrInquiry.NotFound>(inquiry);
        }

        [Fact]
        public async Task AClientErrorWithNoBodyMessageIsNotTreatedAsUnknown()
        {
            // The Hub rejected the request outright, so it did not act on it.
            var result = await Terminal(() => Reply(HttpStatusCode.Unauthorized, "{}")).SaleAsync(1.000m);

            Assert.Equal(NextStep.None, Assert.IsType<EcrResult.Declined>(result).NextStep);
        }
    }
}
