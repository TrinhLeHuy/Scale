namespace TTC.Weighing.WinForms.Localization;

/// <summary>
/// Cung cấp chuỗi đã dịch theo ngôn ngữ hiện hành và cho phép đổi ngôn ngữ lúc chạy.
/// Ngôn ngữ có thể do API web portal quyết định (theo BU của tài khoản).
/// </summary>
public interface ILocalizer
{
    /// <summary>Ngôn ngữ đang dùng.</summary>
    AppLanguage Current { get; }

    /// <summary>Phát khi ngôn ngữ đổi — form đăng ký để vẽ lại chuỗi.</summary>
    event EventHandler? LanguageChanged;

    /// <summary>Đổi ngôn ngữ hiện hành và cập nhật culture của luồng UI.</summary>
    void SetLanguage(AppLanguage language);

    /// <summary>
    /// Danh sách ngôn ngữ mà tài khoản/BU được phép dùng.
    /// Null hoặc rỗng = cho phép tất cả 4 ngôn ngữ (chế độ trước đăng nhập).
    /// Sau đăng nhập, giới hạn theo <c>allowed_cultures</c> từ API.
    /// </summary>
    IReadOnlyList<AppLanguage>? AllowedLanguages { get; }

    /// <summary>
    /// Thiết lập danh sách ngôn ngữ được phép cho tài khoản/BU.
    /// Gọi sau khi đăng nhập thành công.
    /// </summary>
    void SetAllowedLanguages(IEnumerable<AppLanguage>? languages);

    /// <summary>
    /// Lấy chuỗi theo khóa. Nếu thiếu ở ngôn ngữ hiện tại thì lùi về tiếng Việt,
    /// rồi tới chính khóa (để lộ khóa thiếu khi dev).
    /// </summary>
    string T(string key);

    /// <summary>Lấy chuỗi và format tham số theo culture hiện hành.</summary>
    string T(string key, params object[] args);

    /// <summary>Truy cập nhanh dạng indexer: loc["key"].</summary>
    string this[string key] { get; }
}
