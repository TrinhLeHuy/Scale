namespace TTC.Weighing.Application.Abstractions;

/// <summary>
/// Gọi FRM API để lấy thông tin cập nhật của chuyến tại thời điểm quét QR.
/// Dùng cho cân lần 2 (Req 4.1): đồng bộ tại thời điểm quét, không phải lúc lưu.
/// </summary>
public interface IFrmSyncService
{
    /// <summary>
    /// Gọi FRM để đồng bộ thông tin chuyến (loại mía, giống, chủ mía, phương pháp thu hoạch, trạm, vụ).
    /// Trả <see cref="FrmSyncResult.IsSuccess"/> = false nếu FRM không phản hồi hoặc lỗi.
    /// </summary>
    Task<FrmSyncResult> SyncTripInfoAsync(
        Guid tripId, Guid siteId, CancellationToken ct = default);
}

/// <summary>Kết quả đồng bộ thông tin chuyến từ FRM tại thời điểm quét QR cân lần 2.</summary>
public sealed record FrmSyncResult(
    bool IsSuccess,
    string? SugarcaneType,
    string? SugarcaneVariety,
    string? SugarcaneOwner,
    string? HarvestMethod,
    string? Station,
    string? CropSeason,
    string? ErrorMessage);
