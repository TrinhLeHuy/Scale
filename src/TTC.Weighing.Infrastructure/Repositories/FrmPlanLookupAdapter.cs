using Microsoft.Extensions.Logging;
using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Infrastructure.Adapters;

namespace TTC.Weighing.Infrastructure.Repositories;

/// <summary>
/// Adapter tra cứu kế hoạch FRM dùng cache local (FrmMasterDataCache).
/// Cache kéo toàn bộ the-xe + xe-van-chuyen khi app khởi động,
/// tra cứu theo biển số trong memory — không gọi API mỗi lần.
/// </summary>
public sealed class FrmPlanLookupAdapter(
    FrmMasterDataCache cache,
    ILogger<FrmPlanLookupAdapter> logger) : IFrmPlanLookup
{
    /// <summary>
    /// Tìm xe theo biển số từ cache FRM (the-xe + xe-van-chuyen).
    /// Nếu cache trống (chưa load xong) → trả thông báo "đang tải".
    /// </summary>
    public Task<PlanLookupResult> LookupAsync(
        Guid siteId, string plate, DateTimeOffset operatingDate, CancellationToken ct = default)
    {
        if (cache.VehicleCount == 0)
        {
            logger.LogWarning(
                "FrmPlanLookup: cache is empty (loading={IsLoading}). Plate={Plate}",
                cache.IsLoading, plate);

            var msg = cache.IsLoading
                ? "Đang tải dữ liệu FRM, vui lòng thử lại sau vài giây..."
                : "Dữ liệu FRM chưa được tải. Kiểm tra kết nối hoặc bấm Làm mới FRM.";

            return Task.FromResult(new PlanLookupResult(
                IsSuccess: false,
                Plans: [],
                ErrorMessage: msg));
        }

        var matches = cache.FindByPlate(plate);

        logger.LogInformation(
            "FrmPlanLookup: found {Count} match(es) for plate {Plate} in cache ({Total} vehicles, refreshed {Ago})",
            matches.Count, plate, cache.VehicleCount, cache.LastRefreshedAt);

        if (matches.Count == 0)
        {
            return Task.FromResult(new PlanLookupResult(
                IsSuccess: true,
                Plans: [],
                ErrorMessage: null));
        }

        var plans = matches
            .Where(v => v.MaThe is not null) // Chỉ xe CÓ thẻ (có kế hoạch)
            .Where(v =>
            {
                // Lọc theo ngày vận hành: thẻ phải hiệu lực tại thời điểm tra cứu
                if (v.NgayApDungDen is not null && DateTimeOffset.TryParse(v.NgayApDungDen, out var den)
                    && den < operatingDate)
                    return false; // Thẻ đã hết hạn

                if (v.NgayApDungTu is not null && DateTimeOffset.TryParse(v.NgayApDungTu, out var tu)
                    && tu > operatingDate)
                    return false; // Thẻ chưa bắt đầu

                return true;
            })
            .OrderByDescending(v => v.DoiTacVanChuyenName is not null ? 1 : 0)
            .Select(MapToPlanEntry)
            .ToList();

        // Nếu không có thẻ hiệu lực, fallback thẻ hết hạn gần nhất (để hiện cảnh báo)
        if (plans.Count == 0 && matches.Any(v => v.MaThe is not null))
        {
            plans = matches
                .Where(v => v.MaThe is not null)
                .OrderByDescending(v => v.NgayApDungDen) // Thẻ mới nhất trước
                .Take(1)
                .Select(MapToPlanEntry)
                .ToList();
        }

        // Nếu vẫn không có → fallback xe-van-chuyen
        if (plans.Count == 0 && matches.Count > 0)
        {
            plans = matches.Take(1).Select(MapToPlanEntry).ToList();
        }

        return Task.FromResult(new PlanLookupResult(
            IsSuccess: true,
            Plans: plans,
            ErrorMessage: null));
    }

    /// <summary>
    /// Gửi thông báo hủy đến FRM khi hủy đăng tài.
    /// FRM chưa cung cấp endpoint hủy — trả Acknowledged (stub).
    /// </summary>
    public Task<IntegrationSendResult> NotifyCancellationAsync(
        Guid siteId, Guid tripId, string? referenceTripNo, string reason,
        string correlationId, CancellationToken ct = default)
    {
        logger.LogDebug(
            "FrmPlanLookup.NotifyCancellationAsync: tripId={TripId}, ref={Ref}. FRM chưa có endpoint hủy.",
            tripId, referenceTripNo);

        return Task.FromResult(new IntegrationSendResult(
            IsSuccess: true,
            Status: IntegrationMessageStatus.Acknowledged,
            ErrorMessage: null));
    }

    private static PlanEntry MapToPlanEntry(FrmVehicleInfo v)
    {
        DateTimeOffset? validFrom = null, validTo = null;
        if (v.NgayApDungTu is not null && DateTimeOffset.TryParse(v.NgayApDungTu, out var from))
            validFrom = from;
        if (v.NgayApDungDen is not null && DateTimeOffset.TryParse(v.NgayApDungDen, out var to))
            validTo = to;

        return new PlanEntry(
            Source: "FRM",
            ReferenceNo: v.MaThe ?? v.MaXe ?? v.BienSo,
            CommodityType: v.LoaiTheXeName ?? v.LoaiXeName,
            OwnerOrCustomer: v.DoiTacVanChuyenName,
            WarehouseCode: null,
            SugarcaneType: null,
            SugarcaneVariety: null,
            HarvestMethod: v.ThuHoachMay == true ? "Thu hoạch máy" : "Thu hoạch thủ công",
            Station: v.TramName,
            CropSeason: v.VuDauTuName,
            VehiclePlate: v.BienSo,
            VehicleCode: v.MaXe,
            VehicleTypeName: v.LoaiXeName,
            VehiclePayload: v.TaiTrong,
            VehicleMaxWeight: v.TrongTaiToiDa,
            CardCode: v.MaThe,
            CardStatus: v.StatusName,
            ValidFrom: validFrom,
            ValidTo: validTo,
            ContractCode: v.MaHopDong,
            ContractName: v.HopDongVanChuyenName);
    }
}
