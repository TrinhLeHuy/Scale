using FsCheck;
using FsCheck.Xunit;
using NSubstitute;
using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.UseCases.Trips;
using TTC.Weighing.Domain.Common;
using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Application.Tests.Properties;

/// <summary>
/// Property 1: Quy tắc bắt buộc VehicleType theo loại hồ sơ.
/// **Validates: Requirements 2.1, 2.4**
///
/// - Với mọi RegisterTripCommand mía nhập + VehicleType null → IsSuccess = false
/// - Với mọi command không phải mía nhập + VehicleType null → IsSuccess = true (điều kiện khác hợp lệ)
/// </summary>
public class VehicleTypePropertyTests
{
    // --- Shared constants ---
    private static readonly DateTimeOffset Now = new(2025, 1, 15, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid SiteId = Guid.NewGuid();
    private static readonly Guid GateId = Guid.NewGuid();

    // --- FsCheck generators ---

    /// <summary>
    /// Generates a non-null, non-empty alphanumeric string suitable for plate numbers, driver refs, etc.
    /// </summary>
    private static Gen<string> GenNonEmptyAlphaNum() =>
        from len in Gen.Choose(3, 15)
        from chars in Gen.ArrayOf(len, Gen.Elements(
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-.".ToCharArray()))
        select new string(chars);

    /// <summary>
    /// Generates a WorkflowProfile that IS inbound sugarcane (matches IsInboundSugarcaneProfile).
    /// Two paths: Code starts with "CANE_IN" OR (Direction=Inbound AND SourceSystem="FRM").
    /// </summary>
    private static Gen<WorkflowProfile> GenInboundSugarcaneProfile() =>
        Gen.OneOf(
            // Path 1: Code starts with CANE_IN
            from suffix in GenNonEmptyAlphaNum()
            select CreateApprovedProfile($"CANE_IN_{suffix}", "FRM", ProfileDirection.Inbound),

            // Path 2: Direction=Inbound + SourceSystem=FRM, code does NOT start with CANE_IN
            from prefix in Gen.Elements("SUGAR", "MIA", "FARM", "INBOUND_FRM")
            from suffix in GenNonEmptyAlphaNum()
            select CreateApprovedProfile($"{prefix}_{suffix}", "FRM", ProfileDirection.Inbound)
        );

    /// <summary>
    /// Generates a WorkflowProfile that is NOT inbound sugarcane.
    /// Must fail BOTH conditions: code NOT starting with CANE_IN AND NOT (Direction=Inbound + SourceSystem=FRM).
    /// </summary>
    private static Gen<WorkflowProfile> GenNonSugarcaneProfile() =>
        Gen.OneOf(
            // ERP source, Inbound direction — not sugarcane because SourceSystem != FRM
            from suffix in GenNonEmptyAlphaNum()
            select CreateApprovedProfile($"MATERIAL_IN_{suffix}", "ERP", ProfileDirection.Inbound),

            // FRM source, Outbound direction — not sugarcane because Direction != Inbound
            from suffix in GenNonEmptyAlphaNum()
            select CreateApprovedProfile($"MATERIAL_OUT_{suffix}", "FRM", ProfileDirection.Outbound),

            // ERP source, Outbound direction
            from suffix in GenNonEmptyAlphaNum()
            select CreateApprovedProfile($"MATERIAL_OUT_{suffix}", "ERP", ProfileDirection.Outbound)
        );

    private static WorkflowProfile CreateApprovedProfile(
        string code, string sourceSystem, ProfileDirection direction) => new()
    {
        Code = code,
        Name = $"Profile {code}",
        ProfileVersion = 1,
        SourceSystem = sourceSystem,
        Direction = direction,
        ApprovedBy = "admin",
        ApprovedAt = Now.AddDays(-30),
        EffectiveFrom = Now.AddDays(-60),
        EffectiveTo = Now.AddDays(60)
    };

    /// <summary>
    /// Creates a RegisterTripCommand with the given profile and VehicleType.
    /// Other fields are populated with random valid data.
    /// </summary>
    private static Gen<RegisterTripCommand> GenCommand(
        WorkflowProfile profile, VehicleType? vehicleType, bool withPlan) =>
        from plate in GenNonEmptyAlphaNum()
        from driverRef in GenNonEmptyAlphaNum()
        from refNo in GenNonEmptyAlphaNum()
        select new RegisterTripCommand(
            SiteId: SiteId,
            GateId: GateId,
            WorkflowProfileId: profile.Id,
            WorkflowProfileVersion: profile.ProfileVersion,
            ReferenceTripNo: withPlan ? refNo : null,
            PlanSource: withPlan ? profile.SourceSystem : null,
            CommodityType: withPlan ? "Sugarcane" : null,
            OwnerOrCustomer: withPlan ? "Owner" : null,
            WarehouseCode: withPlan ? "KHO-A" : null,
            TractorPlate: plate,
            TrailerPlate: null,
            DriverRef: driverRef,
            VehicleType: vehicleType,
            SugarcaneType: null,
            SugarcaneVariety: null,
            SugarcaneOwner: null,
            HarvestMethod: null,
            Station: null,
            CropSeason: null,
            CorrelationId: Guid.NewGuid().ToString(),
            IssueTemporaryQrOnly: !withPlan);

    // --- Handler factory with fully mocked dependencies ---

    private static (RegisterTripHandler handler, IWorkflowProfileRepository profiles) CreateHandler()
    {
        var trips = Substitute.For<ITripRepository>();
        var qrTokens = Substitute.For<IQrTokenRepository>();
        var profiles = Substitute.For<IWorkflowProfileRepository>();
        var tripNumbers = Substitute.For<ITripNumberGenerator>();
        var queueSequence = Substitute.For<IQueueSequenceGenerator>();
        var weighbridges = Substitute.For<IWeighbridgeRepository>();
        var camera = Substitute.For<ICameraService>();
        var uow = Substitute.For<IUnitOfWork>();
        var clock = Substitute.For<IClock>();
        var user = Substitute.For<ICurrentUser>();
        var audit = Substitute.For<IAuditLogger>();

        clock.UtcNow.Returns(Now);
        user.UserId.Returns("test-operator");
        user.HasPermission(RegisterTripHandler.RequiredPermission, Arg.Any<Guid>()).Returns(true);
        tripNumbers.NextAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("TT-TEST-001");
        queueSequence.NextAsync(Arg.Any<Guid>(), Arg.Any<VehicleType>(), Arg.Any<CancellationToken>())
            .Returns(1);

        // Setup weighbridge for queue assignment
        var bridgeId = Guid.NewGuid();
        var bridge = new Weighbridge
        {
            Code = "BC-01",
            SiteId = SiteId,
            IsOutOfService = false,
            MinCapacityKg = 100,
            MaxCapacityKg = 80000,
            ResolutionKg = 10
        };
        typeof(AuditableEntity).GetProperty(nameof(AuditableEntity.Id))!.SetValue(bridge, bridgeId);
        weighbridges.ListBySiteAsync(SiteId, Arg.Any<CancellationToken>()).Returns([bridge]);
        weighbridges.GetByIdAsync(bridgeId, Arg.Any<CancellationToken>()).Returns(bridge);
        trips.CountPendingByWeighbridgeAsync(SiteId, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, int>());
        trips.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new Trip { SiteId = SiteId });

        var queueTrip = new QueueTripHandler(weighbridges, trips);
        var handler = new RegisterTripHandler(
            trips, qrTokens, profiles, tripNumbers,
            queueSequence, queueTrip, weighbridges, camera,
            uow, clock, user, audit);

        return (handler, profiles);
    }

    // ================================================================
    // Property 1a: Inbound sugarcane + VehicleType null → always fails
    // ================================================================

    [Property(MaxTest = 100)]
    public Property InboundSugarcane_NullVehicleType_AlwaysFails()
    {
        return Prop.ForAll(
            GenInboundSugarcaneProfile().ToArbitrary(),
            GenNonEmptyAlphaNum().ToArbitrary(),
            (profile, plate) =>
            {
                var (handler, profiles) = CreateHandler();
                profiles.GetEffectiveAsync(profile.Id, profile.ProfileVersion, Arg.Any<CancellationToken>())
                    .Returns(profile);

                // Build command with VehicleType = null and a valid reference (planned flow)
                var cmd = new RegisterTripCommand(
                    SiteId: SiteId,
                    GateId: GateId,
                    WorkflowProfileId: profile.Id,
                    WorkflowProfileVersion: profile.ProfileVersion,
                    ReferenceTripNo: $"FRM-{plate}",
                    PlanSource: profile.SourceSystem,
                    CommodityType: "Sugarcane",
                    OwnerOrCustomer: "Owner",
                    WarehouseCode: "KHO-A",
                    TractorPlate: plate,
                    TrailerPlate: null,
                    DriverRef: "DRV-001",
                    VehicleType: null, // <-- key: null VehicleType
                    SugarcaneType: null,
                    SugarcaneVariety: null,
                    SugarcaneOwner: null,
                    HarvestMethod: null,
                    Station: null,
                    CropSeason: null,
                    CorrelationId: Guid.NewGuid().ToString());

                var result = handler.HandleAsync(cmd).GetAwaiter().GetResult();

                return (!result.IsSuccess)
                    .Label($"Expected failure for inbound sugarcane profile '{profile.Code}' " +
                           $"(Direction={profile.Direction}, Source={profile.SourceSystem}) " +
                           $"with null VehicleType, but got IsSuccess=true");
            });
    }

    // ================================================================
    // Property 1b: Non-sugarcane profile + VehicleType null → succeeds
    // (given all other conditions are valid)
    // ================================================================

    [Property(MaxTest = 100)]
    public Property NonSugarcane_NullVehicleType_AllowsRegistration()
    {
        return Prop.ForAll(
            GenNonSugarcaneProfile().ToArbitrary(),
            GenNonEmptyAlphaNum().ToArbitrary(),
            (profile, plate) =>
            {
                var (handler, profiles) = CreateHandler();
                profiles.GetEffectiveAsync(profile.Id, profile.ProfileVersion, Arg.Any<CancellationToken>())
                    .Returns(profile);

                // Non-sugarcane command with VehicleType = null (temp flow for simplicity)
                var cmd = new RegisterTripCommand(
                    SiteId: SiteId,
                    GateId: GateId,
                    WorkflowProfileId: profile.Id,
                    WorkflowProfileVersion: profile.ProfileVersion,
                    ReferenceTripNo: null,
                    PlanSource: null,
                    CommodityType: null,
                    OwnerOrCustomer: null,
                    WarehouseCode: null,
                    TractorPlate: plate,
                    TrailerPlate: null,
                    DriverRef: null,
                    VehicleType: null, // <-- key: null VehicleType
                    SugarcaneType: null,
                    SugarcaneVariety: null,
                    SugarcaneOwner: null,
                    HarvestMethod: null,
                    Station: null,
                    CropSeason: null,
                    CorrelationId: Guid.NewGuid().ToString(),
                    IssueTemporaryQrOnly: true);

                var result = handler.HandleAsync(cmd).GetAwaiter().GetResult();

                return result.IsSuccess
                    .Label($"Expected success for non-sugarcane profile '{profile.Code}' " +
                           $"(Direction={profile.Direction}, Source={profile.SourceSystem}) " +
                           $"with null VehicleType, but got FailureReason='{result.FailureReason}'");
            });
    }
}
