using TTC.Weighing.Domain.Common;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Domain.ValueObjects;

namespace TTC.Weighing.Domain.Entities;

/// <summary>
/// Phiên cân sở hữu độc quyền một bàn cân (B-003, R-011).
/// Một bàn chỉ có một phiên đang sở hữu tại một thời điểm.
/// </summary>
public class WeighSession : AuditableEntity
{
    public Guid TripId { get; set; }
    public Guid WeighbridgeId { get; set; }

    public CheckpointType Checkpoint { get; set; }

    /// <summary>Vai trò đo của lượt cân này, do profile quyết định — không hard-code theo thứ tự.</summary>
    public WeightRole Role { get; set; }

    /// <summary>Phân đoạn cho profile nhiều lượt cân (R-030).</summary>
    public int SegmentIndex { get; set; }

    public WeighSessionStatus Status { get; set; } = WeighSessionStatus.Open;

    /// <summary>Khóa sở hữu bàn cân. Phiên khác không được chốt trên cùng bàn.</summary>
    public string OwnershipToken { get; set; } = string.Empty;

    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }

    public List<WeighEvent> Events { get; set; } = [];
}

/// <summary>
/// Một lần cân đã chốt. BẤT BIẾN sau khi ghi (03_Du_lieu mục 3, R-009).
/// Cân lại tạo bản mới liên kết SupersedesId — không UPDATE, không DELETE.
/// </summary>
public class WeighEvent : AuditableEntity
{
    public Guid WeighSessionId { get; set; }
    public Guid TripId { get; set; }

    /// <summary>Số hiệu bản sửa. Mỗi vai trò trong một phân đoạn chỉ có một bản đang hiệu lực.</summary>
    public int Revision { get; set; }

    /// <summary>Bản cân bị thay thế. Bản cũ được giữ nguyên để tra cứu.</summary>
    public Guid? SupersedesId { get; set; }

    public bool IsEffective { get; set; } = true;

    public WeightRole Role { get; set; }

    // Số cân bất biến — lưu gốc + quy đổi
    public decimal RawWeight { get; set; }
    public string RawUom { get; set; } = "kg";
    public decimal NormalizedKg { get; set; }

    public DateTimeOffset MeasuredAt { get; set; }

    /// <summary>AUTO là mặc định. MANUAL/IMPORT/TEST phải có approval và không đi luồng production như AUTO.</summary>
    public WeighSource Source { get; set; } = WeighSource.Auto;

    public Guid? DeviceId { get; set; }

    /// <summary>Thời gian ổn định thực tế đã đạt tại thời điểm chốt.</summary>
    public TimeSpan StableWindow { get; set; }

    /// <summary>Phiên bản profile/quy tắc áp dụng khi chốt — để truy vết về sau.</summary>
    public int PolicyVersion { get; set; }

    /// <summary>Định danh ảnh/bằng chứng liên kết sự kiện cân.</summary>
    public List<string> EvidenceRefs { get; set; } = [];

    /// <summary>Bắt buộc với Source = Manual hoặc khi cân lại.</summary>
    public string? ApprovalRef { get; set; }
    public string? Reason { get; set; }

    public WeightMeasurement ToMeasurement() => WeightMeasurement.Create(RawWeight, RawUom);
}
