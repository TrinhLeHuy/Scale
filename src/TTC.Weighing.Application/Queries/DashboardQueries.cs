using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Application.Queries;

/// <summary>Dòng trong danh sách xe của UI-01.</summary>
public sealed record TripBoardRow(
    Guid TripId,
    int Version,
    string TripNo,
    string? ReferenceNo,
    string? Plate,
    string ProfileCode,
    TripLifecycle Lifecycle,
    ExecutionStatus ExecutionStatus,
    string? PauseIssueCode,
    decimal? FirstWeightKg,
    decimal? SecondWeightKg,
    decimal? VehicleNetKg,
    DateTimeOffset? FirstWeighedAt,
    DateTimeOffset? SecondWeighedAt,
    DateTimeOffset CreatedAt);

/// <summary>
/// Số chuyến đang ở từng chặng của vòng đời, dùng vẽ luồng nghiệp vụ trên UI-01.
/// Nguồn: 02_Quy_tac mục 2.1.
/// </summary>
public sealed record LifecycleCounts(
    int TempRegistered,
    int Registered,
    int InProgress,
    int WeighingCompleted,
    int Reconciled,
    int Posted,
    int Closed,
    int Cancelled);

/// <summary>Tổng quan trạng thái nhà máy (R-028).</summary>
public sealed record SiteOverview(
    int WaitingForWeighing,
    int InsidePlant,
    int FirstWeightOnly,
    int PausedTrips,
    int ReconciliationBlockers,
    int IntegrationAttention,
    LifecycleCounts Lifecycle,
    int TotalTripsInRange,
    decimal? TotalVehicleNetKgInRange,
    int ManualInterventions,
    IReadOnlyList<DeviceHealthRow> Devices);

/// <param name="IsOnline">Null nghĩa là chưa có kênh giám sát cho thiết bị này.</param>
public sealed record DeviceHealthRow(
    string DeviceCode,
    string DeviceClass,
    string LaneCode,
    bool IsEnabled,
    bool? IsOnline,
    DateTimeOffset? LastSeenAt);

public interface IDashboardQueries
{
    Task<SiteOverview> GetOverviewAsync(Guid siteId, CancellationToken ct = default);

    Task<IReadOnlyList<TripBoardRow>> ListTripsAsync(
        Guid siteId, TripBoardFilter filter, CancellationToken ct = default);
}

/// <summary>Lọc theo ngày/ca/loại hàng/đối tác/xe (R-028).</summary>
public sealed record TripBoardFilter
{
    public DateTimeOffset? FromUtc { get; init; }
    public DateTimeOffset? ToUtc { get; init; }
    public string? ProfileCode { get; init; }
    public TripLifecycle? Lifecycle { get; init; }
    public ExecutionStatus? ExecutionStatus { get; init; }
    public string? PlateContains { get; init; }

    /// <summary>Chỉ lấy chuyến có cân lần 1 mà chưa có lần 2.</summary>
    public bool FirstWeightOnly { get; init; }

    /// <summary>Tách lưới Mía / Vật tư.</summary>
    public TripCategory Category { get; init; } = TripCategory.All;
}
