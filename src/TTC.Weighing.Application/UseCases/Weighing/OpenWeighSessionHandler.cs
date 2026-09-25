using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Application.UseCases.Weighing;

public sealed record OpenWeighSessionCommand(
    Guid TripId,
    int ExpectedTripVersion,
    Guid WeighbridgeId,
    CheckpointType Checkpoint,
    int SegmentIndex,
    string CorrelationId);

/// <param name="StabilityWindow">
/// Cửa sổ ổn định của profile, để UI render tiến trình. null = chưa duyệt (D-04).
/// </param>
public sealed record OpenWeighSessionResult(
    bool IsSuccess,
    Guid? SessionId,
    WeightRole? Role,
    string? FailureReason,
    TimeSpan? StabilityWindow = null,
    string? ProfileCode = null);

/// <summary>
/// Mở phiên cân sở hữu độc quyền một bàn cân (R-011, B-003).
/// Vai trò cân do profile quyết định theo phân đoạn — không hard-code lần 1 là bì.
/// </summary>
public sealed class OpenWeighSessionHandler(
    ITripRepository trips,
    IWeighSessionRepository sessions,
    IWorkflowProfileRepository profiles,
    IWeighbridgeRepository weighbridges,
    IUnitOfWork uow,
    IClock clock,
    ICurrentUser user,
    IAuditLogger audit)
{
    public const string RequiredPermission = "weighing.session.open";

    public async Task<OpenWeighSessionResult> HandleAsync(
        OpenWeighSessionCommand cmd, CancellationToken ct = default)
    {
        var trip = await trips.GetForUpdateAsync(cmd.TripId, cmd.ExpectedTripVersion, ct);
        if (trip is null)
            return Fail("Chuyến không tồn tại hoặc đã thay đổi phiên bản. Tải lại dữ liệu.");

        if (!user.HasPermission(RequiredPermission, trip.SiteId))
            return Fail("Không có quyền mở phiên cân tại nhà máy này.");

        if (!trip.IsOperable)
            return Fail($"Chuyến không cho phép thao tác: {trip.Lifecycle} / {trip.ExecutionStatus}.");

        var bridge = await weighbridges.GetByIdAsync(cmd.WeighbridgeId, ct);
        if (bridge is null || bridge.IsOutOfService)
            return Fail("Bàn cân không tồn tại hoặc đang ngừng sử dụng.");

        if (bridge.SiteId != trip.SiteId)
            return Fail("Bàn cân không thuộc nhà máy của chuyến.");

        // B-003: một bàn chỉ có một phiên đang sở hữu.
        var owning = await sessions.GetOwningSessionAsync(cmd.WeighbridgeId, ct);
        if (owning is not null)
        {
            // Phiên cũ của CÙNG chuyến, CÙNG checkpoint — chưa commit nghĩa là lần trước bị chặn
            // (ví dụ chặn vì chưa tạp chất). Abandon để nhường chỗ cho lần thử mới.
            var isSameContext = owning.TripId == cmd.TripId
                && owning.Checkpoint == cmd.Checkpoint
                && owning.SegmentIndex == cmd.SegmentIndex
                && owning.Status == WeighSessionStatus.Open;

            if (isSameContext)
            {
                owning.Status = WeighSessionStatus.Abandoned;
                owning.ClosedAt = clock.UtcNow;
                owning.UpdatedAt = clock.UtcNow;
                owning.UpdatedBy = user.UserId;

                await audit.LogAsync("WEIGH_SESSION_ABANDONED", trip.Id, user.UserId,
                    $"{{\"sessionId\":\"{owning.Id}\",\"checkpoint\":\"{owning.Checkpoint}\"}}",
                    "{\"reason\":\"Phiên cũ chưa commit, được giải phóng khi mở phiên mới cho cùng bước.\"}",
                    null, cmd.CorrelationId, ct);

                await uow.SaveChangesAsync(ct);
            }
            else
            {
                return Fail($"Bàn cân đang bị phiên khác chiếm giữ (chuyến {owning.TripId}).");
            }
        }

        var profile = await profiles.GetEffectiveAsync(
            trip.WorkflowProfileId, trip.WorkflowProfileVersion, ct);
        if (profile is null)
            return Fail("Không tìm thấy profile hiệu lực của chuyến.");

        if (!profile.IsApprovedForProduction)
            return Fail("Profile chưa được duyệt cho vận hành.");

        if (!profile.Checkpoints.Contains(cmd.Checkpoint))
            return Fail($"Checkpoint {cmd.Checkpoint} không thuộc profile {profile.Code}.");

        var role = ResolveRole(profile, cmd.SegmentIndex);
        if (role is null)
            return Fail($"Profile {profile.Code} không khai báo vai trò cân cho phân đoạn {cmd.SegmentIndex}.");

        var session = new WeighSession
        {
            TripId = trip.Id,
            WeighbridgeId = cmd.WeighbridgeId,
            Checkpoint = cmd.Checkpoint,
            Role = role.Value,
            SegmentIndex = cmd.SegmentIndex,
            Status = WeighSessionStatus.Open,
            OwnershipToken = Guid.NewGuid().ToString("N"),
            OpenedAt = clock.UtcNow,
            CreatedAt = clock.UtcNow,
            CreatedBy = user.UserId
        };

        await sessions.AddAsync(session, ct);

        // Audit phải vào cùng lần lưu với thay đổi nó mô tả, nếu không sẽ mất dấu vết (R-026).
        await audit.LogAsync(
            actionType: "WEIGH_SESSION_OPENED",
            tripId: trip.Id,
            actor: user.UserId,
            beforeJson: null,
            afterJson: $"{{\"sessionId\":\"{session.Id}\",\"bridgeId\":\"{cmd.WeighbridgeId}\",\"role\":\"{role}\"}}",
            reason: null,
            correlationId: cmd.CorrelationId,
            ct: ct);

        await uow.SaveChangesAsync(ct);

        return new OpenWeighSessionResult(
            IsSuccess: true,
            SessionId: session.Id,
            Role: role,
            FailureReason: null,
            StabilityWindow: profile.Thresholds.StabilityWindow,
            ProfileCode: profile.Code);
    }

    /// <summary>Vai trò lấy từ chuỗi khai báo của profile, theo thứ tự phân đoạn.</summary>
    private static WeightRole? ResolveRole(WorkflowProfile profile, int segmentIndex)
        => segmentIndex >= 0 && segmentIndex < profile.WeightRoleSequence.Count
            ? profile.WeightRoleSequence[segmentIndex]
            : null;

    private static OpenWeighSessionResult Fail(string reason) => new(false, null, null, reason);
}
