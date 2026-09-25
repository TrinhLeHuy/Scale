using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Application.UseCases.Integration;

public sealed record SubmitTripResultCommand(
    Guid TripId,
    string Destination,
    string Operation,
    string? ShipmentNo,
    string CorrelationId);

public sealed record SubmitTripResultResult(
    bool IsQueued,
    Guid? MessageId,
    string? BusinessKey,
    IntegrationMessageStatus Status,
    string? Note);

/// <summary>
/// R-022 / R-024 — Đưa kết quả vào sổ thông điệp bền vững.
/// Không gửi trực tiếp trong luồng nghiệp vụ; không tạo thông điệp trùng business_key.
/// </summary>
public sealed class SubmitTripResultHandler(
    ITripRepository trips,
    IReconciliationRepository reconciliations,
    IAllocationRepository allocations,
    IIntegrationMessageRepository messages,
    IIntegrationPayloadBuilder payloadBuilder,
    IUnitOfWork uow,
    IClock clock,
    ICurrentUser user,
    IAuditLogger audit)
{
    public const string RequiredPermission = "integration.submit";

    public async Task<SubmitTripResultResult> HandleAsync(
        SubmitTripResultCommand cmd, CancellationToken ct = default)
    {
        var trip = await trips.GetByIdAsync(cmd.TripId, ct);
        if (trip is null)
            return Held("Chuyến không tồn tại.");

        if (!user.HasPermission(RequiredPermission, trip.SiteId))
            return Held("Không có quyền gửi kết quả.");

        if (trip.ExecutionStatus == ExecutionStatus.Paused)
            return Held("Chuyến đang PAUSED, không gửi kết quả từ dữ liệu chưa tin cậy.");

        var reconciliation = await reconciliations.GetEffectiveAsync(cmd.TripId, ct);
        if (reconciliation is null || !reconciliation.IsPassed)
            return Held("Chưa đạt đối soát, chưa đủ điều kiện gửi kết quả chính thức.");

        // Phân bổ chưa duyệt thì giữ chưa đồng bộ (S02 tr.18).
        var tripAllocations = await allocations.ListByTripAsync(cmd.TripId, ct);
        if (tripAllocations.Count > 0 && tripAllocations.Any(a => !a.IsApproved))
            return Held("Còn phân bổ chưa được duyệt, chưa gửi kết quả phân bổ.");

        var businessKey = BuildBusinessKey(cmd, trip, reconciliation.Revision);

        // Chống lặp: cùng business_key thì trả thông điệp đã có, không tạo bản mới (R-024).
        var existing = await messages.FindByBusinessKeyAsync(businessKey, ct);
        if (existing is not null)
        {
            return new(true, existing.Id, businessKey, existing.Status,
                $"Thông điệp đã tồn tại ở trạng thái {existing.Status}, không tạo bản trùng.");
        }

        var payload = await payloadBuilder.BuildAsync(cmd.TripId, cmd.Operation, cmd.ShipmentNo, ct);

        var message = new IntegrationMessage
        {
            TripId = cmd.TripId,
            TripRevision = reconciliation.Revision,
            Destination = cmd.Destination.ToUpperInvariant(),
            Operation = cmd.Operation,
            BusinessKey = businessKey,
            ShipmentNo = cmd.ShipmentNo,
            PayloadRef = payload.PayloadRef,
            PayloadHash = payload.PayloadHash,
            InterfaceReference = payload.InterfaceReference,

            // Hợp đồng chưa xác minh nên giữ HELD, không gửi mù (D-02).
            Status = payload.ContractVerified
                ? IntegrationMessageStatus.New
                : IntegrationMessageStatus.Held,

            // Chỉ cho retry tự động khi hợp đồng đã chứng minh cơ chế chống trùng.
            AutoRetryAllowed = payload.IdempotencySupported,

            BusinessErrorMessage = payload.ContractVerified
                ? null
                : "Hợp đồng API chưa được xác minh (D-02). Thông điệp giữ ở HELD.",

            CreatedAt = clock.UtcNow,
            CreatedBy = user.UserId
        };

        await messages.AddAsync(message, ct);

        await audit.LogAsync("INTEGRATION_MESSAGE_QUEUED", cmd.TripId, user.UserId, null,
            $"{{\"businessKey\":\"{businessKey}\",\"status\":\"{message.Status}\"}}",
            null, cmd.CorrelationId, ct);

        await uow.SaveChangesAsync(ct);

        return new(true, message.Id, businessKey, message.Status, message.BusinessErrorMessage);
    }

    /// <summary>
    /// Khóa nghiệp vụ: đích + trip + operation + revision + shipment.
    /// Không đổi khóa chỉ để gửi lại cho qua lỗi (03_Du_lieu mục 7).
    /// </summary>
    private static string BuildBusinessKey(SubmitTripResultCommand cmd, Trip trip, int revision)
        => string.Join(':',
            cmd.Destination.ToUpperInvariant(),
            trip.TripNo,
            cmd.Operation,
            revision.ToString(),
            cmd.ShipmentNo ?? "-");

    private static SubmitTripResultResult Held(string note)
        => new(false, null, null, IntegrationMessageStatus.Held, note);
}

/// <summary>
/// Payload đã dựng theo hợp đồng. ContractVerified = false nghĩa là chưa có hợp đồng đã kiểm chứng.
/// </summary>
public sealed record IntegrationPayload(
    string PayloadRef,
    string PayloadHash,
    string? InterfaceReference,
    bool ContractVerified,
    bool IdempotencySupported);

public interface IIntegrationPayloadBuilder
{
    Task<IntegrationPayload> BuildAsync(
        Guid tripId, string operation, string? shipmentNo, CancellationToken ct = default);
}

public sealed record ResolveUnknownMessageCommand(
    Guid MessageId,
    IntegrationMessageStatus ResolvedStatus,
    string? ExternalDocumentRef,
    string Reason,
    string CorrelationId);

/// <summary>
/// R-024 — Giải quyết thông điệp UNKNOWN bằng tra cứu hoặc đối soát có người xác nhận.
/// Không gửi lại mù và không tạo số phiếu khác.
/// </summary>
public sealed class ResolveUnknownMessageHandler(
    IIntegrationMessageRepository messages,
    ITripRepository trips,
    IUnitOfWork uow,
    IClock clock,
    ICurrentUser user,
    IAuditLogger audit)
{
    public const string RequiredPermission = "integration.resolve";

    public async Task<(bool IsSuccess, string? FailureReason)> HandleAsync(
        ResolveUnknownMessageCommand cmd, CancellationToken ct = default)
    {
        var all = await messages.ListRequiringAttentionAsync(Guid.Empty, ct);
        var message = all.FirstOrDefault(m => m.Id == cmd.MessageId);

        if (message is null)
            return (false, "Không tìm thấy thông điệp cần xử lý.");

        var trip = await trips.GetByIdAsync(message.TripId, ct);
        if (trip is null)
            return (false, "Không tìm thấy chuyến của thông điệp.");

        if (!user.HasPermission(RequiredPermission, trip.SiteId))
            return (false, "Không có quyền giải quyết thông điệp tích hợp.");

        if (string.IsNullOrWhiteSpace(cmd.Reason))
            return (false, "Phải ghi căn cứ đối soát khi giải quyết UNKNOWN.");

        if (cmd.ResolvedStatus is not (IntegrationMessageStatus.Acknowledged or IntegrationMessageStatus.Failed))
            return (false, "Chỉ được kết luận Acknowledged hoặc Failed sau khi đối soát.");

        var before = $"{{\"status\":\"{message.Status}\"}}";

        message.Status = cmd.ResolvedStatus;
        message.ExternalDocumentRef = cmd.ExternalDocumentRef;
        message.BusinessErrorMessage = cmd.Reason;
        message.UpdatedAt = clock.UtcNow;
        message.UpdatedBy = user.UserId;

        await audit.LogAsync("INTEGRATION_UNKNOWN_RESOLVED", message.TripId, user.UserId,
            before, $"{{\"status\":\"{cmd.ResolvedStatus}\"}}", cmd.Reason, cmd.CorrelationId, ct);

        await uow.SaveChangesAsync(ct);

        return (true, null);
    }
}
