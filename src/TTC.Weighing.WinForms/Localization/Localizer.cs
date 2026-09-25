using System.Globalization;
using System.Text.Json;

namespace TTC.Weighing.WinForms.Localization;

/// <summary>
/// Bản cài đặt <see cref="ILocalizer"/> đọc chuỗi từ các tệp JSON trong thư mục
/// Localization/Resources (strings.vi.json, strings.en.json, strings.lo.json, strings.km.json).
/// Các tệp được sao chép ra thư mục output khi build.
/// </summary>
public sealed class Localizer : ILocalizer
{
    private const AppLanguage FallbackLanguage = AppLanguage.Vi;

    private readonly Dictionary<AppLanguage, Dictionary<string, string>> _tables = [];
    private readonly string _resourceDir;

    public Localizer(AppLanguage initial = AppLanguage.Vi)
    {
        _resourceDir = Path.Combine(AppContext.BaseDirectory, "Localization", "Resources");
        LoadAll();
        Current = initial;
        ApplyCulture(initial);
    }

    public AppLanguage Current { get; private set; }

    public IReadOnlyList<AppLanguage>? AllowedLanguages { get; private set; }

    public event EventHandler? LanguageChanged;

    public void SetAllowedLanguages(IEnumerable<AppLanguage>? languages)
    {
        AllowedLanguages = languages?.ToList().AsReadOnly();
    }

    public void SetLanguage(AppLanguage language)
    {
        if (language == Current) return;
        Current = language;
        ApplyCulture(language);
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string this[string key] => T(key);

    public string T(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;

        if (_tables.TryGetValue(Current, out var table)
            && table.TryGetValue(key, out var value))
            return value;

        // Lùi về tiếng Việt nếu ngôn ngữ hiện tại thiếu khóa
        if (Current != FallbackLanguage
            && _tables.TryGetValue(FallbackLanguage, out var fallback)
            && fallback.TryGetValue(key, out var fallbackValue))
            return fallbackValue;

        // Lộ khóa thiếu để dễ phát hiện khi phát triển
        return key;
    }

    public string T(string key, params object[] args)
    {
        var template = T(key);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    private static void ApplyCulture(AppLanguage language)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(language.ToCultureName());
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
        }
        catch (CultureNotFoundException)
        {
            // Một số culture (lo-LA, km-KH) có thể không đầy đủ trên máy cũ — bỏ qua, giữ định dạng mặc định.
        }
    }

    private void LoadAll()
    {
        foreach (AppLanguage lang in Enum.GetValues<AppLanguage>())
            _tables[lang] = LoadTable(lang);
    }

    private Dictionary<string, string> LoadTable(AppLanguage lang)
    {
        var path = Path.Combine(_resourceDir, $"strings.{lang.ToCode()}.json");

        if (!File.Exists(path))
            return [];

        try
        {
            var json = File.ReadAllText(path);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return dict ?? [];
        }
        catch (Exception)
        {
            // Tệp lỗi không làm sập app — trả bảng rỗng, khóa sẽ lùi về tiếng Việt.
            return [];
        }
    }
}
