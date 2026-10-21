using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace AmwalPay.Ecr
{
    /// <summary>
    /// Opens the terminal through libusb. This is how a macOS or Linux till
    /// reaches the cable: the same accessory handshake Windows does with WinUSB,
    /// against the device that is actually plugged in.
    /// </summary>
    internal sealed class LibUsbAccessoryBus : IUsbAccessoryBus
    {
        private static readonly object Gate = new object();
        private static Api? _api;
        private static IntPtr _context;
        private static string? _loadError;
        private readonly HashSet<string> _notTerminal = new HashSet<string>();

        public LibUsbAccessoryBus()
        {
            lock (Gate)
            {
                EnsureLoaded();
                Unavailable = _loadError;
            }
        }

        public string? Unavailable { get; }

        public string NothingPluggedIn =>
            "Nothing is plugged in. Connect the terminal with a USB cable.";

        public void PrepareForTerminal()
        {
        }

        public IReadOnlyList<UsbPlug> List()
        {
            if (Unavailable != null || _api == null) return Array.Empty<UsbPlug>();

            lock (Gate)
            {
                var count = _api.GetDeviceList(_context, out var devices);
                if (count < 0)
                {
                    throw new IOException("Could not list USB devices (" + _api.Describe(count) + ").");
                }

                try
                {
                    var plugs = new List<UsbPlug>();
                    var present = new HashSet<string>();
                    for (var i = 0; i < count; i++)
                    {
                        var device = Marshal.ReadIntPtr(devices, i * IntPtr.Size);
                        var descriptor = new DeviceDescriptor();
                        if (_api.GetDeviceDescriptor(device, ref descriptor) != 0) continue;

                        var id = _api.GetBusNumber(device) + ":" + _api.GetDeviceAddress(device);
                        present.Add(id);
                        if (IsAoa(descriptor.idVendor, descriptor.idProduct))
                        {
                            plugs.Add(new UsbPlug(id, descriptor.idVendor, descriptor.idProduct));
                            continue;
                        }

                        if (!MightBeTerminal(descriptor) || _notTerminal.Contains(id)) continue;
                        if (!TryAndroidInterface(device, out var android))
                        {
                            plugs.Add(new UsbPlug(id, descriptor.idVendor, descriptor.idProduct));
                            continue;
                        }

                        if (!android)
                        {
                            _notTerminal.Add(id);
                            continue;
                        }

                        plugs.Add(new UsbPlug(id, descriptor.idVendor, descriptor.idProduct));
                    }

                    _notTerminal.RemoveWhere(id => !present.Contains(id));
                    return plugs;
                }
                finally
                {
                    _api.FreeDeviceList(devices, 1);
                }
            }
        }

        public IUsbPipe Open(UsbPlug plug)
        {
            if (Unavailable != null) throw new IOException(Unavailable);
            if (_api == null) throw new IOException(NothingPluggedIn);

            lock (Gate)
            {
                if (!TryOpen(plug.Id, out var handle, out var error))
                {
                    throw new IOException(CouldNotOpen(error));
                }

                if (!IsAoa(plug.VendorId, plug.ProductId))
                {
                    return new Pipe(handle, claimed: false, 0x81, 0x01);
                }

                _api.SetAutoDetach(handle, 1);
                if (_api.GetConfiguration(handle, out var configuration) == 0 && configuration == 0)
                {
                    _api.SetConfiguration(handle, 1);
                }

                var claimed = _api.ClaimInterface(handle, 0);
                if (claimed != 0)
                {
                    _api.Close(handle);
                    throw new IOException("Could not claim the terminal on the cable (" + _api.Describe(claimed) + ").");
                }

                FindBulkEndpoints(handle, out var input, out var output);
                return new Pipe(handle, claimed: true, input, output);
            }
        }

        /// <summary>
        /// A terminal that has not switched yet still looks like an ordinary Android
        /// device. Its accessory interface is not there yet; the ADB interface is
        /// (class 255, subclass 66). USB ethernet uses class 255 as well, with a
        /// different subclass, and must not be asked to switch.
        /// </summary>
        private static bool TryAndroidInterface(IntPtr device, out bool android)
        {
            android = false;
            if (_api == null) return false;
            var opened = _api.Open(device, out var handle);
            if (opened != 0 || handle == IntPtr.Zero) return false;

            try
            {
                if (!ReadConfig(handle, out var config)) return false;
                android = HasAdbInterface(config);
                return true;
            }
            finally
            {
                _api.Close(handle);
            }
        }

        private static bool ReadConfig(IntPtr handle, out byte[] config)
        {
            config = Array.Empty<byte>();
            var header = new byte[4];
            if (Transfer(handle, 0x80, 6, 0x0200, 0, header, header.Length, 1000) < 4) return false;
            var total = header[2] | (header[3] << 8);
            if (total < 9 || total > 4096) return false;
            config = new byte[total];
            return Transfer(handle, 0x80, 6, 0x0200, 0, config, config.Length, 1000) >= 9;
        }

        private static bool HasAdbInterface(byte[] config)
        {
            var offset = 0;
            while (offset + 1 < config.Length)
            {
                var length = config[offset];
                if (length < 2 || offset + length > config.Length) break;
                if (config[offset + 1] == 4 && length >= 6 &&
                    config[offset + 5] == 0xFF && config[offset + 6] == 0x42)
                {
                    return true;
                }

                offset += length;
            }

            return false;
        }

        private static void FindBulkEndpoints(IntPtr handle, out byte input, out byte output)
        {
            input = 0x81;
            output = 0x01;
            if (!ReadConfig(handle, out var config)) return;

            byte? foundIn = null;
            byte? foundOut = null;
            var offset = 0;
            var interfaceNumber = -1;
            while (offset + 1 < config.Length)
            {
                var length = config[offset];
                if (length < 2 || offset + length > config.Length) break;
                var type = config[offset + 1];
                if (type == 4 && length >= 5) interfaceNumber = config[offset + 2];
                if (type == 5 && length >= 5 && interfaceNumber == 0 && (config[offset + 3] & 0x03) == 2)
                {
                    var address = config[offset + 2];
                    if ((address & 0x80) != 0) foundIn = address;
                    else foundOut = address;
                }

                offset += length;
            }

            if (foundIn.HasValue) input = foundIn.Value;
            if (foundOut.HasValue) output = foundOut.Value;
        }

        private static int Transfer(IntPtr handle, byte requestType, byte request, int value, int index, byte[] buffer, int length, int timeoutMs)
        {
            if (_api == null) return -1;
            var pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                return _api.Control(handle, requestType, request, (ushort)value, (ushort)index, pin.AddrOfPinnedObject(), (ushort)length, (uint)timeoutMs);
            }
            finally
            {
                pin.Free();
            }
        }

        private static bool TryOpen(string id, out IntPtr handle, out int error)
        {
            handle = IntPtr.Zero;
            error = -4;
            if (_api == null) return false;
            var count = _api.GetDeviceList(_context, out var devices);
            if (count < 0)
            {
                error = count;
                return false;
            }

            try
            {
                for (var i = 0; i < count; i++)
                {
                    var device = Marshal.ReadIntPtr(devices, i * IntPtr.Size);
                    var plugId = _api.GetBusNumber(device) + ":" + _api.GetDeviceAddress(device);
                    if (plugId != id) continue;
                    error = _api.Open(device, out handle);
                    return error == 0 && handle != IntPtr.Zero;
                }
            }
            finally
            {
                _api.FreeDeviceList(devices, 1);
            }

            return false;
        }

        private static bool MightBeTerminal(DeviceDescriptor descriptor)
        {
            if (descriptor.idVendor == 0x05AC || descriptor.bDeviceClass == 9) return false;
            return descriptor.bDeviceClass == 0 || descriptor.bDeviceClass == 0xFF || descriptor.bDeviceClass == 0xEF;
        }

        private static bool IsAoa(int vendorId, int productId) =>
            vendorId == 0x18D1 && (productId == 0x2D00 || productId == 0x2D01 || productId == 0x2D04 || productId == 0x2D05);

        private static string CouldNotOpen(int error) => error switch
        {
            -3 => "Could not open the terminal on the cable. This machine refused access to the USB device.",
            -4 => "The terminal is no longer on the cable.",
            _ => "Could not open the terminal on the cable (" + (_api == null ? error.ToString() : _api.Describe(error)) + ").",
        };

        private static void EnsureLoaded()
        {
            if (_api != null || _loadError != null) return;

            IntPtr library = IntPtr.Zero;
            foreach (var name in LibraryNames())
            {
                if (Native.TryLoad(name, out library)) break;
            }

            if (library == IntPtr.Zero)
            {
                _loadError = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    ? "USB cable needs libusb. Install it with Homebrew (brew install libusb) and start the till again."
                    : "USB cable needs libusb. Install libusb and start the till again.";
                return;
            }

            if (Marshal.SizeOf<DeviceDescriptor>() != 18)
            {
                _loadError = "USB cable cannot read device descriptors on this runtime.";
                return;
            }

            try
            {
                var api = new Api(library);
                var started = api.Init(out _context);
                if (started != 0)
                {
                    _loadError = "USB cable could not open the USB bus (" + api.Describe(started) + ").";
                    return;
                }

                _api = api;
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException || e is TargetInvocationException)
            {
                _loadError = "USB cable needs libusb (" + e.Message + ").";
            }
        }

        private static IEnumerable<string> LibraryNames()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                yield return "/opt/homebrew/lib/libusb-1.0.dylib";
                yield return "/usr/local/lib/libusb-1.0.dylib";
                yield return "libusb-1.0.0.dylib";
                yield break;
            }

            yield return "libusb-1.0.so.0";
            yield return "libusb-1.0.so";
        }

        private const int NoDevice = -4;
        private const int Timeout = -7;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct DeviceDescriptor
        {
            public byte bLength;
            public byte bDescriptorType;
            public ushort bcdUSB;
            public byte bDeviceClass;
            public byte bDeviceSubClass;
            public byte bDeviceProtocol;
            public byte bMaxPacketSize0;
            public ushort idVendor;
            public ushort idProduct;
            public ushort bcdDevice;
            public byte iManufacturer;
            public byte iProduct;
            public byte iSerialNumber;
            public byte bNumConfigurations;
        }

        private sealed class Pipe : IUsbPipe
        {
            private IntPtr _handle;
            private readonly bool _claimed;
            private readonly byte _input;
            private readonly byte _output;
            private bool _present = true;

            public Pipe(IntPtr handle, bool claimed, byte input, byte output)
            {
                _handle = handle;
                _claimed = claimed;
                _input = input;
                _output = output;
            }

            public bool StillPresent => _present;

            public int Control(byte requestType, byte request, int value, int index, byte[]? buffer, int length, int timeoutMs)
            {
                if (_api == null || _handle == IntPtr.Zero) return -1;
                var data = buffer ?? Array.Empty<byte>();
                var pin = length > 0 ? GCHandle.Alloc(data, GCHandleType.Pinned) : default;
                try
                {
                    var pointer = length > 0 ? pin.AddrOfPinnedObject() : IntPtr.Zero;
                    var transferred = _api.Control(
                        _handle, requestType, request, (ushort)value, (ushort)index, pointer, (ushort)Math.Max(length, 0), (uint)Math.Max(timeoutMs, 1));
                    if (transferred < 0)
                    {
                        if (transferred == NoDevice) _present = false;
                        return -1;
                    }

                    return transferred;
                }
                finally
                {
                    if (length > 0) pin.Free();
                }
            }

            public int Bulk(bool input, byte[] buffer, int count, int timeoutMs)
            {
                if (_api == null || _handle == IntPtr.Zero) return -1;
                var pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                try
                {
                    var result = _api.Bulk(
                        _handle, input ? _input : _output, pin.AddrOfPinnedObject(), count, out var transferred, (uint)Math.Max(timeoutMs, 1));
                    if (result == 0 || (result == Timeout && transferred > 0)) return transferred;
                    if (result == NoDevice) _present = false;
                    return -1;
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
                if (_api != null && _handle != IntPtr.Zero)
                {
                    if (_claimed) _api.ReleaseInterface(_handle, 0);
                    _api.Close(_handle);
                }

                _handle = IntPtr.Zero;
            }
        }

        private const int DrainTimeoutMs = 50;
        private const int MaxDrainBytes = 256 * 1024;

        private static class Native
        {
            public static bool TryLoad(string path, out IntPtr handle)
            {
                handle = IntPtr.Zero;
                var type = Type.GetType("System.Runtime.InteropServices.NativeLibrary");
                var method = type?.GetMethod("TryLoad", new[] { typeof(string), typeof(IntPtr).MakeByRefType() });
                if (method == null) return false;
                var args = new object[] { path, IntPtr.Zero };
                if (method.Invoke(null, args) is bool loaded && loaded && args[1] is IntPtr opened)
                {
                    handle = opened;
                    return true;
                }

                return false;
            }

            public static IntPtr Export(IntPtr library, string name)
            {
                var type = Type.GetType("System.Runtime.InteropServices.NativeLibrary");
                var method = type?.GetMethod("GetExport", new[] { typeof(IntPtr), typeof(string) });
                if (method == null) throw new DllNotFoundException("This runtime cannot load libusb.");
                return (IntPtr)method.Invoke(null, new object[] { library, name })!;
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int InitDelegate(out IntPtr context);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr StrErrorDelegate(int code);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetDeviceListDelegate(IntPtr context, out IntPtr list);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void FreeDeviceListDelegate(IntPtr list, int unref);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetDeviceDescriptorDelegate(IntPtr device, ref DeviceDescriptor descriptor);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte BusDelegate(IntPtr device);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int OpenDelegate(IntPtr device, out IntPtr handle);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void CloseDelegate(IntPtr handle);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ClaimDelegate(IntPtr handle, int interfaceNumber);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetConfigurationDelegate(IntPtr handle, out int configuration);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int SetConfigurationDelegate(IntPtr handle, int configuration);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int SetAutoDetachDelegate(IntPtr handle, int enable);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ControlDelegate(IntPtr handle, byte requestType, byte request, ushort value, ushort index, IntPtr data, ushort length, uint timeout);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int BulkDelegate(IntPtr handle, byte endpoint, IntPtr data, int length, out int transferred, uint timeout);

        private sealed class Api
        {
            private readonly InitDelegate _init;
            private readonly StrErrorDelegate _strerror;
            private readonly GetDeviceListDelegate _getDeviceList;
            private readonly FreeDeviceListDelegate _freeDeviceList;
            private readonly GetDeviceDescriptorDelegate _getDeviceDescriptor;
            private readonly BusDelegate _busNumber;
            private readonly BusDelegate _deviceAddress;
            private readonly OpenDelegate _open;
            private readonly CloseDelegate _close;
            private readonly ClaimDelegate _claim;
            private readonly ClaimDelegate _release;
            private readonly GetConfigurationDelegate _getConfiguration;
            private readonly SetConfigurationDelegate _setConfiguration;
            private readonly SetAutoDetachDelegate _setAutoDetach;
            private readonly ControlDelegate _control;
            private readonly BulkDelegate _bulk;

            public Api(IntPtr library)
            {
                _init = Load<InitDelegate>(library, "libusb_init");
                _strerror = Load<StrErrorDelegate>(library, "libusb_strerror");
                _getDeviceList = Load<GetDeviceListDelegate>(library, "libusb_get_device_list");
                _freeDeviceList = Load<FreeDeviceListDelegate>(library, "libusb_free_device_list");
                _getDeviceDescriptor = Load<GetDeviceDescriptorDelegate>(library, "libusb_get_device_descriptor");
                _busNumber = Load<BusDelegate>(library, "libusb_get_bus_number");
                _deviceAddress = Load<BusDelegate>(library, "libusb_get_device_address");
                _open = Load<OpenDelegate>(library, "libusb_open");
                _close = Load<CloseDelegate>(library, "libusb_close");
                _claim = Load<ClaimDelegate>(library, "libusb_claim_interface");
                _release = Load<ClaimDelegate>(library, "libusb_release_interface");
                _getConfiguration = Load<GetConfigurationDelegate>(library, "libusb_get_configuration");
                _setConfiguration = Load<SetConfigurationDelegate>(library, "libusb_set_configuration");
                _setAutoDetach = Load<SetAutoDetachDelegate>(library, "libusb_set_auto_detach_kernel_driver");
                _control = Load<ControlDelegate>(library, "libusb_control_transfer");
                _bulk = Load<BulkDelegate>(library, "libusb_bulk_transfer");
            }

            public int Init(out IntPtr context) => _init(out context);

            public string Describe(int code)
            {
                var text = Marshal.PtrToStringAnsi(_strerror(code));
                return string.IsNullOrEmpty(text) ? code.ToString() : text!;
            }

            public int GetDeviceList(IntPtr context, out IntPtr list) => _getDeviceList(context, out list);

            public void FreeDeviceList(IntPtr list, int unref) => _freeDeviceList(list, unref);

            public int GetDeviceDescriptor(IntPtr device, ref DeviceDescriptor descriptor) => _getDeviceDescriptor(device, ref descriptor);

            public byte GetBusNumber(IntPtr device) => _busNumber(device);

            public byte GetDeviceAddress(IntPtr device) => _deviceAddress(device);

            public int Open(IntPtr device, out IntPtr handle) => _open(device, out handle);

            public void Close(IntPtr handle) => _close(handle);

            public int ClaimInterface(IntPtr handle, int interfaceNumber) => _claim(handle, interfaceNumber);

            public int ReleaseInterface(IntPtr handle, int interfaceNumber) => _release(handle, interfaceNumber);

            public int GetConfiguration(IntPtr handle, out int configuration) => _getConfiguration(handle, out configuration);

            public int SetConfiguration(IntPtr handle, int configuration) => _setConfiguration(handle, configuration);

            public int SetAutoDetach(IntPtr handle, int enable) => _setAutoDetach(handle, enable);

            public int Control(IntPtr handle, byte requestType, byte request, ushort value, ushort index, IntPtr data, ushort length, uint timeout) =>
                _control(handle, requestType, request, value, index, data, length, timeout);

            public int Bulk(IntPtr handle, byte endpoint, IntPtr data, int length, out int transferred, uint timeout) =>
                _bulk(handle, endpoint, data, length, out transferred, timeout);

            private static T Load<T>(IntPtr library, string name) where T : Delegate =>
                Marshal.GetDelegateForFunctionPointer<T>(Native.Export(library, name));
        }
    }
}
