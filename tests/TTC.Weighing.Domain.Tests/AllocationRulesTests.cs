using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Domain.Rules;

namespace TTC.Weighing.Domain.Tests;

public class AllocationRulesTests
{
    private static AllocationRequestLine RatioLine(string owner, decimal ratio)
        => new(null, owner, ratio, null, false);

    private static AllocationRequestLine FixedLine(string owner, decimal kg)
        => new(null, owner, null, kg, false);

    private static AllocationRequestLine RemainderLine(string owner)
        => new(null, owner, null, null, true);

    /// <summary>Tổng phân bổ phải bằng tổng thực tế (R-021).</summary>
    [Fact]
    public void Ratio_allocation_preserves_measured_total()
    {
        var result = AllocationRules.Allocate(
            measuredTotalKg: 18_000m,
            AllocationMethod.ApprovedRatio,
            [RatioLine("A", 0.5m), RatioLine("B", 0.3m), RatioLine("C", 0.2m)]);

        Assert.True(result.IsValid, result.InvalidReason);
        Assert.Equal(18_000m, result.TotalAllocatedKg);
        Assert.Equal(3, result.Lines.Count);
    }

    [Fact]
    public void Ratio_allocation_splits_proportionally()
    {
        var result = AllocationRules.Allocate(
            10_000m, AllocationMethod.ApprovedRatio,
            [RatioLine("A", 0.6m), RatioLine("B", 0.4m)]);

        Assert.Equal(6_000m, result.Lines[0].AllocatedKg);
        Assert.Equal(4_000m, result.Lines[1].AllocatedKg);
    }

    /// <summary>
    /// Phần dư làm tròn đi vào một dòng điều chỉnh minh bạch,
    /// không được làm biến mất (02_Quy_tac 4.3).
    /// </summary>
    [Fact]
    public void Rounding_remainder_is_recorded_not_lost()
    {
        var result = AllocationRules.Allocate(
            10_000m, AllocationMethod.ApprovedRatio,
            [RatioLine("A", 1m), RatioLine("B", 1m), RatioLine("C", 1m)]);

        Assert.True(result.IsValid, result.InvalidReason);
        Assert.Equal(10_000m, result.TotalAllocatedKg);

        // Dòng cuối nhận phần dư và ghi rõ mức điều chỉnh.
        Assert.NotEqual(0m, result.Lines[^1].RoundingAdjustmentKg);
    }

    /// <summary>Tỷ lệ không cần chuẩn hóa sẵn về 1; engine tự chia theo tổng tỷ lệ.</summary>
    [Fact]
    public void Ratios_are_normalised_by_their_sum()
    {
        var result = AllocationRules.Allocate(
            9_000m, AllocationMethod.OrderWeightRatio,
            [RatioLine("A", 2m), RatioLine("B", 1m)]);

        Assert.Equal(6_000m, result.Lines[0].AllocatedKg);
        Assert.Equal(3_000m, result.Lines[1].AllocatedKg);
    }

    [Fact]
    public void Missing_ratio_is_rejected()
    {
        var result = AllocationRules.Allocate(
            10_000m, AllocationMethod.ApprovedRatio,
            [RatioLine("A", 0.5m), new AllocationRequestLine(null, "B", null, null, false)]);

        Assert.False(result.IsValid);
        Assert.Contains("tỷ lệ đã duyệt", result.InvalidReason);
    }

    // Phương pháp số lượng xác định và phần còn lại

    [Fact]
    public void Fixed_quantity_assigns_remainder_to_marked_line()
    {
        var result = AllocationRules.Allocate(
            18_000m, AllocationMethod.FixedQuantityWithRemainder,
            [FixedLine("A", 5_000m), FixedLine("B", 3_000m), RemainderLine("C")]);

        Assert.True(result.IsValid, result.InvalidReason);
        Assert.Equal(18_000m, result.TotalAllocatedKg);
        Assert.Equal(10_000m, result.Lines[^1].AllocatedKg);
    }

    [Fact]
    public void Fixed_quantity_exceeding_total_is_rejected()
    {
        var result = AllocationRules.Allocate(
            5_000m, AllocationMethod.FixedQuantityWithRemainder,
            [FixedLine("A", 4_000m), FixedLine("B", 3_000m), RemainderLine("C")]);

        Assert.False(result.IsValid);
        Assert.Contains("vượt tổng đã đo", result.InvalidReason);
    }

    [Fact]
    public void Exactly_one_remainder_line_is_required()
    {
        var twoRemainders = AllocationRules.Allocate(
            10_000m, AllocationMethod.FixedQuantityWithRemainder,
            [RemainderLine("A"), RemainderLine("B")]);

        var noRemainder = AllocationRules.Allocate(
            10_000m, AllocationMethod.FixedQuantityWithRemainder,
            [FixedLine("A", 5_000m)]);

        Assert.False(twoRemainders.IsValid);
        Assert.False(noRemainder.IsValid);
    }

    // Điều kiện đầu vào

    [Fact]
    public void Empty_line_list_is_rejected()
    {
        var result = AllocationRules.Allocate(10_000m, AllocationMethod.ApprovedRatio, []);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Non_positive_total_is_rejected(decimal total)
    {
        var result = AllocationRules.Allocate(
            total, AllocationMethod.ApprovedRatio, [RatioLine("A", 1m)]);

        Assert.False(result.IsValid);
    }
}
