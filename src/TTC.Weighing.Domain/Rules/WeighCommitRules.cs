using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Domain.Rules;

/// <summary>Kết quả kiểm tra một điều kiện đơn lẻ, để UI-03 hiển thị checklist.</summary>
public sealed record ConditionCheck(string Code, string Name, bool Passed, string? Detail = null);

/// <summary>
/// Quyết định chốt cân do server tính. UI không được tự suy ra kết quả này.
/// </summary>
public sealed record WeighCommitDecision(bool CanCommit, IReadOnlyList<ConditionCheck> Checks)
{
    public IEnumerable<ConditionCheck> Failed => Checks.Where(c => !c.Passed);

    public string BlockSummary => CanCommit
        ? string.Empty
        : string.Join(Environment.NewLine, Failed.Select(c => $"[{c.Code}] {c.Name}: {c.Detail}"));
}

/// <summary>
/// Ngưỡng lấy từ WorkflowProfile đã duyệt. null = chưa duyệt (các điểm D-04/D-05),
/// và chưa duyệt thì KHÔNG được chốt cân tự động.
/// </summary>
public sealed record WeighThresholds
{
    /// <summary>Cửa sổ ổn định. S02 tr.17 và S04 tr.26 nêu 5 giây; vẫn để cấu hình theo profile.</summary>
    public TimeSpan? StabilityWindow { get; init; }

    /// <summary>Biên dao động cho phép trong cửa sổ ổn định. D-04 — không tự đặt ±1 kg cho mọi đầu cân.</summary>
    public decimal? StabilityDeltaKg { get; init; }

    /// <summary>Độ mới tối đa của tín hiệu cân. D-04.</summary>
    public TimeSpan? ScaleSignalMaxAge { get; init; }

    /// <summary>Độ mới tối đa của bằng chứng AI. D-05.</summary>
    public TimeSpan? AiEvidenceMaxAge { get; init; }

    /// <summary>Ngưỡng confidence AI vị trí. D-05.</summary>
    public decimal? PositionConfidenceThreshold { get; init; }
}

/// <summary>Ảnh chụp trạng thái tại thời điểm đánh giá chốt cân.</summary>
public sealed record WeighCommitContext
{
    // 1. Chuyến và QR
    public required TripLifecycle TripLifecycle { get; init; }
    public required ExecutionStatus ExecutionStatus { get; init; }
    public required QrTokenType QrType { get; init; }
    public required QrTokenStatus QrStatus { get; init; }
    public required bool QrMatchesTrip { get; init; }
    public required bool QrMatchesSite { get; init; }
    public required bool CheckpointAllowedByProfile { get; init; }

    // 2. Thiết bị và phiên
    public required bool DevicesMappedToLane { get; init; }
    public required bool SessionOwnsBridge { get; init; }

    // 3. Danh tính xe (D-03) — QR/AI vị trí không đủ để chứng minh danh tính (R-039)
    public required bool VehicleIdentityVerified { get; init; }
    public string? IdentityVerificationMethod { get; init; }

    // 4. AI vị trí
    public required PositionVerdict PositionVerdict { get; init; }
    public decimal? PositionConfidence { get; init; }
    public TimeSpan? PositionEvidenceAge { get; init; }
    public required bool PositionRegionConfigValid { get; init; }

    // 5. Tín hiệu đầu cân
    public required ScaleDeviceStatus ScaleStatus { get; init; }
    public TimeSpan? ScaleReadingAge { get; init; }
    public required bool WithinScaleCapacity { get; init; }
    public required bool SignalContinuous { get; init; }

    // 6. Ổn định
    public required TimeSpan StableDuration { get; init; }
    public decimal? ObservedDeltaKg { get; init; }

    // 7. Điều kiện nghiệp vụ trước lượt cân
    public required bool PrecedingWeightsSatisfied { get; init; }
    public required bool ImpurityRequirementSatisfied { get; init; }
    public required bool PolicyRequirementSatisfied { get; init; }

    // 8. Khả năng lưu bằng chứng
    public required bool EvidencePersistable { get; init; }
    public required bool NoPendingUnknownTransaction { get; init; }

    public required WeighThresholds Thresholds { get; init; }
}

/// <summary>
/// B-001 — Một lần chốt cân là một quyết định có bằng chứng (R-007).
/// Tất cả 8 nhóm điều kiện phải đồng thời thỏa. Không có nút bỏ qua.
/// </summary>
public static class WeighCommitRules
{
    public static WeighCommitDecision Evaluate(WeighCommitContext ctx)
    {
        var checks = new List<ConditionCheck>
        {
            CheckTripAndQr(ctx),
            CheckDeviceBinding(ctx),
            CheckVehicleIdentity(ctx),
            CheckPositionEvidence(ctx),
            CheckScaleSignal(ctx),
            CheckStability(ctx),
            CheckBusinessPreconditions(ctx),
            CheckPersistence(ctx)
        };

        return new WeighCommitDecision(checks.All(c => c.Passed), checks);
    }

    private static ConditionCheck CheckTripAndQr(WeighCommitContext c)
    {
        const string code = "B-001.1";
        const string name = "QR và chuyến hợp lệ";

        // QR tạm cho phép cân (cân offline — sau đẩy lên hệ thống chính).
        // Chỉ cần QR chưa thu hồi và chuyến chưa hủy/đóng.

        if (c.QrStatus != QrTokenStatus.Issued && c.QrType != QrTokenType.Temp)
            return new(code, name, false, $"QR ở trạng thái {c.QrStatus}.");

        if (!c.QrMatchesTrip)
            return new(code, name, false, "QR không thuộc chuyến đang cân.");

        if (!c.QrMatchesSite)
            return new(code, name, false, "QR không thuộc nhà máy hiện tại.");

        if (!c.CheckpointAllowedByProfile)
            return new(code, name, false, "Checkpoint không được profile cho phép ở bước này.");

        if (c.TripLifecycle is TripLifecycle.Closed or TripLifecycle.Cancelled)
            return new(code, name, false, $"Chuyến đã {c.TripLifecycle}.");

        if (c.ExecutionStatus == ExecutionStatus.Paused)
            return new(code, name, false, "Chuyến đang PAUSED, phải xử lý sự cố trước (U-13).");

        return new(code, name, true);
    }

    private static ConditionCheck CheckDeviceBinding(WeighCommitContext c)
    {
        const string code = "B-001.2";
        const string name = "Thiết bị đúng làn và phiên độc quyền";

        if (!c.DevicesMappedToLane)
            return new(code, name, false, "Bàn cân/camera/đầu đọc không thuộc đúng làn đã cấu hình.");

        if (!c.SessionOwnsBridge)
            return new(code, name, false, "Phiên cân không sở hữu độc quyền bàn cân này (B-003).");

        return new(code, name, true);
    }

    private static ConditionCheck CheckVehicleIdentity(WeighCommitContext c)
    {
        const string code = "B-001.3";
        const string name = "Danh tính xe đã xác thực";

        if (!c.VehicleIdentityVerified)
            return new(code, name, false,
                "Chưa xác thực danh tính xe theo D-03. QR và AI vị trí không đủ để kết luận (R-039).");

        return new(code, name, true, $"Phương thức: {c.IdentityVerificationMethod ?? "không rõ"}");
    }

    private static ConditionCheck CheckPositionEvidence(WeighCommitContext c)
    {
        const string code = "B-001.4";
        const string name = "AI vị trí hợp lệ";

        if (!c.PositionRegionConfigValid)
            return new(code, name, false, "Cấu hình vùng quan sát sai — báo lỗi, không tự giả định vùng mới.");

        if (c.PositionVerdict != PositionVerdict.Valid)
            return new(code, name, false, $"Kết luận AI vị trí = {c.PositionVerdict}, không đạt.");

        if (c.Thresholds.PositionConfidenceThreshold is null)
            return new(code, name, false, "Ngưỡng confidence AI chưa được duyệt (D-05).");

        // Không chuyển confidence null thành 1 (03_Du_lieu mục 4).
        if (c.PositionConfidence is null)
            return new(code, name, false, "Thiếu confidence từ dịch vụ AI.");

        if (c.PositionConfidence < c.Thresholds.PositionConfidenceThreshold)
            return new(code, name, false,
                $"Confidence {c.PositionConfidence} dưới ngưỡng {c.Thresholds.PositionConfidenceThreshold}.");

        if (c.Thresholds.AiEvidenceMaxAge is null)
            return new(code, name, false, "Ngưỡng độ mới bằng chứng AI chưa được duyệt (D-05).");

        if (c.PositionEvidenceAge is null || c.PositionEvidenceAge > c.Thresholds.AiEvidenceMaxAge)
            return new(code, name, false, "Bằng chứng AI đã cũ hoặc thiếu dấu thời gian.");

        return new(code, name, true);
    }

    private static ConditionCheck CheckScaleSignal(WeighCommitContext c)
    {
        const string code = "B-001.5";
        const string name = "Tín hiệu đầu cân hợp lệ";

        if (c.ScaleStatus != ScaleDeviceStatus.Ok)
            return new(code, name, false, $"Trạng thái đầu cân = {c.ScaleStatus}.");

        if (!c.WithinScaleCapacity)
            return new(code, name, false, "Giá trị ngoài khả năng đo đã cấu hình.");

        if (!c.SignalContinuous)
            return new(code, name, false, "Mất gói tín hiệu làm đứt cửa sổ đánh giá.");

        if (c.Thresholds.ScaleSignalMaxAge is null)
            return new(code, name, false, "Ngưỡng độ mới tín hiệu cân chưa được duyệt (D-04).");

        if (c.ScaleReadingAge is null || c.ScaleReadingAge > c.Thresholds.ScaleSignalMaxAge)
            return new(code, name, false, "Tín hiệu cân đã cũ. Không dùng số cuối như đang trực tuyến.");

        return new(code, name, true);
    }

    private static ConditionCheck CheckStability(WeighCommitContext c)
    {
        const string code = "B-001.6";
        const string name = "Trọng lượng ổn định liên tục";

        if (c.Thresholds.StabilityWindow is null)
            return new(code, name, false, "Cửa sổ ổn định chưa được cấu hình cho profile này.");

        if (c.Thresholds.StabilityDeltaKg is null)
            return new(code, name, false,
                "Biên dao động chưa được duyệt (D-04). Không áp một ngưỡng cho mọi đầu cân.");

        if (c.ObservedDeltaKg is null)
            return new(code, name, false, "Thiếu số liệu dao động trong cửa sổ.");

        if (c.ObservedDeltaKg > c.Thresholds.StabilityDeltaKg)
            return new(code, name, false,
                $"Dao động {c.ObservedDeltaKg} kg vượt biên {c.Thresholds.StabilityDeltaKg} kg.");

        if (c.StableDuration < c.Thresholds.StabilityWindow)
            return new(code, name, false,
                $"Ổn định {c.StableDuration.TotalSeconds:F1}s / {c.Thresholds.StabilityWindow.Value.TotalSeconds:F1}s.");

        return new(code, name, true);
    }

    private static ConditionCheck CheckBusinessPreconditions(WeighCommitContext c)
    {
        const string code = "B-001.7";
        const string name = "Điều kiện nghiệp vụ trước lượt cân";

        if (!c.PrecedingWeightsSatisfied)
            return new(code, name, false, "Chưa có lượt cân trước hợp lệ cho vai trò này.");

        // Mía: chặn cân lần 2 khi chưa đo tạp chất (S02 tr.21).
        // Lưu ý: KHÔNG chặn vì chưa có chữ đường (S02 tr.20-22, R-014).
        if (!c.ImpurityRequirementSatisfied)
            return new(code, name, false, "Mía chưa đo tạp chất, chặn cân lần 2 (S02 tr.21).");

        if (!c.PolicyRequirementSatisfied)
            return new(code, name, false, "Điều kiện chính sách FRM chưa đạt theo hợp đồng đã xác nhận.");

        return new(code, name, true);
    }

    private static ConditionCheck CheckPersistence(WeighCommitContext c)
    {
        const string code = "B-001.8";
        const string name = "Lưu được bằng chứng và không có giao dịch treo";

        if (!c.EvidencePersistable)
            return new(code, name, false, "Không lưu được ảnh/bằng chứng, không phát hành lượt cân.");

        if (!c.NoPendingUnknownTransaction)
            return new(code, name, false,
                "Còn giao dịch chưa rõ kết quả. Không phát sinh lượt mới bằng cách bấm lại (R-024).");

        return new(code, name, true);
    }
}
