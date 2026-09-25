using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Domain.Rules;

namespace TTC.Weighing.Application.UseCases.Reconcile;

public sealed record AllocateTripCommand(
    Guid TripId,
    AllocationMethod Method,
    IReadOnlyList<AllocationRequestLine> Lines,
    string ApprovalRef,
    string CorrelationId);

public sealed record AllocateTripResult(
    bool IsSuccess,
    decimal? MeasuredTotalKg,
    decimal? TotalAllocatedKg,
    IReadOnlyList<AllocationLineResult> Lines,
    string? FailureReason);

/// <summary>
/// R-016 / R-021 — Phân bổ lượng đã đo cho từng chủ mía hoặc Shipment.
/// Cân tổng giữ nguyên; tổng phân bổ bảo toàn tổng thực tế.
/// Chưa duyệt thì chưa gửi kết quả phân bổ (S02 tr.18).
/// </summary>
public sealed class AllocateTripHandler(
    ITripRepository trips,
    IWeighSessionRepository weighSessions,
    IAllocationRepository allocations,
    IApprovalRepository approvals,
    IWorkflowProfileRepository profiles,
    IUnitOfWork uow,
    IClock clock,
    ICurrentUser user,
    IAuditLogger audit)
{
    public const string RequiredPermission = "reconciliation.allocate";

    public async Task<AllocateTripResult> HandleAsync(
        AllocateTripCommand cmd, CancellationToken ct = default)
    {
        var trip = await trips.GetByIdAsync(cmd.TripId, ct);
        if (trip is null)
            return Fail("Chuyến không tồn tại.");

        if (!user.HasPermission(RequiredPermission, trip.SiteId))
            return Fail("Không có quyền phân bổ.");

        if (!Guid.TryParse(cmd.ApprovalRef, out var approvalId))
            return Fail("Phân bổ bắt buộc có phê duyệt hợp lệ.");

        var approval = await approvals.GetByIdAsync(approvalId, ct);
        if (approval is null || !approval.IsGranted)
            return Fail("Phê duyệt phân bổ chưa được cấp.");

        var profile = await profiles.GetEffectiveAsync(
            trip.WorkflowProfileId, trip.WorkflowProfileVersion, ct);
        if (profile is null)
            return Fail("Không tìm thấy hồ sơ nghiệp vụ của chuyến.");

        var gross = await weighSessions.GetEffectiveEventAsync(cmd.TripId, WeightRole.Gross, 0, ct);
        var tare = await weighSessions.GetEffectiveEventAsync(cmd.TripId, WeightRole.Tare, 0, ct);

        if (gross is null || tare is null)
            return Fail("Chưa có đủ hai lượt cân hiệu lực để phân bổ.");

        var net = NetWeightCalculator.Calculate(
            profile.Direction,
            profile.Direction == ProfileDirection.Outbound ? tare.NormalizedKg : gross.NormalizedKg,
            profile.Direction == ProfileDirection.Outbound ? gross.NormalizedKg : tare.NormalizedKg);

        // Net âm hoặc bằng 0 là ngoại lệ, phải xử lý theo quy trình trước khi phân bổ.
        if (net.RequiresExceptionHandling)
            return Fail(
                $"Net cân xe {net.VehicleNetKg:N3} kg là ngoại lệ (âm hoặc bằng 0), " +
                "phải xử lý theo quy trình được duyệt trước khi phân bổ.");

        var result = AllocationRules.Allocate(net.VehicleNetKg, cmd.Method, cmd.Lines);

        if (!result.IsValid)
            return Fail(result.InvalidReason);

        var entities = result.Lines.Select(l => new Allocation
        {
            TripId = cmd.TripId,
            MeasuredTotalRef = gross.Id,
            DocumentLineId = l.DocumentLineId,
            OwnerRef = l.OwnerRef,
            AllocatedKg = l.AllocatedKg,
            Method = cmd.Method,
            Ratio = cmd.Lines.FirstOrDefault(r => r.DocumentLineId == l.DocumentLineId)?.Ratio,
            RoundingAdjustmentKg = l.RoundingAdjustmentKg,
            ApprovalRef = cmd.ApprovalRef,
            CreatedAt = clock.UtcNow,
            CreatedBy = user.UserId
        }).ToList();

        await allocations.ReplaceForTripAsync(cmd.TripId, entities, ct);

        await audit.LogAsync("TRIP_ALLOCATED", cmd.TripId, user.UserId, null,
            $"{{\"method\":\"{cmd.Method}\",\"totalKg\":{net.VehicleNetKg},\"lines\":{entities.Count}}}",
            approval.Reason, cmd.CorrelationId, ct);

        await uow.SaveChangesAsync(ct);

        return new(true, net.VehicleNetKg, result.TotalAllocatedKg, result.Lines, null);
    }

    private static AllocateTripResult Fail(string? reason) => new(false, null, null, [], reason);
}
