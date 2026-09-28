using NSubstitute;
using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.Queries;
using TTC.Weighing.Application.UseCases.Integration;
using TTC.Weighing.Application.UseCases.Weighing;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.WinForms.Forms;
using TTC.Weighing.WinForms.Presenters;

namespace TTC.Weighing.Application.Tests;

/// <summary>
/// Unit tests cho WeighOperationPresenter — thin delegation tests.
/// Kiểm tra presenter chuyển tiếp đúng tham số đến các service đã inject.
/// Requirements: 1.1, 4.1, 5.1, 11.1
/// </summary>
public class WeighOperationPresenterTests
{
    private static readonly Guid SiteId = Guid.NewGuid();
    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly Guid WeighbridgeId = Guid.NewGuid();

    // ── Mocks ──────────────────────────────────────────────

    private readonly OpenWeighSessionHandler _openSession;
    private readonly EvaluateAndCommitWeightHandler _commitWeight;
    private readonly IScaleSignalGateway _scaleGateway = Substitute.For<IScaleSignalGateway>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IQrScanHandler _qrScanHandler = Substitute.For<IQrScanHandler>();
    private readonly IFrmSyncService _frmSyncService = Substitute.For<IFrmSyncService>();
    private readonly IWeighTicketPrintService _printService = Substitute.For<IWeighTicketPrintService>();
    private readonly SubmitTripResultHandler _submitTripResult;
    private readonly IWeighPreconditionQuery _preconditionQuery = Substitute.For<IWeighPreconditionQuery>();
    private readonly IDeviceStatusMonitor _deviceStatusMonitor = Substitute.For<IDeviceStatusMonitor>();
    private readonly ApproveManualWeightHandler _manualWeightHandler;
    private readonly RequestReweighHandler _reweighHandler;
    private readonly IWeighOperationQueries _weighOperationQueries = Substitute.For<IWeighOperationQueries>();

    private readonly WeighOperationPresenter _sut;

    public WeighOperationPresenterTests()
    {
        // Handlers are concrete classes — create via NSubstitute.Substitute.For to mock their virtual methods.
        // Since they use primary constructors and HandleAsync is NOT virtual, we create real instances
        // with all-substitute dependencies. The tests mock the underlying services instead.
        // However, for thin delegation tests we only care that presenter forwards to the handler/service.
        // We use NSubstitute.ForPartsOf or create with substituted dependencies.

        // For handlers (concrete classes), create mock instances via Substitute.For<T>
        // which creates a proxy subclass. HandleAsync is not virtual, so we use a different approach:
        // We create real handler instances with mocked dependencies, but we don't need them to do anything
        // because the delegation tests only verify calls reach the injected services (IQrScanHandler, etc.).
        // For the 4 delegation tests in scope, we only test methods that delegate to interfaces, not handlers.

        // Create minimal handler stubs with all-substitute dependencies
        _openSession = new OpenWeighSessionHandler(
            Substitute.For<ITripRepository>(),
            Substitute.For<IWeighSessionRepository>(),
            Substitute.For<IWorkflowProfileRepository>(),
            Substitute.For<IWeighbridgeRepository>(),
            Substitute.For<IUnitOfWork>(),
            Substitute.For<IClock>(),
            Substitute.For<ICurrentUser>(),
            Substitute.For<IAuditLogger>());

        _commitWeight = new EvaluateAndCommitWeightHandler(
            Substitute.For<IWeighSessionRepository>(),
            Substitute.For<ITripRepository>(),
            Substitute.For<IWorkflowProfileRepository>(),
            Substitute.For<IWeighbridgeRepository>(),
            Substitute.For<IScaleSignalGateway>(),
            Substitute.For<IPositionEvidenceGateway>(),
            Substitute.For<IWeighPreconditionQuery>(),
            Substitute.For<IVehicleIdentityVerifier>(),
            Substitute.For<IIntegrationStateQuery>(),
            Substitute.For<IEvidenceStore>(),
            Substitute.For<IUnitOfWork>(),
            Substitute.For<IClock>(),
            Substitute.For<ICurrentUser>(),
            Substitute.For<IAuditLogger>());

        _submitTripResult = new SubmitTripResultHandler(
            Substitute.For<ITripRepository>(),
            Substitute.For<IReconciliationRepository>(),
            Substitute.For<IAllocationRepository>(),
            Substitute.For<IIntegrationMessageRepository>(),
            Substitute.For<IIntegrationPayloadBuilder>(),
            Substitute.For<IUnitOfWork>(),
            Substitute.For<IClock>(),
            Substitute.For<ICurrentUser>(),
            Substitute.For<IAuditLogger>());

        _manualWeightHandler = new ApproveManualWeightHandler(
            Substitute.For<ITripRepository>(),
            Substitute.For<IWeighSessionRepository>(),
            Substitute.For<IWeighbridgeRepository>(),
            Substitute.For<IWorkflowProfileRepository>(),
            Substitute.For<IApprovalRepository>(),
            Substitute.For<IIntegrationStateQuery>(),
            Substitute.For<IUnitOfWork>(),
            Substitute.For<IClock>(),
            Substitute.For<ICurrentUser>(),
            Substitute.For<IAuditLogger>());

        _reweighHandler = new RequestReweighHandler(
            Substitute.For<ITripRepository>(),
            Substitute.For<IWeighSessionRepository>(),
            Substitute.For<IIntegrationMessageRepository>(),
            Substitute.For<IApprovalRepository>(),
            Substitute.For<IUnitOfWork>(),
            Substitute.For<IClock>(),
            Substitute.For<ICurrentUser>(),
            Substitute.For<IAuditLogger>());

        _sut = new WeighOperationPresenter(
            _openSession,
            _commitWeight,
            _scaleGateway,
            _clock,
            _qrScanHandler,
            _frmSyncService,
            _printService,
            _submitTripResult,
            _preconditionQuery,
            _deviceStatusMonitor,
            _manualWeightHandler,
            _reweighHandler,
            _weighOperationQueries,
            Substitute.For<IWeighSessionRepository>(),
            Substitute.For<IWeighbridgeRepository>(),
            Substitute.For<ISiteContext>(),
            Substitute.For<TTC.Weighing.Infrastructure.Adapters.DigiScaleTicketAdapter>());
    }

    // ──────────────────────────────────────────────────────
    // Req 1.1 — ScanQrAsync delegates đúng đến IQrScanHandler
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task ScanQrAsync_delegates_to_IQrScanHandler_with_correct_parameters()
    {
        var qrContent = "QR-TOKEN-ABC123";
        var expected = new QrScanResult(
            IsSuccess: true,
            TripId: TripId,
            TripVersion: 1,
            TripNo: "TT-001",
            Plate: "51A-12345",
            TrailerPlate: null,
            CustomerOrOwner: "Chủ mía A",
            Commodity: "Mía",
            Warehouse: "Kho 1",
            ShipmentNo: null,
            SugarcaneType: "Tươi",
            SugarcaneVariety: "ROC22",
            SugarcaneOwner: "Chủ mía A",
            AssignedWeighbridgeId: WeighbridgeId,
            AssignedWarehouseId: null,
            QrType: QrTokenType.Trip,
            ErrorCode: null,
            ErrorMessage: null);

        _qrScanHandler
            .DecodeAndLookupAsync(qrContent, SiteId, Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await _sut.ScanQrAsync(qrContent, SiteId);

        Assert.Same(expected, result);
        await _qrScanHandler.Received(1)
            .DecodeAndLookupAsync(qrContent, SiteId, Arg.Any<CancellationToken>());
    }

    // ──────────────────────────────────────────────────────
    // Req 4.1 — SyncFrmAsync delegates đúng đến IFrmSyncService
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task SyncFrmAsync_delegates_to_IFrmSyncService_with_correct_parameters()
    {
        var expected = new FrmSyncResult(
            IsSuccess: true,
            SugarcaneType: "Cháy",
            SugarcaneVariety: "ROC22",
            SugarcaneOwner: "Chủ mía B",
            HarvestMethod: "Cơ giới",
            Station: "Trạm 3",
            CropSeason: "2024-2025",
            ErrorMessage: null);

        _frmSyncService
            .SyncTripInfoAsync(TripId, SiteId, Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await _sut.SyncFrmAsync(TripId, SiteId);

        Assert.Same(expected, result);
        await _frmSyncService.Received(1)
            .SyncTripInfoAsync(TripId, SiteId, Arg.Any<CancellationToken>());
    }

    // ──────────────────────────────────────────────────────
    // Req 5.1 — PrintTicketAsync delegates đúng đến IWeighTicketPrintService
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task PrintTicketAsync_delegates_to_IWeighTicketPrintService_with_correct_parameters()
    {
        var config = new PrinterConfig("HP LaserJet", 2, "MIA_NHAP");
        var expected = new PrintTicketResult(true, "PC-2025-001", null);

        _printService
            .PrintAsync(TripId, config, false, Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await _sut.PrintTicketAsync(TripId, config, false);

        Assert.Same(expected, result);
        await _printService.Received(1)
            .PrintAsync(TripId, config, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PrintTicketAsync_passes_isReprint_flag_correctly()
    {
        var config = new PrinterConfig("HP LaserJet", 1, "MIA_NHAP");
        var expected = new PrintTicketResult(true, "PC-2025-001", null);

        _printService
            .PrintAsync(TripId, config, true, Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await _sut.PrintTicketAsync(TripId, config, true);

        Assert.Same(expected, result);
        await _printService.Received(1)
            .PrintAsync(TripId, config, true, Arg.Any<CancellationToken>());
    }

    // ──────────────────────────────────────────────────────
    // Req 11.1 — ManualWeightAsync delegates đúng đến ApproveManualWeightHandler
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task ManualWeightAsync_delegates_to_ApproveManualWeightHandler_with_correct_parameters()
    {
        var cmd = new ApproveManualWeightCommand(
            TripId: TripId,
            WeighbridgeId: WeighbridgeId,
            Checkpoint: CheckpointType.FirstWeight,
            SegmentIndex: 0,
            Role: WeightRole.Gross,
            RawWeight: 30500m,
            RawUom: "kg",
            Reason: "Đầu cân hỏng, đọc từ cân dự phòng",
            ApprovalRef: Guid.NewGuid().ToString(),
            CorrelationId: "corr-manual-001");

        // The handler will return a failure because trip is null in the mock,
        // but that's fine — we're testing delegation, not handler logic.
        var result = await _sut.ManualWeightAsync(cmd);

        // Verify the presenter forwarded the call (handler returns failure because
        // ITripRepository returns null, which confirms the handler was actually called)
        Assert.NotNull(result);
        Assert.False(result.IsSuccess);
        Assert.Contains("không tồn tại", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    // ──────────────────────────────────────────────────────
    // Bổ sung: PrintService property exposes service (Req 5.1)
    // ──────────────────────────────────────────────────────

    [Fact]
    public void PrintService_property_exposes_injected_service()
    {
        Assert.Same(_printService, _sut.PrintService);
    }
}
