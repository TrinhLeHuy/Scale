using System.Drawing.Text;
using TTC.Weighing.Infrastructure.Adapters;
using TTC.Weighing.WinForms.Localization;

namespace TTC.Weighing.WinForms.Printing;

/// <summary>
/// Chữ trên phiếu in: song ngữ theo BU của tài khoản ("ngôn ngữ BU/Tiếng Việt", ví dụ KSK → Khmer/Việt),
/// giá trị trống in "Không có", và vẽ chuỗi trộn chữ Khmer/Lào với chữ Latin bằng đúng font cho từng đoạn
/// (một font không có đủ glyph Việt + Khmer/Lào → tránh "tofu" □).
/// </summary>
public static class PrintText
{
    /// <summary>
    /// Ngôn ngữ in kèm tiếng Việt: ngôn ngữ đầu tiên khác tiếng Việt mà BU cho phép.
    /// Chưa có cấu hình BU → ngôn ngữ đang chọn (nếu khác tiếng Việt). Null = phiếu chỉ tiếng Việt.
    /// </summary>
    public static AppLanguage? Secondary
    {
        get
        {
            if (Loc.AllowedLanguages is { Count: > 0 } allowed)
                return allowed.Where(l => l != AppLanguage.Vi).Select(l => (AppLanguage?)l).FirstOrDefault();
            return Loc.Language != AppLanguage.Vi ? Loc.Language : null;
        }
    }

    public static bool IsBilingual => Secondary is not null;

    /// <summary>Nhãn song ngữ "ngôn ngữ BU/Tiếng Việt". Hai bản giống nhau (thiếu dịch) → in một lần.</summary>
    public static string L(string key, params object[] args)
    {
        var vi = Loc.TIn(AppLanguage.Vi, key, args);
        if (Secondary is not { } sec) return vi;
        var other = Loc.TIn(sec, key, args);
        return string.Equals(other, vi, StringComparison.Ordinal) ? vi : $"{other}/{vi}";
    }

    /// <summary>"Không có" (song ngữ) — in thay cho ô trống.</summary>
    public static string None => L("print.none");

    /// <summary>Giá trị in ra: trống → "Không có".</summary>
    public static string V(string? value) => string.IsNullOrWhiteSpace(value) ? None : value.Trim();

    /// <summary>Loại mía theo lệnh đốn (Mía cháy / Mía khúc / Mía cây), song ngữ. Null nếu FRM chưa có cờ.</summary>
    public static string? CaneForm(FrmLenhDonInfo? ld)
    {
        var vi = Services.SugarcaneCommodity.Label(ld, k => Loc.TIn(AppLanguage.Vi, k));
        if (vi is null || Secondary is not { } sec) return vi;
        var other = Services.SugarcaneCommodity.Label(ld, k => Loc.TIn(sec, k));
        return other is null || other == vi ? vi : $"{other}/{vi}";
    }

    /// <summary>Hình thức thu hoạch song ngữ; null nếu FRM chưa có cờ.</summary>
    public static string? HarvestMethod(bool? thuHoachMay) => thuHoachMay switch
    {
        true => L("reg.harvest_machine"),
        false => L("reg.harvest_manual"),
        null => null
    };

    // ════════════════════ Vẽ chữ trộn script ════════════════════

    private static readonly StringFormat Typographic = CreateFormat();
    private static readonly Dictionary<(string, float, FontStyle, GraphicsUnit), Font> _fonts = [];
    private static readonly object _lock = new();

    private static StringFormat CreateFormat()
    {
        var f = (StringFormat)StringFormat.GenericTypographic.Clone();
        f.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
        return f;
    }

    private enum Script { Latin, Khmer, Lao }

    private static Script? ScriptOf(char c)
    {
        if (char.IsWhiteSpace(c)) return null; // trung tính — theo đoạn trước
        if (c is >= '\u1780' and <= '\u17FF' or >= '\u19E0' and <= '\u19FF') return Script.Khmer;
        if (c is >= '\u0E80' and <= '\u0EFF') return Script.Lao;
        return Script.Latin;
    }

    private static bool IsComplexFamily(string family)
        => family == AppLanguage.Km.PreferredFontFamily() || family == AppLanguage.Lo.PreferredFontFamily()
           || family.Contains("Khmer", StringComparison.OrdinalIgnoreCase)
           || family.Contains("Lao", StringComparison.OrdinalIgnoreCase);

    private static Font FontFor(Script script, Font baseFont)
    {
        var family = script switch
        {
            Script.Khmer => LocalizedFont.FamilyFor(AppLanguage.Km),
            Script.Lao => LocalizedFont.FamilyFor(AppLanguage.Lo),
            // Font form đang theo ngôn ngữ Khmer/Lào thì phần Latin (tiếng Việt) dùng Segoe UI.
            _ => IsComplexFamily(baseFont.FontFamily.Name) ? "Segoe UI" : baseFont.FontFamily.Name
        };
        if (family == baseFont.FontFamily.Name) return baseFont;

        var key = (family, baseFont.Size, baseFont.Style, baseFont.Unit);
        lock (_lock)
        {
            if (!_fonts.TryGetValue(key, out var font))
                _fonts[key] = font = new Font(family, baseFont.Size, baseFont.Style, baseFont.Unit);
            return font;
        }
    }

    private static IEnumerable<(string Text, Script Script)> Runs(string text)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        var start = 0;
        Script? current = null;
        for (var i = 0; i < text.Length; i++)
        {
            var s = ScriptOf(text[i]);
            if (s is null) continue;
            if (current is null) { current = s; continue; }
            if (s == current) continue;
            yield return (text[start..i], current.Value);
            start = i;
            current = s;
        }
        yield return (text[start..], current ?? Script.Latin);
    }

    /// <summary>Kích thước một dòng chữ (đã tách font theo script).</summary>
    public static SizeF Measure(Graphics g, string? text, Font font)
    {
        float w = 0, h = font.GetHeight(g);
        foreach (var (run, script) in Runs(text ?? ""))
        {
            var f = FontFor(script, font);
            var sz = g.MeasureString(run, f, PointF.Empty, Typographic);
            w += sz.Width;
            h = Math.Max(h, sz.Height);
        }
        return new SizeF(w, h);
    }

    /// <summary>Vẽ một dòng chữ tại (x, y), mỗi đoạn script dùng font có glyph tương ứng.</summary>
    public static void Draw(Graphics g, string? text, Font font, Brush brush, float x, float y)
    {
        foreach (var (run, script) in Runs(text ?? ""))
        {
            var f = FontFor(script, font);
            g.DrawString(run, f, brush, x, y, Typographic);
            x += g.MeasureString(run, f, PointF.Empty, Typographic).Width;
        }
    }

    /// <summary>Vẽ căn giữa trong [left, left+width]; hỗ trợ nhiều dòng ("\n"). Trả chiều cao đã vẽ.</summary>
    public static float DrawCentered(Graphics g, string? text, Font font, Brush brush, float left, float width, float y)
    {
        float total = 0;
        foreach (var line in (text ?? "").Split('\n'))
        {
            var sz = Measure(g, line, font);
            Draw(g, line, font, brush, left + Math.Max(0, (width - sz.Width) / 2), y + total);
            total += sz.Height + 1;
        }
        return total;
    }

    /// <summary>Vẽ căn phải, kết thúc tại <paramref name="right"/>.</summary>
    public static void DrawRight(Graphics g, string? text, Font font, Brush brush, float right, float y)
        => Draw(g, text, font, brush, right - Measure(g, text, font).Width, y);

    /// <summary>Bề rộng cột nhãn = nhãn dài nhất + khoảng cách, tối thiểu <paramref name="min"/>.</summary>
    public static int LabelWidth(Graphics g, Font font, int min, params string[] labels)
        => Math.Max(min, (int)Math.Ceiling(labels.Max(l => Measure(g, l, font).Width)) + 10);

    /// <summary>
    /// Tách chữ thành các dòng vừa <paramref name="maxWidth"/> (theo khoảng trắng; từ quá dài thì cắt theo ký tự).
    /// Giữ "\n" có sẵn. Chữ Khmer/Lào không có khoảng trắng giữa từ vẫn được cắt theo ký tự, không tràn khung.
    /// </summary>
    public static List<string> Wrap(Graphics g, string? text, Font font, float maxWidth)
    {
        var lines = new List<string>();
        foreach (var paragraph in (text ?? "").Split('\n'))
        {
            var current = "";
            foreach (var word in paragraph.Split(' '))
            {
                var candidate = current.Length == 0 ? word : current + " " + word;
                if (Measure(g, candidate, font).Width <= maxWidth) { current = candidate; continue; }

                if (current.Length > 0) lines.Add(current);
                current = word;

                // Từ đơn vẫn dài hơn khung → cắt theo ký tự.
                while (current.Length > 1 && Measure(g, current, font).Width > maxWidth)
                {
                    var cut = current.Length - 1;
                    while (cut > 1 && Measure(g, current[..cut], font).Width > maxWidth) cut--;
                    lines.Add(current[..cut]);
                    current = current[cut..];
                }
            }
            lines.Add(current);
        }
        return lines;
    }

    /// <summary>Vẽ chữ tự xuống dòng trong khung rộng <paramref name="maxWidth"/>. Trả chiều cao đã vẽ.</summary>
    public static float DrawWrapped(Graphics g, string? text, Font font, Brush brush, float x, float y, float maxWidth)
    {
        var lineHeight = Measure(g, "Ág ភ្ជ ກ", font).Height;
        var lines = Wrap(g, text, font, maxWidth);
        for (var i = 0; i < lines.Count; i++)
            Draw(g, lines[i], font, brush, x, y + i * lineHeight);
        return lines.Count * lineHeight;
    }

    /// <summary>Vẽ căn giữa, tự xuống dòng trong khung rộng <paramref name="width"/>. Trả chiều cao đã vẽ.</summary>
    public static float DrawCenteredWrapped(Graphics g, string? text, Font font, Brush brush, float left, float width, float y)
    {
        var lineHeight = Measure(g, "Ág ភ្ជ ກ", font).Height;
        var lines = Wrap(g, text, font, width);
        for (var i = 0; i < lines.Count; i++)
        {
            var w = Measure(g, lines[i], font).Width;
            Draw(g, lines[i], font, brush, left + Math.Max(0, (width - w) / 2), y + i * lineHeight);
        }
        return lines.Count * lineHeight;
    }

    /// <summary>
    /// "nhãn : giá trị" trong khung [labelX, right]: nhãn tự xuống dòng trong cột nhãn, giá trị tự xuống dòng
    /// trong phần còn lại — không bao giờ tràn sang cột bên cạnh. Trả chiều cao dòng đã vẽ.
    /// </summary>
    public static float FieldWrapped(Graphics g, string label, string? value, Font labelFont, Font valueFont, Brush brush,
        float labelX, float valueX, float right, float y)
    {
        var labelHeight = DrawWrapped(g, label, labelFont, brush, labelX, y, Math.Max(20, valueX - labelX - 6));
        var colon = Measure(g, ": ", valueFont).Width;
        Draw(g, ":", valueFont, brush, valueX, y);
        var valueHeight = DrawWrapped(g, V(value), valueFont, brush, valueX + colon, y, Math.Max(20, right - valueX - colon));
        return Math.Max(labelHeight, valueHeight);
    }

    /// <summary>Vẽ "nhãn : giá trị" (giá trị trống → "Không có").</summary>
    public static void Field(Graphics g, string label, string? value, Font labelFont, Font valueFont, Brush brush,
        float labelX, float valueX, float y)
    {
        Draw(g, label, labelFont, brush, labelX, y);
        Draw(g, ": " + V(value), valueFont, brush, valueX, y);
    }
}
