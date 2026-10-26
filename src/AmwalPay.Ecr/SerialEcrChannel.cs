using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

#pragma warning disable CA1416 // The cable is a Windows COM port. The same source also builds on macOS, where listing ports is a no-op.

namespace AmwalPay.Ecr
{
    /// <summary>
    /// One serial device Windows may assign a COM port. <see cref="Name"/> is the
    /// COM port assigned right now. <see cref="DeviceSerial"/> is the USB serial
    /// Plug and Play remembers, and <see cref="Selection"/> is what the till stores.
    /// </summary>
    public sealed class SerialPortChoice
    {
        public SerialPortChoice(string name, string label, string deviceSerial = "")
        {
            Name = name ?? "";
            DeviceSerial = deviceSerial ?? "";
            Label = string.IsNullOrWhiteSpace(label) ? Name : label;
        }

        /// <summary>The COM port Windows has assigned at this moment, such as COM3.</summary>
        public string Name { get; }

        /// <summary>Device Manager text when Windows has it, otherwise <see cref="Name"/>.</summary>
        public string Label { get; }

        /// <summary>
        /// USB serial from the Plug and Play instance. Empty when the device has
        /// no serial and the COM port follows the USB socket instead.
        /// </summary>
        public string DeviceSerial { get; }

        /// <summary>Stable identity to save: the device serial when it has one, otherwise the COM name.</summary>
        public string Selection => DeviceSerial.Length != 0 ? DeviceSerial : Name;
    }

    /// <summary>
    /// The ports to show for one terminal, and the row to select.
    /// <see cref="Ports"/> is only what is plugged in on this call.
    /// </summary>
    public sealed class ConnectedPorts
    {
        public ConnectedPorts(IReadOnlyList<SerialPortChoice> ports, string selected)
        {
            Ports = ports ?? Array.Empty<SerialPortChoice>();
            Selected = selected ?? "";
        }

        public IReadOnlyList<SerialPortChoice> Ports { get; }

        /// <summary>The <see cref="SerialPortChoice.Selection"/> to store on the terminal.</summary>
        public string Selected { get; }
    }

    /// <summary>
    /// The COM port to open for a saved cable, and the identity to store so the
    /// next sale follows the device when Windows assigns a different COM number.
    /// </summary>
    public sealed class SerialPortBinding
    {
        public SerialPortBinding(string port, string remember)
        {
            Port = port ?? "";
            Remember = remember ?? "";
        }

        /// <summary>COM port to open now. Empty when that device is unplugged.</summary>
        public string Port { get; }

        /// <summary>USB serial when the device has one, otherwise the saved COM name.</summary>
        public string Remember { get; }
    }

    /// <summary>
    /// COM ports this computer can open. Windows labels come from Device Manager,
    /// so "MediaTek USB VCOM (Android) (COM8)" can be told apart from the other
    /// USB serial devices. The Mac call-in twin is hidden, and COM numbers are
    /// ordered numerically.
    /// </summary>
    public static class SerialPorts
    {
        /// <summary>
        /// Ports for the terminal form. <paramref name="saved"/> is the identity
        /// already stored. <paramref name="terminalSerial"/> wins when it equals
        /// a plugged-in USB serial, so the row follows that terminal.
        /// </summary>
        public static ConnectedPorts ListFor(string saved, string terminalSerial) =>
            ListFor(saved, terminalSerial, List());

        public static ConnectedPorts ListFor(string saved, string terminalSerial, IReadOnlyList<SerialPortChoice> ports)
        {
            var listed = new List<SerialPortChoice>();
            if (ports != null)
            {
                foreach (var port in ports)
                {
                    if (port != null) listed.Add(port);
                }
            }

            var selected = (saved ?? "").Trim();
            var serial = (terminalSerial ?? "").Trim();
            SerialPortChoice? byTerminal = null;
            SerialPortChoice? bySaved = null;
            foreach (var port in listed)
            {
                if (serial.Length != 0 && port.DeviceSerial.Length != 0
                    && string.Equals(port.DeviceSerial, serial, StringComparison.OrdinalIgnoreCase))
                {
                    byTerminal = port;
                }

                if (selected.Length != 0 && (
                        string.Equals(port.Selection, selected, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(port.Name, selected, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(port.DeviceSerial, selected, StringComparison.OrdinalIgnoreCase)))
                {
                    bySaved = port;
                }
            }

            var match = byTerminal ?? bySaved;
            var choice = match != null ? match.Selection : Preferred(listed, serial);
            return new ConnectedPorts(listed, choice);
        }

        /// <summary>The COM port a sale opens for <paramref name="saved"/>, and what to store afterwards.</summary>
        public static SerialPortBinding PortFor(string saved) => PortFor(saved, List());

        public static SerialPortBinding PortFor(string saved, IReadOnlyList<SerialPortChoice> ports)
        {
            var key = (saved ?? "").Trim();
            var live = Resolve(key, ports);
            if (live.Length == 0) return new SerialPortBinding("", key);
            if (!IsDirectPort(key) || ports == null) return new SerialPortBinding(live, key);
            foreach (var port in ports)
            {
                if (port != null
                    && string.Equals(port.Name, live, StringComparison.OrdinalIgnoreCase)
                    && port.DeviceSerial.Length != 0)
                {
                    return new SerialPortBinding(live, port.DeviceSerial);
                }
            }

            return new SerialPortBinding(live, key);
        }

        /// <summary>
        /// Serial devices plugged in right now. Asked of the operating system on
        /// every call. A COM port remembered from an earlier plug is not included.
        /// </summary>
        public static IReadOnlyList<SerialPortChoice> List()
        {
            string[] present;
            try
            {
                present = SerialPort.GetPortNames();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is PlatformNotSupportedException || e is System.Security.SecurityException)
            {
                return Array.Empty<SerialPortChoice>();
            }

            var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in present)
            {
                if (!string.IsNullOrWhiteSpace(name)) live.Add(name.Trim());
            }

            var described = Described();
            if (described.Count == 0)
            {
                var plain = new List<SerialPortChoice>();
                foreach (var name in live) plain.Add(new SerialPortChoice(name, name));
                return Present(plain);
            }

            var matched = new List<SerialPortChoice>();
            var describedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var port in described)
            {
                if (port == null || port.Name.Length == 0) continue;
                describedNames.Add(port.Name);
                if (live.Contains(port.Name)) matched.Add(port);
            }

            foreach (var name in live)
            {
                if (!describedNames.Contains(name)) matched.Add(new SerialPortChoice(name, name));
            }

            return Present(matched);
        }

        /// <summary>
        /// The device to offer first. A terminal serial that equals a USB serial
        /// wins. Otherwise the Android virtual COM ("MediaTek USB VCOM"), then
        /// the first name in list order. The result is <see cref="SerialPortChoice.Selection"/>,
        /// so a later sale can find the device after Windows moves its COM port.
        /// </summary>
        public static string Preferred(IReadOnlyList<SerialPortChoice> ports) => Preferred(ports, "");

        public static string Preferred(IReadOnlyList<SerialPortChoice> ports, string terminalSerial)
        {
            if (ports == null || ports.Count == 0) return "";
            var wanted = (terminalSerial ?? "").Trim();
            if (wanted.Length != 0)
            {
                foreach (var port in ports)
                {
                    if (port.DeviceSerial.Length != 0 && string.Equals(port.DeviceSerial, wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        return port.Selection;
                    }
                }
            }

            foreach (var port in ports)
            {
                if (IsVirtualCom(port.Label) || IsVirtualCom(port.Name)) return port.Selection;
            }

            return ports[0].Selection;
        }

        /// <summary>
        /// The COM port to open for a saved cable identity. A device serial is
        /// looked up again, so a terminal that moved to another USB socket is
        /// opened on the COM port Windows assigned this time. A saved COM name
        /// is opened as that name until the till has learned the device serial.
        /// An unplugged device serial returns empty: a stale COM number may now
        /// belong to a different device.
        /// </summary>
        public static string Resolve(string saved) => Resolve(saved, List());

        public static string Resolve(string saved, IReadOnlyList<SerialPortChoice> ports)
        {
            var key = (saved ?? "").Trim();
            if (key.Length == 0) return "";
            if (ports != null)
            {
                foreach (var port in ports)
                {
                    if (port != null && port.DeviceSerial.Length != 0 && string.Equals(port.DeviceSerial, key, StringComparison.OrdinalIgnoreCase))
                    {
                        return port.Name;
                    }
                }

                if (IsDirectPort(key))
                {
                    foreach (var port in ports)
                    {
                        if (port != null && string.Equals(port.Name, key, StringComparison.OrdinalIgnoreCase)) return port.Name;
                    }
                }
            }

            if (!IsDirectPort(key)) return "";
            return key;
        }

        /// <summary>The USB serial of the device that currently owns this COM port, or empty.</summary>
        public static string DeviceSerialFor(string portName)
        {
            var name = (portName ?? "").Trim();
            if (name.Length == 0) return "";
            foreach (var port in List())
            {
                if (string.Equals(port.Name, name, StringComparison.OrdinalIgnoreCase)) return port.DeviceSerial;
            }

            return "";
        }

        /// <summary>A COM name or a device path. A USB serial is neither.</summary>
        public static bool IsDirectPort(string value)
        {
            var key = (value ?? "").Trim();
            if (key.Length == 0) return false;
            if (key.IndexOf("/dev/", StringComparison.Ordinal) == 0) return true;
            return ComNumber(key) >= 0;
        }

        internal static IReadOnlyList<SerialPortChoice> Present(IEnumerable<SerialPortChoice> ports)
        {
            var seen = new List<SerialPortChoice>();
            var names = new List<string>();
            if (ports != null)
            {
                foreach (var port in ports)
                {
                    if (port == null) continue;
                    var name = port.Name.Trim();
                    if (name.Length == 0) continue;
                    if (name.IndexOf("Bluetooth-Incoming-Port", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (name.IndexOf("debug-console", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (name.IndexOf("wlan-debug", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    var kept = new SerialPortChoice(name, port.Label, port.DeviceSerial);
                    seen.Add(kept);
                    names.Add(name);
                }
            }

            var result = new List<SerialPortChoice>();
            foreach (var port in seen)
            {
                if (port.Name.IndexOf("/dev/tty.", StringComparison.Ordinal) == 0)
                {
                    var twin = "/dev/cu." + port.Name.Substring("/dev/tty.".Length);
                    if (names.Contains(twin)) continue;
                }

                result.Add(port);
            }

            result.Sort(delegate (SerialPortChoice left, SerialPortChoice right) { return Compare(left.Name, right.Name); });
            return result;
        }

        private static bool IsVirtualCom(string text) =>
            text.IndexOf("VCOM", StringComparison.OrdinalIgnoreCase) >= 0
            || text.IndexOf("MediaTek", StringComparison.OrdinalIgnoreCase) >= 0;

        private static List<SerialPortChoice> Described()
        {
            var found = new Dictionary<string, SerialPortChoice>(StringComparer.OrdinalIgnoreCase);
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return new List<SerialPortChoice>();
            try
            {
                using (var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum"))
                {
                    if (root != null) Walk(root, "", "", found, 0);
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is PlatformNotSupportedException || e is System.Security.SecurityException)
            {
                return new List<SerialPortChoice>();
            }

            var list = new List<SerialPortChoice>();
            foreach (var pair in found) list.Add(pair.Value);
            return list;
        }

        // The COM number is not chosen by the MediaTek driver. Plug and Play
        // stores PortName on the device instance, and that instance is the USB
        // serial when the device has one. The same device in another USB socket,
        // or a COM number that is already taken, gets a different PortName.
        private static void Walk(RegistryKey key, string instanceId, string inheritedSerial, Dictionary<string, SerialPortChoice> found, int depth)
        {
            if (depth > 8) return;
            var serial = LooksLikeSerial(instanceId) ? instanceId : inheritedSerial;
            try
            {
                using (var parameters = key.OpenSubKey("Device Parameters"))
                {
                    var port = parameters?.GetValue("PortName") as string;
                    if (port != null && ComNumber(port.Trim()) >= 0)
                    {
                        var name = port.Trim();
                        var friendly = (key.GetValue("FriendlyName") as string)?.Trim();
                        var shown = string.IsNullOrEmpty(friendly) ? name : StripCom(friendly!, name);
                        var identity = serial.Length != 0 ? "device " + serial : "this USB socket";
                        Keep(found, name, name + " · " + shown + " · " + identity, serial);
                    }
                }

                foreach (var childName in key.GetSubKeyNames())
                {
                    using (var child = key.OpenSubKey(childName))
                    {
                        if (child != null) Walk(child, childName, serial, found, depth + 1);
                    }
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is PlatformNotSupportedException || e is System.Security.SecurityException)
            {
            }
        }

        private static void Keep(Dictionary<string, SerialPortChoice> found, string name, string label, string serial)
        {
            if (!found.TryGetValue(name, out var existing))
            {
                found[name] = new SerialPortChoice(name, label, serial);
                return;
            }

            var keepSerial = existing.DeviceSerial.Length != 0 ? existing.DeviceSerial : serial;
            var keepLabel = existing.Label;
            if (IsVirtualCom(label) || !IsVirtualCom(existing.Label)) keepLabel = label;
            found[name] = new SerialPortChoice(name, keepLabel, keepSerial);
        }

        private static bool LooksLikeSerial(string instanceId)
        {
            if (instanceId == null || instanceId.Length < 4) return false;
            if (instanceId.IndexOf('&') >= 0 || instanceId.IndexOf(' ') >= 0) return false;
            return ComNumber(instanceId) < 0;
        }

        private static string StripCom(string friendly, string port)
        {
            var suffix = " (" + port + ")";
            if (friendly.Length >= suffix.Length
                && string.Equals(friendly.Substring(friendly.Length - suffix.Length), suffix, StringComparison.OrdinalIgnoreCase))
            {
                return friendly.Substring(0, friendly.Length - suffix.Length).Trim();
            }

            return friendly;
        }

        private static int Compare(string left, string right)
        {
            var leftCom = ComNumber(left);
            var rightCom = ComNumber(right);
            if (leftCom >= 0 && rightCom >= 0) return leftCom.CompareTo(rightCom);
            if (leftCom >= 0) return -1;
            if (rightCom >= 0) return 1;
            return string.Compare(left, right, StringComparison.Ordinal);
        }

        private static int ComNumber(string name)
        {
            if (name.Length < 4) return -1;
            if (name.IndexOf("COM", StringComparison.OrdinalIgnoreCase) != 0) return -1;
            int number;
            return int.TryParse(name.Substring(3), out number) ? number : -1;
        }
    }

    /// <summary>
    /// The terminal on the USB virtual serial port Windows assigned to the cable.
    /// <para>
    /// The line is 115200 baud, 8 data bits, no parity, 1 stop bit, which is
    /// what "MediaTek USB VCOM (Android)" enumerates as. The bytes are the same
    /// framed messages as Wi-Fi.
    /// </para>
    /// </summary>
    public sealed class SerialEcrChannel : IEcrChannel
    {
        public const int BaudRate = 115200;

        private readonly string _portName;

        public SerialEcrChannel(string portName)
        {
            if (portName == null) throw new ArgumentNullException(nameof(portName));
            _portName = portName.Trim();
        }

        public string Endpoint => _portName.Length != 0 ? _portName : "USB cable";

        public string Host => Endpoint;

        public int Port => 0;

        public Task<string?> ProbeAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_portName.Length == 0)
            {
                return Task.FromResult<string?>("Windows has not assigned a COM port to this terminal. Plug it in. The COM number follows the device serial and can change.");
            }

            // Do not open the COM port here. Opening it and closing it drops
            // DTR, and the terminal's USB gadget treats that as a hangup. The
            // sale that follows is then written while the terminal has stopped
            // reading. Exchange opens the port and keeps it open for the write
            // and the answer.
            try
            {
                foreach (var name in SerialPort.GetPortNames())
                {
                    if (string.Equals(name, _portName, StringComparison.OrdinalIgnoreCase))
                    {
                        return Task.FromResult<string?>(null);
                    }
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is PlatformNotSupportedException)
            {
                return Task.FromResult<string?>(null);
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Task.FromResult<string?>(null);
            }

            return Task.FromResult<string?>("Windows is not listing " + _portName + ". Plug the terminal in. The COM number follows the device serial and can change.");
        }

        public Task<byte[]> ExchangeAsync(byte[] body, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (_portName.Length == 0) throw new IOException("Windows has not assigned a COM port to this terminal. Plug it in. The COM number follows the device serial and can change.");
            return Task.Run(() => Exchange(body, timeout, cancellationToken), cancellationToken);
        }

        private byte[] Exchange(byte[] body, TimeSpan timeout, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var packet = EcrFrames.Wrap(body);
            using (var port = Open(timeout))
            using (cancellationToken.Register(delegate { try { port.Close(); } catch (IOException) { } }))
            {
                DiscardStale(port);
                port.Write(packet, 0, packet.Length);
                return ReadFrame(port, timeout, NonceOf(body), cancellationToken);
            }
        }

        private SerialPort Open(TimeSpan timeout)
        {
            var port = new SerialPort(_portName, BaudRate, Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.None,
                DtrEnable = true,
                RtsEnable = true,
                ReadTimeout = (int)Math.Max(1, Math.Min(timeout.TotalMilliseconds, int.MaxValue)),
                WriteTimeout = 3000,
            };
            try
            {
                port.Open();
                return port;
            }
            catch
            {
                port.Dispose();
                throw;
            }
        }

        private static void DiscardStale(SerialPort port)
        {
            var previous = port.ReadTimeout;
            port.ReadTimeout = 50;
            var bin = new byte[256];
            try
            {
                while (port.BytesToRead > 0)
                {
                    port.Read(bin, 0, bin.Length);
                }
            }
            catch (TimeoutException)
            {
            }
            finally
            {
                port.ReadTimeout = previous;
            }
        }

        private static byte[] ReadFrame(SerialPort port, TimeSpan timeout, string expectedNonce, CancellationToken cancellationToken)
        {
            var pending = new List<byte>();
            var buffer = new byte[4096];
            var deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    throw new EcrChannelTimeout("the terminal did not answer in time");
                }

                port.ReadTimeout = (int)Math.Max(1, Math.Min(remaining.TotalMilliseconds, int.MaxValue));
                int read;
                try
                {
                    read = port.Read(buffer, 0, buffer.Length);
                }
                catch (TimeoutException)
                {
                    throw new EcrChannelTimeout("the terminal did not answer in time");
                }

                if (read <= 0) continue;
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

        private static string NonceOf(byte[] body)
        {
            if (body.Length == 0) return "";
            try
            {
                using (var document = JsonDocument.Parse(body))
                {
                    JsonElement nonce;
                    if (document.RootElement.TryGetProperty("nonce", out nonce) && nonce.ValueKind == JsonValueKind.String)
                    {
                        return nonce.GetString() ?? "";
                    }
                }
            }
            catch (JsonException)
            {
            }

            return "";
        }
    }
}
