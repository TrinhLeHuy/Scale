using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.Queries;
using TTC.Weighing.Application.UseCases.Trips;
using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Infrastructure.Adapters;
using TTC.Weighing.WinForms.Forms;
using TTC.Weighing.Domain.Common;

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

    /// <summary>Dữ liệu FRM vừa kéo về có thay đổi (gọi từ luồng nền).</summary>
    public event Action? FrmDataRefreshed
    {
        add => frmCache.Refreshed += value;
        remove => frmCache.Refreshed -= value;
    }

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
        var now = VnTime.NowOffset;
        var busyPlates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var ld in frmCache.AllLenhDon)
        {
            if (activeBu is not null && !string.Equals(ld.BusinessUnit, activeBu, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!OverlapsDateRange(ld, from, to)) { outOfRange++; continue; }
            if (!IsApproved(ld) || IsCancelledLenhDon(ld)) { notApproved++; continue; }
            if (string.IsNullOrWhiteSpace(ld.VehiclePlate)) { noPlate++; continue; }
            // Chưa tới ngày cấp / đã hết hạn so với BÂY GIỜ → chưa / không đăng tài được, không đưa vào danh sách.
            if (!IsWithinRegistrationWindow(ld, now)) { outOfRange++; continue; }
            // Thẻ xe Hủy / hết hạn / Nháp → bị chặn khi đăng tài (thẻ xe tạm không kiểm tra thẻ).
            if (!ld.IsTemporaryCard && !IsCardUsable(ld)) { notApproved++; continue; }
            if (await tripRepository.FindActiveByLenhDonIdAsync(ld.SourceKey ?? "", ct) is not null) { registered++; continue; }
            if (await tripRepository.FindActiveByReferenceAsync(FRM_SOURCE_SYSTEM, ld.MaLD, ct) is not null) { registered++; continue; }
            // Phiếu cũ đã hủy → lệnh đốn này phải lập lệnh mới, không đưa vào danh sách đăng tài.
            if (await tripRepository.FindCancelledByLenhDonAsync(ld.SourceKey, ld.MaLD, ct) is not null) { registered++; continue; }
            // Xe đang có chuyến chưa cân ra → chưa đăng tài tiếp được.
            if (!busyPlates.TryGetValue(ld.VehiclePlate!, out var busy))
                busyPlates[ld.VehiclePlate!] = busy =
                    await tripRepository.FindNotWeighedOutByPlateAsync(ld.VehiclePlate!, siteContext.CurrentSiteId, ct) is not null;
            if (busy) { registered++; continue; }

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
        return start.Value.DateTime.Date <= to.Date
            && (end is null || end.Value.DateTime.Date >= from.Date);
    }

    /// <summary>
    /// Kiểm tra thời điểm đăng tài có nằm trong thời gian hiệu lực của lệnh đốn [Ngày cấp, Ngày hết hạn].
    /// Trả null nếu hợp lệ; ngược lại trả (từ, đến) để báo lỗi "Lệnh đốn không nằm trong thời gian đăng tài".
    /// Ngày không đọc được thì không chặn ở đầu đó.
    /// </summary>
    public static (DateTimeOffset? From, DateTimeOffset? To)? CheckLenhDonPeriod(FrmLenhDonInfo ld, DateTimeOffset at)
    {
        var from = ParseDate(ld.NgayCap);
        var to = ParseDate(ld.NgayHetHan);

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
        var now = VnTime.NowOffset;

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

    /// <summary>
    /// Lệnh đốn CHƯA ĐĂNG TÀI của xe (đã duyệt, chưa hủy, chưa có phiếu đăng tài đang dùng, chưa có phiếu đã hủy),
    /// đúng BU — không giới hạn khung ngày. Nghiệp vụ: mỗi xe tại một thời điểm chỉ có 1 lệnh đốn chưa đăng tài;
    /// nếu dữ liệu có nhiều hơn thì lệnh ưu tiên đứng trước, rồi lệnh cấp mới nhất.
    /// </summary>
    public async Task<IReadOnlyList<FrmLenhDonInfo>> FindUnregisteredLenhDonByPlateAsync(string plate, CancellationToken ct = default)
    {
        static string N(string? p) => (p ?? "").Replace(" ", "").Replace("-", "").Replace(".", "").ToUpperInvariant();
        var normalized = N(plate);
        if (normalized.Length == 0) return [];
        var activeBu = frmCache.ActiveBusinessUnit;

        var result = new List<FrmLenhDonInfo>();
        foreach (var ld in frmCache.AllLenhDon)
        {
            if (activeBu is not null && !string.Equals(ld.BusinessUnit, activeBu, StringComparison.OrdinalIgnoreCase)) continue;
            if (N(ld.VehiclePlate) != normalized) continue;
            if (!IsApproved(ld) || IsCancelledLenhDon(ld)) continue;
            if (await tripRepository.FindActiveByLenhDonIdAsync(ld.SourceKey ?? "", ct) is not null) continue;
            if (await tripRepository.FindActiveByReferenceAsync(FRM_SOURCE_SYSTEM, ld.MaLD, ct) is not null) continue;
            if (await tripRepository.FindCancelledByLenhDonAsync(ld.SourceKey, ld.MaLD, ct) is not null) continue;
            result.Add(ld);
        }

        return result
            .OrderByDescending(IsPriority)
            .ThenByDescending(ld => ParseDate(ld.NgayCap) ?? DateTimeOffset.MinValue)
            .ToList();
    }

    /// <summary>
    /// Thẻ xe của lệnh đốn dùng được (giống kiểm tra lúc đăng tài): trạng thái thẻ trên lệnh đốn, rồi thẻ khớp
    /// trong danh mục. Hủy / hết hạn / Inactive / Nháp → không dùng được. Không có thông tin thẻ → coi là dùng được
    /// (FRM / DigiScale kiểm tra lần cuối khi đăng tài).
    /// </summary>
    private bool IsCardUsable(FrmLenhDonInfo ld)
    {
        static bool Blocked(string? status)
        {
            var s = status?.Trim().ToUpperInvariant() ?? "";
            return s.Contains("HỦY") || s.Contains("HUỶ") || s.Contains("HẾT HẠN") || s.Contains("INACTIVE")
                   || s.Contains("CANCEL") || s.Contains("EXPIRED") || s.Contains("NHÁP") || s.Contains("DRAFT");
        }

        if (!string.IsNullOrWhiteSpace(ld.TheXeStatusName)) return !Blocked(ld.TheXeStatusName);
        return frmCache.FindCardForLenhDon(ld) is not { } card || !Blocked(card.StatusName);
    }

    /// <summary>Lệnh đốn ưu tiên trên FRM (Uutien = true → "1").</summary>
    public static bool IsPriority(FrmLenhDonInfo ld) => ld.UuTien == "1";

    /// <summary>Lệnh đốn đã Hủy trên FRM — đăng tài coi như không tìm thấy lệnh đốn / xe.</summary>
    public static bool IsCancelledLenhDon(FrmLenhDonInfo ld)
    {
        var s = ld.StatusCodeName?.Trim().ToUpperInvariant() ?? "";
        return s.Contains("HỦY") || s.Contains("HUỶ") || s.Contains("CANCEL");
    }

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

    /// <summary>Ngày FRM giữ nguyên giờ API trả (không cộng múi giờ).</summary>
    private static DateTimeOffset? ParseDate(string? value)
        => TTC.Weighing.Infrastructure.Adapters.DigiScaleDate.ParseOffset(value);

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

        // Màn Đăng tài mở lâu: bản chuyến trong bộ nhớ có thể cũ (màn Cân đã đóng chuyến).
        // Không nạp lại thì lưu sẽ ghi đè trạng thái cũ "Registered" lên chuyến đã cân xong.
        await tripRepository.ReloadAsync(trip, ct);

        // Xe đã cân (dù 1 lần) → không sửa thông tin đăng tài (lệnh đốn, biển số...) nữa.
        if (await tripRepository.HasWeighEventsAsync(tripId, ct))
            return (false, TTC.Weighing.WinForms.Localization.Loc.T("reg.update_blocked_weighed", trip.TripNo));

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
            var from = (fromDate ?? VnTime.Today.AddDays(-90)).Date;
            var to = (toDate ?? VnTime.Today).Date.AddDays(1);

            // ── Có Số TRIP/Shipment: tìm đúng chứng từ đó ──
            if (!string.IsNullOrWhiteSpace(tripOrShipment))
            {
                var key = tripOrShipment.Trim();

                // 1. API trước (dữ liệu mới nhất trên DigiScale) theo shipmentNumber, rồi tripNumber → lưu đè DB local.
                var response = await erpOrderLines.GetOrderLinesAsync(
                    businessUnit, from, to, shipmentNumber: key, pageSize: 1, ct: ct);
                var item = response?.Items?.FirstOrDefault();
                if (item is null && response is not null)
                {
                    response = await erpOrderLines.GetOrderLinesAsync(
                        businessUnit, from, to, tripNumber: key, pageSize: 1, ct: ct);
                    item = response?.Items?.FirstOrDefault();
                }
                if (item is not null) return await CacheErpLineAsync(item, businessUnit);

                // 2. Mất kết nối DigiScale → DB local (đã kéo về trước đó), chỉ nhận đơn trong khung ngày.
                if (response is not null) return null; // DigiScale trả lời: không có chứng từ này
                var dbItem = await erpOrderLines.FindInDbAsync(key);
                return dbItem is not null && InErpRange(dbItem, from, to) ? dbItem : null;
            }

            // ── Chỉ có biển số: chọn đơn của đúng xe đó trong khung ngày, mới nhất trước ──
            // (Trước đây gọi API không có bộ lọc shipment → lấy nhầm đơn đầu tiên bất kỳ trong khung ngày.)
            if (string.IsNullOrWhiteSpace(plateNumber)) return null;
            var plateKey = NormalizePlate(plateNumber);

            // 1. API trước theo plateNumber — vẫn kiểm lại biển số + khung ngày, không nhận dòng không khớp.
            var byPlate = await erpOrderLines.GetOrderLinesAsync(
                businessUnit, from, to, plateNumber: plateNumber.Trim(), pageSize: 50, ct: ct);
            if (byPlate is not null)
            {
                var found = byPlate.Items?
                    .Where(l => NormalizePlate(l.PlateNumber) == plateKey && InErpRange(l, from, to))
                    .OrderByDescending(l => ErpDate(l) ?? DateTime.MinValue)
                    .FirstOrDefault();
                return found is null ? null : await CacheErpLineAsync(found, businessUnit);
            }

            // 2. Mất kết nối DigiScale → DB local: so biển số đã chuẩn hóa, đúng BU + khung ngày.
            var local = (await ListErpOrdersInRangeAsync(from, to.AddDays(-1), ct))
                .Where(l => NormalizePlate(l.PlateNumber) == plateKey)
                .ToList();
            return local.Count > 0 ? local[0] : null;
        }, ct);
    }

    /// <summary>Dòng ERP vừa lấy từ DigiScale → lưu đè DB local (các màn khác và lúc mất mạng thấy bản mới).</summary>
    private async Task<TTC.Weighing.Infrastructure.Adapters.ErpOrderLine> CacheErpLineAsync(
        TTC.Weighing.Infrastructure.Adapters.ErpOrderLine line, string businessUnit)
    {
        if (string.IsNullOrWhiteSpace(line.BusinessUnit))
            line.BusinessUnit = line.BusinessUnitName ?? businessUnit;
        await erpOrderLines.SaveToDbAsync([line]);
        return line;
    }

    private static string NormalizePlate(string? plate)
        => plate is null ? string.Empty
            : plate.Replace(" ", "").Replace("-", "").Replace(".", "").ToUpperInvariant();

    /// <summary>Ngày giao dịch ERP — giữ nguyên giờ API trả (không cộng múi giờ). Null nếu ERP không trả / không đọc được.</summary>
    public static DateTime? ErpDate(TTC.Weighing.Infrastructure.Adapters.ErpOrderLine line)
        => TTC.Weighing.Infrastructure.Adapters.DigiScaleDate.Parse(line.TransactionDate);

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

    /// <summary>
    /// Chuyến đã dùng lệnh đốn này (mọi trạng thái trừ hủy / xóa, kể cả đã cân xong): theo ID lệnh đốn
    /// HOẶC mã lệnh đốn lưu trên chuyến — ID trên chuyến có thể lệch mã (chuyến cũ bị sửa lệnh đốn).
    /// </summary>
    public async Task<string?> FindActiveTripByLenhDonAsync(string? lenhDonId, string? maLenhDon = null, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(lenhDonId)
            && await tripRepository.FindActiveByLenhDonIdAsync(lenhDonId, ct) is { } byId)
            return byId;

        return string.IsNullOrWhiteSpace(maLenhDon)
            ? null
            : await tripRepository.FindActiveByReferenceAsync(FRM_SOURCE_SYSTEM, maLenhDon, ct);
    }

    private const string FRM_SOURCE_SYSTEM = "FRM";

    /// <summary>Phiếu đăng tài đã HỦY (chưa xóa) của lệnh đốn → phải lập lệnh đốn mới. Null nếu không có.</summary>
    public Task<string?> FindCancelledTripByLenhDonAsync(string? lenhDonId, string? maLenhDon, CancellationToken ct = default)
        => tripRepository.FindCancelledByLenhDonAsync(lenhDonId, maLenhDon, ct);

    /// <summary>Kết quả gửi DigiScale lệnh Delete phiếu đăng tài.</summary>
    /// <param name="Ok">Được phép hủy / xóa tiếp dưới app.</param>
    /// <param name="Sent">Đã gửi và DigiScale nhận (false = phiếu không có trên DigiScale nên không cần gửi).</param>
    /// <param name="BlockReason">Lý do chặn TRƯỚC khi gửi (phiếu đã đổi, đã cân) — không phải lỗi DigiScale.</param>
    /// <param name="DigiError">Lỗi DigiScale trả về (chưa dịch).</param>
    /// <param name="BlockAction">Cách xử lý cho <paramref name="BlockReason"/>.</param>
    public sealed record DigiDeleteOutcome(bool Ok, bool Sent, string? BlockReason = null, string? DigiError = null,
        string? BlockAction = null);

    /// <summary>
    /// Báo DigiScale hủy / xóa phiếu đăng tài (new_phieudangtai, action Delete) theo lệnh đốn LƯU TRÊN CHUYẾN.
    /// Kiểm tra trước những gì app sẽ chặn (phiếu bị người khác sửa, đã cân) để không xóa trên DigiScale
    /// mà app lại không hủy / xóa được. Chuyến không có trên DigiScale (vật tư, mía tạm, đã hủy trước đó) → không gửi.
    /// </summary>
    public async Task<DigiDeleteOutcome> DeleteRegistrationOnDigiScaleAsync(Guid tripId, int expectedVersion, CancellationToken ct = default)
    {
        var trip = await tripRepository.GetByIdAsync(tripId, ct);
        if (trip is not null) await tripRepository.ReloadAsync(trip, ct);
        if (trip is null || trip.DeletedAt is not null || trip.Version != expectedVersion)
            return new(false, false, BlockReason: TTC.Weighing.WinForms.Localization.Loc.T("rule.trip_changed_reload"),
                BlockAction: TTC.Weighing.WinForms.Localization.Loc.T("reg.act_reload_retry"));

        if (await tripRepository.HasWeighEventsAsync(tripId, ct))
            return new(false, false, BlockReason: TTC.Weighing.WinForms.Localization.Loc.T("reg.digi_delete_weighed", trip.TripNo),
                BlockAction: TTC.Weighing.WinForms.Localization.Loc.T("reg.act_weighed_no_cancel"));

        // Chỉ phiếu mía chính thức (có lệnh đốn) mới có trên DigiScale. Đã hủy trước đó → đã gửi Delete lúc hủy.
        if (!string.Equals(trip.SourceSystem, FRM_SOURCE_SYSTEM, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(trip.ReferenceTripNo)
            || trip.Lifecycle == TTC.Weighing.Domain.Enums.TripLifecycle.TempRegistered
            || trip.Lifecycle == TTC.Weighing.Domain.Enums.TripLifecycle.Cancelled)
            return new(true, false);

        // Lệnh đốn của chuyến (theo ID FRM lưu lúc đăng tài, thiếu thì theo mã lệnh đốn) — lấy BU.
        var ld = frmCache.FindLenhDonBySourceKey(trip.FrmLenhDonId) ?? FindLenhDonByCode(trip.ReferenceTripNo);
        var bu = ld?.BusinessUnit ?? frmCache.ActiveBusinessUnit ?? "";

        // phieudangtaiid = ID LỆNH ĐỐN TRÊN DIGISCALE — tra trực tiếp từ DigiScale ngay lúc hủy / xóa
        // (không dùng dữ liệu đã lưu trên máy). Không lấy được → KHÔNG hủy / xóa dưới app.
        var (registrationId, fetchError) = await frmCache.FetchLenhDonDigiIdAsync(
            trip.FrmLenhDonId, trip.ReferenceTripNo, bu, ct);
        if (fetchError is not null)
            return new(false, false, DigiError: fetchError);
        if (!IsGuid(registrationId))
            return new(false, false, BlockReason: TTC.Weighing.WinForms.Localization.Loc.T("reg.digi_delete_no_id", trip.TripNo),
                BlockAction: TTC.Weighing.WinForms.Localization.Loc.T("reg.act_digi_no_id"));

        var result = await digiScaleTickets.DeleteDangTaiAsync(registrationId!, bu.Length > 0 ? bu : null, ct);
        return result.IsSuccess ? new(true, true) : new(false, false, DigiError: result.ErrorMessage ?? "—");
    }

    /// <summary>Đơn ERP đã kéo về DB local theo Shipment / Số TRIP (+ biển số) — chỉ đọc local, không gọi API.</summary>
    public Task<TTC.Weighing.Infrastructure.Adapters.ErpOrderLine?> FindErpLineLocalAsync(string? shipmentOrTrip, string? plate)
        => erpOrderLines.FindInDbAsync(shipmentOrTrip, plate);

    private static bool IsGuid(string? s)
        => Guid.TryParse(s, out var g) && g != Guid.Empty;

    /// <summary>Lưu ID phiếu đăng tài DigiScale vừa nhận (để hủy / xóa sau này).</summary>
    public async Task SaveDigiRegistrationIdAsync(Guid tripId, string digiId, CancellationToken ct = default)
    {
        var trip = await tripRepository.GetByIdAsync(tripId, ct);
        if (trip is null) return;
        await tripRepository.ReloadAsync(trip, ct);
        trip.DigiRegistrationId = digiId;
        await tripRepository.UpdateAsync(trip, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Thông tin để đẩy lại phiếu đăng tài: chuyến đã cân chưa + lệnh đốn LƯU TRÊN CHUYẾN (không lấy theo ô đang gõ).
    /// </summary>
    public async Task<(bool Weighed, string? LenhDonId, string? MaLenhDon)> GetRegistrationPushContextAsync(
        Guid tripId, CancellationToken ct = default)
    {
        var trip = await tripRepository.GetByIdAsync(tripId, ct);
        if (trip is null) return (false, null, null);
        await tripRepository.ReloadAsync(trip, ct);
        var weighed = await tripRepository.HasWeighEventsAsync(tripId, ct);
        return (weighed, trip.FrmLenhDonId, trip.ReferenceTripNo);
    }

    /// <summary>Tìm chuyến vật tư đã dùng Shipment/TRIP ERP (mọi trạng thái trừ đã hủy, kể cả đã cân xong).</summary>
    public async Task<string?> FindActiveTripByErpShipmentAsync(string? shipmentOrTrip, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(shipmentOrTrip)) return null;
        return await tripRepository.FindActiveByReferenceAsync(
            TTC.Weighing.Application.Queries.TripCategoryExtensions.MATERIAL_SOURCE_SYSTEM, shipmentOrTrip, ct);
    }

    /// <summary>
    /// Chuyến của xe CHƯA CÂN RA (chặn đăng tài tiếp). Xe đã cân ra xong (có lượt cân ra) → null:
    /// được đăng tài lệnh đốn khác, kể cả khi trạng thái chuyến cũ chưa kịp chuyển sang Đóng.
    /// </summary>
    public async Task<string?> FindActiveTripByPlateAsync(string plate, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(plate)) return null;
        return await tripRepository.FindNotWeighedOutByPlateAsync(plate, siteContext.CurrentSiteId, ct);
    }

    /// <summary>
    /// Thẻ xe của lệnh đốn trong danh mục thẻ FRM. Tên thẻ trên lệnh đốn dạng "{HĐ vận chuyển}-{xe}-{mã thẻ}"
    /// (ví dụ "2026VC0006-70R-020.10-T260000047") → khớp theo mã thẻ ở cuối. Null nếu không có thẻ nào khớp
    /// (thẻ chưa phát hành / chưa duyệt — ví dụ "2026VC0006-TAI00237" không có mã thẻ T…).
    /// </summary>
    public TTC.Weighing.Infrastructure.Adapters.FrmVehicleInfo? FindCardForLenhDon(FrmLenhDonInfo ld)
        => frmCache.FindCardForLenhDon(ld);

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
