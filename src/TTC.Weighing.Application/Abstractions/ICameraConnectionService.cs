namespace TTC.Weighing.Application.Abstractions;

/// <summary>Hãng camera IP — quyết định đường dẫn chụp ảnh (HTTP) và luồng RTSP.</summary>
public enum CameraBrand
{
    /// <summary>Tự nhận: thử Hikvision rồi Dahua.</summary>
    Auto,
    Hikvision,
    Dahua,

    /// <summary>Tự nhập đường dẫn chụp ảnh (<see cref="CameraConnectionSettings.SnapshotPath"/>).</summary>
    Custom
}

/// <summary>
/// Cấu hình kết nối một camera IP (TCP/IP) tại cổng. Lưu theo máy trạm (màn Kết nối camera).
/// Mật khẩu chỉ giữ trong bộ nhớ; khi lưu xuống đĩa được mã hóa theo tài khoản Windows.
/// </summary>
public sealed record CameraConnectionSettings
{
    public const string InPosition = "In";
    public const string OutPosition = "Out";

    /// <summary>Màn Cân (bàn cân).</summary>
    public const string StationWeigh = "Weigh";

    /// <summary>Màn Đăng tài.</summary>
    public const string StationRegistration = "Registration";

    public static readonly string[] Stations = [StationWeigh, StationRegistration];
    public static readonly string[] Positions = [InPosition, OutPosition];

    /// <summary>
    /// Màn hình dùng camera: <see cref="StationWeigh"/> | <see cref="StationRegistration"/>.
    /// Cấu hình cũ (chưa có trường này) được coi là camera bàn cân.
    /// </summary>
    public string Station { get; init; } = StationWeigh;

    /// <summary>Vị trí: <see cref="InPosition"/> (Camera Vào) | <see cref="OutPosition"/> (Camera Ra).</summary>
    public required string Position { get; init; }

    public bool Is(string station, string position)
        => string.Equals(Station, station, StringComparison.OrdinalIgnoreCase)
           && string.Equals(Position, position, StringComparison.OrdinalIgnoreCase);

    public bool Enabled { get; init; }

    /// <summary>Địa chỉ IP hoặc tên máy của camera.</summary>
    public string? Host { get; init; }

    public int HttpPort { get; init; } = 80;
    public int RtspPort { get; init; } = 554;
    public string? Username { get; init; }
    public string? Password { get; init; }
    public CameraBrand Brand { get; init; } = CameraBrand.Auto;
    public int Channel { get; init; } = 1;

    /// <summary>Đường dẫn chụp ảnh khi <see cref="Brand"/> = Custom, ví dụ "/cgi-bin/snapshot.cgi".</summary>
    public string? SnapshotPath { get; init; }

    /// <summary>Thời gian chờ kết nối / chụp ảnh.</summary>
    public int TimeoutMs { get; init; } = 3000;
}

/// <summary>
/// Kết quả kiểm tra / chụp một ảnh từ camera.
/// Error: NOT_CONFIGURED, HOST_UNREACHABLE, AUTH_FAILED, SNAPSHOT_NOT_FOUND, NOT_IMAGE, HTTP_ERROR.
/// </summary>
public sealed record CameraTestResult
{
    public bool Success => Error is null && Image is not null;
    public bool TcpOk { get; init; }
    public int? LatencyMs { get; init; }
    public bool? RtspPortOpen { get; init; }
    public int? HttpStatus { get; init; }

    /// <summary>Hãng nhận ra được khi cấu hình để Auto.</summary>
    public CameraBrand? DetectedBrand { get; init; }

    /// <summary>Đường dẫn chụp ảnh đã dùng (không chứa tài khoản).</summary>
    public string? SnapshotUrl { get; init; }

    /// <summary>Luồng RTSP (không chứa mật khẩu) để mở bằng VLC / đầu ghi.</summary>
    public string? RtspUrl { get; init; }

    /// <summary>Ảnh JPEG chụp được.</summary>
    public byte[]? Image { get; init; }

    public string? Error { get; init; }
    public string? ErrorDetail { get; init; }
}

/// <summary>Trạng thái kết nối camera đã lưu (kiểm tra nền định kỳ qua TCP).</summary>
public sealed record CameraLiveState
{
    public bool Online { get; init; }
    public int? LatencyMs { get; init; }
    public DateTimeOffset? CheckedAt { get; init; }

    /// <summary>NOT_CONFIGURED, DISABLED, HOST_UNREACHABLE.</summary>
    public string? Error { get; init; }

    public string? ErrorDetail { get; init; }
}

/// <summary>
/// Quản lý kết nối camera IP của máy trạm: lưu cấu hình, kiểm tra kết nối, chụp ảnh.
/// Mỗi camera xác định bằng màn hình (<see cref="CameraConnectionSettings.Station"/>) + vị trí Vào / Ra.
/// </summary>
public interface ICameraConnectionService
{
    IReadOnlyList<CameraConnectionSettings> GetSettings();

    /// <summary>Cấu hình đang bật của camera (null = chưa cấu hình / đang tắt).</summary>
    CameraConnectionSettings? FindEnabled(string station, string position);

    /// <summary>Lưu và kiểm tra lại kết nối theo cấu hình mới.</summary>
    void Save(IReadOnlyList<CameraConnectionSettings> settings);

    /// <summary>Kiểm tra TCP + chụp thử một ảnh theo cấu hình truyền vào (chưa cần lưu).</summary>
    Task<CameraTestResult> TestAsync(CameraConnectionSettings settings, CancellationToken ct);

    /// <summary>
    /// Chụp ảnh từ camera đã lưu (nhanh: không kiểm tra lại TCP, dùng đường dẫn chụp đã dò được).
    /// Null khi chưa cấu hình hoặc không chụp được.
    /// </summary>
    Task<byte[]?> CaptureAsync(string station, string position, CancellationToken ct);

    CameraLiveState GetLive(string station, string position);
}
