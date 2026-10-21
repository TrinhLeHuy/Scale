using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Infrastructure.Adapters;

/// <summary>
/// Tra cứu chuyến theo số đăng tài hoặc biển số xe.
/// Chưa có thiết bị quét QR — bỏ qua bước hash/lookup QrToken.
/// Khi có thiết bị QR sau này, thêm lại bước decode QR → hash → lookup QrToken.
/// Dùng chung cho WeighOperationForm, QualityForm, MeasurementSessionForm, GateControlForm.
/// </summary>
public sealed class QrScanHandlerAdapter(
    IQrTokenRepository _qrTokens,
    ITripRepository trips,
    IClock _clock) : IQrScanHandler
{
    public async Task<QrScanResult> DecodeAndLookupAsync(
        string qrContent, Guid siteId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(qrContent))
            return Fail("INPUT_EMPTY", "Vui lòng nhập số đăng tài hoặc biển số xe.");

        var input = qrContent.Trim();

        // 1. Tìm theo số đăng tài (TripNo)
        var tripByNo = await trips.GetByTripNoAsync(input, siteId, ct);
        if (tripByNo is not null)
            return BuildSuccessFromTrip(tripByNo);

        // 2. Tìm chuyến đang trong nhà máy theo biển số xe
        var tripByPlate = await trips.GetActiveByPlateAsync(input, siteId, ct);
        if (tripByPlate is not null)
            return BuildSuccessFromTrip(tripByPlate);

        return Fail("NOT_FOUND", $"Không tìm thấy chuyến nào cho \"{input}\". Kiểm tra lại số đăng tài hoặc biển số.");
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
            Commodity: trip.SugarcaneType ?? (trip.SourceSystem == "FRM" ? "Mía nhập" : "Vật tư"),
            Warehouse: null,
            ShipmentNo: trip.ReferenceTripNo,
            SugarcaneType: trip.SugarcaneType,
            SugarcaneVariety: trip.SugarcaneVariety,
            SugarcaneOwner: trip.SugarcaneOwner,
            AssignedWeighbridgeId: trip.AssignedWeighbridgeId,
            AssignedWarehouseId: trip.AssignedWarehouseId,
            QrType: qrType,
            SourceSystem: trip.SourceSystem,
            DriverRef: trip.DriverRef,
            FrmLenhDonId: trip.FrmLenhDonId,
            FrmMaLenhDon: trip.FrmMaLenhDon,
            FrmLenhDonBu: trip.FrmLenhDonBu,
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
            SourceSystem: null,
            DriverRef: null,
            FrmLenhDonId: null,
            FrmMaLenhDon: null,
            FrmLenhDonBu: null,
            ErrorCode: errorCode,
            ErrorMessage: errorMessage);
}
