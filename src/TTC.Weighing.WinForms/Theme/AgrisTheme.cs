using TTC.Weighing.WinForms.Localization;

namespace TTC.Weighing.WinForms.Theme;

/// <summary>
/// Design tokens của hệ thống — "Agris Enterprise".
/// Nền trung tính tông slate, màu thương hiệu xanh lá Agris dùng cho hành động chính,
/// xanh biển cho thông tin. Mọi form dùng các token ở đây, không tự đặt màu rời rạc.
/// Tên token cũ được giữ nguyên để các màn hình hiện có không phải sửa.
/// </summary>
public static class AgrisTheme
{
    // ── Thương hiệu ──
    // Xanh lá được làm sâu hơn một chút so với logo để chữ trắng trên nền đạt tương phản AA.
    public static readonly Color BrandGreen = Color.FromArgb(0, 133, 62);
    public static readonly Color BrandGreenDark = Color.FromArgb(0, 108, 50);
    public static readonly Color BrandGreenPressed = Color.FromArgb(0, 88, 41);
    public static readonly Color BrandGreenSoft = Color.FromArgb(230, 245, 236);
    public static readonly Color BrandTeal = Color.FromArgb(21, 101, 150);
    public static readonly Color BrandTealDark = Color.FromArgb(15, 76, 117);

    /// <summary>Alias ngữ nghĩa cho hành động chính.</summary>
    public static Color Primary => BrandGreen;
    public static Color PrimaryHover => BrandGreenDark;
    public static Color PrimaryPressed => BrandGreenPressed;
    public static Color PrimarySoft => BrandGreenSoft;

    // ── Nền và bề mặt ──
    // Nền canvas tông slate-xanh đủ đậm để thẻ trắng "nổi" lên (thay vì cả màn hình một màu trắng).
    public static readonly Color Canvas = Color.FromArgb(226, 232, 240);
    public static readonly Color Surface = Color.White;
    public static readonly Color SurfaceMuted = Color.FromArgb(246, 248, 251);
    public static readonly Color SurfaceHover = Color.FromArgb(238, 243, 248);
    public static readonly Color Border = Color.FromArgb(218, 225, 234);
    public static readonly Color BorderStrong = Color.FromArgb(196, 206, 219);

    /// <summary>Nền dải tiêu đề thẻ — tách tiêu đề khỏi thân thẻ trắng.</summary>
    public static readonly Color CardHeader = Color.FromArgb(241, 245, 250);

    /// <summary>Nền header bảng dữ liệu.</summary>
    public static readonly Color GridHeader = Color.FromArgb(232, 238, 245);

    /// <summary>Mép dưới thẻ, tạo cảm giác nổi (elevation) nhẹ.</summary>
    public static readonly Color CardShadow = Color.FromArgb(184, 195, 209);

    // Ô chỉ đọc — nền xám rõ ràng để phân biệt với ô được nhập.
    public static readonly Color ReadOnlyBg = Color.FromArgb(236, 240, 244);
    public static readonly Color ReadOnlyText = Color.FromArgb(71, 85, 105);

    // Ô được nhập — nền trắng, viền đậm hơn viền thẻ.
    public static readonly Color InputBg = Color.White;

    // ── Chữ ──
    public static readonly Color TextPrimary = Color.FromArgb(15, 23, 42);
    public static readonly Color TextSecondary = Color.FromArgb(51, 65, 85);
    public static readonly Color TextMuted = Color.FromArgb(100, 116, 139);
    public static readonly Color TextOnBrand = Color.White;

    // ── Trạng thái nghiệp vụ ──
    public static readonly Color Success = Color.FromArgb(0, 133, 62);
    public static readonly Color Warning = Color.FromArgb(180, 115, 0);
    public static readonly Color Danger = Color.FromArgb(200, 40, 50);
    public static readonly Color Info = Color.FromArgb(21, 101, 150);
    public static readonly Color Neutral = Color.FromArgb(100, 116, 139);

    // Nền nhấn nhẹ cho dòng bảng / badge.
    public static readonly Color SuccessSoft = Color.FromArgb(230, 245, 236);
    public static readonly Color WarningSoft = Color.FromArgb(254, 245, 222);
    public static readonly Color DangerSoft = Color.FromArgb(253, 234, 235);
    public static readonly Color InfoSoft = Color.FromArgb(230, 241, 250);

    // ── Thanh điều hướng (sidebar) ──
    public static readonly Color NavBack = Color.FromArgb(16, 32, 42);
    public static readonly Color NavBackHover = Color.FromArgb(28, 49, 61);
    public static readonly Color NavBackActive = Color.FromArgb(22, 58, 52);
    public static readonly Color NavText = Color.FromArgb(203, 213, 225);
    public static readonly Color NavTextMuted = Color.FromArgb(120, 140, 155);
    public static readonly Color NavTextActive = Color.White;
    public static readonly Color NavAccent = Color.FromArgb(52, 199, 112);
    public static readonly Color NavDivider = Color.FromArgb(34, 52, 64);

    // ── Vùng hiển thị số cân (nền tối, tương phản cao) ──
    public static readonly Color ScaleDisplayBack = Color.FromArgb(13, 22, 29);
    public static readonly Color ScaleDisplayIdle = Color.FromArgb(84, 98, 110);
    public static readonly Color ScaleDisplayTrack = Color.FromArgb(32, 44, 53);
    public static readonly Color ScaleDisplayCaption = Color.FromArgb(148, 163, 178);
    public static readonly Color ScaleStable = Color.FromArgb(94, 230, 150);
    public static readonly Color ScaleMeasuring = Color.FromArgb(250, 204, 92);

    /// <summary>Viền focus bàn phím — bắt buộc nhìn thấy được (accessibility).</summary>
    public static readonly Color FocusRing = Color.FromArgb(21, 101, 150);

    // ── Hình khối và khoảng cách ──
    public const int RadiusSm = 4;
    public const int RadiusMd = 6;
    public const int RadiusLg = 10;
    public const int SpaceXs = 4;
    public const int SpaceSm = 8;
    public const int SpaceMd = 12;
    public const int SpaceLg = 16;
    public const int SpaceXl = 24;

    /// <summary>
    /// Họ font giao diện theo ngôn ngữ hiện hành. Segoe UI không có glyph Lào/Khmer,
    /// nên phải đổi font theo ngôn ngữ để tránh "tofu" (□).
    /// </summary>
    public static string FontFamily => LocalizedFont.Current;

    public const string MonoFamily = "Consolas";

    /// <summary>Font biểu tượng của Windows. Win11: Segoe Fluent Icons; Win10: Segoe MDL2 Assets.</summary>
    public static string? IconFamily => LocalizedFont.IconFamily;

    /// <summary>
    /// Hệ số phóng to chữ dùng chung cho nội dung màn hình nghiệp vụ.
    /// Mọi font đi qua Body/Heading/Caption/Mono/Display đều nhân hệ số này để người vận hành
    /// đọc rõ ở khoảng cách xa. Chrome của shell (sidebar, header) dùng <see cref="Ui"/> không nhân.
    /// </summary>
    public const float FontScale = 1.50F;

    // ── Font cache — tránh tạo GDI object mới mỗi lần gọi ──
    private static readonly Dictionary<(string Family, float Size, FontStyle Style), Font> _fontCache = [];
    private static readonly object _fontLock = new();

    private static Font GetOrCreateFont(string family, float size, FontStyle style)
    {
        var key = (family, size, style);
        lock (_fontLock)
        {
            if (_fontCache.TryGetValue(key, out var cached))
                return cached;

            var font = new Font(family, size, style);
            _fontCache[key] = font;
            return font;
        }
    }

    /// <summary>Font hiển thị lớn (số liệu, KPI).</summary>
    public static Font Display(float size, FontStyle style = FontStyle.Bold)
        => GetOrCreateFont(FontFamily, size * FontScale, style);

    public static Font Body(float size = 9.5F, FontStyle style = FontStyle.Regular)
        => GetOrCreateFont(FontFamily, size * FontScale, style);

    public static Font Heading(float size = 12F)
        => GetOrCreateFont(FontFamily, size * FontScale, FontStyle.Bold);

    public static Font Caption() => GetOrCreateFont(FontFamily, 8.5F * FontScale, FontStyle.Regular);

    public static Font Mono(float size = 10F, FontStyle style = FontStyle.Regular)
        => GetOrCreateFont(MonoFamily, size * FontScale, style);

    /// <summary>Font cho chrome của shell (sidebar, header, status bar) — không nhân FontScale.</summary>
    public static Font Ui(float size = 10F, FontStyle style = FontStyle.Regular)
        => GetOrCreateFont(FontFamily, size, style);

    /// <summary>Font biểu tượng, null nếu máy không có font icon của Windows.</summary>
    public static Font? Icon(float size = 12F)
        => IconFamily is { } family ? GetOrCreateFont(family, size, FontStyle.Regular) : null;

    /// <summary>Màu chữ cho một trạng thái nghiệp vụ.</summary>
    public static Color StatusForeground(string? status) => status switch
    {
        "Paused" or "Mismatch" or "Failed" or "Invalid" or "Cancelled" => Danger,
        "Unknown" or "Held" or "Pending" or "MissingData" or "ClosedIncomplete" => Warning,
        "Pass" or "Acknowledged" or "Valid" or "Closed" or "Posted" or "Reconciled" => Success,
        "Active" or "InProgress" or "Registered" or "Open" => Info,
        _ => TextMuted
    };

    /// <summary>Màu nền nhạt cho một trạng thái nghiệp vụ.</summary>
    public static Color StatusBackground(string? status) => status switch
    {
        "Paused" or "Mismatch" or "Failed" or "Invalid" or "Cancelled" => DangerSoft,
        "Unknown" or "Held" or "Pending" or "MissingData" or "ClosedIncomplete" => WarningSoft,
        "Pass" or "Acknowledged" or "Valid" or "Closed" or "Posted" or "Reconciled" => SuccessSoft,
        _ => Surface
    };

    /// <summary>Trộn hai màu, dùng cho trạng thái hover/pressed tự sinh.</summary>
    public static Color Blend(Color a, Color b, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            (int)(a.R + (b.R - a.R) * amount),
            (int)(a.G + (b.G - a.G) * amount),
            (int)(a.B + (b.B - a.B) * amount));
    }
}
