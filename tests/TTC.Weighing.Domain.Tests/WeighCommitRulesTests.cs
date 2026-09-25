using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Domain.Rules;

namespace TTC.Weighing.Domain.Tests;

public class WeighCommitRulesTests
{
    /// <summary>Bối cảnh mọi điều kiện đạt, dùng làm gốc để thay từng biến.</summary>
    private static WeighCommitContext Passing() => new()
    {
        TripLifecycle = TripLifecycle.InProgress,
        ExecutionStatus = ExecutionStatus.Active,
        QrType = QrTokenType.Trip,
        QrStatus = QrTokenStatus.Issued,
        QrMatchesTrip = true,
        QrMatchesSite = true,
        CheckpointAllowedByProfile = true,

        DevicesMappedToLane = true,
        SessionOwnsBridge = true,

        VehicleIdentityVerified = true,
        IdentityVerificationMethod = "LANE_OPERATOR_CONFIRMATION",

        PositionVerdict = PositionVerdict.Valid,
        PositionConfidence = 0.95m,
        PositionEvidenceAge = TimeSpan.FromMilliseconds(500),
        PositionRegionConfigValid = true,

        ScaleStatus = ScaleDeviceStatus.Ok,
        ScaleReadingAge = TimeSpan.FromMilliseconds(200),
        WithinScaleCapacity = true,
        SignalContinuous = true,

        StableDuration = TimeSpan.FromSeconds(5.2),
        ObservedDeltaKg = 0.5m,

        PrecedingWeightsSatisfied = true,
        ImpurityRequirementSatisfied = true,
        PolicyRequirementSatisfied = true,

        EvidencePersistable = true,
        NoPendingUnknownTransaction = true,

        Thresholds = new WeighThresholds
        {
            StabilityWindow = TimeSpan.FromSeconds(5),
            StabilityDeltaKg = 1m,
            ScaleSignalMaxAge = TimeSpan.FromSeconds(1),
            AiEvidenceMaxAge = TimeSpan.FromSeconds(2),
            PositionConfidenceThreshold = 0.90m
        }
    };

    [Fact]
    public void All_conditions_met_allows_commit()
    {
        var decision = WeighCommitRules.Evaluate(Passing());

        Assert.True(decision.CanCommit, decision.BlockSummary);
        Assert.Equal(8, decision.Checks.Count);
    }

    /// <summary>R-004: QR tạm không đủ điều kiện cân chính thức.</summary>
    [Fact]
    public void Temp_qr_blocks_commit()
    {
        var decision = WeighCommitRules.Evaluate(Passing() with { QrType = QrTokenType.Temp });

        Assert.False(decision.CanCommit);
        Assert.Contains(decision.Failed, c => c.Code == "B-001.1");
    }

    [Theory]
    [InlineData(TripLifecycle.Closed)]
    [InlineData(TripLifecycle.Cancelled)]
    public void Closed_or_cancelled_trip_blocks_commit(TripLifecycle lifecycle)
    {
        var decision = WeighCommitRules.Evaluate(Passing() with { TripLifecycle = lifecycle });

        Assert.False(decision.CanCommit);
    }

    /// <summary>U-13: chuyến đang PAUSED không được chốt cân mới.</summary>
    [Fact]
    public void Paused_execution_blocks_commit()
    {
        var decision = WeighCommitRules.Evaluate(
            Passing() with { ExecutionStatus = ExecutionStatus.Paused });

        Assert.False(decision.CanCommit);
    }

    /// <summary>R-039: QR và AI vị trí không đủ chứng minh danh tính xe.</summary>
    [Fact]
    public void Unverified_vehicle_identity_blocks_commit()
    {
        var decision = WeighCommitRules.Evaluate(
            Passing() with { VehicleIdentityVerified = false });

        Assert.False(decision.CanCommit);
        Assert.Contains(decision.Failed, c => c.Code == "B-001.3");
    }

    /// <summary>B-001.4: Invalid và Unknown đều không đạt.</summary>
    [Theory]
    [InlineData(PositionVerdict.Invalid)]
    [InlineData(PositionVerdict.Unknown)]
    public void Non_valid_position_verdict_blocks_commit(PositionVerdict verdict)
    {
        var decision = WeighCommitRules.Evaluate(Passing() with { PositionVerdict = verdict });

        Assert.False(decision.CanCommit);
        Assert.Contains(decision.Failed, c => c.Code == "B-001.4");
    }

    /// <summary>03_Du_lieu mục 4: không chuyển confidence null thành 1.</summary>
    [Fact]
    public void Null_position_confidence_blocks_commit()
    {
        var decision = WeighCommitRules.Evaluate(Passing() with { PositionConfidence = null });

        Assert.False(decision.CanCommit);
        Assert.Contains(decision.Failed, c => c.Code == "B-001.4");
    }

    /// <summary>D-04/D-05: ngưỡng chưa duyệt thì không được chốt cân tự động.</summary>
    [Fact]
    public void Unapproved_thresholds_block_commit()
    {
        var decision = WeighCommitRules.Evaluate(
            Passing() with { Thresholds = new WeighThresholds() });

        Assert.False(decision.CanCommit);
        Assert.Contains(decision.Failed, c => c.Code == "B-001.4");
        Assert.Contains(decision.Failed, c => c.Code == "B-001.5");
        Assert.Contains(decision.Failed, c => c.Code == "B-001.6");
    }

    [Theory]
    [InlineData(ScaleDeviceStatus.Fault)]
    [InlineData(ScaleDeviceStatus.Overload)]
    [InlineData(ScaleDeviceStatus.Unknown)]
    public void Faulty_scale_blocks_commit(ScaleDeviceStatus status)
    {
        var decision = WeighCommitRules.Evaluate(Passing() with { ScaleStatus = status });

        Assert.False(decision.CanCommit);
        Assert.Contains(decision.Failed, c => c.Code == "B-001.5");
    }

    /// <summary>Tín hiệu cũ không được dùng để chốt cân (02_Quy_tac mục 7).</summary>
    [Fact]
    public void Stale_scale_signal_blocks_commit()
    {
        var decision = WeighCommitRules.Evaluate(
            Passing() with { ScaleReadingAge = TimeSpan.FromSeconds(30) });

        Assert.False(decision.CanCommit);
        Assert.Contains(decision.Failed, c => c.Code == "B-001.5");
    }

    /// <summary>B-001.6: chưa đủ cửa sổ ổn định thì chưa chốt.</summary>
    [Fact]
    public void Insufficient_stability_duration_blocks_commit()
    {
        var decision = WeighCommitRules.Evaluate(
            Passing() with { StableDuration = TimeSpan.FromSeconds(3.4) });

        Assert.False(decision.CanCommit);
        Assert.Contains(decision.Failed, c => c.Code == "B-001.6");
    }

    [Fact]
    public void Excessive_fluctuation_blocks_commit()
    {
        var decision = WeighCommitRules.Evaluate(Passing() with { ObservedDeltaKg = 5m });

        Assert.False(decision.CanCommit);
        Assert.Contains(decision.Failed, c => c.Code == "B-001.6");
    }

    /// <summary>S02 tr.21: mía chưa đo tạp chất thì chặn cân lần 2.</summary>
    [Fact]
    public void Missing_impurity_measurement_blocks_commit()
    {
        var decision = WeighCommitRules.Evaluate(
            Passing() with { ImpurityRequirementSatisfied = false });

        Assert.False(decision.CanCommit);
        Assert.Contains(decision.Failed, c => c.Code == "B-001.7");
    }

    /// <summary>R-024: còn giao dịch UNKNOWN thì không phát sinh lượt cân mới.</summary>
    [Fact]
    public void Pending_unknown_transaction_blocks_commit()
    {
        var decision = WeighCommitRules.Evaluate(
            Passing() with { NoPendingUnknownTransaction = false });

        Assert.False(decision.CanCommit);
        Assert.Contains(decision.Failed, c => c.Code == "B-001.8");
    }

    /// <summary>B-001.8: không lưu được bằng chứng thì không phát hành lượt cân.</summary>
    [Fact]
    public void Unpersistable_evidence_blocks_commit()
    {
        var decision = WeighCommitRules.Evaluate(Passing() with { EvidencePersistable = false });

        Assert.False(decision.CanCommit);
        Assert.Contains(decision.Failed, c => c.Code == "B-001.8");
    }

    /// <summary>B-003: phiên không sở hữu bàn cân thì không được chốt.</summary>
    [Fact]
    public void Session_without_bridge_ownership_blocks_commit()
    {
        var decision = WeighCommitRules.Evaluate(Passing() with { SessionOwnsBridge = false });

        Assert.False(decision.CanCommit);
        Assert.Contains(decision.Failed, c => c.Code == "B-001.2");
    }

    /// <summary>
    /// R-014: thiếu chữ đường KHÔNG phải điều kiện chốt cân.
    /// Bối cảnh đạt đủ 8 điều kiện vẫn chốt được dù CCS đang chờ.
    /// </summary>
    [Fact]
    public void Pending_ccs_does_not_block_commit()
    {
        // CCS không xuất hiện trong WeighCommitContext — đúng theo thiết kế.
        var decision = WeighCommitRules.Evaluate(Passing());

        Assert.True(decision.CanCommit);
    }
}
