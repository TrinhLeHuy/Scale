namespace TTC.Weighing.Domain.Entities;

/// <summary>
/// Theo dõi số thứ tự hàng chờ theo loại xe, tách riêng per-site per-day.
/// Dùng PostgreSQL INSERT ON CONFLICT để đảm bảo atomic increment.
/// Không kế thừa AuditableEntity vì đây là bảng kỹ thuật, không phải entity nghiệp vụ.
/// </summary>
public class QueueSequenceTracker
{
    public Guid SiteId { get; set; }
    public string VehicleType { get; set; } = string.Empty;
    public DateOnly OperatingDate { get; set; }
    public int LastValue { get; set; }
}
