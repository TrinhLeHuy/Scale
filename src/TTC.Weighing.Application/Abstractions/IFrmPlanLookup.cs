using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Application.Abstractions;

/// <summary>
/// Tra cứu kế hoạch mía (FRM) hoặc vật tư (ERP) theo biển số.
/// Infrastructure implement bằng HTTP client gọi API ngoài.
/// </summary>
public interface IFrmPlanLookup
{
    /// <summary>Tìm kế hoạch khớp biển số trong ngày vận hành.</summary>
    Task<PlanLookupResult> LookupAsync(
        Guid siteId, string plate, DateTimeOffset operatingDate, CancellationToken ct = default);

    /// <summary>Gửi thông báo hủy đến FRM khi hủy đăng tài.</summary>
    Task<IntegrationSendResult> NotifyCancellationAsync(
        Guid siteId, Guid tripId, string? referenceTripNo, string reason,
        string correlationId, CancellationToken ct = default);
}

public sealed record PlanLookupResult(
    bool IsSuccess,
    IReadOnlyList<PlanEntry> Plans,
    string? ErrorMessage);

public sealed record PlanEntry(
    string Source,
    string ReferenceNo,           // Mã thẻ xe (MaThe) hoặc Mã xe
    string? CommodityType,        // Loại thẻ xe (LoaiTheXeName)
    string? OwnerOrCustomer,      // Đối tác vận chuyển (DoiTacVanChuyenName)
    string? WarehouseCode,
    string? SugarcaneType,
    string? SugarcaneVariety,
    string? HarvestMethod,        // Thu hoạch máy / thủ công
    string? Station,              // Trạm (TramName)
    string? CropSeason,           // Vụ đầu tư (VuDauTuName)
    // Thông tin xe
    string? VehiclePlate = null,
    string? VehicleCode = null,        // Mã xe (Maxe)
    string? VehicleTypeName = null,    // Loại xe (LoaixeName)
    decimal? VehiclePayload = null,    // Tải trọng (tấn)
    decimal? VehicleMaxWeight = null,
    string? CardCode = null,           // Mã thẻ xe (MaThe)
    string? CardStatus = null,         // Trạng thái thẻ (StatusName)
    DateTimeOffset? ValidFrom = null,
    DateTimeOffset? ValidTo = null,
    // Thông tin hợp đồng vận chuyển
    string? ContractCode = null,       // Mã hợp đồng (MaHopDong)
    string? ContractName = null);      // Tên HĐVC đầy đủ

public sealed record IntegrationSendResult(
    bool IsSuccess,
    IntegrationMessageStatus Status,
    string? ErrorMessage);
