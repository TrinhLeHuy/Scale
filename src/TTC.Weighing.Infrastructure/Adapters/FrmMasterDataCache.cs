using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TTC.Weighing.Infrastructure.Adapters;

/// <summary>
/// Dữ liệu xe đã chuẩn hóa từ FRM: ghép xe-van-chuyen + the-xe + hop-dong-van-chuyen.
/// </summary>
public sealed record FrmVehicleInfo(
    string BienSo,
    string? MaXe,
    string? LoaiXe,
    string? LoaiXeName,
    decimal? TaiTrong,
    decimal? TrongTaiToiDa,
    // Từ the-xe
    string? MaThe,
    string? TramName,
    string? HopDongVanChuyenName,
    string? LoaiTheXeName,
    string? StatusName,
    bool? ThuHoachMay,
    string? NgayApDungTu,
    string? NgayApDungDen,
    // Từ hop-dong-van-chuyen (ghép qua HopDongVanChuyen ID)
    string? DoiTacVanChuyenName,  // Khách hàng / NCC
    string? VuDauTuName,          // Vụ mùa
    string? MaHopDong);           // Mã hợp đồng VC

/// <summary>
/// Cache dữ liệu master FRM vào bộ nhớ. Kéo toàn bộ xe-van-chuyen + the-xe
/// về memory khi khởi động, index theo biển số (normalized) để tra cứu nhanh.
/// Hỗ trợ refresh thủ công và tự động sau khoảng thời gian cấu hình.
/// </summary>
public sealed class FrmMasterDataCache
{
    private const string API_PATH = "api/v1/integration/frm";

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FrmMasterDataCache> _logger;

    // Index: normalized plate → list of vehicle info (1 biển số có thể có nhiều thẻ xe)
    private ConcurrentDictionary<string, List<FrmVehicleInfo>> _vehicleIndex = new();

    // Danh sách xe-van-chuyen thô (dùng cho autocomplete)
    private List<FrmVehicleInfo> _allVehicles = [];

    private DateTimeOffset _lastRefreshedAt = DateTimeOffset.MinValue;
    private bool _isLoading;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public FrmMasterDataCache(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<FrmMasterDataCache> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>Thời điểm refresh cuối cùng.</summary>
    public DateTimeOffset LastRefreshedAt => _lastRefreshedAt;

    /// <summary>Tổng số xe đã cache.</summary>
    public int VehicleCount => _allVehicles.Count;

    /// <summary>Đang kéo dữ liệu?</summary>
    public bool IsLoading => _isLoading;

    /// <summary>
    /// Tìm xe theo biển số (normalized: bỏ dấu cách, gạch, chấm, upper).
    /// Trả danh sách thẻ xe khớp (1 biển số có thể nhiều thẻ).
    /// </summary>
    public List<FrmVehicleInfo> FindByPlate(string plate)
    {
        var key = NormalizePlate(plate);
        return _vehicleIndex.TryGetValue(key, out var list) ? list : [];
    }

    /// <summary>Toàn bộ xe đã cache — dùng cho autocomplete.</summary>
    public IReadOnlyList<FrmVehicleInfo> AllVehicles => _allVehicles;

    /// <summary>
    /// Kéo toàn bộ xe-van-chuyen và the-xe từ FRM API về cache.
    /// Hỗ trợ phân trang (hasNext = true → gọi tiếp với Date = sourceModifiedDate cuối).
    /// Thread-safe — chỉ 1 lần load cùng lúc.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (!await _loadLock.WaitAsync(0, ct))
        {
            _logger.LogDebug("FrmCache: refresh already in progress, skipping");
            return;
        }

        try
        {
            _isLoading = true;
            _logger.LogInformation("FrmCache: starting refresh from FRM API...");

            var businessUnit = _configuration.GetValue<string>("FrmPlanLookup:DefaultBusinessUnit") ?? "TTCS";

            // 1. Kéo xe-van-chuyen
            var xeVanChuyenItems = await FetchAllPagesAsync("xe-van-chuyen", businessUnit, ct);
            _logger.LogInformation("FrmCache: fetched {Count} xe-van-chuyen items", xeVanChuyenItems.Count);

            // 2. Kéo the-xe
            var theXeItems = await FetchAllPagesAsync("the-xe", businessUnit, ct);
            _logger.LogInformation("FrmCache: fetched {Count} the-xe items", theXeItems.Count);

            // 3. Kéo hop-dong-van-chuyen (có DoiTacVanChuyenName = Khách hàng, VuDauTuName = Vụ mùa)
            var hdvcItems = await FetchAllPagesAsync("hop-dong-van-chuyen", businessUnit, ct);
            _logger.LogInformation("FrmCache: fetched {Count} hop-dong-van-chuyen items", hdvcItems.Count);

            // 4. Build lookup: xe-van-chuyen theo sourceKey
            var xeBySourceKey = new Dictionary<string, FrmEntityItem>();
            foreach (var item in xeVanChuyenItems)
            {
                if (item.SourceKey is not null)
                    xeBySourceKey[item.SourceKey] = item;
            }

            // 5. Build lookup: hop-dong-van-chuyen theo sourceKey
            var hdvcBySourceKey = new Dictionary<string, FrmEntityItem>();
            foreach (var item in hdvcItems)
            {
                if (item.SourceKey is not null)
                    hdvcBySourceKey[item.SourceKey] = item;
            }

            // 6. Build vehicle list: mỗi the-xe ghép với xe-van-chuyen + hop-dong-van-chuyen
            var vehicles = new List<FrmVehicleInfo>();
            var newIndex = new ConcurrentDictionary<string, List<FrmVehicleInfo>>();

            foreach (var theXe in theXeItems)
            {
                var xeVanChuyenId = FrmFieldsReader.GetString(theXe.Fields, "XeVanChuyen");
                var xeVanChuyenName = FrmFieldsReader.GetString(theXe.Fields, "XeVanChuyenName");

                if (string.IsNullOrWhiteSpace(xeVanChuyenName))
                    continue;

                // Ghép xe-van-chuyen
                FrmEntityItem? xeItem = null;
                if (xeVanChuyenId is not null)
                    xeBySourceKey.TryGetValue(xeVanChuyenId, out xeItem);

                // Ghép hop-dong-van-chuyen → lấy Khách hàng (DoiTacVanChuyenName) + Vụ mùa (VuDauTuName)
                var hdvcId = FrmFieldsReader.GetString(theXe.Fields, "HopDongVanChuyen");
                FrmEntityItem? hdvcItem = null;
                if (hdvcId is not null && hdvcId != "00000000-0000-0000-0000-000000000000")
                    hdvcBySourceKey.TryGetValue(hdvcId, out hdvcItem);

                var info = new FrmVehicleInfo(
                    BienSo: xeVanChuyenName,
                    MaXe: xeItem is not null ? FrmFieldsReader.GetString(xeItem.Fields, "Maxe") : null,
                    LoaiXe: xeItem is not null ? FrmFieldsReader.GetString(xeItem.Fields, "LoaiXe") : null,
                    LoaiXeName: xeItem is not null ? FrmFieldsReader.GetString(xeItem.Fields, "LoaixeName") : null,
                    TaiTrong: xeItem is not null ? FrmFieldsReader.GetDecimal(xeItem.Fields, "TaiTrong") : null,
                    TrongTaiToiDa: xeItem is not null ? FrmFieldsReader.GetDecimal(xeItem.Fields, "TrongTaiToiDa") : null,
                    MaThe: FrmFieldsReader.GetString(theXe.Fields, "MaThe"),
                    TramName: FrmFieldsReader.GetString(theXe.Fields, "TramName"),
                    HopDongVanChuyenName: FrmFieldsReader.GetString(theXe.Fields, "HopDongVanChuyenName"),
                    LoaiTheXeName: FrmFieldsReader.GetString(theXe.Fields, "LoaiTheXeName"),
                    StatusName: FrmFieldsReader.GetString(theXe.Fields, "StatusName"),
                    ThuHoachMay: FrmFieldsReader.GetBool(theXe.Fields, "ThuHoachMay"),
                    NgayApDungTu: FrmFieldsReader.GetString(theXe.Fields, "NgayApDungTu"),
                    NgayApDungDen: FrmFieldsReader.GetString(theXe.Fields, "NgayApDungDen"),
                    // Từ hop-dong-van-chuyen
                    DoiTacVanChuyenName: hdvcItem is not null ? FrmFieldsReader.GetString(hdvcItem.Fields, "DoiTacVanChuyenName") : null,
                    VuDauTuName: hdvcItem is not null ? FrmFieldsReader.GetString(hdvcItem.Fields, "VuDauTuName") : null,
                    MaHopDong: hdvcItem is not null ? FrmFieldsReader.GetString(hdvcItem.Fields, "MaHopDong") : null);

                vehicles.Add(info);

                var key = NormalizePlate(xeVanChuyenName);
                newIndex.AddOrUpdate(key,
                    _ => [info],
                    (_, list) => { list.Add(info); return list; });
            }

            // 5. Thêm xe-van-chuyen không có thẻ (vẫn tra cứu được theo biển số)
            foreach (var xeItem in xeVanChuyenItems)
            {
                var bienSo = FrmFieldsReader.GetString(xeItem.Fields, "BienSo");
                if (string.IsNullOrWhiteSpace(bienSo)) continue;

                var key = NormalizePlate(bienSo);
                if (newIndex.ContainsKey(key)) continue; // Đã có từ the-xe

                var info = new FrmVehicleInfo(
                    BienSo: bienSo,
                    MaXe: FrmFieldsReader.GetString(xeItem.Fields, "Maxe"),
                    LoaiXe: FrmFieldsReader.GetString(xeItem.Fields, "LoaiXe"),
                    LoaiXeName: FrmFieldsReader.GetString(xeItem.Fields, "LoaixeName"),
                    TaiTrong: FrmFieldsReader.GetDecimal(xeItem.Fields, "TaiTrong"),
                    TrongTaiToiDa: FrmFieldsReader.GetDecimal(xeItem.Fields, "TrongTaiToiDa"),
                    MaThe: null,
                    TramName: null,
                    HopDongVanChuyenName: null,
                    LoaiTheXeName: null,
                    StatusName: FrmFieldsReader.GetString(xeItem.Fields, "StateCodeName"),
                    ThuHoachMay: null,
                    NgayApDungTu: null,
                    NgayApDungDen: null,
                    DoiTacVanChuyenName: null,
                    VuDauTuName: null,
                    MaHopDong: null);

                vehicles.Add(info);
                newIndex.TryAdd(key, [info]);
            }

            // 6. Swap atomically
            _allVehicles = vehicles;
            _vehicleIndex = newIndex;
            _lastRefreshedAt = DateTimeOffset.UtcNow;

            _logger.LogInformation(
                "FrmCache: refresh complete. {VehicleCount} vehicles, {PlateCount} unique plates",
                vehicles.Count, newIndex.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FrmCache: refresh failed");
        }
        finally
        {
            _isLoading = false;
            _loadLock.Release();
        }
    }

    /// <summary>
    /// Fetch tất cả pages của một entity. API hỗ trợ page/pageSize parameters.
    /// </summary>
    private async Task<List<FrmEntityItem>> FetchAllPagesAsync(
        string entity, string businessUnit, CancellationToken ct)
    {
        var allItems = new List<FrmEntityItem>();

        for (var page = 1; page <= 200; page++)
        {
            var requestBody = new Dictionary<string, object>
            {
                ["entities"] = entity,
                ["busineesUnit"] = businessUnit,
                ["Date"] = "1900-01-01 00:00:00",
                ["page"] = page,
                ["pageSize"] = 200
            };

            FrmApiResponse? apiResponse = null;

            // Retry 3 lần nếu timeout
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var httpRequest = new HttpRequestMessage(HttpMethod.Post, API_PATH);
                    httpRequest.Content = JsonContent.Create(requestBody);
                    SetBasicAuth(httpRequest);

                    var response = await _httpClient.SendAsync(httpRequest, ct);

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning(
                            "FrmCache: HTTP {Status} fetching {Entity} page {Page}",
                            (int)response.StatusCode, entity, page);
                        return allItems; // Trả về những gì đã có
                    }

                    var responseBody = await response.Content.ReadAsStringAsync(ct);
                    apiResponse = System.Text.Json.JsonSerializer.Deserialize<FrmApiResponse>(responseBody);
                    break;
                }
                catch (TaskCanceledException) when (!ct.IsCancellationRequested)
                {
                    _logger.LogWarning(
                        "FrmCache: timeout {Entity} page {Page}, attempt {Attempt}/3",
                        entity, page, attempt);

                    if (attempt == 3)
                    {
                        _logger.LogError(
                            "FrmCache: giving up {Entity} at page {Page}. Got {Count} items.",
                            entity, page, allItems.Count);
                        return allItems;
                    }

                    await Task.Delay(2000, ct);
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning(ex,
                        "FrmCache: HTTP error {Entity} page {Page}", entity, page);
                    return allItems;
                }
            }

            var entityResult = apiResponse?.Results?.FirstOrDefault(r =>
                string.Equals(r.Entity, entity, StringComparison.OrdinalIgnoreCase));

            if (entityResult?.Items is null || entityResult.Items.Count == 0)
                break;

            allItems.AddRange(entityResult.Items);

            _logger.LogDebug(
                "FrmCache: {Entity} page {Page}/{TotalPages} — {Count} items, total: {Total}",
                entity, page, entityResult.TotalPages, entityResult.Items.Count, allItems.Count);

            if (!entityResult.HasNext)
                break;
        }

        return allItems;
    }

    private void SetBasicAuth(HttpRequestMessage request)
    {
        var username = _configuration.GetValue<string>("FrmPlanLookup:Username");
        var password = _configuration.GetValue<string>("FrmPlanLookup:Password");

        if (!string.IsNullOrWhiteSpace(username))
        {
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{username}:{password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
    }

    private static string NormalizePlate(string? plate)
        => plate is null
            ? string.Empty
            : plate.Replace(" ", "").Replace("-", "").Replace(".", "").ToUpperInvariant();
}
