using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace AmwalPay.Ecr
{
    /// <summary>
    /// Opens the terminal through the WinUSB driver that ships with Windows.
    /// The first connection attaches that driver to the terminal's interface,
    /// then to the accessory interface the terminal comes back as. No separate
    /// driver package is installed.
    /// </summary>
    internal sealed class WinUsbAccessoryBus : IUsbAccessoryBus
    {
        private static readonly Guid UsbDeviceInterface = new Guid("A5DCBF10-6530-11D2-901F-00C04FB951ED");
        private static readonly Guid WinUsbInterface = new Guid("DEE824EF-729B-4A0E-9C14-B7117D33A817");

        public WinUsbAccessoryBus()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Unavailable = "USB cable runs on Windows. This machine cannot open the terminal over a cable.";
            }
        }

        public string? Unavailable { get; }

        public string NothingPluggedIn =>
            "Nothing is plugged in. Connect the terminal with a USB cable.";

        public void PrepareForTerminal()
        {
            if (Unavailable != null) return;
            WinUsbDriverSetup.Ensure();
        }

        public IReadOnlyList<UsbPlug> List()
        {
            if (Unavailable != null) return Array.Empty<UsbPlug>();

            var plugs = new List<UsbPlug>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Collect(UsbDeviceInterface, plugs, seen);
            Collect(WinUsbInterface, plugs, seen);
            return plugs;
        }

        private static void Collect(Guid deviceInterface, List<UsbPlug> plugs, HashSet<string> seen)
        {
            var set = SetupDiGetClassDevsW(ref deviceInterface, null, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
            if (set == InvalidHandle) return;

            try
            {
                var data = new SpDeviceInterfaceData { cbSize = Marshal.SizeOf<SpDeviceInterfaceData>() };
                for (uint index = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref deviceInterface, index, ref data); index++)
                {
                    var path = DevicePath(set, ref data, out var hardwareId);
                    if (path == null || !seen.Add(path)) continue;
                    if (!TryVidPid(hardwareId, out var vendor, out var product)) continue;
                    plugs.Add(new UsbPlug(path, vendor, product));
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(set);
            }
        }

        public IUsbPipe Open(UsbPlug plug)
        {
            var handle = CreateFileW(plug.Id, GenericRead | GenericWrite, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (handle == InvalidHandle)
            {
                throw new IOException("Could not open the terminal on the cable. Approve the Windows prompt that enables the built-in USB driver, then try again.");
            }

            if (!WinUsb_Initialize(handle, out var iface))
            {
                CloseHandle(handle);
                throw new IOException("Could not open the terminal on the cable. Approve the Windows prompt that enables the built-in USB driver, then try again.");
            }

            byte input = 0x81;
            byte output = 0x01;
            var settings = new UsbInterfaceDescriptor();
            if (WinUsb_QueryInterfaceSettings(iface, 0, ref settings))
            {
                byte? foundIn = null;
                byte? foundOut = null;
                for (byte pipeIndex = 0; pipeIndex < settings.bNumEndpoints; pipeIndex++)
                {
                    var pipe = new WinUsbPipeInformation();
                    if (!WinUsb_QueryPipe(iface, 0, pipeIndex, ref pipe)) continue;
                    if (pipe.PipeType != 2) continue;
                    if ((pipe.PipeId & 0x80) != 0) foundIn = pipe.PipeId;
                    else foundOut = pipe.PipeId;
                }

                if (foundIn.HasValue) input = foundIn.Value;
                if (foundOut.HasValue) output = foundOut.Value;
            }

            return new Pipe(handle, iface, input, output);
        }

        private static string? DevicePath(IntPtr set, ref SpDeviceInterfaceData data, out string hardwareId)
        {
            hardwareId = "";
            SetupDiGetDeviceInterfaceDetailW(set, ref data, IntPtr.Zero, 0, out var required, IntPtr.Zero);
            if (required <= 0) return null;

            var detail = Marshal.AllocHGlobal(required);
            var info = Marshal.AllocHGlobal(Marshal.SizeOf<SpDevinfoData>());
            try
            {
                Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                var dev = new SpDevinfoData { cbSize = Marshal.SizeOf<SpDevinfoData>() };
                Marshal.StructureToPtr(dev, info, false);
                if (!SetupDiGetDeviceInterfaceDetailW(set, ref data, detail, required, out _, info)) return null;

                var path = Marshal.PtrToStringUni(new IntPtr(detail.ToInt64() + 4));
                var chars = new char[512];
                if (SetupDiGetDeviceRegistryPropertyW(set, info, SpdrpHardwareId, out _, chars, chars.Length * 2, out _))
                {
                    hardwareId = new string(chars).TrimEnd('\0');
                }

                return path;
            }
            finally
            {
                Marshal.FreeHGlobal(detail);
                Marshal.FreeHGlobal(info);
            }
        }

        private static bool TryVidPid(string hardwareId, out int vendor, out int product)
        {
            vendor = 0;
            product = 0;
            var match = Regex.Match(hardwareId ?? "", @"VID_([0-9A-Fa-f]{4}).*PID_([0-9A-Fa-f]{4})");
            if (!match.Success) return false;
            vendor = int.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            product = int.Parse(match.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return true;
        }

        private sealed class Pipe : IUsbPipe
        {
            private readonly IntPtr _handle;
            private readonly IntPtr _iface;
            private readonly byte _input;
            private readonly byte _output;
            private bool _present = true;

            public Pipe(IntPtr handle, IntPtr iface, byte input, byte output)
            {
                _handle = handle;
                _iface = iface;
                _input = input;
                _output = output;
            }

            public bool StillPresent => _present;

            public int Control(byte requestType, byte request, int value, int index, byte[]? buffer, int length, int timeoutMs)
            {
                var setup = new WinUsbSetupPacket
                {
                    RequestType = requestType,
                    Request = request,
                    Value = (ushort)value,
                    Index = (ushort)index,
                    Length = (ushort)length,
                };
                var data = buffer ?? Array.Empty<byte>();
                var pin = length > 0 ? GCHandle.Alloc(data, GCHandleType.Pinned) : default;
                try
                {
                    var pointer = length > 0 ? pin.AddrOfPinnedObject() : IntPtr.Zero;
                    if (!WinUsb_ControlTransfer(_iface, setup, pointer, (uint)Math.Max(length, 0), out var transferred, IntPtr.Zero))
                    {
                        Note(Marshal.GetLastWin32Error());
                        return -1;
                    }

                    return (int)transferred;
                }
                finally
                {
                    if (length > 0) pin.Free();
                }
            }

            public int Bulk(bool input, byte[] buffer, int count, int timeoutMs)
            {
                var pipe = input ? _input : _output;
                var timeout = (uint)Math.Max(timeoutMs, 1);
                WinUsb_SetPipePolicy(_iface, pipe, PipeTransferTimeout, 4, ref timeout);
                var pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                try
                {
                    var pointer = pin.AddrOfPinnedObject();
                    var ok = input
                        ? WinUsb_ReadPipe(_iface, pipe, pointer, (uint)count, out var transferred, IntPtr.Zero)
                        : WinUsb_WritePipe(_iface, pipe, pointer, (uint)count, out transferred, IntPtr.Zero);
                    if (!ok)
                    {
                        Note(Marshal.GetLastWin32Error());
                        return -1;
                    }

                    return (int)transferred;
                }
                finally
                {
                    pin.Free();
                }
            }

            public void DiscardStale()
            {
                var scratch = new byte[16 * 1024];
                var dropped = 0;
                while (dropped <= MaxDrainBytes)
                {
                    var read = Bulk(input: true, scratch, scratch.Length, DrainTimeoutMs);
                    if (read <= 0) break;
                    dropped += read;
                }

                _present = true;
            }

            public void Dispose()
            {
                if (_iface != IntPtr.Zero) WinUsb_Free(_iface);
                if (_handle != InvalidHandle) CloseHandle(_handle);
            }

            private void Note(int error)
            {
                if (error == ErrorDeviceNotConnected) _present = false;
            }
        }

        private const int DigcfPresent = 0x02;
        private const int DigcfDeviceInterface = 0x10;
        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint FileShareRead = 1;
        private const uint FileShareWrite = 2;
        private const uint OpenExisting = 3;
        private const uint SpdrpHardwareId = 1;
        private const uint PipeTransferTimeout = 3;
        private const int DrainTimeoutMs = 50;
        private const int MaxDrainBytes = 256 * 1024;
        private const int ErrorDeviceNotConnected = 1167;
        private static readonly IntPtr InvalidHandle = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        private struct SpDeviceInterfaceData
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SpDevinfoData
        {
            public int cbSize;
            public Guid ClassGuid;
            public int DevInst;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct UsbInterfaceDescriptor
        {
            public byte bLength;
            public byte bDescriptorType;
            public byte bInterfaceNumber;
            public byte bAlternateSetting;
            public byte bNumEndpoints;
            public byte bInterfaceClass;
            public byte bInterfaceSubClass;
            public byte bInterfaceProtocol;
            public byte iInterface;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct WinUsbPipeInformation
        {
            public uint PipeType;
            public byte PipeId;
            public ushort MaximumPacketSize;
            public byte Interval;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct WinUsbSetupPacket
        {
            public byte RequestType;
            public byte Request;
            public ushort Value;
            public ushort Index;
            public ushort Length;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, string? enumerator, IntPtr hwndParent, int flags);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr deviceInfoSet, ref SpDeviceInterfaceData deviceInterfaceData, IntPtr deviceInterfaceDetailData, int deviceInterfaceDetailDataSize, out int requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr deviceInfoSet, IntPtr deviceInfoData, uint property, out uint propertyRegDataType, char[] propertyBuffer, int propertyBufferSize, out int requiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_Initialize(IntPtr deviceHandle, out IntPtr interfaceHandle);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_Free(IntPtr interfaceHandle);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_QueryInterfaceSettings(IntPtr interfaceHandle, byte alternateInterfaceNumber, ref UsbInterfaceDescriptor usbAltInterfaceDescriptor);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_QueryPipe(IntPtr interfaceHandle, byte alternateInterfaceNumber, byte pipeIndex, ref WinUsbPipeInformation pipeInformation);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_SetPipePolicy(IntPtr interfaceHandle, byte pipeId, uint policyType, uint valueLength, ref uint value);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_ControlTransfer(IntPtr interfaceHandle, WinUsbSetupPacket setupPacket, IntPtr buffer, uint bufferLength, out uint lengthTransferred, IntPtr overlapped);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_ReadPipe(IntPtr interfaceHandle, byte pipeId, IntPtr buffer, uint bufferLength, out uint lengthTransferred, IntPtr overlapped);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_WritePipe(IntPtr interfaceHandle, byte pipeId, IntPtr buffer, uint bufferLength, out uint lengthTransferred, IntPtr overlapped);
    }
}
