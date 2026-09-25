using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Application.UseCases.Trips;

public sealed record UpdateSugarcaneTypeCommand(
    Guid TripId,
    string NewSugarcaneType,
    string Source,
    string CorrelationId);

public sealed record UpdateSugarcaneTypeResult(
    bool IsSuccess,
    string? OldValue,
    string? NewValue,
    bool RequiresAdjustmentRequest,
    string? FailureReason);

/// <summary>
/// Req 12 — Cập nhật loại mía khi FRM sửa (tươi ↔ cháy).
/// Nếu đã Posted (có IntegrationMessage ở trạng thái Acknowledged) thì đánh dấu cần tạo yêu cầu điều chỉnh.
/// </summary>
public sealed class UpdateSugarcaneTypeHandler(
    ITripRepository trips,
    IIntegrationMessageRepository messages,
    IUnitOfWork uow,
    IClock clock,
    ICurrentUser user,
    IAuditLogger audit)
{
    public async Task<UpdateSugarcaneTypeResult> HandleAsync(
        UpdateSugarcaneTypeCommand cmd, CancellationToken ct = default)
    {
        var trip = await trips.GetByIdAsync(cmd.TripId, ct);
        if (trip is null)
            return new(false, null, null, false, "Chuyến không tồn tại.");

        if (trip.Lifecycle == TripLifecycle.Cancelled)
            return new(false, null, null, false, "Chuyến đã hủy.");

        var oldValue = trip.SugarcaneType;
        trip.SugarcaneType = cmd.NewSugarcaneType;
        trip.UpdatedAt = clock.UtcNow;
        trip.UpdatedBy = user.UserId;

        // Kiểm tra đã gửi kết quả chưa — Acknowledged nghĩa là đã Posted
        var sent = await messages.ListByTripAsync(cmd.TripId, ct);
        var alreadyPosted = sent.Any(m =>
            m.Status == IntegrationMessageStatus.Acknowledged);

        await audit.LogAsync("SUGARCANE_TYPE_UPDATED", cmd.TripId, user.UserId,
            $"{{\"sugarcaneType\":\"{oldValue}\"}}",
            $"{{\"sugarcaneType\":\"{cmd.NewSugarcaneType}\",\"source\":\"{cmd.Source}\"}}",
            null, cmd.CorrelationId, ct);

        await uow.SaveChangesAsync(ct);

        return new(true, oldValue, cmd.NewSugarcaneType, alreadyPosted, null);
    }
}
