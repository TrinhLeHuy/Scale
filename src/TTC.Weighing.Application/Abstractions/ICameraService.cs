namespace TTC.Weighing.Application.Abstractions;

/// <summary>
/// Dịch vụ camera tại cổng. Trạng thái camera không chặn đăng tài.
/// </summary>
public interface ICameraService
{
    /// <summary>Chụp ảnh từ camera, trả về đường dẫn ảnh lưu trữ. Null nếu camera offline.</summary>
    Task<string?> CaptureSnapshotAsync(
        Guid gateId, string cameraPosition, CancellationToken ct = default);

    /// <summary>Kiểm tra trạng thái camera.</summary>
    Task<CameraStatus> GetStatusAsync(
        Guid gateId, string cameraPosition, CancellationToken ct = default);
}

public sealed record CameraStatus(bool IsOnline, string? LastError);
