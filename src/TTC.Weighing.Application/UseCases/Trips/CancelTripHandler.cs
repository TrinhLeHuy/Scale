using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Application.UseCases.Trips;

public sealed record CancelTripCommand(
    Guid TripId,
    int ExpectedVersion,
    string Reason,
    string ConfirmUserId,
    string CorrelationId);

public sealed record CancelTripResult(
    bool IsSuccess,
    string? FailureReason,
    IntegrationMessageStatus? FrmNotifyStatus);

/// <summary>
/// Hủy đăng tài (Req 5.1–5.6).
/// Chỉ cho phép khi Lifecycle ∈ {TempRegistered, Registered} và chưa có WeighSession Committed.
/// Thu hồi QR, giải phóng hàng chờ, gửi FRM nếu nguồn FRM.
/// </summary>
public sealed class CancelTripHandler(
    ITripRepository trips,
    IFrmPlanLookup frmLookup,
    IUnitOfWork uow,
    IClock clock,
    ICurrentUser user,
    IAuditLogger audit)
{
    public const string RequiredPermission = "trip.cancel";

    public async Task<CancelTripResult> HandleAsync(
        CancelTripCommand cmd, CancellationToken ct = default)
    {
        // 1. Kiểm tra quyền trip.cancel
        // SiteId lấy từ trip sau khi load — cần load trip trước.
        // Tuy nhiên, GetForUpdateAsync cần version check nên ta load + kiểm tra quyền cùng lúc.

        // 2. Load trip với version check (optimistic concurrency)
        var trip = await trips.GetForUpdateAsync(cmd.TripId, cmd.ExpectedVersion, ct);
        if (trip is null)
            return Fail("Chuyến không tồn tại hoặc đã bị thay đổi bởi người khác (version mismatch). Vui lòng tải lại.");

        // Kiểm tra quyền sau khi có SiteId
        if (!user.HasPermission(RequiredPermission, trip.SiteId))
            return Fail("Không có quyền hủy đăng tài tại nhà máy này.");

        // 3. Validate Lifecycle ∈ {TempRegistered, Registered, InProgress}
        if (trip.Lifecycle is not (TripLifecycle.TempRegistered or TripLifecycle.Registered or TripLifecycle.InProgress))
            return Fail($"Không thể hủy chuyến ở trạng thái {trip.Lifecycle}. Chỉ hủy được khi chuyến chưa hoàn thành.");

        // 4. Đã cân (có bất kỳ lượt cân nào, kể cả mới cân lần 1) → không được hủy.
        //    Tra thẳng bảng weigh_events: GetForUpdateAsync không nạp WeighSessions nên không dựa vào navigation.
        if (await trips.HasWeighEventsAsync(trip.Id, ct)
            || trip.WeighSessions.Any(ws => ws.Status == WeighSessionStatus.Committed))
            return Fail(Domain.Common.DomainText.T("rule.trip_weighed_no_cancel",
                "Chuyến {0} đã cân — không được hủy đăng tài.", trip.TripNo));

        // Validate lý do không trống
        if (string.IsNullOrWhiteSpace(cmd.Reason))
            return Fail("Lý do hủy là bắt buộc.");

        // Snapshot trạng thái trước khi thay đổi (cho audit)
        var beforeJson = $"{{\"lifecycle\":\"{trip.Lifecycle}\",\"qrTokenCount\":{trip.QrTokens.Count},"
            + $"\"assignedWeighbridgeId\":\"{trip.AssignedWeighbridgeId}\","
            + $"\"queueSequenceNumber\":{trip.QueueSequenceNumber?.ToString() ?? "null"}}}";

        // 5. Chuyển Lifecycle → Cancelled
        trip.Lifecycle = TripLifecycle.Cancelled;
        trip.UpdatedAt = clock.UtcNow;
        trip.UpdatedBy = user.UserId;
        trip.Version++;

        // 6. Thu hồi tất cả QrToken → Revoked
        var now = clock.UtcNow;
        foreach (var token in trip.QrTokens)
        {
            if (token.Status == QrTokenStatus.Issued)
            {
                token.Status = QrTokenStatus.Revoked;
                token.RevokedAt = now;
                token.UpdatedAt = now;
                token.UpdatedBy = user.UserId;
            }
        }

        // 7. Giải phóng hàng chờ (xóa assignment)
        trip.AssignedWeighbridgeId = null;
        trip.QueueSequenceNumber = null;

        // 7b. Đóng tất cả WeighSession đang active → giải phóng bàn cân
        foreach (var ws in trip.WeighSessions)
        {
            if (ws.Status is WeighSessionStatus.Open)
            {
                ws.Status = WeighSessionStatus.Abandoned;
                ws.ClosedAt = now;
                ws.UpdatedAt = now;
                ws.UpdatedBy = user.UserId;
            }
        }

        // 8. Nếu nguồn FRM → gọi NotifyCancellationAsync, xử lý UNKNOWN theo R-024
        IntegrationMessageStatus? frmNotifyStatus = null;

        if (!string.IsNullOrWhiteSpace(trip.ReferenceTripNo)
            && string.Equals(trip.SourceSystem, "FRM", StringComparison.OrdinalIgnoreCase))
        {
            var sendResult = await frmLookup.NotifyCancellationAsync(
                trip.SiteId, trip.Id, trip.ReferenceTripNo, cmd.Reason,
                cmd.CorrelationId, ct);

            frmNotifyStatus = sendResult.Status;

            // R-024: Trip vẫn Cancelled dù FRM trả UNKNOWN hoặc lỗi.
            // IntegrationMonitor xử lý retry cho UNKNOWN.
        }

        // 9. Ghi audit log đầy đủ
        var afterJson = $"{{\"lifecycle\":\"{trip.Lifecycle}\",\"reason\":\"{EscapeJson(cmd.Reason)}\","
            + $"\"confirmUserId\":\"{cmd.ConfirmUserId}\","
            + $"\"frmNotifyStatus\":\"{frmNotifyStatus?.ToString() ?? "N/A"}\"}}";

        await audit.LogAsync(
            "TRIP_CANCELLED",
            trip.Id,
            user.UserId,
            beforeJson,
            afterJson,
            cmd.Reason,
            cmd.CorrelationId,
            ct);

        await uow.SaveChangesAsync(ct);

        // 10. Return CancelTripResult
        return new CancelTripResult(true, null, frmNotifyStatus);
    }

    private static CancelTripResult Fail(string reason) => new(false, reason, null);

    private static string EscapeJson(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
