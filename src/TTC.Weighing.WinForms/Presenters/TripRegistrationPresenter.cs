using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.Queries;
using TTC.Weighing.Application.UseCases.Trips;
using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Infrastructure.Adapters;
using TTC.Weighing.WinForms.Forms;

namespace TTC.Weighing.WinForms.Presenters;

/// <summary>
/// Presenter cho UI-02 — Đăng tài. Form gọi Presenter, Presenter gọi Application handlers.
/// Form không trực tiếp gọi Domain hay Infrastructure — đúng pattern MVP.
/// </summary>
public sealed class TripRegistrationPresenter(
    CheckPlanHandler checkPlan,
    RegisterTripHandler registerTrip,
    CancelTripHandler cancelTrip,
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
        RegisterTripCommand cmd, CancellationToken ct = default)
        => registerTrip.HandleAsync(cmd, ct);

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
    /// Tải danh sách chuyến cho lưới (Req 7.1). Tự gắn SiteId từ context.
    /// </summary>
    public Task<IReadOnlyList<TripRegistrationRow>> LoadGridAsync(
        TripRegistrationFilter filter, CancellationToken ct = default)
        => tripQueries.ListAsync(siteContext.CurrentSiteId, filter, ct);

    /// <summary>
    /// Kiểm tra trạng thái camera tại vị trí (In/Out) (Req 10.1).
    /// Dùng GateId = Guid.Empty vì cổng chưa được cấu hình cụ thể ở ISiteContext.
    /// Khi có cấu hình cổng thực tế, sẽ truyền GateId từ form.
    /// </summary>
    public Task<CameraStatus> GetCameraStatusAsync(
        string position, CancellationToken ct = default)
        => camera.GetStatusAsync(Guid.Empty, position, ct);

    /// <summary>
    /// Lấy danh sách biển số unique từ FRM cache cho ComboBox autocomplete.
    /// Trả danh sách biển số đã sắp xếp.
    /// </summary>
    public IReadOnlyList<string> GetAllPlates()
    {
        return frmCache.AllVehicles
            .Where(v => v.MaThe is not null) // Chỉ xe CÓ thẻ (có kế hoạch trong FRM)
            .Select(v => v.BienSo)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p)
            .ToList();
    }

    /// <summary>Tổng số xe trong FRM cache.</summary>
    public int FrmVehicleCount => frmCache.VehicleCount;

    /// <summary>Thống kê cache — bao nhiêu xe có thẻ, bao nhiêu không.</summary>
    public (int withCard, int withoutCard) FrmCacheStats
    {
        get
        {
            var withCard = frmCache.AllVehicles.Count(v => v.MaThe is not null);
            return (withCard, frmCache.VehicleCount - withCard);
        }
    }

    /// <summary>FRM cache đang tải?</summary>
    public bool FrmCacheLoading => frmCache.IsLoading;

    /// <summary>Refresh FRM cache thủ công.</summary>
    public Task RefreshFrmCacheAsync(CancellationToken ct = default)
        => frmCache.RefreshAsync(ct);

    /// <summary>
    /// Lấy danh sách biển số xe từ LenhDon đã duyệt, có NgayCap trong khung ngày [from, to].
    /// Lọc theo BU active của account đăng nhập.
    /// <summary>
    /// Lấy danh sách biển số xe từ LenhDon đã duyệt, có NgayCap trong khung ngày [from, to].
    /// Lọc theo BU active. Loại bỏ biển số đã có chuyến active (chưa hủy/đóng).
    /// </summary>
    public async Task<string[]> GetPlatesInDateRangeAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var activeBu = frmCache.ActiveBusinessUnit;

        var candidates = frmCache.AllLenhDon
            .Where(ld =>
            {
                if (activeBu is not null && !string.Equals(ld.BusinessUnit, activeBu, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (ld.NgayCap is null) return false;
                if (!DateTimeOffset.TryParse(ld.NgayCap, out var ngayCap)) return false;
                return ngayCap.LocalDateTime.Date >= from.Date && ngayCap.LocalDateTime.Date <= to.Date;
            })
            .Where(ld => ld.StatusCodeName == "Đã duyệt")
            .ToList();

        // Loại bỏ lệnh đốn đã có trip active
        var available = new List<string>();
        foreach (var ld in candidates)
        {
            if (string.IsNullOrWhiteSpace(ld.BienSoXe)) continue;
            var existing = await tripRepository.FindActiveByLenhDonIdAsync(ld.SourceKey ?? "", ct);
            if (existing is null)
                available.Add(ld.BienSoXe);
        }

        return available
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p)
            .ToArray();
    }

    /// <summary>Sync version — dùng cho khởi tạo nhanh (không filter active trips).</summary>
    public string[] GetPlatesInDateRange(DateTime from, DateTime to)
    {
        var activeBu = frmCache.ActiveBusinessUnit;

        // Lọc theo NgayCap của LenhDon (ngày cấp lệnh đốn) + BU
        return frmCache.AllLenhDon
            .Where(ld =>
            {
                // Filter theo BU
                if (activeBu is not null && !string.Equals(ld.BusinessUnit, activeBu, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (ld.NgayCap is null) return false;
                if (!DateTimeOffset.TryParse(ld.NgayCap, out var ngayCap)) return false;
                return ngayCap.LocalDateTime.Date >= from.Date && ngayCap.LocalDateTime.Date <= to.Date;
            })
            .Where(ld => ld.StatusCodeName == "Đã duyệt") // Chỉ lệnh đốn đã duyệt
            .Select(ld => ld.BienSoXe)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p)
            .ToArray()!;
    }

    /// <summary>Lấy biển số từ lệnh đốn đã duyệt trong khung ngày — fallback khi thẻ xe rỗng.</summary>
    public string[] GetLenhDonPlatesInDateRange(DateTime from, DateTime to)
    {
        var activeBu = frmCache.ActiveBusinessUnit;
        return frmCache.AllLenhDon
            .Where(ld =>
            {
                if (activeBu is not null && !string.Equals(ld.BusinessUnit, activeBu, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (ld.StatusCodeName != "Đã duyệt") return false;

                // Check NgayCap trong khung ngày
                if (ld.NgayCap is not null && DateTimeOffset.TryParse(ld.NgayCap, out var ngayCap))
                {
                    if (ngayCap.LocalDateTime.Date >= from.Date && ngayCap.LocalDateTime.Date <= to.Date)
                        return true;
                }
                // Fallback: check SourceCreatedDate (ngày tạo từ API) nếu NgayCap ngoài khung
                if (ld.SourceCreatedDate is not null && DateTimeOffset.TryParse(ld.SourceCreatedDate, out var srcDate))
                {
                    if (srcDate.LocalDateTime.Date >= from.Date && srcDate.LocalDateTime.Date <= to.Date)
                        return true;
                }
                return false;
            })
            .Select(ld => ld.BienSoXe)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p)
            .ToArray()!;
    }

    /// <summary>Tìm LenhDon theo biển số xe — lọc theo BU active (TTCBH thấy tất cả).</summary>
    public FrmLenhDonInfo? FindLenhDonByPlate(string plate)
    {
        var normalized = plate.Replace(" ", "").Replace("-", "").Replace(".", "").ToUpperInvariant();
        var activeBu = frmCache.ActiveBusinessUnit;

        return frmCache.AllLenhDon
            .Where(ld => ld.StatusCodeName == "Đã duyệt")
            .Where(ld => activeBu is null || string.Equals(ld.BusinessUnit, activeBu, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(ld => ld.NgayCap) // Mới nhất trước
            .FirstOrDefault(ld =>
            {
                if (ld.BienSoXe is null) return false;
                var ldPlate = ld.BienSoXe.Replace(" ", "").Replace("-", "").Replace(".", "").ToUpperInvariant();
                return ldPlate == normalized;
            });
    }

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
        string businessUnit, string? tripOrShipment, string? plateNumber, CancellationToken ct = default)
    {
        return Task.Run(async () =>
        {
            // 1. Tìm trong DB local trước (đã kéo về từ form ERP)1
            var dbItem = await erpOrderLines.FindInDbAsync(tripOrShipment, plateNumber);
            if (dbItem is not null) return dbItem;

            // 2. Fallback: gọi API tìm theo shipmentNumber
            var from = DateTime.Today.AddDays(-90);
            var to = DateTime.Today.AddDays(1);

            var response = await erpOrderLines.GetOrderLinesAsync(
                businessUnit, from, to,
                shipmentNumber: tripOrShipment,
                pageSize: 1, ct: ct);

            var item = response?.Items?.FirstOrDefault();
            if (item is not null) return item;

            // Fallback: tìm theo tripNumber
            if (!string.IsNullOrWhiteSpace(tripOrShipment))
            {
                response = await erpOrderLines.GetOrderLinesAsync(
                    businessUnit, from, to,
                    tripNumber: tripOrShipment,
                    pageSize: 1, ct: ct);
                item = response?.Items?.FirstOrDefault();
                if (item is not null) return item;
            }

            // Fallback: tìm theo biển số (nếu có)
            if (!string.IsNullOrWhiteSpace(plateNumber))
            {
                response = await erpOrderLines.GetOrderLinesAsync(
                    businessUnit, from, to,
                    plateNumber: plateNumber,
                    pageSize: 1, ct: ct);
                return response?.Items?.FirstOrDefault();
            }

            return null;
        }, ct);
    }

    /// <summary>Tìm chuyến active (chưa Closed/Cancelled) theo lệnh đốn GUID.</summary>
    public async Task<string?> FindActiveTripByLenhDonAsync(string? lenhDonId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(lenhDonId)) return null;
        var trips = await tripRepository.FindActiveByLenhDonIdAsync(lenhDonId, ct);
        return trips;
    }

    /// <summary>Tìm chuyến active theo biển số (xe chưa cân ra).</summary>
    public async Task<string?> FindActiveTripByPlateAsync(string plate, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(plate)) return null;
        var trip = await tripRepository.GetActiveByPlateAsync(plate, siteContext.CurrentSiteId, ct);
        return trip?.TripNo;
    }

    /// <summary>Tìm xe trong FRM cache theo biển số.</summary>
    public List<TTC.Weighing.Infrastructure.Adapters.FrmVehicleInfo> FindVehicleByPlate(string plate)
        => frmCache.FindByPlate(plate);
}
