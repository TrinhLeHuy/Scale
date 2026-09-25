using NSubstitute;
using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.UseCases.Trips;
using TTC.Weighing.Domain.Common;
using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Application.Tests;

public class RegisterTripHandlerTests
{
    // --- Shared dependencies (all mocked) ---
    private readonly ITripRepository _trips = Substitute.For<ITripRepository>();
    private readonly IQrTokenRepository _qrTokens = Substitute.For<IQrTokenRepository>();
    private readonly IWorkflowProfileRepository _profiles = Substitute.For<IWorkflowProfileRepository>();
    private readonly ITripNumberGenerator _tripNumbers = Substitute.For<ITripNumberGenerator>();
    private readonly IQueueSequenceGenerator _queueSequence = Substitute.For<IQueueSequenceGenerator>();
    private readonly IWeighbridgeRepository _weighbridges = Substitute.For<IWeighbridgeRepository>();
    private readonly ICameraService _camera = Substitute.For<ICameraService>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly IAuditLogger _audit = Substitute.For<IAuditLogger>();

    private readonly DateTimeOffset _now = new(2025, 1, 15, 8, 0, 0, TimeSpan.Zero);
    private readonly Guid _siteId = Guid.NewGuid();
    private readonly Guid _gateId = Guid.NewGuid();

    public RegisterTripHandlerTests()
    {
        _clock.UtcNow.Returns(_now);
        _user.UserId.Returns("gate-operator-01");
        _user.HasPermission(RegisterTripHandler.RequiredPermission, Arg.Any<Guid>()).Returns(true);
        _tripNumbers.NextAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("TT-20250115-001");
    }

    /// <summary>Hồ sơ mía nhập — đã duyệt, hiệu lực.</summary>
    private WorkflowProfile InboundSugarcaneProfile() => new()
    {
        Code = "CANE_IN_01",
        Name = "Mía nhập chuẩn",
        ProfileVersion = 1,
        SourceSystem = "FRM",
        Direction = ProfileDirection.Inbound,
        ApprovedBy = "admin",
        ApprovedAt = _now.AddDays(-30),
        EffectiveFrom = _now.AddDays(-60),
        EffectiveTo = _now.AddDays(60)
    };

    /// <summary>Hồ sơ vật tư nhập — không phải mía.</summary>
    private WorkflowProfile MaterialInboundProfile() => new()
    {
        Code = "MATERIAL_IN_01",
        Name = "Vật tư nhập",
        ProfileVersion = 1,
        SourceSystem = "ERP",
        Direction = ProfileDirection.Inbound,
        ApprovedBy = "admin",
        ApprovedAt = _now.AddDays(-30),
        EffectiveFrom = _now.AddDays(-60),
        EffectiveTo = _now.AddDays(60)
    };

    private RegisterTripCommand PlannedCommand(WorkflowProfile profile, VehicleType? vehicleType = VehicleType.Truck) => new(
        SiteId: _siteId,
        GateId: _gateId,
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
        VehicleType: vehicleType,
        SugarcaneType: "Mía tím",
        SugarcaneVariety: "ROC16",
        SugarcaneOwner: "Nguyen Van A",
        HarvestMethod: "Máy",
        Station: "Trạm 3",
        CropSeason: "2024-2025",
        CorrelationId: Guid.NewGuid().ToString());

    private RegisterTripCommand TempCommand(WorkflowProfile profile) => new(
        SiteId: _siteId,
        GateId: _gateId,
        WorkflowProfileId: profile.Id,
        WorkflowProfileVersion: profile.ProfileVersion,
        ReferenceTripNo: null,
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
        IssueTemporaryQrOnly: true);

    private RegisterTripHandler CreateHandler()
    {
        // QueueTripHandler needs real instances of its deps (both mocked)
        var queueTrip = new QueueTripHandler(_weighbridges, _trips);
        return new RegisterTripHandler(
            _trips, _qrTokens, _profiles, _tripNumbers,
            _queueSequence, queueTrip, _weighbridges, _camera,
            _uow, _clock, _user, _audit);
    }

    private void SetupProfile(WorkflowProfile profile)
    {
        _profiles.GetEffectiveAsync(profile.Id, profile.ProfileVersion, Arg.Any<CancellationToken>())
            .Returns(profile);
    }

    private void SetupWeighbridge(Guid? bridgeId = null, string code = "BC-01")
    {
        var id = bridgeId ?? Guid.NewGuid();
        var bridge = new Weighbridge
        {
            Code = code,
            SiteId = _siteId,
            IsOutOfService = false,
            MinCapacityKg = 100,
            MaxCapacityKg = 80000,
            ResolutionKg = 10
        };
        // Set Id via reflection since it's protected set
        typeof(AuditableEntity).GetProperty(nameof(AuditableEntity.Id))!
            .SetValue(bridge, id);

        _weighbridges.ListBySiteAsync(_siteId, Arg.Any<CancellationToken>())
            .Returns([bridge]);
        _weighbridges.GetByIdAsync(id, Arg.Any<CancellationToken>())
            .Returns(bridge);
        _trips.CountPendingByWeighbridgeAsync(_siteId, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, int>());
        // QueueTripHandler calls GetByIdAsync on the trip — capture the added trip
        _trips.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                // Return a minimal trip so QueueTripHandler can set fields
                return new Trip { SiteId = _siteId };
            });
    }

    // ============================================================
    // Test 1: Luồng có kế hoạch — Registered, xếp hàng, QR_chuyến
    // Validates: Requirements 2.1, 3.1
    // ============================================================

    [Fact]
    public async Task Planned_flow_creates_registered_trip_with_queue_and_trip_qr()
    {
        // Arrange
        var profile = InboundSugarcaneProfile();
        SetupProfile(profile);
        SetupWeighbridge();
        _queueSequence.NextAsync(_siteId, VehicleType.Truck, Arg.Any<CancellationToken>())
            .Returns(42);

        var handler = CreateHandler();
        var cmd = PlannedCommand(profile);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        Assert.True(result.IsSuccess, result.FailureReason);
        Assert.NotNull(result.TripId);
        Assert.Equal("TT-20250115-001", result.TripNo);
        Assert.Equal(42, result.QueueSequenceNumber);
        Assert.Equal(QrTokenType.Trip, result.QrType);
        Assert.NotNull(result.QrToken);
        Assert.NotNull(result.AssignedWeighbridgeId);

        // Verify IQueueSequenceGenerator was called
        await _queueSequence.Received(1).NextAsync(_siteId, VehicleType.Truck, Arg.Any<CancellationToken>());

        // Verify trip was added to repository
        await _trips.Received(1).AddAsync(Arg.Is<Trip>(t =>
            t.Lifecycle == TripLifecycle.Registered &&
            t.QueueSequenceNumber == 42 &&
            t.VehicleType == VehicleType.Truck),
            Arg.Any<CancellationToken>());

        // Verify QR token was persisted
        await _qrTokens.Received(1).AddAsync(Arg.Is<QrToken>(q =>
            q.Type == QrTokenType.Trip &&
            q.EncodedContent != null),
            Arg.Any<CancellationToken>());

        // Verify SaveChanges was called
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        // Verify audit log was called
        await _audit.Received(1).LogAsync(
            Arg.Is("TRIP_REGISTERED"),
            Arg.Any<Guid?>(),
            Arg.Is("gate-operator-01"),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Is(cmd.CorrelationId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Planned_flow_encodes_qr_with_queue_sequence_and_weighbridge()
    {
        // Arrange
        var profile = InboundSugarcaneProfile();
        SetupProfile(profile);
        SetupWeighbridge(code: "BC-02");
        _queueSequence.NextAsync(_siteId, VehicleType.Truck, Arg.Any<CancellationToken>())
            .Returns(7);

        // Capture the token before handler runs
        QrToken? capturedToken = null;
        _ = _qrTokens.AddAsync(Arg.Do<QrToken>(t => capturedToken = t), Arg.Any<CancellationToken>());

        var handler = CreateHandler();
        var cmd = PlannedCommand(profile);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert — verify QR encoded content round-trips correctly
        Assert.NotNull(capturedToken);
        Assert.NotNull(capturedToken!.EncodedContent);

        var decoded = QrTokenFactory.Decode(capturedToken.EncodedContent!);
        Assert.NotNull(decoded);
        Assert.Equal(7, decoded!.QueueSequenceNumber);
        Assert.Equal("BC-02", decoded.WeighbridgeCode);
    }

    // ============================================================
    // Test 2: Luồng QR tạm — TempRegistered, QR_tạm chỉ có token prefix
    // Validates: Requirements 4.1
    // ============================================================

    [Fact]
    public async Task Temp_flow_creates_temp_registered_trip_with_temp_qr()
    {
        // Arrange
        var profile = MaterialInboundProfile();
        SetupProfile(profile);

        // Capture the token before handler runs
        QrToken? capturedToken = null;
        _ = _qrTokens.AddAsync(Arg.Do<QrToken>(t => capturedToken = t), Arg.Any<CancellationToken>());

        var handler = CreateHandler();
        var cmd = TempCommand(profile);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        Assert.True(result.IsSuccess, result.FailureReason);
        Assert.NotNull(result.TripId);
        Assert.Equal(QrTokenType.Temp, result.QrType);
        Assert.Null(result.QueueSequenceNumber);
        Assert.Null(result.AssignedWeighbridgeId);
        Assert.NotNull(result.QrToken);

        // Verify Trip lifecycle
        await _trips.Received(1).AddAsync(Arg.Is<Trip>(t =>
            t.Lifecycle == TripLifecycle.TempRegistered),
            Arg.Any<CancellationToken>());

        // Verify QR_tạm encoded content only has token prefix
        Assert.NotNull(capturedToken);
        Assert.Equal(QrTokenType.Temp, capturedToken!.Type);
        Assert.NotNull(capturedToken.EncodedContent);

        var decoded = QrTokenFactory.Decode(capturedToken.EncodedContent!);
        Assert.NotNull(decoded);
        Assert.NotNull(decoded!.TokenPrefix);
        Assert.Null(decoded.QueueSequenceNumber);
        Assert.Null(decoded.WeighbridgeCode);
        Assert.Null(decoded.WarehouseCode);

        // IQueueSequenceGenerator should NOT be called for temp flow
        await _queueSequence.DidNotReceive().NextAsync(Arg.Any<Guid>(), Arg.Any<VehicleType>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_reference_trip_creates_temp_registered_even_without_flag()
    {
        // Arrange — command with no ReferenceTripNo and IssueTemporaryQrOnly = false (default)
        var profile = MaterialInboundProfile();
        SetupProfile(profile);

        var cmd = new RegisterTripCommand(
            SiteId: _siteId,
            GateId: _gateId,
            WorkflowProfileId: profile.Id,
            WorkflowProfileVersion: profile.ProfileVersion,
            ReferenceTripNo: null,
            PlanSource: null,
            CommodityType: null,
            OwnerOrCustomer: null,
            WarehouseCode: null,
            TractorPlate: "51C-111.11",
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

        var handler = CreateHandler();

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert — no ReferenceTripNo means temp flow
        Assert.True(result.IsSuccess, result.FailureReason);
        Assert.Equal(QrTokenType.Temp, result.QrType);

        await _trips.Received(1).AddAsync(Arg.Is<Trip>(t =>
            t.Lifecycle == TripLifecycle.TempRegistered),
            Arg.Any<CancellationToken>());
    }

    // ============================================================
    // Test 3: VehicleType null cho mía nhập → từ chối
    // Validates: Requirements 2.1
    // ============================================================

    [Fact]
    public async Task Inbound_sugarcane_without_vehicle_type_is_rejected()
    {
        // Arrange
        var profile = InboundSugarcaneProfile();
        SetupProfile(profile);

        var handler = CreateHandler();
        var cmd = PlannedCommand(profile, vehicleType: null);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("VehicleType", result.FailureReason);

        // Should not persist anything
        await _trips.DidNotReceive().AddAsync(Arg.Any<Trip>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Inbound_sugarcane_by_direction_and_source_system_also_requires_vehicle_type()
    {
        // Arrange — profile Direction=Inbound + SourceSystem=FRM, code NOT starting with CANE_IN
        var profile = new WorkflowProfile
        {
            Code = "SUGAR_INBOUND_X",
            Name = "Profile mía nhập khác",
            ProfileVersion = 1,
            SourceSystem = "FRM",
            Direction = ProfileDirection.Inbound,
            ApprovedBy = "admin",
            ApprovedAt = _now.AddDays(-5),
            EffectiveFrom = _now.AddDays(-30),
            EffectiveTo = _now.AddDays(30)
        };
        SetupProfile(profile);

        var handler = CreateHandler();
        var cmd = PlannedCommand(profile, vehicleType: null);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert — IsInboundSugarcaneProfile matches Direction=Inbound + SourceSystem=FRM
        Assert.False(result.IsSuccess);
        Assert.Contains("VehicleType", result.FailureReason);
    }

    // ============================================================
    // Test 4: VehicleType null cho vật tư → cho phép
    // Validates: Requirements 2.4
    // ============================================================

    [Fact]
    public async Task Material_profile_allows_null_vehicle_type()
    {
        // Arrange
        var profile = MaterialInboundProfile();
        SetupProfile(profile);

        var handler = CreateHandler();
        // Planned command with VehicleType = null on non-sugarcane profile
        var cmd = new RegisterTripCommand(
            SiteId: _siteId,
            GateId: _gateId,
            WorkflowProfileId: profile.Id,
            WorkflowProfileVersion: profile.ProfileVersion,
            ReferenceTripNo: "PO-2025-001",
            PlanSource: "ERP",
            CommodityType: "Cement",
            OwnerOrCustomer: "Company XYZ",
            WarehouseCode: "KHO-B",
            TractorPlate: "51D-777.88",
            TrailerPlate: null,
            DriverRef: "DRV-002",
            VehicleType: null,
            SugarcaneType: null,
            SugarcaneVariety: null,
            SugarcaneOwner: null,
            HarvestMethod: null,
            Station: null,
            CropSeason: null,
            CorrelationId: Guid.NewGuid().ToString());

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert — material profile, VehicleType null is OK
        // Since VehicleType is null, the queue+weighbridge assignment is skipped
        Assert.True(result.IsSuccess, result.FailureReason);
        Assert.Null(result.QueueSequenceNumber);
    }

    // ============================================================
    // Test 5: Profile chưa duyệt → từ chối
    // Validates: Requirements 2.1
    // ============================================================

    [Fact]
    public async Task Unapproved_profile_is_rejected()
    {
        // Arrange
        var profile = new WorkflowProfile
        {
            Code = "CANE_IN_TEST",
            Name = "Profile chưa duyệt",
            ProfileVersion = 1,
            SourceSystem = "FRM",
            Direction = ProfileDirection.Inbound,
            ApprovedBy = null,          // chưa duyệt
            ApprovedAt = null,
            EffectiveFrom = _now.AddDays(-30),
            EffectiveTo = _now.AddDays(30)
        };
        SetupProfile(profile);

        var handler = CreateHandler();
        var cmd = PlannedCommand(profile);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("chưa được duyệt", result.FailureReason);
    }

    // ============================================================
    // Test 6: Profile hết hiệu lực → từ chối
    // Validates: Requirements 2.1
    // ============================================================

    [Fact]
    public async Task Expired_profile_is_rejected()
    {
        // Arrange — EffectiveTo in the past
        var profile = new WorkflowProfile
        {
            Code = "CANE_IN_EXPIRED",
            Name = "Profile hết hạn",
            ProfileVersion = 1,
            SourceSystem = "FRM",
            Direction = ProfileDirection.Inbound,
            ApprovedBy = "admin",
            ApprovedAt = _now.AddDays(-90),
            EffectiveFrom = _now.AddDays(-120),
            EffectiveTo = _now.AddDays(-1)  // hết hiệu lực hôm qua
        };
        SetupProfile(profile);

        var handler = CreateHandler();
        var cmd = PlannedCommand(profile);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("hiệu lực", result.FailureReason);
    }

    [Fact]
    public async Task Profile_not_yet_effective_is_rejected()
    {
        // Arrange — EffectiveFrom in the future
        var profile = new WorkflowProfile
        {
            Code = "CANE_IN_FUTURE",
            Name = "Profile chưa đến hiệu lực",
            ProfileVersion = 1,
            SourceSystem = "FRM",
            Direction = ProfileDirection.Inbound,
            ApprovedBy = "admin",
            ApprovedAt = _now.AddDays(-5),
            EffectiveFrom = _now.AddDays(10),  // chưa đến
            EffectiveTo = _now.AddDays(60)
        };
        SetupProfile(profile);

        var handler = CreateHandler();
        var cmd = PlannedCommand(profile);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("hiệu lực", result.FailureReason);
    }

    // ============================================================
    // Test 7: Profile không tìm thấy → từ chối
    // ============================================================

    [Fact]
    public async Task Missing_profile_is_rejected()
    {
        // Arrange — profile not found
        _profiles.GetEffectiveAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((WorkflowProfile?)null);

        var handler = CreateHandler();
        var profile = InboundSugarcaneProfile();
        var cmd = PlannedCommand(profile);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("hồ sơ nghiệp vụ", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    // ============================================================
    // Test 8: Không có quyền → từ chối
    // ============================================================

    [Fact]
    public async Task Missing_permission_is_rejected()
    {
        // Arrange
        _user.HasPermission(RegisterTripHandler.RequiredPermission, Arg.Any<Guid>()).Returns(false);

        var handler = CreateHandler();
        var profile = InboundSugarcaneProfile();
        SetupProfile(profile);
        var cmd = PlannedCommand(profile);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("quyền", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }
}
