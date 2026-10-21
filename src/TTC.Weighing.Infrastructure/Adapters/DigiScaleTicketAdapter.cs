using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TTC.Weighing.Infrastructure.Adapters;

/// <summary>
/// Đẩy phiếu lên DigiScale qua POST /api/v1/integration/tickets.
/// Hỗ trợ: phiếu đăng tài, phiếu cân, phiếu tạp chất, phiếu CCS.
/// </summary>
public sealed class DigiScaleTicketAdapter
{
    private const string API_PATH = "api/v1/integration/tickets";

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DigiScaleTicketAdapter> _logger;

    /// <summary>BU hiện tại — set từ bên ngoài (Program.cs / FrmMasterDataCache).</summary>
    public string? ActiveBusinessUnit { get; set; }

    public DigiScaleTicketAdapter(HttpClient httpClient, IConfiguration configuration, ILogger<DigiScaleTicketAdapter> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>Đẩy phiếu đăng tài lên DigiScale. BU lấy từ lệnh đốn.</summary>
    public Task<TicketResult> PushDangTaiAsync(string lenhDonId, string maLenhDon, DateTime ngay, string loaiXe, string soDangTai, string? uuTien = null, string? lenhDonBu = null, CancellationToken ct = default)
        => PushAsync("new_phieudangtai", new { PhieuDangTai = new[] { new { LenhDon = lenhDonId, MaLenhDon = maLenhDon, Ngay = ngay.ToString("M/d/yyyy  h:mm:ss tt", System.Globalization.CultureInfo.InvariantCulture), UuTien = uuTien, LoaiXe = loaiXe, SoDangTai = soDangTai } } }, ct, lenhDonBu);

    /// <summary>Đẩy phiếu cân lên DigiScale. LoaiCan: 100000000=cân vào, 100000001=cân ra. isQuetCS: truyền "Yes" khi cân lần 2 để lấy chính sách.</summary>
    public async Task<TicketResult> PushPhieuCanAsync(string loaiCan, string lanCan, string nvCan, string? userNameNvCan, string lenhDonId, string maLenhDon, string trongLuong, DateTime thoiGian, string soPhieuCan, string? maBanCan, string tlThanhToan, string tcThanhToan, string ccsThanhToan, string? lenhDonBu = null, CancellationToken ct = default, bool isQuetCS = false)
    {
        var result = await PushAsync("new_phieucan", new
        {
            PhieuCan = new[] { new {
                LoaiCan = loaiCan, LanCan = lanCan,
                NhanVienCanVao = nvCan, UserNameNhanVienCanVao = userNameNvCan,
                LenhDon = lenhDonId, MaLenhDon = maLenhDon, MaBanCan = maBanCan,
                TrongLuong = trongLuong,
                ThoiGianCanVao = thoiGian.ToString("M/d/yyyy  h:mm:ss tt", System.Globalization.CultureInfo.InvariantCulture),
                SoPhieuCan = soPhieuCan, Name = "Null",
                TrongLuongThanhToan = tlThanhToan, TapChatThanhToan = tcThanhToan, CCSThanhToan = ccsThanhToan,
                IsQuetCS = isQuetCS ? "Yes" : "No",
                TapChatCS = "0", CCSChinhSach = "0"
            } }
        }, ct, lenhDonBu);

        // Nếu thành công + quét CS → parse response để lấy chính sách
        if (result.IsSuccess && isQuetCS && result.ErrorMessage is not null)
        {
            try
            {
                var parsed = ParsePolicyFromResponse(result);
                // Log kết quả parse
                _logger.LogInformation("DigiScale policy parse: TapChatCS={TC} CCS={CCS} ChinhSach={CS}",
                    parsed.TapChatCS, parsed.CcsChinhSach, parsed.ChinhSachThuMia);
                return parsed;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse policy from DigiScale response");
            }
        }

        return result;
    }

    /// <summary>Parse CCSChinhSach, TapChatCS, ChinhSachThuMia từ response JSON DigiScale.</summary>
    private static TicketResult ParsePolicyFromResponse(TicketResult original)
    {
        var body = original.ErrorMessage ?? "";

        var jsonStart = body.IndexOf('{');
        var jsonEnd = body.LastIndexOf('}');
        if (jsonStart < 0 || jsonEnd <= jsonStart) return original;

        var json = body[jsonStart..(jsonEnd + 1)];

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            decimal? ccsCS = null;
            decimal? tcCS = null;
            string? chinhSach = null;

            // Format 1 (mới): items[0].policy object
            if (root.TryGetProperty("items", out var itemsArr) && itemsArr.GetArrayLength() > 0)
            {
                var item = itemsArr[0];

                // Check error
                if (item.TryGetProperty("error", out var errEl) && errEl.GetString() is string errMsg && !string.IsNullOrWhiteSpace(errMsg))
                {
                    // API trả lỗi chính sách → trả về result với error, không có policy
                    return original with { ErrorMessage = errMsg };
                }

                if (item.TryGetProperty("policy", out var policyEl) && policyEl.ValueKind == JsonValueKind.Object)
                {
                    tcCS = TryGetDecimal(policyEl, "TapChatCS");
                    ccsCS = TryGetDecimal(policyEl, "CCSChinhSach");
                    if (policyEl.TryGetProperty("ChinhSachThuMia", out var csEl))
                        chinhSach = csEl.GetString();
                }
            }

            // Format 2 (cũ/fallback): entityRequest[0].PhieuCan[0]
            if (!tcCS.HasValue && !ccsCS.HasValue
                && root.TryGetProperty("entityRequest", out var erArr) && erArr.GetArrayLength() > 0)
            {
                var er = erArr[0];
                if (er.TryGetProperty("PhieuCan", out var pcArr) && pcArr.GetArrayLength() > 0)
                {
                    var pc = pcArr[0];
                    var t = TryGetDecimal(pc, "TapChatCS");
                    if (t.HasValue && t.Value != 0) tcCS = t;
                    var c = TryGetDecimal(pc, "CCSChinhSach");
                    if (c.HasValue && c.Value != 0) ccsCS = c;
                    if (pc.TryGetProperty("ChinhSachThuMia", out var csEl2))
                    {
                        var s = csEl2.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) chinhSach = s;
                    }
                }
            }

            return original with { CcsChinhSach = ccsCS, TapChatCS = tcCS, ChinhSachThuMia = chinhSach };
        }
        catch
        {
            return original;
        }
    }

    private static decimal? TryGetDecimal(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var val)) return null;
        if (val.ValueKind == JsonValueKind.Number) return val.GetDecimal();
        if (val.ValueKind == JsonValueKind.String && decimal.TryParse(val.GetString(),
            System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d))
            return d;
        return null;
    }

    /// <summary>Đẩy phiếu tạp chất lên DigiScale.</summary>
    public Task<TicketResult> PushTapChatAsync(string lenhDonId, string maLenhDon, string sttCan, string lanDo, DateTime ngay, string klBefore, string klAfter, string tapChatThucTe, string? lenhDonBu = null, CancellationToken ct = default)
        => PushAsync("new_phieudotapchat", new { PhieuDoTapChat = new[] { new { LenhDon = lenhDonId, MaLenhDon = maLenhDon, STTCan = sttCan, LanDo = lanDo, Ngay = ngay.ToString("M/d/yyyy  h:mm:ss tt", System.Globalization.CultureInfo.InvariantCulture), KLBefore = klBefore, KLAfter = klAfter, TapChatThucTe = tapChatThucTe } } }, ct, lenhDonBu);

    /// <summary>Đẩy phiếu CCS lên DigiScale.</summary>
    public Task<TicketResult> PushCcsAsync(string lenhDon, string maLenhDon, string sttCan, string lanDo, DateTime ngay, string brix, string ccsDo, string pol, string polDoc, string caLamViec, string? nhanVienDoCcs = null, string? lenhDonBu = null, CancellationToken ct = default)
        => PushAsync("new_phieudoccs", new { PhieuDoCCS = new[] { new { CCSThanhToan = "0", NhanVienDoCCS = nhanVienDoCcs, Pol = pol, CaLamViec = caLamViec, Ngay = ngay.ToString("M/d/yyyy  h:mm:ss tt", System.Globalization.CultureInfo.InvariantCulture), LenhDon = lenhDon, MaLenhDon = maLenhDon, Polquansat = "0", Poldoc = polDoc, STTCan = sttCan, LanDo = lanDo, Brix = brix, CCSDo = ccsDo } } }, ct, lenhDonBu);

    /// <summary>Đẩy phiếu cân vật tư lên ERP qua DigiScale (oracle-tickets).</summary>
    public async Task<TicketResult> PushOracleTicketAsync(
        string transactionTypeName, string transactionTypeCode, string actionCode,
        string tripNumber, string shipmentNumber,
        decimal weightIn, decimal weightOut, decimal netWeight,
        string weightNumber, string organizationCode, string businessUnit,
        string interfaceReference,
        string? itemNumber = null, string? itemDescription = null, decimal? quantity = null,
        string? transporter = null, string? plateNumber = null, string? driverName = null, string? truckOwner = null,
        string? vendorName = null, string? vendorNumber = null, string? vendorSiteCode = null,
        CancellationToken ct = default)
    {
        // Lines array — bắt buộc ít nhất 1 dòng
        var line = new Dictionary<string, object?>
        {
            ["OrganizationCode"] = organizationCode,
            ["ReferenceNumber"] = shipmentNumber,
            ["ReferenceLineNumber"] = "1.1",
            ["ShipmentLineNumber"] = "1",
            ["ItemNumber"] = itemNumber ?? "",
            ["ItemDescription"] = itemDescription ?? "",
            ["Quantity"] = quantity ?? netWeight,
            ["UnitOfMeasure"] = "Kg"
        };

        var payload = new Dictionary<string, object?>
        {
            ["TransactionTypeName"] = transactionTypeName,
            ["TransactionTypeCode"] = transactionTypeCode,
            ["ActionCodeRequest"] = actionCode,
            ["TripNumber"] = tripNumber,
            ["ShipmentNumber"] = shipmentNumber,
            ["Transporter"] = transporter ?? "",
            ["PlateNumber"] = plateNumber ?? "",
            ["DriverName"] = driverName ?? "",
            ["TruckOwner"] = truckOwner ?? "",
            ["WeightIn"] = weightIn,
            ["WeightOut"] = weightOut,
            ["NetWeight"] = netWeight,
            ["TotalWeightOut"] = weightOut,
            ["TotalNetWeight"] = netWeight,
            ["WeightNumber"] = weightNumber,
            ["OrganizationCode"] = organizationCode,
            ["VendorName"] = vendorName ?? "",
            ["VendorNumber"] = vendorNumber ?? "",
            ["VendorSiteCode"] = vendorSiteCode ?? "",
            ["BusinessUnit"] = businessUnit,
            ["InterfaceReference"] = interfaceReference,
            ["Lines"] = new[] { line }
        };

        try
        {
            var jsonBody = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });

            using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/integration/oracle-tickets");
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            SetBasicAuth(request);

            _logger.LogInformation("DigiScale: POST oracle-tickets Body={Body}", jsonBody);

            var response = await _httpClient.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            _logger.LogInformation("DigiScale: oracle-tickets HTTP {Status} Response={Body}",
                (int)response.StatusCode, responseBody.Length > 500 ? responseBody[..500] : responseBody);

            // HTTP 200 = đã đẩy lên DigiScale thành công — không cần check Oracle sync status
            if (response.IsSuccessStatusCode)
                return new TicketResult(true, $"✅ Đã đẩy lên DigiScale. {Truncate(responseBody)}");

            return new TicketResult(false, $"❌ HTTP {(int)response.StatusCode}: {Truncate(responseBody)}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DigiScale: error pushing oracle-tickets");
            return new TicketResult(false, ex.Message);
        }
    }

    private async Task<TicketResult> PushAsync(string logicalName, object entityData, CancellationToken ct, string? buOverride = null)
    {
        var bu = buOverride
            ?? ActiveBusinessUnit
            ?? _configuration.GetValue<string>("FrmPlanLookup:DefaultBusinessUnit")
            ?? "TTCS";
        var envelope = new
        {
            logicalName,
            busineesUnit = bu,
            status = "Create",
            entityRequest = new[] { entityData }
        };

        try
        {
            var jsonBody = JsonSerializer.Serialize(envelope, new JsonSerializerOptions { WriteIndented = false });

            using var request = new HttpRequestMessage(HttpMethod.Post, API_PATH);
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            SetBasicAuth(request);

            var fullUrl = _httpClient.BaseAddress is not null
                ? new Uri(_httpClient.BaseAddress, API_PATH).ToString()
                : API_PATH;

            _logger.LogInformation("DigiScale POST {Url} Body={Body}", fullUrl, jsonBody);

            // Debug: ghi body ra file
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "last-digiscale-body.json"), jsonBody); } catch { }

            var response = await _httpClient.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            // Debug: ghi response ra file
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, $"last-digiscale-response-{logicalName}.txt"), $"HTTP {(int)response.StatusCode}\n{responseBody}"); } catch { }

            _logger.LogInformation("DigiScale {LogicalName} HTTP {Status} Response={Body}",
                logicalName, (int)response.StatusCode, responseBody.Length > 500 ? responseBody[..500] : responseBody);

            if (response.IsSuccessStatusCode)
            {
                // Check items[0].saved — nếu false thì DigiScale nhận nhưng không lưu
                bool actuallySaved = true;
                string? itemError = null;

                try
                {
                    using var doc = JsonDocument.Parse(responseBody);
                    if (doc.RootElement.TryGetProperty("items", out var items) && items.GetArrayLength() > 0)
                    {
                        var item = items[0];
                        if (item.TryGetProperty("saved", out var savedEl) && !savedEl.GetBoolean())
                        {
                            actuallySaved = false;
                            if (item.TryGetProperty("error", out var errEl))
                                itemError = errEl.GetString();
                        }
                    }
                }
                catch { /* parse fail → coi như saved */ }

                if (actuallySaved)
                    return new TicketResult(true, responseBody);
                else
                    return new TicketResult(false, $"DigiScale không lưu: {itemError ?? "unknown"}");
            }

            return new TicketResult(false, $"HTTP {(int)response.StatusCode}: {(responseBody.Length > 200 ? responseBody[..200] : responseBody)}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DigiScale error pushing {LogicalName}", logicalName);
            return new TicketResult(false, ex.Message);
        }
    }

    private void SetBasicAuth(HttpRequestMessage request)
    {
        var username = _configuration.GetValue<string>("FrmPlanLookup:Username");
        var password = _configuration.GetValue<string>("FrmPlanLookup:Password");
        if (!string.IsNullOrWhiteSpace(username))
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
    }

    private static string Truncate(string s, int max = 300) => s.Length <= max ? s : s[..max] + "...";
}

public sealed record TicketResult(bool IsSuccess, string? ErrorMessage)
{
    /// <summary>CCS chính sách trả về từ DigiScale khi TrQuetCS = Yes.</summary>
    public decimal? CcsChinhSach { get; init; }

    /// <summary>Tạp chất chính sách trả về từ DigiScale khi TrQuetCS = Yes.</summary>
    public decimal? TapChatCS { get; init; }

    /// <summary>Tên chính sách thu mía trả về từ DigiScale.</summary>
    public string? ChinhSachThuMia { get; init; }
}
