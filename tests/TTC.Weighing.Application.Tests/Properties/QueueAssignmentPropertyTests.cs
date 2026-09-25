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
/// Property 5: Queue assignment
/// **Validates: Requirements 3.2, 9.1, 9.2**
///
/// Cho mọi trip đăng tài thành công với Lifecycle = Registered và VehicleType != null,
/// nếu tồn tại ít nhất một bàn cân khả dụng (IsOutOfService = false) tại site
/// thì AssignedWeighbridgeId phải khác null và QueueSequenceNumber phải > 0.
/// </summary>
public class QueueAssignmentPropertyTests
{
    private static readonly DateTimeOffset Now = new(2025, 1, 15, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Test input combining all random inputs for the property.
    /// </summary>
    public sealed record QueueAssignmentInput(
        VehicleType VehicleType,
        int BridgeCount,
        int QueueSeq);

    /// <summary>
    /// Custom Arbitrary provider for FsCheck — must be public static class with public static members.
    /// </summary>
    public static class QueueAssignmentArbs
    {
        public static Arbitrary<QueueAssignmentInput> QueueAssignmentInputArb()
        {
            var gen = from vt in Gen.Elements(VehicleType.Container, VehicleType.Truck, VehicleType.Trailer)
                      from bridgeCount in Gen.Choose(1, 3)
                      from seq in Gen.Choose(1, 10000)
                      select new QueueAssignmentInput(vt, bridgeCount, seq);
            return gen.ToArbitrary();
        }
    }

    private static List<Weighbridge> CreateBridges(int count, Guid siteId)
    {
        return Enumerable.Range(1, count).Select(i =>
        {
            var bridge = new Weighbridge
            {
                SiteId = siteId,
                Code = $"BC-{i:D2}",
                IsOutOfService = false,
                MinCapacityKg = 100,
                MaxCapacityKg = 80000,
                ResolutionKg = 10
            };
            typeof(AuditableEntity).GetProperty(nameof(AuditableEntity.Id))!
                .SetValue(bridge, Guid.NewGuid());
            return bridge;
        }).ToList();
    }

    /// <summary>
    /// Property 5: For any RegisterTripCommand where:
    /// - Profile is valid inbound sugarcane
    /// - ReferenceTripNo is non-empty (planned flow)
    /// - VehicleType is one of {Container, Truck, Trailer}
    /// - At least one weighbridge is available (not out of service)
    /// Then the result should have:
    /// - IsSuccess = true
    /// - QueueSequenceNumber > 0
    /// - AssignedWeighbridgeId != null
    ///
    /// **Validates: Requirements 3.2, 9.1, 9.2**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(QueueAssignmentArbs) })]
    public bool Registered_trip_with_available_bridges_gets_queue_assignment(
        QueueAssignmentInput input)
    {
        // --- Arrange ---
        var siteId = Guid.NewGuid();
        var gateId = Guid.NewGuid();
        var bridges = CreateBridges(input.BridgeCount, siteId);

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
        user.UserId.Returns("gate-operator");
        user.HasPermission(RegisterTripHandler.RequiredPermission, Arg.Any<Guid>()).Returns(true);
        tripNumbers.NextAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("TT-20250115-001");

        // Profile: valid inbound sugarcane
        var profile = new WorkflowProfile
        {
            Code = "CANE_IN_01",
            Name = "Mía nhập chuẩn",
            ProfileVersion = 1,
            SourceSystem = "FRM",
            Direction = ProfileDirection.Inbound,
            ApprovedBy = "admin",
            ApprovedAt = Now.AddDays(-30),
            EffectiveFrom = Now.AddDays(-60),
            EffectiveTo = Now.AddDays(60)
        };
        profiles.GetEffectiveAsync(profile.Id, profile.ProfileVersion, Arg.Any<CancellationToken>())
            .Returns(profile);

        // Queue sequence generator returns the generated positive int
        queueSequence.NextAsync(siteId, input.VehicleType, Arg.Any<CancellationToken>())
            .Returns(input.QueueSeq);

        // Weighbridges: return generated available bridges
        weighbridges.ListBySiteAsync(siteId, Arg.Any<CancellationToken>())
            .Returns(bridges.AsReadOnly());
        foreach (var b in bridges)
        {
            weighbridges.GetByIdAsync(b.Id, Arg.Any<CancellationToken>())
                .Returns(b);
        }

        // No pending trips at any weighbridge
        trips.CountPendingByWeighbridgeAsync(siteId, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, int>());

        // QueueTripHandler calls GetByIdAsync for the trip
        trips.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ci => new Trip { SiteId = siteId });

        var queueTrip = new QueueTripHandler(weighbridges, trips);
        var handler = new RegisterTripHandler(
            trips, qrTokens, profiles, tripNumbers,
            queueSequence, queueTrip, weighbridges, camera,
            uow, clock, user, audit);

        // Command: planned flow with VehicleType
        var cmd = new RegisterTripCommand(
            SiteId: siteId,
            GateId: gateId,
            WorkflowProfileId: profile.Id,
            WorkflowProfileVersion: profile.ProfileVersion,
            ReferenceTripNo: "FRM-2025-100",
            PlanSource: "FRM",
            CommodityType: "Sugarcane",
            OwnerOrCustomer: "Nguyen Van A",
            WarehouseCode: "KHO-A",
            TractorPlate: "51A-123.45",
            TrailerPlate: null,
            DriverRef: "DRV-001",
            VehicleType: input.VehicleType,
            SugarcaneType: "Mía tím",
            SugarcaneVariety: "ROC16",
            SugarcaneOwner: "Nguyen Van A",
            HarvestMethod: "Máy",
            Station: "Trạm 3",
            CropSeason: "2024-2025",
            CorrelationId: Guid.NewGuid().ToString());

        // --- Act ---
        var result = handler.HandleAsync(cmd).GetAwaiter().GetResult();

        // --- Assert: three invariants ---
        var isSuccess = result.IsSuccess;
        var hasAssignedBridge = result.AssignedWeighbridgeId is not null;
        var hasPositiveQueueSeq = result.QueueSequenceNumber is not null
                                  && result.QueueSequenceNumber > 0;
        var bridgeIsFromAvailable = bridges.Any(b => b.Id == result.AssignedWeighbridgeId);

        return isSuccess && hasAssignedBridge && hasPositiveQueueSeq && bridgeIsFromAvailable;
    }
}
