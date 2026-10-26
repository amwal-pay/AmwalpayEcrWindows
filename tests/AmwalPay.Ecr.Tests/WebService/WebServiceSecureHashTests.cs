using System;
using System.Text.Json.Nodes;
using AmwalPay.Ecr.Internal;
using AmwalPay.Ecr.WebService;
using Xunit;

namespace AmwalPay.Ecr.Tests.WebService
{
    public class WebServiceSecureHashTests
    {
        private static string WebServiceKey => EcrTestConfigs.WebService.SecureHashKey;
        private static string WifiKey => EcrTestConfigs.Lan.SecureHashKey;

        private const long MerchantId = 8899;
        private const long TerminalId = 999;
        private const string TerminalSerial = "QQW";

        [Fact]
        public void CalcHashIsStableForAGivenPayloadAndConfigKey()
        {
            const string obj =
                "amount=000000000111&currencyCode=512&ecrId=ECR01&merchantId=8899&merchantReference=EA5C14BDB0A2&messageType=SALE&requestDateTime=20260826115847&terminalId=999&terminalSerial=QQW&transactionDateTime=20260826115847&version=1";

            var hash = WebServiceSecureHash.CalcHash(obj, WifiKey);

            Assert.Equal(64, hash.Length);
            Assert.Equal(hash, WebServiceSecureHash.CalcHash(obj, WifiKey));
            Assert.Equal(hash, WebServiceSecureHash.Sign(obj, WifiKey));
        }

        [Fact]
        public void KnownAnswerForTheWebServiceSigningString()
        {
            // Expected value computed with Python's hmac module over the
            // hex-decoded key, independently of this code base.
            const string obj =
                "amount=000000000111&currencyCode=512&ecrId=ECR01&merchantId=8899&merchantReference=EA5C14BDB0A2&messageType=SALE&requestDateTime=20260826115847&terminalId=999&terminalSerial=QQW&transactionDateTime=20260826115847&version=1";

            Assert.Equal(
                "FEF8D18506A6AC035DC825A1DBA4413EE885898BBCE27EE4A681683FFAADC61E",
                WebServiceSecureHash.CalcHash(obj, EcrTestConfigs.SecureHashKeyWebService));
            Assert.Equal(
                "E4190003B5E8B496DAFE11C464F6BDA387735F1050C1DE69DCC4B2848D19F7C2",
                WebServiceSecureHash.CalcHash(obj, EcrTestConfigs.SecureHashKeyEcrWifi));
        }

        [Fact]
        public void ASaleBodyIsSignedOverExactlyTheDocumentedSigningString()
        {
            // Numbers (merchantId, terminalId, version) enter the signing string as
            // plain digits, exactly as the platform's calcHash sees them.
            var body = EcrWebServiceRequestJson.Sale(
                new EcrWebServiceSaleRequest(
                    MerchantId: MerchantId,
                    TerminalId: TerminalId,
                    TerminalSerial: TerminalSerial,
                    AmountMinorUnits: 111,
                    MerchantReference: "EA5C14BDB0A2",
                    TransactionDateTime: "20260826115847"),
                secureHashKey: EcrTestConfigs.SecureHashKeyWebService);

            Assert.Equal(
                "amount=000000000111&currencyCode=512&ecrId=ECR01&merchantId=8899&merchantReference=EA5C14BDB0A2&messageType=SALE&requestDateTime=20260826115847&terminalId=999&terminalSerial=QQW&transactionDateTime=20260826115847&version=1",
                WebServiceSecureHash.PayloadString(body));
            Assert.Equal(
                "FEF8D18506A6AC035DC825A1DBA4413EE885898BBCE27EE4A681683FFAADC61E",
                body.Str(WebServiceSecureHash.Field));
        }

        [Fact]
        public void Merchant13593SaleSignsWithTheWebServiceConfigKey()
        {
            var body = EcrWebServiceRequestJson.Sale(
                new EcrWebServiceSaleRequest(
                    MerchantId: 13593,
                    TerminalId: 101311,
                    TerminalSerial: "P653200008251-Web",
                    AmountMinorUnits: 222,
                    MerchantReference: "E04A8DF76843",
                    TransactionDateTime: "20260901134901"),
                secureHashKey: "");

            var signed = WebServiceSecureHash.WithHash(body, WebServiceKey);
            var hash = signed.Str(WebServiceSecureHash.Field);

            Assert.Equal(64, hash.Length);
            Assert.Equal(hash, WebServiceSecureHash.WithHash(body, WebServiceKey).Str(WebServiceSecureHash.Field));
        }

        [Fact]
        public void HostedSaleExampleSignsWithTheWebServiceConfigKey()
        {
            const string payload =
                "amount=1000&currencyCode=512&merchantId=13593&merchantReference=REF-1586&requestDateTime=20260816155749&terminalId=101311";

            var hash = WebServiceSecureHash.CalcHash(payload, WebServiceKey);

            Assert.Equal(64, hash.Length);
            Assert.Equal(hash, WebServiceSecureHash.CalcHash(payload, WebServiceKey));
        }

        [Fact]
        public void CalcHashReturnsEmptyStringForInvalidSecret()
        {
            Assert.Equal("", WebServiceSecureHash.CalcHash("amount=1", "not-hex"));
            Assert.Equal("", WebServiceSecureHash.CalcHash("amount=1", ""));
        }

        [Fact]
        public void SalePayloadMatchesExpectedWireShape()
        {
            var unsigned = Wire(EcrWebServiceRequestJson.Sale(
                new EcrWebServiceSaleRequest(
                    MerchantId: MerchantId,
                    TerminalId: TerminalId,
                    TerminalSerial: TerminalSerial,
                    AmountMinorUnits: 111,
                    MerchantReference: "EA5C14BDB0A2",
                    TransactionDateTime: "20260826115847")));

            Assert.Equal(
                """{"version":1,"messageType":"SALE","merchantId":8899,"terminalId":999,"terminalSerial":"QQW","ecrId":"ECR01","merchantReference":"EA5C14BDB0A2","currencyCode":"512","transactionDateTime":"20260826115847","requestDateTime":"20260826115847","amount":"000000000111"}""",
                unsigned);
        }

        [Fact]
        public void VoidPayloadMatchesExpectedWireShape()
        {
            var unsigned = Wire(EcrWebServiceRequestJson.Void(
                new EcrWebServiceVoidRequest(
                    MerchantId: MerchantId,
                    TerminalId: TerminalId,
                    TerminalSerial: TerminalSerial,
                    Stan: "8",
                    MerchantReference: "77619797A075",
                    TransactionDateTime: "20260826120107")));

            Assert.Equal(
                """{"version":1,"messageType":"VOID","merchantId":8899,"terminalId":999,"terminalSerial":"QQW","ecrId":"ECR01","merchantReference":"77619797A075","currencyCode":"512","transactionDateTime":"20260826120107","requestDateTime":"20260826120107","stan":"000008","originalMerchantReference":"77619797A075","originalTerminalId":999}""",
                unsigned);
        }

        [Fact]
        public void RefundPayloadMatchesExpectedWireShape()
        {
            var unsigned = Wire(EcrWebServiceRequestJson.Refund(
                new EcrWebServiceRefundRequest(
                    MerchantId: MerchantId,
                    TerminalId: TerminalId,
                    TerminalSerial: TerminalSerial,
                    AmountMinorUnits: 22,
                    Stan: "11",
                    OriginalTransactionDate: "20260826",
                    MerchantReference: "3FD2197EB935",
                    TransactionDateTime: "20260826120556")));

            Assert.Equal(
                """{"version":1,"messageType":"REFUND","merchantId":8899,"terminalId":999,"terminalSerial":"QQW","ecrId":"ECR01","merchantReference":"3FD2197EB935","currencyCode":"512","transactionDateTime":"20260826120556","requestDateTime":"20260826120556","amount":"000000000022","stan":"000011","originalTransactionDate":"20260826","originalMerchantReference":"3FD2197EB935","originalTerminalId":999}""",
                unsigned);
        }

        [Fact]
        public void InquiryPayloadMatchesExpectedWireShape()
        {
            var unsigned = Wire(EcrWebServiceRequestJson.Inquiry(
                new EcrWebServiceTransactionStatusRequest(
                    MerchantId: MerchantId,
                    TerminalId: TerminalId,
                    TerminalSerial: TerminalSerial,
                    Stan: "13",
                    OriginalTransactionDate: "20260826",
                    MerchantReference: "0B574EB48120",
                    TransactionDateTime: "20260826120654")));

            Assert.Equal(
                """{"version":1,"messageType":"INQUIRY","merchantId":8899,"terminalId":999,"terminalSerial":"QQW","ecrId":"ECR01","merchantReference":"0B574EB48120","currencyCode":"512","transactionDateTime":"20260826120654","requestDateTime":"20260826120654","stan":"000013","originalTransactionDate":"20260826","originalMerchantReference":"","originalTerminalId":999}""",
                unsigned);
        }

        [Fact]
        public void InquiryByMerchantReferencePayloadMatchesExpectedWireShape()
        {
            var unsigned = Wire(EcrWebServiceRequestJson.Inquiry(
                new EcrWebServiceTransactionStatusRequest(
                    MerchantId: MerchantId,
                    TerminalId: TerminalId,
                    TerminalSerial: TerminalSerial,
                    Stan: "",
                    OriginalTransactionDate: "20260826",
                    MerchantReference: "INQ-NEW-REF01",
                    OriginalMerchantReference: "ORD-88231ABC",
                    TransactionDateTime: "20260826120654")));

            Assert.Equal(
                """{"version":1,"messageType":"INQUIRY","merchantId":8899,"terminalId":999,"terminalSerial":"QQW","ecrId":"ECR01","merchantReference":"INQ-NEW-REF01","currencyCode":"512","transactionDateTime":"20260826120654","requestDateTime":"20260826120654","stan":"","originalTransactionDate":"20260826","originalMerchantReference":"ORD-88231ABC","originalTerminalId":999}""",
                unsigned);
        }

        [Fact]
        public void WithHashAddsSecureHashValueAndExcludesItFromTheSignedPayload()
        {
            var (_, body) = BuildSale(secureHashKey: "");
            var signed = WebServiceSecureHash.WithHash(body, WifiKey);

            Assert.True(signed.ContainsKey(WebServiceSecureHash.Field));
            Assert.Equal(
                WebServiceSecureHash.Sign(body, WifiKey),
                signed.Str(WebServiceSecureHash.Field));
            Assert.DoesNotContain("secureHashValue", WebServiceSecureHash.Compose(signed));
        }

        [Fact]
        public void WithHashDoesNotMutateItsInputAndReplacesAnEarlierHash()
        {
            var (_, body) = BuildSale(secureHashKey: "");

            var once = WebServiceSecureHash.WithHash(body, WifiKey);
            var twice = WebServiceSecureHash.WithHash(once, WebServiceKey);

            Assert.False(body.ContainsKey(WebServiceSecureHash.Field), "the caller's body is left alone");
            Assert.NotEqual(once.Str(WebServiceSecureHash.Field), twice.Str(WebServiceSecureHash.Field));
            Assert.Equal(
                WebServiceSecureHash.Sign(body, WebServiceKey),
                twice.Str(WebServiceSecureHash.Field));
        }

        [Fact]
        public void SignedSaleIncludesSecureHashValue()
        {
            var json = EcrWebServiceRequestJson.Sale(SaleRequest(), secureHashKey: WifiKey);

            Assert.True(json.ContainsKey(WebServiceSecureHash.Field));
            Assert.NotEqual("", json.Str(WebServiceSecureHash.Field));
        }

        [Fact]
        public void UnsignedRequestsOmitSecureHashValue()
        {
            var unsigned = EcrWebServiceRequestJson.Sale(SaleRequest());

            Assert.False(unsigned.ContainsKey(WebServiceSecureHash.Field));
        }

        [Fact]
        public void EmptySecretLeavesTheBodyUnchanged()
        {
            var (_, body) = BuildSale(secureHashKey: "");

            Assert.Same(body, WebServiceSecureHash.WithHash(body, ""));
        }

        [Fact]
        public void AnInvalidSecretIsRefusedRatherThanSendingAnUnsignedBody()
        {
            var (_, body) = BuildSale(secureHashKey: "");

            Assert.Throws<ArgumentException>(() => WebServiceSecureHash.WithHash(body, "not-hex"));
        }

        [Fact]
        public void AReceiptIsNotAWebServiceOperation()
        {
            Assert.Throws<InvalidOperationException>(() => EcrWebServiceMessage.Build(
                EcrTransactionType.Receipt,
                MerchantId,
                TerminalId,
                TerminalSerial,
                secureHashKey: ""));
        }

        [Fact]
        public void CloseReceiptPayloadMatchesExpectedWireShape()
        {
            var unsigned = Wire(EcrWebServiceRequestJson.CloseReceipt(
                new EcrWebServiceCloseReceiptRequest(
                    MerchantId: 13593,
                    TerminalId: 126706,
                    TerminalSerial: "P653200101973",
                    MerchantReference: "AC4C243243C6A7C5",
                    OriginalMerchantReference: "BB13726B7E261874",
                    TransactionDateTime: "20261014113326")));

            Assert.Equal(
                """{"version":1,"merchantId":13593,"terminalId":126706,"terminalSerial":"P653200101973","ecrId":"ECR01","merchantReference":"AC4C243243C6A7C5","transactionDateTime":"20261014113326","requestDateTime":"20261014113326","originalMerchantReference":"BB13726B7E261874"}""",
                unsigned);
            Assert.DoesNotContain("messageType", unsigned);
            Assert.DoesNotContain("currencyCode", unsigned);
        }

        [Fact]
        public void SettleAndCheckReachabilityShareTheIdentityBody()
        {
            const string expected =
                """{"version":1,"merchantId":13593,"terminalId":126706,"terminalSerial":"P653200101973","ecrId":"ECR01","merchantReference":"SET01","currencyCode":"512","transactionDateTime":"20261026120000","requestDateTime":"20261026120000"}""";

            var settle = Wire(EcrWebServiceRequestJson.Settle(new EcrWebServiceSettleRequest(
                13593, 126706, "P653200101973", "SET01", TransactionDateTime: "20261026120000")));
            var reachability = Wire(EcrWebServiceRequestJson.CheckReachability(new EcrWebServiceCheckReachabilityRequest(
                13593, 126706, "P653200101973", "SET01", TransactionDateTime: "20261026120000")));

            Assert.Equal(expected, settle);
            Assert.Equal(expected, reachability);
            Assert.DoesNotContain("messageType", settle);
        }

        [Fact]
        public void ASignedSettlePutsSecureHashValueLastAndLeavesItOutOfTheSignedText()
        {
            var json = EcrWebServiceRequestJson.Settle(
                new EcrWebServiceSettleRequest(
                    13593, 126706, "P653200101973", "SET01", TransactionDateTime: "20261026120000"),
                secureHashKey: WebServiceKey);

            Assert.True(json.ContainsKey(WebServiceSecureHash.Field));
            Assert.Equal(64, json.Str(WebServiceSecureHash.Field).Length);
            Assert.DoesNotContain("secureHashValue", WebServiceSecureHash.Compose(json));
            Assert.DoesNotContain("messageType", EcrJson.Write(json));
        }

        [Fact]
        public void SignedCloseReceiptIncludesSecureHashValue()
        {
            var json = EcrWebServiceRequestJson.CloseReceipt(
                new EcrWebServiceCloseReceiptRequest(
                    MerchantId: 13593,
                    TerminalId: 126706,
                    TerminalSerial: "P653200101973",
                    MerchantReference: "AC4C243243C6A7C5",
                    OriginalMerchantReference: "BB13726B7E261874",
                    TransactionDateTime: "20261014113326"),
                secureHashKey: WebServiceKey);

            Assert.True(json.ContainsKey(WebServiceSecureHash.Field));
            Assert.Equal(64, json.Str(WebServiceSecureHash.Field).Length);
            Assert.DoesNotContain("secureHashValue", WebServiceSecureHash.Compose(json));
        }

        [Fact]
        public void TheWebServiceAndLanSignaturesLiveInDifferentFields()
        {
            Assert.Equal("secureHashValue", WebServiceSecureHash.Field);
            Assert.Equal("secureHash", SecureHash.Field);
        }

        private static string Wire(JsonObject json) => EcrJson.Write(json);

        private static EcrWebServiceSaleRequest SaleRequest() =>
            new EcrWebServiceSaleRequest(
                MerchantId: MerchantId,
                TerminalId: TerminalId,
                TerminalSerial: TerminalSerial,
                AmountMinorUnits: 111,
                MerchantReference: "EA5C14BDB0A2",
                TransactionDateTime: "20260826115847");

        private static (string Reference, JsonObject Json) BuildSale(string secureHashKey) =>
            EcrWebServiceMessage.Build(
                type: EcrTransactionType.Sale,
                merchantId: MerchantId,
                terminalId: TerminalId,
                terminalSerial: TerminalSerial,
                secureHashKey: secureHashKey,
                amountMinorUnits: 111,
                merchantReference: "EA5C14BDB0A2",
                transactionDateTime: "20260826115847");
    }
}
