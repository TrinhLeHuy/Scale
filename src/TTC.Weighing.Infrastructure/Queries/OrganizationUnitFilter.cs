using TTC.Weighing.Domain.Entities;

namespace TTC.Weighing.Infrastructure.Queries;

/// <summary>
/// Extension filter Trip query theo BU (Organization Unit).
/// Mỗi tài khoản chỉ thấy dữ liệu BU của mình. Admin (null) thấy tất cả.
/// </summary>
public static class OrganizationUnitFilter
{
    /// <summary>
    /// Static accessor cho org unit code hiện tại.
    /// Set bởi Program.cs sau đăng nhập. Null = admin → không filter.
    /// </summary>
    public static string? CurrentOrgUnit { get; set; }

    /// <summary>Tổng công ty — thấy dữ liệu mọi nhà máy (cùng quy ước FrmMasterDataCache.IsHeadquarters).</summary>
    public const string HEADQUARTERS_CODE = "TTCBH";

    /// <summary>Không lọc: chưa có BU (admin) hoặc tài khoản tổng công ty.</summary>
    public static bool SeesAllUnits
        => string.IsNullOrWhiteSpace(CurrentOrgUnit)
           || string.Equals(CurrentOrgUnit, HEADQUARTERS_CODE, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Dữ liệu ghi BU dạng tên (vd. ERP "KSK BU") hoặc mã ("KSK") có thuộc BU hiện tại không.
    /// </summary>
    public static bool MatchesCurrent(string? businessUnit)
        => SeesAllUnits
           || (businessUnit is not null
               && (string.Equals(businessUnit.Trim(), CurrentOrgUnit, StringComparison.OrdinalIgnoreCase)
                   || businessUnit.Trim().StartsWith(CurrentOrgUnit + " ", StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Filter trips theo BU hiện tại. Trip đã có global query filter cùng điều kiện trong WeighingDbContext;
    /// giữ hàm này cho các truy vấn cũ (lọc lặp không đổi kết quả).
    /// </summary>
    internal static IQueryable<Trip> FilterByOrgUnit(this IQueryable<Trip> query)
    {
        if (SeesAllUnits) return query;
        var orgUnit = CurrentOrgUnit;
        return query.Where(t => t.OrganizationUnitCode == orgUnit);
    }
}
