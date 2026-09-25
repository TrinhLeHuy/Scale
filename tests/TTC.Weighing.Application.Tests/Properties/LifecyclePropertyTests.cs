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
/// Property 4: Registration lifecycle
/// **Validates: Requirements 3.1, 4.1**
///
/// - Với mọi command có kế hoạch (ReferenceTripNo non-empty) + IssueTemporaryQrOnly = false → Lifecycle = Registered
/// - Với mọi command không có kế hoạch (ReferenceTripNo null/empty) hoặc IssueTemporaryQrOnly = true → Lifecycle = TempRegistered
/// </summary>
public class LifecyclePropertyTests
{
    private static readonly DateTimeOffset Now = new(2025, 6, 15, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid SiteId = Guid.NewGuid();
    private static readonly Guid GateId = Guid.NewGuid();

    /// <summary>Hồ sơ vật tư nhập — không phải mía, VehicleType không bắt buộc.</summary>
    private static WorkflowProfile ValidMaterialProfile() => new()
    {
        Code = "MATERIAL_IN_01",
        Name = "Vật tư nhập",
        ProfileVersion = 1,
        SourceSystem = "ERP",
        Direction = ProfileDirection.Inbound,
        ApprovedBy = "admin",
        ApprovedAt = Now.AddDays(-30),
        EffectiveFrom = Now.AddDays(-60),
        EffectiveTo = Now.AddDays(60)
    };

    private static (RegisterTripHandler handler, ITripRepository trips) CreateHandler(WorkflowProfile profile)
    {
        var tripsRepo = Substitute.For<ITripRepository>();
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
        user.UserId.Returns("prop-test-user");
        user.HasPermission(RegisterTripHandler.RequiredPermission, Arg.Any<Guid>()).Returns(true);
        tripNumbers.NextAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("TT-PROP-001");

        profiles.GetEffectiveAsync(profile.Id, profile.ProfileVersion, Arg.Any<CancellationToken>())
            .Returns(profile);

        // QueueTripHandler needs trip lookup for assigning fields
        tripsRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ci => new Trip { SiteId = SiteId });

        // No weighbridges available — keeps test focused on lifecycle, not queue logic
        weighbridges.ListBySiteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<Weighbridge>());
        tripsRepo.CountPendingByWeighbridgeAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, int>());

        queueSequence.NextAsync(Arg.Any<Guid>(), Arg.Any<VehicleType>(), Arg.Any<CancellationToken>())
            .Returns(1);

        var queueTrip = new QueueTripHandler(weighbridges, tripsRepo);
        var handler = new RegisterTripHandler(
            tripsRepo, qrTokens, profiles, tripNumbers,
            queueSequence, queueTrip, weighbridges, camera,
            uow, clock, user, audit);

        return (handler, tripsRepo);
    }

    /// <summary>
    /// FsCheck Arbitrary: non-empty reference trip numbers (simulating a plan source).
    /// Generates strings of length 1–50 from alphanumeric + dash characters.
    /// </summary>
    private static Arbitrary<string> NonEmptyReferenceArb()
    {
        return Arb.Default.NonEmptyString()
            .Filter(s => !string.IsNullOrWhiteSpace(s.Get))
            .Convert(s => s.Get, NonEmptyString.NewNonEmptyString);
    }

    /// <summary>
    /// FsCheck Arbitrary: null or whitespace-only strings (simulating no plan).
    /// </summary>
    private static Arbitrary<string?> NullOrEmptyReferenceArb()
    {
        return Gen.Elements<string?>(null, "", " ", "  ", "\t")
            .ToArbitrary();
    }

    // ================================================================
    // Property 4a: Command with plan + IssueTemporaryQrOnly=false → Registered
    // **Validates: Requirements 3.1**
    // ================================================================

    [Property(MaxTest = 100)]
    public Property Planned_command_produces_registered_lifecycle()
    {
        var profile = ValidMaterialProfile();
        var (handler, tripsRepo) = CreateHandler(profile);

        // Capture the Trip entity added to repository
        Trip? capturedTrip = null;
        tripsRepo.AddAsync(Arg.Do<Trip>(t => capturedTrip = t), Arg.Any<CancellationToken>());

        return Prop.ForAll(
            NonEmptyReferenceArb(),
            (string referenceTripNo) =>
            {
                capturedTrip = null;

                var cmd = new RegisterTripCommand(
                    SiteId: SiteId,
                    GateId: GateId,
                    WorkflowProfileId: profile.Id,
                    WorkflowProfileVersion: profile.ProfileVersion,
                    ReferenceTripNo: referenceTripNo,
                    PlanSource: "ERP",
                    CommodityType: "Material",
                    OwnerOrCustomer: "Test Customer",
                    WarehouseCode: "KHO-A",
                    TractorPlate: "51A-123.45",
                    TrailerPlate: null,
                    DriverRef: "DRV-001",
                    VehicleType: null,
                    SugarcaneType: null,
                    SugarcaneVariety: null,
                    SugarcaneOwner: null,
                    HarvestMethod: null,
                    Station: null,
                    CropSeason: null,
                    CorrelationId: Guid.NewGuid().ToString(),
                    IssueTemporaryQrOnly: false);

                var result = handler.HandleAsync(cmd).GetAwaiter().GetResult();

                return (result.IsSuccess).Label("IsSuccess must be true")
                    .And((capturedTrip is not null).Label("Trip must be captured"))
                    .And((capturedTrip?.Lifecycle == TripLifecycle.Registered)
                        .Label($"Lifecycle must be Registered, got {capturedTrip?.Lifecycle}"));
            });
    }

    // ================================================================
    // Property 4b: Command without plan (null/empty ReferenceTripNo) → TempRegistered
    // **Validates: Requirements 4.1**
    // ================================================================

    [Property(MaxTest = 100)]
    public Property No_plan_command_produces_temp_registered_lifecycle()
    {
        var profile = ValidMaterialProfile();
        var (handler, tripsRepo) = CreateHandler(profile);

        Trip? capturedTrip = null;
        tripsRepo.AddAsync(Arg.Do<Trip>(t => capturedTrip = t), Arg.Any<CancellationToken>());

        return Prop.ForAll(
            NullOrEmptyReferenceArb(),
            (string? referenceTripNo) =>
            {
                capturedTrip = null;

                var cmd = new RegisterTripCommand(
                    SiteId: SiteId,
                    GateId: GateId,
                    WorkflowProfileId: profile.Id,
                    WorkflowProfileVersion: profile.ProfileVersion,
                    ReferenceTripNo: referenceTripNo,
                    PlanSource: null,
                    CommodityType: null,
                    OwnerOrCustomer: null,
                    WarehouseCode: null,
                    TractorPlate: "51B-999.99",
                    TrailerPlate: null,
                    DriverRef: null,
                    VehicleType: null,
                    SugarcaneType: null,
                    SugarcaneVariety: null,
                    SugarcaneOwner: null,
                    HarvestMethod: null,
                    Station: null,
                    CropSeason: null,
                    CorrelationId: Guid.NewGuid().ToString(),
                    IssueTemporaryQrOnly: false);

                var result = handler.HandleAsync(cmd).GetAwaiter().GetResult();

                return (result.IsSuccess).Label("IsSuccess must be true")
                    .And((capturedTrip is not null).Label("Trip must be captured"))
                    .And((capturedTrip?.Lifecycle == TripLifecycle.TempRegistered)
                        .Label($"Lifecycle must be TempRegistered, got {capturedTrip?.Lifecycle}"));
            });
    }

    // ================================================================
    // Property 4c: Command with IssueTemporaryQrOnly=true (regardless of plan) → TempRegistered
    // **Validates: Requirements 4.1**
    // ================================================================

    [Property(MaxTest = 100)]
    public Property Issue_temp_only_flag_forces_temp_registered_lifecycle()
    {
        var profile = ValidMaterialProfile();
        var (handler, tripsRepo) = CreateHandler(profile);

        Trip? capturedTrip = null;
        tripsRepo.AddAsync(Arg.Do<Trip>(t => capturedTrip = t), Arg.Any<CancellationToken>());

        return Prop.ForAll(
            NonEmptyReferenceArb(),
            (string referenceTripNo) =>
            {
                capturedTrip = null;

                // Has a valid ReferenceTripNo BUT IssueTemporaryQrOnly = true
                var cmd = new RegisterTripCommand(
                    SiteId: SiteId,
                    GateId: GateId,
                    WorkflowProfileId: profile.Id,
                    WorkflowProfileVersion: profile.ProfileVersion,
                    ReferenceTripNo: referenceTripNo,
                    PlanSource: "FRM",
                    CommodityType: "Sugarcane",
                    OwnerOrCustomer: "Owner",
                    WarehouseCode: "KHO-B",
                    TractorPlate: "51C-555.66",
                    TrailerPlate: null,
                    DriverRef: "DRV-002",
                    VehicleType: null,
                    SugarcaneType: null,
                    SugarcaneVariety: null,
                    SugarcaneOwner: null,
                    HarvestMethod: null,
                    Station: null,
                    CropSeason: null,
                    CorrelationId: Guid.NewGuid().ToString(),
                    IssueTemporaryQrOnly: true);

                var result = handler.HandleAsync(cmd).GetAwaiter().GetResult();

                return (result.IsSuccess).Label("IsSuccess must be true")
                    .And((capturedTrip is not null).Label("Trip must be captured"))
                    .And((capturedTrip?.Lifecycle == TripLifecycle.TempRegistered)
                        .Label($"Lifecycle must be TempRegistered when IssueTemporaryQrOnly=true, got {capturedTrip?.Lifecycle}"));
            });
    }
}
