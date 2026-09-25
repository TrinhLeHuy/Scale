using Microsoft.EntityFrameworkCore;
using TTC.Weighing.Application.Queries;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Infrastructure.Persistence;

namespace TTC.Weighing.Infrastructure.Queries;

/// <summary>
/// EF implementation cho IWeighOperationQueries — lưới xe đã cân trên WeighOperationForm (UI-03).
/// Join trips → weigh_sessions → weigh_events (Gross/Tare) → integration_messages (SyncStatus).
/// Req 1.2, 4.6, 8.6.
/// </summary>
public sealed class EfWeighOperationQueries(WeighingDbContext db) : IWeighOperationQueries
{
    public async Task<IReadOnlyList<WeighedTripRow>> ListAsync(
        Guid siteId, Guid weighbridgeId, WeighedTripFilter filter,
        CancellationToken ct = default)
    {
        // Lấy tripId có phiên cân tại bàn cân này
        var tripIdsAtBridge = db.WeighSessions
            .Where(ws => ws.WeighbridgeId == weighbridgeId)
            .Select(ws => ws.TripId)
            .Distinct();

        var query = db.Trips
            .AsNoTracking()
            .Where(t => t.SiteId == siteId && t.DeletedAt == null)
            .FilterByOrgUnit()
            .Where(t => tripIdsAtBridge.Contains(t.Id));

        // Lọc theo khoảng ngày [from, to) trên CreatedAt
        if (filter.FromUtc.HasValue)
            query = query.Where(t => t.CreatedAt >= filter.FromUtc.Value);

        if (filter.ToUtc.HasValue)
            query = query.Where(t => t.CreatedAt < filter.ToUtc.Value);

        // Chỉ lấy chuyến đã có ít nhất một lượt cân hiệu lực
        query = query.Where(t =>
            db.WeighEvents.Any(e => e.TripId == t.Id && e.IsEffective));

        var rows = await query
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new
            {
                t.TripNo,
                t.TicketNumber,
                t.ReferenceTripNo,
                Plate = t.TractorPlate,
                Trailer = t.TrailerPlate,
                Gross = db.WeighEvents
                    .Where(e => e.TripId == t.Id
                        && e.Role == WeightRole.Gross
                        && e.IsEffective)
                    .Select(e => new { e.NormalizedKg, e.MeasuredAt })
                    .FirstOrDefault(),
                Tare = db.WeighEvents
                    .Where(e => e.TripId == t.Id
                        && e.Role == WeightRole.Tare
                        && e.IsEffective)
                    .Select(e => new { e.NormalizedKg, e.MeasuredAt })
                    .FirstOrDefault(),
                // SyncStatus: lấy trạng thái mới nhất từ integration_messages
                SyncStatus = db.IntegrationMessages
                    .Where(m => m.TripId == t.Id)
                    .OrderByDescending(m => m.CreatedAt)
                    .Select(m => (IntegrationMessageStatus?)m.Status)
                    .FirstOrDefault(),
                // Customer: chủ mía hoặc tổ chức từ chứng từ nguồn
                Customer = t.SugarcaneOwner ?? db.TripDocumentLines
                    .Where(dl => dl.TripId == t.Id)
                    .Join(db.Set<Domain.Entities.ExternalDocument>(),
                        dl => dl.DocumentId, doc => doc.Id,
                        (dl, doc) => doc.OrganizationCode)
                    .FirstOrDefault(),
                // Commodity: loại mía hoặc mã profile
                Commodity = t.SugarcaneType ?? db.WorkflowProfiles
                    .Where(p => p.Id == t.WorkflowProfileId)
                    .Select(p => p.SourceSystem == "FRM" ? "Mía nhập" : "Vật tư")
                    .FirstOrDefault(),
                // ShipmentNo từ reference hoặc chứng từ
                ShipmentNo = t.ReferenceTripNo ?? db.TripDocumentLines
                    .Where(dl => dl.TripId == t.Id)
                    .Join(db.Set<Domain.Entities.ExternalDocument>(),
                        dl => dl.DocumentId, doc => doc.Id,
                        (dl, doc) => doc.ShipmentNo)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var result = new List<WeighedTripRow>(rows.Count);

        foreach (var r in rows)
        {
            var hasGross = r.Gross is not null;
            var hasTare = r.Tare is not null;

            // FirstWeightOnly: chỉ có Gross chưa có Tare
            if (filter.FirstWeightOnly && (!hasGross || hasTare))
                continue;

            // CompletedOnly: phải có cả Gross và Tare
            if (filter.CompletedOnly && (!hasGross || !hasTare))
                continue;

            // Xác định cân vào/ra dựa theo thời gian, không suy từ vai trò
            var first = r.Gross is null ? r.Tare
                : r.Tare is null ? r.Gross
                : r.Gross.MeasuredAt <= r.Tare.MeasuredAt ? r.Gross : r.Tare;

            var second = r.Gross is null || r.Tare is null ? null
                : ReferenceEquals(first, r.Gross) ? r.Tare : r.Gross;

            decimal? net = null;
            if (hasGross && hasTare)
                net = r.Gross!.NormalizedKg - r.Tare!.NormalizedKg;

            result.Add(new WeighedTripRow(
                RegNo: r.TripNo,
                TicketNo: r.TicketNumber,
                TripNumber: r.ReferenceTripNo,
                ShipmentNo: r.ShipmentNo,
                Plate: r.Plate,
                Trailer: r.Trailer,
                TimeIn: first?.MeasuredAt,
                WeightIn: first?.NormalizedKg,
                TimeOut: second?.MeasuredAt,
                WeightOut: second?.NormalizedKg,
                NetWeight: net,
                Customer: r.Customer,
                Commodity: r.Commodity,
                SyncStatus: r.SyncStatus));
        }

        return result;
    }

    public async Task<IReadOnlyList<WeighedTripRow>> ListPendingAsync(
        Guid siteId, CancellationToken ct = default)
    {
        var rows = await db.Trips
            .AsNoTracking()
            .Where(t => t.SiteId == siteId && t.DeletedAt == null)
            .FilterByOrgUnit()
            .Where(t => t.Lifecycle == TripLifecycle.TempRegistered
                || t.Lifecycle == TripLifecycle.Registered
                || t.Lifecycle == TripLifecycle.InProgress)
            // Chưa có WeighEvent nào
            .Where(t => !db.WeighEvents.Any(e => e.TripId == t.Id))
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new WeighedTripRow(
                t.TripNo,
                null,
                t.ReferenceTripNo,
                t.ReferenceTripNo,
                t.TractorPlate,
                t.TrailerPlate,
                null,
                null,
                null,
                null,
                null,
                t.SugarcaneOwner,
                t.SugarcaneType,
                null))
            .ToListAsync(ct);

        return rows;
    }
}
