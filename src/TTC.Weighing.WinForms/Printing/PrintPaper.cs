using System.Drawing.Printing;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Printing;

/// <summary>
/// Khổ giấy in phiếu (đăng tài, cân mía / vật tư, tạp chất, CCS) — dùng chung cho mọi phiếu.
/// <list type="bullet">
/// <item>Mặc định theo đơn vị (BU): <c>Printing:DefaultPaperByBu</c> (KSK = A5), không có thì <c>Printing:DefaultPaper</c> (A4).</item>
/// <item>Người dùng đổi khổ ở ô "Khổ giấy" trên màn xem trước phiếu → nhớ theo máy + đơn vị
///   (%LocalAppData%\TTC.Weighing\print-paper.json).</item>
/// <item>Mẫu phiếu vẽ theo bố cục A4; in khổ nhỏ hơn thì CO TỈ LỆ vừa trang (không cắt, không vỡ dòng).</item>
/// </list>
/// </summary>
public static class PrintPaper
{
    public static readonly string[] Supported = ["A5", "A4", "A6", "Letter"];

    /// <summary>Vùng in thiết kế của mẫu phiếu = A4 trừ lề mặc định 1 inch (đơn vị 1/100 inch) — mẫu vẽ trong khung này.</summary>
    private static readonly Size DesignArea = new(627, 969);

    private static readonly string PrefFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TTC.Weighing", "print-paper.json");

    /// <summary>Đơn vị đang đăng nhập (bỏ hậu tố " BU").</summary>
    private static string Bu => (Program.ActiveOrganizationUnitCode ?? "").Trim().Replace(" BU", "", StringComparison.OrdinalIgnoreCase).ToUpperInvariant();

    /// <summary>Khổ mặc định của đơn vị theo cấu hình.</summary>
    public static string DefaultForBu
    {
        get
        {
            var cfg = Program.AppConfiguration;
            var byBu = cfg?.GetSection("Printing:DefaultPaperByBu").GetChildren()
                .FirstOrDefault(c => string.Equals(c.Key.Trim(), Bu, StringComparison.OrdinalIgnoreCase))?.Value;
            return Normalize(byBu ?? cfg?["Printing:DefaultPaper"]) ?? "A4";
        }
    }

    /// <summary>Khổ đang dùng: lựa chọn đã lưu của máy cho đơn vị này, không có thì mặc định đơn vị.</summary>
    public static string Current
    {
        get => Normalize(LoadPrefs().GetValueOrDefault(Bu)) ?? DefaultForBu;
        set
        {
            var prefs = LoadPrefs();
            prefs[Bu] = Normalize(value) ?? DefaultForBu;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PrefFile)!);
                File.WriteAllText(PrefFile, JsonSerializer.Serialize(prefs));
            }
            catch { /* không lưu được → chỉ áp dụng phiên này */ }
            _session = prefs[Bu];
        }
    }

    private static string? _session;

    private static Dictionary<string, string> LoadPrefs()
    {
        try
        {
            if (File.Exists(PrefFile))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(PrefFile))
                       ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch { }
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (_session is not null) d[Bu] = _session;
        return d;
    }

    private static string? Normalize(string? name)
        => Supported.FirstOrDefault(s => string.Equals(s, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    private static PaperKind KindOf(string name) => name switch
    {
        "A5" => PaperKind.A5,
        "A6" => PaperKind.A6,
        "Letter" => PaperKind.Letter,
        _ => PaperKind.A4
    };

    /// <summary>Kích thước khổ (1/100 inch, dọc) — dùng khi máy in không khai báo khổ đó.</summary>
    private static Size SizeOf(string name) => name switch
    {
        "A5" => new Size(583, 827),
        "A6" => new Size(413, 583),
        "Letter" => new Size(850, 1100),
        _ => new Size(827, 1169)
    };

    /// <summary>Đặt khổ giấy + lề cho tài liệu in theo <see cref="Current"/>.</summary>
    public static void Apply(PrintDocument doc)
    {
        var name = Current;
        var kind = KindOf(name);
        PaperSize? paper = null;
        try
        {
            paper = doc.PrinterSettings.PaperSizes.Cast<PaperSize>().FirstOrDefault(p => p.Kind == kind)
                    ?? doc.PrinterSettings.PaperSizes.Cast<PaperSize>()
                        .FirstOrDefault(p => p.PaperName.StartsWith(name, StringComparison.OrdinalIgnoreCase));
        }
        catch { /* chưa có máy in mặc định */ }
        var size = SizeOf(name);
        doc.DefaultPageSettings.PaperSize = paper ?? new PaperSize(name, size.Width, size.Height);
        doc.DefaultPageSettings.Landscape = false;
        // A4/Letter giữ lề mặc định 1 inch như trước; khổ nhỏ dùng lề hẹp để chữ không quá nhỏ khi co tỉ lệ.
        var m = name is "A5" or "A6" ? 30 : 100;
        doc.DefaultPageSettings.Margins = new Margins(m, m, m, m);
    }

    /// <summary>
    /// Vẽ mẫu phiếu (bố cục A4) vừa vùng in của trang: co tỉ lệ đều theo cả rộng lẫn cao, căn trên giữa.
    /// Khổ bằng hoặc lớn hơn A4 → vẽ trực tiếp như cũ.
    /// </summary>
    public static void RenderFitted(Graphics g, Rectangle marginBounds, Action<Graphics, Rectangle> render)
    {
        var scale = Math.Min((float)marginBounds.Width / DesignArea.Width, (float)marginBounds.Height / DesignArea.Height);
        if (scale >= 0.999f)
        {
            render(g, marginBounds);
            return;
        }

        var state = g.Save();
        var drawnWidth = DesignArea.Width * scale;
        g.TranslateTransform(marginBounds.Left + (marginBounds.Width - drawnWidth) / 2f, marginBounds.Top);
        g.ScaleTransform(scale, scale);
        render(g, new Rectangle(0, 0, DesignArea.Width, DesignArea.Height));
        g.Restore(state);
    }

    /// <summary>Ô "Khổ giấy" cho thanh công cụ màn xem trước — đổi là lưu ngay (theo máy + đơn vị).</summary>
    public static Control CreatePicker()
    {
        var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 12, 0) };
        var label = new Label
        {
            Text = Loc.T("print.paper_size"), AutoSize = true, Margin = new Padding(0, 8, 6, 0),
            Font = AgrisTheme.Body(9.5F), ForeColor = AgrisTheme.TextSecondary
        };
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90, Font = AgrisTheme.Body(10F) };
        combo.Items.AddRange(Supported.Cast<object>().ToArray());
        combo.SelectedItem = Current;
        combo.SelectedIndexChanged += (_, _) => { if (combo.SelectedItem is string s) Current = s; };
        panel.Controls.Add(label);
        panel.Controls.Add(combo);
        return panel;
    }
}
