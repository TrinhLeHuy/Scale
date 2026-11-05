using TTC.Weighing.Domain.Common;

namespace TTC.Weighing.Domain.Rules;

public sealed record ImpurityResult(
    bool IsValid,
    decimal? ImpurityPercent,
    bool BelowMinimumSampleWarning,
    string? InvalidReason);

/// <summary>
/// Cân tạp chất mía theo S02 tr.18: (before - after) / before × 100.
/// Ngưỡng mẫu tối thiểu là cấu hình, cảnh báo chứ không phải lỗi cứng.
/// Không tự xây công thức trọng lượng thanh toán hay khấu trừ thương mại.
/// </summary>
public static class ImpurityCalculator
{
    /// <summary>Giá trị tham khảo trong tài liệu. Không phải quy chuẩn pháp lý đang hiệu lực.</summary>
    public const decimal DocumentReferenceMinSampleKg = 10m;

    /// <param name="minimumSampleKg">Ngưỡng cảnh báo theo cấu hình profile.</param>
    public static ImpurityResult Calculate(
        decimal sampleBeforeKg,
        decimal sampleAfterKg,
        decimal minimumSampleKg = DocumentReferenceMinSampleKg)
    {
        if (sampleBeforeKg <= 0)
            return new(false, null, false, DomainText.T("rule.impurity_before_positive", "Khối lượng mẫu trước phải lớn hơn 0."));

        if (sampleAfterKg < 0)
            return new(false, null, false, DomainText.T("rule.impurity_after_negative", "Khối lượng mẫu sau không được âm."));

        // Lần 2 (mẫu sau khi làm sạch) phải NHỎ HƠN lần 1 — bằng hoặc lớn hơn đều không hợp lệ.
        if (sampleAfterKg >= sampleBeforeKg)
            return new(false, null, false,
                DomainText.T("rule.impurity_after_gt_before", "Lần 2 phải nhỏ hơn lần 1 — số đo không hợp lệ, cần cân lại."));

        // Số cân mẫu và tạp chất làm tròn 2 chữ số thập phân (quy tắc chung cho dữ liệu tính toán / gửi đi).
        sampleBeforeKg = Num.R2(sampleBeforeKg);
        sampleAfterKg = Num.R2(sampleAfterKg);
        var percent = Num.R2((sampleBeforeKg - sampleAfterKg) / sampleBeforeKg * 100m);
        var belowMinimum = sampleBeforeKg < minimumSampleKg;

        return new(true, percent, belowMinimum, null);
    }
}
