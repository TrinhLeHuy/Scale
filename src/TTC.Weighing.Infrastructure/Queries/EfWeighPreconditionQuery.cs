using Microsoft.EntityFrameworkCore;
using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Infrastructure.Persistence;

namespace TTC.Weighing.Infrastructure.Queries;

/// <summary>
/// B-001.7 — Điều kiện nghiệp vụ trước một lượt cân, đọc từ dữ liệu thật.
/// Thiếu tạp chất CHẶN cân lần 2 (S02 tr.21); thiếu chữ đường chỉ CẢNH BÁO (R-014).
/// </summary>
public sealed class EfWeighPreconditionQuery(WeighingDbContext db) : IWeighPreconditionQuery
{
    public async Task<WeighPreconditions> EvaluateAsync(
        Guid tripId, WeightRole role, int segmentIndex, CancellationToken ct = default)
    {
        var trip = await db.Trips.FirstOrDefaultAsync(t => t.Id == tripId, ct);

        if (trip is null)
        {
            return new WeighPreconditions
            {
                PrecedingWeightsSatisfied = false,
                ImpuritySatisfied = false,
                WarehouseSatisfied = false,
                PolicySatisfied = false,
                Warnings = ["Không tìm thấy chuyến."]
            };
        }

        var profile = await db.WorkflowProfiles.FirstOrDefaultAsync(
            p => p.Id == trip.WorkflowProfileId
                && p.ProfileVersion == trip.WorkflowProfileVersion, ct);

        // Lượt cân đầu của chuyến là phân đoạn 0; các phân đoạn sau cần lượt trước đã có.
        var isFirstSegment = segmentIndex == 0;

        var effectiveWeightCount = await db.WeighEvents
            .CountAsync(e => e.TripId == tripId && e.IsEffective, ct);

        var precedingSatisfied = isFirstSegment || effectiveWeightCount >= segmentIndex;

        var impurityRequired = profile?.ImpurityRequiredBeforeSecondWeight == true && !isFirstSegment;

        var impurityRecorded = await db.QualitySamples.AnyAsync(
            s => s.TripId == tripId
                && s.Type == QualitySampleType.Impurity
                && s.IsEffective
                && s.Status == QualityResultStatus.Valid, ct);

        var ccs = await db.QualitySamples
            .Where(s => s.TripId == tripId && s.Type == QualitySampleType.Ccs && s.IsEffective)
            .Select(s => new { s.Status })
            .FirstOrDefaultAsync(ct);

        var warnings = new List<string>();

        // Chữ đường chưa có hoặc đang chờ đều chỉ là cảnh báo, không chặn cân.
        var ccsPending = ccs is null || ccs.Status == QualityResultStatus.Pending;

        if (ccsPending && profile?.SourceSystem == "FRM")
            warnings.Add("Chữ đường chưa có kết quả hợp lệ — cảnh báo, không chặn cân (R-014).");

        if (impurityRequired && !impurityRecorded)
            warnings.Add("Chưa đo tạp chất — sẽ chặn lượt cân này (S02 tr.21).");

        // Vật tư (ERP): phải xác nhận vào kho trước cân lần 2
        var warehouseRequired = profile?.SourceSystem == "ERP" && !isFirstSegment;
        var warehouseConfirmed = false;

        if (warehouseRequired)
        {
            warehouseConfirmed = await db.CheckpointEvents.AnyAsync(
                c => c.TripId == tripId && c.Type == CheckpointType.WarehouseIn, ct);

            if (!warehouseConfirmed)
                warnings.Add("Chưa xác nhận vào kho — chặn cân lần 2 cho vật tư.");
        }

        return new WeighPreconditions
        {
            PrecedingWeightsSatisfied = precedingSatisfied,
            ImpuritySatisfied = !impurityRequired || impurityRecorded,
            WarehouseSatisfied = !warehouseRequired || warehouseConfirmed,

            // Chính sách FRM cần hợp đồng API đã xác minh (D-09); chưa có thì không tự chặn cân
            // vì đây là điều kiện riêng, khác với lỗi kết nối.
            PolicySatisfied = true,

            CcsPending = ccsPending,
            Warnings = warnings
        };
    }
}
