using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TTC.Weighing.Infrastructure.Adapters;

/// <summary>
/// Kéo "Quy tắc cân" (ngưỡng tạp chất, CCS, Fibre, khối lượng tối đa, cân lần 1 tối thiểu...)
/// từ Portal DigiScale theo đơn vị (BU).
/// GET /api/v1/integration/scale-rules?organizationUnitId={id}
/// Auth: Bearer token của tài khoản đăng nhập; lỗi 401/403 thì thử Basic (tài khoản tích hợp).
/// Không lấy được → trả kết quả lỗi, KHÔNG tự điền ngưỡng mặc định.
/// </summary>
public sealed class DigiScaleScaleRulesAdapter
{
    private const string API_PATH = "api/v1/integration/scale-rules";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DigiScaleScaleRulesAdapter> _logger;

    public DigiScaleScaleRulesAdapter(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<DigiScaleScaleRulesAdapter> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    /// <param name="organizationUnitId">Id đơn vị (BU) của tài khoản đăng nhập.</param>
    /// <param name="accessToken">Bearer token DigiScale của phiên đăng nhập (có thể null).</param>
    public async Task<ScaleRulesFetchResult> GetAsync(
        string organizationUnitId, string? accessToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(organizationUnitId))
            return ScaleRulesFetchResult.Fail("NO_ORGANIZATION_UNIT");

        var query = $"{API_PATH}?organizationUnitId={Uri.EscapeDataString(organizationUnitId)}";
        try
        {
            var (status, body) = await SendAsync(query, accessToken, ct);
            if ((status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                && !string.IsNullOrWhiteSpace(accessToken) && HasBasicCredentials())
                (status, body) = await SendAsync(query, null, ct);

            if ((int)status is < 200 or >= 300)
            {
                _logger.LogWarning("ScaleRules: GET failed HTTP {Status} org={Org}: {Body}",
                    (int)status, organizationUnitId, body.Length > 300 ? body[..300] : body);
                return ScaleRulesFetchResult.Fail($"HTTP {(int)status}");
            }

            var rules = JsonSerializer.Deserialize<ScaleRulesDto>(body, JsonOptions);
            if (rules is null) return ScaleRulesFetchResult.Fail("EMPTY_RESPONSE");

            _logger.LogInformation(
                "ScaleRules: loaded org={Org} isDefault={IsDefault} impurityMax={Impurity} ccs=[{Min};{Max}] fibre={Fibre}",
                rules.OrganizationUnitId, rules.IsDefault, rules.ImpurityMax, rules.CcsMin, rules.CcsMax, rules.Fibre);
            return ScaleRulesFetchResult.Ok(rules);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ScaleRules: error fetching org={Org}", organizationUnitId);
            return ScaleRulesFetchResult.Fail(ex.Message);
        }
    }

    private async Task<(HttpStatusCode Status, string Body)> SendAsync(string query, string? bearerToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, query);
        if (!string.IsNullOrWhiteSpace(bearerToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        else
            SetBasicAuth(request);

        using var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return (response.StatusCode, body);
    }

    private bool HasBasicCredentials()
        => !string.IsNullOrWhiteSpace(_configuration.GetValue<string>("FrmPlanLookup:Username"));

    private void SetBasicAuth(HttpRequestMessage request)
    {
        var username = _configuration.GetValue<string>("FrmPlanLookup:Username");
        var password = _configuration.GetValue<string>("FrmPlanLookup:Password");
        if (string.IsNullOrWhiteSpace(username)) return;
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }
}

public sealed record ScaleRulesFetchResult(bool IsSuccess, ScaleRulesDto? Rules, string? Error)
{
    public static ScaleRulesFetchResult Ok(ScaleRulesDto rules) => new(true, rules, null);
    public static ScaleRulesFetchResult Fail(string error) => new(false, null, error);
}

/// <summary>Response scale-rules. Giá trị null = Portal chưa cài cho đơn vị.</summary>
public sealed class ScaleRulesDto
{
    [JsonPropertyName("organizationUnitId")] public string? OrganizationUnitId { get; set; }
    [JsonPropertyName("isDefault")] public bool IsDefault { get; set; }
    [JsonPropertyName("ccsMin")] public decimal? CcsMin { get; set; }
    [JsonPropertyName("ccsMax")] public decimal? CcsMax { get; set; }
    [JsonPropertyName("fibre")] public decimal? Fibre { get; set; }
    [JsonPropertyName("impurityMax")] public decimal? ImpurityMax { get; set; }
    [JsonPropertyName("maxWeightTon")] public decimal? MaxWeightTon { get; set; }
    [JsonPropertyName("minBeforeKg")] public decimal? MinBeforeKg { get; set; }
    [JsonPropertyName("plotDeviation")] public decimal? PlotDeviation { get; set; }
    [JsonPropertyName("approvedOrderStatus")] public string? ApprovedOrderStatus { get; set; }
    [JsonPropertyName("validCardStatus")] public string? ValidCardStatus { get; set; }
    [JsonPropertyName("disabledRules")] public List<string>? DisabledRules { get; set; }
    [JsonPropertyName("defaults")] public ScaleRulesDefaultsDto? Defaults { get; set; }
    [JsonPropertyName("updatedAtUtc")] public DateTime? UpdatedAtUtc { get; set; }
    [JsonPropertyName("updatedBy")] public string? UpdatedBy { get; set; }
}

/// <summary>Giá trị mặc định do Portal trả về (dùng khi đơn vị chưa cài riêng).</summary>
public sealed class ScaleRulesDefaultsDto
{
    [JsonPropertyName("maxWeightTon")] public decimal? MaxWeightTon { get; set; }
    [JsonPropertyName("minBeforeKg")] public decimal? MinBeforeKg { get; set; }
    [JsonPropertyName("plotDeviation")] public decimal? PlotDeviation { get; set; }
    [JsonPropertyName("approvedOrderStatus")] public string? ApprovedOrderStatus { get; set; }
    [JsonPropertyName("validCardStatus")] public string? ValidCardStatus { get; set; }
}
