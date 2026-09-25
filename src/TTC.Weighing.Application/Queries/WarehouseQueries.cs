using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Application.Queries;

/// <summary>Dòng xe trong danh sách kho cho UI-04 (R-6.5).</summary>
public sealed record WarehouseVehicleRow(
    Guid TripId,
    string TripNo,
    string? Plate,
    string? Commodity,
    DateTimeOffset? EntryTime,
    DateTimeOffset? ExitTime,
    CheckpointStatus Status,
    string? WaiverReason,
    bool HasOpenMeasurementSession);

/// <summary>Bộ lọc danh sách xe trong kho.</summary>
public sealed record WarehouseFilter
{
    public DateTimeOffset? FromUtc { get; init; }
    public DateTimeOffset? ToUtc { get; init; }

    /// <summary>Chỉ lấy xe đang ở trong kho (có EntryTime, chưa có ExitTime).</summary>
    public bool InsideWarehouseOnly { get; init; }
}

/// <summary>Truy vấn danh sách xe trong kho và xe đã ra kho (R-6.5).</summary>
public interface IWarehouseQueries
{
    Task<IReadOnlyList<WarehouseVehicleRow>> ListAsync(
        Guid warehouseId, WarehouseFilter filter, CancellationToken ct = default);
}
