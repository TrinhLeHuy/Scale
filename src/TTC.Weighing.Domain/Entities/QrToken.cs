using TTC.Weighing.Domain.Common;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Domain.Entities;

/// <summary>
/// QR gắn chuyến hoặc xe (R-004). KHÔNG nhét BOM/số cân vào QR để sửa dữ liệu qua QR.
/// QR tạm không đủ điều kiện cân chính thức.
/// </summary>
public class QrToken : AuditableEntity
{
    /// <summary>Chỉ lưu hash, không lưu giá trị token thô.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public QrTokenType Type { get; set; }
    public QrTokenStatus Status { get; set; } = QrTokenStatus.Issued;

    /// <summary>Có giá trị với Type = Temp/Trip.</summary>
    public Guid? TripId { get; set; }

    /// <summary>Có giá trị với Type = InternalVehicle. Phải gán đúng chu kỳ chuyến đang hoạt động.</summary>
    public Guid? VehicleId { get; set; }

    public Guid SiteId { get; set; }

    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>
    /// Nội dung mã hóa trong QR (JSON Base64).
    /// QR_chuyến: token prefix + số thứ tự + bàn cân + kho.
    /// QR_tạm: chỉ token prefix.
    /// </summary>
    public string? EncodedContent { get; set; }

    public bool IsValidAt(DateTimeOffset at) =>
        Status == QrTokenStatus.Issued &&
        RevokedAt is null &&
        (ExpiresAt is null || at < ExpiresAt);

    /// <summary>QR tạm vẫn cho phép cân (cân offline — sau đẩy lên hệ thống chính).</summary>
    public bool AllowsOfficialWeighing => true;
}
