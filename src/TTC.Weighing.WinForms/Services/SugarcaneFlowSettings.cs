using Microsoft.Extensions.Configuration;

namespace TTC.Weighing.WinForms.Services;

public enum FibreEditAccess { Denied, Direct, NeedsApproval }

/// <summary>Cấp phê duyệt thao tác ngoại lệ trong luồng mía.</summary>
public enum ApprovalLevel
{
    /// <summary>User quản lý — nhập tay số cân, nhập tay Pol/Brix, in lại phiếu cân.</summary>
    Manager,

    /// <summary>Trưởng đơn vị — nhập tay số cân tạp chất.</summary>
    UnitHead
}

/// <summary>
/// Tham số luồng mía.
/// - Vai trò/quyền phê duyệt: appsettings, mục "SugarcaneFlow:Approval".
/// - Ngưỡng (tạp chất, CCS, Fibre, khối lượng tối đa, cân lần 1 tối thiểu): Portal DigiScale "Quy tắc cân"
///   theo đơn vị (<see cref="PortalScaleRules"/>). Không có trong appsettings, không hardcode.
/// Giá trị null = Portal CHƯA CÀI hoặc CHƯA LẤY ĐƯỢC → màn hình cảnh báo, không tự điền giá trị.
/// </summary>
public sealed class SugarcaneFlowSettings
{
    public IReadOnlyList<string> ManagerRoles { get; private init; } = [];
    public IReadOnlyList<string> ManagerPermissions { get; private init; } = [];
    public IReadOnlyList<string> UnitHeadRoles { get; private init; } = [];
    public IReadOnlyList<string> UnitHeadPermissions { get; private init; } = [];

    /// <summary>Đã lấy được quy tắc cân từ Portal chưa. False → mọi ngưỡng dưới đây = null.</summary>
    public bool PortalRulesLoaded { get; private init; }

    /// <summary>Vai trò/quyền DigiScale được cài hệ số xơ (Fibre) trên màn CCS — "SugarcaneFlow:Ccs".</summary>
    public IReadOnlyList<string> FibreEditorRoles { get; private init; } = [];
    public IReadOnlyList<string> FibreEditorPermissions { get; private init; } = [];

    /// <summary>
    /// Quyền cài hệ số xơ của tài khoản đang đăng nhập:
    /// - Direct: có vai trò/quyền đã cấu hình → cài ngay.
    /// - NeedsApproval: chưa cấu hình vai trò/quyền → phải có quản lý xác nhận (người thứ hai).
    /// - Denied: đã cấu hình nhưng tài khoản không có → ẩn chức năng.
    /// </summary>
    public FibreEditAccess FibreAccess(string? role, IReadOnlyCollection<string>? permissions)
    {
        if (FibreEditorRoles.Count == 0 && FibreEditorPermissions.Count == 0)
            return FibreEditAccess.NeedsApproval;

        var ok = (role is not null && FibreEditorRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
                 || (permissions ?? []).Any(p => FibreEditorPermissions.Contains(p, StringComparer.OrdinalIgnoreCase));
        return ok ? FibreEditAccess.Direct : FibreEditAccess.Denied;
    }

    /// <summary>Ngưỡng tạp chất (%) — vượt thì hỏi có lưu không.</summary>
    public decimal? ImpurityThresholdPercent { get; private init; }

    /// <summary>Khối lượng xe tối đa (tấn) — vượt thì cảnh báo ở trạm tạp chất.</summary>
    public decimal? MaxVehicleMassTon { get; private init; }

    /// <summary>Khối lượng mẫu lần 1 tối thiểu (kg). Null = chưa có từ Portal → không kiểm.</summary>
    public decimal? MinSampleKg { get; private init; }

    /// <summary>Fibre (tỉ lệ xơ) dùng tính CCS — xác định hàng tuần.</summary>
    public decimal? CcsFibre { get; private init; }

    /// <summary>Ngưỡng CCS hợp lệ [min, max] — ngoài khoảng thì hỏi có lưu không.</summary>
    public decimal? CcsMin { get; private init; }
    public decimal? CcsMax { get; private init; }

    public static SugarcaneFlowSettings Current => Load(Program.AppConfiguration);

    public static SugarcaneFlowSettings Load(IConfiguration? cfg)
    {
        static IReadOnlyList<string> List(IConfiguration? c, string key)
            => c is null
                ? []
                : c.GetSection(key).GetChildren()
                    .Select(x => x.Value?.Trim())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .Select(v => v!)
                    .ToArray();

        // Ngưỡng chỉ lấy từ Portal. Giá trị mặc định (defaults) cũng do Portal trả, không phải hằng số trong app.
        var rules = PortalScaleRules.Rules;

        return new SugarcaneFlowSettings
        {
            ManagerRoles = List(cfg, "SugarcaneFlow:Approval:ManagerRoles"),
            ManagerPermissions = List(cfg, "SugarcaneFlow:Approval:ManagerPermissions"),
            UnitHeadRoles = List(cfg, "SugarcaneFlow:Approval:UnitHeadRoles"),
            UnitHeadPermissions = List(cfg, "SugarcaneFlow:Approval:UnitHeadPermissions"),
            PortalRulesLoaded = rules is not null,
            FibreEditorRoles = List(cfg, "SugarcaneFlow:Ccs:FibreEditorRoles"),
            FibreEditorPermissions = List(cfg, "SugarcaneFlow:Ccs:FibreEditorPermissions"),
            ImpurityThresholdPercent = rules?.ImpurityMax,
            MaxVehicleMassTon = rules?.MaxWeightTon ?? rules?.Defaults?.MaxWeightTon,
            MinSampleKg = rules?.MinBeforeKg ?? rules?.Defaults?.MinBeforeKg,
            CcsFibre = rules?.Fibre,
            CcsMin = rules?.CcsMin,
            CcsMax = rules?.CcsMax
        };
    }

    public (IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions) ApproverRule(ApprovalLevel level)
        => level == ApprovalLevel.UnitHead
            ? (UnitHeadRoles, UnitHeadPermissions)
            : (ManagerRoles, ManagerPermissions);
}
