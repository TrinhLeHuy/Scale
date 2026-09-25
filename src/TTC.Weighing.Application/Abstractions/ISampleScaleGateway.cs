using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Application.Abstractions;

/// <summary>
/// Gateway đầu cân mẫu tại trạm lấy mẫu tạp chất (Req 2).
/// Khác với <see cref="IScaleSignalGateway"/> vốn dùng cho bàn cân xe.
/// </summary>
public interface ISampleScaleGateway
{
    /// <summary>
    /// Đọc tín hiệu hiện tại từ đầu cân mẫu.
    /// Trả <c>null</c> khi thiết bị chưa sẵn sàng hoặc mất kết nối.
    /// </summary>
    Task<SampleScaleSignal?> GetCurrentAsync(
        Guid sampleScaleId, CancellationToken ct = default);
}

/// <summary>
/// Ảnh chụp tín hiệu đầu cân mẫu tạp chất tại một thời điểm.
/// </summary>
public sealed record SampleScaleSignal(
    decimal WeightKg,
    bool IsStable,
    TimeSpan StableDuration,
    ScaleDeviceStatus DeviceStatus,
    DateTimeOffset OccurredAt);
