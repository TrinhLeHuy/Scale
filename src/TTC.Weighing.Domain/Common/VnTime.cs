namespace TTC.Weighing.Domain.Common;

/// <summary>
/// Giờ Việt Nam (UTC+7) cho mọi mốc thời gian DO HỆ THỐNG SINH RA: giờ đăng tài, cân vào / cân ra,
/// đo tạp chất / CCS, giờ in phiếu, giờ gửi FRM / ERP — không phụ thuộc múi giờ cài trên máy trạm.
/// DB vẫn lưu UTC (timestamptz); chỉ quy đổi khi hiển thị / in / gửi đi.
/// <para>
/// Ngày giờ KÉO VỀ từ DigiScale (FRM / ERP) KHÔNG đi qua đây để đổi giờ — giữ nguyên giờ đồng hồ như API trả
/// (xem DigiScaleDate); chỉ gắn offset +7 để so sánh với giờ hiện tại.
/// </para>
/// </summary>
public static class VnTime
{
    /// <summary>Múi giờ Việt Nam. Windows: "SE Asia Standard Time"; Linux/IANA: "Asia/Ho_Chi_Minh"; không có → +7 cố định.</summary>
    public static TimeZoneInfo Zone { get; } = FindZone();

    private static TimeZoneInfo FindZone()
    {
        foreach (var id in new[] { "SE Asia Standard Time", "Asia/Ho_Chi_Minh", "Asia/Saigon" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("VN+07", TimeSpan.FromHours(7), "Việt Nam (UTC+7)", "Việt Nam (UTC+7)");
    }

    /// <summary>Giờ đồng hồ Việt Nam hiện tại (Kind = Unspecified).</summary>
    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);

    /// <summary>Ngày hôm nay theo giờ Việt Nam (00:00).</summary>
    public static DateTime Today => Now.Date;

    /// <summary>Thời điểm hiện tại, offset +7.</summary>
    public static DateTimeOffset NowOffset => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Zone);

    /// <summary>Offset của giờ Việt Nam tại một giờ đồng hồ (luôn +7, VN không đổi giờ mùa hè).</summary>
    public static TimeSpan OffsetAt(DateTime vnClock) => Zone.GetUtcOffset(DateTime.SpecifyKind(vnClock, DateTimeKind.Unspecified));

    /// <summary>Giờ đồng hồ Việt Nam → DateTimeOffset +7 (vd mốc đầu ngày cho bộ lọc).</summary>
    public static DateTimeOffset FromVnClock(DateTime vnClock)
    {
        var clock = DateTime.SpecifyKind(vnClock, DateTimeKind.Unspecified);
        return new DateTimeOffset(clock, OffsetAt(clock));
    }

    /// <summary>Giờ đồng hồ Việt Nam → UTC (để lọc cột timestamptz).</summary>
    public static DateTime ToUtc(DateTime vnClock) => FromVnClock(vnClock).UtcDateTime;

    /// <summary>Một thời điểm (thường UTC từ DB) → hiển thị theo giờ Việt Nam.</summary>
    public static DateTimeOffset ToVnTime(this DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, Zone);

    public static DateTimeOffset? ToVnTime(this DateTimeOffset? value) => value?.ToVnTime();

    /// <summary>
    /// DateTime → giờ Việt Nam. Kind Local = giờ máy trạm; Utc / Unspecified = UTC (như DateTime.ToLocalTime()).
    /// </summary>
    public static DateTime ToVnTime(this DateTime value) => value.Kind == DateTimeKind.Local
        ? TimeZoneInfo.ConvertTime(value, TimeZoneInfo.Local, Zone)
        : TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(value, DateTimeKind.Utc), Zone);
}
