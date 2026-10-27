using TTC.Weighing.Domain.Rules;

namespace TTC.Weighing.Domain.Tests;

public class ImpurityCalculatorTests
{
    /// <summary>S02 tr.18: (before - after) / before × 100.</summary>
    [Fact]
    public void Calculates_percentage_from_two_sample_weights()
    {
        var result = ImpurityCalculator.Calculate(sampleBeforeKg: 20m, sampleAfterKg: 17m);

        Assert.True(result.IsValid);
        Assert.Equal(15m, result.ImpurityPercent);
        Assert.False(result.BelowMinimumSampleWarning);
    }

    [Fact]
    public void Second_weighing_equal_to_first_is_invalid()
    {
        // Lần 2 phải nhỏ hơn lần 1 (quy tắc đo tạp chất).
        var result = ImpurityCalculator.Calculate(20m, 20m);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Sample_before_must_be_positive()
    {
        var result = ImpurityCalculator.Calculate(0m, 0m);

        Assert.False(result.IsValid);
        Assert.Null(result.ImpurityPercent);
    }

    [Fact]
    public void Negative_sample_after_is_invalid()
    {
        var result = ImpurityCalculator.Calculate(20m, -1m);

        Assert.False(result.IsValid);
    }

    /// <summary>Mẫu sau lớn hơn mẫu trước là số đo không hợp lệ, phải lấy lại mẫu.</summary>
    [Fact]
    public void Sample_after_greater_than_before_is_invalid()
    {
        var result = ImpurityCalculator.Calculate(20m, 25m);

        Assert.False(result.IsValid);
        Assert.Contains("cân lại", result.InvalidReason);
    }

    /// <summary>
    /// Mẫu dưới ngưỡng chỉ CẢNH BÁO, không phải lỗi cứng.
    /// Ngưỡng 10 kg là giá trị tham khảo trong tài liệu, không phải quy chuẩn pháp lý.
    /// </summary>
    [Fact]
    public void Sample_below_minimum_warns_but_stays_valid()
    {
        var result = ImpurityCalculator.Calculate(8m, 7m);

        Assert.True(result.IsValid);
        Assert.True(result.BelowMinimumSampleWarning);
        Assert.NotNull(result.ImpurityPercent);
    }

    [Fact]
    public void Minimum_threshold_is_configurable_per_profile()
    {
        var result = ImpurityCalculator.Calculate(8m, 7m, minimumSampleKg: 5m);

        Assert.True(result.IsValid);
        Assert.False(result.BelowMinimumSampleWarning);
    }
}
