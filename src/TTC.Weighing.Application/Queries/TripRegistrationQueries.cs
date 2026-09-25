using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Application.Queries;

/// <summary>Dòng trong lưới đăng tài UI-02 (R-003, R-004, R-005).</summary>
public sealed record TripRegistrationRow(
    Guid TripId,
    int Version,
    string TripNo,
    string? ShipmentNo,
    string? Plate,
    int? QueueSequenceNumber,
    string? DriverRef,
    DateTimeOffset? EntryTime,
    DateTimeOffset? ExitTime,
    string? Transporter,
    string? CustomerOrSupplier,
    string? Commodity,
    TripLifecycle Lifecycle,
    string? VehicleType);

/// <summary>Bộ lọc lưới đăng tài: tab nhanh + khoảng ngày (Req 7.2, 7.3).</summary>
public sealed record TripRegistrationFilter
{
    public DateTimeOffset? FromUtc { get; init; }
    public DateTimeOffset? ToUtc { get; init; }
    public TripGridTab Tab { get; init; } = TripGridTab.InFactory;
}

/// <summary>
/// Tab lọc nhanh trên lưới đăng tài.
/// InFactory: xe đã vào chưa ra. Exited: xe đã ra cổng. Cancelled: phiếu đã hủy.
/// </summary>
public enum TripGridTab
{
    /// <summary>Trong nhà máy: đã vào chưa ra.</summary>
    InFactory,

    /// <summary>Đã ra cổng.</summary>
    Exited,

    /// <summary>Phiếu đã hủy.</summary>
    Cancelled
}

/// <summary>Query lưới danh sách chuyến đăng tài cho UI-02.</summary>
public interface ITripRegistrationQueries
{
    /// <summary>
    /// Lấy danh sách chuyến theo site, lọc theo tab và khoảng ngày.
    /// </summary>
    Task<IReadOnlyList<TripRegistrationRow>> ListAsync(
        Guid siteId, TripRegistrationFilter filter, CancellationToken ct = default);
}
