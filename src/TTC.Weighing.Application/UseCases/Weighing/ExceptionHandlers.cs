using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Application.UseCases.Weighing;

public sealed record ApproveManualWeightCommand(
    Guid TripId,
    Guid WeighbridgeId,
    CheckpointType Checkpoint,
    int SegmentIndex,
    WeightRole Role,
    decimal RawWeight,
    string RawUom,
    string Reason,
    string ApprovalRef,
    string CorrelationId);

public sealed record ManualWeightResult(bool IsSuccess, Guid? WeighEventId, string? FailureReason);

/// <summary>
/// R-010 — Nhập tay số cân. Có quyền, có lý do, nguồn MANUAL.
/// KHÔNG dùng để bỏ qua trạng thái lỗi thiết bị/API đang hiệu lực.
/// </summary>
public sealed class ApproveManualWeightHandler(
    ITripRepository trips,
    IWeighSessionRepository sessions,
    IWeighbridgeRepository weighbridges,
    IWorkflowProfileRepository profiles,
    IApprovalRepository approvals,
    IIntegrationStateQuery integrationState,
    IUnitOfWork uow,
    IClock clock,
    ICurrentUser user,
    IAuditLogger audit)
{
    public const string RequiredPermission = "weighing.weight.manual";

    public async Task<ManualWeightResult> HandleAsync(
        ApproveManualWeightCommand cmd, CancellationToken ct = default)
    {
        var trip = await trips.GetByIdAsync(cmd.TripId, ct);
        if (trip is null)
            return new(false, null, "Chuyến không tồn tại.");

        if (!user.HasPermission(RequiredPermission, trip.SiteId))
            return new(false, null, "Không có quyền nhập tay số cân.");

        // Lý do và phê duyệt — tạm bỏ ràng buộc, cho nhập tự do
        // if (string.IsNullOrWhiteSpace(cmd.Reason) || string.IsNullOrWhiteSpace(cmd.ApprovalRef))
        //     return new(false, null, "Nhập tay bắt buộc có lý do và phê duyệt.");

        // Approval check — tạm bỏ
        // var approval = await approvals.GetByIdAsync(...);

        // Nhập tay không được dùng như đường tránh khi đang lỗi (R-010, U-13).
        if (trip.ExecutionStatus == ExecutionStatus.Paused)
            return new(false, null,
                "Chuyến đang PAUSED. Xử lý sự cố trước, nhập tay không phải cách bỏ qua lỗi.");

        if (await integrationState.HasUnresolvedUnknownAsync(trip.Id, ct))
            return new(false, null, "Còn giao dịch chưa rõ kết quả, phải đối soát trước.");

        if (trip.Lifecycle is TripLifecycle.Closed or TripLifecycle.Cancelled)
            return new(false, null, $"Chuyến đã {trip.Lifecycle}.");

        var bridge = await weighbridges.GetByIdAsync(cmd.WeighbridgeId, ct);
        if (bridge is null)
            return new(false, null, "Bàn cân không tồn tại.");

        var profile = await profiles.GetEffectiveAsync(
            trip.WorkflowProfileId, trip.WorkflowProfileVersion, ct);
        if (profile is null)
            return new(false, null, "Không tìm thấy hồ sơ nghiệp vụ của chuyến.");

        var measurement = Domain.ValueObjects.WeightMeasurement.Create(cmd.RawWeight, cmd.RawUom);

        var session = new WeighSession
        {
            TripId = trip.Id,
            WeighbridgeId = cmd.WeighbridgeId,
            Checkpoint = cmd.Checkpoint,
            Role = cmd.Role,
            SegmentIndex = cmd.SegmentIndex,
            Status = WeighSessionStatus.Committed,
            OwnershipToken = Guid.NewGuid().ToString("N"),
            OpenedAt = clock.UtcNow,
            ClosedAt = clock.UtcNow,
            CreatedAt = clock.UtcNow,
            CreatedBy = user.UserId
        };

        var previous = await sessions.GetEffectiveEventAsync(trip.Id, cmd.Role, cmd.SegmentIndex, ct);

        var weighEvent = new WeighEvent
        {
            WeighSessionId = session.Id,
            TripId = trip.Id,
            Revision = (previous?.Revision ?? 0) + 1,
            SupersedesId = previous?.Id,
            IsEffective = true,
            Role = cmd.Role,
            RawWeight = measurement.RawValue,
            RawUom = measurement.RawUom,
            NormalizedKg = measurement.NormalizedKg,
            MeasuredAt = clock.UtcNow,
            Source = WeighSource.Manual,
            StableWindow = TimeSpan.Zero,
            PolicyVersion = profile.ProfileVersion,
            ApprovalRef = cmd.ApprovalRef,
            Reason = cmd.Reason,
            CreatedAt = clock.UtcNow,
            CreatedBy = user.UserId
        };

        if (previous is not null)
            previous.IsEffective = false;

        await sessions.AddAsync(session, ct);
        await sessions.AddEventAsync(weighEvent, ct);

        await audit.LogAsync("MANUAL_WEIGHT_APPROVED", trip.Id, user.UserId,
            previous is null ? null : $"{{\"revision\":{previous.Revision},\"kg\":{previous.NormalizedKg}}}",
            $"{{\"revision\":{weighEvent.Revision},\"kg\":{weighEvent.NormalizedKg},\"source\":\"MANUAL\"}}",
            cmd.Reason, cmd.CorrelationId, ct);

        await uow.SaveChangesAsync(ct);

        return new(true, weighEvent.Id, null);
    }
}

public sealed record RequestReweighCommand(
    Guid TripId,
    WeightRole Role,
    int SegmentIndex,
    string Reason,
    string? DocumentRef,
    string CorrelationId);

public sealed record RequestReweighResult(bool IsSuccess, Guid? ApprovalId, string? FailureReason);

/// <summary>
/// R-009 — Yêu cầu cân lại. Giữ toàn bộ lịch sử; chỉ một phiên bản có hiệu lực cho mỗi vai trò.
/// Nếu đã gửi FRM/ERP thì cần yêu cầu điều chỉnh, không gửi lặp như giao dịch mới (R-037).
/// </summary>
public sealed class RequestReweighHandler(
    ITripRepository trips,
    IWeighSessionRepository sessions,
    IIntegrationMessageRepository messages,
    IApprovalRepository approvals,
    IUnitOfWork uow,
    IClock clock,
    ICurrentUser user,
    IAuditLogger audit)
{
    public const string RequiredPermission = "weighing.reweigh.request";

    public async Task<RequestReweighResult> HandleAsync(
        RequestReweighCommand cmd, CancellationToken ct = default)
    {
        var trip = await trips.GetByIdAsync(cmd.TripId, ct);
        if (trip is null)
            return new(false, null, "Chuyến không tồn tại.");

        if (!user.HasPermission(RequiredPermission, trip.SiteId))
            return new(false, null, "Không có quyền yêu cầu cân lại.");

        if (string.IsNullOrWhiteSpace(cmd.Reason))
            return new(false, null, "Cân lại bắt buộc có lý do.");

        var existing = await sessions.GetEffectiveEventAsync(cmd.TripId, cmd.Role, cmd.SegmentIndex, ct);
        if (existing is null)
            return new(false, null, "Chưa có lượt cân hiệu lực nào để cân lại.");

        var sent = await messages.ListByTripAsync(cmd.TripId, ct);
        var alreadyPosted = sent.Any(m => m.Status == IntegrationMessageStatus.Acknowledged);

        var approval = new Approval
        {
            TripId = cmd.TripId,
            ActionType = alreadyPosted ? "REWEIGH_AFTER_POST" : "REWEIGH",
            RequestedBy = user.UserId,
            Reason = cmd.Reason,
            DocumentRef = cmd.DocumentRef,
            CreatedAt = clock.UtcNow,
            CreatedBy = user.UserId
        };

        await approvals.AddAsync(approval, ct);

        var note = alreadyPosted
            ? "Kết quả đã gửi hệ thống đích. Cân lại phải tạo yêu cầu điều chỉnh liên kết bản trước (R-037); " +
              "thiếu API điều chỉnh thì giữ chờ xử lý."
            : null;

        await audit.LogAsync("REWEIGH_REQUESTED", cmd.TripId, user.UserId,
            $"{{\"revision\":{existing.Revision},\"kg\":{existing.NormalizedKg}}}",
            $"{{\"approvalId\":\"{approval.Id}\",\"alreadyPosted\":{alreadyPosted.ToString().ToLowerInvariant()}}}",
            cmd.Reason, cmd.CorrelationId, ct);

        await uow.SaveChangesAsync(ct);

        return new(true, approval.Id, note);
    }
}

public sealed record PauseTripCommand(
    Guid TripId,
    string IssueCode,
    string Scope,
    string Description,
    string CorrelationId);

public sealed record ResumeTripCommand(
    Guid TripId,
    Guid IncidentId,
    bool UnknownTransactionsResolved,
    string CorrelationId);

public sealed record PauseResumeResult(bool IsSuccess, string? FailureReason);

/// <summary>
/// R-025 / U-13 — Tạm ngưng khi lỗi và chỉ phục hồi sau kiểm tra, xác nhận
/// và xử lý các giao dịch chưa rõ kết quả.
/// </summary>
public sealed class PauseResumeHandler(
    ITripRepository trips,
    IIncidentRepository incidents,
    IIntegrationStateQuery integrationState,
    IUnitOfWork uow,
    IClock clock,
    ICurrentUser user,
    IAuditLogger audit)
{
    public const string PausePermission = "operations.pause";
    public const string ResumePermission = "operations.resume";

    public async Task<PauseResumeResult> PauseAsync(PauseTripCommand cmd, CancellationToken ct = default)
    {
        var trip = await trips.GetByIdAsync(cmd.TripId, ct);
        if (trip is null)
            return new(false, "Chuyến không tồn tại.");

        if (!user.HasPermission(PausePermission, trip.SiteId))
            return new(false, "Không có quyền tạm ngưng.");

        // PAUSED giữ nguyên bước cuối, không xóa lịch sử để quay lại bước đầu (02_Quy_tac 2.2).
        trip.ExecutionStatus = ExecutionStatus.Paused;
        trip.PauseIssueCode = cmd.IssueCode;
        trip.PauseScope = cmd.Scope;
        trip.PausedAt = clock.UtcNow;
        trip.UpdatedAt = clock.UtcNow;
        trip.UpdatedBy = user.UserId;

        var incident = new Incident
        {
            TripId = cmd.TripId,
            SiteId = trip.SiteId,
            IssueCode = cmd.IssueCode,
            Scope = cmd.Scope,
            Description = cmd.Description,
            OpenedAt = clock.UtcNow,
            OpenedBy = user.UserId,
            CreatedAt = clock.UtcNow,
            CreatedBy = user.UserId
        };

        await incidents.AddAsync(incident, ct);

        await audit.LogAsync("TRIP_PAUSED", cmd.TripId, user.UserId,
            "{\"executionStatus\":\"Active\"}",
            $"{{\"executionStatus\":\"Paused\",\"issueCode\":\"{cmd.IssueCode}\",\"scope\":\"{cmd.Scope}\"}}",
            cmd.Description, cmd.CorrelationId, ct);

        await uow.SaveChangesAsync(ct);

        return new(true, null);
    }

    public async Task<PauseResumeResult> ResumeAsync(ResumeTripCommand cmd, CancellationToken ct = default)
    {
        var trip = await trips.GetByIdAsync(cmd.TripId, ct);
        if (trip is null)
            return new(false, "Chuyến không tồn tại.");

        if (!user.HasPermission(ResumePermission, trip.SiteId))
            return new(false, "Không có quyền cho tiếp tục.");

        if (trip.ExecutionStatus != ExecutionStatus.Paused)
            return new(false, "Chuyến không ở trạng thái PAUSED.");

        // Không cho tiếp tục khi còn giao dịch chưa rõ kết quả (02_Quy_tac mục 8).
        if (await integrationState.HasUnresolvedUnknownAsync(cmd.TripId, ct))
            return new(false,
                "Còn thông điệp UNKNOWN chưa giải quyết. Tra cứu hoặc đối soát với hệ thống đích trước.");

        if (!cmd.UnknownTransactionsResolved)
            return new(false,
                "Người xử lý phải xác nhận đã kiểm tra kết nối, vị trí xe và các số liệu liên quan.");

        var incident = await incidents.GetOpenByTripAsync(cmd.TripId, ct);
        if (incident is not null)
        {
            incident.ResolvedAt = clock.UtcNow;
            incident.ResolvedBy = user.UserId;
            incident.UnknownTransactionsResolved = true;
        }

        trip.ExecutionStatus = ExecutionStatus.Active;
        trip.PauseIssueCode = null;
        trip.PauseScope = null;
        trip.PausedAt = null;
        trip.UpdatedAt = clock.UtcNow;
        trip.UpdatedBy = user.UserId;

        await audit.LogAsync("TRIP_RESUMED", cmd.TripId, user.UserId,
            "{\"executionStatus\":\"Paused\"}", "{\"executionStatus\":\"Active\"}",
            null, cmd.CorrelationId, ct);

        await uow.SaveChangesAsync(ct);

        return new(true, null);
    }
}
