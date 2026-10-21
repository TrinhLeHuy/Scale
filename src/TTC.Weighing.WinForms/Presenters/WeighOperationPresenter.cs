using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.Queries;
using TTC.Weighing.Application.UseCases.Integration;
using TTC.Weighing.Application.UseCases.Trips;
using TTC.Weighing.Application.UseCases.Weighing;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Infrastructure.Adapters;
using TTC.Weighing.WinForms.Forms;

namespace TTC.Weighing.WinForms.Presenters;

/// <summary>Dữ liệu hiển thị số cân, đã tính độ mới tại tầng presenter.</summary>
public sealed record LiveWeightView(
    decimal? NormalizedKg,
    string Uom,
    ScaleDeviceStatus DeviceStatus,
    bool SignalFresh,
    TimeSpan StableDuration);

/// <summary>
/// Presenter cho UI-03. Form không gọi thẳng repository/gateway và không chứa business logic.
/// Mọi quyết định chốt cân do handler ở Application quyết định.
/// </summary>
public sealed class WeighOperationPresenter(
    OpenWeighSessionHandler openSession,
    EvaluateAndCommitWeightHandler commitWeight,
    IScaleSignalGateway scaleGateway,
    IClock clock,
    IQrScanHandler qrScanHandler,
    IFrmSyncService frmSyncService,
    IWeighTicketPrintService printService,
    SubmitTripResultHandler submitTripResult,
    IWeighPreconditionQuery preconditionQuery,
    IDeviceStatusMonitor deviceStatusMonitor,
    ApproveManualWeightHandler manualWeightHandler,
    RequestReweighHandler reweighHandler,
    IWeighOperationQueries weighOperationQueries,
    IWeighSessionRepository weighSessionRepository,
    IWeighbridgeRepository weighbridgeRepository,
    ISiteContext siteContext,
    ITripRegistrationQueries tripRegistrationQueries,
    UpdateSugarcaneTypeHandler updateSugarcaneType,
    TTC.Weighing.Infrastructure.Adapters.FrmMasterDataCache frmCache,
    TTC.Weighing.Infrastructure.Adapters.DigiScaleTicketAdapter digiScaleTickets,
    TTC.Weighing.Infrastructure.Adapters.ErpOrderLineAdapter erpOrderLines,
    IQualitySampleRepository qualitySampleRepository)
{
    /// <summary>Độ mới tối đa dùng riêng cho hiển thị. Không dùng làm điều kiện chốt cân.</summary>
    private static readonly TimeSpan DisplayStaleAfter = TimeSpan.FromSeconds(2);

    /// <summary>
    /// IWeighTicketPrintService cho PrintHelper dùng trực tiếp (Req 5.1).
    /// PrintHelper cần service để hiển thị dialog chọn máy in rồi gọi PrintAsync.
    /// </summary>
    public IWeighTicketPrintService PrintService => printService;

    /// <summary>BU mặc định — từ account login hoặc config.</summary>
    public string DefaultBusinessUnit => frmCache.ActiveBusinessUnit ?? "TTCS";

    // ── Existing methods ──────────────────────────────────────────────

    public Task<OpenWeighSessionResult> OpenSessionAsync(
        OpenWeighSessionCommand cmd, CancellationToken ct = default)
        => openSession.HandleAsync(cmd, ct);

    public Task<EvaluateAndCommitWeightResult> CommitAsync(
        EvaluateAndCommitWeightCommand cmd, CancellationToken ct = default)
        => commitWeight.HandleAsync(cmd, ct);

    public async Task<LiveWeightView> ReadLiveWeightAsync(
        Guid weighbridgeId, CancellationToken ct = default)
    {
        var signal = await scaleGateway.GetCurrentAsync(weighbridgeId, ct);

        if (signal is null)
            return new LiveWeightView(null, "kg", ScaleDeviceStatus.Unknown, false, TimeSpan.Zero);

        var fresh = clock.UtcNow - signal.OccurredAt <= DisplayStaleAfter;

        return new LiveWeightView(
            NormalizedKg: signal.NormalizedKg,
            Uom: signal.RawUom,
            DeviceStatus: signal.DeviceStatus,
            SignalFresh: fresh,
            StableDuration: signal.StableDuration);
    }

    // ── New methods ───────────────────────────────────────────────────

    /// <summary>Quét QR tại bàn cân, decode + validate + lookup trip (Req 1.1).</summary>
    public Task<QrScanResult> ScanQrAsync(
        string qrContent, Guid siteId, CancellationToken ct = default)
        => qrScanHandler.DecodeAndLookupAsync(qrContent, siteId, ct);

    /// <summary>Danh sách chuyến đang trong nhà máy — dùng cho dropdown chọn xe.</summary>
    public Task<IReadOnlyList<TripRegistrationRow>> ListTripsAsync(
        Guid siteId, TripRegistrationFilter filter, CancellationToken ct = default)
        => tripRegistrationQueries.ListAsync(siteId, filter, ct);

    /// <summary>Cập nhật loại mía cho chuyến (sau khi cân lần 1).</summary>
    public Task<UpdateSugarcaneTypeResult> UpdateSugarcaneTypeAsync(
        UpdateSugarcaneTypeCommand cmd, CancellationToken ct = default)
        => updateSugarcaneType.HandleAsync(cmd, ct);

    /// <summary>Đồng bộ thông tin chuyến từ FRM tại thời điểm quét QR cân lần 2 (Req 4.1).</summary>
    public Task<FrmSyncResult> SyncFrmAsync(
        Guid tripId, Guid siteId, CancellationToken ct = default)
        => frmSyncService.SyncTripInfoAsync(tripId, siteId, ct);

    /// <summary>Kiểm tra điều kiện trước lượt cân: tạp chất, chữ đường, lượt cân trước (Req 4.1).</summary>
    public Task<WeighPreconditions> CheckPreconditionsAsync(
        Guid tripId, WeightRole role, int segmentIndex, CancellationToken ct = default)
        => preconditionQuery.EvaluateAsync(tripId, role, segmentIndex, ct);

    /// <summary>In phiếu cân sau cân lần 2 (Req 5.1). Lỗi in không ảnh hưởng dữ liệu cân.</summary>
    public Task<PrintTicketResult> PrintTicketAsync(
        Guid tripId, PrinterConfig config, bool isReprint, CancellationToken ct = default)
        => printService.PrintAsync(tripId, config, isReprint, ct);

    /// <summary>Đồng bộ kết quả cân đến ERP/FRM (Req 8.1).</summary>
    public Task<SubmitTripResultResult> SyncErpAsync(
        SubmitTripResultCommand cmd, CancellationToken ct = default)
        => submitTripResult.HandleAsync(cmd, ct);

    /// <summary>
    /// Lấy WeighEvent hiệu lực cho trip + role + segment.
    /// Dùng để load dữ liệu cân lần 1 khi chuẩn bị cân lần 2.
    /// </summary>
    public Task<Domain.Entities.WeighEvent?> GetEffectiveWeighEventAsync(
        Guid tripId, WeightRole role, int segmentIndex, CancellationToken ct = default)
        => weighSessionRepository.GetEffectiveEventAsync(tripId, role, segmentIndex, ct);

    /// <summary>Nhập tay số cân khi thiết bị sự cố, bắt buộc lý do + phê duyệt (Req 11.1).</summary>
    public Task<ManualWeightResult> ManualWeightAsync(
        ApproveManualWeightCommand cmd, CancellationToken ct = default)
        => manualWeightHandler.HandleAsync(cmd, ct);

    /// <summary>Yêu cầu cân lại sau sự cố, giữ lịch sử lượt cũ (Req 10.3).</summary>
    public Task<RequestReweighResult> RequestReweighAsync(
        RequestReweighCommand cmd, CancellationToken ct = default)
        => reweighHandler.HandleAsync(cmd, ct);

    /// <summary>Lấy trạng thái kết nối thiết bị tại site, poll mỗi 2 giây (Req 13.1).</summary>
    public Task<IReadOnlyList<DeviceStatus>> GetDeviceStatusAsync(
        Guid siteId, Guid? laneId, CancellationToken ct = default)
        => deviceStatusMonitor.GetStatusAsync(siteId, laneId, ct);

    /// <summary>Lấy bàn cân đầu tiên tại site — dùng làm mặc định khi QR tạm không gán bàn cân.</summary>
    public async Task<Guid?> GetDefaultWeighbridgeAsync(CancellationToken ct = default)
    {
        var bridges = await weighbridgeRepository.ListBySiteAsync(siteContext.CurrentSiteId, ct);
        return bridges.FirstOrDefault()?.Id;
    }

    /// <summary>Lấy danh sách xe đã cân tại bàn cân, filter theo ngày và tab (Req 1.2, 8.6).</summary>
    public Task<IReadOnlyList<WeighedTripRow>> LoadWeighedTripsAsync(
        Guid siteId, Guid weighbridgeId, WeighedTripFilter filter,
        CancellationToken ct = default)
        => weighOperationQueries.ListAsync(siteId, weighbridgeId, filter, ct);

    /// <summary>Lấy danh sách xe đang chờ cân (đã đăng tài/vào cổng nhưng chưa cân).</summary>
    public async Task<IReadOnlyList<WeighedTripRow>> LoadPendingTripsAsync(
        Guid siteId, CancellationToken ct = default)
        => await weighOperationQueries.ListPendingAsync(siteId, ct);

    /// <summary>Tìm LenhDon theo biển số xe — lọc theo BU active (TTCBH thấy tất cả).</summary>
    public FrmLenhDonInfo? FindLenhDonByPlate(string plate)
    {
        var normalized = plate.Replace(" ", "").Replace("-", "").Replace(".", "").ToUpperInvariant();
        var activeBu = frmCache.ActiveBusinessUnit;

        return frmCache.AllLenhDon
            .Where(ld => ld.StatusCodeName == "Đã duyệt")
            .Where(ld => activeBu is null || string.Equals(ld.BusinessUnit, activeBu, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(ld => ld.NgayCap)
            .FirstOrDefault(ld =>
            {
                if (ld.BienSoXe is null) return false;
                var ldPlate = ld.BienSoXe.Replace(" ", "").Replace("-", "").Replace(".", "").ToUpperInvariant();
                return ldPlate == normalized;
            });
    }

    /// <summary>Push phiếu cân lên DigiScale (bước 8). isQuetCS = true khi cân lần 2 để lấy chính sách.</summary>
    public Task<TTC.Weighing.Infrastructure.Adapters.TicketResult> PushPhieuCanAsync(
        string loaiCan, string lanCan, string nvCan, string? userNameNvCan, string lenhDonId, string maLenhDon,
        string trongLuong, DateTime thoiGian, string soPhieuCan, string? maBanCan,
        string tlThanhToan, string tcThanhToan, string ccsThanhToan, string? lenhDonBu = null, CancellationToken ct = default, bool isQuetCS = false)
        => digiScaleTickets.PushPhieuCanAsync(loaiCan, lanCan, nvCan, userNameNvCan, lenhDonId, maLenhDon, trongLuong, thoiGian, soPhieuCan, maBanCan, tlThanhToan, tcThanhToan, ccsThanhToan, lenhDonBu, ct, isQuetCS);

    /// <summary>Push phiếu cân vật tư lên ERP (oracle-tickets).</summary>
    public Task<TicketResult> PushOracleTicketAsync(
        string transactionTypeName, string transactionTypeCode, string actionCode,
        string tripNumber, string shipmentNumber,
        decimal weightIn, decimal weightOut, decimal netWeight,
        string weightNumber, string organizationCode, string businessUnit,
        string interfaceReference,
        string? itemNumber = null, string? itemDescription = null, decimal? quantity = null,
        string? transporter = null, string? plateNumber = null, string? driverName = null, string? truckOwner = null,
        string? vendorName = null, string? vendorNumber = null, string? vendorSiteCode = null,
        CancellationToken ct = default)
        => digiScaleTickets.PushOracleTicketAsync(
            transactionTypeName, transactionTypeCode, actionCode,
            tripNumber, shipmentNumber, weightIn, weightOut, netWeight,
            weightNumber, organizationCode, businessUnit, interfaceReference,
            itemNumber, itemDescription, quantity,
            transporter, plateNumber, driverName, truckOwner,
            vendorName, vendorNumber, vendorSiteCode, ct);

    /// <summary>Tra cứu ERP order-line theo shipment number — dùng để lấy TransactionType + OrgCode.</summary>
    public async Task<TTC.Weighing.Infrastructure.Adapters.ErpOrderLine?> LookupErpOrderAsync(string bu, string shipmentNumber)
    {
        // 1. Tìm trong DB local trước
        var dbItem = await erpOrderLines.FindInDbAsync(shipmentNumber);
        if (dbItem is not null) return dbItem;

        // 2. Fallback: gọi API
        var from = DateTime.Today.AddDays(-90);
        var to = DateTime.Today.AddDays(1);
        var response = await erpOrderLines.GetOrderLinesAsync(bu, from, to,
            shipmentNumber: shipmentNumber, pageSize: 1);
        return response?.Items?.FirstOrDefault();
    }

    /// <summary>Ghi trạng thái đồng bộ ERP vào integration_messages.</summary>
    public Task RecordErpSyncAsync(Guid tripId, string operation, string? responseBody)
        => erpOrderLines.RecordErpSyncAsync(tripId, operation, responseBody);

    /// <summary>Lấy mã bàn cân theo ID.</summary>
    public async Task<string?> GetWeighbridgeCodeAsync(Guid weighbridgeId, CancellationToken ct = default)
    {
        if (weighbridgeId == Guid.Empty) return null;
        var bridge = await weighbridgeRepository.GetByIdAsync(weighbridgeId, ct);
        return bridge?.Code;
    }

    /// <summary>Lấy tạp chất hiệu lực cho chuyến — dùng cho phiếu cân.</summary>
    public async Task<decimal?> GetEffectiveImpurityAsync(Guid tripId, CancellationToken ct = default)
    {
        var sample = await qualitySampleRepository.GetEffectiveAsync(
            tripId, Domain.Enums.QualitySampleType.Impurity, ct);
        return sample?.ImpurityPercent;
    }
}