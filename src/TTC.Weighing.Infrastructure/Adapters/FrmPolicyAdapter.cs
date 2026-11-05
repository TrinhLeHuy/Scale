using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TTC.Weighing.Infrastructure.Adapters;

/// <summary>
/// Kéo chính sách thu mua trực tiếp từ FRM — API PhieuCan (POST {BaseUrl}/api/crm/create),
/// gửi phiếu cân lần 2 với IsQuetCS = "Yes".
/// Header: Accept application/vnd.bms+json; version=1.0, Authorization Basic (user/password cấu hình).
/// Response: entityRequest[0].PhieuCan[0] → CCSChinhSach, ChinhSachThuMua, TapChatCS.
/// Cấu hình "FrmPolicyApi" (BaseUrl, Username, Password, Enabled) — mật khẩu đặt ở appsettings.Development.json
/// hoặc biến môi trường FrmPolicyApi__Password, KHÔNG để trong code / file commit.
/// </summary>
public sealed class FrmPolicyAdapter
{
    private const string API_PATH = "api/crm/create";
    private const string ACCEPT = "application/vnd.bms+json; version=1.0";

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FrmPolicyAdapter> _logger;
    private readonly Integration.TripPolicyStore _store;

    public FrmPolicyAdapter(HttpClient httpClient, IConfiguration configuration, ILogger<FrmPolicyAdapter> logger,
        Integration.TripPolicyStore store)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        _store = store;
    }

    /// <summary>Chính sách đã lưu của chuyến (theo số phiếu cân) — dùng hiển thị lại sau khi cân.</summary>
    public Task<Integration.TripPolicy?> GetStoredAsync(string soPhieuCan, CancellationToken ct = default)
        => _store.GetAsync(soPhieuCan, ct);

    /// <summary>Bật khi có BaseUrl + Username và Enabled != false.</summary>
    public bool IsEnabled
        => _configuration.GetValue<bool?>("FrmPolicyApi:Enabled") != false
           && _httpClient.BaseAddress is not null
           && !string.IsNullOrWhiteSpace(_configuration.GetValue<string>("FrmPolicyApi:Username"));

    /// <summary>
    /// Gọi FRM bắt chính sách cho phiếu cân lần 2. Trọng lượng gửi theo TẤN (giống phiếu cân DigiScale).
    /// </summary>
    public async Task<FrmPolicyResult> CatchPolicyAsync(
        string businessUnit, string soPhieuCan, string lenhDonId, string maLenhDon, string? maBanCan,
        decimal trongLuongTon, DateTime thoiGianCan, string? userNameNhanVien, string? nhanVien,
        CancellationToken ct = default, bool isPreview = false)
    {
        if (!IsEnabled) return FrmPolicyResult.Fail("FRM_POLICY_API_NOT_CONFIGURED");

        var payload = new
        {
            logicalName = "new_phieucan",
            busineesUnit = businessUnit,            // đúng chính tả theo tài liệu API FRM
            status = "Create",
            entityRequest = new[]
            {
                new
                {
                    PhieuCan = new[]
                    {
                        new
                        {
                            LoaiCan = "100000001",
                            SoPhieuCan = soPhieuCan,
                            LanCan = 2,
                            LenhDon = lenhDonId,
                            MaLenhDon = maLenhDon,
                            MaBanCan = maBanCan,
                            TrongLuong = Domain.Common.Num.R2(trongLuongTon), // làm tròn 2 chữ số như mọi số gửi đi
                            TrongLuongThanhToan = 0,
                            ThoiGianCanVao = thoiGianCan.ToString("MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                            TapChatThanhToan = 0,
                            CCSThanhToan = 0,
                            IsQuetCS = "Yes",
                            TapChatCS = 0,
                            CCSChinhSach = 0,
                            UserNameNhanVienCanVao = userNameNhanVien,
                            NhanVienCanVao = nhanVien
                        }
                    }
                }
            }
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, API_PATH)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            request.Headers.Accept.Clear();
            request.Headers.TryAddWithoutValidation("Accept", ACCEPT);
            var user = _configuration.GetValue<string>("FrmPolicyApi:Username");
            var pass = _configuration.GetValue<string>("FrmPolicyApi:Password");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}")));

            using var response = await _httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            // Debug: giữ phản hồi gần nhất để đối chiếu (không chứa mật khẩu).
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "last-frm-policy-response.txt"), $"HTTP {(int)response.StatusCode}\n{body}"); } catch { }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("FRM policy HTTP {Status} LD={MaLD} phieu={So}: {Body}",
                    (int)response.StatusCode, maLenhDon, soPhieuCan, body.Length > 500 ? body[..500] : body);
                return FrmPolicyResult.Fail(ExtractError(body) ?? $"HTTP {(int)response.StatusCode}");
            }

            var parsed = Parse(body);
            await _store.SaveAsync(soPhieuCan, maLenhDon, parsed, trongLuongTon, isPreview, ct);
            _logger.LogInformation("FRM policy LD={MaLD} phieu={So}: TC={TC} CCS={CCS} CS={CS} ok={Ok}",
                maLenhDon, soPhieuCan, parsed.TapChatCS, parsed.CcsChinhSach, parsed.ChinhSachThuMua, parsed.HasPolicy);
            return parsed;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FRM policy call failed LD={MaLD} phieu={So}", maLenhDon, soPhieuCan);
            return FrmPolicyResult.Fail(ex.Message);
        }
    }

    /// <summary>Đọc 3 trường chính sách từ entityRequest[0].PhieuCan[0].</summary>
    public static FrmPolicyResult Parse(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("entityRequest", out var er) && er.ValueKind == JsonValueKind.Array && er.GetArrayLength() > 0
                && er[0].TryGetProperty("PhieuCan", out var pcArr) && pcArr.ValueKind == JsonValueKind.Array && pcArr.GetArrayLength() > 0)
            {
                var pc = pcArr[0];
                var tc = Dec(pc, "TapChatCS");
                var ccs = Dec(pc, "CCSChinhSach");
                var name = Str(pc, "ChinhSachThuMua");
                if (tc is not null || ccs is not null || name is not null)
                    return new FrmPolicyResult(true, tc, ccs == 0m ? null : ccs, name, Str(root, "Success"), null);
            }

            return FrmPolicyResult.Fail(ExtractError(body) ?? "FRM_POLICY_NOT_FOUND");
        }
        catch (JsonException)
        {
            return FrmPolicyResult.Fail(body.Length > 300 ? body[..300] : body);
        }
    }

    private static string? ExtractError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return Str(doc.RootElement, "Error", "error", "message", "Message", "message_error");
        }
        catch { return null; }
    }

    private static decimal? Dec(JsonElement el, string name)
    {
        foreach (var p in el.EnumerateObject())
        {
            if (!string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            if (p.Value.ValueKind == JsonValueKind.Number) return p.Value.GetDecimal();
            if (p.Value.ValueKind == JsonValueKind.String
                && decimal.TryParse(p.Value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d;
        }
        return null;
    }

    private static string? Str(JsonElement el, params string[] names)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in names)
            foreach (var p in el.EnumerateObject())
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)
                    && p.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.Value.GetString()))
                    return p.Value.GetString();
        return null;
    }
}

/// <summary>Kết quả bắt chính sách FRM. HasPolicy = có tạp chất chính sách (CCS có thể chưa có).</summary>
public sealed record FrmPolicyResult(
    bool IsSuccess, decimal? TapChatCS, decimal? CcsChinhSach, string? ChinhSachThuMua, string? Message, string? Error)
{
    public bool HasPolicy => IsSuccess && TapChatCS.HasValue;
    public static FrmPolicyResult Fail(string error) => new(false, null, null, null, null, error);
}
