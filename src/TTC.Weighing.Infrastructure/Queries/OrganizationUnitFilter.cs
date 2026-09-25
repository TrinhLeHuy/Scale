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

    /// <summary>Filter trips theo BU hiện tại. Null = admin → không filter.</summary>
    internal static IQueryable<Trip> FilterByOrgUnit(this IQueryable<Trip> query)
    {
        var orgUnit = CurrentOrgUnit;
        if (!string.IsNullOrEmpty(orgUnit))
            return query.Where(t => t.OrganizationUnitCode == orgUnit);
        return query;
    }
}
