using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AmwalPay.Ecr.Internal;
using AmwalPay.Ecr.WebService;
using Xunit;

namespace AmwalPay.Ecr.Tests.WebService
{
    /// <summary>
    /// The HTTP side of Web Service ECR, against a stub handler. No network: the
    /// point is what the SDK sends and how it turns every outcome into one shape.
    /// </summary>
    public class HttpEcrWebServiceApiTests
    {
        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

            public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
            {
                _respond = respond;
            }

            public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

            public List<string> Bodies { get; } = new List<string>();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                Bodies.Add(request.Content == null ? "" : await request.Content.ReadAsStringAsync().ConfigureAwait(false));
                return await _respond(request, cancellationToken).ConfigureAwait(false);
            }
        }

        private static HttpResponseMessage Reply(HttpStatusCode status, string body, string? reason = null) =>
            new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
                ReasonPhrase = reason,
            };

        private static (HttpEcrWebServiceApi Api, StubHandler Handler, List<string> Log) Api(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond,
            EcrConfig? config = null)
        {
            var handler = new StubHandler(respond);
            var log = new List<string>();
            var api = new HttpEcrWebServiceApi(
                config ?? EcrTestConfigs.WebService,
                EcrLogger.From(log.Add),
                new HttpClient(handler));
            return (api, handler, log);
        }

        private static System.Text.Json.Nodes.JsonObject SaleBody(string key = "") =>
            EcrWebServiceRequestJson.Sale(
                new EcrWebServiceSaleRequest(13593, 742001, "TW1", 111, "REF-1", TransactionDateTime: "20260826115847"),
                key);

        [Fact]
        public async Task ASaleIsPostedAsJsonToTheEnvironmentsSaleUrl()
        {
            var (api, handler, _) = Api((_, __) => Task.FromResult(Reply(HttpStatusCode.OK, """{"success":true,"responseCode":"00"}""")));
            var body = SaleBody();

            var answer = await api.PostJsonAsync(EcrWebServiceOperation.Sale, body);

            Assert.True(answer.Flag("success"));
            var request = Assert.Single(handler.Requests);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://test.amwalpg.com:25453/Ecr/Sale", request.RequestUri!.ToString());
            Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
            Assert.Contains(request.Headers.Accept, a => a.MediaType == "application/json");
            Assert.Equal(EcrJson.Write(body), Assert.Single(handler.Bodies));
        }

        [Fact]
        public async Task EachOperationGoesToItsOwnPath()
        {
            var (api, handler, _) = Api((_, __) => Task.FromResult(Reply(HttpStatusCode.OK, """{"success":true}""")));

            foreach (var operation in new[]
            {
                EcrWebServiceOperation.Sale,
                EcrWebServiceOperation.Void,
                EcrWebServiceOperation.Refund,
                EcrWebServiceOperation.TransactionStatus,
            })
            {
                await api.PostJsonAsync(operation, SaleBody());
            }

            Assert.Equal(
                new[] { "/Ecr/Sale", "/Ecr/Void", "/Ecr/Refund", "/Ecr/Inquiry" },
                handler.Requests.ConvertAll(r => r.RequestUri!.AbsolutePath).ToArray());
        }

        [Fact]
        public async Task TheBodyGoesOutAsUtf8WithoutAByteOrderMark()
        {
            byte[]? seen = null;
            var (api, _, _) = Api(async (request, ct) =>
            {
                seen = await request.Content!.ReadAsByteArrayAsync().ConfigureAwait(false);
                return Reply(HttpStatusCode.OK, """{"success":true}""");
            });
            var body = EcrWebServiceRequestJson.Sale(
                new EcrWebServiceSaleRequest(1, 2, "Café", 111, "REF-1", TransactionDateTime: "20260826115847"));

            await api.PostJsonAsync(EcrWebServiceOperation.Sale, body);

            Assert.NotNull(seen);
            Assert.Equal((byte)'{', seen![0]);
            Assert.Contains("\"terminalSerial\":\"Café\"", Encoding.UTF8.GetString(seen));
        }

        [Fact]
        public async Task AnHttpErrorCarryingAnEnvelopeIsReturnedAsTheEnvelope()
        {
            var (api, _, _) = Api((_, __) => Task.FromResult(Reply(
                HttpStatusCode.BadRequest,
                """{"success":false,"responseCode":"400","message":"Invalid signature","errorList":["secureHashValue mismatch"]}""")));

            var answer = await api.PostJsonAsync(EcrWebServiceOperation.Sale, SaleBody());

            var wire = EcrWireResponse.Parse(answer);
            Assert.False(wire.Success);
            Assert.Equal("400", wire.ResponseCode);
            Assert.Equal("secureHashValue mismatch", wire.DisplayMessage);
        }

        [Fact]
        public async Task AnHttpErrorWithNothingReadableBecomesAFailureEnvelopeWithTheStatus()
        {
            var (api, _, _) = Api((_, __) => Task.FromResult(Reply(HttpStatusCode.InternalServerError, "{}", "Server Error")));

            var answer = await api.PostJsonAsync(EcrWebServiceOperation.Sale, SaleBody());

            var wire = EcrWireResponse.Parse(answer);
            Assert.False(wire.Success);
            Assert.Equal("500", wire.ResponseCode);
            Assert.Equal("Server Error", wire.Message);
        }

        [Fact]
        public async Task AnEmptyBodyBecomesAFailureEnvelope()
        {
            var (api, _, log) = Api((_, __) => Task.FromResult(Reply(HttpStatusCode.OK, "  ")));

            var wire = EcrWireResponse.Parse(await api.PostJsonAsync(EcrWebServiceOperation.Sale, SaleBody()));

            Assert.False(wire.Success);
            Assert.Equal("200", wire.ResponseCode);
            Assert.Contains("Empty response", wire.Message);
            Assert.Contains(log, line => line.Contains("Empty response"));
        }

        [Theory]
        [InlineData("<html>bad gateway</html>")]
        [InlineData("null")]
        [InlineData("[1,2]")]
        [InlineData("\"ok\"")]
        [InlineData("{\"success\":false,\"success\":true}")]
        public async Task ABodyThatIsNotAJsonObjectBecomesAFailureEnvelopeNotACrash(string text)
        {
            var (api, _, _) = Api((_, __) => Task.FromResult(Reply(HttpStatusCode.OK, text)));

            var wire = EcrWireResponse.Parse(await api.PostJsonAsync(EcrWebServiceOperation.Sale, SaleBody()));

            Assert.False(wire.Success);
            Assert.Contains("not valid JSON", wire.Message);
        }

        [Fact]
        public async Task ABrokenConnectionBecomesANoResponseEnvelope()
        {
            var (api, _, _) = Api((_, __) => throw new HttpRequestException("Name or service not known"));

            var wire = EcrWireResponse.Parse(await api.PostJsonAsync(EcrWebServiceOperation.Sale, SaleBody()));

            Assert.False(wire.Success);
            Assert.Equal("NO_RESPONSE", wire.ResponseCode);
            Assert.Contains("Name or service not known", wire.Message);
        }

        [Fact]
        public async Task ARequestThatOutlivesItsDeadlineBecomesATimedOutEnvelope()
        {
            var config = EcrTestConfigs.WebService with
            {
                ConnectTimeout = TimeSpan.FromMilliseconds(100),
                ResponseTimeout = TimeSpan.FromMilliseconds(150),
            };
            var (api, _, _) = Api(async (_, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                return Reply(HttpStatusCode.OK, "{}");
            }, config);

            var wire = EcrWireResponse.Parse(await api.PostJsonAsync(EcrWebServiceOperation.Sale, SaleBody()));

            Assert.False(wire.Success);
            Assert.Equal("NO_RESPONSE", wire.ResponseCode);
            Assert.Contains("Timed out after", wire.Message);
        }

        [Fact]
        public async Task ACallersCancellationIsCancellationNotAFailureEnvelope()
        {
            var (api, _, _) = Api(async (_, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                return Reply(HttpStatusCode.OK, "{}");
            });
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromMilliseconds(100));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                api.PostJsonAsync(EcrWebServiceOperation.Sale, SaleBody(), cts.Token));
        }

        [Fact]
        public async Task ASignedBodyIsLoggedWithoutTheSecret()
        {
            var (api, _, log) = Api((_, __) => Task.FromResult(Reply(HttpStatusCode.OK, """{"success":true}""")));

            await api.PostJsonAsync(EcrWebServiceOperation.Sale, SaleBody(EcrTestConfigs.SecureHashKeyWebService));

            Assert.NotEmpty(log);
            Assert.DoesNotContain(log, line => line.IndexOf(EcrTestConfigs.SecureHashKeyWebService, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Fact]
        public async Task TheHighLevelApiSignsAndPostsASale()
        {
            var (api, handler, _) = Api((_, __) => Task.FromResult(Reply(HttpStatusCode.OK, """{"success":true,"responseCode":"00"}""")));

            var wire = await api.SaleAsync(new EcrWebServiceSaleRequest(
                13593, 742001, "TW1", 111, "REF-1", TransactionDateTime: "20260826115847"));

            Assert.True(wire.Success);
            var sent = Wire.Obj(Assert.Single(handler.Bodies));
            Assert.Equal("SALE", sent.Str("messageType"));
            Assert.Equal(64, sent.Str(WebServiceSecureHash.Field).Length);
            Assert.Equal(
                WebServiceSecureHash.Sign(sent, EcrTestConfigs.SecureHashKeyWebService),
                sent.Str(WebServiceSecureHash.Field));
        }
    }
}
