using System.Globalization;
using System.Text;
using TTC.Weighing.WinForms.Controls;

namespace TTC.Weighing.WinForms.Helpers;

/// <summary>
/// Quy tắc chung cho mọi ô tìm kiếm của hệ thống cân:
/// <list type="bullet">
/// <item>Tắt bộ gõ tiếng Việt (Telex / VNI của Windows giữ chữ trong khung soạn thảo tới khi bấm Space
/// → ô chưa nhận chữ, kết quả không hiện ngay khi gõ). Xem <see cref="Prepare"/>.</item>
/// <item>So khớp không phân biệt hoa thường, không dấu (gõ "le thanh duy" khớp "LÊ THANH DUY", "đ" = "D"),
/// bỏ qua dấu cách / "-" / "." trong mã và biển số (gõ "82a01" khớp "82A-01394"). Xem <see cref="Matches"/>.</item>
/// </list>
/// </summary>
public static class SearchText
{
    /// <summary>Tắt bộ gõ cho ô tìm kiếm để mỗi phím vào ô ngay (gợi ý / lọc chạy theo từng ký tự).</summary>
    public static T Prepare<T>(T box) where T : TextBox
    {
        FieldTile.DisableIme(box);
        return box;
    }

    /// <summary>Bỏ dấu tiếng Việt (đ → D) và viết hoa; giữ nguyên dấu cách và ký tự khác.</summary>
    public static string Fold(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(c is 'đ' or 'Đ' ? 'D' : char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary><see cref="Fold"/> rồi bỏ dấu cách, "-", "." — dùng so khớp mã / biển số.</summary>
    public static string Compact(string? s)
    {
        var folded = Fold(s);
        if (folded.Length == 0) return folded;
        var sb = new StringBuilder(folded.Length);
        foreach (var c in folded)
            if (c is not (' ' or '-' or '.')) sb.Append(c);
        return sb.ToString();
    }

    /// <summary><paramref name="text"/> có chứa <paramref name="term"/> (không dấu, không phân biệt hoa thường, bỏ qua "-" "." " ").</summary>
    public static bool Matches(string? text, string? term)
    {
        if (string.IsNullOrWhiteSpace(term)) return true;
        if (string.IsNullOrEmpty(text)) return false;
        return Fold(text).Contains(Fold(term).Trim(), StringComparison.Ordinal)
            || Compact(text).Contains(Compact(term), StringComparison.Ordinal);
    }

    /// <summary>
    /// Dòng đã chuẩn hoá sẵn (gộp các cột) để lọc nhiều từ khóa nhanh: mọi từ phải có trong dòng.
    /// </summary>
    public readonly struct Row
    {
        private readonly string _folded;
        private readonly string _compact;

        public Row(string? text)
        {
            _folded = Fold(text);
            _compact = Compact(text);
        }

        public bool ContainsAll(IReadOnlyList<string> terms)
        {
            foreach (var t in terms)
            {
                var f = Fold(t);
                if (!_folded.Contains(f, StringComparison.Ordinal) && !_compact.Contains(Compact(t), StringComparison.Ordinal))
                    return false;
            }
            return true;
        }
    }
}
