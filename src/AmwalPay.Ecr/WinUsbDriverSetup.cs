using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace AmwalPay.Ecr
{
    /// <summary>
    /// Attaches the WinUSB driver that ships with Windows to a terminal that has
    /// no driver yet, and to the accessory identity that terminal comes back as.
    /// Windows asks for approval once. Nothing is downloaded.
    /// </summary>
    internal static class WinUsbDriverSetup
    {
        internal static readonly Guid UsbClass = new Guid("36FC9E60-C465-11CF-8056-444553540000");
        internal static readonly Guid UsbDeviceClass = new Guid("88BAE032-5A81-49f0-BC3D-A4FF138216D6");

        static readonly object Gate = new object();
        static readonly HashSet<string> Covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static readonly string[] ProtectedServices =
        {
            "usbhub", "usbhub3", "hidusb", "usbstor", "uaspstor", "bthusb", "bthport",
            "kbdhid", "mouhid", "usbvideo", "usbaudio", "usbccgp", "usbprint", "usbser",
            "monitor", "acpi", "wudfrd",
        };

        internal static readonly string[] AccessoryHardwareIds =
        {
            "USB\\VID_18D1&PID_2D00",
            "USB\\VID_18D1&PID_2D00&MI_00",
            "USB\\VID_18D1&PID_2D01",
            "USB\\VID_18D1&PID_2D01&MI_00",
            "USB\\VID_18D1&PID_2D04",
            "USB\\VID_18D1&PID_2D04&MI_00",
            "USB\\VID_18D1&PID_2D05",
            "USB\\VID_18D1&PID_2D05&MI_00",
        };

        public static void Ensure()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
            try
            {
                EnsureCore();
            }
            catch (IOException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new IOException("Could not enable the terminal on the cable. " + e.Message, e);
            }
        }

        static void EnsureCore()
        {
            lock (Gate)
            {
                var driverless = FindDriverless();
                var pending = new List<string>();
                foreach (var id in driverless)
                {
                    if (!Covered.Contains(id)) pending.Add(id);
                }

                if (pending.Count == 0) return;
                var package = PackageIds(pending);
                Install(package);
                foreach (var id in package) Covered.Add(id);
                System.Threading.Thread.Sleep(2000);
            }
        }

        /// <summary>The hardware ids written into the driver package: the live driverless interfaces, plus every accessory id.</summary>
        internal static List<string> PackageIds(IEnumerable<string> driverless)
        {
            var package = new List<string>();
            foreach (var id in driverless)
            {
                if (!Contains(package, id)) package.Add(id);
            }

            foreach (var id in AccessoryHardwareIds)
            {
                if (!Contains(package, id)) package.Add(id);
            }

            return package;
        }

        /// <summary>
        /// Whether this devnode is a driverless vendor interface the till may bind.
        /// Hubs, keyboards, disks, and composite parents are left alone.
        /// </summary>
        internal static bool TrySelect(string? hardwareId, string? service, string? compatibleIds, Guid classGuid, uint problem, out string normalized)
        {
            normalized = "";
            if (problem != 1 && problem != 28 && problem != 31) return false;
            if (classGuid != Guid.Empty && classGuid != UsbClass && classGuid != UsbDeviceClass) return false;
            if (IsProtectedService(service)) return false;
            if (IsStandardOrComposite(compatibleIds)) return false;
            var id = Normalize(hardwareId);
            if (id == null) return false;
            normalized = id;
            return true;
        }

        static bool IsProtectedService(string? service)
        {
            if (service == null) return false;
            var trimmed = service.Trim();
            if (trimmed.Length == 0) return false;
            foreach (var name in ProtectedServices)
            {
                if (string.Equals(trimmed, name, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        static bool IsStandardOrComposite(string? compatibleIds)
        {
            if (compatibleIds == null || compatibleIds.Length == 0) return false;
            if (compatibleIds.IndexOf("USB\\COMPOSITE", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            foreach (Match match in Regex.Matches(compatibleIds.ToUpperInvariant(), @"(?<![A-Z])CLASS_([0-9A-F]{2})"))
            {
                var cls = match.Groups[1].Value;
                if (cls != "FF" && cls != "00") return true;
            }

            return false;
        }

        internal static string? Normalize(string? hardwareId)
        {
            if (hardwareId == null || hardwareId.Length == 0) return null;
            var text = hardwareId.ToUpperInvariant();
            var vendor = Regex.Match(text, @"VID_([0-9A-F]{4})");
            var product = Regex.Match(text, @"PID_([0-9A-F]{4})");
            if (!vendor.Success || !product.Success || text.IndexOf("USB\\", StringComparison.Ordinal) < 0) return null;
            var id = "USB\\VID_" + vendor.Groups[1].Value + "&PID_" + product.Groups[1].Value;
            if (id.Contains("VID_05AC") || id.Contains("VID_05E3") || id.Contains("VID_1D6B") || id.Contains("VID_0B95")) return null;
            var iface = Regex.Match(text, @"MI_([0-9A-F]{2})");
            if (iface.Success) id += "&MI_" + iface.Groups[1].Value;
            return id;
        }

        static List<string> FindDriverless()
        {
            var found = new List<string>();
            var set = SetupDiGetClassDevsW(IntPtr.Zero, "USB", IntPtr.Zero, DigcfPresent | DigcfAllClasses);
            if (set == InvalidHandle) return found;

            try
            {
                var data = new SpDevinfoData { cbSize = Marshal.SizeOf<SpDevinfoData>() };
                for (uint index = 0; SetupDiEnumDeviceInfo(set, index, ref data); index++)
                {
                    if (CM_Get_DevNode_Status(out var status, out var problem, data.DevInst, 0) != 0) continue;
                    if ((status & DnHasProblem) == 0) continue;
                    var hardwareId = ReadProperty(set, ref data, SpdrpHardwareId);
                    var service = ReadProperty(set, ref data, SpdrpService);
                    var compatible = ReadProperty(set, ref data, SpdrpCompatibleIds);
                    if (!TrySelect(hardwareId, service, compatible, data.ClassGuid, problem, out var id)) continue;
                    if (!Contains(found, id)) found.Add(id);
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(set);
            }

            return found;
        }

        static void Install(IReadOnlyList<string> hardwareIds)
        {
            var directory = Path.Combine(Path.GetTempPath(), "amwal-ecr-usb");
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            Directory.CreateDirectory(directory);

            var infPath = Path.Combine(directory, "amwalusb.inf");
            var cdfPath = Path.Combine(directory, "amwalusb.cdf");
            var catPath = Path.Combine(directory, "amwalusb.cat");
            var cerPath = Path.Combine(directory, "amwalusb.cer");
            var scriptPath = Path.Combine(directory, "install.ps1");
            var logPath = Path.Combine(directory, "install.log");

            File.WriteAllText(infPath, Inf(hardwareIds), Encoding.ASCII);
            File.WriteAllText(cdfPath, CatalogDefinition(), Encoding.Unicode);

            var cert = IntPtr.Zero;
            var store = IntPtr.Zero;
            var created = IntPtr.Zero;
            try
            {
                store = CertOpenStore((IntPtr)CertStoreProvSystem, EncodingType, IntPtr.Zero, CertSystemStoreCurrentUser, "My");
                if (store == IntPtr.Zero) throw new IOException(Win32("Could not open the certificate store"));
                cert = CertFindCertificateInStore(store, EncodingType, 0, CertFindSubjectStr, "Amwal ECR USB", IntPtr.Zero);
                if (cert == IntPtr.Zero)
                {
                    created = CreateCertificate();
                    cert = created;
                    if (!CertAddCertificateContextToStore(store, cert, CertStoreAddReplaceExisting, out _))
                    {
                        throw new IOException(Win32("Could not store the USB certificate"));
                    }
                }

                ExportCertificate(cert, cerPath);
                BuildCatalog(directory, cdfPath, catPath);
                SignCatalog(cert, catPath);
            }
            finally
            {
                if (created != IntPtr.Zero) CertFreeCertificateContext(created);
                else if (cert != IntPtr.Zero) CertFreeCertificateContext(cert);
                if (store != IntPtr.Zero) CertCloseStore(store, 0);
            }

            File.WriteAllText(scriptPath, InstallScript(cerPath, infPath, logPath), Encoding.UTF8);
            var code = RunElevated(scriptPath);
            if (code != 0 && code != 259 && code != 3010)
            {
                var detail = File.Exists(logPath) ? File.ReadAllText(logPath).Trim() : "";
                throw new IOException("Windows did not enable the terminal on the cable (" + code + "). " + detail);
            }
        }

        static string Inf(IReadOnlyList<string> hardwareIds)
        {
            var lines = new StringBuilder();
            lines.AppendLine("; Amwal ECR. Uses the WinUSB driver that ships with Windows.");
            lines.AppendLine("[Version]");
            lines.AppendLine("Signature   = \"$Windows NT$\"");
            lines.AppendLine("Class       = USBDevice");
            lines.AppendLine("ClassGuid   = {88BAE032-5A81-49f0-BC3D-A4FF138216D6}");
            lines.AppendLine("Provider    = %ProviderName%");
            lines.AppendLine("CatalogFile = amwalusb.cat");
            lines.AppendLine("DriverVer   = 10/20/2026,1.0.0.0");
            lines.AppendLine("PnpLockdown = 1");
            lines.AppendLine();
            lines.AppendLine("[Manufacturer]");
            lines.AppendLine("%ProviderName% = Amwal, NTamd64, NTarm64");
            lines.AppendLine();
            AppendModels(lines, "Amwal.NTamd64", hardwareIds);
            AppendModels(lines, "Amwal.NTarm64", hardwareIds);
            lines.AppendLine("[USB_Install.NT]");
            lines.AppendLine("Include = winusb.inf");
            lines.AppendLine("Needs   = WINUSB.NT");
            lines.AppendLine();
            lines.AppendLine("[USB_Install.NT.Services]");
            lines.AppendLine("Include = winusb.inf");
            lines.AppendLine("Needs   = WINUSB.NT.Services");
            lines.AppendLine();
            lines.AppendLine("[USB_Install.NT.HW]");
            lines.AppendLine("AddReg = Dev_AddReg");
            lines.AppendLine();
            lines.AppendLine("[Dev_AddReg]");
            lines.AppendLine("HKR,,DeviceInterfaceGUIDs,0x10000,\"{A5DCBF10-6530-11D2-901F-00C04FB951ED}\",\"{DEE824EF-729B-4A0E-9C14-B7117D33A817}\"");
            lines.AppendLine();
            lines.AppendLine("[Strings]");
            lines.AppendLine("ProviderName = \"Amwal Pay\"");
            lines.AppendLine("DeviceName = \"Amwal terminal\"");
            return lines.ToString();
        }

        static void AppendModels(StringBuilder lines, string section, IReadOnlyList<string> hardwareIds)
        {
            lines.Append('[').Append(section).AppendLine("]");
            foreach (var id in hardwareIds)
            {
                lines.Append("%DeviceName% = USB_Install, ").AppendLine(id);
            }

            lines.AppendLine();
        }

        static string CatalogDefinition()
        {
            return "[CatalogHeader]\r\n" +
                   "Name=amwalusb.cat\r\n" +
                   "ResultDir=.\\\r\n" +
                   "PublicVersion=0x0000001\r\n" +
                   "EncodingType=0x00010001\r\n" +
                   "CATATTR1=0x10010001:OSAttr:2:6.0\r\n" +
                   "\r\n" +
                   "[CatalogFiles]\r\n" +
                   "<HASH>amwalusb.inf=amwalusb.inf\r\n";
        }

        static void BuildCatalog(string directory, string cdfPath, string catPath)
        {
            var previous = Environment.CurrentDirectory;
            try
            {
                Environment.CurrentDirectory = directory;
                var catalog = CryptCATCDFOpen(cdfPath, IntPtr.Zero);
                if (catalog == IntPtr.Zero) throw new IOException(Win32("Could not build the USB driver catalog"));
                CryptCATCDFClose(catalog);
            }
            finally
            {
                Environment.CurrentDirectory = previous;
            }

            if (!File.Exists(catPath)) throw new IOException("Could not build the USB driver catalog.");
        }

        static void SignCatalog(IntPtr certificate, string catPath)
        {
            var fileName = Marshal.StringToHGlobalUni(catPath);
            var index = Marshal.AllocHGlobal(4);
            Marshal.WriteInt32(index, 0);
            var fileInfo = Alloc(new SignerFileInfo
            {
                cbSize = (uint)Marshal.SizeOf<SignerFileInfo>(),
                pwszFileName = fileName,
                hFile = IntPtr.Zero,
            });
            var subject = Alloc(new SignerSubjectInfo
            {
                cbSize = (uint)Marshal.SizeOf<SignerSubjectInfo>(),
                pdwIndex = index,
                dwSubjectChoice = SignerSubjectFile,
                pSignerFileInfo = fileInfo,
            });
            var storeInfo = Alloc(new SignerCertStoreInfo
            {
                cbSize = (uint)Marshal.SizeOf<SignerCertStoreInfo>(),
                pSigningCert = certificate,
                dwCertPolicy = SignerCertPolicyChain,
                hCertStore = IntPtr.Zero,
            });
            var signerCert = Alloc(new SignerCert
            {
                cbSize = (uint)Marshal.SizeOf<SignerCert>(),
                dwCertChoice = SignerCertStore,
                pCertStoreInfo = storeInfo,
                hwnd = IntPtr.Zero,
            });
            var signature = Alloc(new SignerSignatureInfo
            {
                cbSize = (uint)Marshal.SizeOf<SignerSignatureInfo>(),
                algidHash = CalgSha256,
                dwAttrChoice = SignerNoAttr,
                pAttrAuthcode = IntPtr.Zero,
                psAuthenticated = IntPtr.Zero,
                psUnauthenticated = IntPtr.Zero,
            });
            var context = IntPtr.Zero;
            try
            {
                var hr = SignerSignEx2(0, subject, signerCert, signature, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out context);
                if (hr < 0) throw new IOException("Could not sign the USB driver catalog (" + hr.ToString("X8", CultureInfo.InvariantCulture) + ").");
            }
            finally
            {
                if (context != IntPtr.Zero) SignerFreeSignerContext(context);
                Marshal.FreeHGlobal(signature);
                Marshal.FreeHGlobal(signerCert);
                Marshal.FreeHGlobal(storeInfo);
                Marshal.FreeHGlobal(subject);
                Marshal.FreeHGlobal(fileInfo);
                Marshal.FreeHGlobal(index);
                Marshal.FreeHGlobal(fileName);
            }
        }

        static IntPtr CreateCertificate()
        {
            var subject = EncodeName("CN=Amwal ECR USB");
            var subjectMem = AllocBytes(subject);
            var subjectBlob = new DataBlob { cbData = (uint)subject.Length, pbData = subjectMem };
            var eku = new byte[] { 0x30, 0x0A, 0x06, 0x08, 0x2B, 0x06, 0x01, 0x05, 0x05, 0x07, 0x03, 0x03 };
            var ekuMem = AllocBytes(eku);
            var oid = Marshal.StringToHGlobalAnsi("2.5.29.37");
            var extension = Alloc(new CertExtension
            {
                pszObjId = oid,
                fCritical = 0,
                Value = new DataBlob { cbData = (uint)eku.Length, pbData = ekuMem },
            });
            var extensions = new CertExtensions { cExtension = 1, rgExtension = extension };
            var keyLen = Marshal.AllocHGlobal(4);
            Marshal.WriteInt32(keyLen, 2048);
            var param = Alloc(new CryptKeyProvParam { dwParam = KpKeyLen, pbData = keyLen, cbData = 4, dwFlags = 0 });
            var container = Marshal.StringToHGlobalUni("AmwalEcrUsbKey");
            var provider = Marshal.StringToHGlobalUni("Microsoft Enhanced RSA and AES Cryptographic Provider");
            var algOid = Marshal.StringToHGlobalAnsi("1.2.840.113549.1.1.11");
            var keyInfo = new CryptKeyProvInfo
            {
                pwszContainerName = container,
                pwszProvName = provider,
                dwProvType = ProvRsaAes,
                dwFlags = CryptSilent,
                cProvParam = 1,
                rgProvParam = param,
                dwKeySpec = AtSignature,
            };
            var algorithm = new CryptAlgorithmIdentifier
            {
                pszObjId = algOid,
                Parameters = new DataBlob { cbData = 0, pbData = IntPtr.Zero },
            };
            var start = ToSystemTime(DateTime.UtcNow.AddDays(-1));
            var end = ToSystemTime(DateTime.UtcNow.AddYears(10));
            try
            {
                var cert = CertCreateSelfSignCertificate(IntPtr.Zero, ref subjectBlob, 0, ref keyInfo, ref algorithm, ref start, ref end, ref extensions);
                if (cert == IntPtr.Zero) throw new IOException(Win32("Could not create the USB certificate"));
                return cert;
            }
            finally
            {
                Marshal.FreeHGlobal(algOid);
                Marshal.FreeHGlobal(provider);
                Marshal.FreeHGlobal(container);
                Marshal.FreeHGlobal(param);
                Marshal.FreeHGlobal(keyLen);
                Marshal.FreeHGlobal(extension);
                Marshal.FreeHGlobal(oid);
                Marshal.FreeHGlobal(ekuMem);
                Marshal.FreeHGlobal(subjectMem);
            }
        }

        static byte[] EncodeName(string name)
        {
            uint size = 0;
            if (!CertStrToNameW(X509AsnEncoding, name, CertX500NameStr, IntPtr.Zero, null, ref size, IntPtr.Zero) || size == 0)
            {
                throw new IOException(Win32("Could not encode the USB certificate name"));
            }

            var bytes = new byte[size];
            if (!CertStrToNameW(X509AsnEncoding, name, CertX500NameStr, IntPtr.Zero, bytes, ref size, IntPtr.Zero))
            {
                throw new IOException(Win32("Could not encode the USB certificate name"));
            }

            return bytes;
        }

        static void ExportCertificate(IntPtr certificate, string path)
        {
            var context = (CertContext)Marshal.PtrToStructure(certificate, typeof(CertContext))!;
            var encoded = new byte[context.cbCertEncoded];
            Marshal.Copy(context.pbCertEncoded, encoded, 0, encoded.Length);
            File.WriteAllBytes(path, encoded);
        }

        static int RunElevated(string scriptPath)
        {
            var info = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + scriptPath + "\"",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal,
            };
            if (!IsAdmin()) info.Verb = "runas";

            try
            {
                using var process = Process.Start(info) ?? throw new IOException("Could not ask Windows to enable the terminal on the cable.");
                if (!process.WaitForExit(120000))
                {
                    try { process.Kill(); } catch (InvalidOperationException) { }
                    throw new IOException("Windows did not finish enabling the terminal on the cable.");
                }

                return process.ExitCode;
            }
            catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 1223)
            {
                throw new IOException("Windows asked for permission to enable the terminal on the cable. Approve that prompt, then try again.");
            }
        }

#pragma warning disable CA1416
        static bool IsAdmin() => IsUserAnAdmin();
#pragma warning restore CA1416

        static string InstallScript(string cerPath, string infPath, string logPath)
        {
            var cer = PsQuote(cerPath);
            var inf = PsQuote(infPath);
            var log = PsQuote(logPath);
            return "$ErrorActionPreference = 'Stop'\r\n" +
                   "try {\r\n" +
                   "  function Add-Store([string]$name) {\r\n" +
                   "    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($name, 'LocalMachine')\r\n" +
                   "    $store.Open('ReadWrite')\r\n" +
                   "    $store.Add((New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(" + cer + ")))\r\n" +
                   "    $store.Close()\r\n" +
                   "  }\r\n" +
                   "  Add-Store 'Root'\r\n" +
                   "  Add-Store 'TrustedPublisher'\r\n" +
                   "  $out = " + PsQuote(Path.Combine(Path.GetDirectoryName(logPath) ?? "", "pnputil-out.txt")) + "\r\n" +
                   "  $err = " + PsQuote(Path.Combine(Path.GetDirectoryName(logPath) ?? "", "pnputil-err.txt")) + "\r\n" +
                   "  $p = Start-Process -FilePath \"$env:SystemRoot\\System32\\pnputil.exe\" -ArgumentList @('/add-driver', " + inf + ", '/install') -Wait -PassThru -RedirectStandardOutput $out -RedirectStandardError $err\r\n" +
                   "  Set-Content -Path " + log + " -Value (\"exit \" + $p.ExitCode)\r\n" +
                   "  exit $p.ExitCode\r\n" +
                   "} catch {\r\n" +
                   "  Set-Content -Path " + log + " -Value $_.Exception.Message\r\n" +
                   "  exit 1\r\n" +
                   "}\r\n";
        }

        static string PsQuote(string value) => "'" + value.Replace("'", "''") + "'";

        static string? ReadProperty(IntPtr set, ref SpDevinfoData data, uint property)
        {
            var buffer = new byte[4096];
            if (!SetupDiGetDeviceRegistryPropertyW(set, ref data, property, out _, buffer, buffer.Length, out var required)) return null;
            if (required < 2) return null;
            return Encoding.Unicode.GetString(buffer, 0, required).TrimEnd('\0');
        }

        static bool Contains(List<string> ids, string id)
        {
            foreach (var existing in ids)
            {
                if (string.Equals(existing, id, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        static SystemTime ToSystemTime(DateTime utc)
        {
            return new SystemTime
            {
                Year = (ushort)utc.Year,
                Month = (ushort)utc.Month,
                Day = (ushort)utc.Day,
                Hour = (ushort)utc.Hour,
                Minute = (ushort)utc.Minute,
                Second = (ushort)utc.Second,
                Milliseconds = (ushort)utc.Millisecond,
            };
        }

        static string Win32(string action) => action + " (" + Marshal.GetLastWin32Error() + ").";

        static IntPtr Alloc<T>(T value) where T : struct
        {
            var memory = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
            Marshal.StructureToPtr(value, memory, false);
            return memory;
        }

        static IntPtr AllocBytes(byte[] value)
        {
            var memory = Marshal.AllocHGlobal(value.Length);
            Marshal.Copy(value, 0, memory, value.Length);
            return memory;
        }

        const int DigcfPresent = 0x02;
        const int DigcfAllClasses = 0x04;
        const uint DnHasProblem = 0x400;
        const uint SpdrpHardwareId = 1;
        const uint SpdrpCompatibleIds = 2;
        const uint SpdrpService = 4;
        const uint CertStoreProvSystem = 10;
        const uint CertSystemStoreCurrentUser = 0x00010000;
        const uint X509AsnEncoding = 1;
        const uint Pkcs7AsnEncoding = 0x10000;
        const uint EncodingType = X509AsnEncoding | Pkcs7AsnEncoding;
        const uint CertX500NameStr = 3;
        const uint CertFindSubjectStr = 0x00080007;
        const uint CertStoreAddReplaceExisting = 3;
        const uint SignerSubjectFile = 1;
        const uint SignerCertStore = 2;
        const uint SignerCertPolicyChain = 2;
        const uint SignerNoAttr = 0;
        const uint CalgSha256 = 0x800c;
        const uint ProvRsaAes = 24;
        const uint AtSignature = 2;
        const uint CryptSilent = 0x40;
        const uint KpKeyLen = 9;
        static readonly IntPtr InvalidHandle = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        struct SpDevinfoData
        {
            public int cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct DataBlob
        {
            public uint cbData;
            public IntPtr pbData;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct CertExtension
        {
            public IntPtr pszObjId;
            public int fCritical;
            public DataBlob Value;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct CertExtensions
        {
            public uint cExtension;
            public IntPtr rgExtension;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct CryptKeyProvParam
        {
            public uint dwParam;
            public IntPtr pbData;
            public uint cbData;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct CryptKeyProvInfo
        {
            public IntPtr pwszContainerName;
            public IntPtr pwszProvName;
            public uint dwProvType;
            public uint dwFlags;
            public uint cProvParam;
            public IntPtr rgProvParam;
            public uint dwKeySpec;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct CryptAlgorithmIdentifier
        {
            public IntPtr pszObjId;
            public DataBlob Parameters;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SystemTime
        {
            public ushort Year;
            public ushort Month;
            public ushort DayOfWeek;
            public ushort Day;
            public ushort Hour;
            public ushort Minute;
            public ushort Second;
            public ushort Milliseconds;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct CertContext
        {
            public uint dwCertEncodingType;
            public IntPtr pbCertEncoded;
            public uint cbCertEncoded;
            public IntPtr pCertInfo;
            public IntPtr hCertStore;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SignerFileInfo
        {
            public uint cbSize;
            public IntPtr pwszFileName;
            public IntPtr hFile;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SignerSubjectInfo
        {
            public uint cbSize;
            public IntPtr pdwIndex;
            public uint dwSubjectChoice;
            public IntPtr pSignerFileInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SignerCertStoreInfo
        {
            public uint cbSize;
            public IntPtr pSigningCert;
            public uint dwCertPolicy;
            public IntPtr hCertStore;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SignerCert
        {
            public uint cbSize;
            public uint dwCertChoice;
            public IntPtr pCertStoreInfo;
            public IntPtr hwnd;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SignerSignatureInfo
        {
            public uint cbSize;
            public uint algidHash;
            public uint dwAttrChoice;
            public IntPtr pAttrAuthcode;
            public IntPtr psAuthenticated;
            public IntPtr psUnauthenticated;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string enumerator, IntPtr hwndParent, int flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SpDevinfoData deviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfoData, uint property, out uint propertyRegDataType, byte[] propertyBuffer, int propertyBufferSize, out int requiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("cfgmgr32.dll")]
        static extern int CM_Get_DevNode_Status(out uint status, out uint problemNumber, uint devInst, uint flags);

        [DllImport("crypt32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        static extern IntPtr CertOpenStore(IntPtr storeProvider, uint encoding, IntPtr cryptProv, uint flags, string storeName);

        [DllImport("crypt32.dll", SetLastError = true)]
        static extern bool CertCloseStore(IntPtr store, uint flags);

        [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CertFindCertificateInStore(IntPtr store, uint encoding, uint findFlags, uint findType, string findPara, IntPtr previous);

        [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CertStrToNameW(uint encoding, string x500, uint strType, IntPtr reserved, byte[]? encoded, ref uint encodedLength, IntPtr error);

        [DllImport("crypt32.dll", SetLastError = true)]
        static extern IntPtr CertCreateSelfSignCertificate(IntPtr cryptProv, ref DataBlob subject, uint flags, ref CryptKeyProvInfo keyProvInfo, ref CryptAlgorithmIdentifier signatureAlgorithm, ref SystemTime start, ref SystemTime end, ref CertExtensions extensions);

        [DllImport("crypt32.dll", SetLastError = true)]
        static extern bool CertAddCertificateContextToStore(IntPtr store, IntPtr certificate, uint disposition, out IntPtr storeContext);

        [DllImport("crypt32.dll", SetLastError = true)]
        static extern bool CertFreeCertificateContext(IntPtr certificate);

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CryptCATCDFOpen(string filePath, IntPtr parseError);

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
        static extern bool CryptCATCDFClose(IntPtr catalog);

        [DllImport("mssign32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern int SignerSignEx2(uint flags, IntPtr subject, IntPtr signerCert, IntPtr signature, IntPtr provider, IntPtr timestampUrl, IntPtr request, IntPtr sipData, out IntPtr signerContext);

        [DllImport("mssign32.dll", CharSet = CharSet.Unicode)]
        static extern int SignerFreeSignerContext(IntPtr signerContext);

        [DllImport("shell32.dll")]
        static extern bool IsUserAnAdmin();
    }
}
