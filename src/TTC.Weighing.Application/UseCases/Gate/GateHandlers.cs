using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Domain.Rules;

namespace TTC.Weighing.Application.UseCases.Gate;

public sealed record EvaluateGateCommand(
    Guid TripId,
    Guid GateId,
    Guid LaneId,
    GateDirection Direction,
    int ExpectedTripVersion,
    string CorrelationId);

public sealed record EvaluateGateResult(
    bool Allowed,
    Guid? DecisionId,
    string? CommandId,
    DateTimeOffset? CommandExpiresAt,
    GateDecisionResult Decision);

/// <summary>
/// R-006 / R-039 — Đánh giá quyền qua cổng rồi phát lệnh mở có thời hạn.
/// Đã gửi lệnh KHÔNG nghĩa là xe đã đi qua; xác nhận qua cổng là bước riêng.
/// </summary>
public sealed class EvaluateGateHandler(
    ITripRepository trips,
    IGateRepository gates,
    IWorkflowProfileRepository profiles,
    IVehicleIdentityVerifier identityVerifier,
    IBarrierControllerGateway controller,
    IClock clock,
    ICurrentUser user,
    IUnitOfWork uow,
    IAuditLogger audit)
{
    public const string RequiredPermission = "gate.evaluate";

    /// <summary>Thời hạn lệnh mở. Lệnh hết hạn không được phát lại sau khôi phục.</summary>
    private static readonly TimeSpan CommandTtl = TimeSpan.FromSeconds(30);

    public async Task<EvaluateGateResult> HandleAsync(
        EvaluateGateCommand cmd, CancellationToken ct = default)
    {
        var trip = await trips.GetByIdAsync(cmd.TripId, ct);
        if (trip is null)
            return Denied("GATE.0", "Chuyến không tồn tại.");

        if (!user.HasPermission(RequiredPermission, trip.SiteId))
            return Denied("PERM", "Không có quyền đánh giá qua cổng.");

        if (trip.Version != cmd.ExpectedTripVersion)
            return Denied("GATE.0", "Dữ liệu chuyến đã thay đổi. Tải lại rồi đánh giá lại.");

        var profile = await profiles.GetEffectiveAsync(
            trip.WorkflowProfileId, trip.WorkflowProfileVersion, ct);
        if (profile is null)
            return Denied("GATE.0", "Không tìm thấy hồ sơ nghiệp vụ của chuyến.");

        var activeQr = trip.QrTokens.FirstOrDefault(q => q.IsValidAt(clock.UtcNow));
        var identity = await identityVerifier.VerifyAsync(cmd.TripId, cmd.LaneId, ct);
        var controllerState = await controller.GetStateAsync(cmd.GateId, ct);

        var pendingCommand = await gates.GetActiveCommandAsync(cmd.TripId, ct);
        var hasPendingUnknown = pendingCommand is not null &&
            pendingCommand.State is GateCommandState.Unknown or GateCommandState.Requested &&
            !pendingCommand.IsExpiredAt(clock.UtcNow);

        var context = new GateDecisionContext
        {
            TripLifecycle = trip.Lifecycle,
            ExecutionStatus = trip.ExecutionStatus,
            QrType = activeQr?.Type ?? QrTokenType.Temp,
            QrStatus = activeQr?.Status ?? QrTokenStatus.Expired,
            QrMatchesTrip = activeQr?.TripId == trip.Id || activeQr?.VehicleId == trip.VehicleId,
            VehicleIdentityVerified = identity.Verified,
            RequestedDirection = cmd.Direction,
            GateMatchesConfiguration = controllerState.GateMatchesLane,
            StageCheckpointsSatisfied = AreStageCheckpointsSatisfied(trip, profile, cmd.Direction),
            ControllerReady = controllerState.Ready,
            LocalSafetyConditionsMet = controllerState.SafetyConditionsMet,
            HasPendingUnknownCommand = hasPendingUnknown
        };

        var decision = GateDecisionRules.Evaluate(context);

        var record = new GateDecision
        {
            TripId = cmd.TripId,
            GateId = cmd.GateId,
            LaneId = cmd.LaneId,
            Direction = cmd.Direction,
            IdentityEvidence = identity.Method,
            IdentityVerifiedBy = identity.VerifiedBy,
            ConditionsJson = SerializeChecks(decision),
            Allowed = decision.Allowed,
            DenyReason = decision.Allowed ? null : decision.DenyReason,
            DecidedAt = clock.UtcNow,
            CreatedAt = clock.UtcNow,
            CreatedBy = user.UserId
        };

        await gates.AddDecisionAsync(record, ct);

        GateCommand? command = null;
        if (decision.Allowed)
        {
            command = new GateCommand
            {
                GateDecisionId = record.Id,
                TripId = cmd.TripId,
                CommandId = Guid.NewGuid().ToString("N"),
                IssuedAt = clock.UtcNow,
                ExpiresAt = clock.UtcNow.Add(CommandTtl),
                State = GateCommandState.Requested,
                CreatedAt = clock.UtcNow,
                CreatedBy = user.UserId
            };

            await gates.AddCommandAsync(command, ct);
        }

        await audit.LogAsync(decision.Allowed ? "GATE_OPEN_REQUESTED" : "GATE_DENIED",
            cmd.TripId, user.UserId, null,
            decision.Allowed
                ? $"{{\"commandId\":\"{command!.CommandId}\",\"direction\":\"{cmd.Direction}\"}}"
                : $"{{\"denyReason\":{System.Text.Json.JsonSerializer.Serialize(decision.DenyReason)}}}",
            null, cmd.CorrelationId, ct);

        await uow.SaveChangesAsync(ct);

        // Lệnh chỉ được gửi xuống thiết bị sau khi đã ghi nhận, để không mất dấu vết.
        if (command is not null)
            await controller.RequestOpenAsync(cmd.GateId, command.CommandId, command.ExpiresAt, ct);

        return new(decision.Allowed, record.Id, command?.CommandId, command?.ExpiresAt, decision);
    }

    /// <summary>
    /// Kiểm tra đủ checkpoint theo giai đoạn. Ra cổng cần các bước cân/đối soát bắt buộc đã xong.
    /// </summary>
    private static bool AreStageCheckpointsSatisfied(
        Trip trip, Domain.Entities.WorkflowProfile profile, GateDirection direction)
    {
        if (!profile.GateCheckpointsRequired)
            return true;

        if (direction == GateDirection.In)
        {
            // Vào cổng chỉ cần đã đăng tài hợp lệ (bao gồm TempRegistered cho QR tạm).
            return trip.Lifecycle is TripLifecycle.TempRegistered
                or TripLifecycle.Registered
                or TripLifecycle.InProgress;
        }

        // Ra cổng cần đã hoàn tất các bước bắt buộc của profile.
        return trip.Lifecycle is TripLifecycle.WeighingCompleted
            or TripLifecycle.Reconciled
            or TripLifecycle.Posted
            or TripLifecycle.Closed;
    }

    private static string SerializeChecks(GateDecisionResult decision)
        => "[" + string.Join(",", decision.Checks.Select(c =>
            $"{{\"code\":\"{c.Code}\",\"passed\":{c.Passed.ToString().ToLowerInvariant()}}}")) + "]";

    private static EvaluateGateResult Denied(string code, string detail)
    {
        var decision = new GateDecisionResult(false,
            [new ConditionCheck(code, "Điều kiện qua cổng", false, detail)]);
        return new(false, null, null, null, decision);
    }
}

public sealed record ConfirmVehiclePassageCommand(
    Guid TripId,
    string CommandId,
    string? PassageEventRef,
    string CorrelationId);

public sealed record ConfirmPassageResult(bool IsSuccess, string? FailureReason);

/// <summary>
/// Xác nhận xe đã qua cổng. Phải dựa vào sự kiện qua cổng hoặc người có quyền,
/// KHÔNG dựa riêng vào ACK mở barie (02_Quy_tac mục 8).
/// </summary>
public sealed class ConfirmVehiclePassageHandler(
    ITripRepository trips,
    IGateRepository gates,
    IUnitOfWork uow,
    IClock clock,
    ICurrentUser user,
    IAuditLogger audit)
{
    public const string RequiredPermission = "gate.passage.confirm";

    public async Task<ConfirmPassageResult> HandleAsync(
        ConfirmVehiclePassageCommand cmd, CancellationToken ct = default)
    {
        var trip = await trips.GetByIdAsync(cmd.TripId, ct);
        if (trip is null)
            return new(false, "Chuyến không tồn tại.");

        if (!user.HasPermission(RequiredPermission, trip.SiteId))
            return new(false, "Không có quyền xác nhận xe qua cổng.");

        var command = await gates.GetActiveCommandAsync(cmd.TripId, ct);
        if (command is null || command.CommandId != cmd.CommandId)
            return new(false, "Không tìm thấy lệnh mở tương ứng.");

        command.State = GateCommandState.Executed;
        command.PassageEventRef = cmd.PassageEventRef;
        command.PassageConfirmedAt = clock.UtcNow;
        command.PassageConfirmedBy = user.UserId;
        command.UpdatedAt = clock.UtcNow;
        command.UpdatedBy = user.UserId;

        await audit.LogAsync("GATE_PASSAGE_CONFIRMED", cmd.TripId, user.UserId, null,
            $"{{\"commandId\":\"{cmd.CommandId}\",\"eventRef\":\"{cmd.PassageEventRef ?? "manual"}\"}}",
            null, cmd.CorrelationId, ct);

        await uow.SaveChangesAsync(ct);

        return new(true, null);
    }
}

/// <summary>
/// Trạng thái bộ điều khiển barie. Cảm biến an toàn và dừng khẩn cấp thuộc thiết kế thiết bị.
/// </summary>
public sealed record BarrierControllerState(
    bool Ready,
    bool SafetyConditionsMet,
    bool GateMatchesLane);

public interface IBarrierControllerGateway
{
    Task<BarrierControllerState> GetStateAsync(Guid gateId, CancellationToken ct = default);

    /// <summary>Gửi yêu cầu mở. Không phát lại lệnh khi kết quả chưa rõ.</summary>
    Task RequestOpenAsync(
        Guid gateId, string commandId, DateTimeOffset expiresAt, CancellationToken ct = default);
}
