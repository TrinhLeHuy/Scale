using System.Runtime.InteropServices;
using System.Text.Json;

namespace TTC.Weighing.WinForms.Services;

/// <summary>
/// Theo dõi thiết bị trên máy (chạy suốt phiên app, một cửa sổ ẩn):
/// <list type="bullet">
/// <item>Cắm / rút thiết bị (WM_DEVICECHANGE) → <see cref="Changed"/> để màn thiết bị tự cập nhật.</item>
/// <item>Phím đến từ thiết bị nào (Raw Input) → khi <see cref="Controls.QrScanner"/> nhận xong một mã quét,
///   thiết bị gửi nhiều phím nhất trong lúc quét được ghi nhận là MÁY QUÉT. Không cần danh sách hãng / mã thiết bị:
///   thiết bị nào thật sự quét mã thì được đánh dấu. Ghi nhớ theo máy
///   (%LOCALAPPDATA%\TTC.Weighing\scanners.json) để lần mở app sau vẫn nhận ra.</item>
/// </list>
/// Không chặn / không đổi phím: chỉ đọc thêm thông tin thiết bị nguồn.
/// </summary>
public static class DeviceWatcher
{
    /// <summary>Máy quét đã nhận ra (theo mã thiết bị USB vật lý).</summary>
    public sealed record ScannerInfo(string DeviceId, DateTime LastScanAt, string LastCode);

    /// <summary>Có thiết bị vừa cắm / rút (đã gộp các báo liên tiếp, chạy trên luồng giao diện).</summary>
    public static event Action? Changed;

    /// <summary>Vừa nhận ra / cập nhật máy quét sau một lần quét.</summary>
    public static event Action<ScannerInfo>? ScannerSeen;

    private static WatcherWindow? _window;
    private static readonly Dictionary<string, ScannerInfo> Scanners = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Queue<(int Tick, IntPtr Device)> RecentKeys = new();
    private static readonly Dictionary<IntPtr, string?> DeviceRoots = [];

    private static readonly string StoreFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TTC.Weighing", "scanners.json");

    /// <summary>Khởi động (một lần, trên luồng giao diện). Gọi lại không sao.</summary>
    public static void EnsureStarted()
    {
        if (_window is not null) return;
        LoadStore();
        try
        {
            _window = new WatcherWindow();
            Controls.QrScanner.Scanned += OnScanned;
        }
        catch
        {
            _window = null; // không tạo được cửa sổ theo dõi → màn thiết bị vẫn dò được khi bấm Làm mới
        }
    }

    /// <summary>Bản chụp máy quét đã nhận ra (khoá = mã thiết bị USB vật lý) — dùng được ở luồng nền.</summary>
    public static IReadOnlyDictionary<string, ScannerInfo> SnapshotScanners()
        => new Dictionary<string, ScannerInfo>(Scanners, StringComparer.OrdinalIgnoreCase);

    // ── Gắn lần quét với thiết bị nguồn ──

    private const int KEY_WINDOW_MS = 4000;

    private static void RecordKey(IntPtr device)
    {
        var now = Environment.TickCount;
        RecentKeys.Enqueue((now, device));
        while (RecentKeys.Count > 0 && unchecked(now - RecentKeys.Peek().Tick) > KEY_WINDOW_MS) RecentKeys.Dequeue();
        while (RecentKeys.Count > 2000) RecentKeys.Dequeue();
    }

    private static void OnScanned(Controls.QrScanner.ScanInfo scan)
    {
        var now = Environment.TickCount;
        var source = RecentKeys
            .Where(k => unchecked(now - k.Tick) <= KEY_WINDOW_MS && k.Device != IntPtr.Zero)
            .GroupBy(k => k.Device)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault()?.Key;
        RecentKeys.Clear();
        if (source is not { } device || RootOf(device) is not { } root) return;

        var info = new ScannerInfo(root, scan.At, scan.Code);
        Scanners[root] = info;
        SaveStore();
        try { ScannerSeen?.Invoke(info); } catch { /* màn hiển thị lỗi không ảnh hưởng quét */ }
    }

    /// <summary>Thiết bị USB vật lý của một nguồn phím Raw Input (cache theo handle).</summary>
    private static string? RootOf(IntPtr device)
    {
        if (DeviceRoots.TryGetValue(device, out var cached)) return cached;
        string? root = null;
        try
        {
            uint chars = 0;
            GetRawInputDeviceInfo(device, RIDI_DEVICENAME, IntPtr.Zero, ref chars);
            if (chars > 0)
            {
                var buf = Marshal.AllocHGlobal((int)chars * 2);
                try
                {
                    if (GetRawInputDeviceInfo(device, RIDI_DEVICENAME, buf, ref chars) != uint.MaxValue)
                        root = DeviceDiscovery.PhysicalRootOfInterface(Marshal.PtrToStringUni(buf));
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }
        catch { root = null; }
        DeviceRoots[device] = root;
        return root;
    }

    // ── Lưu máy quét đã nhận ra ──

    private static void LoadStore()
    {
        try
        {
            if (!File.Exists(StoreFile)) return;
            var items = JsonSerializer.Deserialize<List<ScannerInfo>>(File.ReadAllText(StoreFile));
            foreach (var s in items ?? []) Scanners[s.DeviceId] = s;
        }
        catch { /* file hỏng → nhận ra lại ở lần quét tới */ }
    }

    private static void SaveStore()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StoreFile)!);
            File.WriteAllText(StoreFile, JsonSerializer.Serialize(Scanners.Values.ToList()));
        }
        catch { /* không lưu được → chỉ nhớ trong phiên */ }
    }

    // ── Cửa sổ ẩn: nhận WM_DEVICECHANGE + WM_INPUT ──

    private const int WM_INPUT = 0x00FF;
    private const int WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVNODES_CHANGED = 0x0007;
    private const int DBT_DEVICEARRIVAL = 0x8000;
    private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
    private const uint RID_INPUT = 0x10000003;
    private const uint RIM_TYPEKEYBOARD = 1;
    private const uint RIDI_DEVICENAME = 0x20000007;
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE { public ushort UsagePage; public ushort Usage; public uint Flags; public IntPtr Target; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER { public uint Type; public uint Size; public IntPtr Device; public IntPtr WParam; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWKEYBOARD { public ushort MakeCode, Flags, Reserved, VKey; public uint Message, ExtraInformation; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(IntPtr hRawInput, uint command, IntPtr data, ref uint size, uint headerSize);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetRawInputDeviceInfo(IntPtr hDevice, uint command, IntPtr data, ref uint size);

    private sealed class WatcherWindow : NativeWindow
    {
        private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 800 };

        public WatcherWindow()
        {
            // Cửa sổ cấp cao nhất, ẩn (không phải message-only) → nhận được thông báo WM_DEVICECHANGE gửi chung.
            CreateHandle(new CreateParams { Caption = "TTC.Weighing.DeviceWatcher" });
            _debounce.Tick += (_, _) =>
            {
                _debounce.Stop();
                DeviceRoots.Clear(); // handle Raw Input có thể đổi khi cắm lại
                try { Changed?.Invoke(); } catch { /* màn hiển thị lỗi không ảnh hưởng theo dõi */ }
            };

            // Nhận phím của mọi bàn phím / máy quét cả khi cửa sổ này không có focus. Không chặn phím (không NOLEGACY).
            var rid = new[] { new RAWINPUTDEVICE { UsagePage = 0x01, Usage = 0x06, Flags = RIDEV_INPUTSINK, Target = Handle } };
            RegisterRawInputDevices(rid, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_INPUT) ReadInput(m.LParam);
            else if (m.Msg == WM_DEVICECHANGE
                     && (int)(long)m.WParam is DBT_DEVNODES_CHANGED or DBT_DEVICEARRIVAL or DBT_DEVICEREMOVECOMPLETE)
            {
                _debounce.Stop();
                _debounce.Start();
            }
            base.WndProc(ref m);
        }

        private static void ReadInput(IntPtr lParam)
        {
            try
            {
                var headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
                uint size = 0;
                GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, headerSize);
                if (size == 0 || size > 1024) return;
                var buf = Marshal.AllocHGlobal((int)size);
                try
                {
                    if (GetRawInputData(lParam, RID_INPUT, buf, ref size, headerSize) == uint.MaxValue) return;
                    var header = Marshal.PtrToStructure<RAWINPUTHEADER>(buf);
                    if (header.Type != RIM_TYPEKEYBOARD) return;
                    var kb = Marshal.PtrToStructure<RAWKEYBOARD>(buf + (int)headerSize);
                    if (kb.Message is WM_KEYDOWN or WM_SYSKEYDOWN) RecordKey(header.Device);
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            catch { /* bỏ qua một gói phím đọc lỗi */ }
        }
    }
}
