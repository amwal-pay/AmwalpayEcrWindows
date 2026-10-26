using System.Linq;
using Xunit;

namespace AmwalPay.Ecr.Tests
{
    public class SerialEcrChannelTests
    {
        [Fact]
        public void The_picker_orders_com_ports_and_offers_the_android_virtual_port_first()
        {
            var ports = SerialPorts.Present(new[]
            {
                new SerialPortChoice("COM7", "Intel(R) Active Management Technology - SOL (COM7)"),
                new SerialPortChoice("COM8", "MediaTek USB VCOM (Android) (COM8)"),
                new SerialPortChoice("COM5", "USB Serial Device (COM5)"),
                new SerialPortChoice("COM6", "USB Serial Device (COM6)"),
                new SerialPortChoice("", ""),
                new SerialPortChoice("/dev/tty.T6", "/dev/tty.T6"),
                new SerialPortChoice("/dev/cu.T6", "/dev/cu.T6"),
                new SerialPortChoice("Bluetooth-Incoming-Port", "Bluetooth-Incoming-Port"),
                new SerialPortChoice("wlan-debug", "wlan-debug"),
            });

            Assert.Equal(new[] { "COM5", "COM6", "COM7", "COM8", "/dev/cu.T6" }, ports.Select(p => p.Name).ToArray());
            Assert.Equal("COM8", SerialPorts.Preferred(ports));
        }

        [Fact]
        public void A_saved_device_serial_opens_the_com_port_windows_assigned_now()
        {
            var ports = SerialPorts.Present(new[]
            {
                new SerialPortChoice("COM3", "MediaTek USB VCOM (Android) (COM3)", "HF4349L000053"),
                new SerialPortChoice("COM8", "USB Serial Device (COM8)", "OTHER"),
                new SerialPortChoice("COM5", "USB Serial Device (COM5)"),
            });

            Assert.Equal("HF4349L000053", ports.Single(p => p.Name == "COM3").Selection);
            Assert.Equal("HF4349L000053", SerialPorts.Preferred(ports, "hf4349l000053"));
            Assert.Equal("COM3", SerialPorts.Resolve("HF4349L000053", ports));
            Assert.Equal("COM8", SerialPorts.Resolve("COM8", ports));
            Assert.Equal("", SerialPorts.Resolve("not-plugged-in", ports));
            Assert.Equal("COM9", SerialPorts.Resolve("COM9", ports));
        }

        [Fact]
        public void The_terminal_list_is_the_devices_plugged_in_now()
        {
            var ports = new[]
            {
                new SerialPortChoice("COM3", "MediaTek USB VCOM (Android) (COM3)", "HF4349L000053"),
                new SerialPortChoice("COM8", "USB Serial Device (COM8)", "OTHER"),
            };

            var listed = SerialPorts.ListFor("HF4349L000053", "HF4349L000053", ports);
            Assert.Equal("HF4349L000053", listed.Selected);
            Assert.Equal(2, listed.Ports.Count);

            var missing = SerialPorts.ListFor("ewrewrwer", "", ports);
            Assert.Equal("HF4349L000053", missing.Selected);
            Assert.Equal(2, missing.Ports.Count);
            Assert.DoesNotContain(missing.Ports, port => port.Name == "ewrewrwer");

            var typed = SerialPorts.ListFor("COM8", "HF4349L000053", ports);
            Assert.Equal("HF4349L000053", typed.Selected);
        }

        [Fact]
        public void A_sale_opens_the_com_port_for_the_saved_device()
        {
            var ports = new[]
            {
                new SerialPortChoice("COM3", "MediaTek USB VCOM (Android) (COM3)", "HF4349L000053"),
                new SerialPortChoice("COM8", "USB Serial Device (COM8)", "OTHER"),
            };

            var learned = SerialPorts.PortFor("COM3", ports);
            Assert.Equal("COM3", learned.Port);
            Assert.Equal("HF4349L000053", learned.Remember);

            var followed = SerialPorts.PortFor("HF4349L000053", ports);
            Assert.Equal("COM3", followed.Port);
            Assert.Equal("HF4349L000053", followed.Remember);

            var gone = SerialPorts.PortFor("ewrewrwer", ports);
            Assert.Equal("", gone.Port);
            Assert.Equal("ewrewrwer", gone.Remember);
        }

        [Fact]
        public void A_usb_plan_names_the_com_port()
        {
            var plan = EcrSessions.Plan(new EcrLink.UsbCable("COM8"), EcrTestConfigs.Lan);

            Assert.Equal("COM8", plan.ConnectionSummary);
            Assert.True(plan.UsesUsbCable);
        }
    }
}
