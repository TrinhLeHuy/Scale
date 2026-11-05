using System.Globalization;
using System.Text.RegularExpressions;
using TTC.Weighing.Application.Abstractions;

namespace TTC.Weighing.Infrastructure.Devices.SerialScale;

/// <summary>Một dòng số liệu đã tách từ chuỗi đầu cân.</summary>
public sealed record ScaleFrame(decimal Value, string? Unit, bool? Stable, bool Overload);

/// <summary>
/// Tách dòng dữ liệu đầu cân.
/// <list type="bullet">
/// <item>A&amp;D chuẩn (AD-4329A): <c>ST,GS,+0012345kg</c>. Header 1: ST ổn định, US chưa ổn định, OL quá tải.
///   Header 2: GS cân tổng, NT cân tịnh, TR bì.</item>
/// <item>Chung: lấy số có dấu đầu tiên trong dòng, đơn vị kg / t / g nếu có.</item>
/// <item>Kiểu phần mềm cân cũ (CanDienTu): chỉ giữ "+ - . 0-9", ưu tiên cụm có dấu "+0000140".</item>
/// </list>
/// </summary>
public static partial class ScaleFrameParser
{
    [GeneratedRegex(@"^\s*(?<h1>ST|US|OL|QT|WT)\s*,\s*(?:(?<h2>[A-Z]{2})\s*,\s*)?(?<num>[+-]?\s*[\d.]+)?\s*(?<unit>kg|KG|t|T|g|lb)?",
        RegexOptions.CultureInvariant)]
    private static partial Regex AndRegex();

    [GeneratedRegex(@"(?<num>[+-]?\s*\d+(?:\.\d+)?)\s*(?<unit>kg|KG|Kg|t|T|g)?(?![\w])", RegexOptions.CultureInvariant)]
    private static partial Regex GenericRegex();

    [GeneratedRegex(@"\b(ST|US|OL)\b", RegexOptions.CultureInvariant)]
    private static partial Regex StatusHeaderRegex();

    [GeneratedRegex(@"[-+]\d{1,8}(?:\.\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex SignedDigitsRegex();

    [GeneratedRegex(@"[-+]?\d+(?:\.\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex AnyDigitsRegex();

    public static ScaleFrame? Parse(string line, ScaleFrameFormat format)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        if (format == ScaleFrameFormat.SignedDigits)
        {
            // Như phần mềm cân cũ: chỉ giữ "+ - . 0-9" (bỏ byte nhiễu / ký tự trạng thái), ưu tiên cụm có dấu.
            var clean = new string(line.Where(c => c is '+' or '-' or '.' or (>= '0' and <= '9')).ToArray());
            if (clean.Length == 0) return null;
            var d = SignedDigitsRegex().Match(clean);
            if (!d.Success) d = AnyDigitsRegex().Match(clean);
            if (!d.Success || !TryNumber(d.Value, out var digits)) return null;

            // Khung "ST,GS,+0004450kg": ST = số ổn định, US = đang dao động, OL = quá tải. Không có header → không rõ.
            var head = StatusHeaderRegex().Match(line);
            bool? stable = head.Success ? head.Value switch { "ST" => true, "US" => false, _ => null } : null;
            return new ScaleFrame(digits, null, stable, head.Success && head.Value == "OL");
        }

        if (format == ScaleFrameFormat.AndStandard && AndRegex().Match(line) is { Success: true } m)
        {
            var header = m.Groups["h1"].Value;
            if (header == "OL")
                return new ScaleFrame(0m, Unit(m.Groups["unit"].Value), false, true);

            if (!m.Groups["num"].Success || !TryNumber(m.Groups["num"].Value, out var v))
                return null;

            bool? stable = header switch { "ST" => true, "US" => false, _ => null };
            return new ScaleFrame(v, Unit(m.Groups["unit"].Value), stable, false);
        }

        // A&D không khớp (đầu cân đang đặt kiểu khác) hoặc chọn kiểu chung → lấy số đầu tiên.
        var g = GenericRegex().Match(line);
        if (!g.Success || !TryNumber(g.Groups["num"].Value, out var value)) return null;
        return new ScaleFrame(value, Unit(g.Groups["unit"].Value), null, false);
    }

    /// <summary>Quy về kg theo đơn vị trong chuỗi, không có thì theo đơn vị cấu hình; rồi nhân hệ số.</summary>
    public static decimal ToKg(ScaleFrame frame, ScaleConnectionSettings settings)
    {
        var unit = frame.Unit ?? settings.Unit;
        var kg = unit switch
        {
            "t" => frame.Value * 1000m,
            "g" => frame.Value / 1000m,
            _ => frame.Value
        };
        return kg * settings.Factor;
    }

    private static string? Unit(string raw) => raw.ToLowerInvariant() switch
    {
        "kg" => "kg",
        "t" => "t",
        "g" => "g",
        _ => null
    };

    private static bool TryNumber(string raw, out decimal value)
        => decimal.TryParse(raw.Replace(" ", ""), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out value);
}
