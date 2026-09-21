using System;

namespace AmwalPay.Ecr
{
    /// <summary>
    /// Where the SDK writes its diagnostics.
    /// <para>
    /// It has no logger of its own, so a client can route this to
    /// <c>Microsoft.Extensions.Logging</c>, Serilog, the Windows Event Log or a
    /// plain file, and none inherits a dependency it did not ask for:
    /// </para>
    /// <code>
    /// var logger = EcrLogger.From(message => Debug.WriteLine(message));
    /// </code>
    /// Messages contain request and response payloads, which include the masked
    /// card number. They are safe to keep, but treat them as transaction records.
    /// </summary>
    public interface IEcrLogger
    {
        void Debug(string message);
    }

    public static class EcrLogger
    {
        /// <summary>Discards everything. The default.</summary>
        public static IEcrLogger None { get; } = new Delegating(_ => { });

        /// <summary>Wraps a delegate as a logger.</summary>
        public static IEcrLogger From(Action<string> sink)
        {
            if (sink == null) throw new ArgumentNullException(nameof(sink));
            return new Delegating(sink);
        }

        private sealed class Delegating : IEcrLogger
        {
            private readonly Action<string> _sink;

            public Delegating(Action<string> sink) => _sink = sink;

            public void Debug(string message) => _sink(message);
        }
    }
}
