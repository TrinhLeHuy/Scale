using TTC.Weighing.Application.Abstractions;

namespace TTC.Weighing.Infrastructure.Repositories;

/// <summary>
/// Camera tại cổng đăng tài qua TCP/IP, theo cấu hình màn Kết nối camera (<see cref="ICameraConnectionService"/>).
/// cameraPosition: "In" / "Out" = camera màn Đăng tài; "Weigh.In" / "Registration.Out"... = chỉ rõ màn hình.
/// Trạng thái camera không chặn đăng tài (Req 10.3): lỗi chỉ trả offline / null.
/// </summary>
public sealed class CameraServiceAdapter(ICameraConnectionService connections, string snapshotRoot) : ICameraService
{
    /// <summary>Chụp ảnh và lưu vào {snapshotRoot}/yyyyMMdd/. Null khi camera chưa cấu hình hoặc không chụp được.</summary>
    public async Task<string?> CaptureSnapshotAsync(
        Guid gateId, string cameraPosition, CancellationToken ct = default)
    {
        var (station, position) = Resolve(cameraPosition);
        var image = await connections.CaptureAsync(station, position, ct);
        if (image is null) return null;

        var now = DateTime.Now;
        var dir = Path.Combine(snapshotRoot, now.ToString("yyyyMMdd"));
        Directory.CreateDirectory(dir);
        var name = $"{station}_{position}".ToLowerInvariant();
        var path = Path.Combine(dir, $"{now:HHmmssfff}_{name}_{gateId:N}.jpg");
        await File.WriteAllBytesAsync(path, image, ct);
        return path;
    }

    /// <summary>Trạng thái theo lần kiểm tra TCP nền gần nhất.</summary>
    public Task<CameraStatus> GetStatusAsync(
        Guid gateId, string cameraPosition, CancellationToken ct = default)
    {
        var (station, position) = Resolve(cameraPosition);
        var live = connections.GetLive(station, position);
        return Task.FromResult(new CameraStatus(live.Online, live.Online ? null : live.Error));
    }

    private static (string Station, string Position) Resolve(string cameraPosition)
    {
        var dot = cameraPosition.IndexOf('.');
        return dot > 0
            ? (cameraPosition[..dot], cameraPosition[(dot + 1)..])
            : (CameraConnectionSettings.StationRegistration, cameraPosition);
    }
}
