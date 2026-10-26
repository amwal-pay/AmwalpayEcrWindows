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

        /// <summary>Wi‑Fi — TCP to <see cref="Host"/>:<see cref="Port"/>.</summary>
        public sealed record Lan(string Host, int Port = EcrConfig.DefaultPort) : EcrLink;

        /// <summary>
        /// A USB cable straight from this till to the terminal.
        /// <para>
        /// <see cref="ComPort"/> is the virtual serial port Windows assigned to
        /// that cable, such as <c>COM8</c> ("MediaTek USB VCOM"). The line is
        /// 115200 8N1. Empty means the operator has not chosen a port yet.
        /// </para>
        /// </summary>
        public sealed record UsbCable(string ComPort = "") : EcrLink;

        /// <summary>Hosted REST — HTTPS with JSON body signing.</summary>
        public sealed record WebService(string MerchantId, string TerminalId) : EcrLink;
    }
}
