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
}
