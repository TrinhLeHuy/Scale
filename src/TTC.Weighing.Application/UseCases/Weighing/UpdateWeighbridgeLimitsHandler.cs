using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Domain.Common;
using TTC.Weighing.Domain.Rules;

namespace TTC.Weighing.Application.UseCases.Weighing;

/// <param name="FirstMinKg">Cân lần 1 (vào) tối thiểu, kg. Null = không đặt.</param>
public sealed record UpdateWeighbridgeLimitsCommand(
    Guid WeighbridgeId,
    decimal? FirstMinKg,
    decimal? FirstMaxKg,
    decimal? SecondMinKg,
    decimal? SecondMaxKg,
    string CorrelationId);

public sealed record UpdateWeighbridgeLimitsResult(bool IsSuccess, string? FailureReason);

/// <summary>
/// Lưu giới hạn số cân Vào/Ra (min–Max, kg) của một bàn cân. Dùng chung cho mọi máy trạm của nhà máy.
/// Kiểm tra quyền ở đây, không chỉ ẩn nút trên UI.
/// </summary>
public sealed class UpdateWeighbridgeLimitsHandler(
    IWeighbridgeRepository weighbridges,
    IUnitOfWork uow,
    IClock clock,
    ICurrentUser user,
    IAuditLogger audit)
{
    public const string RequiredPermission = "weighbridge.limits.update";

    public async Task<UpdateWeighbridgeLimitsResult> HandleAsync(
        UpdateWeighbridgeLimitsCommand cmd, CancellationToken ct = default)
    {
        var bridge = await weighbridges.GetByIdAsync(cmd.WeighbridgeId, ct);
        if (bridge is null)
            return new(false, DomainText.T("rule.bridge_not_found", "Bàn cân không tồn tại."));

        if (!user.HasPermission(RequiredPermission, bridge.SiteId))
            return new(false, DomainText.T("rule.weigh_limit_no_permission",
                "Không có quyền đổi giới hạn số cân tại nhà máy này."));

        if (WeighLimitRules.Validate(cmd.FirstMinKg, cmd.FirstMaxKg, 1) is { } firstError)
            return new(false, firstError);
        if (WeighLimitRules.Validate(cmd.SecondMinKg, cmd.SecondMaxKg, 2) is { } secondError)
            return new(false, secondError);

        if (bridge.FirstWeightMinKg == cmd.FirstMinKg && bridge.FirstWeightMaxKg == cmd.FirstMaxKg
            && bridge.SecondWeightMinKg == cmd.SecondMinKg && bridge.SecondWeightMaxKg == cmd.SecondMaxKg)
            return new(true, null);

        var before = Json(bridge.FirstWeightMinKg, bridge.FirstWeightMaxKg, bridge.SecondWeightMinKg, bridge.SecondWeightMaxKg);

        bridge.FirstWeightMinKg = cmd.FirstMinKg;
        bridge.FirstWeightMaxKg = cmd.FirstMaxKg;
        bridge.SecondWeightMinKg = cmd.SecondMinKg;
        bridge.SecondWeightMaxKg = cmd.SecondMaxKg;
        bridge.UpdatedAt = clock.UtcNow;
        bridge.UpdatedBy = user.UserId;
        bridge.Version++;

        await audit.LogAsync("WEIGHBRIDGE_LIMITS_UPDATED", null, user.UserId,
            before, Json(cmd.FirstMinKg, cmd.FirstMaxKg, cmd.SecondMinKg, cmd.SecondMaxKg),
            $"bridge={bridge.Code}", cmd.CorrelationId, ct);

        await uow.SaveChangesAsync(ct);
        return new(true, null);
    }

    private static string Json(decimal? a, decimal? b, decimal? c, decimal? d)
    {
        static string N(decimal? v) => v?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null";
        return $"{{\"first_min_kg\":{N(a)},\"first_max_kg\":{N(b)},\"second_min_kg\":{N(c)},\"second_max_kg\":{N(d)}}}";
    }
}
