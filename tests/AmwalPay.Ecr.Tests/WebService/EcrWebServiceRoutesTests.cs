using AmwalPay.Ecr.Internal;
using AmwalPay.Ecr.WebService;
using Xunit;

namespace AmwalPay.Ecr.Tests.WebService
{
    public class EcrWebServiceRoutesTests
    {
        [Fact]
        public void SitResolvesHubSocketUrlFromInternalConfig()
        {
            var routes = new EcrWebServiceRoutes(EcrEnvironment.SIT);

            Assert.Equal("https://test.amwalpg.com:25452", routes.HubSocketUrl());
            Assert.Equal("/Ecr/Sale", routes.Path(EcrWebServiceOperation.Sale));
            Assert.Equal(
                "https://test.amwalpg.com:25452/Ecr/Void",
                routes.Url(EcrWebServiceOperation.Void));
        }

        [Fact]
        public void EachEnvironmentResolvesADistinctHubHost()
        {
            var sit = EcrEnvironmentUrls.HubSocketUrl(EcrEnvironment.SIT);
            var uat = EcrEnvironmentUrls.HubSocketUrl(EcrEnvironment.UAT);
            var prod = EcrEnvironmentUrls.HubSocketUrl(EcrEnvironment.PROD);

            Assert.Equal("https://test.amwalpg.com:25452", sit);
            Assert.Equal("https://test.amwalpg.com:25453", uat);
            Assert.Equal("https://pos.amwalpg.com", prod);
            Assert.NotEqual(sit, uat);
            Assert.NotEqual(sit, prod);
            Assert.NotEqual(uat, prod);
        }

        [Fact]
        public void ProdSaleUrlDoesNotUseTheTestHost()
        {
            var routes = new EcrWebServiceRoutes(EcrEnvironment.PROD);

            Assert.Equal("https://pos.amwalpg.com/Ecr/Sale", routes.Url(EcrWebServiceOperation.Sale));
        }

        [Fact]
        public void OperationPathsAreFixedAcrossEnvironments()
        {
            var sit = new EcrWebServiceRoutes(EcrEnvironment.SIT);
            var prod = new EcrWebServiceRoutes(new EcrConfig { Environment = EcrEnvironment.PROD });

            Assert.Equal("/Ecr/Inquiry", sit.Url(EcrTransactionType.Inquiry));
            Assert.Equal("/Ecr/Refund", prod.Path(EcrWebServiceOperation.Refund));
            Assert.Equal(
                EcrEnvironmentUrls.HubSocketUrl(EcrEnvironment.PROD),
                prod.HubSocketUrl());
        }

        [Fact]
        public void ReceiptHasNoWebServicePath()
        {
            var routes = new EcrWebServiceRoutes(EcrEnvironment.SIT);

            Assert.Null(routes.Url(EcrTransactionType.Receipt));
        }

        [Fact]
        public void CloseReceiptUsesTheFixedHubPath()
        {
            var routes = new EcrWebServiceRoutes(EcrEnvironment.SIT);

            Assert.Equal("/Ecr/closeReceipt", routes.Path(EcrWebServiceOperation.CloseReceipt));
            Assert.Equal(
                "https://test.amwalpg.com:25452/Ecr/closeReceipt",
                routes.Url(EcrWebServiceOperation.CloseReceipt));
            Assert.Equal("/Ecr/closeReceipt", routes.Url(EcrTransactionType.CloseReceipt));
        }

        [Fact]
        public void EnvironmentFromNameFallsBackToSit()
        {
            Assert.Equal(EcrEnvironment.SIT, EcrEnvironments.FromName(null));
            Assert.Equal(EcrEnvironment.UAT, EcrEnvironments.FromName("uat"));
            Assert.Equal(EcrEnvironment.PROD, EcrEnvironments.FromName("PROD"));
        }

        [Theory]
        [InlineData("  prod  ", EcrEnvironment.PROD)]
        [InlineData("Uat", EcrEnvironment.UAT)]
        [InlineData("production", EcrEnvironment.SIT)]
        [InlineData("", EcrEnvironment.SIT)]
        public void EnvironmentFromNameTrimsIgnoresCaseAndDefaultsOnUnknownNames(string name, EcrEnvironment expected)
        {
            Assert.Equal(expected, EcrEnvironments.FromName(name));
        }

        [Fact]
        public void TheFullUrlOfEveryOperationIsHostPlusItsFixedPath()
        {
            var routes = new EcrWebServiceRoutes(EcrEnvironment.UAT);

            Assert.Equal("https://test.amwalpg.com:25453/Ecr/Sale", routes.FullUrl(EcrWebServiceOperation.Sale));
            Assert.Equal("https://test.amwalpg.com:25453/Ecr/Void", routes.FullUrl(EcrWebServiceOperation.Void));
            Assert.Equal("https://test.amwalpg.com:25453/Ecr/Refund", routes.FullUrl(EcrWebServiceOperation.Refund));
            Assert.Equal(
                "https://test.amwalpg.com:25453/Ecr/Inquiry",
                routes.FullUrl(EcrWebServiceOperation.TransactionStatus));
            Assert.Equal(
                "https://test.amwalpg.com:25453/Ecr/closeReceipt",
                routes.FullUrl(EcrWebServiceOperation.CloseReceipt));
        }

        [Fact]
        public void EveryOperationHasAHumanLabel()
        {
            Assert.Equal("Sale", EcrWebServiceOperation.Sale.Label());
            Assert.Equal("Void", EcrWebServiceOperation.Void.Label());
            Assert.Equal("Refund", EcrWebServiceOperation.Refund.Label());
            Assert.Equal("Transaction status", EcrWebServiceOperation.TransactionStatus.Label());
            Assert.Equal("Close receipt", EcrWebServiceOperation.CloseReceipt.Label());
        }
    }
}
