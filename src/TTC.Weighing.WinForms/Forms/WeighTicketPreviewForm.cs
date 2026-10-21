using TTC.Weighing.WinForms.Controls;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Printing;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Forms;

/// <summary>
/// Popup xem trước + in phiếu cân mía. Layout theo mẫu nhà máy Campuchia:
/// Header công ty → Tiêu đề → Thông tin chuyến → Kết quả cân → Checkbox loại mía → Chữ ký.
/// </summary>
public sealed class WeighTicketPreviewForm : Form
{
    private readonly PhieuCanData _data;

    public WeighTicketPreviewForm(PhieuCanData data)
    {
        _data = data;
        Text = $"Phiếu cân mía — {data.SoDangTai}";
        ClientSize = new Size(820, 960);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = AgrisTheme.Canvas;
        Font = AgrisTheme.Body();

        var pnl = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(230, 230, 230), Padding = new Padding(10), AutoScroll = true };
        var receipt = new DoubleBufferedPanel { Size = new Size(780, 850), BackColor = Color.White, Location = new Point(10, 10) };
        receipt.Paint += OnPaint;
        pnl.Controls.Add(receipt);

        var btnPrint = GridBuilder.PrimaryButton("🖨 In", new Size(120, 36));
        var btnClose = GridBuilder.SecondaryButton(Loc.T("common.close"), new Size(100, 36));
        btnPrint.Click += (_, _) =>
        {
            try
            {
                var doc = new System.Drawing.Printing.PrintDocument();
                doc.PrintPage += (_, pe) => new PhieuCanGdiRenderer(_data).Render(pe.Graphics!, pe.MarginBounds);
                var dlg = new PrintDialog { Document = doc };
                if (dlg.ShowDialog() == DialogResult.OK) doc.Print();
            }
            catch (Exception ex) { MessageBox.Show($"Lỗi in: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        btnClose.Click += (_, _) => Close();

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8), BackColor = AgrisTheme.SurfaceMuted };
        bar.Controls.Add(btnClose);
        bar.Controls.Add(btnPrint);
        Controls.Add(pnl);
        Controls.Add(bar);
    }

    private void OnPaint(object? sender, PaintEventArgs e) =>
        new PhieuCanGdiRenderer(_data).Render(e.Graphics!, new Rectangle(0, 0, ((Control)sender!).Width, ((Control)sender).Height));

    private sealed class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel() { DoubleBuffered = true; SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); }
    }
}

/// <summary>GDI+ renderer cho phiếu cân mía — theo mẫu nhà máy Campuchia.</summary>
internal sealed class PhieuCanGdiRenderer(PhieuCanData d)
{
    public void Render(Graphics g, Rectangle b)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var pw = b.Width;
        var ph = b.Height;
        var m = 30;
        var y = m + b.Top;
        var lx = b.Left + m;
        var rightX = b.Left + pw / 2 + 20;

        // Viền
        using var borderPen = new Pen(AgrisTheme.BrandGreenDark, 2f);
        g.DrawRectangle(borderPen, b.Left + 2, b.Top + 2, pw - 4, ph - 4);

        // ── Fonts ──
        using var companyFont = new Font("Times New Roman", 12, FontStyle.Bold);
        using var addressFont = new Font("Times New Roman", 9, FontStyle.Italic);
        using var titleFont = new Font("Times New Roman", 16, FontStyle.Bold);
        using var labelFont = new Font("Times New Roman", 10);
        using var valueFont = new Font("Times New Roman", 10, FontStyle.Bold);
        using var bigFont = new Font("Times New Roman", 12, FontStyle.Bold);
        using var smallFont = new Font("Times New Roman", 9);
        using var sigFont = new Font("Times New Roman", 10, FontStyle.Bold);
        using var sepPen = new Pen(Color.Gray, 0.5f);

        // ══ HEADER ══
        if (!string.IsNullOrWhiteSpace(d.CompanyName))
        {
            var sz = g.MeasureString(d.CompanyName, companyFont);
            g.DrawString(d.CompanyName, companyFont, Brushes.Black, lx + (pw - m * 2 - sz.Width) / 2, y);
            y += (int)sz.Height + 2;
        }
        if (!string.IsNullOrWhiteSpace(d.CompanyAddress))
        {
            var sz = g.MeasureString(d.CompanyAddress, addressFont, pw - m * 2);
            g.DrawString(d.CompanyAddress, addressFont, Brushes.Black, lx + (pw - m * 2 - sz.Width) / 2, y);
            y += (int)sz.Height + 4;
        }

        // ══ TIÊU ĐỀ ══
        y += 6;
        var title = "PHIẾU CÂN MÍA";
        var titleSz = g.MeasureString(title, titleFont);
        g.DrawString(title, titleFont, Brushes.Black, lx + (pw - m * 2 - titleSz.Width) / 2, y);
        y += (int)titleSz.Height + 10;

        // ══ THÔNG TIN CHUYẾN — 2 cột ══
        var lw = 130; // label width
        var lh = 18;  // line height

        // Row 1: Phiếu cân số ... Ngày
        F(g, "Phiếu cân số:", d.PhieuCanSo, labelFont, valueFont, lx, lx + lw, ref y, lh);
        if (d.Ngay.HasValue)
        {
            var dateStr = $"Ngày {d.Ngay.Value:dd} tháng {d.Ngay.Value:MM} năm {d.Ngay.Value:yyyy}";
            g.DrawString(dateStr, new Font("Times New Roman", 10, FontStyle.Italic), Brushes.Black, rightX, y - lh);
        }

        F(g, "Số đăng tài:", d.SoDangTai, labelFont, valueFont, lx, lx + lw, ref y, lh);
        F(g, "Tên chủ mía:", d.TenChuMia, labelFont, valueFont, lx, lx + lw, ref y, lh);
        F(g, "Hợp đồng số:", d.HopDongSo, labelFont, valueFont, lx, lx + lw, ref y, lh);

        // Lệnh đốn + Số xe
        F(g, "Lệnh đốn:", d.LenhDon, labelFont, valueFont, lx, lx + lw, ref y, lh);
        FInline(g, "Số xe:", d.SoXe, labelFont, valueFont, rightX, rightX + 60, y - lh);

        // Công TH + Công BX
        F(g, "Công TH:", d.CongThuHoach, labelFont, valueFont, lx, lx + lw, ref y, lh);
        FInline(g, "Công BX:", d.CongBocXep, labelFont, valueFont, rightX, rightX + 70, y - lh);

        // Giống mía + Loại mía
        F(g, "Giống mía:", d.GiongMia, labelFont, valueFont, lx, lx + lw, ref y, lh);
        FInline(g, "Loại mía:", d.LoaiMia, labelFont, valueFont, rightX, rightX + 70, y - lh);

        // Hình thức thu hoạch
        F(g, "Hình thức TH:", d.HinhThucThuHoach, labelFont, valueFont, lx, lx + lw, ref y, lh);

        // ══ SEPARATOR ══
        y += 6;
        g.DrawLine(sepPen, lx, y, b.Right - m, y);
        y += 8;

        // ══ KẾT QUẢ CÂN ══
        // Trọng lượng xỉ + thời gian
        F(g, "Trọng lượng xỉ:", d.TrongLuongXi.HasValue ? $"{d.TrongLuongXi:N3} tấn" : "—", labelFont, bigFont, lx, lx + lw, ref y, 22);
        if (d.ThoiGianCanXi.HasValue)
            FInline(g, "Thời gian cân xỉ:", d.ThoiGianCanXi.Value.ToString("dd/MM/yyyy HH:mm"), labelFont, valueFont, rightX, rightX + 120, y - 22);

        // Trọng lượng bì + thời gian
        F(g, "Trọng lượng bì:", d.TrongLuongBi.HasValue ? $"{d.TrongLuongBi:N3} tấn" : "—", labelFont, bigFont, lx, lx + lw, ref y, 22);
        if (d.ThoiGianCanBi.HasValue)
            FInline(g, "Thời gian cân bì:", d.ThoiGianCanBi.Value.ToString("dd/MM/yyyy HH:mm"), labelFont, valueFont, rightX, rightX + 120, y - 22);

        // Tạp chất thanh toán + Ghi chú
        F(g, "Tạp chất TT:", d.TapChatThanhToan.HasValue ? $"{d.TapChatThanhToan:N2} %" : "—", labelFont, valueFont, lx, lx + lw, ref y, lh);
        if (!string.IsNullOrWhiteSpace(d.GhiChu))
            FInline(g, "Ghi chú:", d.GhiChu, labelFont, valueFont, rightX, rightX + 60, y - lh);

        // Trọng lượng thanh toán — lớn, nổi bật
        using var tlttFont = new Font("Times New Roman", 14, FontStyle.Bold);
        using var greenBrush = new SolidBrush(AgrisTheme.BrandGreen);
        var tlttText = d.TrongLuongThanhToan.HasValue ? $"{d.TrongLuongThanhToan:N3} tấn" : "Chờ chính sách";
        F(g, "TL thanh toán:", tlttText, labelFont, d.TrongLuongThanhToan.HasValue ? tlttFont : bigFont,
            lx, lx + lw, ref y, 26);

        // ══ SEPARATOR ══
        y += 6;
        g.DrawLine(sepPen, lx, y, b.Right - m, y);
        y += 8;

        // ══ CHECKBOX LOẠI MÍA ══
        var cbY = y;
        DrawCheckbox(g, "Mía cháy", d.IsMiaChay, smallFont, lx, cbY);
        DrawCheckbox(g, "Mía cây", d.IsMiaCay, smallFont, lx + 140, cbY);
        DrawCheckbox(g, "Mía khúc", d.IsMiaKhuc, smallFont, lx + 260, cbY);

        // Ngày in
        var printLabel = $"Ngày in: {DateTime.Now:dd/MM/yyyy HH:mm}";
        g.DrawString(printLabel, new Font("Times New Roman", 9, FontStyle.Italic), Brushes.Gray, rightX + 80, cbY);
        y = cbY + 24;

        // ══ BARCODE ══
        if (!string.IsNullOrWhiteSpace(d.Barcode))
        {
            y += 8;
            Code128Renderer.Draw(g, d.Barcode, lx, y, 280, 45);
            y += 48;
            g.DrawString(d.Barcode, new Font("Consolas", 9), Brushes.Black, lx, y);
            y += 16;
        }

        // ══ CHỮ KÝ ══
        y += 20;
        var sigW = (pw - m * 2) / 3;
        var sigs = new[] { "ĐĐ ĐƠN VỊ NHẬN HÀNG\n(Ký, Ghi rõ họ tên)", "ĐĐ ĐƠN VỊ GIAO HÀNG\n(Ký, Ghi rõ họ tên)", "NGƯỜI CÂN\n(Ký, Ghi rõ họ tên)" };
        using var sigSf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near };
        for (var i = 0; i < 3; i++)
        {
            var sigRect = new RectangleF(lx + i * sigW, y, sigW, 50);
            g.DrawString(sigs[i], sigFont, Brushes.Black, sigRect, sigSf);
        }
    }

    private static void F(Graphics g, string label, string? value, Font lf, Font vf, int lx, int vx, ref int y, int lh)
    {
        g.DrawString(label, lf, Brushes.Black, lx, y);
        g.DrawString(string.IsNullOrWhiteSpace(value) ? "" : value, vf, Brushes.Black, vx, y);
        y += lh;
    }

    private static void FInline(Graphics g, string label, string? value, Font lf, Font vf, int lx, int vx, int y)
    {
        g.DrawString(label, lf, Brushes.Black, lx, y);
        g.DrawString(string.IsNullOrWhiteSpace(value) ? "" : value, vf, Brushes.Black, vx, y);
    }

    private static void DrawCheckbox(Graphics g, string text, bool isChecked, Font font, int x, int y)
    {
        var boxSize = 12;
        g.DrawRectangle(Pens.Black, x, y + 2, boxSize, boxSize);
        if (isChecked)
        {
            using var pen = new Pen(Color.Black, 2f);
            g.DrawLine(pen, x + 2, y + 8, x + 5, y + 12);
            g.DrawLine(pen, x + 5, y + 12, x + 10, y + 4);
        }
        g.DrawString(text, font, Brushes.Black, x + boxSize + 4, y);
    }
}
