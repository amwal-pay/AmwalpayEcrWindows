using System;

namespace AmwalPay.Ecr.Internal
{
    /// <summary>
    /// ECR Hub base URLs per <see cref="EcrEnvironment"/> — SDK-internal only.
    /// <para>
    /// Mirrors Amwal POS environment hosts: test SIT/UAT ports and production on
    /// <c>pos.amwalpg.com</c>. Paths (<c>/Ecr/Sale</c>, …) are appended by
    /// <c>EcrWebServiceRoutes</c>.
    /// </para>
    /// </summary>
    internal static class EcrEnvironmentUrls
    {
        private const string SitHost = "https://test.amwalpg.com:25452";
        private const string UatHost = "https://test.amwalpg.com:15452";
        private const string ProdHost = "https://pos.amwalpg.com";

        public static string HubSocketUrl(EcrEnvironment environment)
        {
            var host = environment switch
            {
                EcrEnvironment.SIT => SitHost,
                EcrEnvironment.UAT => UatHost,
                EcrEnvironment.PROD => ProdHost,
                _ => throw new ArgumentOutOfRangeException(nameof(environment)),
            };
            return host.TrimEnd('/');
        }
    }
}
