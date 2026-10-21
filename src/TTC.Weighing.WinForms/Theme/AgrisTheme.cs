namespace TTC.Weighing.WinForms.Theme;

/// <summary>
/// Bảng màu và font dùng chung, lấy theo nhận diện Agris: xanh lá chuyển sang xanh biển.
/// Mọi form dùng các hằng ở đây, không tự đặt màu rời rạc.
/// </summary>
public static class AgrisTheme
{
    // Màu thương hiệu
    public static readonly Color BrandGreen = Color.FromArgb(0, 148, 68);
    public static readonly Color BrandGreenDark = Color.FromArgb(0, 116, 53);
    public static readonly Color BrandTeal = Color.FromArgb(22, 111, 163);
    public static readonly Color BrandTealDark = Color.FromArgb(16, 86, 128);

    // Nền và bề mặt
    public static readonly Color Canvas = Color.FromArgb(242, 246, 245);
    public static readonly Color Surface = Color.White;
    public static readonly Color SurfaceMuted = Color.FromArgb(247, 250, 249);
    public static readonly Color Border = Color.FromArgb(221, 229, 231);

    // Ô chỉ đọc — nền xám rõ ràng, không click vào được
    public static readonly Color ReadOnlyBg = Color.FromArgb(225, 230, 232);
    public static readonly Color ReadOnlyText = Color.FromArgb(80, 90, 95);

    // Chữ
    public static readonly Color TextPrimary = Color.FromArgb(31, 41, 45);
    public static readonly Color TextMuted = Color.FromArgb(104, 116, 121);
    public static readonly Color TextOnBrand = Color.White;

    // Trạng thái nghiệp vụ
    public static readonly Color Success = Color.FromArgb(0, 148, 68);
    public static readonly Color Warning = Color.FromArgb(191, 138, 12);
    public static readonly Color Danger = Color.FromArgb(190, 58, 58);
    public static readonly Color Info = Color.FromArgb(22, 111, 163);
    public static readonly Color Neutral = Color.FromArgb(130, 140, 145);

    // Nền nhấn nhẹ cho dòng bảng
    public static readonly Color SuccessSoft = Color.FromArgb(233, 246, 238);
    public static readonly Color WarningSoft = Color.FromArgb(253, 246, 227);
    public static readonly Color DangerSoft = Color.FromArgb(253, 236, 236);
    public static readonly Color InfoSoft = Color.FromArgb(233, 242, 249);

    // Nền tối cho vùng hiển thị số cân
    public static readonly Color ScaleDisplayBack = Color.FromArgb(18, 26, 30);
    public static readonly Color ScaleDisplayIdle = Color.FromArgb(88, 98, 104);

    public const string FontFamily = "Segoe UI";
    public const string MonoFamily = "Consolas";

    /// <summary>
    /// Hệ số phóng to chữ dùng chung. Mọi font đi qua các hàm Body/Heading/Caption/Mono
    /// đều được nhân với hệ số này, nên chỉ cần đổi một chỗ là cả giao diện to lên đồng đều.
    /// Tăng để người vận hành đọc rõ hơn ở khoảng cách xa; các hằng chiều cao dòng/ô nhập
    /// trong FormLayout và GridBuilder đã được nới tương ứng để không bị chồng chữ.
    /// </summary>
    public const float FontScale = 1.50F;

    // ── Font cache — tránh tạo GDI object mới mỗi lần gọi, giảm GC pressure và GDI leak ──
    private static readonly Dictionary<(string Family, float Size, FontStyle Style), Font> _fontCache = [];
    private static readonly object _fontLock = new();

    private static Font GetOrCreateFont(string family, float size, FontStyle style)
    {
        var scaledSize = size * FontScale;
        var key = (family, scaledSize, style);
        lock (_fontLock)
        {
            if (_fontCache.TryGetValue(key, out var cached))
                return cached;

            var font = new Font(family, scaledSize, style);
            _fontCache[key] = font;
            return font;
        }
    }

    /// <summary>Font hiển thị lớn (số cân, ô số liệu) — cũng theo hệ số phóng to chung.</summary>
    public static Font Display(float size, FontStyle style = FontStyle.Bold)
        => GetOrCreateFont(FontFamily, size, style);

    public static Font Body(float size = 9.5F, FontStyle style = FontStyle.Regular)
        => GetOrCreateFont(FontFamily, size, style);

    public static Font Heading(float size = 12F) => GetOrCreateFont(FontFamily, size, FontStyle.Bold);

    public static Font Caption() => GetOrCreateFont(FontFamily, 8.5F, FontStyle.Regular);

    public static Font Mono(float size = 10F, FontStyle style = FontStyle.Regular)
        => GetOrCreateFont(MonoFamily, size, style);

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
}
