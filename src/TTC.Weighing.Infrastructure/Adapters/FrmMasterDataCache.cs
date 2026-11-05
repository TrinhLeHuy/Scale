using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TTC.Weighing.Domain.Common;

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
    string? MaHopDong,            // Mã hợp đồng VC
    // Ngày tạo record (sourceCreatedDate) — dùng để lọc theo khung ngày đăng tài
    string? SourceCreatedDate,
    // ID thẻ xe FRM (the-xe sourceKey) — lệnh đốn trỏ tới thẻ qua field "TheXe"
    string? TheXeSourceKey = null);

/// <summary>
/// Dữ liệu lệnh đốn (harvest-orders) chuẩn hóa từ FRM.
/// </summary>
public sealed record FrmLenhDonInfo(
    string MaLD,           // Mã lệnh đốn (e.g. LD2425-00000863)
    string? SourceKey,     // ID GUID từ FRM (e.g. f164b2cc-77ca-f011-a815-000d3ac7e30c)
    string? BienSoXe,      // Biển số xe
    string? NgayCap,       // Ngày cấp lệnh (lọc khung ngày)
    string? NgayHetHan,    // Ngày hết hạn
    string? StatusCodeName, // Trạng thái (Đã duyệt)
    string? KHName,        // Chủ mía (cá nhân)
    string? KHDNName,      // Khách hàng doanh nghiệp
    string? LoaiMiaName,   // Loại mía (Mía Tơ, Mía Gốc)
    string? GiongMiaName,  // Giống mía
    string? TramName,      // Trạm
    string? VudautuName,   // Vụ đầu tư
    string? LoaixeCode,    // Mã loại xe (100000000=Xe Tải, 100000001=Máy kéo/Container, 100000002=Xe Kéo)
    string? LoaixeName,    // Tên loại xe
    string? UuTien,        // Ưu tiên (từ FRM LenhDon)
    decimal? TaiTrong,     // Tải trọng
    string? DoiTacThuHoachName, // Đối tác thu hoạch
    string? ThuadatcanhtacName, // Thửa đất
    string? Thexename,     // Tên thẻ xe
    string? HopDongDauTuMiaName, // Hợp đồng đầu tư
    string? SourceCreatedDate,  // sourceCreatedDate from API
    string? BusinessUnit,        // BU (TTCS, ATN, ...)
    // Phân loại mía / thẻ xe (FRM LenhDon)
    bool? MiaChay = null,              // Miachay: mía cháy
    string? LoaiMiaChayName = null,    // LoaiMiaChayName: 24h, 36h, ..., >96h
    bool? ThuHoachMay = null,          // ThuHoachMay: true = máy, false = thủ công
    bool? TheXeTam = null,             // TheXeTam: lệnh dùng thẻ xe tạm
    string? SoXeTam = null,            // SoXe: biển số xe tạm (khi TheXeTam, BienSoXe thường rỗng)
    string? LoaiHopDongName = null,    // LoaiHopDongName: HĐ đầu tư mía, HĐ mua bán mía ngoài...
    string? DoiTacBocXepName = null,   // DoiTacBocXepCNName ?? DoiTacBocXepDNName: công bốc xếp
    string? DoiTacThuHoachDNName = null, // DoiTacThuHoachDNName: đối tác thu hoạch doanh nghiệp
    string? TenNhaMay = null,          // TenNhaMay: tên nhà máy của lệnh đốn
    string? TheXeId = null,            // TheXe: ID thẻ xe (= the-xe sourceKey)
    string? TheXeStatusName = null,    // StatusCodeTheXeName: tình trạng thẻ xe trên DigiScale (Đã ký HĐ...)
    string? DigiId = null)             // id bản ghi lệnh đốn trên DigiScale (item.id, vd 01a18362-…) — dùng cho lệnh hủy / xóa
{
    /// <summary>Công thu hoạch: cá nhân, không có thì doanh nghiệp.</summary>
    public string? HarvestPartner => DoiTacThuHoachName.NullIfBlank() ?? DoiTacThuHoachDNName.NullIfBlank();

    /// <summary>
    /// Tên chủ mía hiển thị / in phiếu: chủ mía cá nhân (KHName), không có thì khách hàng doanh nghiệp (KHDNName).
    /// Ví dụ lệnh đốn của công ty "Cty KSK-NT01-1" chỉ có KHDN.
    /// </summary>
    public string? OwnerName => KHName.NullIfBlank() ?? KHDNName.NullIfBlank();

    /// <summary>
    /// Biển số dùng để tra cứu/đăng tài/cân:
    /// - Lệnh thẻ xe tạm có "Số xe tạm" → số xe tạm (xe thực tế chạy bằng thẻ tạm);
    /// - còn lại → "Biển số xe", rỗng thì lấy "Số xe tạm".
    /// </summary>
    public string? VehiclePlate =>
        TheXeTam == true && !string.IsNullOrWhiteSpace(SoXeTam)
            ? SoXeTam.NullIfBlank()
            : !string.IsNullOrWhiteSpace(BienSoXe) ? BienSoXe : SoXeTam.NullIfBlank();

    /// <summary>Lệnh đốn dùng thẻ xe tạm (FRM TheXeTam = true).</summary>
    public bool IsTemporaryCard => TheXeTam == true;

    /// <summary>
    /// Phân loại mía cho phiếu cân:
    /// Mía cháy = FRM Miachay; Mía khúc = thu hoạch máy; Mía cây = thu hoạch thủ công.
    /// Mía cháy độc lập với khúc/cây (mía cháy thu hoạch máy → cháy + khúc).
    /// </summary>
    public (bool IsMiaChay, bool IsMiaCay, bool IsMiaKhuc) SugarcaneForm =>
        (MiaChay == true, ThuHoachMay != true, ThuHoachMay == true);
}

public enum FrmChangeKind { Added, Updated, Removed }

/// <summary>Một bản ghi FRM mới / đổi / mất so với lần kéo trước (Entity: LenhDon, the-xe, xe-van-chuyen, hop-dong-van-chuyen).</summary>
public sealed record FrmPullChange(string Entity, FrmChangeKind Kind, string Code);

/// <summary>Tóm tắt một lần kéo FRM cho nhật ký đồng bộ.</summary>
public sealed record FrmPullSummary(
    DateTimeOffset At,
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyList<FrmPullChange> Changes,
    bool IsFirstPull);

internal static class FrmStringExtensions
{
    public static string? NullIfBlank(this string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

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
    private readonly string? _connectionString;

    /// <summary>Connection string for raw queries (QualityPresenter dùng để tránh EF concurrent access).</summary>
    public string? ConnectionString => _connectionString;

    // Index: normalized plate → list of vehicle info (1 biển số có thể có nhiều thẻ xe)
    private ConcurrentDictionary<string, List<FrmVehicleInfo>> _vehicleIndex = new();

    // Danh sách xe-van-chuyen thô (dùng cho autocomplete)
    private List<FrmVehicleInfo> _allVehicles = [];

    // Danh sách lệnh đốn (harvest-orders)
    private List<FrmLenhDonInfo> _allLenhDon = [];
    /// <summary>
    /// Lệnh đốn của nhà máy đang đăng nhập (BU). Tổng công ty / chưa có BU thấy tất cả.
    /// Lọc cả ở đây để mọi màn hình (Đăng tài, Cân, Tạp chất, CCS, Dữ liệu FRM) không lẫn BU khác.
    /// </summary>
    public IReadOnlyList<FrmLenhDonInfo> AllLenhDon
        => string.IsNullOrWhiteSpace(ActiveBusinessUnit) || IsHeadquarters
            ? _allLenhDon
            : _allLenhDon.Where(ld => ld.BusinessUnit is null
                || string.Equals(ld.BusinessUnit, ActiveBusinessUnit, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>Tìm lệnh đốn theo mã lệnh đốn (vd LD2526-00014630) — Shipment của chuyến mía.</summary>
    public FrmLenhDonInfo? FindLenhDonByCode(string? maLenhDon)
        => string.IsNullOrWhiteSpace(maLenhDon)
            ? null
            : AllLenhDon.FirstOrDefault(ld => string.Equals(ld.MaLD, maLenhDon.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Tìm lệnh đốn theo ID FRM (frm_lenh_don_id lưu trên chuyến lúc đăng tài).</summary>
    public FrmLenhDonInfo? FindLenhDonBySourceKey(string? sourceKey)
        => string.IsNullOrWhiteSpace(sourceKey)
            ? null
            : AllLenhDon.FirstOrDefault(ld => string.Equals(ld.SourceKey, sourceKey, StringComparison.OrdinalIgnoreCase));

    private DateTimeOffset _lastRefreshedAt = DateTimeOffset.MinValue;
    private bool _isLoading;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly List<string> _lastRefreshErrors = [];

    /// <summary>Dấu vân tay của lần kéo đủ gần nhất — giống nhau = DigiScale không có gì mới.</summary>
    private string? _lastFingerprint;

    /// <summary>Kết quả lần kéo gần nhất: số bản ghi theo loại + bản ghi mới / đổi / mất so với lần kéo trước.</summary>
    public FrmPullSummary? LastPull { get; private set; }

    /// <summary>Băm từng bản ghi của lần kéo trước, theo "loại|id".</summary>
    private Dictionary<string, (string Hash, string Code)>? _itemHashes;

    private FrmPullSummary BuildPullSummary(
        params (string Entity, IEnumerable<FrmEntityItem> Items, Func<FrmEntityItem, string?> Code)[] groups)
    {
        var current = new Dictionary<string, (string Hash, string Code)>();
        var counts = new Dictionary<string, int>();
        foreach (var (entity, items, codeOf) in groups)
        {
            var n = 0;
            foreach (var it in items)
            {
                n++;
                var key = $"{entity}|{it.Id ?? it.SourceKey}";
                var hash = $"{it.SourceModifiedDate}|{it.UpdatedAtUtc}|{it.Fields?.GetRawText()}";
                current[key] = (hash, codeOf(it) ?? it.SourceKey ?? it.Id ?? "?");
            }
            counts[entity] = n;
        }

        var previous = _itemHashes;
        var changes = new List<FrmPullChange>();
        if (previous is not null)
        {
            foreach (var (key, (hash, code)) in current)
            {
                var entity = key[..key.IndexOf('|')];
                if (!previous.TryGetValue(key, out var old)) changes.Add(new(entity, FrmChangeKind.Added, code));
                else if (old.Hash != hash) changes.Add(new(entity, FrmChangeKind.Updated, code));
            }
            // Kéo lỗi → không kết luận bản ghi bị mất.
            if (_lastRefreshErrors.Count == 0)
                foreach (var (key, (_, code)) in previous)
                    if (!current.ContainsKey(key))
                        changes.Add(new(key[..key.IndexOf('|')], FrmChangeKind.Removed, code));
        }

        if (_lastRefreshErrors.Count == 0) _itemHashes = current;
        return new FrmPullSummary(VnTime.NowOffset, counts, changes, IsFirstPull: previous is null);
    }

    /// <summary>Băm mọi bản ghi (id + các mốc sửa/đồng bộ + nội dung fields). Đổi bất kỳ trường nào → khác.</summary>
    private static string Fingerprint(params IEnumerable<FrmEntityItem>[] groups)
    {
        using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        foreach (var group in groups)
        {
            foreach (var it in group)
            {
                var line = $"{it.Id}|{it.SourceKey}|{it.SourceModifiedDate}|{it.LastSyncedAtUtc}|{it.UpdatedAtUtc}|{it.Fields?.GetRawText()}\n";
                sha.AppendData(Encoding.UTF8.GetBytes(line));
            }
            sha.AppendData("#"u8);
        }
        return Convert.ToHexString(sha.GetHashAndReset());
    }

    /// <summary>Phát sau mỗi lần kéo xong (dữ liệu trong bộ nhớ đã thay). Gọi từ luồng nền.</summary>
    public event Action? Refreshed;

    /// <summary>Lỗi của lần kéo gần nhất (HTTP lỗi, timeout, dừng giữa chừng). Rỗng = kéo đủ.</summary>
    public IReadOnlyList<string> LastRefreshErrors => _lastRefreshErrors.ToArray();

    /// <summary>BU hiện tại (lấy từ account login, fallback config).</summary>
    public string? ActiveBusinessUnit { get; set; }

    /// <summary>
    /// TTCBH = tổng công ty → thấy toàn bộ dữ liệu, không filter theo BU.
    /// </summary>
    public bool IsHeadquarters =>
        string.Equals(ActiveBusinessUnit, "TTCBH", StringComparison.OrdinalIgnoreCase);

    public FrmMasterDataCache(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<FrmMasterDataCache> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        _connectionString = configuration.GetConnectionString("WeighingDb");
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
        // Đang có lần kéo khác (khởi động / hẹn giờ) → CHỜ xong rồi kéo lại, không bỏ qua im lặng:
        // lần kéo kia có thể bắt đầu trước khi DigiScale cập nhật bản ghi người dùng vừa sửa.
        await _loadLock.WaitAsync(ct);

        try
        {
            _isLoading = true;
            _lastRefreshErrors.Clear();
            _logger.LogInformation("FrmCache: starting refresh from FRM API...");

            // Chưa nạp danh mục đã lưu (refresh chạy trước khi load DB) → nạp trước để có cơ sở gộp ở bước 6.
            if (_allVehicles.Count == 0)
                await LoadFromDbAsync();

            var businessUnit = ActiveBusinessUnit
                ?? _configuration.GetValue<string>("FrmPlanLookup:DefaultBusinessUnit")
                ?? "TTCS";

            // Chỉ kéo BU của account login — mỗi nhà máy chỉ kéo dữ liệu của mình
            var allBUs = new[] { businessUnit };

            var allXeVanChuyen = new List<FrmEntityItem>();
            var allTheXe = new List<FrmEntityItem>();
            var allHdvc = new List<FrmEntityItem>();
            var allLenhDonItems = new List<(FrmEntityItem Item, string BU)>();

            foreach (var bu in allBUs)
            {
                try
                {
                    var xvc = await FetchAllPagesAsync("xe-van-chuyen", bu, ct);
                    allXeVanChuyen.AddRange(xvc);

                    var tx = await FetchAllPagesAsync("the-xe", bu, ct);
                    allTheXe.AddRange(tx);

                    var hd = await FetchAllPagesAsync("hop-dong-van-chuyen", bu, ct);
                    allHdvc.AddRange(hd);

                    var ld = await FetchAllPagesAsync("LenhDon", bu, ct);
                    allLenhDonItems.AddRange(ld.Select(item => (item, bu)));

                    _logger.LogInformation("FrmCache: BU {BU} — XVC:{Xvc} TX:{Tx} HDVC:{Hd} LD:{Ld}",
                        bu, xvc.Count, tx.Count, hd.Count, ld.Count);
                }
                catch (Exception buEx)
                {
                    _logger.LogWarning(buEx, "FrmCache: error fetching BU {BU}", bu);
                    _lastRefreshErrors.Add($"BU {bu}: {buEx.Message}");
                }
            }

            // Lệnh đốn cấp lại trên FRM giữ nguyên ID nhưng đổi mã (vd LD2526-00014604 → LD2526-000146041);
            // DigiScale có thể trả cả bản cũ lẫn bản mới cùng ID → chỉ giữ bản sửa mới nhất mỗi ID FRM,
            // để danh sách không hiện bản cũ (đã hết hạn) như một lệnh đốn khác.
            allLenhDonItems = allLenhDonItems
                .GroupBy(x => (x.BU, Id: FrmFieldsReader.GetString(x.Item.Fields, "ID") ?? x.Item.SourceKey ?? x.Item.Id ?? Guid.NewGuid().ToString()))
                .Select(g => g.OrderByDescending(x => ModifiedAt(x.Item)).First())
                .ToList();

            var xeVanChuyenItems = allXeVanChuyen;
            var theXeItems = allTheXe;
            var hdvcItems = allHdvc;

            // Tóm tắt "kéo được gì / đổi gì" cho nhật ký đồng bộ (màn Dữ liệu FRM).
            LastPull = BuildPullSummary(
                ("LenhDon", allLenhDonItems.Select(x => x.Item), it => FrmFieldsReader.GetString(it.Fields, "MaLD")),
                ("the-xe", allTheXe, it => FrmFieldsReader.GetString(it.Fields, "MaThe") ?? FrmFieldsReader.GetString(it.Fields, "XeVanChuyenName")),
                ("xe-van-chuyen", allXeVanChuyen, it => FrmFieldsReader.GetString(it.Fields, "BienSo")),
                ("hop-dong-van-chuyen", allHdvc, it => FrmFieldsReader.GetString(it.Fields, "MaHopDong")));

            // Kéo liên tục (vài giây/lần): dữ liệu DigiScale không đổi → bỏ qua dựng lại + ghi DB (chỉ cập nhật giờ kéo).
            var fingerprint = Fingerprint(allXeVanChuyen, allTheXe, allHdvc, allLenhDonItems.Select(x => x.Item));
            if (_lastRefreshErrors.Count == 0 && fingerprint == _lastFingerprint && _allVehicles.Count > 0)
            {
                _lastRefreshedAt = DateTimeOffset.UtcNow;
                _logger.LogDebug("FrmCache: no change since last pull — skip rebuild/save");
                return;
            }

            _logger.LogInformation("FrmCache: total — XVC:{Xvc} TX:{Tx} HDVC:{Hd} LD:{Ld}",
                xeVanChuyenItems.Count, theXeItems.Count, hdvcItems.Count, allLenhDonItems.Count);

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
                    MaHopDong: hdvcItem is not null ? FrmFieldsReader.GetString(hdvcItem.Fields, "MaHopDong") : null,
                    SourceCreatedDate: theXe.SourceCreatedDate,
                    TheXeSourceKey: theXe.SourceKey);

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
                    MaHopDong: null,
                    SourceCreatedDate: xeItem.SourceCreatedDate);

                vehicles.Add(info);
                newIndex.TryAdd(key, [info]);
            }

            // 6. Gộp với danh mục đã có (DB local / lần kéo trước) — KHÔNG thay toàn bộ.
            //    API có lúc chỉ trả một phần (ví dụ 38 xe thay vì 8.074) → thay toàn bộ làm mất xe đang có
            //    lệnh đốn hợp lệ, đăng tài báo nhầm "Xe không tồn tại". Biển số API trả về → dùng bản API
            //    (mới nhất); biển số API không trả → giữ bản đã lưu (trạng thái thẻ/xe vẫn được kiểm tra).
            var previous = _allVehicles;
            var kept = 0;
            foreach (var old in previous)
            {
                var key = NormalizePlate(old.BienSo);
                if (string.IsNullOrEmpty(key) || newIndex.ContainsKey(key)) continue;
                vehicles.Add(old);
                kept++;
            }
            foreach (var old in vehicles.Skip(vehicles.Count - kept))
            {
                var key = NormalizePlate(old.BienSo);
                newIndex.AddOrUpdate(key, _ => [old], (_, list) => { list.Add(old); return list; });
            }

            _logger.LogInformation(
                "FrmCache: merged vehicle catalog — API {Api} records, kept {Kept} previously known (plates not returned this time)",
                vehicles.Count - kept, kept);

            // Swap atomically
            _allVehicles = vehicles;
            _vehicleIndex = newIndex;
            _lastRefreshedAt = DateTimeOffset.UtcNow;

            // 7. Parse LenhDon — giữ JSON gốc để lưu DB: lần khởi động sau đọc lại đủ mọi trường như vừa kéo.
            var lenhDonList = new List<FrmLenhDonInfo>();
            var rawLenhDon = new Dictionary<(string, string), string>();
            foreach (var (item, itemBu) in allLenhDonItems)
            {
                var ld = MapLenhDon(item, itemBu);
                if (ld is null) continue;
                lenhDonList.Add(ld);
                rawLenhDon[(ld.MaLD, itemBu)] = JsonSerializer.Serialize(item);
            }

            // Kéo lỗi / dở dang → không được làm mất lệnh đốn đang có: bản API đè lên, bản không trả về giữ nguyên.
            // Kéo đủ → thay toàn bộ bằng dữ liệu DigiScale (mọi thay đổi trạng thái, biển số, thẻ... đều cập nhật).
            if (_lastRefreshErrors.Count > 0)
            {
                var returned = lenhDonList.Select(l => (l.MaLD, l.BusinessUnit ?? "")).ToHashSet();
                lenhDonList.AddRange(_allLenhDon.Where(l => !returned.Contains((l.MaLD, l.BusinessUnit ?? ""))));
            }
            _allLenhDon = lenhDonList;
            _rawLenhDon = rawLenhDon;
            // Kéo lỗi → không thay thẻ/xe trong DB (tránh mất thẻ khi the-xe lỗi mà xe-van-chuyen vẫn về).
            _apiPlates = _lastRefreshErrors.Count > 0 ? [] : vehicles.Take(vehicles.Count - kept)
                .Select(v => v.BienSo).Where(b => !string.IsNullOrWhiteSpace(b))
                .ToHashSet(StringComparer.Ordinal);
            _logger.LogInformation("FrmCache: parsed {Count} LenhDon records", lenhDonList.Count);

            // Lưu xuống DB local để khởi động nhanh lần sau
            await SaveToDbAsync();
            if (_lastRefreshErrors.Count == 0) _lastFingerprint = fingerprint;

            // Báo các màn đang mở (vd Dữ liệu FRM) nạp lại — kể cả khi lần kéo do khởi động / hẹn giờ chạy.
            try { Refreshed?.Invoke(); }
            catch (Exception evEx) { _logger.LogWarning(evEx, "FrmCache: Refreshed handler failed"); }

            _logger.LogInformation(
                "FrmCache: refresh complete. {VehicleCount} vehicles, {PlateCount} unique plates",
                vehicles.Count, newIndex.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FrmCache: refresh failed");
            _lastRefreshErrors.Add(ex.Message);
        }
        finally
        {
            _isLoading = false;
            _loadLock.Release();
        }
    }

    /// <summary>Thời điểm sửa của bản ghi FRM (sourceModifiedDate, rồi Modifieddate trong fields) — để chọn bản mới nhất.</summary>
    private static DateTimeOffset ModifiedAt(FrmEntityItem item)
    {
        foreach (var s in new[] { item.SourceModifiedDate, FrmFieldsReader.GetString(item.Fields, "Modifieddate") })
            if (DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var at))
                return at;
        return DateTimeOffset.MinValue;
    }

    /// <summary>JSON gốc của lệnh đốn vừa kéo, theo (mã LD, BU) — lưu cột raw_json.</summary>
    private Dictionary<(string, string), string> _rawLenhDon = [];

    /// <summary>Biển số (nguyên văn) DigiScale trả về ở lần kéo gần nhất — DB thay toàn bộ thẻ/xe của các biển này.</summary>
    private HashSet<string> _apiPlates = [];

    /// <summary>Lệnh đốn từ một item FRM (dùng cho cả API lẫn JSON gốc lưu DB). Null nếu không có mã LD.</summary>
    private static FrmLenhDonInfo? MapLenhDon(FrmEntityItem item, string? itemBu)
    {
                var maLD = FrmFieldsReader.GetString(item.Fields, "MaLD");
                if (string.IsNullOrWhiteSpace(maLD)) return null;

                return new FrmLenhDonInfo(
                    MaLD: maLD,
                    SourceKey: FrmFieldsReader.GetString(item.Fields, "ID") ?? item.SourceKey,
                    BienSoXe: FrmFieldsReader.GetString(item.Fields, "BienSoXe"),
                    NgayCap: FrmFieldsReader.GetString(item.Fields, "NgayCap"),
                    NgayHetHan: FrmFieldsReader.GetString(item.Fields, "NgayHetHan"),
                    StatusCodeName: FrmFieldsReader.GetString(item.Fields, "StatusCodeName"),
                    KHName: FrmFieldsReader.GetString(item.Fields, "KHName"),
                    KHDNName: FrmFieldsReader.GetString(item.Fields, "KHDNName"),
                    LoaiMiaName: FrmFieldsReader.GetString(item.Fields, "LoaiMiaName"),
                    GiongMiaName: FrmFieldsReader.GetString(item.Fields, "GiongMiaName"),
                    TramName: FrmFieldsReader.GetString(item.Fields, "TramName"),
                    VudautuName: FrmFieldsReader.GetString(item.Fields, "VudautuName"),
                    LoaixeCode: FrmFieldsReader.GetString(item.Fields, "LoaiXe") ?? FrmFieldsReader.GetString(item.Fields, "Loaixe"),
                    LoaixeName: FrmFieldsReader.GetString(item.Fields, "LoaixeName"),
                    UuTien: ParseUuTien(FrmFieldsReader.GetString(item.Fields, "Uutien") ?? FrmFieldsReader.GetString(item.Fields, "UuTien")),
                    TaiTrong: FrmFieldsReader.GetDecimal(item.Fields, "TaiTrong"),
                    DoiTacThuHoachName: FrmFieldsReader.GetString(item.Fields, "DoiTacThuHoachName"),
                    ThuadatcanhtacName: FrmFieldsReader.GetString(item.Fields, "ThuadatcanhtacName"),
                    Thexename: FrmFieldsReader.GetString(item.Fields, "Thexename"),
                    HopDongDauTuMiaName: FrmFieldsReader.GetString(item.Fields, "HopDongDauTuMiaName"),
                    SourceCreatedDate: item.SourceCreatedDate,
                    BusinessUnit: itemBu,
                    MiaChay: FrmFieldsReader.GetBool(item.Fields, "Miachay"),
                    LoaiMiaChayName: FrmFieldsReader.GetString(item.Fields, "LoaiMiaChayName").NullIfBlank(),
                    ThuHoachMay: FrmFieldsReader.GetBool(item.Fields, "ThuHoachMay"),
                    TheXeTam: FrmFieldsReader.GetBool(item.Fields, "TheXeTam"),
                    SoXeTam: FrmFieldsReader.GetString(item.Fields, "SoXe").NullIfBlank(),
                    LoaiHopDongName: FrmFieldsReader.GetString(item.Fields, "LoaiHopDongName").NullIfBlank(),
                    DoiTacBocXepName: FrmFieldsReader.GetString(item.Fields, "DoiTacBocXepCNName").NullIfBlank()
                                      ?? FrmFieldsReader.GetString(item.Fields, "DoiTacBocXepDNName").NullIfBlank(),
                    DoiTacThuHoachDNName: FrmFieldsReader.GetString(item.Fields, "DoiTacThuHoachDNName").NullIfBlank(),
                    TenNhaMay: FrmFieldsReader.GetString(item.Fields, "TenNhaMay").NullIfBlank(),
                    TheXeId: NullIfEmptyGuid(FrmFieldsReader.GetString(item.Fields, "TheXe")),
                    TheXeStatusName: FrmFieldsReader.GetString(item.Fields, "StatusCodeTheXeName").NullIfBlank(),
                    DigiId: item.Id.NullIfBlank());
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
                        _lastRefreshErrors.Add($"{entity} trang {page}: HTTP {(int)response.StatusCode}");
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
                        _lastRefreshErrors.Add($"{entity} trang {page}: timeout");
                        return allItems;
                    }

                    await Task.Delay(2000, ct);
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning(ex,
                        "FrmCache: HTTP error {Entity} page {Page}", entity, page);
                    _lastRefreshErrors.Add($"{entity} trang {page}: {ex.Message}");
                    return allItems;
                }
            }

            var entityResult = apiResponse?.Results?.FirstOrDefault(r =>
                string.Equals(r.Entity, entity, StringComparison.OrdinalIgnoreCase))
                ?? apiResponse?.Results?.FirstOrDefault(); // Fallback: lấy result đầu tiên (API có thể trả tên khác, vd LenhDon → harvest-orders)

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

    /// <summary>
    /// Tra ID bản ghi lệnh đốn trên DigiScale NGAY LÚC GỌI (API LenhDon, không dùng bộ nhớ đệm / DB local).
    /// Khớp theo ID FRM (sourceKey / fields.ID) trước, rồi theo mã lệnh đốn. Lấy bản sửa mới nhất nếu trùng.
    /// Trả (Id, Error): Id null + Error null = DigiScale trả lời nhưng không có lệnh đốn này;
    /// Error khác null = không gọi được DigiScale.
    /// </summary>
    public async Task<(string? Id, string? Error)> FetchLenhDonDigiIdAsync(
        string? frmLenhDonId, string? maLenhDon, string businessUnit, CancellationToken ct = default)
    {
        for (var page = 1; page <= 200; page++)
        {
            var body = new Dictionary<string, object>
            {
                ["entities"] = "LenhDon",
                ["busineesUnit"] = businessUnit,
                ["Date"] = "1900-01-01 00:00:00",
                ["page"] = page,
                ["pageSize"] = 200
            };
            FrmApiResponse? res;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, API_PATH) { Content = JsonContent.Create(body) };
                SetBasicAuth(req);
                using var resp = await _httpClient.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode) return (null, $"HTTP {(int)resp.StatusCode}");
                res = JsonSerializer.Deserialize<FrmApiResponse>(await resp.Content.ReadAsStringAsync(ct));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { return (null, ex.Message); }

            var result = res?.Results?.FirstOrDefault();
            if (result?.Items is not { Count: > 0 } items) return (null, null);

            var match = items
                .Where(it =>
                    (!string.IsNullOrWhiteSpace(frmLenhDonId)
                     && (string.Equals(it.SourceKey, frmLenhDonId, StringComparison.OrdinalIgnoreCase)
                         || string.Equals(FrmFieldsReader.GetString(it.Fields, "ID"), frmLenhDonId, StringComparison.OrdinalIgnoreCase)))
                    || (!string.IsNullOrWhiteSpace(maLenhDon)
                        && string.Equals(FrmFieldsReader.GetString(it.Fields, "MaLD"), maLenhDon, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(ModifiedAt)
                .FirstOrDefault();
            // ID gửi lệnh Delete = ID lệnh đốn FRM mà DigiScale trả về (fields.ID / sourceKey, dạng 4810C0D4-F1D2-…),
            // KHÔNG phải id nội bộ DigiScale (01a1…). Viết HOA theo đúng mẫu FRM.
            var frmId = match is null ? null : (FrmFieldsReader.GetString(match.Fields, "ID") ?? match.SourceKey);
            if (!string.IsNullOrWhiteSpace(frmId)) return (frmId.Trim().ToUpperInvariant(), null);
            if (!result.HasNext) return (null, null);
        }
        return (null, null);
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

    /// <summary>Kiểm tra số lượng records cho 1 entity trên API (không cache). Trả (count, errorDetail).</summary>
    public async Task<(int Count, string? ErrorDetail)> FetchEntityCountDetailAsync(string entity, string? businessUnit, CancellationToken ct = default)
    {
        var bu = businessUnit ?? _configuration.GetValue<string>("FrmPlanLookup:DefaultBusinessUnit") ?? "TTCS";
        var requestBody = new Dictionary<string, object>
        {
            ["entities"] = entity,
            ["busineesUnit"] = bu,
            ["Date"] = "1900-01-01 00:00:00",
            ["page"] = 1,
            ["pageSize"] = 1
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, API_PATH);
        httpRequest.Content = JsonContent.Create(requestBody);
        SetBasicAuth(httpRequest);

        var response = await _httpClient.SendAsync(httpRequest, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "FrmCache: FetchEntityCount HTTP {Status} for entity={Entity}, bu={Bu}. Body={Body}",
                (int)response.StatusCode, entity, bu, body);
            return (-1, $"HTTP {(int)response.StatusCode}: {TruncateBody(body)}");
        }

        var apiResponse = System.Text.Json.JsonSerializer.Deserialize<FrmApiResponse>(body);
        return (apiResponse?.TotalCount ?? 0, null);
    }

    private static string TruncateBody(string body)
        => body.Length <= 200 ? body : body[..200] + "...";

    // ═══════════════════════════════════════════════════════════
    //  DB persistence — lưu/load cache từ PostgreSQL
    // ═══════════════════════════════════════════════════════════

    /// <summary>Load dữ liệu FRM từ DB local khi khởi động (nhanh hơn gọi API).</summary>
    public async Task LoadFromDbAsync()
    {
        if (string.IsNullOrWhiteSpace(_connectionString)) return;

        try
        {
            await using var conn = new Npgsql.NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            // Load vehicles
            var vehicles = new List<FrmVehicleInfo>();
            await using (var cmd = new Npgsql.NpgsqlCommand("SELECT * FROM frm_vehicles", conn))
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    vehicles.Add(new FrmVehicleInfo(
                        BienSo: reader["bien_so"] as string ?? "",
                        MaXe: reader["ma_xe"] as string,
                        LoaiXe: reader["loai_xe"] as string,
                        LoaiXeName: reader["loai_xe_name"] as string,
                        TaiTrong: reader["tai_trong"] is DBNull ? null : (decimal?)reader["tai_trong"],
                        TrongTaiToiDa: reader["trong_tai_toi_da"] is DBNull ? null : (decimal?)reader["trong_tai_toi_da"],
                        MaThe: reader["ma_the"] as string,
                        TramName: reader["tram_name"] as string,
                        HopDongVanChuyenName: reader["hop_dong_van_chuyen_name"] as string,
                        LoaiTheXeName: reader["loai_the_xe_name"] as string,
                        StatusName: reader["status_name"] as string,
                        ThuHoachMay: reader["thu_hoach_may"] is DBNull ? null : (bool?)reader["thu_hoach_may"],
                        NgayApDungTu: reader["ngay_ap_dung_tu"] as string,
                        NgayApDungDen: reader["ngay_ap_dung_den"] as string,
                        DoiTacVanChuyenName: reader["doi_tac_van_chuyen_name"] as string,
                        VuDauTuName: reader["vu_dau_tu_name"] as string,
                        MaHopDong: reader["ma_hop_dong"] as string,
                        SourceCreatedDate: reader["source_created_date"] as string,
                        TheXeSourceKey: ReadString(reader, "the_xe_id")));
                }
            }

            // Load lenh don — chỉ load BU active
            var lenhDon = new List<FrmLenhDonInfo>();
            var lenhDonSql = ActiveBusinessUnit is not null
                ? "SELECT * FROM frm_lenh_don WHERE business_unit = $1"
                : "SELECT * FROM frm_lenh_don";
            await using (var cmd = new Npgsql.NpgsqlCommand(lenhDonSql, conn))
            {
                if (ActiveBusinessUnit is not null)
                    cmd.Parameters.AddWithValue(ActiveBusinessUnit);
                await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    // Có JSON gốc → đọc y như lúc kéo API (đủ mọi trường). Dòng cũ chưa có → đọc theo cột.
                    var rawJson = ReadString(reader, "raw_json");
                    if (!string.IsNullOrWhiteSpace(rawJson))
                    {
                        try
                        {
                            var item = JsonSerializer.Deserialize<FrmEntityItem>(rawJson);
                            var mapped = item is null ? null : MapLenhDon(item, reader["business_unit"] as string);
                            if (mapped is not null) { lenhDon.Add(mapped); continue; }
                        }
                        catch (JsonException) { /* JSON hỏng → đọc theo cột */ }
                    }

                    lenhDon.Add(new FrmLenhDonInfo(
                        MaLD: reader["ma_ld"] as string ?? "",
                        SourceKey: reader["source_key"] as string,
                        BienSoXe: reader["bien_so_xe"] as string,
                        NgayCap: reader["ngay_cap"] as string,
                        NgayHetHan: reader["ngay_het_han"] as string,
                        StatusCodeName: reader["status_code_name"] as string,
                        KHName: reader["kh_name"] as string,
                        KHDNName: HasColumn(reader, "khdn_name") ? reader["khdn_name"] as string : null,
                        LoaiMiaName: reader["loai_mia_name"] as string,
                        GiongMiaName: reader["giong_mia_name"] as string,
                        TramName: reader["tram_name"] as string,
                        VudautuName: reader["vu_dau_tu_name"] as string,
                        LoaixeCode: HasColumn(reader, "loai_xe_code") ? reader["loai_xe_code"] as string : null,
                        LoaixeName: reader["loai_xe_name"] as string,
                        UuTien: HasColumn(reader, "uu_tien") ? reader["uu_tien"] as string : null,
                        TaiTrong: reader["tai_trong"] is DBNull ? null : (decimal?)reader["tai_trong"],
                        DoiTacThuHoachName: reader["doi_tac_thu_hoach_name"] as string,
                        ThuadatcanhtacName: reader["thua_dat_canh_tac_name"] as string,
                        Thexename: reader["the_xe_name"] as string,
                        HopDongDauTuMiaName: reader["hop_dong_dau_tu_mia_name"] as string,
                        SourceCreatedDate: reader["source_created_date"] as string,
                        BusinessUnit: reader["business_unit"] as string,
                        MiaChay: ReadBool(reader, "mia_chay"),
                        LoaiMiaChayName: ReadString(reader, "loai_mia_chay_name"),
                        ThuHoachMay: ReadBool(reader, "thu_hoach_may"),
                        TheXeTam: ReadBool(reader, "the_xe_tam"),
                        SoXeTam: ReadString(reader, "so_xe_tam"),
                        LoaiHopDongName: ReadString(reader, "loai_hop_dong_name"),
                        DoiTacBocXepName: ReadString(reader, "doi_tac_boc_xep_name"),
                        DoiTacThuHoachDNName: ReadString(reader, "doi_tac_thu_hoach_dn_name"),
                        TenNhaMay: ReadString(reader, "ten_nha_may"),
                        TheXeId: ReadString(reader, "the_xe_id"),
                        TheXeStatusName: ReadString(reader, "the_xe_status_name")));
                }
            }
            }

            if (vehicles.Count > 0 || lenhDon.Count > 0)
            {
                // Build index
                var newIndex = new ConcurrentDictionary<string, List<FrmVehicleInfo>>();
                foreach (var v in vehicles)
                {
                    var key = NormalizePlate(v.BienSo);
                    newIndex.AddOrUpdate(key, _ => [v], (_, list) => { list.Add(v); return list; });
                }

                _allVehicles = vehicles;
                _vehicleIndex = newIndex;
                _allLenhDon = lenhDon;
                _lastRefreshedAt = DateTimeOffset.UtcNow;

                _logger.LogInformation(
                    "FrmCache: loaded from DB — {Vehicles} vehicles, {LenhDon} lệnh đốn",
                    vehicles.Count, lenhDon.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FrmCache: failed to load from DB, will fetch from API");
        }
    }

    /// <summary>Lưu cache hiện tại xuống DB local (UPSERT). Trùng key thì đè, không trùng thì thêm. Không xóa data cũ.</summary>
    private async Task SaveToDbAsync()
    {
        if (string.IsNullOrWhiteSpace(_connectionString)) return;
        if (_allVehicles.Count == 0 && _allLenhDon.Count == 0) return;

        try
        {
            await using var conn = new Npgsql.NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            // Đảm bảo unique index tồn tại (idempotent)
            await using (var idx1 = new Npgsql.NpgsqlCommand(
                "CREATE UNIQUE INDEX IF NOT EXISTS uq_frm_vehicles_bien_so_ma_the ON frm_vehicles (bien_so, COALESCE(ma_the, ''))", conn))
                await idx1.ExecuteNonQueryAsync();
            await using (var idx2 = new Npgsql.NpgsqlCommand(
                "CREATE UNIQUE INDEX IF NOT EXISTS uq_frm_lenh_don_ma_ld_bu ON frm_lenh_don (ma_ld, COALESCE(business_unit, ''))", conn))
                await idx2.ExecuteNonQueryAsync();

            // Cột phân loại mía / thẻ xe tạm (thêm sau). Idempotent; quay lui: DROP COLUMN các cột này.
            await using (var alter = new Npgsql.NpgsqlCommand(@"
ALTER TABLE frm_lenh_don
  ADD COLUMN IF NOT EXISTS mia_chay boolean,
  ADD COLUMN IF NOT EXISTS loai_mia_chay_name text,
  ADD COLUMN IF NOT EXISTS thu_hoach_may boolean,
  ADD COLUMN IF NOT EXISTS the_xe_tam boolean,
  ADD COLUMN IF NOT EXISTS so_xe_tam text,
  ADD COLUMN IF NOT EXISTS loai_hop_dong_name text,
  ADD COLUMN IF NOT EXISTS doi_tac_boc_xep_name text,
  ADD COLUMN IF NOT EXISTS doi_tac_thu_hoach_dn_name text,
  ADD COLUMN IF NOT EXISTS ten_nha_may text,
  ADD COLUMN IF NOT EXISTS the_xe_id text,
  ADD COLUMN IF NOT EXISTS the_xe_status_name text,
  ADD COLUMN IF NOT EXISTS raw_json text,
  ADD COLUMN IF NOT EXISTS khdn_name text;
ALTER TABLE frm_vehicles
  ADD COLUMN IF NOT EXISTS the_xe_id text", conn))
                await alter.ExecuteNonQueryAsync();

            // Một transaction: lỗi giữa chừng thì DB giữ nguyên bản cũ, không lưu nửa vời.
            await using var tx = await conn.BeginTransactionAsync();

            // Biển số DigiScale vừa trả về → thay toàn bộ thẻ/xe của biển đó (thẻ đổi mã, bị gỡ, xe có thẻ mới
            // đều phản ánh đúng). Biển không trả về giữ nguyên. Bảng cache từ API, không phải chứng từ nghiệp vụ.
            if (_apiPlates.Count > 0)
            {
                await using var del = new Npgsql.NpgsqlCommand("DELETE FROM frm_vehicles WHERE bien_so = ANY($1)", conn);
                del.Parameters.AddWithValue(_apiPlates.ToArray());
                await del.ExecuteNonQueryAsync();
            }

            // Upsert vehicles
            foreach (var v in _allVehicles)
            {
                await using var cmd = new Npgsql.NpgsqlCommand(@"
INSERT INTO frm_vehicles (bien_so,ma_xe,loai_xe,loai_xe_name,tai_trong,trong_tai_toi_da,ma_the,tram_name,
  hop_dong_van_chuyen_name,loai_the_xe_name,status_name,thu_hoach_may,ngay_ap_dung_tu,ngay_ap_dung_den,
  doi_tac_van_chuyen_name,vu_dau_tu_name,ma_hop_dong,source_created_date,the_xe_id,synced_at)
VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19,now())
ON CONFLICT (bien_so, COALESCE(ma_the, ''))
DO UPDATE SET ma_xe=$2,loai_xe=$3,loai_xe_name=$4,tai_trong=$5,trong_tai_toi_da=$6,
  tram_name=$8,hop_dong_van_chuyen_name=$9,loai_the_xe_name=$10,status_name=$11,thu_hoach_may=$12,
  ngay_ap_dung_tu=$13,ngay_ap_dung_den=$14,doi_tac_van_chuyen_name=$15,vu_dau_tu_name=$16,
  ma_hop_dong=$17,source_created_date=$18,the_xe_id=COALESCE($19,frm_vehicles.the_xe_id),synced_at=now()
", conn);
                cmd.Parameters.AddWithValue(v.BienSo);
                cmd.Parameters.AddWithValue((object?)v.MaXe ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)v.LoaiXe ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)v.LoaiXeName ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)v.TaiTrong ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)v.TrongTaiToiDa ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)v.MaThe ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)v.TramName ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)v.HopDongVanChuyenName ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)v.LoaiTheXeName ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)v.StatusName ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)v.ThuHoachMay ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)v.NgayApDungTu ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)v.NgayApDungDen ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)v.DoiTacVanChuyenName ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)v.VuDauTuName ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)v.MaHopDong ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)v.SourceCreatedDate ?? DBNull.Value);
                cmd.Parameters.Add(new Npgsql.NpgsqlParameter { Value = (object?)v.TheXeSourceKey ?? DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text });
                await cmd.ExecuteNonQueryAsync();
            }

            // Upsert lenh don
            foreach (var ld in _allLenhDon)
            {
                await using var cmd = new Npgsql.NpgsqlCommand(@"
INSERT INTO frm_lenh_don (ma_ld,source_key,bien_so_xe,ngay_cap,ngay_het_han,status_code_name,kh_name,
  loai_mia_name,giong_mia_name,tram_name,vu_dau_tu_name,loai_xe_code,loai_xe_name,uu_tien,tai_trong,doi_tac_thu_hoach_name,
  thua_dat_canh_tac_name,the_xe_name,hop_dong_dau_tu_mia_name,source_created_date,business_unit,
  mia_chay,loai_mia_chay_name,thu_hoach_may,the_xe_tam,so_xe_tam,loai_hop_dong_name,
  doi_tac_boc_xep_name,doi_tac_thu_hoach_dn_name,ten_nha_may,the_xe_id,the_xe_status_name,khdn_name,raw_json,synced_at)
VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19,$20,$21,$22,$23,$24,$25,$26,$27,$28,$29,$30,$31,$32,$33,$34,now())
ON CONFLICT (ma_ld, COALESCE(business_unit, ''))
DO UPDATE SET source_key=$2,bien_so_xe=$3,ngay_cap=$4,ngay_het_han=$5,status_code_name=$6,kh_name=$7,
  loai_mia_name=$8,giong_mia_name=$9,tram_name=$10,vu_dau_tu_name=$11,loai_xe_code=$12,loai_xe_name=$13,uu_tien=$14,tai_trong=$15,
  doi_tac_thu_hoach_name=$16,thua_dat_canh_tac_name=$17,the_xe_name=$18,hop_dong_dau_tu_mia_name=$19,
  source_created_date=$20,mia_chay=$22,loai_mia_chay_name=$23,thu_hoach_may=$24,the_xe_tam=$25,
  so_xe_tam=$26,loai_hop_dong_name=$27,doi_tac_boc_xep_name=$28,doi_tac_thu_hoach_dn_name=$29,ten_nha_may=$30,
  the_xe_id=$31,the_xe_status_name=$32,khdn_name=$33,
  raw_json=COALESCE($34,frm_lenh_don.raw_json),synced_at=now()
", conn);
                cmd.Parameters.AddWithValue(ld.MaLD);
                cmd.Parameters.AddWithValue((object?)ld.SourceKey ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)ld.BienSoXe ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)ld.NgayCap ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)ld.NgayHetHan ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)ld.StatusCodeName ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)ld.KHName ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)ld.LoaiMiaName ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)ld.GiongMiaName ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)ld.TramName ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)ld.VudautuName ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)ld.LoaixeCode ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)ld.LoaixeName ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)ld.UuTien ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)ld.TaiTrong ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)ld.DoiTacThuHoachName ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)ld.ThuadatcanhtacName ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)ld.Thexename ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)ld.HopDongDauTuMiaName ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)ld.SourceCreatedDate ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)ld.BusinessUnit ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)ld.MiaChay ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)ld.LoaiMiaChayName ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)ld.ThuHoachMay ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)ld.TheXeTam ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)ld.SoXeTam ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)ld.LoaiHopDongName ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)ld.DoiTacBocXepName ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)ld.DoiTacThuHoachDNName ?? DBNull.Value);
                cmd.Parameters.AddWithValue((object?)ld.TenNhaMay ?? DBNull.Value);
                cmd.Parameters.Add(new Npgsql.NpgsqlParameter { Value = (object?)ld.TheXeId ?? DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text });
                cmd.Parameters.Add(new Npgsql.NpgsqlParameter { Value = (object?)ld.TheXeStatusName ?? DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text });
                cmd.Parameters.Add(new Npgsql.NpgsqlParameter { Value = (object?)ld.KHDNName ?? DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text });
                var raw = _rawLenhDon.TryGetValue((ld.MaLD, ld.BusinessUnit ?? ""), out var r) ? r : null;
                cmd.Parameters.Add(new Npgsql.NpgsqlParameter { Value = (object?)raw ?? DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text });
                await cmd.ExecuteNonQueryAsync();
            }

            // Bản cũ của lệnh đốn cấp lại (cùng ID FRM, mã khác): frm_lenh_don chỉ là bản sao dữ liệu FRM,
            // bản cũ đã bị FRM thay thế → bỏ để lần khởi động sau không nạp lại. Chỉ khi kéo đủ (không lỗi).
            var current = _allLenhDon.Where(l => !string.IsNullOrWhiteSpace(l.SourceKey)).ToList();
            if (_lastRefreshErrors.Count == 0 && current.Count > 0)
            {
                await using var del = new Npgsql.NpgsqlCommand(@"
DELETE FROM frm_lenh_don f
USING unnest($1::text[], $2::text[], $3::text[]) AS c(source_key, bu, ma_ld)
WHERE f.source_key = c.source_key AND COALESCE(f.business_unit,'') = c.bu AND f.ma_ld <> c.ma_ld", conn);
                del.Parameters.AddWithValue(current.Select(l => l.SourceKey!).ToArray());
                del.Parameters.AddWithValue(current.Select(l => l.BusinessUnit ?? "").ToArray());
                del.Parameters.AddWithValue(current.Select(l => l.MaLD).ToArray());
                var removed = await del.ExecuteNonQueryAsync();
                if (removed > 0)
                    _logger.LogInformation("FrmCache: removed {Count} superseded lệnh đốn versions (same FRM ID, old code)", removed);
            }

            await tx.CommitAsync();

            _logger.LogInformation(
                "FrmCache: upserted to DB — {Vehicles} vehicles, {LenhDon} lệnh đốn",
                _allVehicles.Count, _allLenhDon.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FrmCache: failed to save to DB");
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Seed test data — dữ liệu mẫu khi FRM API không trả records
    // ═══════════════════════════════════════════════════════════

    private static string NormalizePlate(string? plate)
        => plate is null
            ? string.Empty
            : plate.Replace(" ", "").Replace("-", "").Replace(".", "").ToUpperInvariant();

    /// <summary>Parse UuTien từ FRM: boolean (true/false) → "1"/"0", hoặc giữ nguyên nếu đã là string số.</summary>
    private static string? ParseUuTien(string? value)
    {
        if (value is null) return null;
        if (bool.TryParse(value, out var b)) return b ? "1" : "0";
        return value; // Đã là "1"/"0" hoặc giá trị khác
    }

    private static string? NullIfEmptyGuid(string? value)
        => string.IsNullOrWhiteSpace(value) || value.Trim() == "00000000-0000-0000-0000-000000000000"
            ? null : value.Trim();

    /// <summary>
    /// Thẻ xe của lệnh đốn: khớp theo ID thẻ (field TheXe = the-xe sourceKey) — chắc chắn nhất;
    /// không có ID thì theo tên thẻ "{HĐ vận chuyển}-{xe}-{mã thẻ}" (mã thẻ ở cuối).
    /// Tên thẻ trên lệnh đốn có thể không chứa mã thẻ (vd "2026VC0006-TAI00242" trong khi thẻ là T260000121).
    /// </summary>
    public FrmVehicleInfo? FindCardForLenhDon(FrmLenhDonInfo ld)
    {
        var cards = _allVehicles;
        if (ld.TheXeId is { } id)
        {
            var byId = cards.FirstOrDefault(v => string.Equals(v.TheXeSourceKey, id, StringComparison.OrdinalIgnoreCase));
            if (byId is not null) return byId;
        }

        var name = ld.Thexename?.Trim();
        if (string.IsNullOrEmpty(name)) return null;
        return cards.FirstOrDefault(v =>
            !string.IsNullOrWhiteSpace(v.MaThe)
            && (string.Equals(v.MaThe.Trim(), name, StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("-" + v.MaThe.Trim(), StringComparison.OrdinalIgnoreCase)));
    }

    private static string? ReadString(System.Data.IDataReader reader, string columnName)
        => HasColumn(reader, columnName) ? reader[columnName] as string : null;

    private static bool? ReadBool(System.Data.IDataReader reader, string columnName)
        => HasColumn(reader, columnName) && reader[columnName] is bool b ? b : null;

    private static bool HasColumn(System.Data.IDataReader reader, string columnName)
    {
        for (var i = 0; i < reader.FieldCount; i++)
            if (reader.GetName(i).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
