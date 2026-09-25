using Microsoft.EntityFrameworkCore;
using TTC.Weighing.Application.Queries;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Infrastructure.Persistence;

namespace TTC.Weighing.Infrastructure.Queries;

/// <summary>
/// EF implementation cho ITripRegistrationQueries — lưới đăng tài UI-02.
/// Tab InFactory/Exited/Cancelled lọc theo Lifecycle, khoảng ngày [from, to) trên CreatedAt.
/// Req 7.1, 7.2, 7.3.
/// </summary>
public sealed class EfTripRegistrationQueries(WeighingDbContext db) : ITripRegistrationQueries
{
    /// <summary>Lifecycle thuộc tab "Trong nhà máy": xe đã vào chưa ra.</summary>
    private static readonly TripLifecycle[] InFactoryLifecycles =
    [
        TripLifecycle.TempRegistered,
        TripLifecycle.Registered,
        TripLifecycle.InProgress,
        TripLifecycle.WeighingCompleted,
        TripLifecycle.Reconciled,
        TripLifecycle.Posted
    ];

    public async Task<IReadOnlyList<TripRegistrationRow>> ListAsync(
        Guid siteId, TripRegistrationFilter filter, CancellationToken ct = default)
    {
        var query = db.Trips
            .AsNoTracking()
            .Where(t => t.SiteId == siteId && t.DeletedAt == null)
            .FilterByOrgUnit();

        // Lọc theo tab (Req 7.2)
        query = filter.Tab switch
        {
            TripGridTab.InFactory => query.Where(t => InFactoryLifecycles.Contains(t.Lifecycle)),
            TripGridTab.Exited => query.Where(t => t.Lifecycle == TripLifecycle.Closed),
            TripGridTab.Cancelled => query.Where(t => t.Lifecycle == TripLifecycle.Cancelled),
            _ => query.Where(t => InFactoryLifecycles.Contains(t.Lifecycle))
        };

        // Lọc theo khoảng ngày [from, to) trên CreatedAt (Req 7.3)
        if (filter.FromUtc.HasValue)
            query = query.Where(t => t.CreatedAt >= filter.FromUtc.Value);

        if (filter.ToUtc.HasValue)
            query = query.Where(t => t.CreatedAt < filter.ToUtc.Value);

        // Project sang anonymous type rồi map sang TripRegistrationRow
        // (EF expression tree không hỗ trợ named arguments trong constructor của record)
        var rows = await query
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new
            {
                t.Id,
                t.Version,
                t.TripNo,
                ShipmentNo = t.ReferenceTripNo,
                Plate = t.TractorPlate,
                t.QueueSequenceNumber,
                t.DriverRef,
                EntryTime = db.CheckpointEvents
                    .Where(c => c.TripId == t.Id && c.Type == CheckpointType.GateIn)
                    .OrderBy(c => c.StartedAt ?? c.CreatedAt)
                    .Select(c => (DateTimeOffset?)(c.CompletedAt ?? c.StartedAt))
                    .FirstOrDefault()
                    ?? t.CreatedAt, // Fallback: thời điểm đăng tài
                ExitTime = db.CheckpointEvents
                    .Where(c => c.TripId == t.Id && c.Type == CheckpointType.GateOut)
                    .OrderBy(c => c.StartedAt ?? c.CreatedAt)
                    .Select(c => (DateTimeOffset?)(c.CompletedAt ?? c.StartedAt))
                    .FirstOrDefault(),
                Transporter = db.TripDocumentLines
                    .Where(dl => dl.TripId == t.Id)
                    .Join(db.Set<Domain.Entities.ExternalDocument>(),
                        dl => dl.DocumentId, doc => doc.Id,
                        (dl, doc) => doc.OrganizationCode)
                    .FirstOrDefault()
                    ?? t.SugarcaneOwner, // Fallback: đối tác vận chuyển từ FRM
                CustomerOrSupplier = t.SugarcaneOwner,
                Commodity = t.SugarcaneType ?? db.WorkflowProfiles
                    .Where(p => p.Id == t.WorkflowProfileId)
                    .Select(p => p.SourceSystem == "FRM" ? "Mía nhập" : "Vật tư")
                    .FirstOrDefault(),
                t.Lifecycle,
                t.VehicleType
            })
            .ToListAsync(ct);

        return rows
            .Select(r => new TripRegistrationRow(
                r.Id,
                r.Version,
                r.TripNo,
                r.ShipmentNo,
                r.Plate,
                r.QueueSequenceNumber,
                r.DriverRef,
                r.EntryTime,
                r.ExitTime,
                r.Transporter,
                r.CustomerOrSupplier,
                r.Commodity,
                r.Lifecycle,
                r.VehicleType?.ToString()))
            .ToList();
    }
}
