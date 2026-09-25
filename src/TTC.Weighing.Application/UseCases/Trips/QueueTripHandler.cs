using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Application.UseCases.Trips;

public sealed record QueueTripCommand(
    Guid TripId,
    Guid SiteId,
    VehicleType VehicleType,
    string? PreferredWarehouseCode);

public sealed record QueueTripResult(
    bool IsSuccess,
    Guid? AssignedWeighbridgeId,
    Guid? AssignedWarehouseId,
    string? FailureReason);

/// <summary>
/// Xếp hàng tự động cho chuyến đã đăng tài (Req 9.1, 9.2, 9.5).
/// Phân bổ bàn cân theo quy tắc load balancing: chọn bàn cân có ít chuyến chờ nhất.
/// Nếu không có bàn cân khả dụng, chuyến vẫn đăng tài thành công nhưng chưa gán bàn.
/// </summary>
public sealed class QueueTripHandler(
    IWeighbridgeRepository weighbridges,
    ITripRepository trips)
{
    public async Task<QueueTripResult> HandleAsync(
        QueueTripCommand cmd, CancellationToken ct = default)
    {
        // 1. Lấy danh sách bàn cân khả dụng cho site (IsOutOfService = false)
        var allBridges = await weighbridges.ListBySiteAsync(cmd.SiteId, ct);
        var availableBridges = allBridges
            .Where(b => !b.IsOutOfService)
            .ToList();

        Guid? assignedWeighbridgeId = null;

        if (availableBridges.Count > 0)
        {
            // 2. Load balancing: đếm chuyến chờ per-weighbridge, chọn bàn ít nhất
            var pendingCounts = await trips.CountPendingByWeighbridgeAsync(cmd.SiteId, ct);

            assignedWeighbridgeId = availableBridges
                .OrderBy(b => pendingCounts.TryGetValue(b.Id, out var count) ? count : 0)
                .ThenBy(b => b.Code) // Deterministic tie-break theo mã bàn cân
                .First()
                .Id;
        }

        // 3. Gán kho đích — trong phiên bản hiện tại chưa có Warehouse entity/repository,
        //    kho được truyền qua PreferredWarehouseCode từ kế hoạch nguồn.
        //    Khi chưa có resolver, AssignedWarehouseId sẽ null.
        //    RegisterTripHandler chịu trách nhiệm resolve warehouse code → ID nếu cần.
        Guid? assignedWarehouseId = null;

        // 4. Cập nhật Trip
        var trip = await trips.GetByIdAsync(cmd.TripId, ct);
        if (trip is null)
            return new(false, null, null, "Không tìm thấy chuyến.");

        trip.AssignedWeighbridgeId = assignedWeighbridgeId;
        trip.AssignedWarehouseId = assignedWarehouseId;

        return new(true, assignedWeighbridgeId, assignedWarehouseId, null);
    }
}
