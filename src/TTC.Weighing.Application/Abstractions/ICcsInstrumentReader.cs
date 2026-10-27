namespace TTC.Weighing.Application.Abstractions;

/// <summary>Loại máy đo trong phòng phân tích chữ đường.</summary>
public enum CcsInstrument
{
    /// <summary>Khúc xạ kế (Brix).</summary>
    Brix,

    /// <summary>Phân cực kế (Pol).</summary>
    Pol
}

/// <summary>Kết quả đọc một lần từ máy đo. <see cref="Value"/> null khi chưa đọc được.</summary>
public sealed record CcsInstrumentReading(decimal? Value, bool DeviceConnected, string? Error, DateTimeOffset? ReadAt);

/// <summary>
/// Đọc số Brix / Pol trực tiếp từ máy đo phòng CCS.
/// Chưa có giao thức thiết bị → implementation hiện tại báo "chưa kết nối";
/// khi có máy thật chỉ cần thay adapter ở Infrastructure.
/// </summary>
public interface ICcsInstrumentReader
{
    Task<CcsInstrumentReading> ReadAsync(CcsInstrument instrument, CancellationToken ct = default);
}
