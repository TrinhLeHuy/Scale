namespace TTC.Weighing.WinForms.Localization;

/// <summary>
/// Bốn ngôn ngữ được hỗ trợ. Mã theo ISO 639-1 để khớp với API web portal
/// (portal trả về ngôn ngữ theo BU của tài khoản).
/// </summary>
public enum AppLanguage
{
    /// <summary>Tiếng Việt.</summary>
    Vi,

    /// <summary>English.</summary>
    En,

    /// <summary>ພາສາລາວ — Lào.</summary>
    Lo,

    /// <summary>ភាសាខ្មែរ — Khmer.</summary>
    Km
}

/// <summary>Ánh xạ giữa <see cref="AppLanguage"/>, mã ISO, culture và font phù hợp với chữ viết.</summary>
public static class AppLanguageInfo
{
    /// <summary>Mã ISO 639-1 dùng khi trao đổi với API portal.</summary>
    public static string ToCode(this AppLanguage lang) => lang switch
    {
        AppLanguage.Vi => "vi",
        AppLanguage.En => "en",
        AppLanguage.Lo => "lo",
        AppLanguage.Km => "km",
        _ => "vi"
    };

    /// <summary>Culture name cho định dạng số/ngày.</summary>
    public static string ToCultureName(this AppLanguage lang) => lang switch
    {
        AppLanguage.Vi => "vi-VN",
        AppLanguage.En => "en-US",
        AppLanguage.Lo => "lo-LA",
        AppLanguage.Km => "km-KH",
        _ => "vi-VN"
    };

    /// <summary>Tên hiển thị của ngôn ngữ (viết bằng chính ngôn ngữ đó).</summary>
    public static string DisplayName(this AppLanguage lang) => lang switch
    {
        AppLanguage.Vi => "Tiếng Việt",
        AppLanguage.En => "English",
        AppLanguage.Lo => "ພາສາລາວ",
        AppLanguage.Km => "ភាសាខ្មែរ",
        _ => "Tiếng Việt"
    };

    /// <summary>
    /// Font phù hợp với chữ viết của ngôn ngữ. Segoe UI không có glyph Lào/Khmer nên phải
    /// đổi sang font hệ thống hỗ trợ. Có fallback nếu font không được cài.
    /// </summary>
    public static string PreferredFontFamily(this AppLanguage lang) => lang switch
    {
        AppLanguage.Lo => "Leelawadee UI",   // Windows ships — hỗ trợ chữ Lào
        AppLanguage.Km => "Khmer UI",         // Windows ships — hỗ trợ chữ Khmer
        _ => "Segoe UI"
    };

    /// <summary>Chuyển mã ISO (từ API) thành <see cref="AppLanguage"/>. Mặc định tiếng Việt.</summary>
    public static AppLanguage FromCode(string? code) => (code?.Trim().ToLowerInvariant()) switch
    {
        "vi" or "vi-vn" => AppLanguage.Vi,
        "en" or "en-us" or "en-gb" => AppLanguage.En,
        "lo" or "lo-la" => AppLanguage.Lo,
        "km" or "km-kh" => AppLanguage.Km,
        _ => AppLanguage.Vi
    };
}
