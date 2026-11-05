using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Application.Queries;

/// <summary>
/// Dòng trong lưới xe đã cân trên WeighOperationForm (UI-03).
/// Hiển thị thông tin cân vào/ra, trọng lượng hàng, và trạng thái đồng bộ.
/// </summary>
public sealed record WeighedTripRow(
    Guid TripId,
    string RegNo,
    string? TicketNo,
    string? TripNumber,
    string? ShipmentNo,
    string? Plate,
    string? Trailer,
    DateTimeOffset? TimeIn,
    decimal? WeightIn,
    DateTimeOffset? TimeOut,
    decimal? WeightOut,
    decimal? NetWeight,
    string? Customer,
    string? Commodity,
    IntegrationMessageStatus? SyncStatus,
    // Mía: tạp chất / CCS thực tế (mẫu hiệu lực đã đo) và thanh toán (chính sách FRM). Vật tư: null.
    decimal? ImpurityActual = null,
    decimal? ImpurityPaid = null,
    decimal? CcsActual = null,
    decimal? CcsPaid = null);

/// <summary>Bộ lọc cho lưới xe đã cân (R-028, UI-03).</summary>
public sealed record WeighedTripFilter
{
    /// <summary>Lọc từ thời điểm (UTC).</summary>
    public DateTimeOffset? FromUtc { get; init; }

    /// <summary>Lọc đến thời điểm (UTC).</summary>
    public DateTimeOffset? ToUtc { get; init; }

    /// <summary>Chỉ lấy chuyến có cân lần 1 mà chưa có lần 2.</summary>
    public bool FirstWeightOnly { get; init; }

    /// <summary>Chỉ lấy chuyến đã hoàn thành cả cân lần 1 và lần 2.</summary>
    public bool CompletedOnly { get; init; }

    /// <summary>Tách lưới Mía / Vật tư.</summary>
    public TripCategory Category { get; init; } = TripCategory.All;
}

/// <summary>
/// Query lưới xe đã cân cho WeighOperationForm (UI-03).
/// Dữ liệu join trips + weigh_events + integration_messages.
/// </summary>
public interface IWeighOperationQueries
{
    /// <summary>
    /// Lấy danh sách xe đã cân tại bàn cân, hỗ trợ filter theo ngày và trạng thái.
    /// </summary>
    Task<IReadOnlyList<WeighedTripRow>> ListAsync(
        Guid siteId, Guid weighbridgeId, WeighedTripFilter filter,
        CancellationToken ct = default);

    /// <summary>
    /// Lấy danh sách xe đang chờ cân (Registered/TempRegistered/InProgress, chưa có WeighEvent).
    /// </summary>
    Task<IReadOnlyList<WeighedTripRow>> ListPendingAsync(
        Guid siteId, TripCategory category = TripCategory.All, CancellationToken ct = default);
}
