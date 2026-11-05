using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Domain.Common;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Application.UseCases.Trips;

public sealed record DeleteTripCommand(
    Guid TripId,
    int ExpectedVersion,
    string Reason,
    string ConfirmUserId,
    string CorrelationId);

public sealed record DeleteTripResult(bool IsSuccess, string? FailureReason);

/// <summary>
/// Xóa phiếu đăng tài (mía và vật tư).
/// - Đã cân (có bất kỳ lượt cân nào) → KHÔNG được xóa.
/// - Chưa cân → được xóa; lệnh đốn / Shipment được đăng tài lại.
/// Chưa hủy thì hủy trước (thu hồi QR, giải phóng hàng chờ, báo FRM) rồi mới đánh dấu xóa.
/// Xóa mềm (deleted_at) — không DELETE cứng chứng từ; có audit.
/// </summary>
public sealed class DeleteTripHandler(
    ITripRepository trips,
    CancelTripHandler cancelTrip,
    IUnitOfWork uow,
    IClock clock,
    ICurrentUser user,
    IAuditLogger audit)
{
    /// <summary>Cùng quyền với hủy đăng tài.</summary>
    public const string RequiredPermission = CancelTripHandler.RequiredPermission;

    public async Task<DeleteTripResult> HandleAsync(DeleteTripCommand cmd, CancellationToken ct = default)
    {
        var trip = await trips.GetForUpdateAsync(cmd.TripId, cmd.ExpectedVersion, ct);
        if (trip is null)
            return Fail(DomainText.T("rule.trip_changed_reload",
                "Chuyến không tồn tại hoặc đã bị thay đổi bởi người khác. Vui lòng tải lại."));

        if (!user.HasPermission(RequiredPermission, trip.SiteId))
            return Fail(DomainText.T("rule.trip_delete_no_permission", "Không có quyền xóa đăng tài tại nhà máy này."));

        if (string.IsNullOrWhiteSpace(cmd.Reason))
            return Fail(DomainText.T("rule.trip_delete_reason_required", "Lý do xóa là bắt buộc."));

        // Đã cân → không được xóa (trạng thái sau cân, hoặc đã có lượt cân dù chỉ lần 1).
        if (trip.Lifecycle is not (TripLifecycle.TempRegistered or TripLifecycle.Registered
                or TripLifecycle.InProgress or TripLifecycle.Cancelled)
            || await trips.HasWeighEventsAsync(trip.Id, ct))
            return Fail(DomainText.T("rule.trip_weighed_no_delete",
                "Chuyến {0} đã cân — không được xóa đăng tài.", trip.TripNo));

        // Phiếu đã HỦY → xóa = gỡ khỏi hệ thống cân (FRM đã nhận lệnh hủy lúc bấm Hủy).
        // Audit TRIP_DELETED_CANCELLED → lệnh đốn mía vẫn bị chặn đăng tài lại (phải lập lệnh đốn mới).
        var lifecycleBefore = trip.Lifecycle;

        // Chưa hủy → hủy trước để thu hồi QR, giải phóng bàn cân và báo FRM như luồng Hủy phiếu.
        if (trip.Lifecycle != TripLifecycle.Cancelled)
        {
            var cancel = await cancelTrip.HandleAsync(new CancelTripCommand(
                trip.Id, trip.Version, cmd.Reason, cmd.ConfirmUserId, cmd.CorrelationId), ct);
            if (!cancel.IsSuccess)
                return Fail(cancel.FailureReason);

            // Cùng DbContext → lấy lại đúng bản vừa hủy (version đã tăng).
            trip = await trips.GetByIdAsync(cmd.TripId, ct);
            if (trip is null)
                return Fail(DomainText.T("rule.trip_changed_reload",
                    "Chuyến không tồn tại hoặc đã bị thay đổi bởi người khác. Vui lòng tải lại."));
        }

        var now = clock.UtcNow;
        trip.DeletedAt = now;
        trip.UpdatedAt = now;
        trip.UpdatedBy = user.UserId;
        trip.Version++;

        // TRIP_DELETED_CANCELLED: xóa phiếu đã hủy trước đó → lệnh đốn vẫn không đăng tài lại được.
        await audit.LogAsync(lifecycleBefore == TripLifecycle.Cancelled ? "TRIP_DELETED_CANCELLED" : "TRIP_DELETED", trip.Id, user.UserId,
            $"{{\"lifecycle\":\"{lifecycleBefore}\",\"tripNo\":\"{trip.TripNo}\",\"reference\":\"{trip.ReferenceTripNo}\"}}",
            $"{{\"deletedAt\":\"{now:O}\",\"confirmUserId\":\"{cmd.ConfirmUserId}\"}}",
            cmd.Reason, cmd.CorrelationId, ct);

        await uow.SaveChangesAsync(ct);
        return new DeleteTripResult(true, null);
    }

    private static DeleteTripResult Fail(string? reason) => new(false, reason);
}
