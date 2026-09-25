using TTC.Weighing.Domain.Common;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Domain.Entities;

/// <summary>
/// Mẫu chất lượng gắn với chuyến (R-013, R-014).
/// Tạp chất và chữ đường là hai trạng thái khác nhau; checkpoint lấy mẫu khác kết quả.
/// </summary>
public class QualitySample : AuditableEntity
{
    public Guid TripId { get; set; }

    /// <summary>QR của mẫu, nếu quy trình dùng QR riêng cho mẫu.</summary>
    public string? SampleQrRef { get; set; }

    public QualitySampleType Type { get; set; }
    public QualityResultStatus Status { get; set; } = QualityResultStatus.Collected;

    public int Revision { get; set; } = 1;
    public Guid? SupersedesId { get; set; }
    public bool IsEffective { get; set; } = true;

    // Cân tạp chất: hai số đo mẫu (02_Quy_tac 4.2)
    public decimal? SampleBeforeKg { get; set; }
    public decimal? SampleAfterKg { get; set; }

    /// <summary>Tỷ lệ tạp chất tính theo công thức S02 tr.18. Null khi chưa đủ dữ liệu.</summary>
    public decimal? ImpurityPercent { get; set; }

    /// <summary>Kết quả chữ đường do nguồn có thẩm quyền cung cấp. Không tự tính trong phần mềm cân.</summary>
    public decimal? CcsValue { get; set; }

    /// <summary>Nguồn kết quả: DEVICE, MANUAL, FRM, LAB.</summary>
    public string Source { get; set; } = "DEVICE";

    public DateTimeOffset? ReceivedAt { get; set; }
    public string? ReceivedBy { get; set; }

    /// <summary>Cảnh báo mẫu dưới ngưỡng cấu hình (tài liệu nêu 10 kg) — cảnh báo, không lỗi cứng.</summary>
    public bool BelowMinimumSampleWarning { get; set; }

    /// <summary>Bắt buộc khi Source = MANUAL hoặc khi lấy lại mẫu.</summary>
    public string? ApprovalRef { get; set; }
    public string? Reason { get; set; }

    /// <summary>Kết quả không hợp lệ thì không đủ điều kiện gửi đi.</summary>
    public bool IsSyncEligible => Status == QualityResultStatus.Valid && IsEffective;
}
