using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Domain.Rules;

/// <summary>Kết quả tính net cân xe, kèm cờ ngoại lệ để tầng trên xử lý.</summary>
public sealed record NetWeightResult(
    decimal VehicleNetKg,
    bool IsNegative,
    bool IsZero)
{
    /// <summary>
    /// Net âm hoặc bằng 0 là ngoại lệ cần xử lý theo profile đã duyệt, không tự hợp thức hóa.
    /// </summary>
    public bool RequiresExceptionHandling => IsNegative || IsZero;
}

/// <summary>
/// Tính net cân xe theo hướng nghiệp vụ. Nguồn: 02_Quy_tac mục 4.1.
/// KHÔNG dùng Math.Abs để che net âm. KHÔNG hard-code lần 1 luôn là bì.
/// </summary>
public static class NetWeightCalculator
{
    /// <param name="direction">Hướng nghiệp vụ của profile, quyết định dấu của công thức.</param>
    /// <param name="firstWeightKg">Lần cân theo thứ tự thời gian, không phải vai trò.</param>
    /// <param name="secondWeightKg">Lần cân thứ hai theo thứ tự thời gian.</param>
    public static NetWeightResult Calculate(
        ProfileDirection direction,
        decimal firstWeightKg,
        decimal secondWeightKg)
    {
        var net = direction switch
        {
            // Nhập: lần 1 có hàng, lần 2 là bì.
            ProfileDirection.Inbound => firstWeightKg - secondWeightKg,

            // Xuất: lần 1 là bì, lần 2 có hàng.
            ProfileDirection.Outbound => secondWeightKg - firstWeightKg,

            // Nội bộ theo S04 tr.55: kho nguồn -> cân có hàng -> kho đích -> cân bì.
            ProfileDirection.InternalTransfer => firstWeightKg - secondWeightKg,

            _ => throw new ArgumentOutOfRangeException(nameof(direction))
        };

        return new NetWeightResult(
            VehicleNetKg: net,
            IsNegative: net < 0,
            IsZero: net == 0);
    }

    /// <summary>
    /// Tính net theo vai trò đo đã khai báo, dùng cho profile nhiều phân đoạn (R-030).
    /// </summary>
    public static NetWeightResult CalculateByRole(
        decimal grossKg,
        decimal tareKg)
    {
        var net = grossKg - tareKg;
        return new NetWeightResult(net, net < 0, net == 0);
    }
}
