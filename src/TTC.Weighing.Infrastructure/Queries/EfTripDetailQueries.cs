using Microsoft.EntityFrameworkCore;
using TTC.Weighing.Application.Queries;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Infrastructure.Persistence;

namespace TTC.Weighing.Infrastructure.Queries;

public sealed class EfTripDetailQueries(WeighingDbContext db)
    : ITripDetailQueries
{
    public async Task<TripDetailView?> GetAsync(Guid tripId, CancellationToken ct = default)
    {
        var trip = await db.Trips
            .Include(t => t.Checkpoints)
            .FirstOrDefaultAsync(t => t.Id == tripId && t.DeletedAt == null, ct);
        if (trip is null) return null;

        // Build header trực tiếp từ Trip entity thay vì qua Dashboard query
        var grossEvent = await db.WeighEvents
            .Where(e => e.TripId == tripId && e.Role == Domain.Enums.WeightRole.Gross && e.IsEffective)
            .FirstOrDefaultAsync(ct);
        var tareEvent = await db.WeighEvents
            .Where(e => e.TripId == tripId && e.Role == Domain.Enums.WeightRole.Tare && e.IsEffective)
            .FirstOrDefaultAsync(ct);

        var profileCode = await db.WorkflowProfiles
            .Where(p => p.Id == trip.WorkflowProfileId)
            .Select(p => p.Code)
            .FirstOrDefaultAsync(ct) ?? "—";

        var header = new TripBoardRow(
            trip.Id, trip.Version, trip.TripNo, trip.ReferenceTripNo,
            trip.TractorPlate,
            profileCode,
            trip.Lifecycle, trip.ExecutionStatus, trip.PauseIssueCode,
            grossEvent?.NormalizedKg, tareEvent?.NormalizedKg,
            grossEvent is not null && tareEvent is not null ? grossEvent.NormalizedKg - tareEvent.NormalizedKg : null,
            grossEvent?.MeasuredAt, tareEvent?.MeasuredAt,
            trip.CreatedAt);

        // Build checkpoints từ dữ liệu thực tế (WeighEvents, QualitySamples, GateDecisions)
        // vì checkpoint_events chưa được ghi tự động bởi handlers hiện tại.
        var checkpoints = await BuildCheckpointsFromDataAsync(tripId, ct);

        // Hiển thị cả bản đã bị thay thế để giữ dấu vết (R-026).
        var weighEvents = await db.WeighEvents
            .Where(e => e.TripId == tripId)
            .OrderBy(e => e.Role).ThenByDescending(e => e.Revision)
            .Select(e => new WeighEventRow(
                e.Id, e.Revision, e.IsEffective, e.Role,
                e.RawWeight, e.RawUom, e.NormalizedKg, e.Source,
                e.MeasuredAt, e.StableWindow, e.Reason, e.ApprovalRef,
                e.EvidenceRefs.Count))
            .ToListAsync(ct);

        var samples = await db.QualitySamples
            .Where(s => s.TripId == tripId)
            .OrderBy(s => s.Type).ThenByDescending(s => s.Revision)
            .Select(s => new QualitySampleRow(
                s.Type, s.Status, s.Revision, s.IsEffective,
                s.SampleBeforeKg, s.SampleAfterKg, s.ImpurityPercent, s.CcsValue,
                s.BelowMinimumSampleWarning, s.Source, s.ReceivedAt))
            .ToListAsync(ct);

        var measurementSessions = await db.MeasurementSessions
            .Where(m => m.TripId == tripId)
            .OrderBy(m => m.OpenedAt)
            .Select(m => new MeasurementSessionRow(
                m.Id, m.ItemCode, m.Status, m.ReadingMode, m.MassBasis,
                m.IotMassKg, m.IotMassOrigin,
                m.ForwardBagCount, m.ReverseBagCount, m.NetBagCount,
                m.DataComplete, m.HoldReason))
            .ToListAsync(ct);

        var reconciliation = await db.Reconciliations
            .Include(r => r.Checks)
            .Where(r => r.TripId == tripId && r.IsEffective)
            .OrderByDescending(r => r.Revision)
            .FirstOrDefaultAsync(ct);

        var checks = reconciliation is null
            ? []
            : reconciliation.Checks
                .Select(c => new ReconciliationCheckRow(
                    c.CheckType, c.Verdict, c.ObservedValue, c.ExpectedValue,
                    c.LowerBound, c.UpperBound, c.Difference, c.Unit,
                    c.TolerancePolicyId, c.Detail))
                .ToList();

        var allocations = await db.Allocations
            .Where(a => a.TripId == tripId && a.DeletedAt == null)
            .OrderBy(a => a.OwnerRef)
            .Select(a => new AllocationRow(
                a.OwnerRef, a.DocumentLineId, a.AllocatedKg, a.Method,
                a.Ratio, a.RoundingAdjustmentKg, a.ApprovalRef != null))
            .ToListAsync(ct);

        var messages = await db.IntegrationMessages
            .Where(m => m.TripId == tripId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new IntegrationMessageRow(
                m.Id, m.Destination, m.Operation, m.BusinessKey, m.ShipmentNo,
                m.Status, m.Attempts, m.LastAttemptAt,
                m.BusinessErrorCode, m.BusinessErrorMessage, m.ExternalDocumentRef,
                m.Status == IntegrationMessageStatus.Unknown
                    || (m.Status == IntegrationMessageStatus.Failed && !m.AutoRetryAllowed)))
            .ToListAsync(ct);

        var audit = await db.AuditLogEntries
            .Where(a => a.TripId == tripId)
            .OrderByDescending(a => a.OccurredAt)
            .Take(200)
            .Select(a => new AuditRow(
                a.OccurredAt, a.ActionType, a.Actor, a.Reason, a.BeforeJson, a.AfterJson))
            .ToListAsync(ct);

        var blockers = BuildBlockers(trip.ExecutionStatus, trip.PauseIssueCode, checks, messages, allocations);

        return new TripDetailView(
            header, checkpoints, weighEvents, samples, measurementSessions,
            checks, allocations, messages, audit, blockers);
    }

    /// <summary>
    /// Tự suy checkpoint từ dữ liệu thực tế: WeighEvents, QualitySamples, GateDecisions, Warehouse checkpoints.
    /// Dùng khi checkpoint_events chưa được ghi tự động bởi handlers.
    /// </summary>
    private async Task<List<CheckpointRow>> BuildCheckpointsFromDataAsync(Guid tripId, CancellationToken ct)
    {
        var result = new List<CheckpointRow>();

        // Vào cổng
        var gateIn = await db.GateDecisions
            .Where(d => d.TripId == tripId && d.Direction == Domain.Enums.GateDirection.In && d.Allowed)
            .OrderBy(d => d.DecidedAt)
            .FirstOrDefaultAsync(ct);
        result.Add(new CheckpointRow(
            Domain.Enums.CheckpointType.GateIn,
            gateIn is not null ? Domain.Enums.CheckpointStatus.Done : Domain.Enums.CheckpointStatus.Pending,
            gateIn?.DecidedAt, gateIn?.DecidedAt, gateIn?.CreatedBy, null));

        // Cân lần 1
        var gross = await db.WeighEvents
            .Where(e => e.TripId == tripId && e.Role == Domain.Enums.WeightRole.Gross && e.IsEffective)
            .FirstOrDefaultAsync(ct);
        result.Add(new CheckpointRow(
            Domain.Enums.CheckpointType.FirstWeight,
            gross is not null ? Domain.Enums.CheckpointStatus.Done : Domain.Enums.CheckpointStatus.Pending,
            gross?.MeasuredAt, gross?.MeasuredAt, gross?.CreatedBy, null));

        // Tạp chất
        var impurity = await db.QualitySamples
            .Where(s => s.TripId == tripId && s.Type == Domain.Enums.QualitySampleType.Impurity && s.IsEffective)
            .FirstOrDefaultAsync(ct);
        result.Add(new CheckpointRow(
            Domain.Enums.CheckpointType.ImpuritySample,
            impurity is not null ? Domain.Enums.CheckpointStatus.Done : Domain.Enums.CheckpointStatus.Pending,
            impurity?.ReceivedAt, impurity?.ReceivedAt, impurity?.ReceivedBy, null));

        // Cân lần 2
        var tare = await db.WeighEvents
            .Where(e => e.TripId == tripId && e.Role == Domain.Enums.WeightRole.Tare && e.IsEffective)
            .FirstOrDefaultAsync(ct);
        result.Add(new CheckpointRow(
            Domain.Enums.CheckpointType.SecondWeight,
            tare is not null ? Domain.Enums.CheckpointStatus.Done : Domain.Enums.CheckpointStatus.Pending,
            tare?.MeasuredAt, tare?.MeasuredAt, tare?.CreatedBy, null));

        // Vào kho — check CheckpointEvent WarehouseIn hoặc MeasurementSession
        var warehouseCheckpoint = await db.CheckpointEvents
            .Where(c => c.TripId == tripId && c.Type == Domain.Enums.CheckpointType.WarehouseIn)
            .OrderBy(c => c.StartedAt ?? c.CreatedAt)
            .FirstOrDefaultAsync(ct);
        var warehouseSession = warehouseCheckpoint is null
            ? await db.MeasurementSessions
                .Where(m => m.TripId == tripId)
                .OrderBy(m => m.OpenedAt)
                .FirstOrDefaultAsync(ct)
            : null;
        var whDone = warehouseCheckpoint is not null || warehouseSession is not null;
        result.Add(new CheckpointRow(
            Domain.Enums.CheckpointType.WarehouseIn,
            whDone ? Domain.Enums.CheckpointStatus.Done : Domain.Enums.CheckpointStatus.Pending,
            warehouseCheckpoint?.StartedAt ?? warehouseSession?.OpenedAt,
            warehouseCheckpoint?.CompletedAt ?? warehouseSession?.ClosedAt,
            warehouseCheckpoint?.ConfirmedBy, null));

        // Ra cổng
        var gateOut = await db.GateDecisions
            .Where(d => d.TripId == tripId && d.Direction == Domain.Enums.GateDirection.Out && d.Allowed)
            .OrderBy(d => d.DecidedAt)
            .FirstOrDefaultAsync(ct);
        result.Add(new CheckpointRow(
            Domain.Enums.CheckpointType.GateOut,
            gateOut is not null ? Domain.Enums.CheckpointStatus.Done : Domain.Enums.CheckpointStatus.Pending,
            gateOut?.DecidedAt, gateOut?.DecidedAt, gateOut?.CreatedBy, null));

        return result;
    }

    private static List<string> BuildBlockers(
        ExecutionStatus executionStatus,
        string? pauseIssueCode,
        IReadOnlyList<ReconciliationCheckRow> checks,
        IReadOnlyList<IntegrationMessageRow> messages,
        IReadOnlyList<AllocationRow> allocations)
    {
        var blockers = new List<string>();

        if (executionStatus == ExecutionStatus.Paused)
            blockers.Add($"Chuyến đang PAUSED (mã sự cố {pauseIssueCode ?? "không rõ"}).");

        blockers.AddRange(checks
            .Where(c => c.Verdict is ReconciliationVerdict.Mismatch
                or ReconciliationVerdict.MissingData
                or ReconciliationVerdict.Pending)
            .Select(c => $"Đối soát {c.CheckType} = {c.Verdict}: {c.Detail}"));

        blockers.AddRange(messages
            .Where(m => m.RequiresManualReconciliation)
            .Select(m => $"Thông điệp {m.Operation} ở trạng thái {m.Status}, cần đối soát thủ công."));

        if (allocations.Any(a => !a.IsApproved))
            blockers.Add("Còn phân bổ chưa được duyệt, chưa gửi kết quả phân bổ.");

        return blockers;
    }
}

public sealed class EfExceptionQueueQueries(WeighingDbContext db) : IExceptionQueueQueries
{
    public async Task<IReadOnlyList<ExceptionQueueItem>> ListAsync(
        Guid siteId, CancellationToken ct = default)
    {
        var items = new List<ExceptionQueueItem>();

        var pendingApprovals = await db.Approvals
            .Where(a => a.ApprovedAt == null
                && db.Trips.Any(t => t.Id == a.TripId && t.SiteId == siteId))
            .Select(a => new
            {
                a.TripId,
                a.ActionType,
                a.Reason,
                a.CreatedAt,
                a.RequestedBy,
                TripNo = db.Trips.Where(t => t.Id == a.TripId).Select(t => t.TripNo).FirstOrDefault()
            })
            .ToListAsync(ct);

        items.AddRange(pendingApprovals.Select(a => new ExceptionQueueItem(
            a.TripId ?? Guid.Empty,
            a.TripNo ?? "—",
            $"Chờ duyệt: {a.ActionType}",
            a.Reason,
            a.CreatedAt,
            a.RequestedBy,
            "Duyệt hoặc từ chối kèm lý do và biên bản")));

        var openIncidents = await db.Incidents
            .Where(i => i.SiteId == siteId && i.ResolvedAt == null)
            .Select(i => new
            {
                i.TripId,
                i.IssueCode,
                i.Description,
                i.OpenedAt,
                i.OpenedBy,
                TripNo = db.Trips.Where(t => t.Id == i.TripId).Select(t => t.TripNo).FirstOrDefault()
            })
            .ToListAsync(ct);

        items.AddRange(openIncidents.Select(i => new ExceptionQueueItem(
            i.TripId ?? Guid.Empty,
            i.TripNo ?? "—",
            $"Sự cố: {i.IssueCode}",
            i.Description,
            i.OpenedAt,
            i.OpenedBy,
            "Kiểm tra, xử lý giao dịch UNKNOWN rồi cho tiếp tục")));

        var mismatches = await db.ReconciliationChecks
            .Where(c => (c.Verdict == ReconciliationVerdict.Mismatch
                    || c.Verdict == ReconciliationVerdict.MissingData)
                && db.Reconciliations.Any(r => r.Id == c.ReconciliationId && r.IsEffective))
            .Select(c => new
            {
                TripId = db.Reconciliations
                    .Where(r => r.Id == c.ReconciliationId).Select(r => r.TripId).FirstOrDefault(),
                c.CheckType,
                c.Verdict,
                c.Detail,
                c.CreatedAt
            })
            .ToListAsync(ct);

        var tripNumbers = await db.Trips
            .Where(t => t.SiteId == siteId)
            .ToDictionaryAsync(t => t.Id, t => t.TripNo, ct);

        items.AddRange(mismatches
            .Where(m => tripNumbers.ContainsKey(m.TripId))
            .Select(m => new ExceptionQueueItem(
                m.TripId,
                tripNumbers[m.TripId],
                $"Đối soát {m.Verdict}: {m.CheckType}",
                m.Detail ?? "—",
                m.CreatedAt,
                null,
                "Kiểm đếm ngoài hệ thống rồi cân lại; không sửa số đo để khớp đơn")));

        return [.. items.OrderByDescending(i => i.RaisedAt)];
    }
}

public sealed class EfIntegrationMonitorQueries(WeighingDbContext db) : IIntegrationMonitorQueries
{
    public async Task<IReadOnlyList<IntegrationMessageRow>> ListAsync(
        Guid siteId, bool onlyRequiringAttention, CancellationToken ct = default)
    {
        var query = db.IntegrationMessages
            .Where(m => db.Trips.Any(t => t.Id == m.TripId && t.SiteId == siteId));

        if (onlyRequiringAttention)
        {
            query = query.Where(m =>
                m.Status == IntegrationMessageStatus.Unknown
                || m.Status == IntegrationMessageStatus.Failed
                || m.Status == IntegrationMessageStatus.Held);
        }

        return await query
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => new IntegrationMessageRow(
                m.Id, m.Destination, m.Operation, m.BusinessKey, m.ShipmentNo,
                m.Status, m.Attempts, m.LastAttemptAt,
                m.BusinessErrorCode, m.BusinessErrorMessage, m.ExternalDocumentRef,
                m.Status == IntegrationMessageStatus.Unknown
                    || (m.Status == IntegrationMessageStatus.Failed && !m.AutoRetryAllowed)))
            .ToListAsync(ct);
    }
}

public sealed class EfReportQueries(WeighingDbContext db, IDashboardQueries dashboard) : IReportQueries
{
    public async Task<IReadOnlyList<WeighReportRow>> ListWeighReportAsync(
        Guid siteId, TripBoardFilter filter, CancellationToken ct = default)
    {
        var trips = await dashboard.ListTripsAsync(siteId, filter, ct);
        var tripIds = trips.Select(t => t.TripId).ToList();

        var samples = await db.QualitySamples
            .Where(s => tripIds.Contains(s.TripId) && s.IsEffective)
            .Select(s => new { s.TripId, s.Type, s.ImpurityPercent, s.CcsValue })
            .ToListAsync(ct);

        var manualCounts = await db.WeighEvents
            .Where(e => tripIds.Contains(e.TripId) && e.Source != WeighSource.Auto)
            .GroupBy(e => e.TripId)
            .Select(g => new { TripId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TripId, x => x.Count, ct);

        var verdicts = await db.Reconciliations
            .Include(r => r.Checks)
            .Where(r => tripIds.Contains(r.TripId) && r.IsEffective)
            .ToListAsync(ct);

        return trips.Select(t =>
        {
            var impurity = samples.FirstOrDefault(
                s => s.TripId == t.TripId && s.Type == QualitySampleType.Impurity)?.ImpurityPercent;

            var ccs = samples.FirstOrDefault(
                s => s.TripId == t.TripId && s.Type == QualitySampleType.Ccs)?.CcsValue;

            var reconciliation = verdicts.FirstOrDefault(r => r.TripId == t.TripId);

            ReconciliationVerdict? overall = reconciliation is null
                ? null
                : reconciliation.IsPassed
                    ? ReconciliationVerdict.Pass
                    : reconciliation.Checks
                        .Where(c => c.Verdict != ReconciliationVerdict.Pass
                            && c.Verdict != ReconciliationVerdict.NotApplicable)
                        .Select(c => c.Verdict)
                        .FirstOrDefault();

            return new WeighReportRow(
                TripNo: t.TripNo,
                ReferenceNo: t.ReferenceNo,
                ProfileCode: t.ProfileCode,
                Plate: t.Plate,
                GrossKg: t.FirstWeightKg,
                TareKg: t.SecondWeightKg,
                NetKg: t.VehicleNetKg,
                ImpurityPercent: impurity,
                CcsValue: ccs,
                OverallVerdict: overall,
                ManualInterventionCount: manualCounts.GetValueOrDefault(t.TripId),
                CompletedAt: t.SecondWeighedAt);
        }).ToList();
    }
}
