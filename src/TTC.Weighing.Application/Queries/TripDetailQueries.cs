using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Application.Queries;

/// <summary>Chi tiết chuyến cho UI-06: timeline, các lần cân, đối soát, tích hợp, blocker.</summary>
public sealed record TripDetailView(
    TripBoardRow Header,
    IReadOnlyList<CheckpointRow> Checkpoints,
    IReadOnlyList<WeighEventRow> WeighEvents,
    IReadOnlyList<QualitySampleRow> QualitySamples,
    IReadOnlyList<MeasurementSessionRow> MeasurementSessions,
    IReadOnlyList<ReconciliationCheckRow> ReconciliationChecks,
    IReadOnlyList<AllocationRow> Allocations,
    IReadOnlyList<IntegrationMessageRow> IntegrationMessages,
    IReadOnlyList<AuditRow> AuditTrail,
    IReadOnlyList<string> Blockers);

public sealed record CheckpointRow(
    CheckpointType Type,
    CheckpointStatus Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? ConfirmedBy,
    string? WaiverReason);

/// <summary>Mọi bản cân đều hiển thị, kể cả bản đã bị thay thế (R-026).</summary>
public sealed record WeighEventRow(
    Guid Id,
    int Revision,
    bool IsEffective,
    WeightRole Role,
    decimal RawWeight,
    string RawUom,
    decimal NormalizedKg,
    WeighSource Source,
    DateTimeOffset MeasuredAt,
    TimeSpan StableWindow,
    string? Reason,
    string? ApprovalRef,
    int EvidenceCount);

public sealed record QualitySampleRow(
    Guid? TripId,
    string? TripNo,
    string? Plate,
    QualitySampleType Type,
    QualityResultStatus Status,
    int Revision,
    bool IsEffective,
    decimal? SampleBeforeKg,
    decimal? SampleAfterKg,
    decimal? ImpurityPercent,
    decimal? CcsValue,
    bool BelowMinimumSampleWarning,
    string Source,
    DateTimeOffset? ReceivedAt);

public sealed record MeasurementSessionRow(
    Guid Id,
    string ItemCode,
    MeasurementSessionStatus Status,
    IotReadingMode ReadingMode,
    MassBasis MassBasis,
    decimal? IotMassKg,
    MassOrigin? IotMassOrigin,
    int? ForwardBagCount,
    int? ReverseBagCount,
    int? NetBagCount,
    bool DataComplete,
    string? HoldReason);

public sealed record ReconciliationCheckRow(
    ReconciliationCheckType CheckType,
    ReconciliationVerdict Verdict,
    decimal? ObservedValue,
    decimal? ExpectedValue,
    decimal? LowerBound,
    decimal? UpperBound,
    decimal? Difference,
    string Unit,
    string? TolerancePolicyId,
    string? Detail);

public sealed record AllocationRow(
    string? OwnerRef,
    Guid? DocumentLineId,
    decimal AllocatedKg,
    AllocationMethod Method,
    decimal? Ratio,
    decimal RoundingAdjustmentKg,
    bool IsApproved);

public sealed record IntegrationMessageRow(
    Guid Id,
    string Destination,
    string Operation,
    string BusinessKey,
    string? ShipmentNo,
    IntegrationMessageStatus Status,
    int Attempts,
    DateTimeOffset? LastAttemptAt,
    string? BusinessErrorCode,
    string? BusinessErrorMessage,
    string? ExternalDocumentRef,
    bool RequiresManualReconciliation);

public sealed record AuditRow(
    DateTimeOffset OccurredAt,
    string ActionType,
    string Actor,
    string? Reason,
    string? BeforeJson,
    string? AfterJson);

public interface ITripDetailQueries
{
    Task<TripDetailView?> GetAsync(Guid tripId, CancellationToken ct = default);

    /// <summary>Load tất cả quality samples trong khung ngày cho 1 site.</summary>
    Task<IReadOnlyList<QualitySampleRow>> ListAllSamplesAsync(
        Guid siteId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);
}

/// <summary>Hàng chờ ngoại lệ cho UI-07 (R-009, R-010, R-025).</summary>
public sealed record ExceptionQueueItem(
    Guid TripId,
    string TripNo,
    string Category,
    string Description,
    DateTimeOffset RaisedAt,
    string? RaisedBy,
    string? NextAllowedAction);

public interface IExceptionQueueQueries
{
    Task<IReadOnlyList<ExceptionQueueItem>> ListAsync(Guid siteId, CancellationToken ct = default);
}

public interface IIntegrationMonitorQueries
{
    Task<IReadOnlyList<IntegrationMessageRow>> ListAsync(
        Guid siteId, bool onlyRequiringAttention, CancellationToken ct = default);
}

/// <summary>Dòng báo cáo lượt cân cho UI-10 (R-028).</summary>
public sealed record WeighReportRow(
    string TripNo,
    string? ReferenceNo,
    string ProfileCode,
    string? Plate,
    decimal? GrossKg,
    decimal? TareKg,
    decimal? NetKg,
    decimal? ImpurityPercent,
    decimal? CcsValue,
    ReconciliationVerdict? OverallVerdict,
    int ManualInterventionCount,
    DateTimeOffset? CompletedAt);

public interface IReportQueries
{
    Task<IReadOnlyList<WeighReportRow>> ListWeighReportAsync(
        Guid siteId, TripBoardFilter filter, CancellationToken ct = default);
}
