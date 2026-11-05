using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TTC.Weighing.Application.Queries;

namespace TTC.Weighing.Infrastructure.Integration;

/// <summary>Người dùng đang đăng nhập — ghi vào nhật ký API (set từ UI sau đăng nhập).</summary>
public static class ApiCallLogContext
{
    public static string? Actor { get; set; }

    /// <summary>BU (nhà máy) của tài khoản đăng nhập — mỗi nhà máy chỉ xem nhật ký của mình.</summary>
    public static string? OrgUnit { get; set; }

    private static readonly AsyncLocal<bool> _quietSuccess = new();

    /// <summary>
    /// Luồng kéo dữ liệu nền (FRM / ERP mỗi vài giây): KHÔNG ghi nhật ký các lượt gọi thành công
    /// (mỗi lượt kéo FRM ~40 request, body ~100 KB → nhật ký phình rất nhanh). Lượt lỗi vẫn ghi.
    /// AsyncLocal: chỉ áp dụng cho đúng luồng async đặt cờ.
    /// </summary>
    public static bool QuietSuccess
    {
        get => _quietSuccess.Value;
        set => _quietSuccess.Value = value;
    }
}

/// <summary>Một bản ghi nhật ký trước khi lưu DB.</summary>
public sealed record ApiCallLogEntry(
    Guid Id, DateTimeOffset OccurredAt, string Direction, string System, string Operation, string? BusinessKey,
    bool IsPolicy, string Method, string Url, int? HttpStatus, bool Success, int DurationMs,
    string? RequestBody, string? ResponseBody, string? Error, string? Actor, string ClientName, string? OrgUnit = null);

/// <summary>
/// Ghi mọi lượt gọi HTTP ra ngoài của các HttpClient tích hợp (DigiScale, FRM, ERP, Portal) vào bảng api_call_logs.
/// Đặt trong pipeline HttpClient nên không phải sửa từng adapter. Không ghi header Authorization;
/// mật khẩu / token trong nội dung được che "***". Lỗi ghi log không làm hỏng lượt gọi.
/// </summary>
public sealed class ApiCallLoggingHandler(ApiCallLogStore store, string clientName) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;
        var sw = Stopwatch.StartNew();
        string? requestBody = null;
        try
        {
            if (request.Content is not null)
            {
                await request.Content.LoadIntoBufferAsync();
                requestBody = await request.Content.ReadAsStringAsync(ct);
            }
        }
        catch { /* không đọc được nội dung gửi — vẫn gọi API */ }

        try
        {
            var response = await base.SendAsync(request, ct);
            sw.Stop();

            string? responseBody = null;
            try
            {
                await response.Content.LoadIntoBufferAsync();
                responseBody = await response.Content.ReadAsStringAsync(ct);
            }
            catch { }

            var ok = response.IsSuccessStatusCode && !ApiCallLogStore.LooksLikeBusinessError(responseBody);
            if (!(ok && ApiCallLogContext.QuietSuccess))
                store.Enqueue(ApiCallLogStore.Build(started, request, requestBody, (int)response.StatusCode,
                    ok, (int)sw.ElapsedMilliseconds, responseBody, null, clientName));
            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            store.Enqueue(ApiCallLogStore.Build(started, request, requestBody, null, false,
                (int)sw.ElapsedMilliseconds, null, ex is TaskCanceledException && !ct.IsCancellationRequested
                    ? $"TIMEOUT: {ex.Message}" : ex.Message, clientName));
            throw;
        }
    }
}

/// <summary>Lưu / đọc nhật ký API (PostgreSQL, bảng api_call_logs). Ghi nền qua hàng đợi để không chậm thao tác cân.</summary>
public sealed class ApiCallLogStore : IApiCallLogQueries
{
    private const int MAX_REQUEST_CHARS = 200_000;
    private const int MAX_RESPONSE_CHARS = 100_000;

    private static readonly Regex SecretRegex = new(
        "(\"(?:password|pass|pwd|accessToken|refreshToken|token|secret|clientSecret)\"\\s*:\\s*)\"[^\"]*\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex UrlSecretRegex = new("((?:token|password|key)=)[^&]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LogicalNameRegex = new("\"logicalName\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PolicyRegex = new("\"IsQuetCS\"\\s*:\\s*\"Yes\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string[] KeyFields = ["SoPhieuCan", "SoDangTai", "MaLenhDon", "shipmentNumber", "tripNumber"];

    private readonly string? _connStr;
    private readonly int _retentionDays;
    private readonly ILogger<ApiCallLogStore> _logger;
    private readonly Channel<ApiCallLogEntry> _queue = Channel.CreateBounded<ApiCallLogEntry>(
        new BoundedChannelOptions(5000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly SemaphoreSlim _schemaGate = new(1, 1);
    private bool _schemaReady;

    public ApiCallLogStore(IConfiguration configuration, ILogger<ApiCallLogStore> logger)
    {
        _connStr = configuration.GetConnectionString("WeighingDb");
        _retentionDays = configuration.GetValue<int?>("ApiCallLog:RetentionDays") ?? 30;
        _logger = logger;
        _ = Task.Run(WriterLoopAsync);
    }

    public void Enqueue(ApiCallLogEntry entry) => _queue.Writer.TryWrite(entry);

    /// <summary>Dựng bản ghi: phân loại đẩy/kéo + hệ thống, mã chứng từ, cờ bắt chính sách, che bí mật, cắt nội dung dài.</summary>
    public static ApiCallLogEntry Build(DateTimeOffset at, HttpRequestMessage request, string? requestBody, int? status,
        bool success, int durationMs, string? responseBody, string? error, string clientName)
    {
        var uri = request.RequestUri;
        var path = uri?.AbsolutePath.Trim('/') ?? string.Empty;
        var (direction, system) = Classify(request.Method, path, uri?.Host);
        var operation = requestBody is not null && LogicalNameRegex.Match(requestBody) is { Success: true } m
            ? m.Groups[1].Value
            : path;
        if (path.Contains("oracle-tickets", StringComparison.OrdinalIgnoreCase)) operation = "oracle-tickets";

        if (!success && error is null && responseBody is not null)
            error = responseBody.Length > 500 ? responseBody[..500] : responseBody;

        return new ApiCallLogEntry(
            Guid.NewGuid(), at, direction, system, Truncate(operation, 100)!, Truncate(BusinessKey(requestBody, uri), 100),
            requestBody is not null && PolicyRegex.IsMatch(requestBody),
            request.Method.Method, UrlSecretRegex.Replace(uri?.ToString() ?? string.Empty, "$1***"),
            status, success, durationMs,
            Truncate(Mask(requestBody), MAX_REQUEST_CHARS), Truncate(Mask(responseBody), MAX_RESPONSE_CHARS),
            Truncate(Mask(error), 2000), ApiCallLogContext.Actor, clientName, ApiCallLogContext.OrgUnit);
    }

    /// <summary>HTTP 200 nhưng nội dung báo lỗi nghiệp vụ (DigiScale items[].saved=false / error).</summary>
    public static bool LooksLikeBusinessError(string? body)
        => body is not null && (body.Contains("\"saved\":false", StringComparison.OrdinalIgnoreCase)
                                || body.Contains("\"pushSucceeded\":false", StringComparison.OrdinalIgnoreCase)
                                || body.Contains("message_error", StringComparison.OrdinalIgnoreCase));

    private static (string Direction, string System) Classify(HttpMethod method, string path, string? host)
    {
        var p = path.ToLowerInvariant();
        if (p.Contains("api/crm/create")) return ("PUSH", "FRM");
        if (p.Contains("integration/tickets") || p.Contains("oracle-tickets")) return ("PUSH", "DIGISCALE");
        if (p.Contains("integration/frm")) return ("PULL", "FRM");
        if (p.Contains("sugarcane-info")) return ("PULL", "FRM");
        if (p.Contains("order-lines")) return ("PULL", "ERP");
        if (p.Contains("scale-rules")) return ("PULL", "PORTAL");
        var system = host?.Contains("frm", StringComparison.OrdinalIgnoreCase) == true ? "FRM"
            : host?.Contains("digiscale", StringComparison.OrdinalIgnoreCase) == true ? "DIGISCALE" : "OTHER";
        return (method == HttpMethod.Get ? "PULL" : "PUSH", system);
    }

    private static string? BusinessKey(string? body, Uri? uri)
    {
        if (body is not null)
            foreach (var field in KeyFields)
                if (Regex.Match(body, $"\"{field}\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase) is { Success: true } m)
                    return m.Groups[1].Value;
        var query = uri?.Query;
        if (!string.IsNullOrEmpty(query))
            foreach (var field in new[] { "shipmentNumber", "tripNumber", "businessUnit", "organizationUnitId" })
                if (Regex.Match(query, $"[?&]{field}=([^&]+)", RegexOptions.IgnoreCase) is { Success: true } m)
                    return Uri.UnescapeDataString(m.Groups[1].Value);
        return null;
    }

    private static string? Mask(string? text) => text is null ? null : SecretRegex.Replace(text, "$1\"***\"");

    private static string? Truncate(string? text, int max)
        => text is null || text.Length <= max ? text : text[..max] + $"\n… (đã cắt, tổng {text.Length:N0} ký tự)";

    // ── Ghi nền ──

    private async Task WriterLoopAsync()
    {
        await foreach (var entry in _queue.Reader.ReadAllAsync())
        {
            try { await InsertAsync(entry); }
            catch (Exception ex) { _logger.LogWarning(ex, "ApiCallLog: ghi nhật ký lỗi ({Op})", entry.Operation); }
        }
    }

    private async Task EnsureSchemaAsync(Npgsql.NpgsqlConnection conn)
    {
        if (_schemaReady) return;
        await _schemaGate.WaitAsync();
        try
        {
            if (_schemaReady) return;
            await using (var cmd = new Npgsql.NpgsqlCommand(@"
CREATE TABLE IF NOT EXISTS api_call_logs (
  id uuid PRIMARY KEY,
  occurred_at timestamptz NOT NULL,
  direction varchar(10) NOT NULL,
  system varchar(20) NOT NULL,
  operation varchar(120) NOT NULL,
  business_key varchar(120),
  is_policy boolean NOT NULL DEFAULT false,
  method varchar(10) NOT NULL,
  url text NOT NULL,
  http_status integer,
  success boolean NOT NULL,
  duration_ms integer NOT NULL,
  request_body text,
  response_body text,
  error text,
  actor text,
  client_name varchar(80),
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_api_call_logs_occurred_at ON api_call_logs (occurred_at DESC);
CREATE INDEX IF NOT EXISTS idx_api_call_logs_business_key ON api_call_logs (business_key);
ALTER TABLE api_call_logs ADD COLUMN IF NOT EXISTS organization_unit varchar(30);", conn))
                await cmd.ExecuteNonQueryAsync();

            // Giữ nhật ký N ngày (ApiCallLog:RetentionDays) — nhật ký kỹ thuật, không phải chứng từ nghiệp vụ.
            await using (var purge = new Npgsql.NpgsqlCommand(
                "DELETE FROM api_call_logs WHERE occurred_at < now() - make_interval(days => $1)", conn))
            {
                purge.Parameters.AddWithValue(Math.Max(1, _retentionDays));
                await purge.ExecuteNonQueryAsync();
            }
            _schemaReady = true;
        }
        finally { _schemaGate.Release(); }
    }

    private async Task<Npgsql.NpgsqlConnection?> OpenAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_connStr)) return null;
        var conn = new Npgsql.NpgsqlConnection(_connStr);
        await conn.OpenAsync(ct);
        await EnsureSchemaAsync(conn);
        return conn;
    }

    private async Task InsertAsync(ApiCallLogEntry e)
    {
        await using var conn = await OpenAsync();
        if (conn is null) return;
        await using var cmd = new Npgsql.NpgsqlCommand(@"
INSERT INTO api_call_logs (id, occurred_at, direction, system, operation, business_key, is_policy, method, url,
  http_status, success, duration_ms, request_body, response_body, error, actor, client_name, organization_unit)
VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18)", conn);
        cmd.Parameters.AddWithValue(e.Id);
        cmd.Parameters.AddWithValue(e.OccurredAt.ToUniversalTime());
        cmd.Parameters.AddWithValue(e.Direction);
        cmd.Parameters.AddWithValue(e.System);
        cmd.Parameters.AddWithValue(e.Operation);
        cmd.Parameters.AddWithValue((object?)e.BusinessKey ?? DBNull.Value);
        cmd.Parameters.AddWithValue(e.IsPolicy);
        cmd.Parameters.AddWithValue(e.Method);
        cmd.Parameters.AddWithValue(e.Url);
        cmd.Parameters.AddWithValue((object?)e.HttpStatus ?? DBNull.Value);
        cmd.Parameters.AddWithValue(e.Success);
        cmd.Parameters.AddWithValue(e.DurationMs);
        cmd.Parameters.AddWithValue((object?)e.RequestBody ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)e.ResponseBody ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)e.Error ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)e.Actor ?? DBNull.Value);
        cmd.Parameters.AddWithValue(e.ClientName);
        cmd.Parameters.AddWithValue((object?)e.OrgUnit ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    // ── Đọc ──

    public async Task<IReadOnlyList<ApiCallLogRow>> ListAsync(ApiCallLogFilter f, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        if (conn is null) return [];

        var sql = new StringBuilder(@"
SELECT id, occurred_at, direction, system, operation, business_key, is_policy, http_status, success, duration_ms, error
FROM api_call_logs WHERE occurred_at >= $1 AND occurred_at < $2");
        var args = new List<object> { f.FromUtc.ToUniversalTime(), f.ToUtc.ToUniversalTime() };
        void Add(string clause, object value) { args.Add(value); sql.Append(clause.Replace("@", "$" + args.Count)); }

        // Mỗi nhà máy chỉ xem nhật ký của mình (tổng công ty / admin xem tất cả).
        if (!TTC.Weighing.Infrastructure.Queries.OrganizationUnitFilter.SeesAllUnits)
            Add(" AND organization_unit = @", TTC.Weighing.Infrastructure.Queries.OrganizationUnitFilter.CurrentOrgUnit!);
        if (!string.IsNullOrWhiteSpace(f.System)) Add(" AND system = @", f.System);
        if (!string.IsNullOrWhiteSpace(f.Direction)) Add(" AND direction = @", f.Direction);
        if (f.Success is { } ok) Add(" AND success = @", ok);
        if (f.PolicyOnly) sql.Append(" AND is_policy");
        if (!string.IsNullOrWhiteSpace(f.Search))
            Add(" AND (business_key ILIKE @ OR operation ILIKE @ OR request_body ILIKE @)", $"%{f.Search.Trim()}%");
        var terms = f.AnyOf?.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct().ToList() ?? [];
        if (terms.Count > 0)
        {
            sql.Append(" AND (");
            for (var i = 0; i < terms.Count; i++)
            {
                if (i > 0) sql.Append(" OR ");
                args.Add(terms[i]);
                var keyIdx = args.Count;
                args.Add($"%{terms[i]}%");
                sql.Append($"business_key = ${keyIdx} OR request_body ILIKE ${args.Count}");
            }
            sql.Append(')');
        }
        Add(" ORDER BY occurred_at DESC LIMIT @", Math.Clamp(f.Limit, 1, 5000));

        await using var cmd = new Npgsql.NpgsqlCommand(sql.ToString(), conn);
        foreach (var a in args) cmd.Parameters.AddWithValue(a);

        var rows = new List<ApiCallLogRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            rows.Add(new ApiCallLogRow(
                r.GetGuid(0), r.GetFieldValue<DateTimeOffset>(1), r.GetString(2), r.GetString(3), r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5), r.GetBoolean(6), r.IsDBNull(7) ? null : r.GetInt32(7),
                r.GetBoolean(8), r.GetInt32(9), r.IsDBNull(10) ? null : r.GetString(10)));
        return rows;
    }

    public async Task<ApiCallLogDetail?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        if (conn is null) return null;
        await using var cmd = new Npgsql.NpgsqlCommand(@"
SELECT id, occurred_at, direction, system, operation, business_key, is_policy, http_status, success, duration_ms, error,
       method, url, request_body, response_body, actor, client_name
FROM api_call_logs WHERE id = $1 AND ($2 OR organization_unit = $3)", conn);
        cmd.Parameters.AddWithValue(id);
        cmd.Parameters.AddWithValue(TTC.Weighing.Infrastructure.Queries.OrganizationUnitFilter.SeesAllUnits);
        cmd.Parameters.AddWithValue((object?)TTC.Weighing.Infrastructure.Queries.OrganizationUnitFilter.CurrentOrgUnit ?? string.Empty);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        var row = new ApiCallLogRow(
            r.GetGuid(0), r.GetFieldValue<DateTimeOffset>(1), r.GetString(2), r.GetString(3), r.GetString(4),
            r.IsDBNull(5) ? null : r.GetString(5), r.GetBoolean(6), r.IsDBNull(7) ? null : r.GetInt32(7),
            r.GetBoolean(8), r.GetInt32(9), r.IsDBNull(10) ? null : r.GetString(10));
        return new ApiCallLogDetail(row, r.GetString(11), r.GetString(12),
            r.IsDBNull(13) ? null : r.GetString(13), r.IsDBNull(14) ? null : r.GetString(14),
            r.IsDBNull(15) ? null : r.GetString(15), r.IsDBNull(16) ? null : r.GetString(16));
    }
}
