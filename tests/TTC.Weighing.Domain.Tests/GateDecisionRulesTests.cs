using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Domain.Rules;

namespace TTC.Weighing.Domain.Tests;

public class GateDecisionRulesTests
{
    /// <summary>Bối cảnh xe vào cổng, mọi điều kiện đạt.</summary>
    private static GateDecisionContext AllowedIn() => new()
    {
        TripLifecycle = TripLifecycle.Registered,
        ExecutionStatus = ExecutionStatus.Active,
        QrType = QrTokenType.Trip,
        QrStatus = QrTokenStatus.Issued,
        QrMatchesTrip = true,
        VehicleIdentityVerified = true,
        RequestedDirection = GateDirection.In,
        GateMatchesConfiguration = true,
        StageCheckpointsSatisfied = true,
        ControllerReady = true,
        LocalSafetyConditionsMet = true,
        HasPendingUnknownCommand = false
    };

    [Fact]
    public void All_conditions_met_allows_open()
    {
        var decision = GateDecisionRules.Evaluate(AllowedIn());

        Assert.True(decision.Allowed, decision.DenyReason);
        Assert.Equal(4, decision.Checks.Count);
    }

    /// <summary>R-004: QR tạm không đủ điều kiện qua cổng nghiệp vụ.</summary>
    [Fact]
    public void Temp_qr_is_denied()
    {
        var decision = GateDecisionRules.Evaluate(AllowedIn() with { QrType = QrTokenType.Temp });

        Assert.False(decision.Allowed);
        Assert.Contains("GATE.1", decision.DenyReason);
    }

    [Theory]
    [InlineData(QrTokenStatus.Revoked)]
    [InlineData(QrTokenStatus.Expired)]
    public void Invalid_qr_status_is_denied(QrTokenStatus status)
    {
        Assert.False(GateDecisionRules.Evaluate(AllowedIn() with { QrStatus = status }).Allowed);
    }

    /// <summary>Chuyến đã kết thúc không mở cổng mới bằng QR cũ (02_Quy_tac 2.1).</summary>
    [Theory]
    [InlineData(TripLifecycle.Closed)]
    [InlineData(TripLifecycle.Cancelled)]
    public void Closed_or_cancelled_trip_is_denied(TripLifecycle lifecycle)
    {
        Assert.False(GateDecisionRules.Evaluate(AllowedIn() with { TripLifecycle = lifecycle }).Allowed);
    }

    /// <summary>U-13: đang PAUSED thì ngừng cấp quyền mở mới.</summary>
    [Fact]
    public void Paused_trip_is_denied()
    {
        var decision = GateDecisionRules.Evaluate(
            AllowedIn() with { ExecutionStatus = ExecutionStatus.Paused });

        Assert.False(decision.Allowed);
        Assert.Contains("PAUSED", decision.DenyReason);
    }

    /// <summary>R-039: QR không phải danh tính vật lý của xe.</summary>
    [Fact]
    public void Unverified_vehicle_identity_is_denied()
    {
        var decision = GateDecisionRules.Evaluate(
            AllowedIn() with { VehicleIdentityVerified = false });

        Assert.False(decision.Allowed);
        Assert.Contains("GATE.2", decision.DenyReason);
        Assert.Contains("D-03", decision.DenyReason);
    }

    [Fact]
    public void Wrong_gate_configuration_is_denied()
    {
        var decision = GateDecisionRules.Evaluate(
            AllowedIn() with { GateMatchesConfiguration = false });

        Assert.False(decision.Allowed);
        Assert.Contains("GATE.3", decision.DenyReason);
    }

    [Fact]
    public void Incomplete_stage_checkpoints_is_denied()
    {
        var decision = GateDecisionRules.Evaluate(
            AllowedIn() with
            {
                RequestedDirection = GateDirection.Out,
                StageCheckpointsSatisfied = false
            });

        Assert.False(decision.Allowed);
        Assert.Contains("checkpoint", decision.DenyReason);
    }

    [Fact]
    public void Controller_not_ready_is_denied()
    {
        Assert.False(GateDecisionRules.Evaluate(AllowedIn() with { ControllerReady = false }).Allowed);
    }

    /// <summary>Ứng dụng không bỏ qua cảm biến an toàn tại chỗ (02_Quy_tac mục 8).</summary>
    [Fact]
    public void Unmet_safety_conditions_is_denied()
    {
        var decision = GateDecisionRules.Evaluate(
            AllowedIn() with { LocalSafetyConditionsMet = false });

        Assert.False(decision.Allowed);
        Assert.Contains("an toàn", decision.DenyReason);
    }

    /// <summary>Mất phản hồi lệnh cũ thì không cấp lệnh mới trước khi kiểm tra (R-024).</summary>
    [Fact]
    public void Pending_unknown_command_is_denied()
    {
        var decision = GateDecisionRules.Evaluate(
            AllowedIn() with { HasPendingUnknownCommand = true });

        Assert.False(decision.Allowed);
        Assert.Contains("chưa rõ kết quả", decision.DenyReason);
    }

    /// <summary>Ra cổng sau khi đã đối soát và đủ checkpoint là hợp lệ.</summary>
    [Fact]
    public void Outbound_after_reconciliation_is_allowed()
    {
        var decision = GateDecisionRules.Evaluate(AllowedIn() with
        {
            TripLifecycle = TripLifecycle.Reconciled,
            RequestedDirection = GateDirection.Out
        });

        Assert.True(decision.Allowed, decision.DenyReason);
    }
}
