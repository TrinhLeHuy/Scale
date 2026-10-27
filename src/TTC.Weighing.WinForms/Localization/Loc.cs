using System.Drawing.Text;

namespace TTC.Weighing.WinForms.Localization;

/// <summary>
/// Điểm truy cập tĩnh tới localizer dùng chung cho toàn UI. Được khởi tạo một lần lúc
/// khởi động (Program.cs). Form gọi <c>Loc.T("key")</c> và đăng ký <see cref="Changed"/>
/// để vẽ lại khi đổi ngôn ngữ.
/// </summary>
public static class Loc
{
    private static ILocalizer? _localizer;

    /// <summary>Cài instance localizer dùng chung. Gọi một lần khi khởi động app.</summary>
    public static void Initialize(ILocalizer localizer)
    {
        _localizer = localizer;
    }

    /// <summary>Localizer hiện hành. Ném lỗi nếu chưa Initialize (lỗi lập trình).</summary>
    public static ILocalizer Current =>
        _localizer ?? throw new InvalidOperationException(
            "Loc chưa được khởi tạo. Gọi Loc.Initialize(...) khi khởi động app.");

    /// <summary>Ngôn ngữ đang dùng.</summary>
    public static AppLanguage Language => _localizer?.Current ?? AppLanguage.Vi;

    /// <summary>Phát khi ngôn ngữ đổi.</summary>
    public static event EventHandler? Changed
    {
        add { if (_localizer is not null) _localizer.LanguageChanged += value; }
        remove { if (_localizer is not null) _localizer.LanguageChanged -= value; }
    }

    /// <summary>Đổi ngôn ngữ toàn app.</summary>
    public static void SetLanguage(AppLanguage language) => _localizer?.SetLanguage(language);

    /// <summary>Danh sách ngôn ngữ mà tài khoản/BU được phép dùng. Null = tất cả.</summary>
    public static IReadOnlyList<AppLanguage>? AllowedLanguages => _localizer?.AllowedLanguages;

    /// <summary>Thiết lập danh sách ngôn ngữ được phép. Gọi sau khi đăng nhập.</summary>
    public static void SetAllowedLanguages(IEnumerable<AppLanguage>? languages)
        => _localizer?.SetAllowedLanguages(languages);

    /// <summary>Lấy chuỗi đã dịch.</summary>
    public static string T(string key) => _localizer?.T(key) ?? key;

    /// <summary>Lấy chuỗi đã dịch và format.</summary>
    public static string T(string key, params object[] args) => _localizer?.T(key, args) ?? key;

    /// <summary>
    /// Tên hiển thị của giá trị enum theo ngôn ngữ hiện hành — khóa <c>enum.{TênKiểu}.{GiáTrị}</c>.
    /// Thiếu khóa thì trả tên gốc của enum.
    /// </summary>
    public static string Enum(System.Enum value)
    {
        var key = $"enum.{value.GetType().Name}.{value}";
        return (_localizer as Localizer)?.TryT(key) ?? value.ToString();
    }

    /// <summary>Chuỗi của một khóa ở ngôn ngữ chỉ định (phiếu in song ngữ). Có format nếu truyền tham số.</summary>
    public static string TIn(AppLanguage language, string key, params object[] args)
    {
        var template = (_localizer as Localizer)?.TFor(language, key) ?? key;
        if (args.Length == 0) return template;
        try { return string.Format(System.Globalization.CultureInfo.CurrentCulture, template, args); }
        catch (FormatException) { return template; }
    }

    /// <summary>Có / Không theo ngôn ngữ hiện hành.</summary>
    public static string YesNo(bool value) => T(value ? "common.yes" : "common.no");
}

/// <summary>Chọn font phù hợp với chữ viết của ngôn ngữ hiện hành, có fallback nếu font chưa cài.</summary>
public static class LocalizedFont
{
    private static readonly HashSet<string> _installed = LoadInstalledFamilies();

    private static HashSet<string> LoadInstalledFamilies()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var fonts = new InstalledFontCollection();
            foreach (var family in fonts.Families)
                set.Add(family.Name);
        }
        catch
        {
            // Không lấy được danh sách font — dùng mặc định.
        }
        return set;
    }

    /// <summary>
    /// Họ font nên dùng cho ngôn ngữ hiện hành. Nếu font ưu tiên (Lào/Khmer) chưa cài
    /// thì lùi về "Leelawadee UI" rồi "Segoe UI".
    /// </summary>
    public static string FamilyFor(AppLanguage language)
    {
        var preferred = language.PreferredFontFamily();
        if (_installed.Count == 0 || _installed.Contains(preferred))
            return preferred;

        if (_installed.Contains("Leelawadee UI"))
            return "Leelawadee UI";

        return "Segoe UI";
    }

    /// <summary>Họ font cho ngôn ngữ đang dùng.</summary>
    public static string Current => FamilyFor(Loc.Language);

    /// <summary>
    /// Font in phiếu theo ngôn ngữ hiện hành. Phiếu tiếng Việt/Anh giữ Times New Roman như mẫu giấy;
    /// Times New Roman không có chữ Lào/Khmer nên dùng font hệ thống có glyph tương ứng.
    /// </summary>
    public static string Print => Loc.Language switch
    {
        AppLanguage.Lo or AppLanguage.Km => FamilyFor(Loc.Language),
        _ => "Times New Roman"
    };

    /// <summary>
    /// Font biểu tượng có sẵn trên Windows: ưu tiên Segoe Fluent Icons (Win11),
    /// lùi về Segoe MDL2 Assets (Win10). Null nếu không có — UI chỉ hiển thị chữ.
    /// </summary>
    public static string? IconFamily { get; } =
        _installed.Contains("Segoe Fluent Icons") ? "Segoe Fluent Icons"
        : _installed.Contains("Segoe MDL2 Assets") ? "Segoe MDL2 Assets"
        : null;
}
