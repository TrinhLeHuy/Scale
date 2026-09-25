using Microsoft.EntityFrameworkCore;
using TTC.Weighing.Application.Queries;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Infrastructure.Persistence;

namespace TTC.Weighing.Infrastructure.Queries;

/// <summary>
/// EF implementation cho IWarehouseQueries — danh sách xe trong kho và xe đã ra kho (UI-04).
/// Join trips → checkpoint_events (WarehouseIn/WarehouseOut) → measurement_sessions.
/// Req 6.5.
/// </summary>
public sealed class EfWarehouseQueries(WeighingDbContext db) : IWarehouseQueries
{
    public async Task<IReadOnlyList<WarehouseVehicleRow>> ListAsync(
        Guid warehouseId, WarehouseFilter filter, CancellationToken ct = default)
    {
        // Lấy checkpoint WarehouseIn tại kho này — mỗi chuyến có thể ghé kho nhiều lần (R-012)
        var entryQuery = db.CheckpointEvents
            .AsNoTracking()
            .Where(c => c.Type == CheckpointType.WarehouseIn
                && c.WarehouseId == warehouseId);

        // Lọc theo khoảng ngày [from, to) trên StartedAt của checkpoint vào kho
        if (filter.FromUtc.HasValue)
            entryQuery = entryQuery.Where(c => (c.StartedAt ?? c.CreatedAt) >= filter.FromUtc.Value);

        if (filter.ToUtc.HasValue)
            entryQuery = entryQuery.Where(c => (c.StartedAt ?? c.CreatedAt) < filter.ToUtc.Value);

        var rows = await entryQuery
            .Join(db.Trips.Where(t => t.DeletedAt == null).FilterByOrgUnit(),
                c => c.TripId, t => t.Id,
                (entry, trip) => new
                {
                    trip.Id,
                    trip.TripNo,
                    Plate = trip.TractorPlate,
                    // Commodity: loại mía hoặc mã profile
                    Commodity = trip.SugarcaneType ?? db.WorkflowProfiles
                        .Where(p => p.Id == trip.WorkflowProfileId)
                        .Select(p => p.Code)
                        .FirstOrDefault(),
                    EntryTime = entry.StartedAt ?? entry.CreatedAt,
                    EntryStatus = entry.Status,
                    EntryWaiverReason = entry.WaiverReason,
                    // Checkpoint ra kho tại cùng kho, hoàn thành (Status = Done/Waived)
                    Exit = db.CheckpointEvents
                        .Where(c => c.TripId == trip.Id
                            && c.Type == CheckpointType.WarehouseOut
                            && c.WarehouseId == warehouseId)
                        .OrderByDescending(c => c.StartedAt ?? c.CreatedAt)
                        .Select(c => new
                        {
                            CompletedAt = c.CompletedAt ?? c.StartedAt,
                            c.Status,
                            c.WaiverReason
                        })
                        .FirstOrDefault(),
                    // Phiên đo mở tại kho này
                    HasOpenMeasurementSession = db.MeasurementSessions
                        .Any(m => m.TripId == trip.Id
                            && m.WarehouseId == warehouseId
                            && m.Status == MeasurementSessionStatus.Open)
                })
            .OrderByDescending(r => r.EntryTime)
            .ToListAsync(ct);

        var result = new List<WarehouseVehicleRow>(rows.Count);

        foreach (var r in rows)
        {
            var exitCompleted = r.Exit is not null
                && r.Exit.Status is CheckpointStatus.Done or CheckpointStatus.Waived;

            // InsideWarehouseOnly: có WarehouseIn nhưng chưa có WarehouseOut hoàn thành
            if (filter.InsideWarehouseOnly && exitCompleted)
                continue;

            // Trạng thái tổng hợp: ưu tiên checkpoint ra nếu có, nếu không lấy checkpoint vào
            var status = r.Exit is not null ? r.Exit.Status : r.EntryStatus;
            var waiverReason = r.Exit is not null ? r.Exit.WaiverReason : r.EntryWaiverReason;

            result.Add(new WarehouseVehicleRow(
                TripId: r.Id,
                TripNo: r.TripNo,
                Plate: r.Plate,
                Commodity: r.Commodity,
                EntryTime: r.EntryTime,
                ExitTime: r.Exit?.CompletedAt,
                Status: status,
                WaiverReason: waiverReason,
                HasOpenMeasurementSession: r.HasOpenMeasurementSession));
        }

        return result;
    }
}
