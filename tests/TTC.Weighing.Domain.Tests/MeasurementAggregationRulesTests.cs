using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Domain.Rules;

namespace TTC.Weighing.Domain.Tests;

public class MeasurementAggregationRulesTests
{
    private static IotReading Reading(
        string eventId, long sequence, decimal kg, string? epoch = null)
        => new(eventId, sequence, kg, epoch, DateTimeOffset.UtcNow);

    // B-011 — chế độ DELTA

    [Fact]
    public void Delta_sums_distinct_events()
    {
        var result = MeasurementAggregationRules.AggregateDelta(
            [Reading("a", 1, 100m), Reading("b", 2, 200m), Reading("c", 3, 300m)]);

        Assert.True(result.IsUsable);
        Assert.Equal(600m, result.TotalKg);
    }

    /// <summary>Gửi lại cùng EventId không được cộng hai lần (R-024).</summary>
    [Fact]
    public void Delta_ignores_duplicate_event_ids()
    {
        var result = MeasurementAggregationRules.AggregateDelta(
            [Reading("a", 1, 100m), Reading("a", 1, 100m), Reading("b", 2, 200m)]);

        Assert.True(result.IsUsable);
        Assert.Equal(300m, result.TotalKg);
    }

    [Fact]
    public void Delta_with_negative_value_requires_hold()
    {
        var result = MeasurementAggregationRules.AggregateDelta(
            [Reading("a", 1, 100m), Reading("b", 2, -50m)]);

        Assert.False(result.IsUsable);
        Assert.True(result.RequiresHold);
    }

    [Fact]
    public void Delta_with_no_events_is_not_usable()
    {
        var result = MeasurementAggregationRules.AggregateDelta([]);

        Assert.False(result.IsUsable);
        Assert.False(result.RequiresHold);
    }

    // B-011 — chế độ CUMULATIVE

    /// <summary>Lấy chênh giữa mốc cuối và mốc đầu, KHÔNG cộng mọi bản tin bộ đếm.</summary>
    [Fact]
    public void Cumulative_takes_difference_between_last_and_first()
    {
        var result = MeasurementAggregationRules.AggregateCumulative(
        [
            Reading("a", 1, 1_000m, "epoch-1"),
            Reading("b", 2, 1_500m, "epoch-1"),
            Reading("c", 3, 2_200m, "epoch-1")
        ]);

        Assert.True(result.IsUsable);
        Assert.Equal(1_200m, result.TotalKg);
    }

    /// <summary>Reset bộ đếm phải tạm giữ để xác minh, không tính max(0, end-start).</summary>
    [Fact]
    public void Cumulative_with_multiple_epochs_requires_hold()
    {
        var result = MeasurementAggregationRules.AggregateCumulative(
        [
            Reading("a", 1, 5_000m, "epoch-1"),
            Reading("b", 2, 100m, "epoch-2")
        ]);

        Assert.False(result.IsUsable);
        Assert.True(result.RequiresHold);
        Assert.Contains("reset", result.HoldReason);
    }

    [Fact]
    public void Cumulative_missing_epoch_requires_hold()
    {
        var result = MeasurementAggregationRules.AggregateCumulative(
            [Reading("a", 1, 1_000m), Reading("b", 2, 2_000m)]);

        Assert.False(result.IsUsable);
        Assert.True(result.RequiresHold);
    }

    /// <summary>Bộ đếm cộng dồn giảm trong cùng chu kỳ là bất thường.</summary>
    [Fact]
    public void Cumulative_decreasing_within_epoch_requires_hold()
    {
        var result = MeasurementAggregationRules.AggregateCumulative(
        [
            Reading("a", 1, 2_000m, "epoch-1"),
            Reading("b", 2, 1_500m, "epoch-1")
        ]);

        Assert.False(result.IsUsable);
        Assert.True(result.RequiresHold);
    }

    // B-012 — đếm bao

    [Fact]
    public void Bag_count_uses_forward_passages()
    {
        var passages = Enumerable.Range(0, 600)
            .Select(i => ($"p-{i}", true))
            .ToList();

        var result = MeasurementAggregationRules.AggregateBagPassages(passages, false);

        Assert.True(result.IsUsable);
        Assert.Equal(600, result.NetCount);
    }

    /// <summary>net = forward - reverse chỉ khi chiều trả lại đã nghiệm thu.</summary>
    [Fact]
    public void Bag_count_subtracts_reverse_when_accepted()
    {
        var result = MeasurementAggregationRules.AggregateBagPassages(
            [("p-1", true), ("p-2", true), ("p-3", true), ("p-4", false)],
            reverseDirectionAccepted: true);

        Assert.True(result.IsUsable);
        Assert.Equal(3, result.ForwardCount);
        Assert.Equal(1, result.ReverseCount);
        Assert.Equal(2, result.NetCount);
    }

    [Fact]
    public void Reverse_passage_without_accepted_config_is_not_usable()
    {
        var result = MeasurementAggregationRules.AggregateBagPassages(
            [("p-1", true), ("p-2", false)], reverseDirectionAccepted: false);

        Assert.False(result.IsUsable);
        Assert.Null(result.NetCount);
    }

    /// <summary>Gửi lại cùng PassageId không được đếm trùng (B-013).</summary>
    [Fact]
    public void Duplicate_passage_ids_are_counted_once()
    {
        var result = MeasurementAggregationRules.AggregateBagPassages(
            [("p-1", true), ("p-1", true), ("p-2", true)], false);

        Assert.Equal(2, result.NetCount);
    }

    [Fact]
    public void More_reverse_than_forward_requires_physical_recount()
    {
        var result = MeasurementAggregationRules.AggregateBagPassages(
            [("p-1", true), ("p-2", false), ("p-3", false)], reverseDirectionAccepted: true);

        Assert.False(result.IsUsable);
        Assert.Contains("kiểm đếm thực tế", result.HoldReason);
    }

    // B-013 — phát hiện thiếu dữ liệu

    [Fact]
    public void Detects_gap_in_sequence_range()
    {
        var hasGap = MeasurementAggregationRules.HasSequenceGap([1, 2, 3, 7, 8], out var firstMissing);

        Assert.True(hasGap);
        Assert.Equal(4, firstMissing);
    }

    [Fact]
    public void Continuous_sequence_has_no_gap()
    {
        var hasGap = MeasurementAggregationRules.HasSequenceGap([5, 6, 7, 8], out var firstMissing);

        Assert.False(hasGap);
        Assert.Null(firstMissing);
    }

    /// <summary>
    /// B-011: IoT lấy số bao nhân quy cách không phải nguồn cân kg độc lập,
    /// nên không được coi là hai phép đo xác nhận lẫn nhau.
    /// </summary>
    [Theory]
    [InlineData(MassOrigin.Measured, true)]
    [InlineData(MassOrigin.DerivedFromCount, false)]
    public void Only_measured_mass_is_an_independent_source(MassOrigin origin, bool expected)
    {
        Assert.Equal(expected, MeasurementAggregationRules.IsIndependentMassSource(origin));
    }
}
