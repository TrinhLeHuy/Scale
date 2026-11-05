using TTC.Weighing.Domain.Common;
using TTC.Weighing.Domain.Entities;

namespace TTC.Weighing.Domain.Rules;

/// <summary>
/// Giới hạn số cân theo lượt của bàn cân (Vào min/Max = lần 1, Ra min/Max = lần 2), cài ở màn Kết nối cân.
/// Giới hạn chưa đặt (null) thì bỏ qua. Không thay cho kiểm tra khả năng đo (MinCapacityKg/MaxCapacityKg).
/// </summary>
public static class WeighLimitRules
{
    public const string CODE = "B-001.9";

    /// <summary>Null = lượt cân này không đặt giới hạn nào.</summary>
    public static ConditionCheck? Check(Weighbridge bridge, int segmentIndex, decimal kg)
    {
        var (minKg, maxKg) = bridge.WeighLimits(segmentIndex);
        if (minKg is null && maxKg is null) return null;

        var lan = segmentIndex == 0 ? 1 : 2;
        var name = DomainText.T("rule.weigh_limit_name", "Giới hạn số cân lần {0}", lan);

        if (minKg is { } min && kg < min)
            return new(CODE, name, false, DomainText.T("rule.weigh_limit_below",
                "Số cân {0:N3} kg nhỏ hơn mức tối thiểu lần {1} của bàn {2} ({3:N3} kg).", kg, lan, bridge.Code, min));

        if (maxKg is { } max && kg > max)
            return new(CODE, name, false, DomainText.T("rule.weigh_limit_above",
                "Số cân {0:N3} kg vượt mức tối đa lần {1} của bàn {2} ({3:N3} kg).", kg, lan, bridge.Code, max));

        return new(CODE, name, true);
    }

    /// <summary>Lỗi nhập giới hạn (null = hợp lệ). Giá trị null = không đặt.</summary>
    public static string? Validate(decimal? minKg, decimal? maxKg, int lan)
    {
        if (minKg is < 0 || maxKg is < 0)
            return DomainText.T("rule.weigh_limit_negative", "Giới hạn lần {0} không được âm.", lan);
        if (minKg is { } min && maxKg is { } max && min >= max)
            return DomainText.T("rule.weigh_limit_order",
                "Giới hạn lần {0}: tối thiểu ({1:N3} kg) phải nhỏ hơn tối đa ({2:N3} kg).", lan, min, max);
        return null;
    }
}
