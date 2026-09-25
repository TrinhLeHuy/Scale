using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Application.Abstractions;

/// <summary>
/// Decode QR content, lookup trip, validate token.
/// Dùng chung cho WeighOperationForm, QualityForm, MeasurementSessionForm, GateControlForm.
/// </summary>
public interface IQrScanHandler
{
    /// <summary>
    /// Decode QR content → lookup QrToken → load Trip.
    /// Trả <see cref="QrScanResult.IsSuccess"/> = false nếu QR không hợp lệ,
    /// hết hạn, đã thu hồi, hoặc là QR tạm.
    /// </summary>
    Task<QrScanResult> DecodeAndLookupAsync(
        string qrContent, Guid siteId, CancellationToken ct = default);
}

/// <summary>
/// Kết quả tra cứu QR tại điểm quét. Chứa thông tin chuyến nếu thành công,
/// hoặc mã lỗi cụ thể nếu thất bại.
/// </summary>
public sealed record QrScanResult(
    bool IsSuccess,
    Guid? TripId,
    int? TripVersion,
    string? TripNo,
    string? Plate,
    string? TrailerPlate,
    string? CustomerOrOwner,
    string? Commodity,
    string? Warehouse,
    string? ShipmentNo,
    string? SugarcaneType,
    string? SugarcaneVariety,
    string? SugarcaneOwner,
    Guid? AssignedWeighbridgeId,
    Guid? AssignedWarehouseId,
    QrTokenType? QrType,
    string? ErrorCode,
    string? ErrorMessage);
