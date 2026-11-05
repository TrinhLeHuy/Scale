using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TTC.Weighing.Domain.Common;

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

    private readonly FrmPolicyAdapter _frmPolicy;

    public DigiScaleTicketAdapter(HttpClient httpClient, IConfiguration configuration, ILogger<DigiScaleTicketAdapter> logger,
        FrmPolicyAdapter frmPolicy)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        _frmPolicy = frmPolicy;
    }

    /// <summary>Đẩy phiếu đăng tài lên DigiScale. BU lấy từ lệnh đốn.</summary>
    public Task<TicketResult> PushDangTaiAsync(string lenhDonId, string maLenhDon, DateTime ngay, string loaiXe, string soDangTai, string? uuTien = null, string? lenhDonBu = null, CancellationToken ct = default)
        => PushAsync("new_phieudangtai", new { PhieuDangTai = new[] { new { LenhDon = lenhDonId, MaLenhDon = maLenhDon, Ngay = ngay.ToString("M/d/yyyy  h:mm:ss tt", System.Globalization.CultureInfo.InvariantCulture), UuTien = uuTien, LoaiXe = loaiXe, SoDangTai = soDangTai } } }, ct, lenhDonBu);

    /// <summary>
    /// Hủy / xóa phiếu đăng tài trên DigiScale — đúng mẫu:
    /// <code>{ "logicalName": "new_phieudangtai", "busineesUnit": "KSK BU", "status": "Delete",
    ///   "entityRequest": [ { "PhieuDangTai": { "phieudangtaiid": "…" } } ] }</code>
    /// <paramref name="phieuDangTaiId"/> = ID bản ghi lệnh đốn trên DigiScale của chuyến (item.id khi kéo LenhDon). Hủy và Xóa dưới app cân đều gửi lệnh này.
    /// </summary>
    public Task<TicketResult> DeleteDangTaiAsync(string phieuDangTaiId, string? lenhDonBu = null, CancellationToken ct = default)
        => PushAsync("new_phieudangtai", new { PhieuDangTai = new { phieudangtaiid = phieuDangTaiId } }, ct, lenhDonBu, action: "Delete");

    /// <summary>ID phiếu DigiScale vừa lưu (items[0].id trong phản hồi tạo phiếu). Null nếu không đọc được.</summary>
    public static string? SavedItemId(TicketResult result)
    {
        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.ErrorMessage)) return null;
        var body = result.ErrorMessage;
        var start = body.IndexOf('{');
        if (start < 0) return null;
        try
        {
            using var doc = JsonDocument.Parse(body[start..]);
            return doc.RootElement.TryGetProperty("items", out var items) && items.GetArrayLength() > 0
                   && items[0].TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                ? id.GetString().NullIfBlank()
                : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Tên trường ghi chú gửi DigiScale. CHƯA có trong tài liệu API (new_phieucan / oracle-tickets) —
    /// đặt theo cách đặt tên của từng payload; cần DigiScale xác nhận. Đổi tên chỉ sửa ở đây.
    /// </summary>
    private const string PHIEU_CAN_NOTE_FIELD = "GhiChu";
    private const string ORACLE_TICKET_NOTE_FIELD = "Note";

    /// <summary>Thêm trường ghi chú vào một bản ghi payload khi có nội dung; rỗng → giữ nguyên (không gửi trường).</summary>
    private static object WithNote(object entry, string field, string? note)
    {
        if (string.IsNullOrWhiteSpace(note)) return entry;
        var node = JsonSerializer.SerializeToNode(entry)!.AsObject();
        node[field] = note.Trim();
        return node;
    }

    /// <summary>Định dạng ThoiGianCanVao của phiếu cân (new_phieucan): "10/28/2026 09:30:26" — 24h, không AM/PM.</summary>
    private const string PHIEU_CAN_TIME_FORMAT = "MM/dd/yyyy HH:mm:ss";

    /// <summary>Đẩy phiếu cân lên DigiScale. LoaiCan: 100000000=cân vào, 100000001=cân ra. isQuetCS: truyền "Yes" khi cân lần 2 để lấy chính sách.</summary>
    /// <param name="tapChatCs">Tạp chất theo chính sách (%) — gửi khi đã có chính sách; null = "0".</param>
    /// <param name="ccsChinhSach">CCS theo chính sách — gửi khi đã có chính sách; null = "0".</param>
    /// <param name="ghiChu">Ghi chú người cân gõ trên màn Cân — gửi kèm phiếu khi có (không gửi rỗng để không xóa ghi chú cũ).</param>
    public async Task<TicketResult> PushPhieuCanAsync(string loaiCan, string lanCan, string nvCan, string? userNameNvCan, string lenhDonId, string maLenhDon, string trongLuong, DateTime thoiGian, string soPhieuCan, string? maBanCan, string tlThanhToan, string tcThanhToan, string ccsThanhToan, string? lenhDonBu = null, CancellationToken ct = default, bool isQuetCS = false, string? tapChatCs = null, string? ccsChinhSach = null, string? ghiChu = null)
    {
        // Có API chính sách FRM (FrmPolicyApi) → bắt chính sách trực tiếp ở FRM (IsQuetCS = Yes),
        // rồi gửi phiếu lần 2 lên DigiScale kèm tạp chất / CCS chính sách (IsQuetCS = No, không bắt lại).
        if (isQuetCS && _frmPolicy.IsEnabled)
            return await PushPhieuCanWithFrmPolicyAsync(loaiCan, lanCan, nvCan, userNameNvCan, lenhDonId, maLenhDon,
                trongLuong, thoiGian, soPhieuCan, maBanCan, tlThanhToan, tcThanhToan, ccsThanhToan, lenhDonBu, ct, ghiChu);

        var result = await PushAsync("new_phieucan", new
        {
            PhieuCan = new[] { WithNote(new {
                LoaiCan = loaiCan, LanCan = lanCan,
                NhanVienCanVao = nvCan, UserNameNhanVienCanVao = userNameNvCan,
                LenhDon = lenhDonId, MaLenhDon = maLenhDon, MaBanCan = maBanCan,
                TrongLuong = trongLuong,
                ThoiGianCanVao = thoiGian.ToString(PHIEU_CAN_TIME_FORMAT, System.Globalization.CultureInfo.InvariantCulture),
                SoPhieuCan = soPhieuCan, Name = "Null",
                TrongLuongThanhToan = tlThanhToan, TapChatThanhToan = tcThanhToan, CCSThanhToan = ccsThanhToan,
                IsQuetCS = isQuetCS ? "Yes" : "No",
                TapChatCS = tapChatCs ?? "0", CCSChinhSach = ccsChinhSach ?? "0"
            }, PHIEU_CAN_NOTE_FIELD, ghiChu) }
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

    /// <summary>Có API chính sách FRM để xem trước chính sách (trước khi lưu cân lần 2).</summary>
    public bool CanPreviewPolicy => _frmPolicy.IsEnabled;

    /// <summary>Chính sách đã lưu của chuyến (theo số phiếu cân).</summary>
    public Task<Integration.TripPolicy?> GetStoredPolicyAsync(string soPhieuCan, CancellationToken ct = default)
        => _frmPolicy.GetStoredAsync(soPhieuCan, ct);

    /// <summary>
    /// Xem trước chính sách FRM cho cân lần 2 (IsQuetCS = Yes) với số cân hiện tại — KHÔNG gửi DigiScale.
    /// FRM ghi nhận phiếu lần 2 tạm với số này; lúc bấm Xác nhận cân lần 2 app gửi lại đúng số chốt.
    /// </summary>
    public Task<FrmPolicyResult> PreviewPolicyAsync(string businessUnit, string soPhieuCan, string lenhDonId, string maLenhDon,
        string? maBanCan, decimal trongLuongTon, string? userNameNvCan, string? nvCan, CancellationToken ct = default)
        => _frmPolicy.CatchPolicyAsync(businessUnit, soPhieuCan, lenhDonId, maLenhDon, maBanCan,
            trongLuongTon, VnTime.Now, userNameNvCan, nvCan, ct, isPreview: true);

    private async Task<TicketResult> PushPhieuCanWithFrmPolicyAsync(string loaiCan, string lanCan, string nvCan, string? userNameNvCan,
        string lenhDonId, string maLenhDon, string trongLuong, DateTime thoiGian, string soPhieuCan, string? maBanCan,
        string tlThanhToan, string tcThanhToan, string ccsThanhToan, string? lenhDonBu, CancellationToken ct, string? ghiChu = null)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var bu = lenhDonBu ?? ActiveBusinessUnit ?? string.Empty;
        _ = decimal.TryParse(trongLuong, System.Globalization.NumberStyles.Any, inv, out var tons);

        var policy = await _frmPolicy.CatchPolicyAsync(bu, soPhieuCan, lenhDonId, maLenhDon, maBanCan,
            tons, thoiGian, userNameNvCan, nvCan, ct);
        if (!policy.HasPolicy)
            return new TicketResult(false, $"POLICY_ERROR: {policy.Error ?? "FRM_POLICY_NOT_FOUND"}");

        var tcText = Num.S2(policy.TapChatCS!.Value);
        var ccsText = Num.S2(policy.CcsChinhSach, "0");
        var pushed = await PushAsync("new_phieucan", new
        {
            PhieuCan = new[] { WithNote(new {
                LoaiCan = loaiCan, LanCan = lanCan,
                NhanVienCanVao = nvCan, UserNameNhanVienCanVao = userNameNvCan,
                LenhDon = lenhDonId, MaLenhDon = maLenhDon, MaBanCan = maBanCan,
                TrongLuong = trongLuong,
                ThoiGianCanVao = thoiGian.ToString(PHIEU_CAN_TIME_FORMAT, inv),
                SoPhieuCan = soPhieuCan, Name = "Null",
                TrongLuongThanhToan = tlThanhToan, TapChatThanhToan = tcThanhToan, CCSThanhToan = ccsThanhToan,
                IsQuetCS = "No",
                TapChatCS = tcText, CCSChinhSach = ccsText
            }, PHIEU_CAN_NOTE_FIELD, ghiChu) }
        }, ct, lenhDonBu);

        // DigiScale lỗi → trả lỗi DigiScale (phiếu chưa được nhận), giữ chính sách để hiển thị lý do.
        return pushed with
        {
            TapChatCS = policy.TapChatCS,
            CcsChinhSach = policy.CcsChinhSach,
            ChinhSachThuMia = policy.ChinhSachThuMua
        };
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

            // Format 1 (mới): items[0].policy — object, hoặc chuỗi JSON kết quả action FRM
            // "BatChinhSachCanDo": {"status":200,"tapchatthanhtoan":x,"ccsthanhtoan":y}
            //                     / {"status":500,"message_error":"..."}.
            if (root.TryGetProperty("items", out var itemsArr) && itemsArr.GetArrayLength() > 0)
            {
                var item = itemsArr[0];

                // Check error
                if (item.TryGetProperty("error", out var errEl) && errEl.ValueKind == JsonValueKind.String
                    && errEl.GetString() is string errMsg && !string.IsNullOrWhiteSpace(errMsg))
                {
                    // API trả lỗi chính sách → trả về result với error, không có policy
                    return original with { IsSuccess = false, ErrorMessage = errMsg };
                }

                if (item.TryGetProperty("policy", out var policyRaw))
                {
                    using var policyDoc = policyRaw.ValueKind == JsonValueKind.String
                        && policyRaw.GetString() is { Length: > 0 } policyJson && policyJson.TrimStart().StartsWith('{')
                            ? JsonDocument.Parse(policyJson)
                            : null;
                    var policyEl = policyDoc?.RootElement ?? policyRaw;

                    if (policyEl.ValueKind == JsonValueKind.Object)
                    {
                        // Action FRM báo lỗi (không tìm thấy hợp đồng / chính sách / thời gian cân ra...)
                        var status = TryGetDecimal(policyEl, "status");
                        if (status is >= 400)
                        {
                            var msg = TryGetString(policyEl, "message_error", "message", "error") ?? "FRM policy error";
                            return original with { IsSuccess = false, ErrorMessage = $"POLICY_ERROR: {msg}" };
                        }

                        tcCS = TryGetDecimal(policyEl, "TapChatCS", "tapchatthanhtoan", "TapChatThanhToan");
                        ccsCS = TryGetDecimal(policyEl, "CCSChinhSach", "ccsthanhtoan", "CCSThanhToan");
                        chinhSach = TryGetString(policyEl, "ChinhSachThuMua", "ChinhSachThuMia", "TenChinhSach");

                        // CCS = 0 nghĩa là chưa có kết quả CCS (action giữ CCS thực tế = 0) — không phải giá trị thật.
                        if (ccsCS == 0m) ccsCS = null;
                    }
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
                    // Tài liệu API FRM: tên trường là "ChinhSachThuMua" (giữ "ChinhSachThuMia" cho bản cũ).
                    if (TryGetString(pc, "ChinhSachThuMua", "ChinhSachThuMia") is { } s)
                        chinhSach = s;
                }
            }

            return original with { CcsChinhSach = ccsCS, TapChatCS = tcCS, ChinhSachThuMia = chinhSach };
        }
        catch
        {
            return original;
        }
    }

    /// <summary>Đọc số theo tên thuộc tính đầu tiên tìm thấy (không phân biệt hoa thường).</summary>
    private static decimal? TryGetDecimal(JsonElement el, params string[] props)
    {
        if (FindProperty(el, props) is not { } val) return null;
        if (val.ValueKind == JsonValueKind.Number) return val.GetDecimal();
        if (val.ValueKind == JsonValueKind.String && decimal.TryParse(val.GetString(),
            System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d))
            return d;
        return null;
    }

    private static string? TryGetString(JsonElement el, params string[] props)
        => FindProperty(el, props) is { ValueKind: JsonValueKind.String } val
           && val.GetString() is { } s && !string.IsNullOrWhiteSpace(s) ? s : null;

    private static JsonElement? FindProperty(JsonElement el, string[] props)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in props)
            foreach (var p in el.EnumerateObject())
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind != JsonValueKind.Null)
                    return p.Value;
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
        CancellationToken ct = default,
        string? organizationName = null,
        string? note = null)
    {
        // Số cân / số lượng gửi ERP: làm tròn 2 chữ số thập phân.
        weightIn = Num.R2(weightIn);
        weightOut = Num.R2(weightOut);
        netWeight = Num.R2(netWeight);
        quantity = Num.R2(quantity);

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
            // Không có nhà vận chuyển → gửi null (không gửi chuỗi rỗng).
            ["Transporter"] = string.IsNullOrWhiteSpace(transporter) ? null : transporter.Trim(),
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
            // Kho (tên tổ chức kho ERP). Không có → null.
            ["OrganizationName"] = string.IsNullOrWhiteSpace(organizationName) ? null : organizationName.Trim(),
            ["VendorName"] = vendorName ?? "",
            ["VendorNumber"] = vendorNumber ?? "",
            ["VendorSiteCode"] = vendorSiteCode ?? "",
            ["BusinessUnit"] = businessUnit,
            ["InterfaceReference"] = interfaceReference,
            ["Lines"] = new[] { line }
        };
        // Ghi chú người cân — chỉ gửi khi có (không gửi rỗng để không xóa ghi chú cũ).
        if (!string.IsNullOrWhiteSpace(note)) payload[ORACLE_TICKET_NOTE_FIELD] = note.Trim();

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
                return new TicketResult(true, $"✅ Đã đẩy lên ERP. {Truncate(responseBody)}");

            return new TicketResult(false, $"❌ HTTP {(int)response.StatusCode}: {Truncate(responseBody)}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DigiScale: error pushing oracle-tickets");
            return new TicketResult(false, ex.Message);
        }
    }

    private async Task<TicketResult> PushAsync(string logicalName, object entityData, CancellationToken ct, string? buOverride = null,
        string action = "Create")
    {
        var bu = buOverride
            ?? ActiveBusinessUnit
            ?? _configuration.GetValue<string>("FrmPlanLookup:DefaultBusinessUnit")
            ?? "TTCS";
        // "status": "Create" (tạo phiếu) hoặc "Delete" (hủy / xóa phiếu đăng tài) — theo mẫu DigiScale.
        var envelope = new { logicalName, busineesUnit = bu, status = action, entityRequest = new[] { entityData } };

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

            // HTTP 4xx (vd 422) vẫn có items[0].error mô tả lỗi nghiệp vụ (NO_POLICY...) —
            // phải lấy đủ câu lỗi, không cắt 200 ký tự đầu (phần đó chỉ là logicalName/organizationUnitId).
            string? businessError = null;
            try
            {
                using var errDoc = JsonDocument.Parse(responseBody);
                if (errDoc.RootElement.TryGetProperty("items", out var errItems) && errItems.GetArrayLength() > 0
                    && errItems[0].TryGetProperty("error", out var errText) && errText.ValueKind == JsonValueKind.String)
                    businessError = errText.GetString();
            }
            catch { /* body không phải JSON */ }

            _logger.LogWarning("DigiScale {LogicalName} rejected HTTP {Status}: {Error}",
                logicalName, (int)response.StatusCode, businessError ?? Truncate(responseBody));

            return new TicketResult(false, businessError is not null
                ? $"HTTP {(int)response.StatusCode}: {businessError}"
                : $"HTTP {(int)response.StatusCode}: {(responseBody.Length > 200 ? responseBody[..200] : responseBody)}");
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

    /// <summary>
    /// DigiScale từ chối vì lệnh đốn chưa có chính sách thu mua (CR-07 NO_POLICY).
    /// Khi đó phiếu cân KHÔNG được lưu bên DigiScale.
    /// </summary>
    public bool IsNoPolicy => ErrorMessage?.Contains("NO_POLICY", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Đã bắt được chính sách. Action FRM luôn trả tạp chất thanh toán khi tìm được chính sách
    /// (có thể = 0 khi miễn trừ); CCS có thể chưa có (null) — không làm mất chính sách.
    /// </summary>
    public bool HasPolicy => IsSuccess && TapChatCS.HasValue;
}
