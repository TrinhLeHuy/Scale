namespace TTC.Weighing.Domain.ValueObjects;

/// <summary>
/// Số cân bất biến. Lưu số gốc + đơn vị gốc + kg quy đổi, không làm tròn trung gian (02_Quy_tac 4.1, R-008).
/// Dùng decimal, tuyệt đối không double/float.
/// </summary>
public sealed record WeightMeasurement
{
    /// <summary>Giá trị nguyên bản do thiết bị trả về.</summary>
    public decimal RawValue { get; }

    /// <summary>Đơn vị gốc của thiết bị: kg, g, t.</summary>
    public string RawUom { get; }

    /// <summary>Giá trị quy đổi sang kg. Giữ đủ độ phân giải của thiết bị.</summary>
    public decimal NormalizedKg { get; }

    private WeightMeasurement(decimal rawValue, string rawUom, decimal normalizedKg)
    {
        RawValue = rawValue;
        RawUom = rawUom;
        NormalizedKg = normalizedKg;
    }

    public static WeightMeasurement Create(decimal rawValue, string rawUom)
    {
        if (string.IsNullOrWhiteSpace(rawUom))
            throw new ArgumentException("Đơn vị gốc bắt buộc; không suy đoán kg.", nameof(rawUom));

        var factor = rawUom.Trim().ToLowerInvariant() switch
        {
            "kg" => 1m,
            "g" => 0.001m,
            "t" => 1000m,
            _ => throw new ArgumentException($"Đơn vị không hỗ trợ: {rawUom}", nameof(rawUom))
        };

        return new WeightMeasurement(rawValue, rawUom.Trim().ToLowerInvariant(), rawValue * factor);
    }

    public override string ToString() => $"{NormalizedKg:N3} kg (gốc {RawValue} {RawUom})";
}
