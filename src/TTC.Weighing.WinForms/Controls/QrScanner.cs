using System.Runtime.InteropServices;

namespace TTC.Weighing.WinForms.Controls;

/// <summary>
/// Nhận mã từ máy quét QR / mã vạch kiểu bàn phím (USB HID "keyboard wedge") cho MỌI màn của hệ thống cân.
/// Màn đăng ký bằng <see cref="Register"/>; quét xong là màn CHỌN LUÔN (tra chuyến / lệnh đốn), không chỉ điền mã.
/// <para><b>Cách nhận một lần quét</b> (logic gom ở <see cref="ScanAccumulator"/>):</para>
/// <list type="number">
/// <item>Phân biệt máy quét với người gõ theo tốc độ: ≥ 4 ký tự liền, mỗi ký tự cách ≤ 50 ms.
///   Thời gian lấy theo lúc PHÍM ĐƯỢC GÕ (<c>GetMessageTime</c>), nên app đang bận (lọc gợi ý, vẽ lại)
///   không làm mã bị tách.</item>
/// <item>Đã nhận ra máy quét → các ký tự sau bị CHẶN, không gõ vào ô nào (ô lọc gợi ý không phải lọc
///   theo từng ký tự, không bật danh sách gợi ý giữa chừng). Vài ký tự đầu lỡ vào ô thì được trả ô về như cũ.</item>
/// <item>Máy quét gửi nhiều đợt, hoặc có Enter/Tab giữa chừng → vẫn gom thành MỘT mã
///   (mọi QR của hệ thống là một dòng: số đăng tài, biển số, mã lệnh đốn).</item>
/// <item>Quét xong khi im lặng: 150 ms sau Enter/Tab, hoặc 500 ms nếu máy quét không gửi Enter.
///   Còn phím chờ trong hàng đợi thì chưa kết luận.</item>
/// <item>Enter/Tab của máy quét luôn bị chặn → không bấm nhầm nút đang chọn, không xác nhận nhầm hộp thoại.</item>
/// </list>
/// <para>
/// Mã giao cho màn đang hiện (màn chứa con trỏ, hoặc màn duy nhất đang hiện của cửa sổ). Đang mở hộp thoại /
/// màn không đăng ký → mã được điền vào ô đang nhập như gõ tay, không tự xác nhận gì.
/// Mỗi lần quét ghi một dòng vào <c>%LOCALAPPDATA%\TTC.Weighing\qr-scan.log</c> để đối chiếu khi có sự cố.
/// </para>
/// </summary>
public static class QrScanner
{
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_CHAR = 0x0102;
    private const int VK_TAB = 0x09;
    private const int VK_RETURN = 0x0D;
    private const long KEY_REPEAT_FLAG = 0x40000000; // bit 30 lParam: phím đang giữ (auto-repeat)
    private const uint QS_KEY = 0x0001;
    private const int POLL_MS = 40;

    [DllImport("user32.dll")] private static extern int GetMessageTime();
    [DllImport("user32.dll")] private static extern uint GetQueueStatus(uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetFocus();

    private sealed record Registration(Control Screen, Func<Control?> OwnInput, Action<string> OnScanned);

    /// <summary>Nội dung ô nhập trước khi máy quét gõ vào — để trả ô về như cũ.</summary>
    private sealed record BoxSnapshot(TextBoxBase Box, string Text, int SelStart, int SelLength);

    private static readonly List<Registration> Registrations = [];
    private static readonly ScanAccumulator Acc = new();
    private static int? _lastKeyDownTime;
    private static IntPtr _scanHwnd;
    private static BoxSnapshot? _snapshot;
    private static bool _restoring;
    private static bool _filterInstalled;
    private static System.Windows.Forms.Timer? _pollTimer;

    /// <summary>Một lần quét đã nhận (để màn Thiết bị và cấu hình hiện lần quét gần nhất).</summary>
    public sealed record ScanInfo(DateTime At, string Code, string Target, int Chunks, int MaxGapMs, bool SentEnter);

    /// <summary>Lần quét gần nhất trong phiên chạy app; null nếu chưa quét lần nào.</summary>
    public static ScanInfo? LastScan { get; private set; }

    /// <summary>Báo mỗi khi nhận xong một lần quét (chạy trên luồng giao diện).</summary>
    public static event Action<ScanInfo>? Scanned;

    /// <summary>
    /// true khi máy quét đang gõ dở một mã (hoặc đang trả ô nhập về như cũ). Ô lọc gợi ý theo từng phím
    /// nên bỏ qua lọc trong lúc này.
    /// </summary>
    public static bool ScanInProgress => _restoring || Acc.ScannerLike;

    /// <summary>
    /// Đăng ký một màn nhận mã quét. <paramref name="ownInput"/> = ô tìm của màn (màn tự điền mã vào ô này),
    /// null nếu màn không có ô tìm. Tự hủy đăng ký khi màn bị đóng.
    /// </summary>
    public static void Register(Control screen, Func<Control?> ownInput, Action<string> onScanned)
    {
        if (!_filterInstalled)
        {
            System.Windows.Forms.Application.AddMessageFilter(new Filter());
            _pollTimer = new System.Windows.Forms.Timer { Interval = POLL_MS };
            _pollTimer.Tick += (_, _) => Poll();
            _filterInstalled = true;
            // Ghi nhận thiết bị nào gửi mã quét (để màn Thiết bị đánh dấu máy quét) + theo dõi cắm / rút.
            Services.DeviceWatcher.EnsureStarted();
        }
        var reg = new Registration(screen, ownInput, onScanned);
        Registrations.Add(reg);
        screen.Disposed += (_, _) => Registrations.Remove(reg);
    }

    private static Control? ControlFor(IntPtr hwnd)
        => hwnd == IntPtr.Zero ? null : Control.FromHandle(hwnd) ?? Control.FromChildHandle(hwnd);

    /// <summary>Kiểm tra định kỳ: hết phím chờ + đủ thời gian im lặng → kết luận lần quét.</summary>
    private static void Poll()
    {
        if (!Acc.Active) { _pollTimer?.Stop(); _snapshot = null; return; }

        // Còn phím trong hàng đợi (app đang bận) → chưa kết luận, đợi xử lý hết.
        if (((GetQueueStatus(QS_KEY) >> 16) & QS_KEY) != 0) return;

        if (FinishPending(Environment.TickCount)) _pollTimer?.Stop();
    }

    /// <summary>
    /// Kết luận chuỗi đang gom nếu đã im đủ lâu tính tới <paramref name="now"/> (ms theo GetTickCount).
    /// Gọi từ Poll (giờ hiện tại) và trước mỗi ký tự mới (giờ gõ ký tự đó) — app bị treo lâu
    /// rồi mới xử lý phím của lần quét sau thì lần quét trước vẫn không bị mất / dính vào lần sau.
    /// Trả true nếu đã kết luận.
    /// </summary>
    private static bool FinishPending(int now)
    {
        var chunks = Acc.Chunks;
        var maxGap = Acc.MaxObservedGapMs;
        var terminated = Acc.SawTerminator;
        var code = Acc.TryComplete(now, out var finished);
        if (!finished) return false;

        var snapshot = _snapshot;
        var hwnd = _scanHwnd;
        _snapshot = null;
        _scanHwnd = IntPtr.Zero;

        if (code is not null) // null = người gõ tay: ký tự đã vào ô như bình thường
            Complete(code, hwnd, snapshot, chunks, maxGap, terminated);
        return true;
    }

    private static void Complete(
        string code, IntPtr hwnd, BoxSnapshot? snapshot, int chunks, int maxGap, bool terminated)
    {
        var focused = ControlFor(hwnd) ?? ControlFor(GetFocus());

        var reg = Registrations.LastOrDefault(r => !r.Screen.IsDisposed && r.Screen.Visible
            && focused is not null && (r.Screen == focused || r.Screen.Contains(focused)));
        // Con trỏ ở ngoài màn (vd thanh tab, sidebar) → màn đang hiện duy nhất của cửa sổ đang dùng.
        reg ??= Registrations.Where(r => !r.Screen.IsDisposed && r.Screen.Visible
                && r.Screen.TopLevelControl is Form f && f == Form.ActiveForm)
            .Take(2).ToList() is [var only] ? only : null;

        // Trả ô lỡ nhận vài ký tự đầu về như cũ.
        RestoreSnapshot(snapshot);

        var target = reg?.Screen.GetType().Name ?? "(không có màn nhận quét)";
        QrScanLog.Write(code, target, chunks, maxGap, terminated);
        LastScan = new ScanInfo(DateTime.Now, code, target, chunks, maxGap, terminated);
        try { Scanned?.Invoke(LastScan); } catch { /* màn hiển thị lỗi không được làm hỏng thao tác quét */ }

        if (reg is null)
        {
            // Hộp thoại / màn không nhận quét: điền mã vào ô đang nhập như gõ tay, không tự xác nhận.
            if (focused is TextBoxBase box && !box.ReadOnly && box.Enabled)
                box.SelectedText = code;
            return;
        }

        reg.Screen.BeginInvoke(() =>
        {
            if (!reg.Screen.IsDisposed) reg.OnScanned(code);
        });
    }

    private static void RestoreSnapshot(BoxSnapshot? snapshot)
    {
        if (snapshot is not { Box: { IsDisposed: false } box } || box.Text == snapshot.Text) return;
        _restoring = true;
        try
        {
            box.Text = snapshot.Text;
            box.SelectionStart = Math.Min(snapshot.SelStart, box.TextLength);
            box.SelectionLength = Math.Min(snapshot.SelLength, box.TextLength - box.SelectionStart);
        }
        finally { _restoring = false; }
    }

    private sealed class Filter : IMessageFilter
    {
        public bool PreFilterMessage(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_KEYDOWN or WM_SYSKEYDOWN:
                {
                    var time = GetMessageTime();
                    _lastKeyDownTime = time;

                    var vk = (int)(long)m.WParam;
                    if (m.Msg == WM_KEYDOWN && (vk == VK_RETURN || vk == VK_TAB) && Acc.OnTerminator(time))
                    {
                        _pollTimer?.Start();
                        return true; // Enter/Tab của máy quét: chặn, không bấm nút / nhảy ô
                    }
                    return false;
                }

                case WM_CHAR:
                {
                    var ch = (char)(long)m.WParam;
                    if (ch < ' ') return false; // Enter / phím điều khiển đã xử lý ở WM_KEYDOWN

                    // Giữ phím (auto-repeat) cũng gõ rất nhanh → không phải máy quét.
                    if (((long)m.LParam & KEY_REPEAT_FLAG) != 0)
                    {
                        Acc.Reset();
                        _snapshot = null;
                        return false;
                    }

                    var time = _lastKeyDownTime ?? GetMessageTime();

                    // Lần quét trước đã im đủ lâu (tính theo giờ gõ ký tự này) → kết luận nó trước.
                    if (Acc.Active) FinishPending(time);

                    var wasScanner = Acc.ScannerLike;
                    if (Acc.OnChar(ch, time))
                    {
                        // Chuỗi mới: chụp ô đang nhập TRƯỚC khi ký tự này vào ô.
                        _scanHwnd = m.HWnd;
                        _snapshot = ControlFor(m.HWnd) is TextBoxBase box
                            ? new BoxSnapshot(box, box.Text, box.SelectionStart, box.SelectionLength)
                            : null;
                    }
                    _pollTimer?.Start();

                    // Đã là máy quét → chặn ký tự (không gõ vào ô). Ký tự vừa làm lộ máy quét cũng chặn.
                    return wasScanner || Acc.ScannerLike;
                }

                default:
                    return false;
            }
        }
    }
}

/// <summary>Nhật ký quét QR (một dòng mỗi lần quét) — để đối chiếu khi máy quét / mã bị sai.</summary>
internal static class QrScanLog
{
    private const long MAX_BYTES = 512 * 1024;

    public static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TTC.Weighing", "qr-scan.log");

    public static void Write(string code, string target, int chunks, int maxGapMs, bool terminated)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
            if (File.Exists(LogFile) && new FileInfo(LogFile).Length > MAX_BYTES)
                File.Move(LogFile, LogFile + ".1", overwrite: true);

            File.AppendAllText(LogFile,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\t{target}\tcode={code}\tlen={code.Length}" +
                $"\tchunks={chunks}\tmaxGapMs={maxGapMs}\tenter={(terminated ? "yes" : "no")}{Environment.NewLine}");
        }
        catch
        {
            // Ghi nhật ký lỗi không được làm hỏng thao tác quét.
        }
    }
}
