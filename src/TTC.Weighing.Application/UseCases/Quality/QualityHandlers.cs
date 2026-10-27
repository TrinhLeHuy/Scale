using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Domain.Common;
using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Domain.Rules;

namespace TTC.Weighing.Application.UseCases.Quality;

public sealed record RecordImpuritySampleCommand(
    Guid TripId,
    decimal SampleBeforeKg,
    decimal SampleAfterKg,
    string Source,
    string CorrelationId,
    string? ApprovalRef = null,
    string? Reason = null,
    int WeighingCount = 2,
    int? AdjustedWeighing = null);

/// <summary>Tham số luồng đo tạp chất (đọc từ cấu hình SugarcaneFlow:Quality).</summary>
public sealed class SugarcaneQualityOptions
{
    /// <summary>
    /// Số lần CÂN tối đa cho một lần đo tạp chất: lần 1 (mẫu trước) + lần 2 (mẫu sau)
    /// + 1 lần điều chỉnh lại lần 1 hoặc lần 2 (nếu có) = 3.
    /// </summary>
    public int MaxImpurityWeighings { get; init; } = 3;

    /// <summary>
    /// Khối lượng mẫu lần 1 tối thiểu (kg) — lấy từ Quy tắc cân trên Portal theo đơn vị, đọc lại mỗi lần ghi.
    /// Dưới mức này: chặn ghi. Null = chưa có từ Portal → không kiểm (không dùng số hardcode).
    /// </summary>
    public Func<decimal?> MinSampleKgProvider { get; init; } = () => null;
}

public sealed record RecordImpuritySampleResult(
    bool IsSuccess,
    Guid? SampleId,
    decimal? ImpurityPercent,
    bool BelowMinimumSampleWarning,
    string? FailureReason,
    int? MeasurementNumber = null);

/// <summary>
/// R-013 — Ghi kết quả cân tạp chất. Lấy lại mẫu tạo bản mới, giữ lịch sử.
/// Nhập tay phải có quyền và lý do; không dùng để bỏ qua trạng thái lỗi.
/// </summary>
public sealed class RecordImpuritySampleHandler(
    ITripRepository trips,
    IQualitySampleRepository samples,
    IWorkflowProfileRepository profiles,
    IUnitOfWork uow,
    IClock clock,
    ICurrentUser user,
    IAuditLogger audit,
    SugarcaneQualityOptions options)
{
    public const string RequiredPermission = "quality.impurity.record";
    public const string ManualEntryPermission = "quality.impurity.manual";

    public async Task<RecordImpuritySampleResult> HandleAsync(
        RecordImpuritySampleCommand cmd, CancellationToken ct = default)
    {
        var trip = await trips.GetByIdAsync(cmd.TripId, ct);
        if (trip is null)
            return new(false, null, null, false, "Chuyến không tồn tại.");

        if (!user.HasPermission(RequiredPermission, trip.SiteId))
            return new(false, null, null, false, "Không có quyền ghi kết quả tạp chất.");

        var isManual = string.Equals(cmd.Source, "MANUAL", StringComparison.OrdinalIgnoreCase);

        if (isManual)
        {
            if (!user.HasPermission(ManualEntryPermission, trip.SiteId))
                return new(false, null, null, false, "Không có quyền nhập tay kết quả tạp chất.");

            if (string.IsNullOrWhiteSpace(cmd.Reason))
                return new(false, null, null, false, "Nhập tay phải có lý do.");
        }

        if (!trip.IsOperable)
            return new(false, null, null, false,
                $"Chuyến không cho phép thao tác: {trip.Lifecycle} / {trip.ExecutionStatus}.");

        var profile = await profiles.GetEffectiveAsync(
            trip.WorkflowProfileId, trip.WorkflowProfileVersion, ct);

        var minSampleKg = options.MinSampleKgProvider();
        var calculation = ImpurityCalculator.Calculate(cmd.SampleBeforeKg, cmd.SampleAfterKg, minSampleKg ?? 0m);

        if (!calculation.IsValid)
            return new(false, null, null, false, calculation.InvalidReason);

        // Mẫu lần 1 dưới khối lượng tối thiểu (Portal) → không ghi.
        if (minSampleKg is not null && calculation.BelowMinimumSampleWarning)
            return new(false, null, null, true,
                Loc("rule.impurity_sample_below_min",
                    "Khối lượng mẫu lần 1 ({0:N2} kg) nhỏ hơn mức tối thiểu {1:N2} kg. Lấy lại mẫu.",
                    cmd.SampleBeforeKg, minSampleKg.Value));

        // Tối đa 3 lần cân: 2 lần đầu + 1 lần điều chỉnh (chỉ sửa lần 1 HOẶC lần 2).
        var adjustmentValid = cmd.AdjustedWeighing is null
            ? cmd.WeighingCount == 2
            : cmd.AdjustedWeighing is 1 or 2 && cmd.WeighingCount == 3;
        if (cmd.WeighingCount > options.MaxImpurityWeighings || !adjustmentValid)
            return new(false, null, null, false,
                Loc("rule.impurity_weighing_count",
                    "Số lần cân tạp chất không hợp lệ ({0}). Tối đa {1} lần: 2 lần đầu + 1 lần điều chỉnh.",
                    cmd.WeighingCount, options.MaxImpurityWeighings));

        // Mỗi chuyến chỉ một kết quả tạp chất — sai thì điều chỉnh trong lần cân thứ 3, không đo lại.
        var previous = await samples.GetEffectiveAsync(cmd.TripId, QualitySampleType.Impurity, ct);
        if (previous is not null)
            return new(false, null, null, false,
                Loc("rule.impurity_already_measured",
                    "Chuyến đã có kết quả tạp chất ({0:N4} %). Mỗi chuyến chỉ đo một lần.",
                    previous.ImpurityPercent));

        var sample = new QualitySample
        {
            TripId = cmd.TripId,
            Type = QualitySampleType.Impurity,
            Status = QualityResultStatus.Valid,
            Revision = (previous?.Revision ?? 0) + 1,
            SupersedesId = previous?.Id,
            IsEffective = true,
            SampleBeforeKg = cmd.SampleBeforeKg,
            SampleAfterKg = cmd.SampleAfterKg,
            ImpurityPercent = calculation.ImpurityPercent,
            BelowMinimumSampleWarning = calculation.BelowMinimumSampleWarning,
            Source = cmd.Source.ToUpperInvariant(),
            ReceivedAt = clock.UtcNow,
            ReceivedBy = user.UserId,
            ApprovalRef = cmd.ApprovalRef,
            Reason = cmd.Reason,
            CreatedAt = clock.UtcNow,
            CreatedBy = user.UserId
        };

        // Bản cũ giữ nguyên để tra cứu, chỉ hết hiệu lực.
        if (previous is not null)
        {
            previous.IsEffective = false;
            previous.Status = QualityResultStatus.Superseded;
        }

        await samples.AddAsync(sample, ct);

        await audit.LogAsync("IMPURITY_RECORDED", cmd.TripId, user.UserId,
            previous is null ? null : $"{{\"revision\":{previous.Revision},\"percent\":{previous.ImpurityPercent}}}",
            $"{{\"revision\":{sample.Revision},\"percent\":{calculation.ImpurityPercent}}}",
            cmd.Reason, cmd.CorrelationId, ct);

        await uow.SaveChangesAsync(ct);

        _ = profile; // Ngưỡng mẫu tối thiểu theo profile sẽ dùng khi D-06 chốt giá trị.

        return new(true, sample.Id, calculation.ImpurityPercent,
            calculation.BelowMinimumSampleWarning, null, sample.Revision);
    }

    private static string Loc(string key, string fallback, params object?[] args)
        => DomainText.T(key, fallback, args);
}

public sealed record ReceiveCcsResultCommand(
    Guid TripId,
    decimal? CcsValue,
    string Source,
    bool SourceReportsPending,
    string CorrelationId,
    string? ApprovalRef = null,
    string? Reason = null,
    decimal? Brix = null,
    decimal? Pol = null,
    decimal? PolCorrected = null);

public sealed record ReceiveCcsResultResult(
    bool IsSuccess,
    Guid? SampleId,
    QualityResultStatus Status,
    string? FailureReason);

/// <summary>
/// R-014 / R-040 — Tiếp nhận chữ đường. Kết quả có thể đến SAU cân lần 2 và sau khi chuyến CLOSED,
/// không mở lại QR hoặc barie. Kết quả không hợp lệ giữ cờ không đồng bộ.
/// </summary>
public sealed class ReceiveCcsResultHandler(
    ITripRepository trips,
    IQualitySampleRepository samples,
    IUnitOfWork uow,
    IClock clock,
    ICurrentUser user,
    IAuditLogger audit)
{
    public const string RequiredPermission = "quality.ccs.receive";

    public async Task<ReceiveCcsResultResult> HandleAsync(
        ReceiveCcsResultCommand cmd, CancellationToken ct = default)
    {
        var trip = await trips.GetByIdAsync(cmd.TripId, ct);
        if (trip is null)
            return new(false, null, QualityResultStatus.Invalid, "Chuyến không tồn tại.");

        if (!user.HasPermission(RequiredPermission, trip.SiteId))
            return new(false, null, QualityResultStatus.Invalid, "Không có quyền tiếp nhận chữ đường.");

        // Chuyến vật tư (ERP) không có chữ đường.
        if (string.Equals(trip.SourceSystem, "ERP", StringComparison.OrdinalIgnoreCase))
            return new(false, null, QualityResultStatus.Invalid,
                DomainText.T("rule.ccs_material_trip", "Chuyến vật tư không đo chữ đường."));

        // Chuyến đã CLOSED vẫn nhận được kết quả chữ đường (R-040).
        if (trip.Lifecycle == TripLifecycle.Cancelled)
            return new(false, null, QualityResultStatus.Invalid,
                "Chuyến đã hủy, không tiếp nhận kết quả mới.");

        var status = DetermineStatus(cmd);

        var previous = await samples.GetEffectiveAsync(cmd.TripId, QualitySampleType.Ccs, ct);

        // Mẫu đã có CCS hợp lệ → không đo lần 2. Kết quả "đang chờ" vẫn được cập nhật khi có số thật.
        if (previous is { Status: QualityResultStatus.Valid })
            return new(false, null, QualityResultStatus.Invalid,
                DomainText.T("rule.ccs_already_measured", "Mẫu đã đo CCS (lần {0}). Không đo CCS lần 2.",
                    previous.Revision));

        var sample = new QualitySample
        {
            TripId = cmd.TripId,
            Type = QualitySampleType.Ccs,
            Status = status,
            Revision = (previous?.Revision ?? 0) + 1,
            SupersedesId = previous?.Id,
            IsEffective = true,
            CcsValue = status == QualityResultStatus.Valid ? cmd.CcsValue : null,
            BrixValue = status == QualityResultStatus.Valid ? cmd.Brix : null,
            PolValue = status == QualityResultStatus.Valid ? cmd.Pol : null,
            PolCorrected = status == QualityResultStatus.Valid ? cmd.PolCorrected : null,
            Source = cmd.Source.ToUpperInvariant(),
            ReceivedAt = clock.UtcNow,
            ReceivedBy = user.UserId,
            ApprovalRef = cmd.ApprovalRef,
            Reason = cmd.Reason,
            CreatedAt = clock.UtcNow,
            CreatedBy = user.UserId
        };

        if (previous is not null)
        {
            previous.IsEffective = false;
            previous.Status = QualityResultStatus.Superseded;
        }

        await samples.AddAsync(sample, ct);

        await audit.LogAsync("CCS_RECEIVED", cmd.TripId, user.UserId,
            null, $"{{\"status\":\"{status}\",\"value\":{cmd.CcsValue?.ToString() ?? "null"}}}",
            cmd.Reason, cmd.CorrelationId, ct);

        await uow.SaveChangesAsync(ct);

        return new(true, sample.Id, status, null);
    }

    private static QualityResultStatus DetermineStatus(ReceiveCcsResultCommand cmd)
    {
        // Nguồn báo đang chờ là trạng thái hợp lệ, KHÔNG phải lỗi thiết bị/API (02_Quy_tac mục 7).
        if (cmd.SourceReportsPending)
            return QualityResultStatus.Pending;

        // Giá trị thiếu hoặc âm là kết quả không hợp lệ, giữ cờ không đồng bộ.
        return cmd.CcsValue is null or < 0
            ? QualityResultStatus.Invalid
            : QualityResultStatus.Valid;
    }
}
