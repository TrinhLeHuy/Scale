using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using TTC.Weighing.Application.Abstractions;

namespace TTC.Weighing.Infrastructure.Adapters;

/// <summary>
/// Gọi FRM API để đồng bộ thông tin chuyến tại thời điểm quét QR cân lần 2.
/// HttpClient được inject qua DI (typed client) với timeout cấu hình từ
/// appsettings → FrmSync:TimeoutSeconds.
/// </summary>
public sealed class FrmSyncAdapter(
    HttpClient httpClient,
    ILogger<FrmSyncAdapter> logger) : IFrmSyncService
{
    /// <summary>
    /// Gọi GET /api/trips/{tripId}/sugarcane-info?siteId={siteId} trên FRM
    /// để lấy thông tin cập nhật chuyến (loại mía, giống, chủ mía, phương pháp thu hoạch, trạm, vụ).
    /// Trả <see cref="FrmSyncResult.IsSuccess"/> = false nếu FRM không phản hồi hoặc lỗi.
    /// </summary>
    public async Task<FrmSyncResult> SyncTripInfoAsync(
        Guid tripId, Guid siteId, CancellationToken ct = default)
    {
        try
        {
            logger.LogDebug(
                "FrmSync: requesting sugarcane info from FRM. TripId={TripId}, SiteId={SiteId}",
                tripId, siteId);

            var response = await httpClient.GetAsync(
                $"api/trips/{tripId}/sugarcane-info?siteId={siteId}", ct);

            response.EnsureSuccessStatusCode();

            var dto = await response.Content
                .ReadFromJsonAsync<FrmSugarcaneInfoDto>(cancellationToken: ct);

            if (dto is null)
            {
                logger.LogWarning(
                    "FrmSync: FRM returned empty body. TripId={TripId}", tripId);

                return new FrmSyncResult(
                    IsSuccess: false,
                    SugarcaneType: null,
                    SugarcaneVariety: null,
                    SugarcaneOwner: null,
                    HarvestMethod: null,
                    Station: null,
                    CropSeason: null,
                    ErrorMessage: "FRM trả về dữ liệu rỗng.");
            }

            logger.LogInformation(
                "FrmSync: synced sugarcane info from FRM. TripId={TripId}, Type={Type}, Owner={Owner}",
                tripId, dto.SugarcaneType, dto.SugarcaneOwner);

            return new FrmSyncResult(
                IsSuccess: true,
                SugarcaneType: dto.SugarcaneType,
                SugarcaneVariety: dto.SugarcaneVariety,
                SugarcaneOwner: dto.SugarcaneOwner,
                HarvestMethod: dto.HarvestMethod,
                Station: dto.Station,
                CropSeason: dto.CropSeason,
                ErrorMessage: null);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex,
                "FrmSync: HTTP error calling FRM. TripId={TripId}, SiteId={SiteId}",
                tripId, siteId);

            return new FrmSyncResult(
                IsSuccess: false,
                SugarcaneType: null,
                SugarcaneVariety: null,
                SugarcaneOwner: null,
                HarvestMethod: null,
                Station: null,
                CropSeason: null,
                ErrorMessage: $"Lỗi kết nối FRM: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "FrmSync: timeout calling FRM. TripId={TripId}, SiteId={SiteId}",
                tripId, siteId);

            return new FrmSyncResult(
                IsSuccess: false,
                SugarcaneType: null,
                SugarcaneVariety: null,
                SugarcaneOwner: null,
                HarvestMethod: null,
                Station: null,
                CropSeason: null,
                ErrorMessage: "FRM không phản hồi trong thời gian cho phép (timeout).");
        }
    }

    /// <summary>DTO nhận từ FRM API — chỉ dùng nội bộ cho deserialization.</summary>
    private sealed record FrmSugarcaneInfoDto(
        string? SugarcaneType,
        string? SugarcaneVariety,
        string? SugarcaneOwner,
        string? HarvestMethod,
        string? Station,
        string? CropSeason);
}
