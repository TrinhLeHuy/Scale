using System.Globalization;

namespace TTC.Weighing.Domain.Common;

/// <summary>
/// Quy tắc số cho MỌI dữ liệu dùng để tính toán và gửi đi (FRM / ERP / DigiScale): làm tròn 2 chữ số thập phân,
/// nửa lên (5 → lên: 4,935 → 4,94). Gửi đi dùng dấu chấm thập phân (InvariantCulture).
/// </summary>
public static class Num
{
    public const int DECIMALS = 2;

    /// <summary>Làm tròn 2 chữ số thập phân, nửa lên.</summary>
    public static decimal R2(decimal value) => Math.Round(value, DECIMALS, MidpointRounding.AwayFromZero);

    public static decimal? R2(decimal? value) => value is { } v ? R2(v) : null;

    /// <summary>Chuỗi gửi đi: "12.35" (luôn 2 chữ số, dấu chấm).</summary>
    public static string S2(decimal value) => R2(value).ToString("F2", CultureInfo.InvariantCulture);

    /// <summary>Chuỗi gửi đi; null → <paramref name="whenNull"/>.</summary>
    public static string S2(decimal? value, string whenNull) => value is { } v ? S2(v) : whenNull;
}
