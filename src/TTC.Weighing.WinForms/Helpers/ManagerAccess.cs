using TTC.Weighing.WinForms.Forms;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Services;

namespace TTC.Weighing.WinForms.Helpers;

/// <summary>
/// Thao tác cần quyền quản lý (xem Fibre, xem / in lại phiếu...).
/// Quản lý = vai trò / quyền duyệt cấp Quản lý trong cấu hình <c>SugarcaneFlow:Approval:Manager*</c>.
/// </summary>
public static class ManagerAccess
{
    /// <summary>
    /// Tài khoản đang đăng nhập là quản lý. Chưa cấu hình vai trò quản lý → coi là không (phải nhờ quản lý xác thực).
    /// </summary>
    public static bool IsLoggedInManager()
    {
        var (roles, permissions) = SugarcaneFlowSettings.Current.ApproverRule(ApprovalLevel.Manager);
        var role = Program.LoggedInRole;
        return (role is not null && roles.Contains(role, StringComparer.OrdinalIgnoreCase))
               || (Program.LoggedInPermissions ?? []).Any(p => permissions.Contains(p, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Cho phép thao tác: quản lý đang đăng nhập → được ngay; người khác → quản lý xác thực tài khoản + mật khẩu.
    /// Trả false nếu hủy / không đạt.
    /// </summary>
    public static bool Require(IWin32Window owner, string actionDescription)
        => IsLoggedInManager() || ApprovalDialog.Request(owner, ApprovalLevel.Manager, actionDescription) is not null;

    /// <summary>Xem / in lại phiếu (nút In phiếu). Phiếu tự hiện sau khi hoàn thành KHÔNG qua bước này.</summary>
    public static bool RequireReprint(IWin32Window owner, string? tripNo)
        => Require(owner, Loc.T("print.reprint_approval", string.IsNullOrWhiteSpace(tripNo) ? "—" : tripNo));
}
