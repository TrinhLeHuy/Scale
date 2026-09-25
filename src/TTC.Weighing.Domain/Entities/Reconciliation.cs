using TTC.Weighing.Domain.Common;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Domain.Entities;

/// <summary>
/// Phiên đối soát của một chuyến (R-020). Gắn phiên bản đầu vào; đổi đầu vào thì kết luận cũ hết hiệu lực.
/// </summary>
public class Reconciliation : AuditableEntity
{
    public Guid TripId { get; set; }
    public int Revision { get; set; } = 1;
    public bool IsEffective { get; set; } = true;

    /// <summary>Phiên bản các nguồn đầu vào: số cân, BOM, xác nhận kho, phiên đo.</summary>
    public string InputVersionsJson { get; set; } = "{}";

    public List<ReconciliationCheck> Checks { get; set; } = [];

    public DateTimeOffset EvaluatedAt { get; set; }
    public string? ResolvedBy { get; set; }

    /// <summary>Chỉ đạt khi mọi phép bắt buộc là Pass hoặc NotApplicable.</summary>
    public bool IsPassed => Checks.Count > 0 && Checks.All(
        c => c.Verdict is ReconciliationVerdict.Pass or ReconciliationVerdict.NotApplicable);

    public IEnumerable<ReconciliationCheck> Blockers => Checks.Where(
        c => c.Verdict is ReconciliationVerdict.Mismatch
            or ReconciliationVerdict.MissingData
            or ReconciliationVerdict.Pending);
}

/// <summary>
/// Một phép so sánh độc lập. Lưu số đo riêng, không trộn nguồn, không lấy trung bình.
/// </summary>
public class ReconciliationCheck : AuditableEntity
{
    public Guid ReconciliationId { get; set; }

    public ReconciliationCheckType CheckType { get; set; }

    /// <summary>Số liệu quan sát được từ nguồn thứ nhất.</summary>
    public decimal? ObservedValue { get; set; }

    /// <summary>Số liệu đối chiếu từ nguồn thứ hai hoặc lượng quy đổi.</summary>
    public decimal? ExpectedValue { get; set; }

    public decimal? LowerBound { get; set; }
    public decimal? UpperBound { get; set; }

    public decimal? Difference { get; set; }

    public string Unit { get; set; } = "kg";

    /// <summary>Chính sách dung sai đã duyệt. Null nghĩa là chưa có (D-06) — không tự đặt ±20 kg.</summary>
    public string? TolerancePolicyId { get; set; }
    public int? TolerancePolicyVersion { get; set; }

    public ReconciliationVerdict Verdict { get; set; } = ReconciliationVerdict.Pending;

    /// <summary>Lý do MissingData hoặc Mismatch, để hiển thị cho người xử lý.</summary>
    public string? Detail { get; set; }
}

/// <summary>
/// Phân bổ lượng đã đo cho từng chủ mía/Shipment (R-016, R-021).
/// Đây là PHÂN BỔ, không phải từng dòng đã đo riêng.
/// </summary>
public class Allocation : AuditableEntity
{
    public Guid TripId { get; set; }

    /// <summary>Tham chiếu tới tổng lượng đã đo được phân bổ.</summary>
    public Guid MeasuredTotalRef { get; set; }

    public Guid? DocumentLineId { get; set; }

    /// <summary>Chủ mía hoặc bên nhận của phần này.</summary>
    public string? OwnerRef { get; set; }

    public decimal AllocatedKg { get; set; }

    public AllocationMethod Method { get; set; }

    /// <summary>Tỷ lệ đã duyệt, nếu Method dùng tỷ lệ.</summary>
    public decimal? Ratio { get; set; }

    /// <summary>Dòng điều chỉnh làm tròn minh bạch — không làm biến mất phần dư.</summary>
    public decimal RoundingAdjustmentKg { get; set; }

    public string? ApprovalRef { get; set; }

    /// <summary>Chưa duyệt thì chưa gửi kết quả phân bổ (S02 tr.18).</summary>
    public bool IsApproved => !string.IsNullOrWhiteSpace(ApprovalRef);
}
