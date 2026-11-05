using System.Globalization;

namespace TTC.Weighing.Infrastructure.Adapters;

/// <summary>
/// Đọc ngày giờ kéo về từ DigiScale (FRM / ERP) — GIỮ NGUYÊN giờ như API trả,
/// không cộng/trừ múi giờ. Hậu tố "Z" hoặc "+hh:mm" (nếu có) bị bỏ qua:
/// "2026-10-28T08:00:00Z" → 28/10/2026 08:00 (không thành 15:00).
/// </summary>
public static class DigiScaleDate
{
    private static readonly string[] DisplayFormats =
        ["dd/MM/yyyy", "dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss"];

    /// <summary>Giờ đồng hồ đúng như chuỗi API (Kind = Unspecified). Null nếu rỗng / không đọc được.</summary>
    public static DateTime? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var s = value.Trim();

        // DateTimeOffset.DateTime = giờ đồng hồ theo offset ghi trong chuỗi, không quy đổi.
        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
            return DateTime.SpecifyKind(dto.DateTime, DateTimeKind.Unspecified);

        return DateTime.TryParseExact(s, DisplayFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var p)
            ? DateTime.SpecifyKind(p, DateTimeKind.Unspecified)
            : null;
    }

    /// <summary>
    /// Như <see cref="Parse"/> nhưng gắn offset giờ Việt Nam (+7): giờ hiển thị (qua ToVnTime()) vẫn đúng
    /// giờ API, và so sánh với VnTime.NowOffset theo giờ Việt Nam — không phụ thuộc múi giờ máy trạm.
    /// </summary>
    public static DateTimeOffset? ParseOffset(string? value)
        => Parse(value) is { } dt ? Domain.Common.VnTime.FromVnClock(dt) : null;
}
