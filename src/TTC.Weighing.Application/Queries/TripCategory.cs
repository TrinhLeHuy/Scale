namespace TTC.Weighing.Application.Queries;

/// <summary>
/// Nhóm nghiệp vụ của chuyến, dùng để tách lưới chuyến Mía và Vật tư.
/// Suy từ <c>trips.source_system</c> (gán từ WorkflowProfile lúc đăng tài):
/// FRM = mía, ERP = vật tư. Không suy từ tên hàng hóa.
/// </summary>
public enum TripCategory
{
    /// <summary>Không lọc — chỉ dùng cho combo chọn chuyến nội bộ, không dùng cho lưới chuyến.</summary>
    All = 0,

    /// <summary>Mía nguyên liệu (nguồn FRM).</summary>
    Sugarcane = 1,

    /// <summary>Vật tư nhập/xuất (nguồn ERP).</summary>
    Material = 2
}

public static class TripCategoryExtensions
{
    public const string SUGARCANE_SOURCE_SYSTEM = "FRM";
    public const string MATERIAL_SOURCE_SYSTEM = "ERP";

    /// <summary>Mã nguồn tương ứng để lọc trên <c>trips.source_system</c>. Null = không lọc.</summary>
    public static string? ToSourceSystem(this TripCategory category) => category switch
    {
        TripCategory.Sugarcane => SUGARCANE_SOURCE_SYSTEM,
        TripCategory.Material => MATERIAL_SOURCE_SYSTEM,
        _ => null
    };
}
