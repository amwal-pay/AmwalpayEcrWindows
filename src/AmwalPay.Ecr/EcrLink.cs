namespace AmwalPay.Ecr
{
    /// <summary>
    /// How this till reaches a POS terminal.
    /// <para>
    /// The app supplies addressing and backend IDs; the SDK owns protocol choice,
    /// signing, and transport.
    /// </para>
    /// </summary>
    public abstract record EcrLink
    {
        // Closed hierarchy: only the three links below exist.
        private protected EcrLink()
        {
        }

        /// <summary>Wi‑Fi / LAN — TCP to <see cref="Host"/>:<see cref="Port"/>.</summary>
        public sealed record Lan(string Host, int Port = EcrConfig.DefaultPort) : EcrLink;

        /// <summary>
        /// A USB cable straight from this till to the terminal.
        /// <para>
        /// No address, because no IP is involved. There is nothing to configure,
        /// so there is nothing here to carry — the cable is found by looking at
        /// what is plugged in, by the <see cref="IEcrChannel"/> you supply.
        /// </para>
        /// </summary>
        public sealed record UsbCable : EcrLink;

        /// <summary>Hosted REST — HTTPS with JSON body signing.</summary>
        public sealed record WebService(string MerchantId, string TerminalId) : EcrLink;
    }
}
