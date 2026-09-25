using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace TTC.Weighing.WinForms.Services;

/// <summary>
/// Gọi API DigiScale để xác thực người dùng.
/// Endpoint: POST /api/v1/auth/local/login
/// </summary>
public sealed class DigiScaleAuthService : IDisposable
{
    private readonly HttpClient _http;

    public DigiScaleAuthService(string baseUrl)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/')),
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    /// <summary>
    /// Đăng nhập và trả về kết quả chứa token + thông tin user.
    /// Trả null nếu lỗi xác thực (401/400).
    /// Ném exception nếu lỗi mạng hoặc server.
    /// </summary>
    public async Task<LoginResult?> LoginAsync(string userName, string password, CancellationToken ct = default)
    {
        var payload = new { userName, password };

        using var response = await _http.PostAsJsonAsync("/api/v1/auth/local/login", payload, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized
            || response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            return null; // Sai tên đăng nhập hoặc mật khẩu
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<LoginResponse>(ct)
            ?? throw new InvalidOperationException("API trả về response rỗng.");

        // Ưu tiên lấy culture và allowedCultures từ response body (server đã trả sẵn),
        // fallback sang JWT nếu response body thiếu.
        var culture = result.Culture
            ?? ParseClaimFromJwt(result.AccessToken, "culture");

        var allowedCultures = result.AllowedCultures is { Length: > 0 }
            ? result.AllowedCultures
            : ParseArrayClaimFromJwt(result.AccessToken, "allowed_cultures");

        return new LoginResult(
            AccessToken: result.AccessToken,
            RefreshToken: result.RefreshToken,
            ExpiresAtUtc: result.AccessTokenExpiresAtUtc,
            UserName: ParseClaimFromJwt(result.AccessToken, "preferred_username") ?? userName,
            DisplayName: ParseClaimFromJwt(result.AccessToken, "name") ?? userName,
            Role: ParseClaimFromJwt(result.AccessToken, "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"),
            Permissions: ParseArrayClaimFromJwt(result.AccessToken, "permission"),
            TenantId: ParseClaimFromJwt(result.AccessToken, "tenant_id"),
            TenantCode: ParseClaimFromJwt(result.AccessToken, "tenant_code"),
            Culture: culture,
            UserId: ParseClaimFromJwt(result.AccessToken, "sub"),
            AllowedCultures: allowedCultures,
            OrganizationUnitId: result.OrganizationUnitId,
            OrganizationUnitCode: result.OrganizationUnitCode,
            OrganizationUnitName: result.OrganizationUnitName);
    }

    public void Dispose() => _http.Dispose();

    /// <summary>Đọc claim đơn từ JWT payload (không xác minh chữ ký — server đã ký).</summary>
    private static string? ParseClaimFromJwt(string jwt, string claimType)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2) return null;

            var payload = parts[1];
            // Bổ sung padding base64
            payload = payload.Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }

            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var doc = System.Text.Json.JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty(claimType, out var elem))
                return null;

            return elem.ValueKind == System.Text.Json.JsonValueKind.Array
                ? elem[0].GetString()
                : elem.GetString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Đọc claim mảng từ JWT payload.</summary>
    private static string[] ParseArrayClaimFromJwt(string jwt, string claimType)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2) return [];

            var payload = parts[1];
            payload = payload.Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }

            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var doc = System.Text.Json.JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty(claimType, out var elem))
                return [];

            if (elem.ValueKind == System.Text.Json.JsonValueKind.Array)
                return elem.EnumerateArray()
                    .Select(e => e.GetString() ?? string.Empty)
                    .Where(s => s.Length > 0)
                    .ToArray();

            var single = elem.GetString();
            return single is not null ? [single] : [];
        }
        catch
        {
            return [];
        }
    }

    private sealed class LoginResponse
    {
        [JsonPropertyName("accessToken")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refreshToken")]
        public string RefreshToken { get; set; } = string.Empty;

        [JsonPropertyName("accessTokenExpiresAtUtc")]
        public DateTimeOffset AccessTokenExpiresAtUtc { get; set; }

        [JsonPropertyName("tokenType")]
        public string TokenType { get; set; } = string.Empty;

        [JsonPropertyName("expiresInSeconds")]
        public int ExpiresInSeconds { get; set; }

        [JsonPropertyName("culture")]
        public string? Culture { get; set; }

        [JsonPropertyName("allowedCultures")]
        public string[]? AllowedCultures { get; set; }

        [JsonPropertyName("organizationUnitId")]
        public string? OrganizationUnitId { get; set; }

        [JsonPropertyName("organizationUnitCode")]
        public string? OrganizationUnitCode { get; set; }

        [JsonPropertyName("organizationUnitName")]
        public string? OrganizationUnitName { get; set; }
    }
}

/// <summary>Kết quả đăng nhập thành công.</summary>
public sealed record LoginResult(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc,
    string UserName,
    string DisplayName,
    string? Role,
    string[] Permissions,
    string? TenantId,
    string? TenantCode,
    string? Culture,
    string? UserId,
    string[] AllowedCultures,
    string? OrganizationUnitId,
    string? OrganizationUnitCode,
    string? OrganizationUnitName);
