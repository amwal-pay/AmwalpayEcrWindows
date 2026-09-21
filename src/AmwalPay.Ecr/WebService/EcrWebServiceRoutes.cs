using AmwalPay.Ecr.Internal;

namespace AmwalPay.Ecr.WebService
{
    /// <summary>Resolves the Hub URL and fixed ECR operation paths for an environment.</summary>
    internal sealed class EcrWebServiceRoutes
    {
        private readonly EcrEnvironment _environment;

        public EcrWebServiceRoutes(EcrEnvironment environment) => _environment = environment;

        public EcrWebServiceRoutes(EcrConfig config) : this(config.Environment)
        {
        }

        public string HubSocketUrl() => EcrEnvironmentUrls.HubSocketUrl(_environment);

        public string HubOrigin() => HubSocketUrl();

        public string Path(EcrWebServiceOperation operation) => operation.Path();

        public string Url(EcrWebServiceOperation operation) => FullUrl(operation);

        public string FullUrl(EcrWebServiceOperation operation) =>
            HubSocketUrl().TrimEnd('/') + Path(operation);

        public string? Url(EcrTransactionType type)
        {
            var operation = EcrWebServiceOperations.From(type);
            return operation.HasValue ? Path(operation.Value) : null;
        }
    }
}
