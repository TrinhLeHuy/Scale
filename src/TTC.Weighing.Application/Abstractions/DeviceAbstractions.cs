using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Application.Abstractions;

/// <summary>
/// Ảnh chụp tín hiệu đầu cân do gateway cung cấp. Không cho UI tự tạo giá trị này.
/// </summary>
public sealed record ScaleSignalSnapshot
{
    public required Guid DeviceId { get; init; }
    public required decimal RawWeight { get; init; }
    public required string RawUom { get; init; }
    public required decimal NormalizedKg { get; init; }
    public required ScaleDeviceStatus DeviceStatus { get; init; }

    /// <summary>Cờ ổn định do thiết bị báo, nếu có. Không tự chuyển null thành true.</summary>
    public bool? StableFlag { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }
    public required DateTimeOffset ReceivedAt { get; init; }

    /// <summary>Thời gian tín hiệu giữ ổn định liên tục tính đến hiện tại.</summary>
    public required TimeSpan StableDuration { get; init; }

    /// <summary>Dao động lớn nhất quan sát trong cửa sổ ổn định.</summary>
    public decimal? ObservedDeltaKg { get; init; }

    /// <summary>False khi có gap trong dải sequence làm đứt cửa sổ đánh giá.</summary>
    public required bool SignalContinuous { get; init; }
}

/// <summary>Bằng chứng AI vị trí. Verdict Invalid/Unknown đều không đạt (B-001.4).</summary>
public sealed record PositionEvidenceSnapshot
{
    public required Guid DeviceId { get; init; }
    public required PositionVerdict Verdict { get; init; }

    /// <summary>Có thể null. Tuyệt đối không quy đổi null thành 1.</summary>
    public decimal? Confidence { get; init; }

    public required string ModelVersion { get; init; }
    public required string RegionVersion { get; init; }
    public required string EvidenceRef { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>False khi cấu hình vùng quan sát không khớp phiên bản đã duyệt.</summary>
    public required bool RegionConfigValid { get; init; }
}

/// <summary>Gateway đọc tín hiệu thiết bị tại nhà máy. Adapter chuyển giao thức thật sang cấu trúc này.</summary>
public interface IScaleSignalGateway
{
    Task<ScaleSignalSnapshot?> GetCurrentAsync(Guid weighbridgeId, CancellationToken ct = default);
}

public interface IPositionEvidenceGateway
{
    Task<PositionEvidenceSnapshot?> GetLatestAsync(Guid laneId, CancellationToken ct = default);
}

/// <summary>Lưu ảnh/bằng chứng. Không lưu được thì không phát hành lượt cân (B-001.8).</summary>
public interface IEvidenceStore
{
    Task<IReadOnlyList<string>> PersistAsync(
        Guid tripId, IEnumerable<string> sourceRefs, CancellationToken ct = default);
}
