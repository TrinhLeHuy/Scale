using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.WinForms.Presenters;

/// <summary>
/// Bối cảnh làm việc hiện tại của trạm cân: chuyến nào, bàn nào, lượt cân nào.
/// Vận hành thật lấy từ thao tác quét QR tại đầu đọc (ResolveQR), không do form tự chọn.
/// </summary>
public sealed record WeighWorkspace(
    Guid TripId,
    int TripVersion,
    string TripNo,
    string? ReferenceNo,
    string? Plate,
    Guid WeighbridgeId,
    string WeighbridgeCode,
    Guid LaneId,
    CheckpointType Checkpoint,
    int SegmentIndex);

public interface IWeighWorkspaceProvider
{
    Task<WeighWorkspace?> GetCurrentAsync(CancellationToken ct = default);
}
