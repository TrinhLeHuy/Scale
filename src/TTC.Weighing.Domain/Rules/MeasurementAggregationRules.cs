using TTC.Weighing.Domain.Common;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Domain.Rules;

/// <summary>Một số đo IoT đã chuẩn hóa, dùng làm đầu vào tổng hợp phiên.</summary>
public sealed record IotReading(
    string EventId,
    long Sequence,
    decimal ValueKg,
    string? CounterEpoch,
    DateTimeOffset OccurredAt);

/// <param name="RequiresHold">Yêu cầu tạm giữ phiên để xác minh (B-011).</param>
public sealed record IotAggregateResult(
    bool IsUsable,
    decimal? TotalKg,
    bool RequiresHold,
    string? HoldReason);

public sealed record BagCountResult(
    bool IsUsable,
    int ForwardCount,
    int ReverseCount,
    int? NetCount,
    string? HoldReason);

/// <summary>
/// B-011 / B-012 — tổng hợp số đo IoT và đếm bao.
/// Không cộng mọi bản tin bộ đếm; không dùng max(0, end-start) để bỏ qua lượng thiếu.
/// </summary>
public static class MeasurementAggregationRules
{
    /// <summary>
    /// DELTA: cộng các sự kiện duy nhất. Trùng EventId bị loại, không cộng hai lần.
    /// </summary>
    public static IotAggregateResult AggregateDelta(IEnumerable<IotReading> readings)
    {
        var distinct = readings
            .GroupBy(r => r.EventId)
            .Select(g => g.First())
            .ToList();

        if (distinct.Count == 0)
            return new(false, null, false, DomainText.T("rule.iot_no_events", "Chưa có sự kiện IoT nào trong phiên."));

        var negative = distinct.FirstOrDefault(r => r.ValueKg < 0);
        if (negative is not null)
            return new(false, null, true,
                DomainText.T("rule.iot_delta_negative", "Sự kiện DELTA có giá trị âm ({0} kg) — cần xác minh, không tự bỏ qua.", negative.ValueKg));

        return new(true, distinct.Sum(r => r.ValueKg), false, null);
    }

    /// <summary>
    /// CUMULATIVE: lấy chênh giữa mốc cuối và mốc đầu của CÙNG counter_epoch.
    /// Reset hoặc giảm bộ đếm không rõ nguyên nhân phải tạm giữ để xác minh.
    /// </summary>
    public static IotAggregateResult AggregateCumulative(IEnumerable<IotReading> readings)
    {
        var ordered = readings
            .GroupBy(r => r.EventId)
            .Select(g => g.First())
            .OrderBy(r => r.Sequence)
            .ToList();

        if (ordered.Count == 0)
            return new(false, null, false, DomainText.T("rule.iot_no_events", "Chưa có sự kiện IoT nào trong phiên."));

        var epochs = ordered
            .Select(r => r.CounterEpoch)
            .Distinct()
            .ToList();

        if (epochs.Any(string.IsNullOrWhiteSpace))
            return new(false, null, true,
                DomainText.T("rule.iot_cumulative_no_epoch", "Sự kiện CUMULATIVE thiếu counter_epoch — không xác định được chu kỳ bộ đếm."));

        if (epochs.Count > 1)
            return new(false, null, true,
                DomainText.T("rule.iot_multiple_epochs", "Phiên chứa {0} chu kỳ bộ đếm khác nhau — bộ đếm đã reset, cần xác minh lượng thiếu.", epochs.Count));

        // Bộ đếm cộng dồn phải không giảm trong cùng chu kỳ.
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].ValueKg < ordered[i - 1].ValueKg)
                return new(false, null, true,
                    DomainText.T("rule.iot_counter_decreased",
                        "Bộ đếm giảm từ {0} xuống {1} kg trong cùng chu kỳ — cần xác minh, không tính max(0, end-start).",
                        ordered[i - 1].ValueKg, ordered[i].ValueKg));
        }

        var total = ordered[^1].ValueKg - ordered[0].ValueKg;
        return new(true, total, false, null);
    }

    /// <summary>
    /// B-012 — Đếm bao theo lần vượt vạch hợp lệ, không theo số khung hình.
    /// net = forward - reverse chỉ khi băng chuyền hai chiều đã nghiệm thu.
    /// </summary>
    /// <param name="reverseDirectionAccepted">
    /// Chiều trả lại đã được nghiệm thu. Chưa nghiệm thu thì có sự kiện REVERSE là bất thường.
    /// </param>
    public static BagCountResult AggregateBagPassages(
        IEnumerable<(string PassageId, bool IsForward)> passages,
        bool reverseDirectionAccepted)
    {
        var distinct = passages
            .GroupBy(p => p.PassageId)
            .Select(g => g.First())
            .ToList();

        var forward = distinct.Count(p => p.IsForward);
        var reverse = distinct.Count(p => !p.IsForward);

        if (reverse > 0 && !reverseDirectionAccepted)
            return new(false, forward, reverse, null,
                DomainText.T("rule.bag_reverse_not_accepted", "Có sự kiện chiều trả lại nhưng cấu hình chưa nghiệm thu chiều này — cần xác minh."));

        var net = forward - reverse;

        if (net < 0)
            return new(false, forward, reverse, null,
                DomainText.T("rule.bag_reverse_exceeds", "Số bao trả lại ({0}) lớn hơn số bao đi ({1}) — cần kiểm đếm thực tế.", reverse, forward));

        return new(true, forward, reverse, net, null);
    }

    /// <summary>
    /// Kiểm tra dải sequence khi đóng phiên để phát hiện thiếu dữ liệu (B-013).
    /// </summary>
    public static bool HasSequenceGap(IEnumerable<long> sequences, out long? firstMissing)
    {
        firstMissing = null;

        var ordered = sequences.Distinct().OrderBy(s => s).ToList();
        if (ordered.Count <= 1) return false;

        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i] != ordered[i - 1] + 1)
            {
                firstMissing = ordered[i - 1] + 1;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// IoT chỉ lấy số bao nhân quy cách thì KHÔNG phải nguồn cân kg độc lập (B-011).
    /// </summary>
    public static bool IsIndependentMassSource(MassOrigin origin) => origin == MassOrigin.Measured;
}
