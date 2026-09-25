using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Domain.Rules;

namespace TTC.Weighing.Application.UseCases.Weighing;

/// <param name="IdempotencyKey">Khóa chống lặp. Bấm lại cùng khóa không tạo lượt cân mới (R-024).</param>
/// <param name="DryRun">
/// Chỉ đánh giá điều kiện để hiển thị, không ghi lượt cân và không sinh audit.
/// UI-03 dùng chế độ này để hiện từng điều kiện đạt/chưa đạt theo thời gian thực (04_Man_hinh mục 1).
/// </param>
public sealed record EvaluateAndCommitWeightCommand(
    Guid SessionId,
    Guid LaneId,
    string CorrelationId,
    string IdempotencyKey,
    bool DryRun = false);

public sealed record EvaluateAndCommitWeightResult(
    bool IsCommitted,
    Guid? WeighEventId,
    decimal? NormalizedKg,
    WeighCommitDecision Decision,
    IReadOnlyList<string> Warnings)
{
    public string BlockReason => Decision.BlockSummary;
}

/// <summary>
/// R-007 / B-001. Server đánh giá toàn bộ điều kiện rồi mới chốt cân.
/// UI chỉ hiển thị kết quả; không tự cộng trừ hay tự quyết định đã chốt.
/// </summary>
public sealed class EvaluateAndCommitWeightHandler(
    IWeighSessionRepository sessions,
    ITripRepository trips,
    IWorkflowProfileRepository profiles,
    IWeighbridgeRepository weighbridges,
    IScaleSignalGateway scaleGateway,
    IPositionEvidenceGateway positionGateway,
    IWeighPreconditionQuery preconditions,
    IVehicleIdentityVerifier identityVerifier,
    IIntegrationStateQuery integrationState,
    IEvidenceStore evidenceStore,
    IUnitOfWork uow,
    IClock clock,
    ICurrentUser user,
    IAuditLogger audit)
{
    public const string RequiredPermission = "weighing.weight.commit";

    public async Task<EvaluateAndCommitWeightResult> HandleAsync(
        EvaluateAndCommitWeightCommand cmd, CancellationToken ct = default)
    {
        var session = await sessions.GetByIdAsync(cmd.SessionId, ct)
            ?? throw new InvalidOperationException($"Phiên cân {cmd.SessionId} không tồn tại.");

        if (session.Status != WeighSessionStatus.Open)
            return Blocked("B-001.2", "Phiên cân không ở trạng thái mở.");

        var trip = await trips.GetByIdAsync(session.TripId, ct)
            ?? throw new InvalidOperationException($"Chuyến {session.TripId} không tồn tại.");

        if (!user.HasPermission(RequiredPermission, trip.SiteId))
            return Blocked("PERM", "Không có quyền chốt cân tại nhà máy này.");

        var profile = await profiles.GetEffectiveAsync(
            trip.WorkflowProfileId, trip.WorkflowProfileVersion, ct);
        if (profile is null || !profile.IsApprovedForProduction)
            return Blocked("PROFILE", "Profile không hiệu lực hoặc chưa được duyệt cho vận hành.");

        var bridge = await weighbridges.GetByIdAsync(session.WeighbridgeId, ct);
        if (bridge is null)
            return Blocked("B-001.2", "Bàn cân không tồn tại.");

        var signal = await scaleGateway.GetCurrentAsync(session.WeighbridgeId, ct);
        if (signal is null)
            return Blocked("B-001.5", "Không nhận được tín hiệu đầu cân.");

        var position = await positionGateway.GetLatestAsync(cmd.LaneId, ct);
        if (position is null)
            return Blocked("B-001.4", "Không nhận được bằng chứng AI vị trí.");

        var pre = await preconditions.EvaluateAsync(trip.Id, session.Role, session.SegmentIndex, ct);
        var identity = await identityVerifier.VerifyAsync(trip.Id, cmd.LaneId, ct);
        var hasUnknown = await integrationState.HasUnresolvedUnknownAsync(trip.Id, ct);

        var activeQr = trip.QrTokens.FirstOrDefault(q => q.IsValidAt(clock.UtcNow));

        var context = new WeighCommitContext
        {
            TripLifecycle = trip.Lifecycle,
            ExecutionStatus = trip.ExecutionStatus,
            QrType = activeQr?.Type ?? QrTokenType.Temp,
            QrStatus = activeQr?.Status ?? QrTokenStatus.Expired,
            QrMatchesTrip = activeQr?.TripId == trip.Id || activeQr?.VehicleId == trip.VehicleId,
            QrMatchesSite = activeQr?.SiteId == trip.SiteId,
            CheckpointAllowedByProfile = profile.Checkpoints.Contains(session.Checkpoint),

            DevicesMappedToLane = bridge.SiteId == trip.SiteId,
            SessionOwnsBridge = !string.IsNullOrEmpty(session.OwnershipToken),

            VehicleIdentityVerified = identity.Verified,
            IdentityVerificationMethod = identity.Method,

            PositionVerdict = position.Verdict,
            PositionConfidence = position.Confidence,
            PositionEvidenceAge = clock.UtcNow - position.OccurredAt,
            PositionRegionConfigValid = position.RegionConfigValid,

            ScaleStatus = signal.DeviceStatus,
            ScaleReadingAge = clock.UtcNow - signal.OccurredAt,
            WithinScaleCapacity = bridge.IsWithinCapacity(signal.NormalizedKg),
            SignalContinuous = signal.SignalContinuous,

            StableDuration = signal.StableDuration,
            ObservedDeltaKg = signal.ObservedDeltaKg,

            PrecedingWeightsSatisfied = pre.PrecedingWeightsSatisfied,
            ImpurityRequirementSatisfied = pre.ImpuritySatisfied,
            PolicyRequirementSatisfied = pre.PolicySatisfied,

            EvidencePersistable = true,
            NoPendingUnknownTransaction = !hasUnknown,

            Thresholds = profile.Thresholds
        };

        var decision = WeighCommitRules.Evaluate(context);

        // Đánh giá thử chỉ trả kết quả để hiển thị, không ghi dữ liệu và không sinh audit.
        if (cmd.DryRun)
            return new EvaluateAndCommitWeightResult(false, null, null, decision, pre.Warnings);

        if (!decision.CanCommit)
        {
            // Lần chốt cân bị chặn cũng phải để lại dấu vết, kèm lý do cụ thể.
            await audit.LogAsync("WEIGH_COMMIT_BLOCKED", trip.Id, user.UserId,
                null, decision.BlockSummary, null, cmd.CorrelationId, ct);

            await uow.SaveChangesAsync(ct);

            return new EvaluateAndCommitWeightResult(false, null, null, decision, pre.Warnings);
        }

        // Lưu bằng chứng trước khi phát hành lượt cân (B-001.8).
        var evidenceRefs = await evidenceStore.PersistAsync(trip.Id, [position.EvidenceRef], ct);
        if (evidenceRefs.Count == 0)
            return Blocked("B-001.8", "Không lưu được bằng chứng, không phát hành lượt cân.");

        await using var transaction = await uow.BeginTransactionAsync(ct);

        var previous = await sessions.GetEffectiveEventAsync(
            trip.Id, session.Role, session.SegmentIndex, ct);

        var weighEvent = new WeighEvent
        {
            WeighSessionId = session.Id,
            TripId = trip.Id,
            Revision = (previous?.Revision ?? 0) + 1,
            SupersedesId = previous?.Id,
            IsEffective = true,
            Role = session.Role,
            RawWeight = signal.RawWeight,
            RawUom = signal.RawUom,
            NormalizedKg = signal.NormalizedKg,
            MeasuredAt = signal.OccurredAt,
            Source = WeighSource.Auto,
            DeviceId = signal.DeviceId,
            StableWindow = signal.StableDuration,
            PolicyVersion = profile.ProfileVersion,
            EvidenceRefs = [.. evidenceRefs],
            CreatedAt = clock.UtcNow,
            CreatedBy = user.UserId
        };

        // Bản cũ giữ nguyên, chỉ hết hiệu lực (03_Du_lieu mục 3).
        if (previous is not null)
            previous.IsEffective = false;

        await sessions.AddEventAsync(weighEvent, ct);

        session.Status = WeighSessionStatus.Committed;
        session.ClosedAt = clock.UtcNow;

        if (trip.Lifecycle is TripLifecycle.Registered or TripLifecycle.TempRegistered)
            trip.Lifecycle = TripLifecycle.InProgress;

        await audit.LogAsync("WEIGH_COMMITTED", trip.Id, user.UserId,
            beforeJson: previous is null ? null : $"{{\"revision\":{previous.Revision}}}",
            afterJson: $"{{\"revision\":{weighEvent.Revision},\"kg\":{weighEvent.NormalizedKg}}}",
            reason: null, correlationId: cmd.CorrelationId, ct: ct);

        await uow.SaveChangesAsync(ct);

        // Lượt cân, bằng chứng, trạng thái phiên và audit phải cùng vào hoặc cùng không vào.
        await transaction.CommitAsync(ct);

        // 02_Quy_tac 2.1: đủ lượt cân có hiệu lực thì chuyển WEIGHING_COMPLETED.
        // Đây chưa phải đạt đối soát, chỉ là đã có bộ số cân hoàn chỉnh.
        await AdvanceWhenAllWeightsCommittedAsync(trip, profile, ct);

        return new EvaluateAndCommitWeightResult(
            true, weighEvent.Id, weighEvent.NormalizedKg, decision, pre.Warnings);
    }

    /// <summary>
    /// Chuyển sang WEIGHING_COMPLETED khi mọi vai trò cân của hồ sơ đã có bản hiệu lực.
    /// Không suy ra từ số lượt cân cố định; lấy theo chuỗi vai trò khai báo trong hồ sơ (R-030).
    /// </summary>
    private async Task AdvanceWhenAllWeightsCommittedAsync(
        Domain.Entities.Trip trip, Domain.Entities.WorkflowProfile profile, CancellationToken ct)
    {
        if (trip.Lifecycle != TripLifecycle.InProgress)
            return;

        for (var segment = 0; segment < profile.WeightRoleSequence.Count; segment++)
        {
            var role = profile.WeightRoleSequence[segment];
            var committed = await sessions.GetEffectiveEventAsync(trip.Id, role, segment, ct);

            if (committed is null)
                return;
        }

        trip.Lifecycle = TripLifecycle.WeighingCompleted;
        trip.UpdatedAt = clock.UtcNow;
        trip.UpdatedBy = user.UserId;

        await uow.SaveChangesAsync(ct);
    }

    private static EvaluateAndCommitWeightResult Blocked(string code, string detail)
    {
        var decision = new WeighCommitDecision(false, [new ConditionCheck(code, "Điều kiện chốt cân", false, detail)]);
        return new EvaluateAndCommitWeightResult(false, null, null, decision, []);
    }
}
