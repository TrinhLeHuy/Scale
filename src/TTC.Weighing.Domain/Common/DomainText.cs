using System.Globalization;

namespace TTC.Weighing.Domain.Common;

/// <summary>
/// Chuỗi thông báo nghiệp vụ đa ngôn ngữ cho Domain/Application/Infrastructure (lý do chặn cân,
/// lỗi QR, kết luận đối soát...). Domain không phụ thuộc UI: tầng UI cài <see cref="Resolver"/>
/// lúc khởi động để tra khóa trong file ngôn ngữ (vi/en/lo/km).
/// Chưa cài resolver (unit test, tool) hoặc thiếu khóa thì dùng bản tiếng Việt đi kèm.
/// Khóa phải được khai báo đủ trong cả 4 file strings.*.json.
/// </summary>
public static class DomainText
{
    /// <summary>Tra chuỗi theo khóa ở ngôn ngữ hiện hành. Trả null nếu không có.</summary>
    public static Func<string, string?>? Resolver { get; set; }

    /// <summary>Lấy chuỗi theo khóa; <paramref name="viFallback"/> là bản tiếng Việt mặc định.</summary>
    public static string T(string key, string viFallback, params object?[] args)
    {
        var template = Resolver?.Invoke(key) ?? viFallback;
        if (args.Length == 0) return template;

        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            return string.Format(CultureInfo.CurrentCulture, viFallback, args);
        }
    }
}
