using TTC.Weighing.Domain.Common;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Domain.Entities;

/// <summary>
/// Chứng từ nguồn từ FRM/ERP (R-038). Nền tảng cân KHÔNG là nguồn chủ của đơn hàng/BOM.
/// Lưu snapshot để truy vết, mọi mã lưu dạng chuỗi.
/// </summary>
public class ExternalDocument : AuditableEntity
{
    public string SourceSystem { get; set; } = string.Empty;
    public string? OrganizationCode { get; set; }
    public string? BusinessUnit { get; set; }

    /// <summary>PO, SO, TO, TRIP, HARVEST_ORDER...</summary>
    public string DocumentType { get; set; } = string.Empty;

    public string ReferenceNo { get; set; } = string.Empty;
    public string? ShipmentNo { get; set; }

    /// <summary>Phiên bản/hash của dữ liệu nguồn để phát hiện thay đổi.</summary>
    public string? SourceVersion { get; set; }
    public string? SourceHash { get; set; }

    public string? Status { get; set; }

    /// <summary>Định danh bản snapshot thô đã lưu.</summary>
    public string? RawSnapshotRef { get; set; }

    public List<TripDocumentLine> Lines { get; set; } = [];
}

/// <summary>
/// Dòng hàng của chuyến. Số kế hoạch KHÁC số xác nhận thực tế của chuyến này (R-038).
/// Cùng document_line có thể đi nhiều chuyến.
/// </summary>
public class TripDocumentLine : AuditableEntity
{
    public Guid TripId { get; set; }
    public Guid DocumentId { get; set; }

    public string? SourceLineId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public Guid? WarehouseId { get; set; }

    /// <summary>Số lượng kế hoạch của dòng. KHÔNG mặc định là lượng trên xe.</summary>
    public decimal? PlannedQuantity { get; set; }
    public string? PlannedUom { get; set; }

    /// <summary>Số kho/ERP đã xác nhận cho chuyến này.</summary>
    public decimal? ConfirmedQuantity { get; set; }
    public string? ConfirmedUom { get; set; }

    /// <summary>Lượng còn lại trên nguồn, do nguồn cung cấp — không tự tính trừ dần.</summary>
    public decimal? RemainingQuantity { get; set; }

    /// <summary>Phân đoạn cho profile nhiều lượt cân.</summary>
    public int MovementSegmentId { get; set; }
}

/// <summary>
/// Quy cách/BOM có phiên bản từ FRM/ERP (R-019). Thiếu quy đổi thì chưa kết luận đối soát.
/// </summary>
public class BomSnapshot : AuditableEntity
{
    public string SourceSystem { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;

    public string? SourceVersion { get; set; }
    public DateTimeOffset? EffectiveAt { get; set; }

    public string Uom { get; set; } = string.Empty;

    /// <summary>Khối lượng danh nghĩa mỗi đơn vị. Có thể null nếu nguồn không cung cấp.</summary>
    public decimal? NominalKgPerUnit { get; set; }

    /// <summary>Min/max chỉ có khi nguồn cung cấp. BOM danh nghĩa chưa chắc có min/max (D-06).</summary>
    public decimal? MinKgPerUnit { get; set; }
    public decimal? MaxKgPerUnit { get; set; }

    /// <summary>Bì bao bì và pallet, nếu nguồn có.</summary>
    public decimal? PackagingTareKg { get; set; }
    public decimal? PalletTareKg { get; set; }

    public MassBasis MassBasis { get; set; } = MassBasis.Unknown;

    public string? SnapshotHash { get; set; }

    public bool HasToleranceRange => MinKgPerUnit.HasValue && MaxKgPerUnit.HasValue;
}
