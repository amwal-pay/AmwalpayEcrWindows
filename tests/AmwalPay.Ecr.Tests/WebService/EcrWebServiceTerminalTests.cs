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
    /// The parts of <see cref="EcrWebServiceTerminal"/> that are decided before
    /// any request leaves the process.
    /// </summary>
    public class EcrWebServiceTerminalTests
    {
        private static EcrWebServiceTerminal WithoutIds() =>
            new EcrWebServiceTerminal("TW1", new EcrConfig { MerchantId = "", TerminalId = "" });

        [Fact]
        public async Task ASaleWithoutBackendIdsFailsLocallyAsMalformed()
        {
            var result = await WithoutIds().SaleAsync(1m, "REF-1");

            var failed = Assert.IsType<EcrResult.Failed>(result);
            Assert.IsType<Failure.Malformed>(failed.Failure);
            Assert.Contains("merchantId and terminalId", failed.Failure.Message);
        }

        [Fact]
        public async Task AVoidAndARefundWithoutBackendIdsFailLocally()
        {
            var voided = await WithoutIds().VoidAsync("24");
            var refunded = await WithoutIds().RefundAsync(1m, "24", "20260806");

            Assert.IsType<Failure.Malformed>(Assert.IsType<EcrResult.Failed>(voided).Failure);
            Assert.IsType<Failure.Malformed>(Assert.IsType<EcrResult.Failed>(refunded).Failure);
        }

        [Fact]
        public async Task AnInquiryWithoutBackendIdsFailsLocallyAndKeepsTheCallersReference()
        {
            var inquiry = await WithoutIds().InquireAsync("24", "20260806", "LOOKUP-1");

            var failed = Assert.IsType<EcrInquiry.Failed>(inquiry);
            Assert.Equal("LOOKUP-1", failed.MerchantReference);
            Assert.IsType<Failure.Malformed>(failed.Failure);
        }

        [Fact]
        public async Task NonNumericIdsAreRefusedNotSentAsZero()
        {
            var terminal = new EcrWebServiceTerminal("TW1", new EcrConfig { MerchantId = "M1", TerminalId = "T1" });

            var result = await terminal.SaleAsync(1m);

            Assert.IsType<Failure.Malformed>(Assert.IsType<EcrResult.Failed>(result).Failure);
        }

        [Fact]
        public async Task ASettlementWithoutBackendIdsFailsBeforeAnythingIsSent()
        {
            var settled = await WithoutIds().SettlementAsync("SET-1");

            var failed = Assert.IsType<EcrSettlement.Failed>(settled);
            Assert.Equal("SET-1", failed.MerchantReference);
            Assert.IsType<Failure.Malformed>(failed.Failure);
        }

        [Fact]
        public async Task SettlePostsTheIdentityBodyAndReadsAClosedBatch()
        {
            string? url = null;
            string? body = null;
            var terminal = Terminal(request =>
            {
                url = request.RequestUri!.AbsoluteUri;
                body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return Reply(HttpStatusCode.OK,
                    """
                    {"success":true,"responseCode":"00","message":"Settlement successful","data":{"settlementStatus":"SETTLED","batchId":"00000007","currency":"OMR","endTime":"20261020130509","totalTransactions":1,"netAmount":"000000001000","totals":{"SALE":{"count":1,"amount":"000000001000"}}}}
                    """);
            });

            var settled = Assert.IsType<EcrSettlement.Settled>(await terminal.SettlementAsync("SET01"));

            Assert.Equal("https://test.amwalpg.com:25453/Ecr/settle", url);
            Assert.Contains("\"merchantId\":13593", body);
            Assert.Contains("\"currencyCode\":", body);
            Assert.DoesNotContain("messageType", body);
            Assert.Equal("00000007", settled.BatchId);
            Assert.Equal("1.000", settled.NetAmount);
        }

        [Fact]
        public async Task CheckReachabilityPostsToTheHubAndReportsTheAnswer()
        {
            string? url = null;
            var up = Terminal(request =>
            {
                url = request.RequestUri!.AbsoluteUri;
                return Reply(HttpStatusCode.OK, """{"success":true,"responseCode":"00","message":"OK"}""");
            });

            var reachable = await up.ProbeReachabilityAsync();

            Assert.Equal("https://test.amwalpg.com:25453/Ecr/CheckReachability", url);
            Assert.True(reachable.Reachable);
            Assert.Equal("https://test.amwalpg.com:25453", reachable.Endpoint);

            var down = Terminal(_ => throw new HttpRequestException("network down"));
            var missed = await down.ProbeReachabilityAsync();

            Assert.False(missed.Reachable);
            Assert.Contains("network down", missed.Error);
        }

        private static EcrWebServiceTerminal Terminal(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
            new EcrWebServiceTerminal(
                "TW1",
                EcrTestConfigs.WebService,
                EcrLogger.None,
                new HttpClient(new Stub(respond)));

        private static HttpResponseMessage Reply(HttpStatusCode status, string body) =>
            new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        private sealed class Stub : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

            public Stub(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(_respond(request));
        }
    }
}
