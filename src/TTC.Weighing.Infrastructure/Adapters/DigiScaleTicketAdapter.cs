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

    public DigiScaleTicketAdapter(HttpClient httpClient, IConfiguration configuration, ILogger<DigiScaleTicketAdapter> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>Đẩy phiếu đăng tài lên DigiScale.</summary>
    public Task<TicketResult> PushDangTaiAsync(string lenhDon, DateTime ngay, string uuTien, string loaiXe, string soDangTai, CancellationToken ct = default)
        => PushAsync("new_phieudangtai", new { PhieuDangTai = new { LenhDon = lenhDon, Ngay = ngay.ToString("M/d/yyyy h:mm:ss tt"), UuTien = uuTien, LoaiXe = loaiXe, SoDangTai = soDangTai } }, ct);

    /// <summary>Đẩy phiếu cân lên DigiScale.</summary>
    public Task<TicketResult> PushPhieuCanAsync(string loaiCan, string lanCan, string nvCan, string lenhDon, string trongLuong, DateTime thoiGian, string soPhieuCan, string tlThanhToan, string tcThanhToan, string ccsThanhToan, CancellationToken ct = default)
        => PushAsync("new_phieucan", new { PhieuCan = new { LoaiCan = loaiCan, LanCan = lanCan, NhanVienCanVao = nvCan, LenhDon = lenhDon, TrongLuong = trongLuong, ThoiGianCanVao = thoiGian.ToString("M/d/yyyy h:mm:ss tt"), SoPhieuCan = soPhieuCan, Name = "Null", TrongLuongThanhToan = tlThanhToan, TapChatThanhToan = tcThanhToan, CCSThanhToan = ccsThanhToan } }, ct);

    /// <summary>Đẩy phiếu tạp chất lên DigiScale.</summary>
    public Task<TicketResult> PushTapChatAsync(string lenhDon, string sttCan, string lanDo, DateTime ngay, string klBefore, string klAfter, string tapChatThucTe, CancellationToken ct = default)
        => PushAsync("new_phieudotapchat", new { PhieuDoTapChat = new { LenhDon = lenhDon, STTCan = sttCan, LanDo = lanDo, Ngay = ngay.ToString("yyyy-MM-dd h:mm:ss tt"), KLBefore = klBefore, KLAfter = klAfter, TapChatThucTe = tapChatThucTe } }, ct);

    /// <summary>Đẩy phiếu CCS lên DigiScale.</summary>
    public Task<TicketResult> PushCcsAsync(string lenhDon, string sttCan, string lanDo, DateTime ngay, string brix, string ccsDo, string pol, string polDoc, string caLamViec, CancellationToken ct = default)
        => PushAsync("new_phieudoccs", new { PhieuDoCCS = new { CCSThanhToan = "0", NhanVienDoCCS = "NV CCS", Pol = pol, CaLamViec = caLamViec, Ngay = ngay.ToString("M/d/yyyy h:mm:ss tt"), LenhDon = lenhDon, Polquansat = "0", Poldoc = polDoc, STTCan = sttCan, LanDo = lanDo, Brix = brix, CCSDo = ccsDo } }, ct);

    private async Task<TicketResult> PushAsync(string logicalName, object entityData, CancellationToken ct)
    {
        var bu = _configuration.GetValue<string>("FrmPlanLookup:DefaultBusinessUnit") ?? "TTCS";
        var envelope = new
        {
            logicalName,
            busineesUnit = bu, // Giữ typo API gốc
            status = "Create",
            entityRequest = new[] { entityData }
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, API_PATH);
            request.Content = JsonContent.Create(envelope);
            SetBasicAuth(request);

            _logger.LogInformation("DigiScale: pushing {LogicalName} to tickets API", logicalName);

            var response = await _httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("DigiScale: {LogicalName} pushed OK", logicalName);
                return new TicketResult(true, null);
            }

            _logger.LogWarning("DigiScale: {LogicalName} failed HTTP {Status}: {Body}", logicalName, (int)response.StatusCode, body.Length > 300 ? body[..300] : body);
            return new TicketResult(false, $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DigiScale: error pushing {LogicalName}", logicalName);
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
}

public sealed record TicketResult(bool IsSuccess, string? ErrorMessage);
