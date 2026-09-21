using System;
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
    }
}
