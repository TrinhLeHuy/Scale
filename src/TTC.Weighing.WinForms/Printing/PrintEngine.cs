using System.Drawing.Printing;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Printing;

/// <summary>
/// Engine in phiếu dùng GDI+ PrintDocument. Mỗi loại phiếu implement IPrintTemplate.
/// </summary>
public static class PrintEngine
{
    /// <summary>Hiện preview rồi in.</summary>
    public static void ShowPreview(IPrintTemplate template, string title)
    {
        var doc = new PrintDocument { DocumentName = title };
        PrintPrinter.Apply(doc);
        PrintPaper.Apply(doc);
        doc.PrintPage += (_, e) => PrintPaper.RenderFitted(e.Graphics!, e.MarginBounds, template.Render);

        using var preview = new PrintPreviewDialog
        {
            Document = doc,
            Text = title,
            WindowState = FormWindowState.Maximized
        };
        preview.ShowDialog();
    }

    /// <summary>In trực tiếp không preview.</summary>
    public static void PrintDirect(IPrintTemplate template, string title)
    {
        var doc = new PrintDocument { DocumentName = title };
        PrintPrinter.Apply(doc);
        PrintPaper.Apply(doc);
        doc.PrintPage += (_, e) => PrintPaper.RenderFitted(e.Graphics!, e.MarginBounds, template.Render);
        doc.Print();
    }
}

/// <summary>Interface cho mỗi template phiếu.</summary>
public interface IPrintTemplate
{
    void Render(Graphics g, Rectangle bounds);
}

/// <summary>Helper vẽ phiếu — dùng chung cho tất cả templates.</summary>
public static class PrintHelper
{
    // Font in theo ngôn ngữ hiện hành (Times New Roman không có chữ Lào/Khmer).
    // Cache theo họ font — tạo lại khi người dùng đổi ngôn ngữ.
    private static readonly object _fontLock = new();
    private static string? _fontFamily;
    private static Font _titleFont = null!, _headerFont = null!, _bodyFont = null!, _smallFont = null!,
        _companyFont = null!, _addressFont = null!, _bigNumberFont = null!;

    private static void EnsureFonts()
    {
        var family = LocalizedFont.Print;
        if (family == _fontFamily) return;
        lock (_fontLock)
        {
            if (family == _fontFamily) return;
            // Không Dispose font cũ: có thể đang được tham chiếu bởi một lượt vẽ khác.
            _titleFont = new Font(family, 16, FontStyle.Bold);
            _headerFont = new Font(family, 10, FontStyle.Bold);
            _bodyFont = new Font(family, 10);
            _smallFont = new Font(family, 8);
            _companyFont = new Font(family, 11, FontStyle.Bold);
            _addressFont = new Font(family, 9, FontStyle.Italic);
            _bigNumberFont = new Font(family, 14, FontStyle.Bold);
            _fontFamily = family;
        }
    }

    public static Font TitleFont { get { EnsureFonts(); return _titleFont; } }
    public static Font HeaderFont { get { EnsureFonts(); return _headerFont; } }
    public static Font BodyFont { get { EnsureFonts(); return _bodyFont; } }
    public static Font SmallFont { get { EnsureFonts(); return _smallFont; } }
    public static Font CompanyFont { get { EnsureFonts(); return _companyFont; } }
    public static Font AddressFont { get { EnsureFonts(); return _addressFont; } }
    public static Font BigNumberFont { get { EnsureFonts(); return _bigNumberFont; } }

    public static int DrawCompanyHeader(Graphics g, Rectangle bounds, int y, string companyName, string address)
    {
        g.DrawString(companyName, CompanyFont, Brushes.Black, bounds.Left, y);
        y += 18;
        g.DrawString(address, AddressFont, Brushes.Black, bounds.Left, y);
        y += 16;
        return y;
    }

    public static int DrawTitle(Graphics g, Rectangle bounds, int y, string title)
    {
        var size = g.MeasureString(title, TitleFont);
        g.DrawString(title, TitleFont, Brushes.Black, bounds.Left + (bounds.Width - size.Width) / 2, y);
        return y + (int)size.Height + 8;
    }

    public static int DrawDateRange(Graphics g, Rectangle bounds, int y, string fromDate, string toDate)
    {
        var text = Loc.T("print.date_range", fromDate, toDate);
        g.DrawString(text, SmallFont, Brushes.Black, bounds.Right - g.MeasureString(text, SmallFont).Width, y);
        return y;
    }

    public static int DrawField(Graphics g, int x, int y, string label, string value, int labelWidth = 160)
    {
        g.DrawString(label, BodyFont, Brushes.Black, x, y);
        g.DrawString(": " + value, BodyFont, Brushes.Black, x + labelWidth, y);
        return y + 18;
    }

    public static int DrawFieldBold(Graphics g, int x, int y, string label, string value, int labelWidth = 160)
    {
        g.DrawString(label, BodyFont, Brushes.Black, x, y);
        g.DrawString(": " + value, BigNumberFont, Brushes.Black, x + labelWidth, y);
        return y + 22;
    }

    public static int DrawLine(Graphics g, Rectangle bounds, int y)
    {
        g.DrawLine(Pens.Black, bounds.Left, y, bounds.Right, y);
        return y + 4;
    }

    public static int DrawSignatures(Graphics g, Rectangle bounds, int y, params string[] names)
    {
        y += 30;
        var colWidth = bounds.Width / names.Length;
        for (var i = 0; i < names.Length; i++)
        {
            var size = g.MeasureString(names[i], HeaderFont);
            g.DrawString(names[i], HeaderFont, Brushes.Black,
                bounds.Left + i * colWidth + (colWidth - size.Width) / 2, y);
        }
        return y + 20;
    }

    /// <summary>Vẽ bảng đơn giản.</summary>
    public static int DrawTable(Graphics g, int x, int y, int width, string[] headers, int[] colWidths, List<string[]> rows)
    {
        var rowHeight = 20;

        // Header
        var cx = x;
        for (var i = 0; i < headers.Length; i++)
        {
            g.DrawRectangle(Pens.Black, cx, y, colWidths[i], rowHeight);
            g.FillRectangle(Brushes.LightGray, cx + 1, y + 1, colWidths[i] - 2, rowHeight - 2);
            g.DrawString(headers[i], SmallFont, Brushes.Black, cx + 4, y + 3);
            cx += colWidths[i];
        }
        y += rowHeight;

        // Rows
        foreach (var row in rows)
        {
            cx = x;
            for (var i = 0; i < Math.Min(row.Length, headers.Length); i++)
            {
                g.DrawRectangle(Pens.Black, cx, y, colWidths[i], rowHeight);
                g.DrawString(row[i], SmallFont, Brushes.Black, cx + 4, y + 3);
                cx += colWidths[i];
            }
            y += rowHeight;
        }

        return y;
    }
}
