using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using TTC.Weighing.Infrastructure.Adapters;
using TTC.Weighing.WinForms.Localization;

namespace TTC.Weighing.WinForms.Helpers;

/// <summary>Lỗi đã dịch sang ngôn ngữ người vận hành: bị gì + cần làm gì. Không bao giờ chứa JSON.</summary>
public sealed record FriendlyError(string Reason, string? Action);

/// <summary>
/// Dịch phản hồi DigiScale/FRM (JSON, mã CR-xx, HTTP, lỗi mạng) sang câu dễ hiểu.
/// Nội dung gốc chỉ ghi log / file last-digiscale-*.txt, KHÔNG hiển thị lên màn hình.
/// </summary>
public static partial class DigiScaleErrors
{
    /// <summary>Lý do + cách xử lý cho một kết quả đẩy DigiScale. Null nếu thành công.</summary>
    public static FriendlyError? Describe(this TicketResult result)
        => result.IsSuccess ? null : Describe(result.ErrorMessage);

    /// <summary>Chuỗi ngắn gọn (lý do) — dùng cho thanh trạng thái / khung thông báo.</summary>
    public static string ShortText(this TicketResult result)
        => result.IsSuccess ? Loc.T("digi.ok") : Describe(result.ErrorMessage).Reason;

    public static FriendlyError Describe(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new(Loc.T("digi.err_unknown"), Loc.T("digi.act_retry"));

        var text = raw.Trim();

        // ── Lỗi kiểm tra đăng tài DigiScale (DT-xx): câu ngắn, mỗi lỗi một dòng ──
        var dtReasons = DescribeRegistrationRules(text);
        if (dtReasons is not null)
            return new(dtReasons, null);

        // ── Mã quy tắc DigiScale đã biết ──
        if (Has(text, "NO_POLICY", "CR_07", "CR-07"))
            return new(Loc.T("digi.err_no_policy"), Loc.T("digi.act_no_policy"));

        if (Has(text, "NOT_WEIGHED_IN", "CR_04", "CR-04"))
            return new(Loc.T("digi.err_not_weighed_in"), Loc.T("digi.act_not_weighed_in"));

        // Action FRM BatChinhSachCanDo trả message_error
        if (text.StartsWith("POLICY_ERROR:", StringComparison.OrdinalIgnoreCase))
        {
            var msg = StripPluginPrefix(text["POLICY_ERROR:".Length..].Trim());
            return new(Loc.T("digi.err_policy_frm", msg), Loc.T("digi.act_policy_frm"));
        }

        // ── Lỗi mạng / máy chủ ──
        if (Has(text, "No such host", "actively refused", "connection attempt failed", "Name or service not known",
                "A connection attempt", "Unable to connect", "SocketException", "network"))
            return new(Loc.T("digi.err_network"), Loc.T("digi.act_network"));

        if (Has(text, "timed out", "Timeout", "TaskCanceled", "The operation was canceled"))
            return new(Loc.T("digi.err_timeout"), Loc.T("digi.act_timeout"));

        // FRM không tìm thấy bản ghi: "... With Id = X Does Not Exist" → câu dễ hiểu + nguyên văn FRM trả về.
        if (Has(text, "Does Not Exist"))
        {
            // Popup chỉ câu dễ hiểu; nguyên văn FRM xem ở khung thông báo / Nhật ký API (LogText).
            return new(Loc.T("digi.err_record_not_exist"), Loc.T("digi.act_check_retry"));
        }

        var http = HttpCodeRegex().Match(text);
        var code = http.Success ? int.Parse(http.Groups[1].Value) : (int?)null;
        if (code is 401 or 403)
            return new(Loc.T("digi.err_auth"), Loc.T("digi.act_auth"));
        if (code is >= 500)
            return new(Loc.T("digi.err_server", code.Value), Loc.T("digi.act_timeout"));
        if (code is 404)
            return new(Loc.T("digi.err_not_found_api"), Loc.T("digi.act_auth"));

        // ── Câu lỗi nghiệp vụ trong JSON (items[0].error / message / title) ──
        var business = ExtractBusinessMessage(text);
        if (business is not null)
            return new(business, Loc.T("digi.act_business"));

        // Câu đã là chữ (không phải JSON) → dùng nguyên văn, bỏ tiền tố kỹ thuật.
        if (!LooksLikeJson(text))
        {
            var clean = StripTechnicalPrefix(text);
            return new(clean, code is not null ? Loc.T("digi.act_business") : Loc.T("digi.act_retry"));
        }

        return new(code is not null ? Loc.T("digi.err_rejected_code", code.Value) : Loc.T("digi.err_unknown"),
            Loc.T("digi.act_retry"));
    }

    /// <summary>
    /// Câu lỗi nguyên văn hệ thống trả về (đã bóc JSON / HTTP) — chỉ ghi khung thông báo / log, KHÔNG đưa vào popup.
    /// </summary>
    public static string LogText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "—";
        var msg = ExtractBusinessMessage(raw) ?? StripTechnicalPrefix(raw);
        return msg.Replace(Environment.NewLine, " · ").Trim();
    }

    /// <summary>Dịch exception (mất mạng, timeout...) khi gọi DigiScale.</summary>
    public static FriendlyError Describe(Exception ex) => ex switch
    {
        HttpRequestException => new(Loc.T("digi.err_network"), Loc.T("digi.act_network")),
        TaskCanceledException or TimeoutException => new(Loc.T("digi.err_timeout"), Loc.T("digi.act_timeout")),
        _ => Describe(ex.Message)
    };

    /// <summary>
    /// Thay mọi khối JSON lẫn trong một đoạn văn bằng câu lỗi đã dịch — lưới an toàn cho popup,
    /// để không bao giờ hiện JSON thô dù nơi gọi quên dịch.
    /// </summary>
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        if (!text.Contains('{') || !text.Contains('"')) return StripInlineCodes(text);

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (end <= start) return StripInlineCodes(text);

        var json = text[start..(end + 1)];
        var replacement = ExtractBusinessMessage(json) ?? Loc.T("digi.err_unknown");
        return StripInlineCodes(text[..start] + replacement + text[(end + 1)..]);
    }

    /// <summary>
    /// Lỗi kiểm tra đăng tài DigiScale "[DT-xx] ..." → câu ngắn theo mã, mỗi lỗi một dòng, không JSON / HTTP / mã trạng thái.
    /// Mã chưa biết → câu tiếng Việt DigiScale gửi kèm, bỏ phần kỹ thuật trong ngoặc.
    /// Null nếu không có mã DT nào.
    /// </summary>
    private static string? DescribeRegistrationRules(string text)
    {
        var codes = DtCodeRegex().Matches(text).Select(m => m.Groups[1].Value).Distinct().ToList();
        if (codes.Count == 0) return null;

        var vietnamese = DtSegmentRegex().Matches(text)
            .Select(m => (Code: m.Groups[1].Value, Text: m.Groups[2].Value.Trim().TrimEnd('.')))
            .Where(s => VietnameseRegex().IsMatch(s.Text))
            .GroupBy(s => s.Code)
            .ToDictionary(g => g.Key, g => g.First().Text);

        var lines = codes.Select(code =>
        {
            var key = $"digi.err_dt{code}";
            var known = Loc.T(key);
            if (!string.Equals(known, key, StringComparison.Ordinal)) return known; // có câu dịch sẵn

            return vietnamese.TryGetValue(code, out var vi)
                ? TechnicalParenRegex().Replace(vi, "").Trim()
                : Loc.T("digi.err_unknown");
        });

        return string.Join(Environment.NewLine, lines.Distinct());
    }

    [GeneratedRegex(@"\[DT-(\d+)\]")]
    private static partial Regex DtCodeRegex();

    [GeneratedRegex(@"\[DT-(\d+)\]\s*([^\[""]+)")]
    private static partial Regex DtSegmentRegex();

    /// <summary>Phần trong ngoặc chứa mã / giá trị kỹ thuật, ví dụ "(trạng thái '1', cần '100000001')".</summary>
    [GeneratedRegex(@"\s*\([^)]*'[^)]*\)")]
    private static partial Regex TechnicalParenRegex();

    // ───────────────────────── helpers ─────────────────────────

    private static bool Has(string text, params string[] needles)
        => needles.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikeJson(string text) => text.Contains('{') && text.Contains('"');

    /// <summary>Lấy câu lỗi nghiệp vụ trong JSON; ưu tiên phần tiếng Việt của chuỗi "[CR-xx] ...".</summary>
    private static string? ExtractBusinessMessage(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        string? found = null;

        if (start >= 0 && end > start)
        {
            try
            {
                using var doc = JsonDocument.Parse(text[start..(end + 1)]);
                var root = doc.RootElement;
                if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array
                    && items.GetArrayLength() > 0 && items[0].TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String)
                    found = e.GetString();
                found ??= Str(root, "message_error") ?? Str(root, "message") ?? Str(root, "error")
                    ?? Str(root, "title") ?? Str(root, "detail");
                // Lỗi FRM (CRM OData) lồng: {"error":{"code":"500","message":{"value":"[{No title:... Does Not Exist}]"}}}
                found ??= ODataMessage(root);
            }
            catch (JsonException)
            {
                // items[0].error là chuỗi "HTTP 500 ...: {json lồng}" → JSON ngoài hỏng khi cắt; thử khối lồng trong cùng.
                found = NestedODataMessage(text);
            }
        }
        else
        {
            found = text;
        }

        // items[0].error = "HTTP 500 Internal Server Error: {OData}" → lấy câu FRM bên trong.
        if (found is not null && LooksLikeJson(found))
            found = NestedODataMessage(found) ?? found;

        return found is null ? null : PreferVietnamese(CleanFrmMessage(found));
    }

    private static string? ODataMessage(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("error", out var err)
            || err.ValueKind != JsonValueKind.Object || !err.TryGetProperty("message", out var msg))
            return null;
        if (msg.ValueKind == JsonValueKind.String) return msg.GetString();
        return msg.ValueKind == JsonValueKind.Object ? Str(msg, "value") : null;
    }

    private static string? NestedODataMessage(string text)
    {
        var start = text.IndexOf("{\"error\"", StringComparison.Ordinal);
        if (start < 0) start = text.IndexOf("{ \"error\"", StringComparison.Ordinal);
        if (start < 0) return null;
        var end = text.LastIndexOf('}');
        for (; end > start; end = text.LastIndexOf('}', end - 1))
        {
            try
            {
                using var doc = JsonDocument.Parse(text[start..(end + 1)]);
                return ODataMessage(doc.RootElement);
            }
            catch (JsonException) { }
        }
        return null;
    }

    /// <summary>"[{No title:new_phieudangtai With Id = X Does Not Exist}]" → "new_phieudangtai With Id = X Does Not Exist".</summary>
    private static string CleanFrmMessage(string message)
    {
        var t = message.Trim();
        if (t.StartsWith("[{") && t.EndsWith("}]")) t = t[2..^2].Trim();
        if (t.StartsWith("No title:", StringComparison.OrdinalIgnoreCase)) t = t["No title:".Length..].Trim();
        return t;
    }

    private static string? Str(JsonElement el, string name)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    /// <summary>"CODE: [CR-04] English. [CR-04] Tiếng Việt." → chỉ giữ các câu tiếng Việt.</summary>
    private static string PreferVietnamese(string message)
    {
        var parts = RuleSegmentRegex().Matches(message)
            .Select(m => m.Groups[1].Value.Trim().TrimEnd('.'))
            .Where(s => s.Length > 0)
            .ToList();

        if (parts.Count == 0) return StripTechnicalPrefix(message);

        var vi = parts.Where(p => VietnameseRegex().IsMatch(p)).Distinct().ToList();
        var chosen = vi.Count > 0 ? vi : parts.Distinct().ToList();
        return string.Join(Environment.NewLine, chosen.Select(p => "• " + p + "."));
    }

    private static string StripTechnicalPrefix(string text)
    {
        var t = text.Trim();
        t = HttpPrefixRegex().Replace(t, "");
        t = t.Replace("DigiScale không lưu:", "", StringComparison.OrdinalIgnoreCase).Trim();
        t = CodePrefixRegex().Replace(t, ""); // INTEGRATION.RULE.CR_04.X:
        return StripPluginPrefix(t);
    }

    private static string StripPluginPrefix(string t)
    {
        var idx = t.IndexOf(": ", StringComparison.Ordinal);
        // "Plugin: Không tìm thấy hợp đồng" / "C30seeds.Action...: ..." → bỏ tên nguồn kỹ thuật
        return idx is > 0 and < 60 && !t[..idx].Contains(' ') ? t[(idx + 2)..].Trim() : t.Trim();
    }

    private static string StripInlineCodes(string text) => CodePrefixRegex().Replace(text, "");

    [GeneratedRegex(@"HTTP\s+(\d{3})")]
    private static partial Regex HttpCodeRegex();

    [GeneratedRegex(@"^HTTP\s+\d{3}\s*:?\s*")]
    private static partial Regex HttpPrefixRegex();

    [GeneratedRegex(@"[A-Z]+(?:\.[A-Z0-9_]+){2,}\s*:\s*")]
    private static partial Regex CodePrefixRegex();

    [GeneratedRegex(@"\[CR-\d+\]\s*([^\[]+)")]
    private static partial Regex RuleSegmentRegex();

    [GeneratedRegex(@"[àáạảãâầấậẩẫăằắặẳẵèéẹẻẽêềếệểễìíịỉĩòóọỏõôồốộổỗơờớợởỡùúụủũưừứựửữỳýỵỷỹđ]", RegexOptions.IgnoreCase)]
    private static partial Regex VietnameseRegex();
}
