using NSubstitute;
using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.UseCases.Trips;
using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Application.Tests;

public class QueueTripHandlerTests
{
    private readonly IWeighbridgeRepository _weighbridges = Substitute.For<IWeighbridgeRepository>();
    private readonly ITripRepository _trips = Substitute.For<ITripRepository>();
    private readonly QueueTripHandler _sut;

    private static readonly Guid SiteId = Guid.NewGuid();
    private static readonly Guid TripId = Guid.NewGuid();

    public QueueTripHandlerTests()
    {
        _sut = new QueueTripHandler(_weighbridges, _trips);
    }

    private static QueueTripCommand DefaultCommand() => new(
        TripId: TripId,
        SiteId: SiteId,
        VehicleType: VehicleType.Truck,
        PreferredWarehouseCode: null);

    private static Weighbridge MakeBridge(string code, bool outOfService = false) => new()
    {
        SiteId = SiteId,
        Code = code,
        IsOutOfService = outOfService,
        MinCapacityKg = 1000m,
        MaxCapacityKg = 80_000m,
        ResolutionKg = 10m
    };

    private static Trip MakeTrip()
    {
        var trip = new Trip
        {
            SiteId = SiteId,
            TripNo = "T-001",
            Lifecycle = TripLifecycle.Registered
        };
        return trip;
    }

    /// <summary>
    /// Req 9.2 — Gán bàn cân có ít chuyến chờ nhất (load balancing).
    /// Bridge BC-01 có 3 chuyến chờ, BC-02 có 1 → chọn BC-02.
    /// </summary>
    [Fact]
    public async Task HandleAsync_AssignsBridgeWithFewestPendingTrips()
    {
        // Arrange
        var bridge1 = MakeBridge("BC-01");
        var bridge2 = MakeBridge("BC-02");

        _weighbridges.ListBySiteAsync(SiteId, Arg.Any<CancellationToken>())
            .Returns([bridge1, bridge2]);

        _trips.CountPendingByWeighbridgeAsync(SiteId, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, int>
            {
                [bridge1.Id] = 3,
                [bridge2.Id] = 1
            });

        _trips.GetByIdAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(MakeTrip());

        // Act
        var result = await _sut.HandleAsync(DefaultCommand());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(bridge2.Id, result.AssignedWeighbridgeId);
    }

    /// <summary>
    /// Req 9.2 — Khi hai bàn cân cùng số chuyến chờ, tie-break theo Code (deterministic).
    /// </summary>
    [Fact]
    public async Task HandleAsync_TieBreaksByCodeWhenSamePendingCount()
    {
        // Arrange
        var bridgeB = MakeBridge("BC-02");
        var bridgeA = MakeBridge("BC-01");

        _weighbridges.ListBySiteAsync(SiteId, Arg.Any<CancellationToken>())
            .Returns([bridgeB, bridgeA]);

        _trips.CountPendingByWeighbridgeAsync(SiteId, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, int>
            {
                [bridgeA.Id] = 2,
                [bridgeB.Id] = 2
            });

        _trips.GetByIdAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(MakeTrip());

        // Act
        var result = await _sut.HandleAsync(DefaultCommand());

        // Assert — BC-01 đứng trước BC-02 theo Code
        Assert.True(result.IsSuccess);
        Assert.Equal(bridgeA.Id, result.AssignedWeighbridgeId);
    }

    /// <summary>
    /// Req 9.2 — Bàn cân chưa có chuyến chờ nào (không có trong dictionary) được tính 0 → ưu tiên nhất.
    /// </summary>
    [Fact]
    public async Task HandleAsync_BridgeNotInPendingCountsTreatedAsZero()
    {
        // Arrange
        var busyBridge = MakeBridge("BC-01");
        var emptyBridge = MakeBridge("BC-02");

        _weighbridges.ListBySiteAsync(SiteId, Arg.Any<CancellationToken>())
            .Returns([busyBridge, emptyBridge]);

        // emptyBridge không có trong dictionary → count = 0
        _trips.CountPendingByWeighbridgeAsync(SiteId, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, int>
            {
                [busyBridge.Id] = 5
            });

        _trips.GetByIdAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(MakeTrip());

        // Act
        var result = await _sut.HandleAsync(DefaultCommand());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(emptyBridge.Id, result.AssignedWeighbridgeId);
    }

    /// <summary>
    /// Req 9.1 — Bàn cân IsOutOfService bị loại, chỉ chọn bàn khả dụng.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ExcludesOutOfServiceBridges()
    {
        // Arrange
        var oosbridge = MakeBridge("BC-01", outOfService: true);
        var activeBridge = MakeBridge("BC-02");

        _weighbridges.ListBySiteAsync(SiteId, Arg.Any<CancellationToken>())
            .Returns([oosbridge, activeBridge]);

        _trips.CountPendingByWeighbridgeAsync(SiteId, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, int>());

        _trips.GetByIdAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(MakeTrip());

        // Act
        var result = await _sut.HandleAsync(DefaultCommand());

        // Assert — chỉ gán bàn đang hoạt động
        Assert.True(result.IsSuccess);
        Assert.Equal(activeBridge.Id, result.AssignedWeighbridgeId);
    }

    /// <summary>
    /// Req 9.5 — Không có bàn cân khả dụng → vẫn thành công, AssignedWeighbridgeId = null.
    /// </summary>
    [Fact]
    public async Task HandleAsync_NoBridgesAvailable_SucceedsWithNullAssignment()
    {
        // Arrange — tất cả bàn đều ngừng hoạt động
        var bridge1 = MakeBridge("BC-01", outOfService: true);
        var bridge2 = MakeBridge("BC-02", outOfService: true);

        _weighbridges.ListBySiteAsync(SiteId, Arg.Any<CancellationToken>())
            .Returns([bridge1, bridge2]);

        _trips.GetByIdAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(MakeTrip());

        // Act
        var result = await _sut.HandleAsync(DefaultCommand());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.AssignedWeighbridgeId);
    }

    /// <summary>
    /// Req 9.5 — Site không có bàn cân nào → vẫn thành công, AssignedWeighbridgeId = null.
    /// </summary>
    [Fact]
    public async Task HandleAsync_EmptyBridgeList_SucceedsWithNullAssignment()
    {
        // Arrange
        _weighbridges.ListBySiteAsync(SiteId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Weighbridge>());

        _trips.GetByIdAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(MakeTrip());

        // Act
        var result = await _sut.HandleAsync(DefaultCommand());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.AssignedWeighbridgeId);
    }

    /// <summary>
    /// Req 9.5 — Kho đích hiện tại chưa có resolver, AssignedWarehouseId luôn null.
    /// Khi PreferredWarehouseCode được truyền, handler vẫn trả null vì chưa có Warehouse entity.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WarehouseAssignment_IsNull_WhenNoResolver()
    {
        // Arrange
        var bridge = MakeBridge("BC-01");
        _weighbridges.ListBySiteAsync(SiteId, Arg.Any<CancellationToken>())
            .Returns([bridge]);

        _trips.CountPendingByWeighbridgeAsync(SiteId, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, int>());

        _trips.GetByIdAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(MakeTrip());

        var cmd = DefaultCommand() with { PreferredWarehouseCode = "KHO-A" };

        // Act
        var result = await _sut.HandleAsync(cmd);

        // Assert — kho null vì chưa có warehouse resolver
        Assert.True(result.IsSuccess);
        Assert.Null(result.AssignedWarehouseId);
    }

    /// <summary>
    /// Handler gán AssignedWeighbridgeId và AssignedWarehouseId trực tiếp lên Trip entity.
    /// </summary>
    [Fact]
    public async Task HandleAsync_UpdatesTripEntityWithAssignedBridge()
    {
        // Arrange
        var bridge = MakeBridge("BC-01");
        var trip = MakeTrip();

        _weighbridges.ListBySiteAsync(SiteId, Arg.Any<CancellationToken>())
            .Returns([bridge]);

        _trips.CountPendingByWeighbridgeAsync(SiteId, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, int>());

        _trips.GetByIdAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(trip);

        // Act
        await _sut.HandleAsync(DefaultCommand());

        // Assert — Trip entity được cập nhật trực tiếp
        Assert.Equal(bridge.Id, trip.AssignedWeighbridgeId);
        Assert.Null(trip.AssignedWarehouseId);
    }

    /// <summary>
    /// Trip không tìm thấy → trả IsSuccess = false.
    /// </summary>
    [Fact]
    public async Task HandleAsync_TripNotFound_ReturnsFailure()
    {
        // Arrange
        var bridge = MakeBridge("BC-01");
        _weighbridges.ListBySiteAsync(SiteId, Arg.Any<CancellationToken>())
            .Returns([bridge]);

        _trips.CountPendingByWeighbridgeAsync(SiteId, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, int>());

        _trips.GetByIdAsync(TripId, Arg.Any<CancellationToken>())
            .Returns((Trip?)null);

        // Act
        var result = await _sut.HandleAsync(DefaultCommand());

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Null(result.AssignedWeighbridgeId);
        Assert.NotNull(result.FailureReason);
    }
}
