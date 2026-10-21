using Microsoft.EntityFrameworkCore;
using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Infrastructure.Persistence;
using TTC.Weighing.WinForms.Presenters;

namespace TTC.Weighing.WinForms.Development;

/// <summary>
/// Danh tính người dùng cục bộ, dùng khi chạy trên cơ sở dữ liệu thật ở máy dev.
/// KHÔNG dùng cho vận hành: vai trò và phân quyền phải lấy từ nguồn xác thực đã duyệt (D-08).
/// </summary>
public sealed class LocalOperatorIdentity(Guid siteId, string displayName) : ICurrentUser
{
    public string UserId => "local.operator";
    public string DisplayName => displayName;

    public IReadOnlyCollection<string> Roles =>
        ["GATE", "WEIGH_OPERATOR", "SUPERVISOR", "WAREHOUSE", "RECONCILER", "IT"];

    public IReadOnlyCollection<Guid> AllowedSiteIds => [siteId, Guid.Empty];

    public bool HasPermission(string permission, Guid siteId) => AllowedSiteIds.Contains(siteId);
}

/// <summary>
/// Chọn chuyến đang cần cân từ cơ sở dữ liệu.
/// Đây là bản thay thế tạm cho thao tác quét QR tại đầu đọc; vận hành thật phải đi qua
/// ResolveQR để biết chuyến nào đang ở bàn cân (03_Du_lieu mục 8).
/// </summary>
public sealed class DatabaseWeighWorkspaceProvider(WeighingDbContext db, Forms.ISiteContext site)
    : IWeighWorkspaceProvider
{
    public async Task<WeighWorkspace?> GetCurrentAsync(CancellationToken ct = default)
    {
        var siteId = site.CurrentSiteId;

        var candidate = await db.Trips
            .Where(t => t.SiteId == siteId
                && t.DeletedAt == null
                && t.ExecutionStatus == ExecutionStatus.Active
                && (t.Lifecycle == TripLifecycle.Registered
                    || t.Lifecycle == TripLifecycle.InProgress
                    || t.Lifecycle == TripLifecycle.WeighingCompleted))
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new
            {
                t.Id,
                t.Version,
                t.TripNo,
                t.ReferenceTripNo,
                t.TractorPlate,
                t.WorkflowProfileId,
                t.WorkflowProfileVersion
            })
            .FirstOrDefaultAsync(ct);

        if (candidate is null)
            return null;

        var profile = await db.WorkflowProfiles.FirstOrDefaultAsync(
            p => p.Id == candidate.WorkflowProfileId
                && p.ProfileVersion == candidate.WorkflowProfileVersion, ct);

        if (profile is null || profile.WeightRoleSequence.Count == 0)
            return null;

        var lane = await db.Lanes
            .Where(l => l.SiteId == siteId)
            .OrderBy(l => l.Code)
            .FirstOrDefaultAsync(ct);

        if (lane?.WeighbridgeId is null)
            return null;

        var bridge = await db.Weighbridges.FirstOrDefaultAsync(
            w => w.Id == lane.WeighbridgeId.Value, ct);

        if (bridge is null)
            return null;

        // Phân đoạn tiếp theo là phân đoạn đầu tiên chưa có bản cân hiệu lực.
        var segmentIndex = 0;

        for (var segment = 0; segment < profile.WeightRoleSequence.Count; segment++)
        {
            var role = profile.WeightRoleSequence[segment];

            var committed = await db.WeighEvents.AnyAsync(
                e => e.TripId == candidate.Id && e.Role == role && e.IsEffective, ct);

            if (!committed)
            {
                segmentIndex = segment;
                break;
            }

            segmentIndex = segment;
        }

        var checkpoint = segmentIndex == 0
            ? CheckpointType.FirstWeight
            : CheckpointType.SecondWeight;

        return new WeighWorkspace(
            TripId: candidate.Id,
            TripVersion: candidate.Version,
            TripNo: candidate.TripNo,
            ReferenceNo: candidate.ReferenceTripNo,
            Plate: candidate.TractorPlate,
            WeighbridgeId: bridge.Id,
            WeighbridgeCode: bridge.Code,
            LaneId: lane.Id,
            Checkpoint: checkpoint,
            SegmentIndex: segmentIndex);
    }
}

/// <summary>
/// Đọc nhà máy đang làm việc từ mã nhà máy trong cấu hình.
/// Chỉ dùng ở máy dev; vận hành phải xác thực nhà máy thuộc quyền người dùng (R-002).
/// </summary>
public sealed class SiteCodeContext : Forms.ISiteContext
{
    public SiteCodeContext(WeighingDbContext db, string siteCode)
    {
        var site = db.Sites.FirstOrDefault(s => s.Code == siteCode);

        if (site is null)
        {
            site = new Domain.Entities.Site
            {
                Code = siteCode,
                Name = $"Nhà máy {siteCode}",
                TimeZoneId = "SE Asia Standard Time",
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = "auto-bootstrap"
            };

            db.Sites.Add(site);
            db.SaveChanges();
        }

        CurrentSiteId = site.Id;
        CurrentSiteCode = site.Code;
        CurrentSiteName = site.Name;
    }

    public Guid CurrentSiteId { get; }
    public string CurrentSiteCode { get; }
    public string? CurrentSiteName { get; }
    public string? CurrentSiteAddress { get; set; }

    /// <summary>Lấy từ Program.ActiveOrganizationUnitCode sau đăng nhập. Null = admin → thấy tất cả.</summary>
    public string? OrganizationUnitCode => WinForms.Program.ActiveOrganizationUnitCode;
}
