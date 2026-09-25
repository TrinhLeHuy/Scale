using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Domain.Rules;

/// <param name="Ratio">Tỷ lệ đã duyệt, dùng với ApprovedRatio hoặc OrderWeightRatio.</param>
/// <param name="FixedKg">Số lượng xác định, dùng với FixedQuantityWithRemainder.</param>
/// <param name="ReceivesRemainder">Dòng nhận phần còn lại. Chỉ một dòng được đánh dấu.</param>
public sealed record AllocationRequestLine(
    Guid? DocumentLineId,
    string? OwnerRef,
    decimal? Ratio,
    decimal? FixedKg,
    bool ReceivesRemainder);

public sealed record AllocationLineResult(
    Guid? DocumentLineId,
    string? OwnerRef,
    decimal AllocatedKg,
    decimal RoundingAdjustmentKg);

public sealed record AllocationResult(
    bool IsValid,
    IReadOnlyList<AllocationLineResult> Lines,
    string? InvalidReason)
{
    public decimal TotalAllocatedKg => Lines.Sum(l => l.AllocatedKg);
}

/// <summary>
/// R-016 / R-021 — Phân bổ tổng lượng đã đo cho từng chủ mía hoặc Shipment.
/// Tổng phân bổ phải bảo toàn tổng thực tế; phần dư làm tròn đi vào một dòng điều chỉnh minh bạch.
/// Đây là phân bổ, KHÔNG phải từng dòng đã đo riêng.
/// </summary>
public static class AllocationRules
{
    /// <param name="roundingDecimals">Độ phân giải làm tròn theo chính sách đã duyệt.</param>
    public static AllocationResult Allocate(
        decimal measuredTotalKg,
        AllocationMethod method,
        IReadOnlyList<AllocationRequestLine> lines,
        int roundingDecimals = 3)
    {
        if (lines.Count == 0)
            return Invalid("Không có dòng nào để phân bổ.");

        if (measuredTotalKg <= 0)
            return Invalid("Tổng lượng đã đo phải lớn hơn 0 để phân bổ.");

        return method switch
        {
            AllocationMethod.ApprovedRatio or AllocationMethod.OrderWeightRatio
                => AllocateByRatio(measuredTotalKg, lines, roundingDecimals),

            AllocationMethod.FixedQuantityWithRemainder
                => AllocateByFixedQuantity(measuredTotalKg, lines, roundingDecimals),

            _ => Invalid($"Phương pháp phân bổ không hỗ trợ: {method}")
        };
    }

    private static AllocationResult AllocateByRatio(
        decimal total, IReadOnlyList<AllocationRequestLine> lines, int decimals)
    {
        if (lines.Any(l => l.Ratio is null or <= 0))
            return Invalid("Mọi dòng phải có tỷ lệ đã duyệt lớn hơn 0.");

        var ratioSum = lines.Sum(l => l.Ratio!.Value);
        if (ratioSum <= 0)
            return Invalid("Tổng tỷ lệ phải lớn hơn 0.");

        var results = new List<AllocationLineResult>();
        var running = 0m;

        // Dòng cuối nhận phần dư để bảo toàn tổng, ghi rõ là điều chỉnh làm tròn.
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var isLast = index == lines.Count - 1;

            if (isLast)
            {
                var remainder = total - running;
                var theoretical = Math.Round(total * line.Ratio!.Value / ratioSum, decimals);
                results.Add(new(line.DocumentLineId, line.OwnerRef, remainder, remainder - theoretical));
            }
            else
            {
                var allocated = Math.Round(total * line.Ratio!.Value / ratioSum, decimals);
                running += allocated;
                results.Add(new(line.DocumentLineId, line.OwnerRef, allocated, 0m));
            }
        }

        return Validate(total, results);
    }

    private static AllocationResult AllocateByFixedQuantity(
        decimal total, IReadOnlyList<AllocationRequestLine> lines, int decimals)
    {
        var remainderLines = lines.Where(l => l.ReceivesRemainder).ToList();
        if (remainderLines.Count != 1)
            return Invalid("Phải có đúng một dòng nhận phần còn lại.");

        var fixedLines = lines.Where(l => !l.ReceivesRemainder).ToList();
        if (fixedLines.Any(l => l.FixedKg is null or <= 0))
            return Invalid("Mọi dòng số lượng xác định phải có giá trị lớn hơn 0.");

        var fixedTotal = fixedLines.Sum(l => Math.Round(l.FixedKg!.Value, decimals));
        if (fixedTotal > total)
            return Invalid(
                $"Tổng số lượng xác định ({fixedTotal:N3} kg) vượt tổng đã đo ({total:N3} kg).");

        var results = fixedLines
            .Select(l => new AllocationLineResult(
                l.DocumentLineId, l.OwnerRef, Math.Round(l.FixedKg!.Value, decimals), 0m))
            .ToList();

        var remainderLine = remainderLines[0];
        results.Add(new(remainderLine.DocumentLineId, remainderLine.OwnerRef, total - fixedTotal, 0m));

        return Validate(total, results);
    }

    private static AllocationResult Validate(decimal total, List<AllocationLineResult> results)
    {
        var sum = results.Sum(r => r.AllocatedKg);

        if (sum != total)
            return Invalid(
                $"Tổng phân bổ {sum:N3} kg không bằng tổng đã đo {total:N3} kg — phần dư không được biến mất.");

        if (results.Any(r => r.AllocatedKg < 0))
            return Invalid("Có dòng phân bổ âm.");

        return new(true, results, null);
    }

    private static AllocationResult Invalid(string reason) => new(false, [], reason);
}
