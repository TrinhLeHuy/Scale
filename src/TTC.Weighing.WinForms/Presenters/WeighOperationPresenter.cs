using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.Queries;
using TTC.Weighing.Application.UseCases.Integration;
using TTC.Weighing.Application.UseCases.Trips;
using TTC.Weighing.Application.UseCases.Weighing;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Infrastructure.Adapters;
using TTC.Weighing.WinForms.Forms;
using TTC.Weighing.Domain.Common;

namespace TTC.Weighing.WinForms.Presenters;

/// <summary>Dữ liệu hiển thị số cân, đã tính độ mới tại tầng presenter.</summary>
public sealed record LiveWeightView(
    decimal? NormalizedKg,
    string Uom,
    ScaleDeviceStatus DeviceStatus,
    bool SignalFresh,
    TimeSpan StableDuration);

/// <summary>
/// Dữ liệu đẩy DigiScale lấy TỪ DB theo đúng chuyến — không lấy từ ô trên form (đã làm tròn hiển thị)
/// và không lấy lệnh đốn theo biển số/biến form cũ (dễ lẫn sang chuyến trước).
/// </summary>
public sealed record DigiScaleWeighContext(
    Guid TripId,
    string TripNo,
    string SourceSystem,
    string? LenhDonId,
    string? MaLenhDon,
    string? LenhDonBu,
    decimal? FirstKg,
    DateTimeOffset? FirstAt,
    decimal? SecondKg,
    DateTimeOffset? SecondAt)
{
    public bool HasLenhDon => !string.IsNullOrWhiteSpace(LenhDonId) && !string.IsNullOrWhiteSpace(MaLenhDon);

    /// <summary>Mã bàn cân của lượt cân lần 2 (gửi kèm phiếu khi đẩy lại từ màn khác).</summary>
    public string? SecondBridgeCode { get; init; }

    /// <summary>
    /// Mã phiếu cân (PC + niên vụ + số tăng dần) — gửi DigiScale làm SoPhieuCan. Cân vào / ra cùng một mã.
    /// Context đọc để đẩy DigiScale luôn có mã (sinh nếu chưa có); chưa sinh được → số đăng tài.
    /// </summary>
    public string? TicketNo { get; init; }

    /// <summary>Số phiếu gửi DigiScale.</summary>
    public string SoPhieuCan => string.IsNullOrWhiteSpace(TicketNo) ? TripNo : TicketNo;
}

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
    public async Task<IReadOnlyList<TripRegistrationRow>> ListTripsAsync(
        Guid siteId, TripRegistrationFilter filter, CancellationToken ct = default)
        => (await tripRegistrationQueries.ListAsync(siteId, filter, ct))
            .Select(r => CommodityForTrip(null, r.ShipmentNo) is { } label ? r with { Commodity = label } : r)
            .ToList();

    /// <summary>
    /// Hàng hóa của chuyến mía: Mía cháy / Mía khúc / Mía cây theo lệnh đốn. Null nếu không phải mía
    /// hoặc FRM chưa trả cờ cháy / thu hoạch → form giữ giá trị cũ.
    /// </summary>
    public string? CommodityForTrip(string? lenhDonId, string? maLenhDon)
        => Services.SugarcaneCommodity.ForTrip(frmCache, lenhDonId, maLenhDon);

    private IReadOnlyList<WeighedTripRow> WithSugarcaneCommodity(IReadOnlyList<WeighedTripRow> rows)
        => rows.Select(r => CommodityForTrip(null, r.ShipmentNo) is { } label ? r with { Commodity = label } : r)
            .ToList();

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
    public async Task<IReadOnlyList<WeighedTripRow>> LoadWeighedTripsAsync(
        Guid siteId, Guid weighbridgeId, WeighedTripFilter filter,
        CancellationToken ct = default)
        => WithSugarcaneCommodity(await weighOperationQueries.ListAsync(siteId, weighbridgeId, filter, ct));

    /// <summary>Lấy danh sách xe đang chờ cân (đã đăng tài/vào cổng nhưng chưa cân).</summary>
    public async Task<IReadOnlyList<WeighedTripRow>> LoadPendingTripsAsync(
        Guid siteId, TripCategory category, CancellationToken ct = default)
        => WithSugarcaneCommodity(await weighOperationQueries.ListPendingAsync(siteId, category, ct));

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
                if (ld.VehiclePlate is null) return false;
                var ldPlate = ld.VehiclePlate.Replace(" ", "").Replace("-", "").Replace(".", "").ToUpperInvariant();
                return ldPlate == normalized;
            });
    }

    /// <summary>
    /// Lệnh đốn của CHUYẾN đang cân: theo ID lệnh đốn lưu lúc đăng tài (đúng lệnh đã đăng tài);
    /// chỉ khi chuyến không có ID mới tra theo biển số. Xe có nhiều lệnh đốn không bị lấy nhầm lệnh khác.
    /// </summary>
    public FrmLenhDonInfo? FindLenhDonForTrip(string? lenhDonId, string? plate)
        => frmCache.FindLenhDonBySourceKey(lenhDonId)
           ?? (string.IsNullOrWhiteSpace(lenhDonId) && !string.IsNullOrWhiteSpace(plate) ? FindLenhDonByPlate(plate) : null);

    /// <summary>
    /// Tìm lệnh đốn đã duyệt theo mã lệnh đốn hoặc biển số — dùng khi quét ở trạm cân không ra chuyến,
    /// để báo đúng lý do "lệnh đốn chưa có phiếu đăng tài".
    /// </summary>
    public FrmLenhDonInfo? FindLenhDonByCodeOrPlate(string content)
    {
        var code = content.Trim();
        if (code.Length == 0) return null;
        var activeBu = frmCache.ActiveBusinessUnit;

        return frmCache.AllLenhDon
                   .Where(ld => ld.StatusCodeName == "Đã duyệt")
                   .Where(ld => activeBu is null || string.Equals(ld.BusinessUnit, activeBu, StringComparison.OrdinalIgnoreCase))
                   .FirstOrDefault(ld => string.Equals(ld.MaLD, code, StringComparison.OrdinalIgnoreCase))
               ?? FindLenhDonByPlate(code);
    }

    /// <summary>Push phiếu cân lên DigiScale (bước 8). isQuetCS = true khi cân lần 2 để lấy chính sách.</summary>
    public Task<TTC.Weighing.Infrastructure.Adapters.TicketResult> PushPhieuCanAsync(
        string loaiCan, string lanCan, string nvCan, string? userNameNvCan, string lenhDonId, string maLenhDon,
        string trongLuong, DateTime thoiGian, string soPhieuCan, string? maBanCan,
        string tlThanhToan, string tcThanhToan, string ccsThanhToan, string? lenhDonBu = null, CancellationToken ct = default, bool isQuetCS = false,
        string? tapChatCs = null, string? ccsChinhSach = null, string? ghiChu = null)
        => digiScaleTickets.PushPhieuCanAsync(loaiCan, lanCan, nvCan, userNameNvCan, lenhDonId, maLenhDon, trongLuong, thoiGian, soPhieuCan, maBanCan, tlThanhToan, tcThanhToan, ccsThanhToan, lenhDonBu, ct, isQuetCS, tapChatCs, ccsChinhSach, ghiChu);

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
        CancellationToken ct = default,
        string? organizationName = null,
        string? note = null)
        => digiScaleTickets.PushOracleTicketAsync(
            transactionTypeName, transactionTypeCode, actionCode,
            tripNumber, shipmentNumber, weightIn, weightOut, netWeight,
            weightNumber, organizationCode, businessUnit, interfaceReference,
            itemNumber, itemDescription, quantity,
            transporter, plateNumber, driverName, truckOwner,
            vendorName, vendorNumber, vendorSiteCode, ct, organizationName, note);

    /// <summary>Tra cứu ERP order-line theo shipment number — dùng để lấy TransactionType + OrgCode.</summary>
    public async Task<TTC.Weighing.Infrastructure.Adapters.ErpOrderLine?> LookupErpOrderAsync(string bu, string shipmentNumber)
    {
        // 1. API trước — bản mới nhất trên DigiScale, lưu đè DB local.
        var from = VnTime.Today.AddDays(-90);
        var to = VnTime.Today.AddDays(1);
        var response = await erpOrderLines.GetOrderLinesAsync(bu, from, to,
            shipmentNumber: shipmentNumber, pageSize: 1);
        var item = response?.Items?.FirstOrDefault();
        if (item is not null)
        {
            if (string.IsNullOrWhiteSpace(item.BusinessUnit)) item.BusinessUnit = item.BusinessUnitName ?? bu;
            await erpOrderLines.SaveToDbAsync([item]);
            return item;
        }

        // 2. Không có trên API trong 90 ngày / mất kết nối → DB local (đã kéo trước đó).
        return await erpOrderLines.FindInDbAsync(shipmentNumber);
    }

    /// <summary>
    /// Nhà vận chuyển đẩy oracle-tickets: giá trị lưu trên chuyến lúc đăng tài, không có thì theo ERP order-line;
    /// cả hai trống → null (DigiScale nhận null, không nhận chuỗi rỗng).
    /// </summary>
    public async Task<string?> ResolveTransporterAsync(Guid? tripId, TTC.Weighing.Infrastructure.Adapters.ErpOrderLine? erpLine, CancellationToken ct = default)
        => await ReadTripTextAsync(tripId, "transporter", ct) ?? erpLine?.EffectiveTransporter;

    /// <summary>
    /// Kho (tên tổ chức kho) đẩy oracle-tickets: giá trị lưu trên chuyến lúc đăng tài, không có thì theo ERP order-line;
    /// cả hai trống → null.
    /// </summary>
    public async Task<string?> ResolveWarehouseAsync(Guid? tripId, TTC.Weighing.Infrastructure.Adapters.ErpOrderLine? erpLine, CancellationToken ct = default)
        => await ReadTripTextAsync(tripId, "warehouse_name", ct) ?? erpLine?.EffectiveWarehouse;

    /// <summary>Mã tổ chức kho: theo ERP order-line, không có thì phần mã đầu của tên kho ("126501 - ..." → "126501").</summary>
    public static string? WarehouseOrganizationCode(string? erpOrganizationCode, string? warehouseName)
    {
        if (!string.IsNullOrWhiteSpace(erpOrganizationCode)) return erpOrganizationCode.Trim();
        if (string.IsNullOrWhiteSpace(warehouseName)) return null;
        var code = warehouseName.Split(" - ", 2)[0].Trim();
        return code.Length > 0 && code.All(char.IsLetterOrDigit) ? code : null;
    }

    /// <summary>Đọc một cột chữ của chuyến (kết nối riêng). Cột chỉ định cố định trong code, không từ người dùng.</summary>
    private async Task<string?> ReadTripTextAsync(Guid? tripId, string column, CancellationToken ct)
    {
        var connStr = frmCache.ConnectionString;
        if (tripId is not { } id || string.IsNullOrWhiteSpace(connStr)) return null;
        try
        {
            await using var conn = new Npgsql.NpgsqlConnection(connStr);
            await conn.OpenAsync(ct);
            await using var cmd = new Npgsql.NpgsqlCommand($"SELECT {column} FROM trips WHERE id = $1", conn);
            cmd.Parameters.AddWithValue(id);
            return await cmd.ExecuteScalarAsync(ct) is string saved && !string.IsNullOrWhiteSpace(saved)
                ? saved.Trim()
                : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Chưa có cột (DB chưa migrate) / lỗi đọc → dùng ERP order-line.
            return null;
        }
    }

    /// <summary>Ghi trạng thái đồng bộ ERP vào integration_messages.</summary>
    public Task RecordErpSyncAsync(Guid tripId, string operation, string? responseBody)
        => erpOrderLines.RecordErpSyncAsync(tripId, operation, responseBody);

    /// <summary>Ghi trạng thái đẩy phiếu cân mía lên DigiScale (cột "Đồng bộ" trên lưới).</summary>
    public Task RecordDigiScaleSyncAsync(Guid tripId, string operation, bool success)
        => erpOrderLines.RecordSyncAsync(tripId, "FRM", operation, success);

    /// <summary>
    /// QR mới nhất của chuyến: loại, còn hiệu lực không, hết hạn lúc nào (đọc DB riêng, không đụng DbContext form).
    /// Null nếu chuyến chưa có QR.
    /// </summary>
    public async Task<(string Type, bool IsValid, DateTimeOffset? ExpiresAt)?> GetTripQrStateAsync(Guid tripId, CancellationToken ct = default)
    {
        var connStr = frmCache.ConnectionString;
        if (string.IsNullOrWhiteSpace(connStr)) return null;
        await using var conn = new Npgsql.NpgsqlConnection(connStr);
        await conn.OpenAsync(ct);
        await using var cmd = new Npgsql.NpgsqlCommand(
            "SELECT type, (status = 'Issued' AND revoked_at IS NULL AND (expires_at IS NULL OR expires_at > now())), expires_at " +
            "FROM qr_tokens WHERE trip_id = $1 AND deleted_at IS NULL ORDER BY issued_at DESC LIMIT 1", conn);
        cmd.Parameters.AddWithValue(tripId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return (r.GetString(0), r.GetBoolean(1), r.IsDBNull(2) ? null : r.GetFieldValue<DateTimeOffset>(2));
    }

    /// <summary>Lấy mã bàn cân theo ID.</summary>
    public async Task<string?> GetWeighbridgeCodeAsync(Guid weighbridgeId, CancellationToken ct = default)
    {
        if (weighbridgeId == Guid.Empty) return null;
        var bridge = await weighbridgeRepository.GetByIdAsync(weighbridgeId, ct);
        return bridge?.Code;
    }

    /// <summary>
    /// Đọc lệnh đốn + hai lượt cân hiệu lực của chuyến từ DB (kết nối riêng, tránh tranh chấp DbContext
    /// với timer của form). Lần 1/lần 2 xác định theo thời điểm cân, không theo vai trò.
    /// Null nếu không đọc được.
    /// </summary>
    public Task<DigiScaleWeighContext?> GetDigiScaleContextAsync(Guid tripId, CancellationToken ct = default,
        bool ensureTicket = false)
        => DigiScaleWeighContextReader.ReadAsync(frmCache.ConnectionString, tripId, ct, ensureTicket);

    /// <summary>Mã phiếu cân của chuyến (sinh nếu chưa có). Gọi sau khi chốt cân lần 1 / trước khi đẩy.</summary>
    public Task<string?> EnsureTicketNumberAsync(Guid tripId, CancellationToken ct = default)
        => TTC.Weighing.Infrastructure.Repositories.WeighTicketNumberGenerator.EnsureAsync(frmCache.ConnectionString, tripId, ct);

    /// <summary>Chính sách đã lưu: theo mã phiếu cân (mới), không có thì theo số đăng tài (dữ liệu cũ).</summary>
    public async Task<TTC.Weighing.Infrastructure.Integration.TripPolicy?> GetStoredPolicyForTripAsync(
        Guid tripId, string tripNo, CancellationToken ct = default)
    {
        var ctx = await GetDigiScaleContextAsync(tripId, ct);
        if (ctx?.TicketNo is { } ticket && await digiScaleTickets.GetStoredPolicyAsync(ticket, ct) is { } byTicket)
            return byTicket;
        return await digiScaleTickets.GetStoredPolicyAsync(tripNo, ct);
    }

    /// <summary>Chính sách đã bắt được của chuyến (lưu sau mỗi lần bắt chính sách) — null nếu chưa có.</summary>
    public Task<TTC.Weighing.Infrastructure.Integration.TripPolicy?> GetStoredPolicyAsync(string tripNo, CancellationToken ct = default)
        => digiScaleTickets.GetStoredPolicyAsync(tripNo, ct);

    public bool CanPreviewPolicy => digiScaleTickets.CanPreviewPolicy;

    /// <summary>
    /// Xem trước chính sách cho cân lần 2 của chuyến mía (sau khi đã đo tạp chất) với số cân hiện tại.
    /// Lệnh đốn / số phiếu lấy từ DB theo chuyến. Null nếu chuyến chưa có lệnh đốn.
    /// </summary>
    public async Task<FrmPolicyResult?> PreviewPolicyAsync(Guid tripId, decimal currentKg, string? maBanCan,
        string? userName, string? displayName, CancellationToken ct = default)
    {
        var ctx = await GetDigiScaleContextAsync(tripId, ct, ensureTicket: true);
        if (ctx is null || !ctx.HasLenhDon || currentKg <= 0) return null;
        return await digiScaleTickets.PreviewPolicyAsync(ctx.LenhDonBu ?? DefaultBusinessUnit, ctx.SoPhieuCan,
            ctx.LenhDonId!, ctx.MaLenhDon!, maBanCan, currentKg / 1000m, userName, displayName, ct);
    }

    /// <summary>Mẫu chất lượng hiệu lực (tạp chất / CCS) của chuyến — dùng cho popup Xem thêm.</summary>
    public Task<Domain.Entities.QualitySample?> GetEffectiveSampleAsync(
        Guid tripId, Domain.Enums.QualitySampleType type, CancellationToken ct = default)
        => qualitySampleRepository.GetEffectiveAsync(tripId, type, ct);

    /// <summary>Lấy tạp chất hiệu lực cho chuyến — dùng cho phiếu cân.</summary>
    public async Task<decimal?> GetEffectiveImpurityAsync(Guid tripId, CancellationToken ct = default)
    {
        var sample = await qualitySampleRepository.GetEffectiveAsync(
            tripId, Domain.Enums.QualitySampleType.Impurity, ct);
        return sample?.ImpurityPercent;
    }
}