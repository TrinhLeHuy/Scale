using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Application.Abstractions;

/// <summary>Kiểu chuỗi dữ liệu đầu cân gửi qua cổng COM.</summary>
public enum ScaleFrameFormat
{
    /// <summary>Chuẩn A&amp;D (AD-4329A...): "ST,GS,+0012345kg" — có cờ ổn định / quá tải.</summary>
    AndStandard,

    /// <summary>Lấy số đầu tiên trong dòng (đa số đầu cân khác). Không có cờ ổn định → tự xét theo số không đổi.</summary>
    Generic,

    /// <summary>
    /// Kiểu phần mềm cân cũ (CanDienTu — Toledo, Keli, A12...): bỏ mọi ký tự trừ "+ - . 0-9",
    /// ưu tiên cụm có dấu ("+0000140"), không có thì lấy số bất kỳ. Không có cờ ổn định.
    /// </summary>
    SignedDigits
}

/// <summary>
/// Cấu hình kết nối một đầu cân qua cổng COM. Lưu theo máy trạm, không lưu chỉ số nghiệp vụ
/// (Fibre, ngưỡng tạp chất, cân tối thiểu... lấy từ Portal &gt; Quy tắc cân).
/// </summary>
public sealed record ScaleConnectionSettings
{
    /// <summary>Mã thiết bị: mã bàn cân (BAN-CAN-01...) hoặc <see cref="SampleScaleKey"/>.</summary>
    public required string Key { get; init; }

    /// <summary>Bàn cân tương ứng (null với cân mẫu tạp chất).</summary>
    public Guid? WeighbridgeId { get; init; }

    // Mặc định theo cấu hình phần mềm cân cũ đang chạy ở nhà máy (CanDienTu, cauhinh.ini): 9600 baud, 8N1.
    public bool Enabled { get; init; }
    public string? PortName { get; init; }
    public int BaudRate { get; init; } = 9600;
    public int DataBits { get; init; } = 8;

    /// <summary>None | Even | Odd.</summary>
    public string Parity { get; init; } = "None";

    /// <summary>1 | 2.</summary>
    public int StopBits { get; init; } = 1;

    public ScaleFrameFormat Format { get; init; } = ScaleFrameFormat.SignedDigits;

    /// <summary>Đơn vị số đầu cân gửi khi chuỗi không ghi đơn vị: kg | t.</summary>
    public string Unit { get; init; } = "kg";

    /// <summary>Hệ số nhân sau khi quy về kg (đầu cân hiển thị bỏ số 0 cuối...). Mặc định 1.</summary>
    public decimal Factor { get; init; } = 1m;

    /// <summary>Không nhận khung hợp lệ quá thời gian này → coi là mất tín hiệu ("---").</summary>
    public int StaleAfterMs { get; init; } = 3000;

    public const string SampleScaleKey = "SAMPLE";
}

/// <summary>Số đọc mới nhất của một đầu cân (cho màn hình cấu hình và gateway).</summary>
public sealed record ScaleLiveReading
{
    /// <summary>Cổng đang mở được.</summary>
    public bool PortOpen { get; init; }

    /// <summary>Số cân đã quy về kg × hệ số. Null khi chưa đọc được.</summary>
    public decimal? Kg { get; init; }

    public decimal? RawValue { get; init; }
    public string? RawUnit { get; init; }

    /// <summary>Cờ ổn định của đầu cân (A&amp;D: ST/US). Null khi chuỗi không có cờ.</summary>
    public bool? StableFlag { get; init; }

    public bool IsStable { get; init; }
    public TimeSpan StableDuration { get; init; }
    public decimal? ObservedDeltaKg { get; init; }
    public ScaleDeviceStatus Status { get; init; } = ScaleDeviceStatus.Unknown;

    /// <summary>Dòng thô cuối cùng nhận được (để đối chiếu khi cài đặt).</summary>
    public string? LastLine { get; init; }

    public DateTimeOffset? ReceivedAt { get; init; }

    /// <summary>Còn mới (nhận trong <see cref="ScaleConnectionSettings.StaleAfterMs"/>).</summary>
    public bool Fresh { get; init; }

    /// <summary>Mã lỗi: PORT_NOT_FOUND, PORT_BUSY, PORT_ERROR, PARSE_ERROR, PORT_CONFLICT, DISABLED, NOT_CONFIGURED.</summary>
    public string? Error { get; init; }

    public string? ErrorDetail { get; init; }
}

/// <summary>
/// Quản lý kết nối đầu cân qua cổng COM của máy trạm: lưu cấu hình, mở cổng, đọc liên tục.
/// Gateway cân xe / cân mẫu đọc số qua dịch vụ này; không có cấu hình thì dùng nguồn cũ.
/// </summary>
public interface IScaleConnectionService
{
    IReadOnlyList<ScaleConnectionSettings> GetSettings();

    /// <summary>Lưu và kết nối lại theo cấu hình mới.</summary>
    void Save(IReadOnlyList<ScaleConnectionSettings> settings);

    /// <summary>Đóng rồi mở lại mọi cổng theo cấu hình đã lưu.</summary>
    void Reconnect();

    ScaleLiveReading GetLive(string key);

    /// <summary>Cấu hình đang bật của bàn cân (null = chưa cấu hình cổng COM).</summary>
    ScaleConnectionSettings? FindEnabled(Guid weighbridgeId);

    ScaleConnectionSettings? FindEnabled(string key);

    IReadOnlyList<string> GetPortNames();

    /// <summary>
    /// Tự dò đầu cân: thử từng cổng (chưa được mở) với các bộ thông số truyền thông dụng, chỉ NGHE,
    /// không gửi gì ra cổng. Trả bộ thông số đầu tiên đọc ra số hợp lệ, null nếu không thấy.
    /// </summary>
    Task<ScaleProbeResult?> ProbeAsync(IReadOnlyList<string> portNames, IProgress<string>? progress, CancellationToken ct);
}

/// <summary>Kết quả tự dò: cổng + thông số truyền + một dòng mẫu để đối chiếu với màn hình đầu cân.</summary>
public sealed record ScaleProbeResult(
    string PortName, int BaudRate, int DataBits, string Parity, int StopBits,
    ScaleFrameFormat Format, string SampleLine, decimal SampleValue, string? SampleUnit);
