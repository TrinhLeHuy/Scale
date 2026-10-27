using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.Queries;
using TTC.Weighing.Application.UseCases.Trips;
using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Infrastructure.Adapters;
using TTC.Weighing.WinForms.Forms;

namespace TTC.Weighing.WinForms.Presenters;

/// <summary>
/// Lệnh đốn đăng tài được trong khung ngày + số lệnh bị loại theo từng lý do
/// (để báo cho người dùng khi danh sách trống).
/// </summary>
public sealed record RegistrableLenhDonList(
    IReadOnlyList<FrmLenhDonInfo> Available,
    int NotApproved,
    int NoPlate,
    int AlreadyRegistered,
    int OutOfRange)
{
    public string[] Plates => Available
        .Select(ld => ld.VehiclePlate!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(p => p)
        .ToArray();

    public string[] Codes => Available
        .Select(ld => ld.MaLD)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(c => c)
        .ToArray();

    public static RegistrableLenhDonList Empty { get; } = new([], 0, 0, 0, 0);
}

/// <summary>
/// Presenter cho UI-02 — Đăng tài. Form gọi Presenter, Presenter gọi Application handlers.
/// Form không trực tiếp gọi Domain hay Infrastructure — đúng pattern MVP.
/// </summary>
public sealed class TripRegistrationPresenter(
    CheckPlanHandler checkPlan,
    RegisterTripHandler registerTrip,
    CancelTripHandler cancelTrip,
    DeleteTripHandler deleteTrip,
    ITripRegistrationQueries tripQueries,
    IWorkflowProfileRepository profileRepository,
    ITripRepository tripRepository,
    IUnitOfWork unitOfWork,
    ICameraService camera,
    ISiteContext siteContext,
    ICurrentUser currentUser,
    IWeighTicketPrintService printService,
    TTC.Weighing.Infrastructure.Adapters.FrmMasterDataCache frmCache,
    TTC.Weighing.Infrastructure.Adapters.DigiScaleTicketAdapter digiScaleTickets,
    TTC.Weighing.Infrastructure.Adapters.ErpOrderLineAdapter erpOrderLines)
{
    /// <summary>
    /// IWeighTicketPrintService cho PrintHelper dùng (Req 8.3).
    /// PrintHelper cần service để hiển thị dialog chọn máy in rồi gọi PrintAsync.
    /// </summary>
    public IWeighTicketPrintService PrintService => printService;
    /// <summary>SiteId hiện tại từ ISiteContext, Form dùng để binding UI.</summary>
    public Guid SiteId => siteContext.CurrentSiteId;

    /// <summary>Tên nhà máy, hiển thị trên phiếu đăng tài.</summary>
    public string? SiteName => siteContext.CurrentSiteName;

    /// <summary>Địa chỉ nhà máy, hiển thị trên phiếu đăng tài.</summary>
    public string? SiteAddress => siteContext.CurrentSiteAddress;

    /// <summary>Tên người dùng hiện tại, hiển thị trên thanh trạng thái.</summary>
    public string OperatorName => currentUser.DisplayName;

    /// <summary>
    /// Lấy danh sách hồ sơ nghiệp vụ đã duyệt tại nhà máy hiện tại.
    /// Form dùng để populate ComboBox chọn loại nghiệp vụ (Mía nhập / Vật tư).
    /// </summary>
    public Task<IReadOnlyList<WorkflowProfile>> GetApprovedProfilesAsync(
        CancellationToken ct = default)
        => profileRepository.ListApprovedAsync(siteContext.CurrentSiteId, ct);

    /// <summary>
    /// Kiểm tra biển số trong kế hoạch FRM/ERP (Req 1.1).
    /// Presenter tự gắn SiteId và CorrelationId — Form chỉ truyền biển số.
    /// </summary>
    public Task<CheckPlanResult> CheckPlanAsync(
        string plate, CancellationToken ct = default)
    {
        var cmd = new CheckPlanCommand(
            SiteId: siteContext.CurrentSiteId,
            Plate: plate,
            CorrelationId: Guid.NewGuid().ToString("N"));

        return checkPlan.HandleAsync(cmd, ct);
    }

    /// <summary>
    /// Đăng tài chính thức — trip có kế hoạch nguồn (Req 3.1).
    /// Form truyền command đầy đủ, Presenter forward sang handler.
    /// </summary>
    public Task<RegisterTripResult> RegisterAsync(
        RegisterTripCommand cmd, CancellationToken ct = default,
        PublishRegistrationBeforeSave? publishBeforeSave = null)
        => registerTrip.HandleAsync(cmd, ct, publishBeforeSave);

    /// <summary>
    /// Đăng tài tạm — phát QR tạm khi chưa có kế hoạch (Req 4.1).
    /// Presenter đảm bảo IssueTemporaryQrOnly = true.
    /// </summary>
    public Task<RegisterTripResult> RegisterTempAsync(
        RegisterTripCommand cmd, CancellationToken ct = default)
    {
        // Đảm bảo luồng tạm dù Form quên set cờ
        var tempCmd = cmd with { IssueTemporaryQrOnly = true };
        return registerTrip.HandleAsync(tempCmd, ct);
    }

    /// <summary>
    /// Hủy đăng tài (Req 5.1). Form phải thu thập lý do và xác nhận user trước khi gọi.
    /// </summary>
    public Task<CancelTripResult> CancelAsync(
        CancelTripCommand cmd, CancellationToken ct = default)
        => cancelTrip.HandleAsync(cmd, ct);

    /// <summary>
    /// Xóa phiếu đăng tài — chỉ khi chưa cân (server kiểm tra). Xóa xong lệnh đốn / Shipment đăng tài lại được.
    /// </summary>
    public Task<DeleteTripResult> DeleteAsync(DeleteTripCommand cmd, CancellationToken ct = default)
        => deleteTrip.HandleAsync(cmd, ct);

    /// <summary>
    /// Tải danh sách chuyến cho lưới (Req 7.1). Tự gắn SiteId từ context.
    /// </summary>
    public async Task<IReadOnlyList<TripRegistrationRow>> LoadGridAsync(
        TripRegistrationFilter filter, CancellationToken ct = default)
    {
        var rows = await tripQueries.ListAsync(siteContext.CurrentSiteId, filter, ct);
        // Chuyến mía: cột Hàng hóa = Mía cháy / Mía khúc / Mía cây theo lệnh đốn (Shipment = mã lệnh đốn).
        return rows
            .Select(r => Services.SugarcaneCommodity.ForTrip(frmCache, null, r.ShipmentNo) is { } label
                ? r with { Commodity = label }
                : r)
            .ToList();
    }

    /// <summary>
    /// Kiểm tra trạng thái camera tại vị trí (In/Out) (Req 10.1).
    /// Dùng GateId = Guid.Empty vì cổng chưa được cấu hình cụ thể ở ISiteContext.
    /// Khi có cấu hình cổng thực tế, sẽ truyền GateId từ form.
    /// </summary>
    public Task<CameraStatus> GetCameraStatusAsync(
        string position, CancellationToken ct = default)
        => camera.GetStatusAsync(Guid.Empty, position, ct);

    /// <summary>Tổng số xe trong FRM cache.</summary>
    public int FrmVehicleCount => frmCache.VehicleCount;

    /// <summary>FRM cache đang tải?</summary>
    public bool FrmCacheLoading => frmCache.IsLoading;

    /// <summary>Refresh FRM cache thủ công.</summary>
    public Task RefreshFrmCacheAsync(CancellationToken ct = default)
        => frmCache.RefreshAsync(ct);

    /// <summary>
    /// MỘT quy tắc duy nhất cho cả ô "Tìm theo biển số" và "Tìm theo lệnh đốn".
    /// Lệnh đốn đăng tài được khi:
    ///   1. đúng BU đang đăng nhập;
    ///   2. thời gian hiệu lực [Ngày cấp, Ngày hết hạn] giao với khung ngày đã chọn;
    ///   3. trạng thái "Đã duyệt";
    ///   4. có biển số (Biển số xe hoặc Số xe tạm);
    ///   5. chưa có phiếu đăng tài đang hoạt động.
    /// Danh sách biển số và danh sách mã lệnh đốn đều lấy từ kết quả này → luôn khớp nhau.
    /// Lệnh ưu tiên đứng trước, rồi lệnh cấp mới nhất.
    /// </summary>
    public async Task<RegistrableLenhDonList> GetRegistrableLenhDonAsync(
        DateTime from, DateTime to, CancellationToken ct = default)
    {
        var activeBu = frmCache.ActiveBusinessUnit;
        int outOfRange = 0, notApproved = 0, noPlate = 0, registered = 0;
        var available = new List<FrmLenhDonInfo>();

        foreach (var ld in frmCache.AllLenhDon)
        {
            if (activeBu is not null && !string.Equals(ld.BusinessUnit, activeBu, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!OverlapsDateRange(ld, from, to)) { outOfRange++; continue; }
            if (!IsApproved(ld)) { notApproved++; continue; }
            if (string.IsNullOrWhiteSpace(ld.VehiclePlate)) { noPlate++; continue; }
            if (await tripRepository.FindActiveByLenhDonIdAsync(ld.SourceKey ?? "", ct) is not null) { registered++; continue; }

            available.Add(ld);
        }

        var ordered = available
            .OrderByDescending(IsPriority)
            .ThenByDescending(ld => ParseDate(ld.NgayCap) ?? DateTimeOffset.MinValue)
            .ToList();

        return new RegistrableLenhDonList(ordered, notApproved, noPlate, registered, outOfRange);
    }

    /// <summary>
    /// Thời gian hiệu lực lệnh đốn [Ngày cấp, Ngày hết hạn] có giao với khung ngày [from, to] không.
    /// Không có Ngày cấp → dùng ngày tạo bản ghi FRM. Không có Ngày hết hạn → không giới hạn phía sau.
    /// </summary>
    private static bool OverlapsDateRange(FrmLenhDonInfo ld, DateTime from, DateTime to)
    {
        var start = ParseDate(ld.NgayCap) ?? ParseDate(ld.SourceCreatedDate);
        if (start is null) return false;

        var end = ParseDate(ld.NgayHetHan);
        return start.Value.LocalDateTime.Date <= to.Date
            && (end is null || end.Value.LocalDateTime.Date >= from.Date);
    }

    /// <summary>
    /// Kiểm tra thời điểm đăng tài có nằm trong thời gian hiệu lực của lệnh đốn [Ngày cấp, Ngày hết hạn].
    /// Trả null nếu hợp lệ; ngược lại trả (từ, đến) để báo lỗi "Lệnh đốn không nằm trong thời gian đăng tài".
    /// Ngày không đọc được thì không chặn ở đầu đó.
    /// </summary>
    public static (DateTimeOffset? From, DateTimeOffset? To)? CheckLenhDonPeriod(FrmLenhDonInfo ld, DateTimeOffset at)
    {
        DateTimeOffset? from = ld.NgayCap is not null && DateTimeOffset.TryParse(ld.NgayCap, out var f) ? f : null;
        DateTimeOffset? to = ld.NgayHetHan is not null && DateTimeOffset.TryParse(ld.NgayHetHan, out var t) ? t : null;

        var tooEarly = from.HasValue && at < from.Value;
        var tooLate = to.HasValue && at > to.Value;

        return tooEarly || tooLate ? (from, to) : null;
    }

    /// <summary>Tìm LenhDon theo mã lệnh đốn (ví dụ LD2526-00014630) — đã duyệt, đúng BU active.</summary>
    public FrmLenhDonInfo? FindLenhDonByCode(string maLenhDon)
    {
        var code = maLenhDon.Trim();
        if (code.Length == 0) return null;
        var activeBu = frmCache.ActiveBusinessUnit;

        // Không lọc trạng thái — lệnh Nháp/Hủy vẫn trả về để form báo "chưa ở trạng thái duyệt".
        return frmCache.AllLenhDon
            .Where(ld => activeBu is null || string.Equals(ld.BusinessUnit, activeBu, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(ld => string.Equals(ld.MaLD, code, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Tìm LenhDon theo biển số xe — lọc theo BU active (TTCBH thấy tất cả).
    /// Xe có nhiều lệnh đốn: ưu tiên lệnh ĐANG trong thời gian đăng tài; không có lệnh nào hợp lệ
    /// thì trả lệnh mới nhất để form báo đúng lý do (ví dụ lệnh cấp ngày mai).
    /// </summary>
    public FrmLenhDonInfo? FindLenhDonByPlate(string plate)
    {
        var normalized = plate.Replace(" ", "").Replace("-", "").Replace(".", "").ToUpperInvariant();
        var activeBu = frmCache.ActiveBusinessUnit;
        var now = DateTimeOffset.Now;

        // Lấy cả lệnh chưa duyệt (Nháp...) để form báo đúng lý do thay vì "không tìm thấy".
        var matches = frmCache.AllLenhDon
            .Where(ld => activeBu is null || string.Equals(ld.BusinessUnit, activeBu, StringComparison.OrdinalIgnoreCase))
            .Where(ld => ld.VehiclePlate is not null
                && ld.VehiclePlate.Replace(" ", "").Replace("-", "").Replace(".", "").ToUpperInvariant() == normalized)
            .OrderByDescending(ld => ParseDate(ld.NgayCap) ?? DateTimeOffset.MinValue) // Mới nhất trước
            .ToList();

        // Ưu tiên: đã duyệt + trong thời gian đăng tài (lệnh ưu tiên FRM trước) → đã duyệt → lệnh mới nhất (để báo lỗi trạng thái)
        var valid = matches.Where(ld => IsApproved(ld) && IsWithinRegistrationWindow(ld, now)).ToList();
        return valid.FirstOrDefault(IsPriority)
            ?? valid.FirstOrDefault()
            ?? matches.FirstOrDefault(IsApproved)
            ?? matches.FirstOrDefault();
    }

    /// <summary>Lệnh đốn ưu tiên trên FRM (Uutien = true → "1").</summary>
    public static bool IsPriority(FrmLenhDonInfo ld) => ld.UuTien == "1";

    /// <summary>Lệnh đốn ở trạng thái "Đã duyệt" — chỉ trạng thái này được đăng tài.</summary>
    public static bool IsApproved(FrmLenhDonInfo ld)
        => string.Equals(ld.StatusCodeName?.Trim(), "Đã duyệt", StringComparison.OrdinalIgnoreCase);

    /// <summary>Thời gian đăng tài của lệnh đốn: [Ngày cấp, Ngày hết hạn]. Null = không giới hạn phía đó.</summary>
    public static (DateTimeOffset? From, DateTimeOffset? To) RegistrationWindow(FrmLenhDonInfo ld)
        => (ParseDate(ld.NgayCap), ParseDate(ld.NgayHetHan));

    /// <summary>Thời điểm <paramref name="at"/> nằm trong thời gian đăng tài của lệnh đốn.</summary>
    public static bool IsWithinRegistrationWindow(FrmLenhDonInfo ld, DateTimeOffset at)
    {
        var (from, to) = RegistrationWindow(ld);
        return (from is null || at >= from) && (to is null || at <= to);
    }

    private static DateTimeOffset? ParseDate(string? value)
        => value is not null && DateTimeOffset.TryParse(value, out var d) ? d : null;

    /// <summary>Danh mục xe vận chuyển (the-xe + xe-van-chuyen) đã tải — chưa tải thì không kết luận được "xe không tồn tại".</summary>
    public bool HasVehicleCatalog => frmCache.VehicleCount > 0;

    /// <summary>
    /// Cập nhật thông tin chuyến đã chọn từ lưới (biển số, shipment, khách hàng, loại xe...).
    /// Load trip từ DB → apply changes → save.
    /// </summary>
    public async Task<(bool IsSuccess, string? FailureReason)> UpdateTripAsync(
        Guid tripId,
        string? plate,
        string? trailerPlate,
        string? shipmentNo,
        string? customerOrSupplier,
        string? sugarcaneOwner,
        string? sugarcaneType,
        string? sugarcaneVariety,
        Domain.Enums.VehicleType? vehicleType,
        CancellationToken ct = default)
    {
        var trip = await tripRepository.GetByIdAsync(tripId, ct);
        if (trip is null)
            return (false, "Không tìm thấy chuyến.");

        // Chỉ cho sửa khi chưa bắt đầu cân (TempRegistered hoặc Registered)
        if (trip.Lifecycle is not (Domain.Enums.TripLifecycle.TempRegistered
            or Domain.Enums.TripLifecycle.Registered))
            return (false, $"Chuyến đang ở trạng thái {trip.Lifecycle}, không thể chỉnh sửa.");

        // Apply changes
        if (plate is not null) trip.TractorPlate = plate;
        if (trailerPlate is not null) trip.TrailerPlate = trailerPlate;
        if (shipmentNo is not null) trip.ReferenceTripNo = shipmentNo;
        if (customerOrSupplier is not null) trip.SugarcaneOwner = customerOrSupplier;
        if (sugarcaneOwner is not null) trip.SugarcaneOwner = sugarcaneOwner;
        if (sugarcaneType is not null) trip.SugarcaneType = sugarcaneType;
        if (sugarcaneVariety is not null) trip.SugarcaneVariety = sugarcaneVariety;
        if (vehicleType is not null) trip.VehicleType = vehicleType;

        trip.Version++;
        trip.UpdatedAt = DateTimeOffset.UtcNow;

        await tripRepository.UpdateAsync(trip, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return (true, null);    
    }

    /// <summary>Push phiếu đăng tài lên DigiScale (bước 8). BU từ lệnh đốn.</summary>
    public async Task<TTC.Weighing.Infrastructure.Adapters.TicketResult> PushDangTaiAsync(
        string lenhDonId, string maLenhDon, DateTime ngay, string loaiXe, string soDangTai, string? uuTien = null, string? lenhDonBu = null, CancellationToken ct = default)
        => await digiScaleTickets.PushDangTaiAsync(lenhDonId, maLenhDon, ngay, loaiXe, soDangTai, uuTien, lenhDonBu, ct);

    /// <summary>Kéo đơn hàng ERP theo shipment/trip/plate. Số TRIP tìm cả shipmentNumber lẫn tripNumber.</summary>
    public Task<TTC.Weighing.Infrastructure.Adapters.ErpOrderLine?> LookupErpOrderAsync(
        string businessUnit, string? tripOrShipment, string? plateNumber, CancellationToken ct = default,
        DateTime? fromDate = null, DateTime? toDate = null)
    {
        return Task.Run(async () =>
        {
            // Khung ngày giao dịch ERP (như khung ngày lệnh đốn FRM). Không truyền → 90 ngày gần nhất.
            var from = (fromDate ?? DateTime.Today.AddDays(-90)).Date;
            var to = (toDate ?? DateTime.Today).Date.AddDays(1);

            // ── Có Số TRIP/Shipment: tìm đúng chứng từ đó ──
            if (!string.IsNullOrWhiteSpace(tripOrShipment))
            {
                var key = tripOrShipment.Trim();

                // 1. DB local (đã kéo về từ form ERP) — chỉ nhận đơn trong khung ngày.
                var dbItem = await erpOrderLines.FindInDbAsync(key);
                if (dbItem is not null && InErpRange(dbItem, from, to)) return dbItem;

                // 2. API theo shipmentNumber, rồi theo tripNumber.
                var response = await erpOrderLines.GetOrderLinesAsync(
                    businessUnit, from, to, shipmentNumber: key, pageSize: 1, ct: ct);
                var item = response?.Items?.FirstOrDefault();
                if (item is not null) return item;

                response = await erpOrderLines.GetOrderLinesAsync(
                    businessUnit, from, to, tripNumber: key, pageSize: 1, ct: ct);
                return response?.Items?.FirstOrDefault();
            }

            // ── Chỉ có biển số: chọn đơn của đúng xe đó trong khung ngày, mới nhất trước ──
            // (Trước đây gọi API không có bộ lọc shipment → lấy nhầm đơn đầu tiên bất kỳ trong khung ngày.)
            if (string.IsNullOrWhiteSpace(plateNumber)) return null;
            var plateKey = NormalizePlate(plateNumber);

            // 1. DB local: so biển số đã chuẩn hóa (bỏ khoảng trắng, '-', '.'), đúng BU + khung ngày.
            var local = (await ListErpOrdersInRangeAsync(from, to.AddDays(-1), ct))
                .Where(l => NormalizePlate(l.PlateNumber) == plateKey)
                .ToList();
            if (local.Count > 0) return local[0];

            // 2. API theo plateNumber — vẫn kiểm lại biển số + khung ngày, không nhận dòng không khớp.
            var byPlate = await erpOrderLines.GetOrderLinesAsync(
                businessUnit, from, to, plateNumber: plateNumber.Trim(), pageSize: 50, ct: ct);
            return byPlate?.Items?
                .Where(l => NormalizePlate(l.PlateNumber) == plateKey && InErpRange(l, from, to))
                .OrderByDescending(l => ErpDate(l) ?? DateTime.MinValue)
                .FirstOrDefault();
        }, ct);
    }

    private static string NormalizePlate(string? plate)
        => plate is null ? string.Empty
            : plate.Replace(" ", "").Replace("-", "").Replace(".", "").ToUpperInvariant();

    /// <summary>Ngày giao dịch ERP (giờ máy). Null nếu ERP không trả / không đọc được.</summary>
    public static DateTime? ErpDate(TTC.Weighing.Infrastructure.Adapters.ErpOrderLine line)
        => DateTimeOffset.TryParse(line.TransactionDate, System.Globalization.CultureInfo.InvariantCulture,
               System.Globalization.DateTimeStyles.AssumeUniversal, out var d)
            ? d.ToLocalTime().DateTime
            : null;

    /// <summary>Đơn nằm trong khung ngày [from, to). Đơn không có ngày giao dịch vẫn nhận (không loại oan).</summary>
    private static bool InErpRange(TTC.Weighing.Infrastructure.Adapters.ErpOrderLine line, DateTime from, DateTime to)
        => ErpDate(line) is not { } d || (d >= from && d < to);

    /// <summary>
    /// Đơn hàng ERP (đã kéo về DB local) trong khung ngày, theo BU đang đăng nhập — danh sách chọn nhanh Số TRIP.
    /// Mới nhất trước.
    /// </summary>
    public async Task<IReadOnlyList<TTC.Weighing.Infrastructure.Adapters.ErpOrderLine>> ListErpOrdersInRangeAsync(
        DateTime fromDate, DateTime toDate, CancellationToken ct = default)
    {
        var from = fromDate.Date;
        var to = toDate.Date.AddDays(1);
        var all = await Task.Run(() => erpOrderLines.LoadFromDbAsync(), ct);
        return all
            .Where(l => TTC.Weighing.Infrastructure.Queries.OrganizationUnitFilter.MatchesCurrent(l.BusinessUnit))
            .Where(l => !string.IsNullOrWhiteSpace(l.TripNumber ?? l.ShipmentNumber))
            .Where(l => InErpRange(l, from, to))
            .OrderByDescending(l => ErpDate(l) ?? DateTime.MinValue)
            // Một TRIP có thể nhiều dòng hàng → mỗi TRIP (theo nguồn Oracle/WMS) hiện một lần.
            .DistinctBy(l => (l.TripNumber ?? l.ShipmentNumber, l.Type))
            .ToList();
    }

    /// <summary>Tìm chuyến active (chưa Closed/Cancelled) theo lệnh đốn GUID.</summary>
    public async Task<string?> FindActiveTripByLenhDonAsync(string? lenhDonId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(lenhDonId)) return null;
        var trips = await tripRepository.FindActiveByLenhDonIdAsync(lenhDonId, ct);
        return trips;
    }

    /// <summary>Tìm chuyến vật tư đã dùng Shipment/TRIP ERP (mọi trạng thái trừ đã hủy, kể cả đã cân xong).</summary>
    public async Task<string?> FindActiveTripByErpShipmentAsync(string? shipmentOrTrip, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(shipmentOrTrip)) return null;
        return await tripRepository.FindActiveByReferenceAsync(
            TTC.Weighing.Application.Queries.TripCategoryExtensions.MATERIAL_SOURCE_SYSTEM, shipmentOrTrip, ct);
    }

    /// <summary>Tìm chuyến active theo biển số (xe chưa cân ra).</summary>
    public async Task<string?> FindActiveTripByPlateAsync(string plate, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(plate)) return null;
        var trip = await tripRepository.GetActiveByPlateAsync(plate, siteContext.CurrentSiteId, ct);
        return trip?.TripNo;
    }

    /// <summary>
    /// Thẻ xe của lệnh đốn trong danh mục thẻ FRM. Tên thẻ trên lệnh đốn dạng "{HĐ vận chuyển}-{xe}-{mã thẻ}"
    /// (ví dụ "2026VC0006-70R-020.10-T260000047") → khớp theo mã thẻ ở cuối. Null nếu không có thẻ nào khớp
    /// (thẻ chưa phát hành / chưa duyệt — ví dụ "2026VC0006-TAI00237" không có mã thẻ T…).
    /// </summary>
    public TTC.Weighing.Infrastructure.Adapters.FrmVehicleInfo? FindCardForLenhDon(string? theXeName)
    {
        if (string.IsNullOrWhiteSpace(theXeName)) return null;
        var name = theXeName.Trim();
        return frmCache.AllVehicles.FirstOrDefault(v =>
            !string.IsNullOrWhiteSpace(v.MaThe)
            && (string.Equals(v.MaThe.Trim(), name, StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("-" + v.MaThe.Trim(), StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Mã hợp đồng vận chuyển trong tên thẻ xe lệnh đốn ("2026VC0006-TAI00237" → "2026VC0006").</summary>
    public static string? TransportContractOf(string? theXeName)
    {
        if (string.IsNullOrWhiteSpace(theXeName)) return null;
        var idx = theXeName.IndexOf('-');
        return idx > 0 ? theXeName[..idx].Trim() : null;
    }

    /// <summary>Tìm xe trong FRM cache theo biển số.</summary>
    public List<TTC.Weighing.Infrastructure.Adapters.FrmVehicleInfo> FindVehicleByPlate(string plate)
        => frmCache.FindByPlate(plate);
}
