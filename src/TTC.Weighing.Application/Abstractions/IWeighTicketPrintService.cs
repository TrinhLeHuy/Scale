namespace TTC.Weighing.Application.Abstractions;

/// <summary>
/// In phiếu cân theo mẫu nhà máy. Lỗi in KHÔNG ảnh hưởng dữ liệu cân đã lưu.
/// Hỗ trợ cấu hình mẫu phiếu riêng theo từng nhà máy (mía nhập vs vật tư).
/// </summary>
public interface IWeighTicketPrintService
{
    /// <summary>
    /// In phiếu cân cho chuyến. Khi <paramref name="isReprint"/> = true, phiếu được đánh dấu BẢN IN LẠI
    /// và ghi audit log cho thao tác in lại.
    /// </summary>
    Task<PrintTicketResult> PrintAsync(
        Guid tripId, PrinterConfig config, bool isReprint, CancellationToken ct = default);

    /// <summary>Liệt kê máy in khả dụng kèm trạng thái sẵn sàng.</summary>
    Task<IReadOnlyList<PrinterInfo>> GetAvailablePrintersAsync(CancellationToken ct = default);
}

/// <summary>Kết quả in phiếu cân. IsSuccess = false khi máy in lỗi; dữ liệu cân không bị ảnh hưởng.</summary>
public sealed record PrintTicketResult(
    bool IsSuccess,
    string? TicketNumber,
    string? ErrorMessage);

/// <summary>Cấu hình in: máy in, số bản, mã mẫu phiếu (phân biệt phiếu mía nhập và phiếu vật tư).</summary>
public sealed record PrinterConfig(
    string PrinterName,
    int Copies,
    string TemplateCode);

/// <summary>Thông tin máy in và trạng thái sẵn sàng.</summary>
public sealed record PrinterInfo(string Name, bool IsReady);

/// <summary>
/// Dữ liệu phiếu cân đã tổng hợp, dùng cho template in.
/// Bao gồm thông tin cân vào/ra, trọng lượng hàng, tạp chất, chữ đường.
/// </summary>
public sealed record WeighTicketData(
    string TicketNumber,
    string TripNo,
    string? ReferenceNo,
    string? Plate,
    string? TrailerPlate,
    string? VehicleTypeName,
    string? CustomerOrOwner,
    string? Commodity,
    string? Warehouse,
    string? ShipmentNo,
    decimal? GrossKg,
    DateTimeOffset? GrossTime,
    decimal? TareKg,
    DateTimeOffset? TareTime,
    decimal? NetKg,
    decimal? ImpurityPercent,
    decimal? CcsValue,
    string? SugarcaneType,
    string? SugarcaneOwner,
    string SiteName,
    DateTimeOffset PrintedAt,
    bool IsReprint);
