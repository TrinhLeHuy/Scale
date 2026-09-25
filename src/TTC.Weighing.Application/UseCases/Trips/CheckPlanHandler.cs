using TTC.Weighing.Application.Abstractions;

namespace TTC.Weighing.Application.UseCases.Trips;

public sealed record CheckPlanCommand(
    Guid SiteId,
    string Plate,
    string CorrelationId);

public sealed record CheckPlanResult(
    bool IsSuccess,
    IReadOnlyList<PlanEntry> Plans,
    string? ErrorMessage);

/// <summary>
/// R-001 — Kiểm tra biển số trong kế hoạch FRM/ERP trước khi đăng tài.
/// Lỗi kết nối FRM/ERP chặn đăng tài chính thức (Req 1.4).
/// </summary>
public sealed class CheckPlanHandler(
    IFrmPlanLookup planLookup,
    IClock clock,
    ICurrentUser user)
{
    public const string RequiredPermission = "trip.register";

    public async Task<CheckPlanResult> HandleAsync(
        CheckPlanCommand cmd, CancellationToken ct = default)
    {
        if (!user.HasPermission(RequiredPermission, cmd.SiteId))
            return new(false, [], "Không có quyền tra cứu tại nhà máy này.");

        var result = await planLookup.LookupAsync(
            cmd.SiteId, cmd.Plate, clock.UtcNow, ct);

        return new(result.IsSuccess, result.Plans, result.ErrorMessage);
    }
}
