using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.UseCases.Gate;
using TTC.Weighing.Application.UseCases.Integration;
using TTC.Weighing.Application.UseCases.Measurement;
using TTC.Weighing.Application.UseCases.Reconcile;
using TTC.Weighing.Application.UseCases.Trips;
using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Domain.Rules;
using TTC.Weighing.WinForms.Presenters;

namespace TTC.Weighing.WinForms.Development;

/// <summary>
/// CHỈ DÙNG XEM TRƯỚC GIAO DIỆN. Không đăng ký ở môi trường vận hành.
/// Các lớp này thay lớp lưu trữ thật để chạy được luồng nghiệp vụ mà chưa cần database.
/// Mock không chứng minh đã tích hợp database/thiết bị thật (07_Prompt).
/// </summary>
public sealed class PreviewUnitOfWork : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);

    public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct = default)
        => Task.FromResult<IUnitOfWorkTransaction>(new NoopTransaction());

    /// <summary>Dữ liệu xem trước nằm trong bộ nhớ nên không có transaction thật.</summary>
    private sealed class NoopTransaction : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

public sealed class PreviewTripRepository(PreviewDataStore store) : ITripRepository
{
    public Task<Trip?> GetByIdAsync(Guid tripId, CancellationToken ct = default)
        => Task.FromResult(store.Trips.FirstOrDefault(t => t.Id == tripId));

    public Task<Trip?> GetByTripNoAsync(string tripNo, Guid siteId, CancellationToken ct = default)
        => Task.FromResult(store.Trips.FirstOrDefault(t => t.TripNo == tripNo && t.SiteId == siteId));

    public Task<Trip?> GetActiveByPlateAsync(string plate, Guid siteId, CancellationToken ct = default)
    {
        var normalized = plate.Replace(" ", "").Replace("-", "").Replace(".", "").ToUpperInvariant();
        return Task.FromResult(store.Trips
            .Where(t => t.SiteId == siteId
                && t.TractorPlate != null
                && t.Lifecycle is TripLifecycle.Registered or TripLifecycle.InProgress or TripLifecycle.WeighingCompleted)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefault(t =>
                t.TractorPlate!.Replace(" ", "").Replace("-", "").Replace(".", "").ToUpperInvariant() == normalized));
    }

    public Task<Trip?> GetForUpdateAsync(Guid tripId, int expectedVersion, CancellationToken ct = default)
        => Task.FromResult(store.Trips.FirstOrDefault(
            t => t.Id == tripId && t.Version == expectedVersion));

    public async Task AddAsync(Trip trip, CancellationToken ct = default)
        => await Task.Run(() => store.Trips.Add(trip), ct);

    public Task UpdateAsync(Trip trip, CancellationToken ct = default)
        => Task.CompletedTask; // In-memory — already tracked by reference

    public Task<IReadOnlyDictionary<Guid, int>> CountPendingByWeighbridgeAsync(
        Guid siteId, CancellationToken ct = default)
    {
        var counts = store.Trips
            .Where(t => t.SiteId == siteId
                && t.AssignedWeighbridgeId != null
                && t.Lifecycle is TripLifecycle.Registered or TripLifecycle.InProgress)
            .GroupBy(t => t.AssignedWeighbridgeId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        return Task.FromResult<IReadOnlyDictionary<Guid, int>>(counts);
    }

    public Task<string?> FindActiveByLenhDonIdAsync(string lenhDonId, CancellationToken ct = default)
    {
        var tripNo = store.Trips
            .Where(t => t.FrmLenhDonId == lenhDonId
                && t.Lifecycle != TripLifecycle.Cancelled)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => t.TripNo)
            .FirstOrDefault();

        return Task.FromResult(tripNo);
    }

    public Task<bool> HasWeighEventsAsync(Guid tripId, CancellationToken ct = default)
        => Task.FromResult(store.WeighEvents.Any(e => e.TripId == tripId));

    public Task<string?> FindActiveByReferenceAsync(
        string sourceSystem, string referenceTripNo, CancellationToken ct = default)
    {
        var reference = referenceTripNo.Trim();
        var tripNo = store.Trips
            .Where(t => t.SourceSystem == sourceSystem
                && t.ReferenceTripNo == reference
                && t.Lifecycle != TripLifecycle.Cancelled)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => t.TripNo)
            .FirstOrDefault();

        return Task.FromResult(tripNo);
    }
}

public sealed class PreviewQrTokenRepository(PreviewDataStore store) : IQrTokenRepository
{
    public Task<QrToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default)
        => Task.FromResult(store.QrTokens.FirstOrDefault(q => q.TokenHash == tokenHash));

    public Task AddAsync(QrToken token, CancellationToken ct = default)
    {
        store.QrTokens.Add(token);
        return Task.CompletedTask;
    }
}

public sealed class PreviewWeighSessionRepository(PreviewDataStore store) : IWeighSessionRepository
{
    public Task<WeighSession?> GetByIdAsync(Guid sessionId, CancellationToken ct = default)
        => Task.FromResult(store.WeighSessions.FirstOrDefault(s => s.Id == sessionId));

    public Task<WeighSession?> GetOwningSessionAsync(Guid weighbridgeId, CancellationToken ct = default)
        => Task.FromResult(store.WeighSessions.FirstOrDefault(
            s => s.WeighbridgeId == weighbridgeId && s.Status == WeighSessionStatus.Open));

    public Task<WeighEvent?> GetEffectiveEventAsync(
        Guid tripId, WeightRole role, int segmentIndex, CancellationToken ct = default)
        => Task.FromResult(store.WeighEvents
            .Where(e => e.TripId == tripId && e.Role == role && e.IsEffective)
            .OrderByDescending(e => e.Revision)
            .FirstOrDefault());

    public Task AddAsync(WeighSession session, CancellationToken ct = default)
    {
        store.WeighSessions.Add(session);
        return Task.CompletedTask;
    }

    public Task AddEventAsync(WeighEvent weighEvent, CancellationToken ct = default)
    {
        store.WeighEvents.Add(weighEvent);
        return Task.CompletedTask;
    }
}

public sealed class PreviewWorkflowProfileRepository(PreviewDataStore store) : IWorkflowProfileRepository
{
    public Task<WorkflowProfile?> GetEffectiveAsync(Guid profileId, int version, CancellationToken ct = default)
        => Task.FromResult(store.Profiles.FirstOrDefault(
            p => p.Id == profileId && p.ProfileVersion == version));

    public Task<IReadOnlyList<WorkflowProfile>> ListApprovedAsync(Guid siteId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<WorkflowProfile>>(
            [.. store.Profiles.Where(p => p.IsApprovedForProduction)]);
}

public sealed class PreviewWeighbridgeRepository(PreviewDataStore store) : IWeighbridgeRepository
{
    public Task<Weighbridge?> GetByIdAsync(Guid weighbridgeId, CancellationToken ct = default)
        => Task.FromResult(
            weighbridgeId == store.BridgeA.Id ? store.BridgeA
            : weighbridgeId == store.BridgeB.Id ? store.BridgeB
            : null);

    public Task<IReadOnlyList<Weighbridge>> ListBySiteAsync(Guid siteId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Weighbridge>>([store.BridgeA, store.BridgeB]);
}

/// <summary>Người dùng xem trước có mọi quyền. Vận hành thật lấy từ nguồn xác thực đã duyệt (D-08).</summary>
public sealed class PreviewCurrentUser(PreviewDataStore store) : ICurrentUser
{
    public string UserId => "preview.operator";
    public string DisplayName => "Người trực cân (xem trước)";

    public IReadOnlyCollection<string> Roles =>
        ["WEIGH_OPERATOR", "SUPERVISOR", "WAREHOUSE", "GATE", "IT", "RECONCILER"];

    public IReadOnlyCollection<Guid> AllowedSiteIds => [store.Site.Id, store.WarehouseId, Guid.Empty];

    public bool HasPermission(string permission, Guid siteId) => AllowedSiteIds.Contains(siteId);
}

public sealed class PreviewAuditLogger(PreviewDataStore store) : IAuditLogger
{
    public Task LogAsync(
        string actionType, Guid? tripId, string actor,
        string? beforeJson, string? afterJson, string? reason,
        string correlationId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        store.AuditLog.Add(new AuditLogEntry
        {
            ActionType = actionType,
            TripId = tripId,
            Actor = actor,
            BeforeJson = beforeJson,
            AfterJson = afterJson,
            Reason = reason,
            CorrelationId = correlationId,
            OccurredAt = now,
            CreatedAt = now,
            CreatedBy = actor
        });

        return Task.CompletedTask;
    }
}

public sealed class PreviewWeighPreconditionQuery(PreviewDataStore store) : IWeighPreconditionQuery
{
    public Task<WeighPreconditions> EvaluateAsync(
        Guid tripId, WeightRole role, int segmentIndex, CancellationToken ct = default)
    {
        var trip = store.Trips.FirstOrDefault(t => t.Id == tripId);
        var profile = store.Profiles.FirstOrDefault(p => p.Id == trip?.WorkflowProfileId);

        // Lượt cân thứ hai theo phân đoạn, không suy từ vai trò cứng.
        var isSecondSegment = segmentIndex > 0;

        var hasFirstSegmentWeight = store.WeighEvents.Any(
            e => e.TripId == tripId && e.IsEffective);

        var impurityDone = store.QualitySamples.Any(
            s => s.TripId == tripId
                && s.Type == QualitySampleType.Impurity
                && s.IsEffective
                && s.Status == QualityResultStatus.Valid) || store.ImpurityMeasured;

        var impurityRequired = profile?.ImpurityRequiredBeforeSecondWeight == true && isSecondSegment;

        var warnings = new List<string>();

        var ccsSample = store.QualitySamples.FirstOrDefault(
            s => s.TripId == tripId && s.Type == QualitySampleType.Ccs && s.IsEffective);

        var ccsPending = ccsSample?.Status == QualityResultStatus.Pending
            || (ccsSample is null && store.CcsPending && profile?.Code == "CANE_IN");

        if (ccsPending)
            warnings.Add("Chữ đường đang chờ kết quả — cảnh báo, không chặn cân (R-014).");

        return Task.FromResult(new WeighPreconditions
        {
            PrecedingWeightsSatisfied = !isSecondSegment || hasFirstSegmentWeight,
            ImpuritySatisfied = !impurityRequired || impurityDone,
            WarehouseSatisfied = true, // Preview mode: always satisfied
            PolicySatisfied = true,
            CcsPending = ccsPending,
            Warnings = warnings
        });
    }
}

public sealed class PreviewVehicleIdentityVerifier(PreviewDataStore store) : IVehicleIdentityVerifier
{
    public Task<VehicleIdentityResult> VerifyAsync(Guid tripId, Guid laneId, CancellationToken ct = default)
        => Task.FromResult(store.VehicleIdentityVerified
            ? new VehicleIdentityResult(true, "LANE_OPERATOR_CONFIRMATION", "preview.gate")
            : new VehicleIdentityResult(false, null, null));
}

public sealed class PreviewIntegrationStateQuery(PreviewDataStore store) : IIntegrationStateQuery
{
    public Task<bool> HasUnresolvedUnknownAsync(Guid tripId, CancellationToken ct = default)
        => Task.FromResult(store.IntegrationMessages.Any(
            m => m.TripId == tripId && m.Status == IntegrationMessageStatus.Unknown));
}

public sealed class PreviewEvidenceStore : IEvidenceStore
{
    public Task<IReadOnlyList<string>> PersistAsync(
        Guid tripId, IEnumerable<string> sourceRefs, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([.. sourceRefs]);
}

public sealed class PreviewTripNumberGenerator(PreviewDataStore store) : ITripNumberGenerator
{
    public Task<string> NextAsync(Guid siteId, string profileCode, CancellationToken ct = default)
        => Task.FromResult(store.NextTripNo());
}

/// <summary>CHỈ DÙNG XEM TRƯỚC. Cấp số thứ tự tăng đơn giản trong bộ nhớ.</summary>
public sealed class PreviewQueueSequenceGenerator : IQueueSequenceGenerator
{
    private int _counter;

    public Task<int> NextAsync(Guid siteId, VehicleType vehicleType, CancellationToken ct = default)
        => Task.FromResult(Interlocked.Increment(ref _counter));
}

/// <summary>CHỉ DÙNG XEM TRƯỚC. Camera luôn online, ảnh trả null.</summary>
public sealed class PreviewCameraService : ICameraService
{
    public Task<string?> CaptureSnapshotAsync(
        Guid gateId, string cameraPosition, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    public Task<CameraStatus> GetStatusAsync(
        Guid gateId, string cameraPosition, CancellationToken ct = default)
        => Task.FromResult(new CameraStatus(IsOnline: true, LastError: null));
}

public sealed class PreviewQualitySampleRepository(PreviewDataStore store) : IQualitySampleRepository
{
    public Task<QualitySample?> GetEffectiveAsync(
        Guid tripId, QualitySampleType type, CancellationToken ct = default)
        => Task.FromResult(store.QualitySamples
            .Where(s => s.TripId == tripId && s.Type == type && s.IsEffective)
            .OrderByDescending(s => s.Revision)
            .FirstOrDefault());

    public Task<IReadOnlyList<QualitySample>> ListByTripAsync(Guid tripId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<QualitySample>>(
            [.. store.QualitySamples.Where(s => s.TripId == tripId)]);

    public Task AddAsync(QualitySample sample, CancellationToken ct = default)
    {
        store.QualitySamples.Add(sample);
        return Task.CompletedTask;
    }
}

public sealed class PreviewMeasurementSessionRepository(PreviewDataStore store) : IMeasurementSessionRepository
{
    public Task<MeasurementSession?> GetByIdAsync(Guid sessionId, CancellationToken ct = default)
        => Task.FromResult(store.MeasurementSessions.FirstOrDefault(s => s.Id == sessionId));

    public Task<IReadOnlyList<MeasurementSession>> ListByTripAsync(
        Guid tripId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MeasurementSession>>(
            [.. store.MeasurementSessions.Where(s => s.TripId == tripId)]);

    public Task<MeasurementSession?> GetOpenSessionByDeviceAsync(
        Guid deviceId, CancellationToken ct = default)
        => Task.FromResult(store.MeasurementSessions.FirstOrDefault(
            s => s.Status == MeasurementSessionStatus.Open && s.DeviceIds.Contains(deviceId)));

    public Task AddAsync(MeasurementSession session, CancellationToken ct = default)
    {
        store.MeasurementSessions.Add(session);
        return Task.CompletedTask;
    }
}

public sealed class PreviewDeviceEventRepository(PreviewDataStore store) : IDeviceEventRepository
{
    public Task<DeviceEvent?> FindByDedupKeyAsync(
        Guid deviceId, string bootSessionId, long sequence, CancellationToken ct = default)
        => Task.FromResult(store.DeviceEvents.FirstOrDefault(
            e => e.DeviceId == deviceId && e.BootSessionId == bootSessionId && e.Sequence == sequence));

    public Task<IReadOnlyList<DeviceEvent>> ListBySessionAsync(
        Guid measurementSessionId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DeviceEvent>>(
            [.. store.DeviceEvents
                .Where(e => e.MeasurementSessionId == measurementSessionId)
                .OrderBy(e => e.Sequence)]);

    public Task AddAsync(DeviceEvent deviceEvent, CancellationToken ct = default)
    {
        store.DeviceEvents.Add(deviceEvent);
        return Task.CompletedTask;
    }
}

public sealed class PreviewReconciliationRepository(PreviewDataStore store) : IReconciliationRepository
{
    public Task<Reconciliation?> GetEffectiveAsync(Guid tripId, CancellationToken ct = default)
        => Task.FromResult(store.Reconciliations
            .Where(r => r.TripId == tripId && r.IsEffective)
            .OrderByDescending(r => r.Revision)
            .FirstOrDefault());

    public Task AddAsync(Reconciliation reconciliation, CancellationToken ct = default)
    {
        store.Reconciliations.Add(reconciliation);
        return Task.CompletedTask;
    }
}

public sealed class PreviewAllocationRepository(PreviewDataStore store) : IAllocationRepository
{
    public Task<IReadOnlyList<Allocation>> ListByTripAsync(Guid tripId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Allocation>>(
            [.. store.Allocations.Where(a => a.TripId == tripId && a.DeletedAt == null)]);

    public Task ReplaceForTripAsync(
        Guid tripId, IEnumerable<Allocation> allocations, CancellationToken ct = default)
    {
        foreach (var old in store.Allocations.Where(a => a.TripId == tripId && a.DeletedAt == null))
            old.DeletedAt = DateTimeOffset.UtcNow;

        store.Allocations.AddRange(allocations);
        return Task.CompletedTask;
    }
}

public sealed class PreviewIntegrationMessageRepository(PreviewDataStore store) : IIntegrationMessageRepository
{
    public Task<IntegrationMessage?> FindByBusinessKeyAsync(
        string businessKey, CancellationToken ct = default)
        => Task.FromResult(store.IntegrationMessages.FirstOrDefault(m => m.BusinessKey == businessKey));

    public Task<IReadOnlyList<IntegrationMessage>> ListByTripAsync(
        Guid tripId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<IntegrationMessage>>(
            [.. store.IntegrationMessages.Where(m => m.TripId == tripId)]);

    public Task<IReadOnlyList<IntegrationMessage>> ListRequiringAttentionAsync(
        Guid siteId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<IntegrationMessage>>(
            [.. store.IntegrationMessages.Where(m =>
                m.Status is IntegrationMessageStatus.Unknown
                    or IntegrationMessageStatus.Failed
                    or IntegrationMessageStatus.Held)]);

    public Task AddAsync(IntegrationMessage message, CancellationToken ct = default)
    {
        store.IntegrationMessages.Add(message);
        return Task.CompletedTask;
    }
}

public sealed class PreviewGateRepository(PreviewDataStore store) : IGateRepository
{
    public Task<GateCommand?> GetActiveCommandAsync(Guid tripId, CancellationToken ct = default)
        => Task.FromResult(store.GateCommands
            .Where(c => c.TripId == tripId && c.State
                is GateCommandState.Requested or GateCommandState.Acknowledged or GateCommandState.Unknown)
            .OrderByDescending(c => c.IssuedAt)
            .FirstOrDefault());

    public Task<IReadOnlyList<GateCommand>> ListUnknownCommandsAsync(
        Guid siteId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<GateCommand>>(
            [.. store.GateCommands.Where(c => c.State == GateCommandState.Unknown)]);

    public Task AddDecisionAsync(GateDecision decision, CancellationToken ct = default)
    {
        store.GateDecisions.Add(decision);
        return Task.CompletedTask;
    }

    public Task AddCommandAsync(GateCommand command, CancellationToken ct = default)
    {
        store.GateCommands.Add(command);
        return Task.CompletedTask;
    }
}

public sealed class PreviewExternalDocumentRepository(PreviewDataStore store) : IExternalDocumentRepository
{
    public Task<ExternalDocument?> GetByReferenceAsync(
        string sourceSystem, string referenceNo, CancellationToken ct = default)
        => Task.FromResult(store.Documents.FirstOrDefault(
            d => d.SourceSystem == sourceSystem && d.ReferenceNo == referenceNo));

    public Task<IReadOnlyList<TripDocumentLine>> ListLinesByTripAsync(
        Guid tripId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TripDocumentLine>>(
            [.. store.DocumentLines.Where(l => l.TripId == tripId)]);

    public Task AddAsync(ExternalDocument document, CancellationToken ct = default)
    {
        store.Documents.Add(document);
        return Task.CompletedTask;
    }

    public Task AddLineAsync(TripDocumentLine line, CancellationToken ct = default)
    {
        store.DocumentLines.Add(line);
        return Task.CompletedTask;
    }
}

public sealed class PreviewBomSnapshotRepository(PreviewDataStore store) : IBomSnapshotRepository
{
    public Task<BomSnapshot?> GetEffectiveAsync(
        string sourceSystem, string itemCode, DateTimeOffset at, CancellationToken ct = default)
        => Task.FromResult(store.BomSnapshots
            .Where(s => s.SourceSystem == sourceSystem && s.ItemCode == itemCode)
            .OrderByDescending(s => s.EffectiveAt)
            .FirstOrDefault());

    public Task AddAsync(BomSnapshot snapshot, CancellationToken ct = default)
    {
        store.BomSnapshots.Add(snapshot);
        return Task.CompletedTask;
    }
}

public sealed class PreviewApprovalRepository(PreviewDataStore store) : IApprovalRepository
{
    public Task<Approval?> GetByIdAsync(Guid approvalId, CancellationToken ct = default)
        => Task.FromResult(store.Approvals.FirstOrDefault(a => a.Id == approvalId));

    public Task<IReadOnlyList<Approval>> ListPendingAsync(Guid siteId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Approval>>(
            [.. store.Approvals.Where(a => a.ApprovedAt == null)]);

    public Task AddAsync(Approval approval, CancellationToken ct = default)
    {
        store.Approvals.Add(approval);
        return Task.CompletedTask;
    }
}

public sealed class PreviewIncidentRepository(PreviewDataStore store) : IIncidentRepository
{
    public Task<Incident?> GetOpenByTripAsync(Guid tripId, CancellationToken ct = default)
        => Task.FromResult(store.Incidents
            .Where(i => i.TripId == tripId && i.ResolvedAt == null)
            .OrderByDescending(i => i.OpenedAt)
            .FirstOrDefault());

    public Task<IReadOnlyList<Incident>> ListOpenAsync(Guid siteId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Incident>>(
            [.. store.Incidents.Where(i => i.ResolvedAt == null)]);

    public Task AddAsync(Incident incident, CancellationToken ct = default)
    {
        store.Incidents.Add(incident);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Chính sách dung sai. Mặc định trả policy rỗng để engine kết luận Pending,
/// phản ánh đúng việc D-06 chưa được duyệt.
/// </summary>
public sealed class PreviewTolerancePolicyProvider(PreviewDataStore store) : ITolerancePolicyProvider
{
    public Task<TolerancePolicy> GetAsync(
        Guid profileId, int profileVersion, CancellationToken ct = default)
        => Task.FromResult(store.PretendToleranceApproved
            ? new TolerancePolicy
            {
                PolicyId = "PREVIEW-TOL-01",
                PolicyVersion = 1,
                ScaleMarginKg = 20m,
                VehicleVsIotToleranceKg = 50m,
                BagCountTolerance = 0
            }
            : new TolerancePolicy());
}

/// <summary>Dựng payload giả. ContractVerified phản ánh trạng thái D-02.</summary>
public sealed class PreviewIntegrationPayloadBuilder(PreviewDataStore store) : IIntegrationPayloadBuilder
{
    public Task<IntegrationPayload> BuildAsync(
        Guid tripId, string operation, string? shipmentNo, CancellationToken ct = default)
        => Task.FromResult(new IntegrationPayload(
            PayloadRef: $"preview://payload/{tripId:N}/{operation}",
            PayloadHash: Guid.NewGuid().ToString("N"),
            InterfaceReference: null,
            ContractVerified: store.PretendContractVerified,
            IdempotencySupported: false));
}

/// <summary>Đọc payload sự kiện đã sinh bởi simulator.</summary>
public sealed class PreviewDeviceEventPayloadReader : IDeviceEventPayloadReader
{
    public IReadOnlyList<IotReading> ReadIotReadings(IEnumerable<DeviceEvent> events)
        => [.. events
            .Where(e => e.EventType == "IOT_MASS_READING")
            .Select(e => new IotReading(
                e.EventId,
                e.Sequence,
                ParseDecimal(e.PayloadJson, "value_kg"),
                ParseString(e.PayloadJson, "counter_epoch"),
                e.OccurredAt))];

    public IReadOnlyList<(string PassageId, bool IsForward)> ReadBagPassages(
        IEnumerable<DeviceEvent> events)
        => [.. events
            .Where(e => e.EventType == "BAG_PASSAGE")
            .Select(e => (
                PassageId: ParseString(e.PayloadJson, "passage_id") ?? e.EventId,
                IsForward: ParseString(e.PayloadJson, "direction") == "FORWARD"))];

    private static decimal ParseDecimal(string json, string key)
    {
        var raw = ParseString(json, key);
        return decimal.TryParse(raw, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0m;
    }

    private static string? ParseString(string json, string key)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty(key, out var element)
            ? element.ValueKind == System.Text.Json.JsonValueKind.Null ? null : element.ToString()
            : null;
    }
}

/// <summary>Bộ điều khiển barie giả lập. Không gửi lệnh tới thiết bị thật.</summary>
public sealed class PreviewBarrierControllerGateway(PreviewDataStore store) : IBarrierControllerGateway
{
    public Task<BarrierControllerState> GetStateAsync(Guid gateId, CancellationToken ct = default)
        => Task.FromResult(new BarrierControllerState(
            Ready: store.BarrierControllerReady,
            SafetyConditionsMet: store.BarrierSafetyOk,
            GateMatchesLane: true));

    public Task RequestOpenAsync(
        Guid gateId, string commandId, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        var command = store.GateCommands.FirstOrDefault(c => c.CommandId == commandId);
        if (command is not null)
            command.State = GateCommandState.Acknowledged;

        return Task.CompletedTask;
    }
}

/// <summary>
/// Bối cảnh làm việc giả lập. Chọn phân đoạn theo dữ liệu đã có.
/// Vận hành thật do thao tác quét QR quyết định.
/// </summary>
public sealed class PreviewWeighWorkspaceProvider(PreviewDataStore store) : IWeighWorkspaceProvider
{
    public Task<WeighWorkspace?> GetCurrentAsync(CancellationToken ct = default)
    {
        var trip = store.ActiveTrip;
        var profile = store.Profiles.First(p => p.Id == trip.WorkflowProfileId);

        var committedCount = store.WeighEvents.Count(e => e.TripId == trip.Id && e.IsEffective);
        var segmentIndex = Math.Min(committedCount, profile.WeightRoleSequence.Count - 1);

        var checkpoint = segmentIndex == 0 ? CheckpointType.FirstWeight : CheckpointType.SecondWeight;

        return Task.FromResult<WeighWorkspace?>(new WeighWorkspace(
            TripId: trip.Id,
            TripVersion: trip.Version,
            TripNo: trip.TripNo,
            ReferenceNo: trip.ReferenceTripNo,
            Plate: trip.TractorPlate,
            WeighbridgeId: store.BridgeA.Id,
            WeighbridgeCode: store.BridgeA.Code,
            LaneId: store.Lane.Id,
            Checkpoint: checkpoint,
            SegmentIndex: segmentIndex));
    }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>CHỈ DÙNG XEM TRƯỚC. Tra cứu FRM luôn trả danh sách rỗng.</summary>
public sealed class PreviewFrmPlanLookup : IFrmPlanLookup
{
    public Task<PlanLookupResult> LookupAsync(
        Guid siteId, string plate, DateTimeOffset operatingDate, CancellationToken ct = default)
        => Task.FromResult(new PlanLookupResult(true, [], null));

    public Task<IntegrationSendResult> NotifyCancellationAsync(
        Guid siteId, Guid tripId, string? referenceTripNo, string reason,
        string correlationId, CancellationToken ct = default)
        => Task.FromResult(new IntegrationSendResult(true, IntegrationMessageStatus.Acknowledged, null));
}

/// <summary>
/// CHỈ DÙNG XEM TRƯỚC. Quét QR giả lập: decode token → tra cứu Trip trong bộ nhớ.
/// Hỗ trợ kịch bản quét bất kỳ nội dung → trả trip đang Active trong preview store.
/// </summary>
public sealed class PreviewQrScanHandler(PreviewDataStore store, IClock clock) : IQrScanHandler
{
    public Task<QrScanResult> DecodeAndLookupAsync(
        string qrContent, Guid siteId, CancellationToken ct = default)
    {
        // Thử tra cứu QrToken bằng nội dung giả lập.
        var token = store.QrTokens.FirstOrDefault(q =>
            q.TokenHash.StartsWith(qrContent, StringComparison.OrdinalIgnoreCase))
            ?? store.QrTokens.FirstOrDefault(q => q.Status == QrTokenStatus.Issued);

        if (token is null)
            return Task.FromResult(new QrScanResult(
                false, null, null, null, null, null, null, null, null, null,
                null, null, null, null, null, null, null, null, null, null, null, "QR_NOT_FOUND",
                "Không tìm thấy mã QR trong dữ liệu xem trước."));

        if (token.Status == QrTokenStatus.Revoked)
            return Task.FromResult(new QrScanResult(
                false, null, null, null, null, null, null, null, null, null,
                null, null, null, null, null, QrTokenType.Trip, null, null, null, null, null, "QR_REVOKED",
                "Mã QR đã bị thu hồi."));

        if (token.ExpiresAt < clock.UtcNow)
            return Task.FromResult(new QrScanResult(
                false, null, null, null, null, null, null, null, null, null,
                null, null, null, null, null, QrTokenType.Trip, null, null, null, null, null, "QR_EXPIRED",
                "Mã QR đã hết hạn."));

        if (token.Type == QrTokenType.Temp)
            return Task.FromResult(new QrScanResult(
                false, token.TripId, null, null, null, null, null, null, null, null,
                null, null, null, null, null, QrTokenType.Temp, null, null, null, null, null, "QR_TEMP",
                "Xe chưa đăng ký kế hoạch, cần quay lại cổng để lấy QR_chuyến."));

        var trip = store.Trips.FirstOrDefault(t => t.Id == token.TripId);
        if (trip is null)
            return Task.FromResult(new QrScanResult(
                false, null, null, null, null, null, null, null, null, null,
                null, null, null, null, null, null, null, null, null, null, null, "TRIP_NOT_FOUND",
                "Chuyến không tồn tại."));

        var profile = store.Profiles.FirstOrDefault(p => p.Id == trip.WorkflowProfileId);

        return Task.FromResult(new QrScanResult(
            IsSuccess: true,
            TripId: trip.Id,
            TripVersion: trip.Version,
            TripNo: trip.TripNo,
            Plate: trip.TractorPlate,
            TrailerPlate: trip.TrailerPlate,
            CustomerOrOwner: trip.SugarcaneOwner ?? "Chủ mía demo",
            Commodity: profile?.Code == "CANE_IN" ? "Mía nguyên liệu" : "Đường tinh",
            Warehouse: "Kho chính (demo)",
            ShipmentNo: trip.ReferenceTripNo,
            SugarcaneType: trip.SugarcaneType ?? "Mía tươi",
            SugarcaneVariety: trip.SugarcaneVariety ?? "ROC-16",
            SugarcaneOwner: trip.SugarcaneOwner ?? "Nguyễn Văn A (demo)",
            AssignedWeighbridgeId: store.BridgeA.Id,
            AssignedWarehouseId: store.WarehouseId,
            QrType: QrTokenType.Trip,
            SourceSystem: trip.SourceSystem,
            DriverRef: trip.DriverRef,
            FrmLenhDonId: trip.FrmLenhDonId,
            FrmMaLenhDon: trip.FrmMaLenhDon,
            FrmLenhDonBu: trip.FrmLenhDonBu,
            ErrorCode: null,
            ErrorMessage: null));
    }
}

/// <summary>
/// CHỈ DÙNG XEM TRƯỚC. Đồng bộ FRM giả lập: luôn trả thành công với dữ liệu mẫu.
/// </summary>
public sealed class PreviewFrmSyncService(PreviewDataStore store) : IFrmSyncService
{
    public Task<FrmSyncResult> SyncTripInfoAsync(
        Guid tripId, Guid siteId, CancellationToken ct = default)
    {
        var trip = store.Trips.FirstOrDefault(t => t.Id == tripId);
        if (trip is null)
            return Task.FromResult(new FrmSyncResult(
                false, null, null, null, null, null, null, "Chuyến không tồn tại."));

        return Task.FromResult(new FrmSyncResult(
            IsSuccess: true,
            SugarcaneType: trip.SugarcaneType ?? "Mía tươi",
            SugarcaneVariety: trip.SugarcaneVariety ?? "ROC-16",
            SugarcaneOwner: trip.SugarcaneOwner ?? "Nguyễn Văn A (demo)",
            HarvestMethod: "Thủ công",
            Station: "Trạm 01 (demo)",
            CropSeason: $"{DateTime.UtcNow.Year}-{DateTime.UtcNow.Year + 1}",
            ErrorMessage: null));
    }
}

/// <summary>
/// CHỈ DÙNG XEM TRƯỚC. In phiếu cân giả lập: tạo WeighTicket trong bộ nhớ, không gửi máy in thật.
/// </summary>
public sealed class PreviewWeighTicketPrintService(
    PreviewDataStore store,
    IClock clock) : IWeighTicketPrintService
{
    private long _ticketCounter = 1;

    public Task<PrintTicketResult> PrintAsync(
        Guid tripId, PrinterConfig config, bool isReprint, CancellationToken ct = default)
    {
        var trip = store.Trips.FirstOrDefault(t => t.Id == tripId);
        if (trip is null)
            return Task.FromResult(new PrintTicketResult(false, null, "Chuyến không tồn tại."));

        var ticketNumber = trip.TicketNumber
            ?? $"PC-{store.Site.Code}-{clock.UtcNow:yyyyMMdd}-{_ticketCounter++:D4}";

        if (trip.TicketNumber is null)
            trip.TicketNumber = ticketNumber;

        // Tìm phiếu đã có hoặc tạo mới.
        var existing = store.WeighTickets.FirstOrDefault(t => t.TripId == tripId && !t.IsReprint);
        if (existing is not null && isReprint)
        {
            existing.PrintCount++;
        }

        store.WeighTickets.Add(new WeighTicket
        {
            TripId = tripId,
            TicketNumber = ticketNumber,
            TemplateCode = config.TemplateCode,
            PrintedAt = clock.UtcNow,
            PrintedBy = "preview.operator",
            IsReprint = isReprint,
            PrintCount = isReprint ? (existing?.PrintCount ?? 2) : 1,
            SnapshotJson = $"{{\"tripNo\":\"{trip.TripNo}\",\"plate\":\"{trip.TractorPlate}\",\"preview\":true}}",
            CreatedAt = clock.UtcNow,
            CreatedBy = "preview.operator"
        });

        return Task.FromResult(new PrintTicketResult(true, ticketNumber, null));
    }

    public Task<IReadOnlyList<PrinterInfo>> GetAvailablePrintersAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PrinterInfo>>([
            new PrinterInfo("PDF (Xem trước)", true),
            new PrinterInfo("Máy in A4 (demo)", true),
            new PrinterInfo("Máy in phiếu cân (demo)", true)
        ]);
}

/// <summary>
/// CHỈ DÙNG XEM TRƯỚC. Giám sát thiết bị giả lập: tất cả thiết bị online, đầu cân Ok.
/// </summary>
public sealed class PreviewDeviceStatusMonitor(PreviewDataStore store, IClock clock) : IDeviceStatusMonitor
{
    public Task<IReadOnlyList<DeviceStatus>> GetStatusAsync(
        Guid siteId, Guid? laneId, CancellationToken ct = default)
    {
        var devices = store.Devices
            .Where(d => laneId == null || d.LaneId == laneId)
            .Select(d =>
            {
                var isScale = d.DeviceClass is "SCALE" or "IOT_MASS";
                var lastSignal = clock.UtcNow.AddSeconds(-1); // Giả lập tín hiệu 1 giây trước.

                return new DeviceStatus(
                    DeviceId: d.Id,
                    DeviceCode: d.Code,
                    DeviceClass: d.DeviceClass,
                    IsOnline: d.IsEnabled,
                    ScaleStatus: isScale ? ScaleDeviceStatus.Ok : null,
                    LastSignalAt: lastSignal,
                    SignalAge: clock.UtcNow - lastSignal,
                    FaultDescription: null);
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<DeviceStatus>>(devices);
    }
}

/// <summary>CHỈ DÙNG XEM TRƯỚC. Repository phiếu cân trong bộ nhớ.</summary>
public sealed class PreviewWeighTicketRepository(PreviewDataStore store) : IWeighTicketRepository
{
    public Task<WeighTicket?> GetByTripAsync(Guid tripId, CancellationToken ct = default)
        => Task.FromResult(store.WeighTickets
            .Where(t => t.TripId == tripId && !t.IsReprint)
            .OrderByDescending(t => t.PrintedAt)
            .FirstOrDefault());

    public Task AddAsync(WeighTicket ticket, CancellationToken ct = default)
    {
        store.WeighTickets.Add(ticket);
        return Task.CompletedTask;
    }

    public Task IncrementPrintCountAsync(Guid ticketId, CancellationToken ct = default)
    {
        var ticket = store.WeighTickets.FirstOrDefault(t => t.Id == ticketId);
        if (ticket is not null)
            ticket.PrintCount++;
        return Task.CompletedTask;
    }
}
