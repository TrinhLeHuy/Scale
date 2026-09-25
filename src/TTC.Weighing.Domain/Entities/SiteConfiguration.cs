using TTC.Weighing.Domain.Common;

namespace TTC.Weighing.Domain.Entities;

/// <summary>Nhà máy. Mọi quyền và cấu hình gán theo site (R-002, R-026).</summary>
public class Site : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Múi giờ hiển thị của nhà máy. Lưu trữ luôn dùng UTC.</summary>
    public string TimeZoneId { get; set; } = "SE Asia Standard Time";
}

public class Gate : AuditableEntity
{
    public Guid SiteId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

/// <summary>Làn — đơn vị gắn kết thiết bị. Mọi sự kiện thiết bị phải khớp làn đã cấu hình.</summary>
public class Lane : AuditableEntity
{
    public Guid SiteId { get; set; }
    public Guid? GateId { get; set; }
    public Guid? WeighbridgeId { get; set; }
    public string Code { get; set; } = string.Empty;
}

/// <summary>Bàn cân. Một máy trạm có thể quản lý hai bàn (R-011).</summary>
public class Weighbridge : AuditableEntity
{
    public Guid SiteId { get; set; }
    public string Code { get; set; } = string.Empty;

    public decimal MinCapacityKg { get; set; }
    public decimal MaxCapacityKg { get; set; }

    /// <summary>Độ phân giải của thiết bị. Không làm tròn mịn hơn giá trị này.</summary>
    public decimal ResolutionKg { get; set; }

    public bool IsOutOfService { get; set; }

    public bool IsWithinCapacity(decimal kg) => kg >= MinCapacityKg && kg <= MaxCapacityKg;
}

/// <summary>
/// Thiết bị: đầu cân, camera, đầu đọc QR, IoT, barie. Ánh xạ có version (R-002).
/// </summary>
public class Device : AuditableEntity
{
    public Guid SiteId { get; set; }
    public Guid? LaneId { get; set; }

    public string Code { get; set; } = string.Empty;

    /// <summary>SCALE, POSITION_AI, BAG_AI, QR, IOT_MASS, BARRIER.</summary>
    public string DeviceClass { get; set; } = string.Empty;

    public string? MappingVersion { get; set; }
    public bool IsEnabled { get; set; } = true;

    /// <summary>Thiết bị chế độ test không được gửi ERP/FRM hoặc mở barie thật (R-034).</summary>
    public bool IsTestDevice { get; set; }
}
