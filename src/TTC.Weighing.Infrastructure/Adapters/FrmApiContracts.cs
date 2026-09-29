using System.Text.Json.Serialization;

namespace TTC.Weighing.Infrastructure.Adapters;

// ══════════════════════════════════════════════════════════════
//  DTOs cho FRM Integration API
//  POST /api/v1/integration/frm
//  Auth: Basic (integration_frm / ...)
//
//  Supported entities (kebab-case):
//    brix, tram, thua-dat, xe-van-chuyen, vu-dau-tu,
//    giong-mia, vung-dia-ly, hop-dong-van-chuyen, the-xe
//  (nhan-vien → Forbidden cho account hiện tại)
//
//  Lưu ý: field "busineesUnit" có lỗi chính tả từ API gốc — giữ nguyên.
//  Lưu ý: "LenhDon" chưa được server DigiScale mirror — server trả 400 UNKNOWN_ENTITY.
//         Cần yêu cầu team DigiScale thêm entity này. Xong thì thêm vào danh sách sync.
// ══════════════════════════════════════════════════════════════

/// <summary>Request body gửi đến FRM API.</summary>
public sealed class FrmApiRequest
{
    [JsonPropertyName("entities")]
    public string Entities { get; set; } = string.Empty;

    /// <summary>Tên field giữ đúng chính tả từ API gốc (busineesUnit — 3 chữ 'e').</summary>
    [JsonPropertyName("busineesUnit")]
    public string BusinessUnit { get; set; } = string.Empty;

    [JsonPropertyName("Date")]
    public string Date { get; set; } = string.Empty;
}

// ── Response wrapper ──────────────────────────────────────────

/// <summary>
/// Response bao ngoài từ FRM API.
/// { "requested": N, "totalCount": N, "organizationUnitIds": [...],
///   "results": [{ "entity": "...", "items": [...], "totalCount": N, "page": 1, "pageSize": 200, "hasNext": bool }] }
/// </summary>
public sealed class FrmApiResponse
{
    [JsonPropertyName("requested")]
    public int Requested { get; set; }

    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("organizationUnitIds")]
    public List<string>? OrganizationUnitIds { get; set; }

    [JsonPropertyName("appliedModifiedFrom")]
    public string? AppliedModifiedFrom { get; set; }

    [JsonPropertyName("results")]
    public List<FrmEntityResult>? Results { get; set; }
}

public sealed class FrmEntityResult
{
    [JsonPropertyName("entity")]
    public string Entity { get; set; } = string.Empty;

    [JsonPropertyName("items")]
    public List<FrmEntityItem>? Items { get; set; }

    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }

    [JsonPropertyName("hasNext")]
    public bool HasNext { get; set; }
}

/// <summary>
/// Mỗi item trong FRM API có cấu trúc generic: id, sourceKey, fields (dynamic object).
/// Business data nằm trong "fields".
/// </summary>
public sealed class FrmEntityItem
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("sourceKey")]
    public string? SourceKey { get; set; }

    [JsonPropertyName("organizationUnitId")]
    public string? OrganizationUnitId { get; set; }

    [JsonPropertyName("sourceCreatedDate")]
    public string? SourceCreatedDate { get; set; }

    [JsonPropertyName("sourceModifiedDate")]
    public string? SourceModifiedDate { get; set; }

    [JsonPropertyName("lastSyncedAtUtc")]
    public string? LastSyncedAtUtc { get; set; }

    /// <summary>Chứa business fields tùy theo entity type. Dùng JsonElement để đọc linh hoạt.</summary>
    [JsonPropertyName("fields")]
    public System.Text.Json.JsonElement? Fields { get; set; }

    [JsonPropertyName("createdAtUtc")]
    public string? CreatedAtUtc { get; set; }

    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; set; }

    [JsonPropertyName("updatedAtUtc")]
    public string? UpdatedAtUtc { get; set; }

    [JsonPropertyName("updatedBy")]
    public string? UpdatedBy { get; set; }
}

// ── Helper đọc fields từ JsonElement ──────────────────────────

public static class FrmFieldsReader
{
    /// <summary>Đọc string field từ "fields" JsonElement, trả null nếu không có.</summary>
    public static string? GetString(System.Text.Json.JsonElement? fields, string fieldName)
    {
        if (fields is null) return null;
        if (fields.Value.TryGetProperty(fieldName, out var prop))
        {
            return prop.ValueKind == System.Text.Json.JsonValueKind.String
                ? prop.GetString()
                : prop.ValueKind == System.Text.Json.JsonValueKind.Null
                    ? null
                    : prop.ToString();
        }
        return null;
    }

    /// <summary>Đọc decimal field, trả null nếu không parse được.</summary>
    public static decimal? GetDecimal(System.Text.Json.JsonElement? fields, string fieldName)
    {
        var str = GetString(fields, fieldName);
        if (str is not null && decimal.TryParse(str,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var val))
            return val;
        return null;
    }

    /// <summary>Đọc boolean field (string "true"/"false"), trả null nếu không có.</summary>
    public static bool? GetBool(System.Text.Json.JsonElement? fields, string fieldName)
    {
        var str = GetString(fields, fieldName);
        if (str is not null && bool.TryParse(str, out var val))
            return val;
        return null;
    }
}

// ══════════════════════════════════════════════════════════════
//  Error response từ FRM API khi entity không hợp lệ
// ══════════════════════════════════════════════════════════════

public sealed class FrmApiError
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("status")]
    public int? Status { get; set; }

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("messageVi")]
    public string? MessageVi { get; set; }

    [JsonPropertyName("messageEn")]
    public string? MessageEn { get; set; }
}
