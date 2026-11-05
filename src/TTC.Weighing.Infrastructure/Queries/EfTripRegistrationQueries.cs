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
public sealed class EfTripRegistrationQueries(
    WeighingDbContext db,
    TTC.Weighing.Infrastructure.Adapters.FrmMasterDataCache frmCache) : ITripRegistrationQueries
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

        // Tách Mía (FRM) / Vật tư (ERP)
        if (filter.Category.ToSourceSystem() is { } sourceSystem)
            query = query.Where(t => t.SourceSystem == sourceSystem);

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
                // Giờ ra: qua cổng ra (nếu có) — chưa có thiết bị cổng thì lấy giờ cân ra (lượt cân phân đoạn ≥ 1).
                ExitTime = db.CheckpointEvents
                    .Where(c => c.TripId == t.Id && c.Type == CheckpointType.GateOut)
                    .OrderBy(c => c.StartedAt ?? c.CreatedAt)
                    .Select(c => (DateTimeOffset?)(c.CompletedAt ?? c.StartedAt))
                    .FirstOrDefault()
                    ?? db.WeighSessions
                        .Where(s => s.TripId == t.Id && s.SegmentIndex >= 1)
                        .SelectMany(s => s.Events)
                        .Where(e => e.IsEffective)
                        .Max(e => (DateTimeOffset?)e.MeasuredAt),
                t.FrmMaLenhDon,
                Transporter = db.TripDocumentLines
                    .Where(dl => dl.TripId == t.Id)
                    .Join(db.Set<Domain.Entities.ExternalDocument>(),
                        dl => dl.DocumentId, doc => doc.Id,
                        (dl, doc) => doc.OrganizationCode)
                    .FirstOrDefault()
                    ?? t.DriverRef, // Nhà vận tải — fallback DriverRef (nhập tay) thay vì chủ mía
                CustomerOrSupplier = t.SugarcaneOwner,
                Commodity = t.SugarcaneType ?? db.WorkflowProfiles
                    .Where(p => p.Id == t.WorkflowProfileId)
                    .Select(p => p.SourceSystem == "FRM" ? "Mía nhập" : "Vật tư")
                    .FirstOrDefault(),
                t.Lifecycle,
                t.VehicleType,
                t.SourceSystem
            })
            .ToListAsync(ct);

        // Pre-fetch weigh events and quality samples for process status
        var tripIds = rows.Select(r => r.Id).ToHashSet();

        var weighEvents = await db.WeighEvents
            .Where(e => tripIds.Contains(e.TripId) && e.IsEffective)
            .Select(e => new { e.TripId, e.Role })
            .ToListAsync(ct);

        var qualitySamples = await db.QualitySamples
            .Where(s => tripIds.Contains(s.TripId))
            .Select(s => new { s.TripId, s.Type })
            .ToListAsync(ct);

        var syncStatuses = await db.IntegrationMessages
            .Where(m => tripIds.Contains(m.TripId))
            .GroupBy(m => m.TripId)
            .Select(g => new { TripId = g.Key, HasAck = g.Any(m => m.Status == Domain.Enums.IntegrationMessageStatus.Acknowledged) })
            .ToListAsync(ct);

        var weighByTrip = weighEvents.ToLookup(e => e.TripId);
        var qualityByTrip = qualitySamples.ToLookup(s => s.TripId);
        var syncByTrip = syncStatuses.ToDictionary(s => s.TripId, s => s.HasAck);

        return rows
            .Select(r =>
            {
                var hasGross = weighByTrip[r.Id].Any(e => e.Role == Domain.Enums.WeightRole.Gross);
                var hasTare = weighByTrip[r.Id].Any(e => e.Role == Domain.Enums.WeightRole.Tare);
                var hasImpurity = qualityByTrip[r.Id].Any(s => s.Type == Domain.Enums.QualitySampleType.Impurity);
                var hasCcs = qualityByTrip[r.Id].Any(s => s.Type == Domain.Enums.QualitySampleType.Ccs);
                var hasSynced = syncByTrip.TryGetValue(r.Id, out var ack) && ack;

                // Không hiện tên trung gian DigiScale: mía → FRM, vật tư → ERP.
                var status = hasSynced ? (r.SourceSystem == "ERP" ? "📤 Đã đẩy lên ERP" : "📤 Đã đẩy lên FRM")
                    : hasTare && hasGross ? "✅ Đã cân đủ"
                    : hasCcs ? "🧪 Đã đo CCS"
                    : hasImpurity ? "🔬 Đã đo tạp chất"
                    : hasGross ? "⚖️ Đã cân lần 1"
                    : r.Lifecycle == Domain.Enums.TripLifecycle.InProgress ? "⚖️ Đang cân"
                    : "📋 Đã đăng tài";

                // Mía: lấy theo lệnh đốn FRM đã kéo về (dữ liệu gốc tiếng Việt).
                var ld = frmCache.FindLenhDonByCode(r.FrmMaLenhDon ?? r.ShipmentNo);
                // Khách hàng/NCC: chủ mía; trống thì khách hàng doanh nghiệp của lệnh đốn.
                var customer = !string.IsNullOrWhiteSpace(r.CustomerOrSupplier) ? r.CustomerOrSupplier
                    : !string.IsNullOrWhiteSpace(ld?.KHName) ? ld!.KHName
                    : ld?.KHDNName;
                // Loại xe: tên FRM ("Xe Tải"...); không có thì mã enum để form dịch theo ngôn ngữ.
                var vehicleType = !string.IsNullOrWhiteSpace(ld?.LoaixeName) ? ld!.LoaixeName : r.VehicleType?.ToString();

                return new TripRegistrationRow(
                    r.Id, r.Version, r.TripNo, r.ShipmentNo, r.Plate,
                    r.QueueSequenceNumber, r.DriverRef, r.EntryTime, r.ExitTime,
                    r.Transporter, customer, r.Commodity,
                    r.Lifecycle, vehicleType, status);
            })
            .ToList();
    }
}
