using TTC.Weighing.Domain.Common;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Domain.Entities;

/// <summary>
/// B-010 — Phiên đo bắt buộc trước khi nhận dữ liệu IoT/đếm bao vào chuyến (R-017, R-018).
/// Một nguồn đo vật lý không gán đồng thời cho hai chuyến.
/// </summary>
public class MeasurementSession : AuditableEntity
{
    public Guid TripId { get; set; }
    public Guid WarehouseId { get; set; }

    /// <summary>Làn hoặc băng chuyền của điểm đo.</summary>
    public Guid? ConveyorLaneId { get; set; }

    /// <summary>Mã hàng bị khóa bởi phiên kho — AI KHÔNG tự nhận diện SKU (R-018).</summary>
    public string ItemCode { get; set; } = string.Empty;

    public Guid? DocumentLineId { get; set; }

    /// <summary>Danh sách thiết bị được phép ghi vào phiên này.</summary>
    public List<Guid> DeviceIds { get; set; } = [];

    /// <summary>Cơ sở khối lượng của số đo IoT. Unknown thì chưa đủ để đối soát.</summary>
    public MassBasis MassBasis { get; set; } = MassBasis.Unknown;

    public IotReadingMode ReadingMode { get; set; } = IotReadingMode.Delta;

    /// <summary>Chu kỳ bộ đếm. Bắt buộc khi ReadingMode = Cumulative (B-011).</summary>
    public string? CounterEpoch { get; set; }
    public decimal? CounterStart { get; set; }
    public decimal? CounterEnd { get; set; }

    public MeasurementSessionStatus Status { get; set; } = MeasurementSessionStatus.Open;

    /// <summary>Sequence lớn nhất đã xử lý, để phát hiện thiếu dữ liệu khi đóng phiên (B-013).</summary>
    public long? SequenceWatermark { get; set; }

    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }

    public string? HoldReason { get; set; }

    // Kết quả tổng hợp của phiên — lưu riêng, KHÔNG cộng với cân xe (02_Quy_tac mục 6)
    public decimal? IotMassKg { get; set; }
    public MassOrigin? IotMassOrigin { get; set; }

    public int? ForwardBagCount { get; set; }
    public int? ReverseBagCount { get; set; }

    /// <summary>Số bao thực đếm = forward - reverse, chỉ khi băng chuyền hai chiều đã nghiệm thu.</summary>
    public int? NetBagCount { get; set; }

    /// <summary>Dữ liệu chưa đủ tin cậy để dùng cho đối soát.</summary>
    public bool DataComplete { get; set; }
}

/// <summary>
/// Sự kiện thiết bị đã chuẩn hóa (03_Du_lieu mục 4).
/// Khóa chống trùng: DeviceId + BootSessionId + Sequence (B-013).
/// </summary>
public class DeviceEvent : AuditableEntity
{
    /// <summary>Định danh do nguồn cung cấp.</summary>
    public string EventId { get; set; } = string.Empty;

    public Guid DeviceId { get; set; }
    public string BootSessionId { get; set; } = string.Empty;
    public long Sequence { get; set; }

    /// <summary>SCALE_READING, POSITION_EVIDENCE, BAG_PASSAGE, IOT_MASS_READING, BARRIER_FEEDBACK.</summary>
    public string EventType { get; set; } = string.Empty;

    public Guid SiteId { get; set; }
    public Guid LaneId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }

    public string CorrelationId { get; set; } = string.Empty;

    public Guid? TripId { get; set; }
    public Guid? MeasurementSessionId { get; set; }

    public string PayloadJson { get; set; } = "{}";

    /// <summary>Hash payload để phát hiện cùng ID nhưng nội dung khác.</summary>
    public string PayloadHash { get; set; } = string.Empty;

    public DeviceEventProcessingStatus ProcessingStatus { get; set; }
        = DeviceEventProcessingStatus.Received;

    public string? ProcessingNote { get; set; }
}
