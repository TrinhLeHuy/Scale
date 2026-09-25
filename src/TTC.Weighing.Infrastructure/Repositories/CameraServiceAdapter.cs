using TTC.Weighing.Application.Abstractions;

namespace TTC.Weighing.Infrastructure.Repositories;

/// <summary>
/// Stub adapter cho camera tại cổng. Trạng thái camera không chặn đăng tài (Req 10.3).
/// Sẽ được thay thế bằng implementation thật kết nối camera IP trong sprint tích hợp.
/// </summary>
public sealed class CameraServiceAdapter : ICameraService
{
    /// <summary>
    /// Stub: luôn trả null — tương đương camera offline, không có ảnh.
    /// Implementation thật sẽ gọi camera SDK/HTTP để chụp và lưu ảnh.
    /// </summary>
    public Task<string?> CaptureSnapshotAsync(
        Guid gateId, string cameraPosition, CancellationToken ct = default)
    {
        return Task.FromResult<string?>(null);
    }

    /// <summary>
    /// Stub: luôn trả Online — đủ để form hiển thị trạng thái camera 🟢.
    /// Implementation thật sẽ ping camera hoặc kiểm tra luồng RTSP.
    /// </summary>
    public Task<CameraStatus> GetStatusAsync(
        Guid gateId, string cameraPosition, CancellationToken ct = default)
    {
        return Task.FromResult(new CameraStatus(IsOnline: true, LastError: null));
    }
}
