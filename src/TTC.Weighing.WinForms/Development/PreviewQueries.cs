using TTC.Weighing.Application.Queries;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.WinForms.Development;

/// <summary>CHỈ DÙNG XEM TRƯỚC. Read model tính trực tiếp trên dữ liệu bộ nhớ.</summary>
public sealed class PreviewDashboardQueries(PreviewDataStore store) : IDashboardQueries
{
    public Task<SiteOverview> GetOverviewAsync(Guid siteId, CancellationToken ct = default)
    {
        var trips = store.Trips;

        var firstWeightOnly = trips.Count(t =>
            store.WeighEvents.Any(e => e.TripId == t.Id && e.IsEffective) &&
            store.WeighEvents.Count(e => e.TripId == t.Id && e.IsEffective) == 1);

        var reconciliationBlockers = store.Reconciliations
            .Where(r => r.IsEffective)
            .SelectMany(r => r.Checks)
            .Count(c => c.Verdict is ReconciliationVerdict.Mismatch or ReconciliationVerdict.MissingData);

        var integrationAttention = store.IntegrationMessages.Count(m =>
            m.Status is IntegrationMessageStatus.Unknown
                or IntegrationMessageStatus.Failed
                or IntegrationMessageStatus.Held);

        var devices = store.Devices.Select(d => new DeviceHealthRow(
            d.Code,
            d.DeviceClass,
            store.Lane.Code,
            d.IsEnabled,
            // Chưa có kênh giám sát thiết bị được nghiệm thu (D-05).
            null,
            store.DeviceEvents
                .Where(e => e.DeviceId == d.Id)
                .Select(e => (DateTimeOffset?)e.ReceivedAt)
                .DefaultIfEmpty(null)
                .Max())).ToList();

        var lifecycle = new LifecycleCounts(
            TempRegistered: trips.Count(t => t.Lifecycle == TripLifecycle.TempRegistered),
            Registered: trips.Count(t => t.Lifecycle == TripLifecycle.Registered),
            InProgress: trips.Count(t => t.Lifecycle == TripLifecycle.InProgress),
            WeighingCompleted: trips.Count(t => t.Lifecycle == TripLifecycle.WeighingCompleted),
            Reconciled: trips.Count(t => t.Lifecycle == TripLifecycle.Reconciled),
            Posted: trips.Count(t => t.Lifecycle == TripLifecycle.Posted),
            Closed: trips.Count(t => t.Lifecycle == TripLifecycle.Closed),
            Cancelled: trips.Count(t => t.Lifecycle == TripLifecycle.Cancelled));

        decimal totalNet = 0;
        foreach (var trip in trips)
        {
            var gross = store.WeighEvents.FirstOrDefault(
                e => e.TripId == trip.Id && e.Role == WeightRole.Gross && e.IsEffective);
            var tare = store.WeighEvents.FirstOrDefault(
                e => e.TripId == trip.Id && e.Role == WeightRole.Tare && e.IsEffective);

            if (gross is not null && tare is not null)
                totalNet += gross.NormalizedKg - tare.NormalizedKg;
        }

        return Task.FromResult(new SiteOverview(
            WaitingForWeighing: trips.Count(t =>
                t.Lifecycle is TripLifecycle.Registered or TripLifecycle.TempRegistered),
            InsidePlant: trips.Count(t => t.Lifecycle is TripLifecycle.InProgress
                or TripLifecycle.WeighingCompleted or TripLifecycle.Reconciled or TripLifecycle.Posted),
            FirstWeightOnly: firstWeightOnly,
            PausedTrips: trips.Count(t => t.ExecutionStatus == ExecutionStatus.Paused),
            ReconciliationBlockers: reconciliationBlockers,
            IntegrationAttention: integrationAttention,
            Lifecycle: lifecycle,
            TotalTripsInRange: trips.Count,
            TotalVehicleNetKgInRange: totalNet == 0 ? null : totalNet,
            ManualInterventions: store.WeighEvents.Count(e => e.Source != WeighSource.Auto),
            Devices: devices));
    }

    public Task<IReadOnlyList<TripBoardRow>> ListTripsAsync(
        Guid siteId, TripBoardFilter filter, CancellationToken ct = default)
    {
        var rows = new List<TripBoardRow>();

        foreach (var trip in store.Trips.OrderByDescending(t => t.CreatedAt))
        {
            var profile = store.Profiles.FirstOrDefault(p => p.Id == trip.WorkflowProfileId);

            var events = store.WeighEvents
                .Where(e => e.TripId == trip.Id && e.IsEffective)
                .OrderBy(e => e.MeasuredAt)
                .ToList();

            var first = events.ElementAtOrDefault(0);
            var second = events.ElementAtOrDefault(1);

            var gross = events.FirstOrDefault(e => e.Role == WeightRole.Gross);
            var tare = events.FirstOrDefault(e => e.Role == WeightRole.Tare);

            decimal? net = gross is not null && tare is not null
                ? gross.NormalizedKg - tare.NormalizedKg
                : null;

            if (filter.FirstWeightOnly && (first is null || second is not null))
                continue;

            if (filter.Lifecycle.HasValue && trip.Lifecycle != filter.Lifecycle.Value)
                continue;

            if (filter.ExecutionStatus.HasValue && trip.ExecutionStatus != filter.ExecutionStatus.Value)
                continue;

            if (!string.IsNullOrWhiteSpace(filter.ProfileCode) &&
                !string.Equals(profile?.Code, filter.ProfileCode, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrWhiteSpace(filter.PlateContains) &&
                trip.TractorPlate?.Contains(filter.PlateContains, StringComparison.OrdinalIgnoreCase) != true)
                continue;

            if (filter.FromUtc.HasValue && trip.CreatedAt < filter.FromUtc.Value)
                continue;

            if (filter.ToUtc.HasValue && trip.CreatedAt >= filter.ToUtc.Value)
                continue;

            rows.Add(new TripBoardRow(
                trip.Id, trip.Version, trip.TripNo, trip.ReferenceTripNo, trip.TractorPlate,
                profile?.Code ?? "—", trip.Lifecycle, trip.ExecutionStatus, trip.PauseIssueCode,
                first?.NormalizedKg, second?.NormalizedKg, net,
                first?.MeasuredAt, second?.MeasuredAt, trip.CreatedAt));
        }

        return Task.FromResult<IReadOnlyList<TripBoardRow>>(rows);
    }
}

public sealed class PreviewTripDetailQueries(PreviewDataStore store, IDashboardQueries dashboard)
    : ITripDetailQueries
{
    public async Task<TripDetailView?> GetAsync(Guid tripId, CancellationToken ct = default)
    {
        var trip = store.Trips.FirstOrDefault(t => t.Id == tripId);
        if (trip is null) return null;

        var board = await dashboard.ListTripsAsync(store.Site.Id, new TripBoardFilter(), ct);
        var header = board.FirstOrDefault(r => r.TripId == tripId);
        if (header is null) return null;

        var checkpoints = trip.Checkpoints
            .Select(c => new CheckpointRow(
                c.Type, c.Status, c.StartedAt, c.CompletedAt, c.ConfirmedBy, c.WaiverReason))
            .ToList();

        var weighEvents = store.WeighEvents
            .Where(e => e.TripId == tripId)
            .OrderBy(e => e.Role).ThenByDescending(e => e.Revision)
            .Select(e => new WeighEventRow(
                e.Id, e.Revision, e.IsEffective, e.Role,
                e.RawWeight, e.RawUom, e.NormalizedKg, e.Source,
                e.MeasuredAt, e.StableWindow, e.Reason, e.ApprovalRef, e.EvidenceRefs.Count))
            .ToList();

        var samples = store.QualitySamples
            .Where(s => s.TripId == tripId)
            .OrderBy(s => s.Type).ThenByDescending(s => s.Revision)
            .Select(s => new QualitySampleRow(
                s.TripId,
                store.Trips.FirstOrDefault(t => t.Id == s.TripId)?.TripNo,
                store.Trips.FirstOrDefault(t => t.Id == s.TripId)?.TractorPlate,
                s.Type, s.Status, s.Revision, s.IsEffective,
                s.SampleBeforeKg, s.SampleAfterKg, s.ImpurityPercent, s.CcsValue,
                s.BelowMinimumSampleWarning, s.Source, s.ReceivedAt))
            .ToList();

        var measurementSessions = store.MeasurementSessions
            .Where(m => m.TripId == tripId)
            .Select(m => new MeasurementSessionRow(
                m.Id, m.ItemCode, m.Status, m.ReadingMode, m.MassBasis,
                m.IotMassKg, m.IotMassOrigin,
                m.ForwardBagCount, m.ReverseBagCount, m.NetBagCount,
                m.DataComplete, m.HoldReason))
            .ToList();

        var reconciliation = store.Reconciliations
            .Where(r => r.TripId == tripId && r.IsEffective)
            .OrderByDescending(r => r.Revision)
            .FirstOrDefault();

        var checks = reconciliation?.Checks
            .Select(c => new ReconciliationCheckRow(
                c.CheckType, c.Verdict, c.ObservedValue, c.ExpectedValue,
                c.LowerBound, c.UpperBound, c.Difference, c.Unit,
                c.TolerancePolicyId, c.Detail))
            .ToList() ?? [];

        var allocations = store.Allocations
            .Where(a => a.TripId == tripId && a.DeletedAt == null)
            .Select(a => new AllocationRow(
                a.OwnerRef, a.DocumentLineId, a.AllocatedKg, a.Method,
                a.Ratio, a.RoundingAdjustmentKg, a.IsApproved))
            .ToList();

        var messages = store.IntegrationMessages
            .Where(m => m.TripId == tripId)
            .Select(m => new IntegrationMessageRow(
                m.Id, m.Destination, m.Operation, m.BusinessKey, m.ShipmentNo,
                m.Status, m.Attempts, m.LastAttemptAt,
                m.BusinessErrorCode, m.BusinessErrorMessage, m.ExternalDocumentRef,
                m.RequiresManualReconciliation))
            .ToList();

        var audit = store.AuditLog
            .Where(a => a.TripId == tripId)
            .OrderByDescending(a => a.OccurredAt)
            .Select(a => new AuditRow(
                a.OccurredAt, a.ActionType, a.Actor, a.Reason, a.BeforeJson, a.AfterJson))
            .ToList();

        var blockers = new List<string>();

        if (trip.ExecutionStatus == ExecutionStatus.Paused)
            blockers.Add($"Chuyến đang PAUSED (mã sự cố {trip.PauseIssueCode ?? "không rõ"}).");

        blockers.AddRange(checks
            .Where(c => c.Verdict is ReconciliationVerdict.Mismatch
                or ReconciliationVerdict.MissingData or ReconciliationVerdict.Pending)
            .Select(c => $"Đối soát {c.CheckType} = {c.Verdict}: {c.Detail}"));

        blockers.AddRange(messages
            .Where(m => m.RequiresManualReconciliation)
            .Select(m => $"Thông điệp {m.Operation} ở trạng thái {m.Status}, cần đối soát thủ công."));

        if (allocations.Any(a => !a.IsApproved))
            blockers.Add("Còn phân bổ chưa được duyệt.");

        return new TripDetailView(
            header, checkpoints, weighEvents, samples, measurementSessions,
            checks, allocations, messages, audit, blockers);
    }

    public Task<IReadOnlyList<QualitySampleRow>> ListAllSamplesAsync(
        Guid siteId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        var result = store.QualitySamples
            .Where(s => s.ReceivedAt >= fromUtc && s.ReceivedAt < toUtc)
            .OrderByDescending(s => s.ReceivedAt)
            .Select(s => new QualitySampleRow(
                s.TripId,
                store.Trips.FirstOrDefault(t => t.Id == s.TripId)?.TripNo,
                store.Trips.FirstOrDefault(t => t.Id == s.TripId)?.TractorPlate,
                s.Type, s.Status, s.Revision, s.IsEffective,
                s.SampleBeforeKg, s.SampleAfterKg, s.ImpurityPercent, s.CcsValue,
                s.BelowMinimumSampleWarning, s.Source, s.ReceivedAt))
            .ToList();

        return Task.FromResult<IReadOnlyList<QualitySampleRow>>(result);
    }
}

public sealed class PreviewExceptionQueueQueries(PreviewDataStore store) : IExceptionQueueQueries
{
    public Task<IReadOnlyList<ExceptionQueueItem>> ListAsync(
        Guid siteId, CancellationToken ct = default)
    {
        var items = new List<ExceptionQueueItem>();

        foreach (var approval in store.Approvals.Where(a => a.ApprovedAt == null))
        {
            var tripNo = store.Trips.FirstOrDefault(t => t.Id == approval.TripId)?.TripNo ?? "—";
            items.Add(new ExceptionQueueItem(
                approval.TripId ?? Guid.Empty, tripNo,
                $"Chờ duyệt: {approval.ActionType}", approval.Reason,
                approval.CreatedAt, approval.RequestedBy,
                "Duyệt hoặc từ chối kèm lý do và biên bản"));
        }

        foreach (var incident in store.Incidents.Where(i => i.ResolvedAt == null))
        {
            var tripNo = store.Trips.FirstOrDefault(t => t.Id == incident.TripId)?.TripNo ?? "—";
            items.Add(new ExceptionQueueItem(
                incident.TripId ?? Guid.Empty, tripNo,
                $"Sự cố: {incident.IssueCode}", incident.Description,
                incident.OpenedAt, incident.OpenedBy,
                "Kiểm tra, xử lý giao dịch UNKNOWN rồi cho tiếp tục"));
        }

        foreach (var reconciliation in store.Reconciliations.Where(r => r.IsEffective))
        {
            var tripNo = store.Trips.FirstOrDefault(t => t.Id == reconciliation.TripId)?.TripNo ?? "—";

            foreach (var check in reconciliation.Blockers)
            {
                items.Add(new ExceptionQueueItem(
                    reconciliation.TripId, tripNo,
                    $"Đối soát {check.Verdict}: {check.CheckType}",
                    check.Detail ?? "—",
                    reconciliation.EvaluatedAt, null,
                    "Kiểm đếm ngoài hệ thống rồi cân lại; không sửa số đo để khớp đơn"));
            }
        }

        return Task.FromResult<IReadOnlyList<ExceptionQueueItem>>(
            [.. items.OrderByDescending(i => i.RaisedAt)]);
    }
}

public sealed class PreviewIntegrationMonitorQueries(PreviewDataStore store) : IIntegrationMonitorQueries
{
    public Task<IReadOnlyList<IntegrationMessageRow>> ListAsync(
        Guid siteId, bool onlyRequiringAttention, CancellationToken ct = default)
    {
        var query = store.IntegrationMessages.AsEnumerable();

        if (onlyRequiringAttention)
        {
            query = query.Where(m => m.Status is IntegrationMessageStatus.Unknown
                or IntegrationMessageStatus.Failed
                or IntegrationMessageStatus.Held);
        }

        return Task.FromResult<IReadOnlyList<IntegrationMessageRow>>(
            [.. query.OrderByDescending(m => m.CreatedAt).Select(m => new IntegrationMessageRow(
                m.Id, m.Destination, m.Operation, m.BusinessKey, m.ShipmentNo,
                m.Status, m.Attempts, m.LastAttemptAt,
                m.BusinessErrorCode, m.BusinessErrorMessage, m.ExternalDocumentRef,
                m.RequiresManualReconciliation))]);
    }
}

public sealed class PreviewReportQueries(PreviewDataStore store, IDashboardQueries dashboard)
    : IReportQueries
{
    public async Task<IReadOnlyList<WeighReportRow>> ListWeighReportAsync(
        Guid siteId, TripBoardFilter filter, CancellationToken ct = default)
    {
        var trips = await dashboard.ListTripsAsync(siteId, filter, ct);

        return trips.Select(t =>
        {
            var impurity = store.QualitySamples.FirstOrDefault(
                s => s.TripId == t.TripId && s.Type == QualitySampleType.Impurity && s.IsEffective);

            var ccs = store.QualitySamples.FirstOrDefault(
                s => s.TripId == t.TripId && s.Type == QualitySampleType.Ccs && s.IsEffective);

            var reconciliation = store.Reconciliations.FirstOrDefault(
                r => r.TripId == t.TripId && r.IsEffective);

            ReconciliationVerdict? overall = reconciliation is null
                ? null
                : reconciliation.IsPassed
                    ? ReconciliationVerdict.Pass
                    : reconciliation.Blockers.Select(c => c.Verdict).FirstOrDefault();

            var manualCount = store.WeighEvents.Count(
                e => e.TripId == t.TripId && e.Source != WeighSource.Auto);

            return new WeighReportRow(
                t.TripNo, t.ReferenceNo, t.ProfileCode, t.Plate,
                t.FirstWeightKg, t.SecondWeightKg, t.VehicleNetKg,
                impurity?.ImpurityPercent, ccs?.CcsValue,
                overall, manualCount, t.SecondWeighedAt);
        }).ToList();
    }
}


/// <summary>CHỈ DÙNG XEM TRƯỚC. Lưới đăng tài từ dữ liệu bộ nhớ.</summary>
public sealed class PreviewTripRegistrationQueries(PreviewDataStore store) : ITripRegistrationQueries
{
    private static readonly TripLifecycle[] InFactoryLifecycles =
    [
        TripLifecycle.TempRegistered,
        TripLifecycle.Registered,
        TripLifecycle.InProgress,
        TripLifecycle.WeighingCompleted,
        TripLifecycle.Reconciled,
        TripLifecycle.Posted
    ];

    public Task<IReadOnlyList<TripRegistrationRow>> ListAsync(
        Guid siteId, TripRegistrationFilter filter, CancellationToken ct = default)
    {
        var query = store.Trips.Where(t => t.SiteId == siteId && t.DeletedAt == null);

        query = filter.Tab switch
        {
            TripGridTab.InFactory => query.Where(t => InFactoryLifecycles.Contains(t.Lifecycle)),
            TripGridTab.Exited => query.Where(t => t.Lifecycle == TripLifecycle.Closed),
            TripGridTab.Cancelled => query.Where(t => t.Lifecycle == TripLifecycle.Cancelled),
            _ => query.Where(t => InFactoryLifecycles.Contains(t.Lifecycle))
        };

        if (filter.FromUtc.HasValue)
            query = query.Where(t => t.CreatedAt >= filter.FromUtc.Value);
        if (filter.ToUtc.HasValue)
            query = query.Where(t => t.CreatedAt < filter.ToUtc.Value);

        var rows = query
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new TripRegistrationRow(
                t.Id, t.Version, t.TripNo, t.ReferenceTripNo, t.TractorPlate,
                t.QueueSequenceNumber, t.DriverRef, null, null, null,
                t.SugarcaneOwner, t.SugarcaneType, t.Lifecycle,
                t.VehicleType?.ToString(), "📋 Đã đăng tài"))
            .ToList();

        return Task.FromResult<IReadOnlyList<TripRegistrationRow>>(rows);
    }
}


/// <summary>CHỈ DÙNG XEM TRƯỚC. Lưới xe đã cân từ dữ liệu bộ nhớ.</summary>
public sealed class PreviewWeighOperationQueries(PreviewDataStore store) : IWeighOperationQueries
{
    public Task<IReadOnlyList<WeighedTripRow>> ListAsync(
        Guid siteId, Guid weighbridgeId, WeighedTripFilter filter,
        CancellationToken ct = default)
    {
        var query = store.Trips.Where(t => t.SiteId == siteId);

        if (filter.FromUtc.HasValue)
            query = query.Where(t => t.CreatedAt >= filter.FromUtc.Value);
        if (filter.ToUtc.HasValue)
            query = query.Where(t => t.CreatedAt < filter.ToUtc.Value);

        var rows = new List<WeighedTripRow>();

        foreach (var trip in query.OrderByDescending(t => t.CreatedAt))
        {
            var events = store.WeighEvents
                .Where(e => e.TripId == trip.Id && e.IsEffective)
                .ToList();

            var gross = events.FirstOrDefault(e => e.Role == WeightRole.Gross);
            var tare = events.FirstOrDefault(e => e.Role == WeightRole.Tare);

            if (filter.FirstWeightOnly && (gross is null || tare is not null))
                continue;

            if (filter.CompletedOnly && (gross is null || tare is null))
                continue;

            decimal? net = gross is not null && tare is not null
                ? gross.NormalizedKg - tare.NormalizedKg
                : null;

            var ticket = store.WeighTickets
                .FirstOrDefault(t => t.TripId == trip.Id && !t.IsReprint);

            var syncMsg = store.IntegrationMessages
                .Where(m => m.TripId == trip.Id)
                .OrderByDescending(m => m.CreatedAt)
                .FirstOrDefault();

            rows.Add(new WeighedTripRow(
                TripId: trip.Id,
                RegNo: trip.TripNo,
                TicketNo: ticket?.TicketNumber,
                TripNumber: trip.TripNo,
                ShipmentNo: trip.ReferenceTripNo,
                Plate: trip.TractorPlate,
                Trailer: trip.TrailerPlate,
                TimeIn: gross?.MeasuredAt,
                WeightIn: gross?.NormalizedKg,
                TimeOut: tare?.MeasuredAt,
                WeightOut: tare?.NormalizedKg,
                NetWeight: net,
                Customer: trip.SugarcaneOwner,
                Commodity: null,
                SyncStatus: syncMsg?.Status));
        }

        return Task.FromResult<IReadOnlyList<WeighedTripRow>>(rows);
    }

    public Task<IReadOnlyList<WeighedTripRow>> ListPendingAsync(
        Guid siteId, CancellationToken ct = default)
    {
        var pending = store.Trips
            .Where(t => t.SiteId == siteId
                && t.Lifecycle is TripLifecycle.TempRegistered
                    or TripLifecycle.Registered
                    or TripLifecycle.InProgress
                && !store.WeighEvents.Any(e => e.TripId == t.Id))
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new WeighedTripRow(
                TripId: t.Id,
                RegNo: t.TripNo,
                TicketNo: null,
                TripNumber: t.ReferenceTripNo,
                ShipmentNo: t.ReferenceTripNo,
                Plate: t.TractorPlate,
                Trailer: t.TrailerPlate,
                TimeIn: null, WeightIn: null, TimeOut: null, WeightOut: null,
                NetWeight: null, Customer: t.SugarcaneOwner, Commodity: t.SugarcaneType,
                SyncStatus: null))
            .ToList();

        return Task.FromResult<IReadOnlyList<WeighedTripRow>>(pending);
    }
}

/// <summary>CHỈ DÙNG XEM TRƯỚC. Danh sách xe trong kho từ dữ liệu bộ nhớ.</summary>
public sealed class PreviewWarehouseQueries(PreviewDataStore store) : IWarehouseQueries
{
    public Task<IReadOnlyList<WarehouseVehicleRow>> ListAsync(
        Guid warehouseId, WarehouseFilter filter, CancellationToken ct = default)
    {
        var rows = new List<WarehouseVehicleRow>();

        foreach (var trip in store.Trips.OrderByDescending(t => t.CreatedAt))
        {
            var warehouseIn = trip.Checkpoints
                .FirstOrDefault(c => c.Type == CheckpointType.WarehouseIn);
            var warehouseOut = trip.Checkpoints
                .FirstOrDefault(c => c.Type == CheckpointType.WarehouseOut);

            // Chỉ lấy chuyến có liên quan đến kho (có checkpoint hoặc profile yêu cầu kho).
            if (warehouseIn is null && warehouseOut is null)
                continue;

            var entryTime = warehouseIn?.StartedAt;
            var exitTime = warehouseOut?.CompletedAt;

            if (filter.FromUtc.HasValue && entryTime < filter.FromUtc.Value)
                continue;
            if (filter.ToUtc.HasValue && entryTime >= filter.ToUtc.Value)
                continue;

            var isInside = warehouseIn is not null
                && (warehouseOut is null
                    || warehouseOut.Status is CheckpointStatus.Pending or CheckpointStatus.Active);

            if (filter.InsideWarehouseOnly && !isInside)
                continue;

            var hasOpenSession = store.MeasurementSessions.Any(
                s => s.TripId == trip.Id && s.Status == MeasurementSessionStatus.Open);

            rows.Add(new WarehouseVehicleRow(
                TripId: trip.Id,
                TripNo: trip.TripNo,
                Plate: trip.TractorPlate,
                Commodity: null,
                EntryTime: entryTime,
                ExitTime: exitTime,
                Status: warehouseOut?.Status ?? warehouseIn?.Status ?? CheckpointStatus.Pending,
                WaiverReason: warehouseOut?.WaiverReason,
                HasOpenMeasurementSession: hasOpenSession));
        }

        return Task.FromResult<IReadOnlyList<WarehouseVehicleRow>>(rows);
    }
}
