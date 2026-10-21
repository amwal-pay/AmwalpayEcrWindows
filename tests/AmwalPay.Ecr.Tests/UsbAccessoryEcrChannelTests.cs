using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace AmwalPay.Ecr.Tests
{
    public class UsbAccessoryEcrChannelTests
    {
        [Fact]
        public void ADriverlessTerminalInterfaceIsSelected()
        {
            var selected = WinUsbDriverSetup.TrySelect(
                "USB\\VID_0E8D&PID_201C&REV_0100&MI_00",
                service: "",
                compatibleIds: "USB\\Class_FF&SubClass_42&Prot_01",
                WinUsbDriverSetup.UsbClass,
                problem: 28,
                out var id);

            Assert.True(selected);
            Assert.Equal("USB\\VID_0E8D&PID_201C&MI_00", id);
        }

        [Fact]
        public void HubsDisksAndCompositeParentsAreLeftAlone()
        {
            Assert.False(WinUsbDriverSetup.TrySelect("USB\\VID_05E3&PID_0749", "", "USB\\Class_09", WinUsbDriverSetup.UsbClass, 28, out _));
            Assert.False(WinUsbDriverSetup.TrySelect("USB\\VID_1234&PID_5678", "usbstor", "USB\\Class_08", WinUsbDriverSetup.UsbClass, 28, out _));
            Assert.False(WinUsbDriverSetup.TrySelect("USB\\VID_0E8D&PID_201C", "usbccgp", "USB\\COMPOSITE", WinUsbDriverSetup.UsbClass, 28, out _));
            Assert.False(WinUsbDriverSetup.TrySelect("USB\\VID_0B95&PID_1790", "", "USB\\Class_FF", WinUsbDriverSetup.UsbClass, 28, out _));
            Assert.False(WinUsbDriverSetup.TrySelect("USB\\VID_0E8D&PID_201C", "WinUSB", "", WinUsbDriverSetup.UsbClass, 0, out _));
        }

        [Fact]
        public void TheDriverPackageIncludesTheAccessoryIds()
        {
            var package = WinUsbDriverSetup.PackageIds(new[] { "USB\\VID_0E8D&PID_201C&MI_00" });

            Assert.Contains("USB\\VID_0E8D&PID_201C&MI_00", package);
            Assert.Contains("USB\\VID_18D1&PID_2D01&MI_00", package);
            Assert.Contains("USB\\VID_18D1&PID_2D00", package);
            Assert.DoesNotContain(package, id => id.Contains("MI_01"));
        }

        [Fact]
        public async Task AnAccessoryAlreadyOnTheBusIsReachable()
        {
            var bus = new ScriptedBus();
            bus.Plugs.Add(new UsbPlug("aoa", 0x18D1, 0x2D00));

            var reason = await new UsbAccessoryEcrChannel(bus).ProbeAsync(TimeSpan.FromSeconds(1));

            Assert.Null(reason);
        }

        [Fact]
        public async Task ASaleSwitchesTheTerminalThenReadsTheMatchingAnswer()
        {
            var bus = new ScriptedBus();
            bus.Plugs.Add(new UsbPlug("phone", 0x1234, 0x0001));
            var channel = new UsbAccessoryEcrChannel(bus);

            var body = await channel.ExchangeAsync(Encoding.UTF8.GetBytes("{\"nonce\":\"abc\"}"), TimeSpan.FromSeconds(2));

            Assert.Equal("{\"nonce\":\"abc\",\"ok\":true}", Encoding.UTF8.GetString(body));
            Assert.Contains(bus.Opened, id => id == "phone");
            Assert.Contains(bus.Opened, id => id == "aoa");
            Assert.Equal(6, bus.IdentityStrings);
        }

        [Fact]
        public async Task AStaleAnswerIsSkippedUntilTheNonceMatches()
        {
            var bus = new ScriptedBus { AccessoryFromTheStart = true };
            bus.Plugs.Add(new UsbPlug("aoa", 0x18D1, 0x2D00));
            bus.StaleBeforeMatch = true;

            var body = await new UsbAccessoryEcrChannel(bus).ExchangeAsync(
                Encoding.UTF8.GetBytes("{\"nonce\":\"mine\"}"), TimeSpan.FromSeconds(2));

            Assert.Contains("\"nonce\":\"mine\"", Encoding.UTF8.GetString(body));
        }

        private sealed class ScriptedBus : IUsbAccessoryBus
        {
            public string? Unavailable => null;

            public string NothingPluggedIn =>
                "Nothing is plugged in. Connect the terminal with a USB cable.";

            public List<UsbPlug> Plugs { get; } = new List<UsbPlug>();

            public List<string> Opened { get; } = new List<string>();

            public int IdentityStrings { get; private set; }

            public bool AccessoryFromTheStart { get; set; }

            public bool StaleBeforeMatch { get; set; }

            public void PrepareForTerminal()
            {
            }

            public IReadOnlyList<UsbPlug> List() => Plugs;

            public IUsbPipe Open(UsbPlug plug)
            {
                Opened.Add(plug.Id);
                return new Pipe(this, plug);
            }

            private sealed class Pipe : IUsbPipe
            {
                private readonly ScriptedBus _bus;
                private readonly UsbPlug _plug;
                private readonly Queue<byte[]> _incoming = new Queue<byte[]>();

                public Pipe(ScriptedBus bus, UsbPlug plug)
                {
                    _bus = bus;
                    _plug = plug;
                }

                public bool StillPresent => true;

                public int Control(byte requestType, byte request, int value, int index, byte[]? buffer, int length, int timeoutMs)
                {
                    if (request == 51 && buffer != null && buffer.Length >= 2)
                    {
                        buffer[0] = 1;
                        buffer[1] = 0;
                        return 2;
                    }

                    if (request == 52)
                    {
                        _bus.IdentityStrings++;
                        return length;
                    }

                    if (request == 53)
                    {
                        _bus.Plugs.Clear();
                        _bus.Plugs.Add(new UsbPlug("aoa", 0x18D1, 0x2D00));
                        return 0;
                    }

                    return -1;
                }

                public int Bulk(bool input, byte[] buffer, int count, int timeoutMs)
                {
                    if (!input) return count;
                    if (_plug.Id != "aoa" && !_bus.AccessoryFromTheStart) return -1;
                    if (_incoming.Count == 0)
                    {
                        if (_bus.StaleBeforeMatch)
                        {
                            _incoming.Enqueue(Frame("{\"nonce\":\"old\"}"));
                            _bus.StaleBeforeMatch = false;
                        }

                        _incoming.Enqueue(Frame("{\"nonce\":\"" + (_bus.AccessoryFromTheStart ? "mine" : "abc") + "\",\"ok\":true}"));
                    }

                    var next = _incoming.Dequeue();
                    System.Buffer.BlockCopy(next, 0, buffer, 0, next.Length);
                    return next.Length;
                }

                public void DiscardStale()
                {
                }

                public void Dispose()
                {
                }

                private static byte[] Frame(string json)
                {
                    var body = Encoding.UTF8.GetBytes(json);
                    var packet = new byte[2 + body.Length];
                    packet[0] = (byte)(body.Length >> 8);
                    packet[1] = (byte)body.Length;
                    System.Buffer.BlockCopy(body, 0, packet, 2, body.Length);
                    return packet;
                }
            }
        }
    }
}
