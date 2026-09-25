using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.Queries;
using TTC.Weighing.Application.UseCases.Trips;
using TTC.Weighing.Domain.Entities;
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
    TTC.Weighing.Infrastructure.Adapters.FrmMasterDataCache frmCache)
{
    /// <summary>
    /// IWeighTicketPrintService cho PrintHelper dùng (Req 8.3).
    /// PrintHelper cần service để hiển thị dialog chọn máy in rồi gọi PrintAsync.
    /// </summary>
    public IWeighTicketPrintService PrintService => printService;
    /// <summary>SiteId hiện tại từ ISiteContext, Form dùng để binding UI.</summary>
    public Guid SiteId => siteContext.CurrentSiteId;

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
    /// Lấy danh sách biển số xe CÓ THẺ FRM hiệu lực trong khung ngày [from, to].
    /// Chỉ xe có thẻ (có kế hoạch/hợp đồng) mới hiện — xe chỉ trong danh mục không hiện.
    /// </summary>
    public string[] GetPlatesInDateRange(DateTime from, DateTime to)
    {
        return frmCache.AllVehicles
            .Where(v => v.MaThe is not null) // Chỉ xe CÓ thẻ
            .Where(v =>
            {
                DateTimeOffset? apDungTu = null, apDungDen = null;
                if (v.NgayApDungTu is not null && DateTimeOffset.TryParse(v.NgayApDungTu, out var tu))
                    apDungTu = tu;
                if (v.NgayApDungDen is not null && DateTimeOffset.TryParse(v.NgayApDungDen, out var den))
                    apDungDen = den;

                // Không có ngày → hiện (thẻ không ghi thời hạn)
                if (!apDungTu.HasValue && !apDungDen.HasValue)
                    return true;

                // Thẻ đã hết hạn trước khung ngày
                if (apDungDen.HasValue && apDungDen.Value.LocalDateTime < from)
                    return false;

                // Thẻ chưa bắt đầu trong khung ngày
                if (apDungTu.HasValue && apDungTu.Value.LocalDateTime > to)
                    return false;

                return true;
            })
            .Select(v => v.BienSo)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p)
            .ToArray();
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
}
