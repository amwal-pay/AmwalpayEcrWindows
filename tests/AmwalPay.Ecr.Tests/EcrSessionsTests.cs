using System;
using System.Linq;
using System.Threading.Tasks;
using AmwalPay.Ecr.Internal;
using AmwalPay.Ecr.WebService;
using Xunit;

namespace AmwalPay.Ecr.Tests
{
    public class EcrSessionsTests
    {
        private static readonly EcrConfig LanConfig = EcrTestConfigs.Lan;
        private static readonly EcrConfig WebConfig = EcrTestConfigs.WebService;

        [Fact]
        public void LanPlanReadyWhenHostPortAndKeyAreValid()
        {
            var plan = EcrSessions.Plan(new EcrLink.Lan("192.168.1.10", 9100), LanConfig);

            Assert.True(plan.IsReady);
            Assert.True(plan.UsesLan);
            Assert.Equal("192.168.1.10:9100", plan.ConnectionSummary);
            Assert.Equal(SecureHash.KeyLabel, plan.SigningKeyLabel);
            Assert.Equal(9100, plan.Config.Port);
        }

        [Fact]
        public void LanPlanReportsMissingHostAndKey()
        {
            var plan = EcrSessions.Plan(new EcrLink.Lan("  ", 9100), new EcrConfig { SecureHashKey = "" });

            Assert.False(plan.IsReady);
            Assert.Contains(plan.Issues, issue => issue.Contains("IP address"));
            Assert.Contains(plan.Issues, issue => issue.Contains(SecureHash.KeyLabel));
        }

        [Fact]
        public void LanPlanRejectsInvalidKey()
        {
            var plan = EcrSessions.Plan(new EcrLink.Lan("127.0.0.1"), new EcrConfig(), rawSecureHashKey: "abc");

            Assert.False(plan.IsReady);
            Assert.Contains(plan.Issues, issue => issue.Contains("hex string"));
            Assert.Equal("", plan.Config.SecureHashKey);
        }

        [Fact]
        public void WebServicePlanMergesIdsFromLink()
        {
            var plan = EcrSessions.Plan(
                new EcrLink.WebService("13593", "742001"),
                WebConfig with { MerchantId = "", TerminalId = "" });

            Assert.True(plan.IsReady);
            Assert.True(plan.UsesWebService);
            Assert.Equal("13593", plan.Config.MerchantId);
            Assert.Equal("742001", plan.Config.TerminalId);
            Assert.Equal(EcrConfig.WebServiceResponseTimeout, plan.Config.ResponseTimeout);
            Assert.Equal(WebServiceSecureHash.KeyLabel, plan.SigningKeyLabel);
            Assert.Contains("UAT", plan.ConnectionSummary);
        }

        [Fact]
        public void WebServicePlanReportsMissingMerchantAndTerminal()
        {
            var plan = EcrSessions.Plan(new EcrLink.WebService("", ""), WebConfig);

            Assert.False(plan.IsReady);
            Assert.Contains(plan.Issues, issue => issue.Contains("Merchant ID"));
            Assert.Contains(plan.Issues, issue => issue.Contains("Terminal ID"));
        }

        [Fact]
        public void WebServicePlanRejectsNonNumericIds()
        {
            var plan = EcrSessions.Plan(new EcrLink.WebService("M1", "T1"), WebConfig);

            Assert.False(plan.IsReady);
            Assert.Contains(plan.Issues, issue => issue.Contains("numeric"));
        }

        [Fact]
        public void OpenDispatchesLanWithoutUsbChannel()
        {
            var plan = EcrSessions.Plan(new EcrLink.Lan("127.0.0.1", 9100), LanConfig);

            var session = EcrSessions.Open("SN1", plan);

            Assert.True(session.UsesLocalTerminal);
            Assert.False(session.UsesWebService);
            Assert.True(session.SupportsReceipt);
        }

        [Fact]
        public void OpenDispatchesWebService()
        {
            var plan = EcrSessions.Plan(new EcrLink.WebService("13593", "742001"), WebConfig);

            var session = EcrSessions.Open("SN1", plan);

            Assert.True(session.UsesWebService);
            Assert.False(session.UsesLocalTerminal);
            Assert.False(session.SupportsReceipt);
        }

        [Fact]
        public void OpenUsbRequiresChannelFactory()
        {
            var plan = EcrSessions.Plan(new EcrLink.UsbCable(), LanConfig);

            Assert.True(plan.IsReady);
            var expected = Assert.Throws<InvalidOperationException>(() => EcrSessions.Open("SN1", plan));
            Assert.Contains("channel", expected.Message);
        }

        // ── Beyond the Kotlin suite ───────────────────────────────────────

        [Fact]
        public void UsbPlanHasNoAddressToValidateAndSaysSo()
        {
            var plan = EcrSessions.Plan(new EcrLink.UsbCable(), LanConfig);

            Assert.True(plan.UsesUsbCable);
            Assert.Equal("USB cable", plan.ConnectionSummary);
            Assert.Equal(SecureHash.KeyLabel, plan.SigningKeyLabel);
            Assert.True(plan.SigningKeyConfigured);
        }

        [Fact]
        public void UsbPlanWithoutAKeyIsNotReady()
        {
            var plan = EcrSessions.Plan(new EcrLink.UsbCable(), new EcrConfig());

            Assert.False(plan.IsReady);
            Assert.Contains(plan.Issues, issue => issue.Contains("is not configured"));
            Assert.False(plan.SigningKeyConfigured);
        }

        [Fact]
        public void TheKeyAsTheOperatorTypedItIsTrimmedBeforeItIsChecked()
        {
            var plan = EcrSessions.Plan(
                new EcrLink.Lan("127.0.0.1"),
                new EcrConfig(),
                rawSecureHashKey: "  " + EcrTestConfigs.SecureHashKeyEcrWifi + "\n");

            Assert.True(plan.IsReady);
            Assert.Equal(EcrTestConfigs.SecureHashKeyEcrWifi, plan.Config.SecureHashKey);
        }

        [Fact]
        public void TheLinksPortWinsOverTheConfigsPort()
        {
            var plan = EcrSessions.Plan(new EcrLink.Lan("10.0.0.5", 9200), LanConfig with { Port = 9100 });

            Assert.Equal(9200, plan.Config.Port);
            Assert.Equal("10.0.0.5:9200", plan.ConnectionSummary);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(65536)]
        [InlineData(-1)]
        public void ALanPortOutOfRangeIsAnIssueNotAnException(int port)
        {
            var plan = EcrSessions.Plan(new EcrLink.Lan("10.0.0.5", port), LanConfig);

            Assert.False(plan.IsReady);
            Assert.Contains(plan.Issues, issue => issue.Contains("Port must be between 1 and 65535"));
        }

        [Fact]
        public void APlanWithIssuesCannotBeOpened()
        {
            var plan = EcrSessions.Plan(new EcrLink.Lan("", 9100), LanConfig);

            var e = Assert.Throws<ArgumentException>(() => EcrSessions.Open("SN1", plan));
            Assert.Contains("unresolved issues", e.Message);
        }

        [Fact]
        public void IssuesAreNotRepeated()
        {
            var plan = EcrSessions.Plan(new EcrLink.WebService("", ""), new EcrConfig());

            Assert.Equal(plan.Issues.Count, plan.Issues.Distinct().Count());
        }

        [Fact]
        public void WebServiceSummaryNamesTheEnvironmentAndBothIds()
        {
            var plan = EcrSessions.Plan(new EcrLink.WebService("13593", "742001"), WebConfig);

            Assert.Equal("UAT · merchant 13593 · terminal 742001", plan.ConnectionSummary);
        }

        [Fact]
        public async Task AUsbSessionRunsTheSameTerminalOverTheSuppliedChannel()
        {
            var channel = FakeChannel.Always(null);
            var plan = EcrSessions.Plan(new EcrLink.UsbCable(), LanConfig with { AutoInquireOnFailure = false });
            var session = EcrSessions.Open("SN1", plan, () => channel);

            var result = await session.SaleAsync(1.234m, "ORD-1");

            Assert.True(session.UsesLocalTerminal);
            Assert.IsType<EcrResult.Failed>(result);
            var sent = Wire.Obj(Assert.Single(channel.Requests));
            Assert.Equal("SALE", sent.Str("messageType"));
            Assert.Equal("ORD-1", sent.Str("merchantReference"));
            Assert.True(SecureHash.Verify(sent, LanConfig.SecureHashKey), "the cable path signs like the socket path");
        }

        [Fact]
        public async Task ASessionOverAWebServiceLinkCannotFetchAReceipt()
        {
            var plan = EcrSessions.Plan(new EcrLink.WebService("13593", "742001"), WebConfig);
            var session = EcrSessions.Open("SN1", plan);

            var receipt = await session.ReceiptAsync("24", "20260817");

            var unavailable = Assert.IsType<EcrReceipt.Unavailable>(receipt);
            Assert.Contains("only supported over", unavailable.Reason);
            Assert.Null(await session.ProbeReachabilityAsync());
        }

        [Fact]
        public async Task AWebServiceSessionNeedsTheOriginalReferenceToCloseAReceipt()
        {
            var plan = EcrSessions.Plan(new EcrLink.WebService("13593", "742001"), WebConfig);
            var session = EcrSessions.Open("SN1", plan);

            var closed = await session.CloseReceiptAsync();

            var failed = Assert.IsType<EcrReceiptClosed.Failed>(closed);
            Assert.Contains("original merchant reference", failed.Failure.Message);
        }

        [Fact]
        public async Task ALocalSessionProbesThroughItsChannel()
        {
            var channel = FakeChannel.Always(null);
            channel.ProbeError = "no cable";
            var plan = EcrSessions.Plan(new EcrLink.UsbCable(), LanConfig);
            var session = EcrSessions.Open("SN1", plan, () => channel);

            var reachability = await session.ProbeReachabilityAsync();

            Assert.NotNull(reachability);
            Assert.False(reachability!.Reachable);
            Assert.Equal("no cable", reachability.Error);
        }
    }
}
