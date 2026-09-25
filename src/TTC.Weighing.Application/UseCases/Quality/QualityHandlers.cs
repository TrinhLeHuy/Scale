using TTC.Weighing.Application.Abstractions;
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
    string? Reason = null);

public sealed record RecordImpuritySampleResult(
    bool IsSuccess,
    Guid? SampleId,
    decimal? ImpurityPercent,
    bool BelowMinimumSampleWarning,
    string? FailureReason);

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
    IAuditLogger audit)
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

        var calculation = ImpurityCalculator.Calculate(cmd.SampleBeforeKg, cmd.SampleAfterKg);

        if (!calculation.IsValid)
            return new(false, null, null, false, calculation.InvalidReason);

        var previous = await samples.GetEffectiveAsync(cmd.TripId, QualitySampleType.Impurity, ct);

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
            calculation.BelowMinimumSampleWarning, null);
    }
}

public sealed record ReceiveCcsResultCommand(
    Guid TripId,
    decimal? CcsValue,
    string Source,
    bool SourceReportsPending,
    string CorrelationId,
    string? ApprovalRef = null,
    string? Reason = null);

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

        // Chuyến đã CLOSED vẫn nhận được kết quả chữ đường (R-040).
        if (trip.Lifecycle == TripLifecycle.Cancelled)
            return new(false, null, QualityResultStatus.Invalid,
                "Chuyến đã hủy, không tiếp nhận kết quả mới.");

        var status = DetermineStatus(cmd);

        var previous = await samples.GetEffectiveAsync(cmd.TripId, QualitySampleType.Ccs, ct);

        var sample = new QualitySample
        {
            TripId = cmd.TripId,
            Type = QualitySampleType.Ccs,
            Status = status,
            Revision = (previous?.Revision ?? 0) + 1,
            SupersedesId = previous?.Id,
            IsEffective = true,
            CcsValue = status == QualityResultStatus.Valid ? cmd.CcsValue : null,
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
