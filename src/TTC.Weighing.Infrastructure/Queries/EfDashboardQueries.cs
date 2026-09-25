using Microsoft.EntityFrameworkCore;
using TTC.Weighing.Application.Queries;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Infrastructure.Persistence;

namespace TTC.Weighing.Infrastructure.Queries;

public sealed class EfDashboardQueries(WeighingDbContext db) : IDashboardQueries
{
    public async Task<SiteOverview> GetOverviewAsync(Guid siteId, CancellationToken ct = default)
    {
        var trips = await db.Trips
            .Where(t => t.SiteId == siteId && t.DeletedAt == null)
            .FilterByOrgUnit()
            .Select(t => new
            {
                t.Id,
                t.Lifecycle,
                t.ExecutionStatus,
                HasGross = db.WeighEvents.Any(
                    e => e.TripId == t.Id && e.Role == WeightRole.Gross && e.IsEffective),
                HasTare = db.WeighEvents.Any(
                    e => e.TripId == t.Id && e.Role == WeightRole.Tare && e.IsEffective)
            })
            .ToListAsync(ct);

        var reconciliationBlockers = await db.ReconciliationChecks
            .CountAsync(c =>
                (c.Verdict == ReconciliationVerdict.Mismatch
                 || c.Verdict == ReconciliationVerdict.MissingData)
                && db.Reconciliations.Any(r => r.Id == c.ReconciliationId && r.IsEffective), ct);

        var integrationAttention = await db.IntegrationMessages
            .CountAsync(m =>
                (m.Status == IntegrationMessageStatus.Unknown
                 || m.Status == IntegrationMessageStatus.Failed
                 || m.Status == IntegrationMessageStatus.Held)
                && db.Trips.Any(t => t.Id == m.TripId && t.SiteId == siteId), ct);

        var devices = await db.Devices
            .Where(d => d.SiteId == siteId && d.DeletedAt == null)
            .Select(d => new
            {
                d.Code,
                d.DeviceClass,
                d.LaneId,
                d.IsEnabled,
                LastSeenAt = db.DeviceEvents
                    .Where(e => e.DeviceId == d.Id)
                    .Max(e => (DateTimeOffset?)e.ReceivedAt)
            })
            .ToListAsync(ct);

        var laneCodes = await db.Lanes
            .Where(l => l.SiteId == siteId)
            .ToDictionaryAsync(l => l.Id, l => l.Code, ct);

        // IsOnline để null vì chưa có kênh giám sát thiết bị được nghiệm thu (D-05).
        var deviceRows = devices
            .Select(d => new DeviceHealthRow(
                d.Code,
                d.DeviceClass,
                d.LaneId.HasValue && laneCodes.TryGetValue(d.LaneId.Value, out var code) ? code : "—",
                d.IsEnabled,
                null,
                d.LastSeenAt))
            .ToList();

        var manualInterventions = await db.WeighEvents
            .CountAsync(e => e.Source != WeighSource.Auto
                && db.Trips.Any(t => t.Id == e.TripId && t.SiteId == siteId), ct);

        // Tổng net cân xe chỉ tính chuyến đã có đủ hai lượt cân hiệu lực.
        var netTotals = await db.Trips
            .Where(t => t.SiteId == siteId && t.DeletedAt == null)
            .FilterByOrgUnit()
            .Select(t => new
            {
                Gross = db.WeighEvents
                    .Where(e => e.TripId == t.Id && e.Role == WeightRole.Gross && e.IsEffective)
                    .Select(e => (decimal?)e.NormalizedKg).FirstOrDefault(),
                Tare = db.WeighEvents
                    .Where(e => e.TripId == t.Id && e.Role == WeightRole.Tare && e.IsEffective)
                    .Select(e => (decimal?)e.NormalizedKg).FirstOrDefault()
            })
            .ToListAsync(ct);

        var totalNet = netTotals
            .Where(x => x.Gross.HasValue && x.Tare.HasValue)
            .Sum(x => x.Gross!.Value - x.Tare!.Value);

        var lifecycle = new LifecycleCounts(
            TempRegistered: trips.Count(t => t.Lifecycle == TripLifecycle.TempRegistered),
            Registered: trips.Count(t => t.Lifecycle == TripLifecycle.Registered),
            InProgress: trips.Count(t => t.Lifecycle == TripLifecycle.InProgress),
            WeighingCompleted: trips.Count(t => t.Lifecycle == TripLifecycle.WeighingCompleted),
            Reconciled: trips.Count(t => t.Lifecycle == TripLifecycle.Reconciled),
            Posted: trips.Count(t => t.Lifecycle == TripLifecycle.Posted),
            Closed: trips.Count(t => t.Lifecycle == TripLifecycle.Closed),
            Cancelled: trips.Count(t => t.Lifecycle == TripLifecycle.Cancelled));

        return new SiteOverview(
            WaitingForWeighing: trips.Count(t =>
                t.Lifecycle is TripLifecycle.Registered or TripLifecycle.TempRegistered),
            InsidePlant: trips.Count(t => t.Lifecycle is TripLifecycle.InProgress
                or TripLifecycle.WeighingCompleted or TripLifecycle.Reconciled or TripLifecycle.Posted),
            FirstWeightOnly: trips.Count(t => t.HasGross && !t.HasTare),
            PausedTrips: trips.Count(t => t.ExecutionStatus == ExecutionStatus.Paused),
            ReconciliationBlockers: reconciliationBlockers,
            IntegrationAttention: integrationAttention,
            Lifecycle: lifecycle,
            TotalTripsInRange: trips.Count,
            TotalVehicleNetKgInRange: totalNet == 0 ? null : totalNet,
            ManualInterventions: manualInterventions,
            Devices: deviceRows);
    }

    public async Task<IReadOnlyList<TripBoardRow>> ListTripsAsync(
        Guid siteId, TripBoardFilter filter, CancellationToken ct = default)
    {
        var query = db.Trips
            .Where(t => t.SiteId == siteId && t.DeletedAt == null)
            .FilterByOrgUnit();

        if (filter.FromUtc.HasValue)
            query = query.Where(t => t.CreatedAt >= filter.FromUtc.Value);

        if (filter.ToUtc.HasValue)
            query = query.Where(t => t.CreatedAt < filter.ToUtc.Value);

        if (filter.Lifecycle.HasValue)
            query = query.Where(t => t.Lifecycle == filter.Lifecycle.Value);

        if (filter.ExecutionStatus.HasValue)
            query = query.Where(t => t.ExecutionStatus == filter.ExecutionStatus.Value);

        if (!string.IsNullOrWhiteSpace(filter.PlateContains))
            query = query.Where(t => t.TractorPlate != null
                && EF.Functions.ILike(t.TractorPlate, $"%{filter.PlateContains}%"));

        var rows = await query
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new
            {
                t.Id,
                t.Version,
                t.TripNo,
                t.ReferenceTripNo,
                t.TractorPlate,
                t.CreatedAt,
                t.Lifecycle,
                t.ExecutionStatus,
                t.PauseIssueCode,
                ProfileCode = db.WorkflowProfiles
                    .Where(p => p.Id == t.WorkflowProfileId)
                    .Select(p => p.Code)
                    .FirstOrDefault(),
                Direction = db.WorkflowProfiles
                    .Where(p => p.Id == t.WorkflowProfileId)
                    .Select(p => p.Direction)
                    .FirstOrDefault(),
                Gross = db.WeighEvents
                    .Where(e => e.TripId == t.Id && e.Role == WeightRole.Gross && e.IsEffective)
                    .Select(e => new { e.NormalizedKg, e.MeasuredAt })
                    .FirstOrDefault(),
                Tare = db.WeighEvents
                    .Where(e => e.TripId == t.Id && e.Role == WeightRole.Tare && e.IsEffective)
                    .Select(e => new { e.NormalizedKg, e.MeasuredAt })
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var result = new List<TripBoardRow>(rows.Count);

        foreach (var r in rows)
        {
            if (filter.FirstWeightOnly && (r.Gross is null || r.Tare is not null))
                continue;

            if (!string.IsNullOrWhiteSpace(filter.ProfileCode) &&
                !string.Equals(r.ProfileCode, filter.ProfileCode, StringComparison.OrdinalIgnoreCase))
                continue;

            // Thứ tự thời gian quyết định lần 1/lần 2, không suy từ vai trò.
            var first = r.Gross is null ? r.Tare
                : r.Tare is null ? r.Gross
                : r.Gross.MeasuredAt <= r.Tare.MeasuredAt ? r.Gross : r.Tare;

            var second = r.Gross is null || r.Tare is null ? null
                : ReferenceEquals(first, r.Gross) ? r.Tare : r.Gross;

            decimal? net = null;
            if (r.Gross is not null && r.Tare is not null)
                net = r.Gross.NormalizedKg - r.Tare.NormalizedKg;

            result.Add(new TripBoardRow(
                TripId: r.Id,
                Version: r.Version,
                TripNo: r.TripNo,
                ReferenceNo: r.ReferenceTripNo,
                Plate: r.TractorPlate,
                ProfileCode: r.ProfileCode ?? "—",
                Lifecycle: r.Lifecycle,
                ExecutionStatus: r.ExecutionStatus,
                PauseIssueCode: r.PauseIssueCode,
                FirstWeightKg: first?.NormalizedKg,
                SecondWeightKg: second?.NormalizedKg,
                VehicleNetKg: net,
                FirstWeighedAt: first?.MeasuredAt,
                SecondWeighedAt: second?.MeasuredAt,
                CreatedAt: r.CreatedAt));
        }

        return result;
    }
}
