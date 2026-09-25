using TTC.Weighing.Domain.Common;

namespace TTC.Weighing.Domain.Entities;

/// <summary>
/// Phiếu cân in sau khi hoàn thành cân lần 2 (R-005.1, R-005.3).
/// Mỗi lần in tạo bản ghi riêng; in lại đánh dấu IsReprint và tăng PrintCount.
/// SnapshotJson lưu toàn bộ dữ liệu phiếu tại thời điểm in để in lại chính xác.
/// </summary>
public class WeighTicket : AuditableEntity
{
    public Guid TripId { get; set; }

    /// <summary>Số phiếu cân duy nhất, format theo quy định nhà máy.</summary>
    public string TicketNumber { get; set; } = string.Empty;

    /// <summary>Mã mẫu phiếu: phân biệt phiếu mía nhập và phiếu vật tư.</summary>
    public string TemplateCode { get; set; } = string.Empty;

    public DateTimeOffset PrintedAt { get; set; }

    /// <summary>Người thực hiện in phiếu.</summary>
    public string PrintedBy { get; set; } = string.Empty;

    /// <summary>True nếu đây là bản in lại — phiếu sẽ đánh dấu "BẢN IN LẠI".</summary>
    public bool IsReprint { get; set; }

    /// <summary>Số lần in (bao gồm bản gốc và in lại).</summary>
    public int PrintCount { get; set; } = 1;

    /// <summary>Ảnh chụp dữ liệu phiếu tại thời điểm in, để in lại chính xác.</summary>
    public string SnapshotJson { get; set; } = "{}";
}
