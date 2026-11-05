using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using TTC.Weighing.WinForms.Helpers;
using TTC.Weighing.WinForms.Localization;

namespace TTC.Weighing.WinForms.Printing;

/// <summary>
/// In phiếu ra máy in nhiệt khổ cuộn (POS-58 / POS-80, lệnh ESC/POS) qua cổng USB, không cần driver.
/// Phiếu vẽ bằng đúng hàm vẽ của màn xem trước thành ảnh đen trắng rồi gửi lệnh ảnh raster (GS v 0),
/// nên chữ Khmer / Lào / Việt và mã QR in ra giống xem trước, không phụ thuộc font trong máy in.
/// <para>
/// Cấu hình <c>Printing:Mode</c> = <c>EscPosUsb</c> để bật. Bật mà không thấy máy in nhiệt USB nào đang cắm
/// thì các màn in như cũ qua máy in Windows.
/// </para>
/// </summary>
public static class ThermalPrint
{
    /// <summary>
    /// Bề rộng thiết kế (1/100 inch) của phiếu dạng <c>TicketCanvas</c> (đăng tài, phiếu cân) khi in cuộn:
    /// co vào 72 mm → chữ 10pt in ra cao ≈ 2,9 mm.
    /// </summary>
    public const int TICKET_DESIGN_WIDTH = 340;

    /// <summary>Bề rộng thiết kế phiếu tạp chất / CCS (nhãn trái + giá trị cùng dòng, cần rộng hơn).</summary>
    public const int LIST_DESIGN_WIDTH = 440;

    private const int DESIGN_HEIGHT = 3000;
    private const int BAND_ROWS = 128;
    private const int BLACK_THRESHOLD = 170;

    /// <summary>Đang vẽ cho máy in nhiệt → mẫu phiếu dùng bố cục một cột, không viền (xem TicketCanvas).</summary>
    [ThreadStatic] private static bool _active;
    public static bool Active => _active;

    private static string? Cfg(string key) => Program.AppConfiguration?[$"Printing:{key}"];

    public static bool Enabled => string.Equals(Cfg("Mode"), "EscPosUsb", StringComparison.OrdinalIgnoreCase);

    /// <summary>Số chấm một dòng in: 576 cho khổ 80 mm, 384 cho khổ 58 mm.</summary>
    private static int DotsPerLine => int.TryParse(Cfg("EscPos:DotsPerLine"), out var d) && d is >= 128 and <= 1024 ? d / 8 * 8 : 576;

    private static bool AutoCut => !string.Equals(Cfg("EscPos:AutoCut"), "false", StringComparison.OrdinalIgnoreCase);

    private static int FeedLines => int.TryParse(Cfg("EscPos:FeedLines"), out var n) && n is >= 0 and <= 20 ? n : 4;

    /// <summary>Máy in nhiệt USB đang cắm (lọc theo <c>Printing:EscPos:DeviceFilter</c>, ví dụ "vid_0483"); null nếu không có.</summary>
    public static string? FindDevice()
    {
        try { return UsbPrinterPort.Find(Cfg("EscPos:DeviceFilter")); }
        catch { return null; }
    }

    /// <summary>
    /// Đang bật in nhiệt và có máy in USB → in luôn (lỗi thì báo bằng popup chung), trả true.
    /// Không bật / không có máy in nhiệt → trả false để màn gọi in qua máy in Windows như cũ.
    /// </summary>
    public static bool TryPrint(IWin32Window owner, Action<Graphics, Rectangle> render, int designWidth, int copies = 1)
    {
        if (!Enabled || FindDevice() is not { } device) return false;
        PrintOrShowError(owner, device, render, designWidth, copies);
        return true;
    }

    /// <summary>In ra máy in nhiệt <paramref name="device"/>; lỗi → popup. Trả true nếu in xong.</summary>
    public static bool PrintOrShowError(IWin32Window owner, string? device, Action<Graphics, Rectangle> render, int designWidth, int copies = 1)
    {
        if (device is null)
        {
            DigiScalePopup.ShowAlert(owner, AlertKind.Error, Loc.T("print.thermal_not_found_title"),
                Loc.T("print.thermal_not_found"), Loc.T("print.thermal_not_found_action"));
            return false;
        }
        try
        {
            UsbPrinterPort.Write(device, BuildJob(render, designWidth, copies));
            return true;
        }
        catch (Exception ex)
        {
            DigiScalePopup.ShowAlert(owner, AlertKind.Error, Loc.T("print.thermal_failed_title"),
                Loc.T("print.thermal_failed", ex.Message), Loc.T("print.thermal_failed_action"));
            return false;
        }
    }

    /// <summary>Vẽ phiếu thành ảnh đen trắng rồi dựng lệnh ESC/POS (khởi tạo → ảnh → đẩy giấy → cắt) × số bản.</summary>
    public static byte[] BuildJob(Action<Graphics, Rectangle> render, int designWidth, int copies = 1)
    {
        var (bits, widthBytes, rows) = Rasterize(render, designWidth);
        using var ms = new MemoryStream();
        for (var copy = 0; copy < Math.Max(1, copies); copy++)
        {
            ms.Write([0x1B, 0x40]); // ESC @ — khởi tạo máy in
            for (var top = 0; top < rows; top += BAND_ROWS)
            {
                var band = Math.Min(BAND_ROWS, rows - top);
                // GS v 0 m xL xH yL yH d1...dk — ảnh raster, 1 bit / chấm, bit cao = chấm bên trái.
                ms.Write([0x1D, 0x76, 0x30, 0x00,
                    (byte)(widthBytes & 0xFF), (byte)(widthBytes >> 8), (byte)(band & 0xFF), (byte)(band >> 8)]);
                ms.Write(bits, top * widthBytes, band * widthBytes);
            }
            ms.Write([0x1B, 0x64, (byte)FeedLines]); // ESC d n — đẩy giấy qua dao cắt
            if (AutoCut) ms.Write([0x1D, 0x56, 0x42, 0x00]); // GS V 66 0 — cắt giấy
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Vẽ phiếu (khung thiết kế rộng <paramref name="designWidth"/> đơn vị 1/100 inch) co vừa bề rộng in,
    /// cắt bỏ phần trắng phía dưới, chuyển sang 1 bit/chấm.
    /// </summary>
    private static (byte[] Bits, int WidthBytes, int Rows) Rasterize(Action<Graphics, Rectangle> render, int designWidth)
    {
        var dots = DotsPerLine;
        var scale = dots / (float)designWidth;
        var height = (int)Math.Ceiling(DESIGN_HEIGHT * scale);

        using var bmp = new Bitmap(dots, height, PixelFormat.Format24bppRgb);
        bmp.SetResolution(100, 100); // 1 đơn vị vẽ = 1/100 inch như khi in qua Windows
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.ScaleTransform(scale, scale);
            _active = true;
            try { render(g, new Rectangle(0, 0, designWidth, DESIGN_HEIGHT)); }
            finally { _active = false; }
        }

        var data = bmp.LockBits(new Rectangle(0, 0, dots, height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            var stride = data.Stride;
            var pixels = new byte[stride * height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

            bool Dark(int x, int y)
            {
                var i = y * stride + x * 3; // BGR
                return (pixels[i + 2] * 299 + pixels[i + 1] * 587 + pixels[i] * 114) / 1000 < BLACK_THRESHOLD;
            }

            var last = height - 1;
            while (last > 0 && !Enumerable.Range(0, dots).Any(x => Dark(x, last))) last--;
            var rows = Math.Min(height, last + 1 + (int)(10 * scale));

            var widthBytes = dots / 8;
            var bits = new byte[widthBytes * rows];
            for (var y = 0; y < rows; y++)
                for (var x = 0; x < dots; x++)
                    if (Dark(x, y)) bits[y * widthBytes + x / 8] |= (byte)(0x80 >> (x % 8));
            return (bits, widthBytes, rows);
        }
        finally { bmp.UnlockBits(data); }
    }
}
