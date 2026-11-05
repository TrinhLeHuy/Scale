using System.Drawing.Printing;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace TTC.Weighing.WinForms.Services;

/// <summary>Nhóm hiển thị của thiết bị — suy ra từ lớp thiết bị Windows / lớp USB chuẩn, không theo tên hãng.</summary>
public enum DeviceCategory { Printer, Scanner, InputDevice, Camera, SerialPort, Storage, Audio, Network, Bluetooth, Other }

public enum DeviceHealth { Ok, Warning, Error }

/// <summary>Một thiết bị vật lý đang kết nối (gộp các thành phần con của cùng một thiết bị).</summary>
public sealed record DeviceEntry(
    string Key,
    string Name,
    string? Manufacturer,
    DeviceCategory Category,
    DeviceConnection Connection,
    string? Detail,
    DeviceHealth Health,
    string? HealthText,
    IReadOnlyList<string> Roles);

public enum DeviceConnection { Usb, Bluetooth, Network, Parallel, Serial, Internal }

/// <summary>Kết quả một lần dò: thiết bị + số máy in ảo đã ẩn.</summary>
public sealed record DiscoveryResult(IReadOnlyList<DeviceEntry> Devices, int HiddenVirtualPrinters, DateTime At);

/// <summary>
/// Dò thiết bị đang kết nối trên MÁY NÀY qua Windows (SetupAPI + hàng chờ in), không có danh sách thiết bị /
/// hãng viết sẵn. Mỗi thiết bị USB / Bluetooth là một mục; lớp của các thành phần con quyết định nhóm
/// (máy in, camera, cổng COM, thiết bị nhập…). Vai trò trong phần mềm (đang in phiếu, máy quét) lấy từ cấu hình
/// in hiện tại và từ việc thiết bị thật sự gửi mã quét (<see cref="DeviceWatcher"/>).
/// Chỉ đọc, không gửi gì tới thiết bị.
/// </summary>
public static class DeviceDiscovery
{
    // ── Nhãn cần dịch (đặt ở form) được truyền vào qua delegate để lớp này không phụ thuộc UI ──
    public sealed record Labels(
        Func<string> RoleTicketThermal, Func<string> RoleTicket, Func<string> RoleTicketFallback,
        Func<DateTime, string, string> RoleScanner, Func<uint, string> DriverProblem, Func<string> Disabled,
        Func<uint, string> PrinterStatus);

    private sealed class Node
    {
        public required string Id;
        public required uint DevInst;
        public string? ParentId;
        public string? Class;
        public string? FriendlyName;
        public string? Description;
        public string? BusReportedName;
        public string? Manufacturer;
        public string[] CompatibleIds = [];
        public uint Problem;
        public bool Started;
        public readonly List<Node> Children = [];
    }

    /// <param name="scanners">Máy quét đã nhận ra (bản chụp từ <see cref="DeviceWatcher.SnapshotScanners"/>) — chạy được ở luồng nền.</param>
    public static DiscoveryResult Discover(Labels labels, IReadOnlyDictionary<string, DeviceWatcher.ScannerInfo> scanners)
    {
        var nodes = EnumerateNodes();
        foreach (var n in nodes.Values)
            if (n.ParentId is { } p && nodes.TryGetValue(p, out var parent)) parent.Children.Add(n);

        // Thiết bị có giao diện máy in USB (usbprint.sys) — máy in nhiệt không cài driver vẫn là máy in.
        var usbPrintRoots = SafeUsbPrinterPaths()
            .Select(PhysicalRootOfInterface).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var thermalPath = Printing.ThermalPrint.Enabled ? SafeThermalDevice() : null;
        var thermalRoot = PhysicalRootOfInterface(thermalPath);

        var spooler = ListSpoolerPrinters(out var hiddenVirtual);
        var ticketPrinter = SafeTicketPrinter();
        var matchedSpooler = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var result = new List<DeviceEntry>();
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in nodes.Values.Where(IsPhysicalRoot).OrderBy(n => n.Id, StringComparer.OrdinalIgnoreCase))
        {
            var all = Descendants(root).Prepend(root).ToList();
            foreach (var n in all) covered.Add(n.Id);

            var classes = all.Select(n => n.Class ?? string.Empty).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var compat = all.SelectMany(n => n.CompatibleIds).ToList();
            bool HasUsbClass(string code) => compat.Any(c => c.StartsWith($"USB\\Class_{code}", StringComparison.OrdinalIgnoreCase));

            var scanner = scanners.TryGetValue(root.Id, out var known) ? known : null;
            var isPrinter = classes.Contains("Printer") || HasUsbClass("07") || usbPrintRoots.Contains(root.Id);
            // Lớp của CHÍNH thiết bị gốc là Bluetooth / mạng (bộ thu Bluetooth, card mạng USB) → giữ nhóm đó,
            // không lấy theo thiết bị con (bàn phím Bluetooth nằm dưới bộ thu).
            var rootClass = root.Class ?? string.Empty;
            var category =
                rootClass.Equals("Bluetooth", StringComparison.OrdinalIgnoreCase) ? DeviceCategory.Bluetooth
                : rootClass.Equals("Net", StringComparison.OrdinalIgnoreCase) ? DeviceCategory.Network
                : isPrinter ? DeviceCategory.Printer
                : scanner is not null ? DeviceCategory.Scanner
                : classes.Contains("Camera") || classes.Contains("Image") || HasUsbClass("0E") ? DeviceCategory.Camera
                : classes.Contains("Ports") ? DeviceCategory.SerialPort
                : classes.Contains("Keyboard") || classes.Contains("Mouse") ? DeviceCategory.InputDevice
                : classes.Contains("DiskDrive") || HasUsbClass("08") ? DeviceCategory.Storage
                : classes.Contains("MEDIA") || classes.Contains("AudioEndpoint") || HasUsbClass("01") ? DeviceCategory.Audio
                : classes.Contains("Net") ? DeviceCategory.Network
                : classes.Contains("Bluetooth") ? DeviceCategory.Bluetooth
                : DeviceCategory.Other;

            var roles = new List<string>();
            var health = DeviceHealth.Ok;
            string? healthText = null;

            // Lỗi driver / thiết bị bị tắt: của chính thiết bị → Lỗi; của thành phần con → Cảnh báo.
            if (root.Problem != 0)
            {
                health = DeviceHealth.Error;
                healthText = root.Problem == 22 ? labels.Disabled() : labels.DriverProblem(root.Problem);
            }
            else if (all.Skip(1).FirstOrDefault(n => n.Problem != 0) is { } bad)
            {
                health = DeviceHealth.Warning;
                healthText = bad.Problem == 22 ? labels.Disabled() : labels.DriverProblem(bad.Problem);
            }

            // Máy in có driver Windows: hàng chờ in mang tên trùng thành phần con → gắn trạng thái hàng chờ vào thẻ.
            // Hàng chờ in thường mang tên sản phẩm của máy in (khớp tên, hoặc tên này chứa tên kia).
            var names = all.SelectMany(n => new[] { n.FriendlyName, n.Description, n.BusReportedName })
                .OfType<string>().Where(s => s.Length >= 6).ToList();
            var queue = !isPrinter ? null : spooler.FirstOrDefault(p => !matchedSpooler.Contains(p.Name)
                && names.Any(n => n.Contains(p.Name, StringComparison.OrdinalIgnoreCase)
                                  || p.Name.Contains(n, StringComparison.OrdinalIgnoreCase)));
            if (queue is not null)
            {
                matchedSpooler.Add(queue.Name);
                ApplyQueueState(queue, labels, ref health, ref healthText);
            }

            if (thermalRoot is not null && string.Equals(root.Id, thermalRoot, StringComparison.OrdinalIgnoreCase))
                roles.Add(labels.RoleTicketThermal());
            else if (queue is not null && string.Equals(queue.Name, ticketPrinter, StringComparison.OrdinalIgnoreCase))
                roles.Add(thermalRoot is null ? labels.RoleTicket() : labels.RoleTicketFallback());

            if (scanner is not null) roles.Add(labels.RoleScanner(scanner.LastScanAt, scanner.LastCode));

            var connection = root.Id.StartsWith("BTH", StringComparison.OrdinalIgnoreCase)
                ? DeviceConnection.Bluetooth
                : root.Id.StartsWith("USB", StringComparison.OrdinalIgnoreCase) ? DeviceConnection.Usb
                : DeviceConnection.Internal;

            result.Add(new DeviceEntry(
                Key: root.Id,
                Name: BestName(root, all, queue?.Name),
                Manufacturer: all.Select(n => CleanManufacturer(n.Manufacturer, root.Id)).FirstOrDefault(m => m is not null),
                Category: category,
                Connection: connection,
                Detail: string.Join("  ·  ", new[] { VidPid(root.Id), ComPort(all), queue?.Port }
                    .Where(s => !string.IsNullOrWhiteSpace(s))),
                Health: health,
                HealthText: healthText,
                Roles: roles));
        }

        // Cổng COM gắn sẵn trong máy (RS-232 nối đầu cân) — không nằm dưới thiết bị USB nào.
        foreach (var port in nodes.Values.Where(n => string.Equals(n.Class, "Ports", StringComparison.OrdinalIgnoreCase)
                     && !covered.Contains(n.Id)))
        {
            result.Add(new DeviceEntry(port.Id, port.FriendlyName ?? port.Description ?? port.Id,
                CleanManufacturer(port.Manufacturer), DeviceCategory.SerialPort, DeviceConnection.Serial,
                ComPort([port]),
                port.Problem == 0 ? DeviceHealth.Ok : DeviceHealth.Error,
                port.Problem == 0 ? null : labels.DriverProblem(port.Problem), []));
        }

        // Máy in mạng / cổng LPT… (hàng chờ in thật, không phải thiết bị USB đã liệt kê ở trên).
        foreach (var p in spooler.Where(p => !matchedSpooler.Contains(p.Name)))
        {
            var health = DeviceHealth.Ok;
            string? text = null;
            ApplyQueueState(p, labels, ref health, ref text);
            var roles = new List<string>();
            if (string.Equals(p.Name, ticketPrinter, StringComparison.OrdinalIgnoreCase))
                roles.Add(thermalRoot is null ? labels.RoleTicket() : labels.RoleTicketFallback());
            result.Add(new DeviceEntry("PRINTER:" + p.Name, p.Name, p.Driver, DeviceCategory.Printer,
                p.Connection, p.Port, health, text, roles));
        }

        return new DiscoveryResult(result, hiddenVirtual, DateTime.Now);
    }

    private static void ApplyQueueState(SpoolerPrinter q, Labels labels, ref DeviceHealth health, ref string? text)
    {
        if (q.Status == 0 && !q.Offline) return;
        var problem = q.Offline || (q.Status & PrinterProblemMask) != 0;
        if (problem && health != DeviceHealth.Error) health = DeviceHealth.Warning;
        text = labels.PrinterStatus(q.Offline ? q.Status | STATUS_OFFLINE : q.Status);
    }

    // ── Chọn thiết bị gốc ──

    /// <summary>
    /// Thiết bị vật lý: thiết bị USB (không phải cổng con "&amp;MI_", không phải hub — lớp USB 09).
    /// Bộ điều khiển / hub gốc của máy không tính.
    /// </summary>
    private static bool IsPhysicalRoot(Node n)
    {
        // Bluetooth không tính: Windows giữ mục của thiết bị đã ghép cả khi thiết bị đang tắt → không phản ánh "đang kết nối".
        if (!n.Id.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase)) return false;
        if (n.Id.Contains("&MI_", StringComparison.OrdinalIgnoreCase)) return false;
        if (n.Id.StartsWith(@"USB\ROOT_HUB", StringComparison.OrdinalIgnoreCase)) return false;
        if (n.CompatibleIds.Any(c => c.StartsWith(@"USB\Class_09", StringComparison.OrdinalIgnoreCase))) return false;
        return true;
    }

    private static IEnumerable<Node> Descendants(Node n)
    {
        foreach (var c in n.Children)
        {
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }

    /// <summary>
    /// Tên thiết bị: tên Windows / driver đặt riêng (FriendlyName) → tên sản phẩm thiết bị tự báo qua USB
    /// → tên hàng chờ in → mô tả chung của driver.
    /// </summary>
    private static string BestName(Node root, List<Node> all, string? queueName)
        => root.FriendlyName
           ?? CleanBusName(root.BusReportedName)
           ?? queueName
           ?? all.Skip(1).Select(n => CleanBusName(n.BusReportedName)).FirstOrDefault(s => s is not null)
           ?? root.Description
           ?? root.Id;

    /// <summary>Tên thiết bị tự báo đôi khi kèm hậu tố kỹ thuật "::EA" — bỏ đi.</summary>
    private static string? CleanBusName(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var cut = s.IndexOf("::", StringComparison.Ordinal);
        var name = (cut > 0 ? s[..cut] : s).Trim();
        return name.Length == 0 ? null : name;
    }

    /// <summary>
    /// Hãng của driver dùng chung Windows ("(Standard …)", "Microsoft" cho thiết bị không phải của Microsoft)
    /// không phải hãng thiết bị → không hiển thị.
    /// </summary>
    private static string? CleanManufacturer(string? m, string? deviceId = null)
    {
        if (string.IsNullOrWhiteSpace(m) || m.TrimStart().StartsWith('(')) return null;
        var isMicrosoftDevice = deviceId?.Contains("VID_045E", StringComparison.OrdinalIgnoreCase) == true;
        return string.Equals(m.Trim(), "Microsoft", StringComparison.OrdinalIgnoreCase) && !isMicrosoftDevice ? null : m.Trim();
    }

    private static readonly Regex VidPidRx = new(@"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ComRx = new(@"\((COM\d+|LPT\d+)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string? VidPid(string id)
        => VidPidRx.Match(id) is { Success: true } m ? $"USB {m.Groups[1].Value.ToUpperInvariant()}:{m.Groups[2].Value.ToUpperInvariant()}" : null;

    private static string? ComPort(IEnumerable<Node> nodes)
        => nodes.Select(n => n.FriendlyName is { } f ? ComRx.Match(f) : Match.Empty)
            .FirstOrDefault(m => m.Success)?.Groups[1].Value.ToUpperInvariant();

    /// <summary>"\\?\usb#vid_0483&amp;pid_5743#serial#{guid}" → "USB\VID_0483&amp;PID_5743\SERIAL" (mã thiết bị Windows).</summary>
    public static string? InterfacePathToInstanceId(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var p = path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path.TrimStart('\\');
        var guidAt = p.LastIndexOf("#{", StringComparison.Ordinal);
        if (guidAt > 0) p = p[..guidAt];
        return p.Replace('#', '\\').ToUpperInvariant();
    }

    /// <summary>Thiết bị vật lý chứa giao diện thiết bị <paramref name="interfacePath"/> (đường dẫn "\\?\…#{guid}").</summary>
    public static string? PhysicalRootOfInterface(string? interfacePath)
        => InterfacePathToInstanceId(interfacePath) is { } id ? FindPhysicalRoot(id) ?? id : null;

    /// <summary>Thiết bị USB vật lý chứa thành phần <paramref name="instanceId"/> — đi ngược lên cây thiết bị.</summary>
    public static string? FindPhysicalRoot(string instanceId)
    {
        try
        {
            if (CM_Locate_DevNodeW(out var inst, instanceId, 0) != 0) return null;
            for (var depth = 0; depth < 16; depth++)
            {
                var id = DeviceId(inst);
                if (id is null) return null;
                if (id.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase)
                    && !id.Contains("&MI_", StringComparison.OrdinalIgnoreCase)
                    && !id.StartsWith(@"USB\ROOT_HUB", StringComparison.OrdinalIgnoreCase))
                    return id.ToUpperInvariant();
                if (CM_Get_Parent(out var parent, inst, 0) != 0) return null;
                inst = parent;
            }
        }
        catch { /* không tra được cây thiết bị */ }
        return null;
    }

    // ── SetupAPI: liệt kê mọi thiết bị đang có mặt ──

    private const int DIGCF_PRESENT = 0x02;
    private const int DIGCF_ALLCLASSES = 0x04;
    private const int SPDRP_DEVICEDESC = 0x00;
    private const int SPDRP_COMPATIBLEIDS = 0x02;
    private const int SPDRP_CLASS = 0x07;
    private const int SPDRP_MFG = 0x0B;
    private const int SPDRP_FRIENDLYNAME = 0x0C;
    private const uint DN_STARTED = 0x8;

    /// <summary>DEVPKEY_Device_BusReportedDeviceDesc — tên sản phẩm do thiết bị tự báo qua USB.</summary>
    private static DEVPROPKEY BusReportedDesc = new()
    { fmtid = new Guid("540b947e-8b40-45bc-a8a2-6a0b894cbda2"), pid = 4 };

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA { public int cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVPROPKEY { public Guid fmtid; public uint pid; }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string? enumerator, IntPtr hwnd, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr set, int index, ref SP_DEVINFO_DATA data);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref SP_DEVINFO_DATA data, StringBuilder id, int size, out int required);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr set, ref SP_DEVINFO_DATA data, int property,
        out int regType, byte[]? buffer, int size, out int required);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref SP_DEVINFO_DATA data, ref DEVPROPKEY key,
        out uint propType, byte[]? buffer, int size, out int required, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, int flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Parent(out uint parent, uint devInst, int flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_IDW(uint devInst, StringBuilder buffer, int length, int flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint devInst, string deviceId, int flags);

    private static string? DeviceId(uint devInst)
    {
        var sb = new StringBuilder(512);
        return CM_Get_Device_IDW(devInst, sb, sb.Capacity, 0) == 0 ? sb.ToString() : null;
    }

    private static Dictionary<string, Node> EnumerateNodes()
    {
        var nodes = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
        var set = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) return nodes;
        try
        {
            for (var i = 0; ; i++)
            {
                var data = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
                if (!SetupDiEnumDeviceInfo(set, i, ref data)) break;

                var sb = new StringBuilder(512);
                if (!SetupDiGetDeviceInstanceIdW(set, ref data, sb, sb.Capacity, out _)) continue;
                var id = sb.ToString();

                var node = new Node
                {
                    Id = id,
                    DevInst = data.DevInst,
                    Class = RegString(set, ref data, SPDRP_CLASS),
                    FriendlyName = RegString(set, ref data, SPDRP_FRIENDLYNAME),
                    Description = RegString(set, ref data, SPDRP_DEVICEDESC),
                    Manufacturer = RegString(set, ref data, SPDRP_MFG),
                    CompatibleIds = RegMultiString(set, ref data, SPDRP_COMPATIBLEIDS),
                    BusReportedName = PropString(set, ref data, ref BusReportedDesc)
                };
                if (CM_Get_DevNode_Status(out var status, out var problem, data.DevInst, 0) == 0)
                {
                    node.Problem = problem;
                    node.Started = (status & DN_STARTED) != 0;
                }
                if (CM_Get_Parent(out var parent, data.DevInst, 0) == 0) node.ParentId = DeviceId(parent);
                nodes[id] = node;
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return nodes;
    }

    private static byte[]? RegBytes(IntPtr set, ref SP_DEVINFO_DATA data, int prop)
    {
        SetupDiGetDeviceRegistryPropertyW(set, ref data, prop, out _, null, 0, out var size);
        if (size <= 0) return null;
        var buf = new byte[size];
        return SetupDiGetDeviceRegistryPropertyW(set, ref data, prop, out _, buf, size, out _) ? buf : null;
    }

    private static string? RegString(IntPtr set, ref SP_DEVINFO_DATA data, int prop)
        => RegBytes(set, ref data, prop) is { } b ? Clean(Encoding.Unicode.GetString(b).TrimEnd('\0')) : null;

    private static string[] RegMultiString(IntPtr set, ref SP_DEVINFO_DATA data, int prop)
        => RegBytes(set, ref data, prop) is { } b
            ? Encoding.Unicode.GetString(b).Split('\0', StringSplitOptions.RemoveEmptyEntries)
            : [];

    private static string? PropString(IntPtr set, ref SP_DEVINFO_DATA data, ref DEVPROPKEY key)
    {
        SetupDiGetDevicePropertyW(set, ref data, ref key, out _, null, 0, out var size, 0);
        if (size <= 0) return null;
        var buf = new byte[size];
        return SetupDiGetDevicePropertyW(set, ref data, ref key, out _, buf, size, out _, 0)
            ? Clean(Encoding.Unicode.GetString(buf).TrimEnd('\0'))
            : null;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ── Hàng chờ in Windows ──

    public sealed record SpoolerPrinter(string Name, string? Port, string? Driver, DeviceConnection Connection,
        bool Offline, uint Status, int Jobs);

    public const uint STATUS_PAUSED = 0x1, STATUS_ERROR = 0x2, STATUS_PAPER_JAM = 0x8, STATUS_PAPER_OUT = 0x10,
        STATUS_PAPER_PROBLEM = 0x40, STATUS_OFFLINE = 0x80, STATUS_NOT_AVAILABLE = 0x1000,
        STATUS_NO_TONER = 0x40000, STATUS_DOOR_OPEN = 0x400000;

    private const uint PrinterProblemMask = STATUS_PAUSED | STATUS_ERROR | STATUS_PAPER_JAM | STATUS_PAPER_OUT
        | STATUS_PAPER_PROBLEM | STATUS_OFFLINE | STATUS_NOT_AVAILABLE | STATUS_NO_TONER | STATUS_DOOR_OPEN;

    private const uint PRINTER_ATTRIBUTE_WORK_OFFLINE = 0x400;

    /// <summary>Cổng của máy in thật: USB, mạng (IP / WSD / TCP / chia sẻ \\máy\…), LPT, COM. Cổng khác = máy in ảo (xuất file).</summary>
    private static readonly Regex PhysicalPortRx = new(
        @"^(USB\d+|LPT\d+:?|COM\d+:?|IP_|WSD|TCP|\\\\|\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static List<SpoolerPrinter> ListSpoolerPrinters(out int hiddenVirtual)
    {
        hiddenVirtual = 0;
        List<string> installed;
        try { installed = PrinterSettings.InstalledPrinters.Cast<string>().ToList(); }
        catch { return []; }

        var result = new List<SpoolerPrinter>();
        foreach (var name in installed)
        {
            var (port, driver, offline, status, jobs) = ReadPrinterState(name);
            var physical = name.StartsWith(@"\\", StringComparison.Ordinal) || (port is not null && PhysicalPortRx.IsMatch(port));
            if (!physical) { hiddenVirtual++; continue; }

            var connection = port is null ? DeviceConnection.Network
                : port.StartsWith("USB", StringComparison.OrdinalIgnoreCase) ? DeviceConnection.Usb
                : port.StartsWith("LPT", StringComparison.OrdinalIgnoreCase) ? DeviceConnection.Parallel
                : port.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ? DeviceConnection.Serial
                : DeviceConnection.Network;
            result.Add(new SpoolerPrinter(name, port, driver, connection, offline, status, jobs));
        }
        return result;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PRINTER_INFO_2
    {
        public IntPtr pServerName, pPrinterName, pShareName, pPortName, pDriverName, pComment, pLocation,
            pDevMode, pSepFile, pPrintProcessor, pDatatype, pParameters, pSecurityDescriptor;
        public uint Attributes, Priority, DefaultPriority, StartTime, UntilTime, Status, cJobs, AveragePPM;
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetPrinter(IntPtr hPrinter, int level, IntPtr pPrinter, int cbBuf, out int pcbNeeded);

    private static (string? Port, string? Driver, bool Offline, uint Status, int Jobs) ReadPrinterState(string name)
    {
        if (!OpenPrinter(name, out var h, IntPtr.Zero)) return (null, null, true, STATUS_NOT_AVAILABLE, 0);
        try
        {
            GetPrinter(h, 2, IntPtr.Zero, 0, out var needed);
            if (needed <= 0) return (null, null, false, 0, 0);
            var buf = Marshal.AllocHGlobal(needed);
            try
            {
                if (!GetPrinter(h, 2, buf, needed, out _)) return (null, null, false, 0, 0);
                var info = Marshal.PtrToStructure<PRINTER_INFO_2>(buf);
                var offline = (info.Attributes & PRINTER_ATTRIBUTE_WORK_OFFLINE) != 0
                              || (info.Status & (STATUS_OFFLINE | STATUS_NOT_AVAILABLE)) != 0;
                return (Marshal.PtrToStringUni(info.pPortName), Marshal.PtrToStringUni(info.pDriverName),
                    offline, info.Status, (int)info.cJobs);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch { return (null, null, false, 0, 0); }
        finally { ClosePrinter(h); }
    }

    private static List<string> SafeUsbPrinterPaths()
    {
        try { return Printing.UsbPrinterPort.FindDevices(); } catch { return []; }
    }

    private static string? SafeThermalDevice()
    {
        try { return Printing.ThermalPrint.FindDevice(); } catch { return null; }
    }

    private static string? SafeTicketPrinter()
    {
        try { return Printing.PrintPrinter.Current; } catch { return null; }
    }
}
