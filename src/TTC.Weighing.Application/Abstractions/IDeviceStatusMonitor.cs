using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Application.Abstractions;

/// <summary>
/// Giám sát realtime trạng thái kết nối thiết bị tại nhà máy.
/// UI poll định kỳ (mỗi 2 giây) để cập nhật thanh trạng thái trên form cân.
/// </summary>
public interface IDeviceStatusMonitor
{
    /// <summary>
    /// Lấy trạng thái kết nối của tất cả thiết bị thuộc site (và lane nếu chỉ định).
    /// Thiết bị bao gồm: đầu đọc barcode, đồng hồ cân 1/2, camera LPR 1/2, camera toàn cảnh.
    /// </summary>
    Task<IReadOnlyList<DeviceStatus>> GetStatusAsync(
        Guid siteId, Guid? laneId, CancellationToken ct = default);
}

/// <summary>
/// Ảnh chụp trạng thái kết nối của một thiết bị tại thời điểm truy vấn.
/// SignalAge tính từ LastSignalAt đến hiện tại — dùng xác định IsOnline.
/// FaultDescription chứa mô tả lỗi cụ thể khi ScaleStatus là Fault/Overload/Underload.
/// </summary>
public sealed record DeviceStatus(
    Guid DeviceId,
    string DeviceCode,
    string DeviceClass,
    bool IsOnline,
    ScaleDeviceStatus? ScaleStatus,
    DateTimeOffset? LastSignalAt,
    TimeSpan? SignalAge,
    string? FaultDescription);
