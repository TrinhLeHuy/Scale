using TTC.Weighing.WinForms.Forms;

namespace TTC.Weighing.WinForms.Development;

/// <summary>CHỈ DÙNG XEM TRƯỚC. Nhà máy lấy từ dữ liệu bộ nhớ.</summary>
public sealed class PreviewSiteContext(PreviewDataStore store) : ISiteContext
{
    public Guid CurrentSiteId => store.Site.Id;
    public string CurrentSiteCode => store.Site.Code;
    public string? OrganizationUnitCode => null; // Preview → thấy tất cả
}

/// <summary>
/// Nhà máy đang làm việc ở chế độ vận hành, đọc từ cấu hình trạm.
/// Sản xuất phải xác thực nhà máy này thuộc quyền của người dùng (R-002, R-026).
/// </summary>
public sealed class ConfiguredSiteContext(Guid siteId, string siteCode) : ISiteContext
{
    public Guid CurrentSiteId { get; } = siteId;
    public string CurrentSiteCode { get; } = siteCode;
    public string? OrganizationUnitCode { get; set; }
}
