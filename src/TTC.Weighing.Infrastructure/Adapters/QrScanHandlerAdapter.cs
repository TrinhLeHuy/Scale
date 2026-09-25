using System.Security.Cryptography;
using System.Text;
using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Infrastructure.Adapters;

/// <summary>
/// Decode QR content → hash token → lookup QrToken → validate → load Trip → build QrScanResult.
/// Dùng chung cho WeighOperationForm, QualityForm, MeasurementSessionForm, GateControlForm.
/// </summary>
public sealed class QrScanHandlerAdapter(
    IQrTokenRepository qrTokens,
    ITripRepository trips,
    IClock clock) : IQrScanHandler
{
    public async Task<QrScanResult> DecodeAndLookupAsync(
        string qrContent, Guid siteId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(qrContent))
            return Fail("QR_INVALID", "Nội dung QR rỗng.");

        // 1. Hash QR content bằng SHA256 → hex string
        var tokenHash = ComputeSha256Hex(qrContent);

        // 2. Lookup QrToken theo hash
        var token = await qrTokens.GetByHashAsync(tokenHash, ct);

        // 2b. Fallback: nếu không tìm thấy QR token, thử tra theo TripNo hoặc biển số.
        //     Hỗ trợ trường hợp nhân viên nhập TripNo (ví dụ TTC-01-202609-000028) thay vì quét QR.
        if (token is null)
        {
            var tripByNo = await trips.GetByTripNoAsync(qrContent.Trim(), siteId, ct);
            if (tripByNo is not null)
                return BuildSuccessFromTrip(tripByNo);

            return Fail("QR_NOT_FOUND", "Không tìm thấy mã QR trong hệ thống.");
        }

        // 3. Validate: đã thu hồi?
        if (token.RevokedAt is not null)
            return Fail("QR_REVOKED", "Mã QR đã bị thu hồi.", token.Type);

        // 4. Validate: hết hạn?
        if (token.ExpiresAt.HasValue && token.ExpiresAt.Value < clock.UtcNow)
            return Fail("QR_EXPIRED", "Mã QR đã hết hạn.", token.Type);

        // 5. QR tạm — cho phép đi tiếp (cân offline), chỉ cảnh báo nhẹ.
        //    Load Trip bình thường để lấy thông tin chuyến.
        if (token.Type == QrTokenType.Temp)
        {
            if (token.TripId is null)
                return Fail("TRIP_NOT_FOUND", "QR tạm không gắn với chuyến nào.", token.Type);

            var tempTrip = await trips.GetByIdAsync(token.TripId.Value, ct);
            if (tempTrip is null)
                return Fail("TRIP_NOT_FOUND", "Chuyến liên kết với QR tạm không tồn tại.", token.Type);

            return new QrScanResult(
                IsSuccess: true,
                TripId: tempTrip.Id,
                TripVersion: tempTrip.Version,
                TripNo: tempTrip.TripNo,
                Plate: tempTrip.TractorPlate,
                TrailerPlate: tempTrip.TrailerPlate,
                CustomerOrOwner: tempTrip.SugarcaneOwner,
                Commodity: tempTrip.SugarcaneType ?? (tempTrip.SourceSystem == "FRM" ? "M�a nh?p" : "V?t t�"),
                Warehouse: null,
                ShipmentNo: tempTrip.ReferenceTripNo,
                SugarcaneType: tempTrip.SugarcaneType,
                SugarcaneVariety: tempTrip.SugarcaneVariety,
                SugarcaneOwner: tempTrip.SugarcaneOwner,
                AssignedWeighbridgeId: tempTrip.AssignedWeighbridgeId,
                AssignedWarehouseId: tempTrip.AssignedWarehouseId,
                QrType: QrTokenType.Temp,
                ErrorCode: null,
                ErrorMessage: null);
        }

        // 6. Load Trip
        if (token.TripId is null)
            return Fail("TRIP_NOT_FOUND", "QR không gắn với chuyến nào.", token.Type);

        var trip = await trips.GetByIdAsync(token.TripId.Value, ct);

        if (trip is null)
            return Fail("TRIP_NOT_FOUND", "Chuyến liên kết với QR không tồn tại.", token.Type);

        // 7. Build QrScanResult thành công
        return new QrScanResult(
            IsSuccess: true,
            TripId: trip.Id,
            TripVersion: trip.Version,
            TripNo: trip.TripNo,
            Plate: trip.TractorPlate,
            TrailerPlate: trip.TrailerPlate,
            CustomerOrOwner: trip.SugarcaneOwner,
            Commodity: trip.SugarcaneType ?? (trip.SourceSystem == "FRM" ? "M�a nh?p" : "V?t t�"),
            Warehouse: null,
            ShipmentNo: trip.ReferenceTripNo,
            SugarcaneType: trip.SugarcaneType,
            SugarcaneVariety: trip.SugarcaneVariety,
            SugarcaneOwner: trip.SugarcaneOwner,
            AssignedWeighbridgeId: trip.AssignedWeighbridgeId,
            AssignedWarehouseId: trip.AssignedWarehouseId,
            QrType: token.Type,
            ErrorCode: null,
            ErrorMessage: null);
    }

    private static QrScanResult BuildSuccessFromTrip(Domain.Entities.Trip trip)
    {
        // Xác định QrType từ QR token hiệu lực gắn với trip (nếu có)
        var activeToken = trip.QrTokens?
            .FirstOrDefault(t => t.RevokedAt is null && t.Status == QrTokenStatus.Issued);
        var qrType = activeToken?.Type ?? QrTokenType.Trip;

        return new QrScanResult(
            IsSuccess: true,
            TripId: trip.Id,
            TripVersion: trip.Version,
            TripNo: trip.TripNo,
            Plate: trip.TractorPlate,
            TrailerPlate: trip.TrailerPlate,
            CustomerOrOwner: trip.SugarcaneOwner,
            Commodity: trip.SugarcaneType ?? (trip.SourceSystem == "FRM" ? "M�a nh?p" : "V?t t�"),
            Warehouse: null,
            ShipmentNo: trip.ReferenceTripNo,
            SugarcaneType: trip.SugarcaneType,
            SugarcaneVariety: trip.SugarcaneVariety,
            SugarcaneOwner: trip.SugarcaneOwner,
            AssignedWeighbridgeId: trip.AssignedWeighbridgeId,
            AssignedWarehouseId: trip.AssignedWarehouseId,
            QrType: qrType,
            ErrorCode: null,
            ErrorMessage: null);
    }

    private static QrScanResult Fail(string errorCode, string errorMessage, QrTokenType? qrType = null) =>
        new(IsSuccess: false,
            TripId: null,
            TripVersion: null,
            TripNo: null,
            Plate: null,
            TrailerPlate: null,
            CustomerOrOwner: null,
            Commodity: null,
            Warehouse: null,
            ShipmentNo: null,
            SugarcaneType: null,
            SugarcaneVariety: null,
            SugarcaneOwner: null,
            AssignedWeighbridgeId: null,
            AssignedWarehouseId: null,
            QrType: qrType,
            ErrorCode: errorCode,
            ErrorMessage: errorMessage);

    private static string ComputeSha256Hex(string input)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hashBytes);
    }
}
