using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AmwalPay.Ecr.Tests
{
    /// <summary>
    /// The SDK is safe to call from code that blocks on it.
    /// <para>
    /// Classic ASP.NET, WinForms and WPF handlers commonly do <c>SaleAsync(...).Result</c>. That
    /// only works if the library never resumes on the caller's synchronization context: one
    /// <c>await</c> missing <c>ConfigureAwait(false)</c> and the blocked thread waits for a
    /// continuation that is queued behind itself, so a payment call hangs forever. This is a
    /// property of every path that awaits real I/O, so each is exercised here from a
    /// single-threaded context, the way a UI thread or a legacy request thread behaves.
    /// </para>
    /// </summary>
    public class BlockingCallerTests
    {
        private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

        private const string ApprovedAnswer =
            """{"success":true,"responseCode":"00","message":"Approved","data":{"amount":"1.234","rrn":"123456789012","authCode":"AB12CD"}}""";

        /// <summary>One thread that runs queued work, like a UI thread or a classic ASP.NET request context.</summary>
        private sealed class SingleThreadContext : SynchronizationContext, IDisposable
        {
            private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue =
                new BlockingCollection<(SendOrPostCallback, object?)>();
            private readonly Thread _thread;

            public SingleThreadContext()
            {
                _thread = new Thread(() =>
                {
                    SetSynchronizationContext(this);
                    foreach (var (callback, state) in _queue.GetConsumingEnumerable())
                    {
                        callback(state);
                    }
                }) { IsBackground = true };
                _thread.Start();
            }

            public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

            /// <summary>Runs <paramref name="work"/> on the context thread; fails the test if it does not finish.</summary>
            public T RunBlocking<T>(Func<T> work)
            {
                var done = new TaskCompletionSource<T>();
                Post(_ =>
                {
                    try
                    {
                        done.SetResult(work());
                    }
                    catch (Exception e)
                    {
                        done.SetException(e);
                    }
                }, null);

                if (!done.Task.Wait(Deadline))
                {
                    throw new TimeoutException("Deadlock: blocking on the call from a single-threaded context never returned.");
                }
                return done.Task.Result;
            }

            public void Dispose() => _queue.CompleteAdding();
        }

        [Fact]
        public void ALanSaleCanBeBlockedOnFromASingleThreadedContext()
        {
            using var server = FakeTcpTerminal.Always(TcpReply.Answer(ApprovedAnswer));
            using var context = new SingleThreadContext();
            var terminal = new EcrTerminal("127.0.0.1", "TW1", new EcrConfig { Port = server.Port });

            var result = context.RunBlocking(() => terminal.SaleAsync(1.234m, "ORD-1").GetAwaiter().GetResult());

            Assert.IsType<EcrResult.Approved>(result);
        }

        [Fact]
        public void ALanProbeCanBeBlockedOnFromASingleThreadedContext()
        {
            using var server = FakeTcpTerminal.Always(TcpReply.Answer(ApprovedAnswer));
            using var context = new SingleThreadContext();
            var terminal = new EcrTerminal("127.0.0.1", "TW1", new EcrConfig { Port = server.Port });

            Assert.True(context.RunBlocking(() => terminal.IsReachableAsync().GetAwaiter().GetResult()));
        }

        [Fact]
        public void ALostAnswerAndItsAutomaticInquiryCanBeBlockedOnFromASingleThreadedContext()
        {
            // The recovery path awaits a second exchange after the first one failed.
            using var server = new FakeTcpTerminal((_, connection) => connection == 0 ? TcpReply.Drop : TcpReply.Answer(ApprovedAnswer));
            using var context = new SingleThreadContext();
            var terminal = new EcrTerminal("127.0.0.1", "TW1", new EcrConfig { Port = server.Port });

            var result = context.RunBlocking(() => terminal.SaleAsync(1.234m, "ORD-1").GetAwaiter().GetResult());

            Assert.IsType<EcrResult.Failed>(result);
        }

        [Fact]
        public void ATimedOutAnswerCanBeBlockedOnFromASingleThreadedContext()
        {
            using var server = FakeTcpTerminal.Always(TcpReply.Stall);
            using var context = new SingleThreadContext();
            var terminal = new EcrTerminal(
                "127.0.0.1", "TW1", new EcrConfig { Port = server.Port, ResponseTimeout = TimeSpan.FromMilliseconds(300), AutoInquireOnFailure = false });

            var result = context.RunBlocking(() => terminal.SaleAsync(1.234m, "ORD-1").GetAwaiter().GetResult());

            Assert.IsType<Failure.Timeout>(Assert.IsType<EcrResult.Failed>(result).Failure);
        }

        [Fact]
        public void AWebServiceSaleCanBeBlockedOnFromASingleThreadedContext()
        {
            // Completes asynchronously, so the continuation really is queued to the context if the
            // library asks for it.
            var handler = new DelegateHandler(async () =>
            {
                await Task.Delay(50).ConfigureAwait(false);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(ApprovedAnswer, Encoding.UTF8, "application/json"),
                };
            });
            using var context = new SingleThreadContext();
            var terminal = new EcrWebServiceTerminal("TW1", EcrTestConfigs.WebService, EcrLogger.None, new HttpClient(handler));

            var result = context.RunBlocking(() => terminal.SaleAsync(1.234m, "ORD-1").GetAwaiter().GetResult());

            Assert.IsType<EcrResult.Approved>(result);
        }

        private sealed class DelegateHandler : HttpMessageHandler
        {
            private readonly Func<Task<HttpResponseMessage>> _respond;

            public DelegateHandler(Func<Task<HttpResponseMessage>> respond) => _respond = respond;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => _respond();
        }
    }
}
