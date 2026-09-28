using System.Drawing.Printing;
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
        doc.PrintPage += (_, e) => template.Render(e.Graphics!, e.MarginBounds);

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
        doc.PrintPage += (_, e) => template.Render(e.Graphics!, e.MarginBounds);
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
    public static readonly Font TitleFont = new("Times New Roman", 16, FontStyle.Bold);
    public static readonly Font HeaderFont = new("Times New Roman", 10, FontStyle.Bold);
    public static readonly Font BodyFont = new("Times New Roman", 10);
    public static readonly Font SmallFont = new("Times New Roman", 8);
    public static readonly Font CompanyFont = new("Times New Roman", 11, FontStyle.Bold);
    public static readonly Font AddressFont = new("Times New Roman", 9, FontStyle.Italic);
    public static readonly Font BigNumberFont = new("Times New Roman", 14, FontStyle.Bold);

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
        var text = $"Từ ngày: {fromDate}    Đến ngày: {toDate}";
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
