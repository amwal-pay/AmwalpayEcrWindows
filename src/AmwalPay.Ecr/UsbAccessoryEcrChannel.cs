using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AmwalPay.Ecr
{
    /// <summary>
    /// Reaches the terminal down a USB cable, with no IP anywhere.
    /// <para>
    /// This till is the USB host and the terminal is the accessory. Android Open
    /// Accessory is what makes that pair talk: ask the terminal what protocol it
    /// speaks, say who is calling, then tell it to switch. It drops off the bus and
    /// comes back under Google's ids with one bulk pipe each way, and those two
    /// pipes are the whole link.
    /// </para>
    /// Bytes only. Signing the request, checking the answer, and deciding what a
    /// response code means stay in <see cref="EcrTerminal"/>, the same as Wi-Fi.
    /// Windows opens the device with WinUSB. macOS and Linux open it with libusb.
    /// </summary>
    public sealed class UsbAccessoryEcrChannel : IEcrChannel
    {
        private readonly IUsbAccessoryBus _bus;

        public UsbAccessoryEcrChannel()
            : this(CreateBus())
        {
        }

        private static IUsbAccessoryBus CreateBus() =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new WinUsbAccessoryBus()
                : new LibUsbAccessoryBus();

        internal UsbAccessoryEcrChannel(IUsbAccessoryBus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        }

        public string Endpoint => "USB cable";

        public string Host => Endpoint;

        public int Port => 0;

        public Task<string?> ProbeAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(_bus.Unavailable)) return Task.FromResult<string?>(_bus.Unavailable);

            try
            {
                return Task.FromResult(Probe());
            }
            catch (Exception e) when (e is IOException || e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                return Task.FromResult<string?>(e.Message);
            }
        }

        public Task<byte[]> ExchangeAsync(byte[] body, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (!string.IsNullOrEmpty(_bus.Unavailable)) throw new IOException(_bus.Unavailable);
            return Task.Run(() => Exchange(body, timeout, cancellationToken), cancellationToken);
        }

        private string? Probe()
        {
            _bus.PrepareForTerminal();
            var plugs = _bus.List();
            if (FindAccessory(plugs) != null) return null;

            string? openProblem = null;
            var opened = false;
            foreach (var plug in plugs)
            {
                if (plug.VendorId == AoaVendor && IsAoaProduct(plug.ProductId)) continue;
                try
                {
                    if (SpeaksAccessory(plug)) return null;
                    opened = true;
                }
                catch (IOException e)
                {
                    openProblem ??= e.Message;
                }
            }

            if (plugs.Count == 0) return _bus.NothingPluggedIn;
            if (!opened && openProblem != null) return openProblem;
            return "A USB device is connected but it is not a terminal in accessory mode.";
        }

        private byte[] Exchange(byte[] body, TimeSpan timeout, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var pipe = OpenAccessory(cancellationToken);
            pipe.DiscardStale();

            var expectedNonce = NonceOf(body);
            var packet = EcrFrames.Wrap(body);
            var written = pipe.Bulk(input: false, packet, packet.Length, WriteTimeoutMs);
            if (written < packet.Length)
            {
                throw new IOException($"Only {written} of {packet.Length} bytes reached the terminal");
            }

            return ReadFrame(pipe, timeout, expectedNonce, cancellationToken);
        }

        private byte[] ReadFrame(IUsbPipe pipe, TimeSpan timeout, string expectedNonce, CancellationToken cancellationToken)
        {
            var buffer = new byte[ReadSize];
            var pending = new List<byte>();
            var deadline = DateTime.UtcNow + timeout;
            var nextPresenceCheck = DateTime.UtcNow + TimeSpan.FromMilliseconds(PresenceCheckMs);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    throw new EcrChannelTimeout("the terminal did not answer in time");
                }

                var slice = (int)Math.Min(remaining.TotalMilliseconds, PresenceCheckMs);
                var read = pipe.Bulk(input: true, buffer, buffer.Length, slice);
                if (read < 0)
                {
                    if (DateTime.UtcNow >= deadline)
                    {
                        throw new EcrChannelTimeout("the terminal did not answer in time");
                    }

                    if (DateTime.UtcNow >= nextPresenceCheck)
                    {
                        nextPresenceCheck = DateTime.UtcNow + TimeSpan.FromMilliseconds(PresenceCheckMs);
                        if (!pipe.StillPresent || FindAccessory(_bus.List()) == null)
                        {
                            throw new IOException(
                                "The terminal left accessory mode — nothing on the cable is listening. Check that its ECR mode is still USB cable.");
                        }
                    }

                    continue;
                }

                if (read == 0) continue;
                for (var i = 0; i < read; i++) pending.Add(buffer[i]);

                while (pending.Count >= EcrFrames.HeaderBytes)
                {
                    var header = new[] { pending[0], pending[1] };
                    var length = EcrFrames.BodyLength(header);
                    if (length <= 0) throw new IOException("Terminal returned an empty message");
                    if (pending.Count < EcrFrames.HeaderBytes + length) break;

                    var frame = new byte[length];
                    pending.CopyTo(EcrFrames.HeaderBytes, frame, 0, length);
                    pending.RemoveRange(0, EcrFrames.HeaderBytes + length);

                    if (expectedNonce.Length == 0) return frame;
                    var answered = NonceOf(frame);
                    if (answered.Length == 0 || answered == expectedNonce) return frame;
                }
            }
        }

        /// <summary>
        /// Opens the accessory pipes. Windows lists the composite parent and each
        /// interface separately; the accessory is interface 0, and a parent that
        /// is not WinUSB is skipped.
        /// </summary>
        private IUsbPipe OpenAccessory(CancellationToken cancellationToken)
        {
            _bus.PrepareForTerminal();
            IOException? last = null;
            for (var attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    var opened = TryCandidates(OrderedAccessories(_bus.List()));
                    if (opened != null) return opened;
                    break;
                }
                catch (IOException e)
                {
                    last = e;
                    Thread.Sleep(ReconnectIntervalMs);
                }
            }

            SwitchIntoAccessoryMode(cancellationToken);
            for (var attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    var opened = TryCandidates(OrderedAccessories(_bus.List()));
                    if (opened != null) return opened;
                }
                catch (IOException e)
                {
                    last = e;
                }

                Thread.Sleep(ReconnectIntervalMs);
            }

            throw last ?? new IOException(
                "The terminal did not come back in accessory mode. Check the cable and that the POS app is on USB cable mode.");
        }

        private IUsbPipe? TryCandidates(IReadOnlyList<UsbPlug> candidates)
        {
            IOException? last = null;
            foreach (var plug in candidates)
            {
                try
                {
                    return _bus.Open(plug);
                }
                catch (IOException e)
                {
                    last = e;
                }
            }

            if (last != null && candidates.Count > 0) throw last;
            return null;
        }

        private UsbPlug? SwitchIntoAccessoryMode(CancellationToken cancellationToken)
        {
            string? openProblem = null;
            var opened = false;
            foreach (var plug in _bus.List())
            {
                if (plug.VendorId == AoaVendor && IsAoaProduct(plug.ProductId)) continue;
                bool started;
                try
                {
                    started = StartAccessory(plug);
                }
                catch (IOException e)
                {
                    openProblem ??= e.Message;
                    continue;
                }

                opened = true;
                if (!started) continue;

                for (var attempt = 0; attempt < ReconnectAttempts; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Thread.Sleep(ReconnectIntervalMs);
                    var accessory = FindAccessory(_bus.List());
                    if (accessory != null) return accessory;
                }
            }

            if (!opened && openProblem != null) throw new IOException(openProblem);
            return null;
        }

        /// <summary>Whether this device answers the accessory protocol request.</summary>
        private bool SpeaksAccessory(UsbPlug plug)
        {
            if (plug.VendorId == AoaVendor && IsAoaProduct(plug.ProductId)) return false;
            using var pipe = _bus.Open(plug);
            return ProtocolVersion(pipe) >= 1;
        }

        /// <summary>Tells a device that speaks accessory mode to switch. Returns false when it does not.</summary>
        private bool StartAccessory(UsbPlug plug)
        {
            using var pipe = _bus.Open(plug);
            if (ProtocolVersion(pipe) < 1) return false;

            for (var index = 0; index < Identity.Length; index++)
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(Identity[index] + "\0");
                pipe.Control(0x40, SendString, 0, index, bytes, bytes.Length, ControlTimeoutMs);
            }

            pipe.Control(0x40, Start, 0, 0, null, 0, ControlTimeoutMs);
            return true;
        }

        private static int ProtocolVersion(IUsbPipe pipe)
        {
            var version = new byte[2];
            var read = pipe.Control(0xC0, GetProtocol, 0, 0, version, version.Length, ProtocolTimeoutMs);
            if (read < 2) return 0;
            return version[0] | (version[1] << 8);
        }

        private static UsbPlug? FindAccessory(IReadOnlyList<UsbPlug> plugs)
        {
            var ordered = OrderedAccessories(plugs);
            return ordered.Count == 0 ? null : ordered[0];
        }

        /// <summary>Accessory interfaces first (MI_00), then the composite parent, then any other interface.</summary>
        private static List<UsbPlug> OrderedAccessories(IReadOnlyList<UsbPlug> plugs)
        {
            var found = new List<UsbPlug>();
            foreach (var plug in plugs)
            {
                if (plug.VendorId == AoaVendor && IsAoaProduct(plug.ProductId)) found.Add(plug);
            }

            found.Sort((a, b) => AccessoryRank(a.Id).CompareTo(AccessoryRank(b.Id)));
            return found;
        }

        private static int AccessoryRank(string id)
        {
            var path = id.ToLowerInvariant();
            if (path.Contains("mi_00")) return 0;
            if (path.Contains("mi_")) return 2;
            return 1;
        }

        private static bool IsAoaProduct(int productId) =>
            productId == 0x2D00 || productId == 0x2D01 || productId == 0x2D04 || productId == 0x2D05;

        private static string NonceOf(byte[] body)
        {
            if (body.Length == 0) return "";
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.TryGetProperty("nonce", out var nonce) && nonce.ValueKind == JsonValueKind.String)
                {
                    return nonce.GetString() ?? "";
                }
            }
            catch (JsonException)
            {
            }

            return "";
        }

        private const int AoaVendor = 0x18D1;
        private const int GetProtocol = 51;
        private const int SendString = 52;
        private const int Start = 53;
        private const int ProtocolTimeoutMs = 500;
        private const int ControlTimeoutMs = 2_000;
        private const int WriteTimeoutMs = 3_000;
        private const int ReconnectAttempts = 30;
        private const int ReconnectIntervalMs = 500;
        private const int ReadSize = 16 * 1024;
        private const int PresenceCheckMs = 1_000;

        /// <summary>
        /// Must match <c>usb_accessory_filter.xml</c> in the POS app, or Android
        /// hands the cable to nobody and the terminal never sees it.
        /// </summary>
        private static readonly string[] Identity =
        {
            "AmwalPay",
            "EcrBridge",
            "ECR requests over the cable",
            "1.0",
            "https://amwal-pay.com",
            "0000000000000001",
        };
    }

    internal interface IUsbAccessoryBus
    {
        /// <summary>Why this process cannot open a cable, or null when it can try.</summary>
        string? Unavailable { get; }

        /// <summary>What to tell the operator when <see cref="List"/> is empty.</summary>
        string NothingPluggedIn { get; }

        /// <summary>
        /// Binds whatever driver the host needs before the first open. On Windows
        /// that is the built-in WinUSB driver, attached to the terminal's own
        /// interface. Other hosts do nothing.
        /// </summary>
        void PrepareForTerminal();

        IReadOnlyList<UsbPlug> List();

        IUsbPipe Open(UsbPlug plug);
    }

    internal sealed class UsbPlug
    {
        public UsbPlug(string id, int vendorId, int productId)
        {
            Id = id;
            VendorId = vendorId;
            ProductId = productId;
        }

        public string Id { get; }

        public int VendorId { get; }

        public int ProductId { get; }
    }

    internal interface IUsbPipe : IDisposable
    {
        bool StillPresent { get; }

        /// <summary>Bytes transferred, or -1 when the pipe timed out or failed.</summary>
        int Control(byte requestType, byte request, int value, int index, byte[]? buffer, int length, int timeoutMs);

        int Bulk(bool input, byte[] buffer, int count, int timeoutMs);

        void DiscardStale();
    }
}
