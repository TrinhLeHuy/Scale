using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Domain.Rules;

namespace TTC.Weighing.Application.UseCases.Reconcile;

public sealed record ReconcileTripCommand(Guid TripId, string CorrelationId);

public sealed record ReconcileTripResult(
    bool IsSuccess,
    Guid? ReconciliationId,
    bool IsPassed,
    IReadOnlyList<ReconciliationCheckResult> Checks,
    decimal? VehicleNetKg,
    decimal? BagCountNominalKg,
    IReadOnlyList<string> Blockers,
    string? FailureReason);

/// <summary>
/// R-020 — Đối soát chuyến. Lưu số đo độc lập và kết luận từng phép so sánh.
/// Không cộng cân xe với IoT, không lấy trung bình, không tự cho qua khi thiếu dữ liệu.
/// </summary>
public sealed class ReconcileTripHandler(
    ITripRepository trips,
    IWeighSessionRepository weighSessions,
    IMeasurementSessionRepository measurementSessions,
    IExternalDocumentRepository documents,
    IBomSnapshotRepository bomSnapshots,
    IWorkflowProfileRepository profiles,
    ITolerancePolicyProvider tolerancePolicies,
    IReconciliationRepository reconciliations,
    IUnitOfWork uow,
    IClock clock,
    ICurrentUser user,
    IAuditLogger audit)
{
    public const string RequiredPermission = "reconciliation.evaluate";

    public async Task<ReconcileTripResult> HandleAsync(
        ReconcileTripCommand cmd, CancellationToken ct = default)
    {
        var trip = await trips.GetByIdAsync(cmd.TripId, ct);
        if (trip is null)
            return Fail("Chuyến không tồn tại.");

        if (!user.HasPermission(RequiredPermission, trip.SiteId))
            return Fail("Không có quyền thực hiện đối soát.");

        var profile = await profiles.GetEffectiveAsync(
            trip.WorkflowProfileId, trip.WorkflowProfileVersion, ct);
        if (profile is null)
            return Fail("Không tìm thấy hồ sơ nghiệp vụ của chuyến.");

        var gross = await weighSessions.GetEffectiveEventAsync(cmd.TripId, WeightRole.Gross, 0, ct);
        var tare = await weighSessions.GetEffectiveEventAsync(cmd.TripId, WeightRole.Tare, 0, ct);

        decimal? vehicleNet = null;
        if (gross is not null && tare is not null)
        {
            var net = NetWeightCalculator.CalculateByRole(gross.NormalizedKg, tare.NormalizedKg);
            vehicleNet = net.VehicleNetKg;
        }

        var lines = await documents.ListLinesByTripAsync(cmd.TripId, ct);
        var primaryLine = lines.FirstOrDefault();

        var sessions = await measurementSessions.ListByTripAsync(cmd.TripId, ct);
        var usableSessions = sessions
            .Where(s => s.Status == MeasurementSessionStatus.Closed && s.DataComplete)
            .ToList();

        BomSnapshot? bom = null;
        if (primaryLine is not null)
        {
            bom = await bomSnapshots.GetEffectiveAsync(
                trip.SourceSystem, primaryLine.ItemCode, clock.UtcNow, ct);
        }

        var tolerance = await tolerancePolicies.GetAsync(profile.Id, profile.ProfileVersion, ct);

        var iotSession = usableSessions.FirstOrDefault(s => s.IotMassKg.HasValue);
        var bagSession = usableSessions.FirstOrDefault(s => s.NetBagCount.HasValue);

        var input = new ReconciliationInput
        {
            VehicleNetKg = vehicleNet,
            VehicleNetBasis = MassBasis.PackagedGoods,

            IotMassKg = iotSession?.IotMassKg,
            IotMassOrigin = iotSession?.IotMassOrigin,
            IotMassBasis = iotSession?.MassBasis ?? MassBasis.Unknown,

            AiBagCount = bagSession?.NetBagCount,

            WarehouseConfirmedQuantity = primaryLine?.ConfirmedQuantity,
            WarehouseConfirmedUom = primaryLine?.ConfirmedUom,
            PlannedQuantity = primaryLine?.PlannedQuantity,

            NominalKgPerUnit = bom?.NominalKgPerUnit,
            MinKgPerUnit = bom?.MinKgPerUnit,
            MaxKgPerUnit = bom?.MaxKgPerUnit,
            BomMassBasis = bom?.MassBasis ?? MassBasis.Unknown,

            Tolerance = tolerance,

            IotApplicable = usableSessions.Count > 0 || sessions.Any(s => s.IotMassKg.HasValue),
            BagCountApplicable = sessions.Any(s => s.ForwardBagCount.HasValue),
            WarehouseConfirmationRequired = profile.WarehouseConfirmationRequired
        };

        var checkResults = ReconciliationRules.Evaluate(input);

        var previous = await reconciliations.GetEffectiveAsync(cmd.TripId, ct);
        if (previous is not null)
            previous.IsEffective = false;

        var reconciliation = new Reconciliation
        {
            TripId = cmd.TripId,
            Revision = (previous?.Revision ?? 0) + 1,
            IsEffective = true,
            InputVersionsJson = BuildInputVersions(gross, tare, bom, profile),
            EvaluatedAt = clock.UtcNow,
            CreatedAt = clock.UtcNow,
            CreatedBy = user.UserId
        };

        reconciliation.Checks =
        [
            .. checkResults.Select(r => new ReconciliationCheck
            {
                ReconciliationId = reconciliation.Id,
                CheckType = r.CheckType,
                ObservedValue = r.ObservedValue,
                ExpectedValue = r.ExpectedValue,
                LowerBound = r.LowerBound,
                UpperBound = r.UpperBound,
                Difference = r.Difference,
                Unit = r.Unit,
                TolerancePolicyId = tolerance.PolicyId,
                TolerancePolicyVersion = tolerance.PolicyVersion,
                Verdict = r.Verdict,
                Detail = r.Detail,
                CreatedAt = clock.UtcNow,
                CreatedBy = user.UserId
            })
        ];

        await reconciliations.AddAsync(reconciliation, ct);

        // Chỉ chuyển RECONCILED khi mọi phép bắt buộc đạt (02_Quy_tac 2.1).
        if (reconciliation.IsPassed && trip.Lifecycle == TripLifecycle.WeighingCompleted)
        {
            trip.Lifecycle = TripLifecycle.Reconciled;
            trip.UpdatedAt = clock.UtcNow;
            trip.UpdatedBy = user.UserId;
        }

        await audit.LogAsync("TRIP_RECONCILED", cmd.TripId, user.UserId, null,
            $"{{\"revision\":{reconciliation.Revision},\"passed\":{reconciliation.IsPassed.ToString().ToLowerInvariant()}}}",
            null, cmd.CorrelationId, ct);

        await uow.SaveChangesAsync(ct);

        // Lượng quy đổi từ đếm bao — hiển thị riêng, không phải kg đã cân.
        var bagNominalKg = ReconciliationRules.ConvertBagCountToNominalKg(
            input.AiBagCount, input.NominalKgPerUnit);

        return new(
            IsSuccess: true,
            ReconciliationId: reconciliation.Id,
            IsPassed: reconciliation.IsPassed,
            Checks: checkResults,
            VehicleNetKg: vehicleNet,
            BagCountNominalKg: bagNominalKg,
            Blockers: [.. reconciliation.Blockers.Select(b => $"{b.CheckType}: {b.Detail}")],
            FailureReason: null);
    }

    private static string BuildInputVersions(
        WeighEvent? gross, WeighEvent? tare, BomSnapshot? bom, Domain.Entities.WorkflowProfile profile)
        => $"{{\"grossRevision\":{gross?.Revision.ToString() ?? "null"}," +
           $"\"tareRevision\":{tare?.Revision.ToString() ?? "null"}," +
           $"\"bomHash\":\"{bom?.SnapshotHash ?? "none"}\"," +
           $"\"profileVersion\":{profile.ProfileVersion}}}";

    private static ReconcileTripResult Fail(string reason)
        => new(false, null, false, [], null, null, [], reason);
}

/// <summary>
/// Cung cấp chính sách dung sai đã duyệt cho profile. Chưa duyệt thì trả policy rỗng
/// để engine kết luận Pending thay vì tự đặt ngưỡng (D-06).
/// </summary>
public interface ITolerancePolicyProvider
{
    Task<TolerancePolicy> GetAsync(Guid profileId, int profileVersion, CancellationToken ct = default);
}
