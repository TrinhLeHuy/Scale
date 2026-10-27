using TTC.Weighing.Domain.Common;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Domain.Rules;

public sealed record GateDecisionContext
{
    public required TripLifecycle TripLifecycle { get; init; }
    public required ExecutionStatus ExecutionStatus { get; init; }

    public required QrTokenType QrType { get; init; }
    public required QrTokenStatus QrStatus { get; init; }
    public required bool QrMatchesTrip { get; init; }

    /// <summary>Xác thực danh tính xe tại làn theo D-03. QR không phải danh tính vật lý.</summary>
    public required bool VehicleIdentityVerified { get; init; }

    public required GateDirection RequestedDirection { get; init; }

    /// <summary>Cổng/làn đúng theo cấu hình và hướng yêu cầu.</summary>
    public required bool GateMatchesConfiguration { get; init; }

    /// <summary>Đủ checkpoint bắt buộc cho giai đoạn này của profile.</summary>
    public required bool StageCheckpointsSatisfied { get; init; }

    /// <summary>Bộ điều khiển barie sẵn sàng nhận lệnh.</summary>
    public required bool ControllerReady { get; init; }

    /// <summary>Điều kiện an toàn tại chỗ đạt. Ứng dụng KHÔNG bỏ qua cảm biến an toàn.</summary>
    public required bool LocalSafetyConditionsMet { get; init; }

    /// <summary>Còn lệnh mở chưa rõ kết quả — không cấp lệnh mới.</summary>
    public required bool HasPendingUnknownCommand { get; init; }
}

public sealed record GateDecisionResult(bool Allowed, IReadOnlyList<ConditionCheck> Checks)
{
    public string DenyReason => Allowed
        ? string.Empty
        : string.Join(Environment.NewLine,
            Checks.Where(c => !c.Passed).Select(c => $"[{c.Code}] {c.Name}: {c.Detail}"));
}

/// <summary>
/// R-006 / 02_Quy_tac mục 8 — điều kiện cho phép mở barie tự động.
/// Không coi QR là danh tính vật lý; không coi đã gửi lệnh là xe đã đi qua.
/// </summary>
public static class GateDecisionRules
{
    public static GateDecisionResult Evaluate(GateDecisionContext ctx)
    {
        var checks = new List<ConditionCheck>
        {
            CheckTripAndQr(ctx),
            CheckIdentity(ctx),
            CheckGateAndStage(ctx),
            CheckController(ctx)
        };

        return new GateDecisionResult(checks.All(c => c.Passed), checks);
    }

    private static ConditionCheck CheckTripAndQr(GateDecisionContext c)
    {
        const string code = "GATE.1";
        var name = DomainText.T("rule.gate_trip_qr_name", "Chuyến và QR còn hiệu lực");

        // QR tạm cho phép qua cổng — cân offline, sau đẩy lên hệ thống chính.
        // Chỉ cần QR chưa thu hồi và chuyến chưa hủy/đóng.

        if (c.QrStatus != QrTokenStatus.Issued && c.QrType != QrTokenType.Temp)
            return new(code, name, false, DomainText.T("rule.qr_status_is", "QR ở trạng thái {0}.", c.QrStatus));

        if (!c.QrMatchesTrip)
            return new(code, name, false, DomainText.T("rule.gate_qr_not_trip", "QR không thuộc chuyến đang xét."));

        if (c.TripLifecycle is TripLifecycle.Cancelled)
            return new(code, name, false, DomainText.T("rule.gate_trip_cancelled", "Chuyến đã bị hủy."));

        if (c.TripLifecycle is TripLifecycle.Closed)
            return new(code, name, false, DomainText.T("rule.gate_trip_closed", "Chuyến đã kết thúc, không mở cổng mới bằng QR này."));

        if (c.ExecutionStatus == ExecutionStatus.Paused)
            return new(code, name, false, DomainText.T("rule.gate_trip_paused", "Chuyến đang PAUSED, ngừng cấp quyền mở mới (U-13)."));

        return new(code, name, true);
    }

    private static ConditionCheck CheckIdentity(GateDecisionContext c)
    {
        const string code = "GATE.2";
        var name = DomainText.T("rule.gate_identity_name", "Đúng danh tính xe tại làn");

        return c.VehicleIdentityVerified
            ? new(code, name, true)
            : new(code, name, false,
                DomainText.T("rule.gate_identity_unverified",
                    "Chưa xác thực xe tại làn theo D-03. Khi chưa có ANPR nghiệm thu, " +
                    "cần xác nhận của nhân viên tại làn trước khi mở tự động."));
    }

    private static ConditionCheck CheckGateAndStage(GateDecisionContext c)
    {
        const string code = "GATE.3";
        var name = DomainText.T("rule.gate_stage_name", "Đúng cổng, hướng và đủ checkpoint");

        if (!c.GateMatchesConfiguration)
            return new(code, name, false, DomainText.T("rule.gate_lane_mismatch", "Cổng/làn không khớp cấu hình cho hướng {0}.", c.RequestedDirection));

        if (!c.StageCheckpointsSatisfied)
            return new(code, name, false, DomainText.T("rule.gate_checkpoints_missing", "Chưa đủ checkpoint bắt buộc của giai đoạn này."));

        return new(code, name, true);
    }

    private static ConditionCheck CheckController(GateDecisionContext c)
    {
        const string code = "GATE.4";
        var name = DomainText.T("rule.gate_controller_name", "Bộ điều khiển và an toàn tại chỗ");

        if (!c.ControllerReady)
            return new(code, name, false, DomainText.T("rule.gate_controller_not_ready", "Bộ điều khiển barie không sẵn sàng."));

        if (!c.LocalSafetyConditionsMet)
            return new(code, name, false,
                DomainText.T("rule.gate_safety_not_met", "Điều kiện an toàn tại chỗ chưa đạt. Không bỏ qua cảm biến an toàn."));

        if (c.HasPendingUnknownCommand)
            return new(code, name, false,
                DomainText.T("rule.gate_pending_unknown", "Còn lệnh mở chưa rõ kết quả. Kiểm tra trạng thái thiết bị và xe trước khi cấp lệnh mới."));

        return new(code, name, true);
    }
}
