using TTC.Weighing.WinForms.Controls;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Printing;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Forms;

/// <summary>
/// Popup xem trước + in phiếu cân. Phiếu mía và phiếu vật tư dùng chung khung xem trước/in,
/// mỗi loại một renderer riêng (vật tư không in các mục chỉ dành cho mía).
/// </summary>
public sealed class WeighTicketPreviewForm : Form
{
    /// <summary>Khổ xem trước theo tỉ lệ A4 dọc (210 × 297).</summary>
    private const int PaperWidth = 900;
    private const int PaperHeight = 1273;

    private readonly Action<Graphics, Rectangle> _render;

    /// <summary>Phiếu cân mía.</summary>
    public WeighTicketPreviewForm(PhieuCanData data)
        : this(Loc.T("print.weigh_form_title", data.SoDangTai ?? ""),
            (g, b) => new PhieuCanGdiRenderer(data).Render(g, b))
    {
    }

    /// <summary>Phiếu cân vật tư.</summary>
    public WeighTicketPreviewForm(PhieuCanVatTuData data)
        : this(Loc.T("print.mat_form_title", data.SoDangTai ?? ""),
            (g, b) => new PhieuCanVatTuGdiRenderer(data).Render(g, b))
    {
    }

    private WeighTicketPreviewForm(string title, Action<Graphics, Rectangle> render)
    {
        _render = render;
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        BackColor = AgrisTheme.Canvas;
        Font = AgrisTheme.Body();

        // Vừa màn hình đang dùng: rộng đủ khổ giấy, cao tối đa 92% vùng làm việc (phần còn lại cuộn).
        var work = Screen.FromPoint(Cursor.Position).WorkingArea;
        ClientSize = new Size(Math.Min(PaperWidth + 60, work.Width - 40), Math.Min(PaperHeight + 90, (int)(work.Height * 0.92)));
        MinimumSize = new Size(640, 520);

        var desk = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(214, 219, 226), AutoScroll = true };
        var paper = new DoubleBufferedPanel { Size = new Size(PaperWidth, PaperHeight), BackColor = Color.White };
        paper.Paint += (s, e) => _render(e.Graphics, new Rectangle(0, 0, paper.Width, paper.Height));
        desk.Controls.Add(paper);

        // Giấy luôn nằm giữa theo chiều ngang, cách mép trên 20px.
        void CenterPaper() => paper.Location = new Point(
            Math.Max(20, (desk.ClientSize.Width - paper.Width) / 2) + desk.AutoScrollPosition.X,
            20 + desk.AutoScrollPosition.Y);
        desk.Resize += (_, _) => CenterPaper();
        desk.AutoScrollMargin = new Size(20, 20);

        var btnPrint = GridBuilder.PrimaryButton("🖨  " + Loc.T("common.print"), new Size(150, 40));
        var btnClose = GridBuilder.SecondaryButton(Loc.T("common.close"), new Size(120, 40));
        btnPrint.Click += (_, _) => Print();
        btnClose.Click += (_, _) => Close();

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 60, FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12, 10, 12, 8), BackColor = AgrisTheme.SurfaceMuted
        };
        btnPrint.Margin = new Padding(0, 0, 10, 0);
        bar.Controls.Add(btnClose);
        bar.Controls.Add(btnPrint);

        Controls.Add(desk);
        Controls.Add(bar);
        Shown += (_, _) => CenterPaper();
        AcceptButton = btnPrint;
        CancelButton = btnClose;
    }

    private void Print()
    {
        try
        {
            using var doc = new System.Drawing.Printing.PrintDocument();
            doc.PrintPage += (_, pe) => _render(pe.Graphics!, pe.MarginBounds);
            using var dlg = new PrintDialog { Document = doc };
            if (dlg.ShowDialog(this) == DialogResult.OK) doc.Print();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.T("quality.print_error", ex.Message), Loc.T("common.error"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private sealed class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
    }
}

/// <summary>
/// Khung bố cục chung cho phiếu cân: header, hai cột "nhãn : giá trị" tự xuống dòng (không tràn sang cột bên),
/// dòng toàn chiều rộng, đường phân cách, khối chữ ký ba cột. Dùng cho cả xem trước và in thật.
/// </summary>
internal sealed class TicketCanvas : IDisposable
{
    private const string Family = "Times New Roman"; // Latin như mẫu giấy; Khmer/Lào PrintText tự đổi font theo đoạn
    private const float ColumnGap = 28f;
    private const float RowGap = 5f;

    public readonly Font Company = new(Family, 13, FontStyle.Bold);
    public readonly Font Address = new(Family, 9, FontStyle.Italic);
    public readonly Font Title = new(Family, 17, FontStyle.Bold);
    public readonly Font Subtitle = new(Family, 11, FontStyle.Bold);
    public readonly Font Label = new(Family, 10);
    public readonly Font Value = new(Family, 10, FontStyle.Bold);
    public readonly Font Big = new(Family, 12, FontStyle.Bold);
    public readonly Font Highlight = new(Family, 15, FontStyle.Bold);
    public readonly Font Small = new(Family, 9);
    public readonly Font SmallItalic = new(Family, 9, FontStyle.Italic);
    public readonly Font Signature = new(Family, 10, FontStyle.Bold);
    private readonly Pen _separator = new(Color.FromArgb(150, 150, 150), 0.8f);

    public Graphics G { get; }
    public float Left { get; }
    public float Right { get; }
    public float Width => Right - Left;
    public float Y { get; set; }

    private float LeftColRight => Left + (Width - ColumnGap) / 2;
    private float RightColLeft => LeftColRight + ColumnGap;

    public TicketCanvas(Graphics g, Rectangle bounds)
    {
        G = g;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // Lề trong tỉ lệ với khổ (xem trước 900px ≈ 36px; khổ in nhỏ hơn thì lề nhỏ hơn).
        var margin = Math.Clamp(bounds.Width * 0.04f, 18f, 40f);
        Left = bounds.Left + margin;
        Right = bounds.Right - margin;
        Y = bounds.Top + margin;

        using var border = new Pen(AgrisTheme.BrandGreenDark, 2f);
        g.DrawRectangle(border, bounds.Left + 2, bounds.Top + 2, bounds.Width - 4, bounds.Height - 4);
    }

    public void Centered(string? text, Font font, float after = 4, Brush? brush = null)
        => Y += PrintText.DrawCenteredWrapped(G, text, font, brush ?? Brushes.Black, Left, Width, Y) + after;

    public void Separator(float before = 8, float after = 10)
    {
        Y += before;
        G.DrawLine(_separator, Left, Y, Right, Y);
        Y += after;
    }

    /// <summary>Bề rộng cột nhãn của một cột: vừa nhãn dài nhất, tối đa 48% cột (nhãn dài hơn thì xuống dòng).</summary>
    private float LabelColumn(float columnWidth, IEnumerable<string> labels)
    {
        var widest = labels.Select(l => PrintText.Measure(G, l, Label).Width).DefaultIfEmpty(0).Max() + 10;
        return Math.Clamp(widest, columnWidth * 0.30f, columnWidth * 0.48f);
    }

    /// <summary>
    /// Bảng hai cột. Mỗi dòng gồm một ô trái và một ô phải (có thể null). Ô phải null và
    /// <c>Wide</c> = true → ô trái chiếm cả chiều rộng (tên dài như chủ mía, khách hàng, kho, ghi chú).
    /// </summary>
    public void Rows(IReadOnlyList<TicketRow> rows)
    {
        var colWidth = LeftColRight - Left;
        var leftLabelW = LabelColumn(colWidth, rows.Select(r => r.Left.Label));
        var rightLabelW = LabelColumn(colWidth, rows.Where(r => r.Right is not null).Select(r => r.Right!.Value.Label));

        foreach (var row in rows)
        {
            float height;
            if (row.Right is null && row.Wide)
            {
                height = PrintText.FieldWrapped(G, row.Left.Label, row.Left.Value, Label, row.Left.Font ?? Value,
                    Brushes.Black, Left, Left + leftLabelW, Right, Y);
            }
            else
            {
                height = PrintText.FieldWrapped(G, row.Left.Label, row.Left.Value, Label, row.Left.Font ?? Value,
                    Brushes.Black, Left, Left + leftLabelW, LeftColRight, Y);
                if (row.Right is { } r)
                    height = Math.Max(height, PrintText.FieldWrapped(G, r.Label, r.Value, Label, r.Font ?? Value,
                        Brushes.Black, RightColLeft, RightColLeft + rightLabelW, Right, Y));
            }
            Y += height + RowGap;
        }
    }

    /// <summary>Ô nổi bật toàn chiều rộng (trọng lượng hàng / thanh toán): nền nhạt, viền, chữ lớn.</summary>
    public void HighlightBox(string label, string value, string? note = null, bool warning = false)
    {
        const float pad = 10;
        var labelSize = PrintText.Measure(G, label, Big);
        var valueSize = PrintText.Measure(G, value, Highlight);
        var noteH = string.IsNullOrWhiteSpace(note) ? 0 : PrintText.Measure(G, note, SmallItalic).Height + 2;

        // Nhãn trái + số phải cùng một dòng; không đủ chỗ (nhãn song ngữ dài, khổ in hẹp) thì số xuống dòng dưới nhãn.
        var stacked = labelSize.Width + valueSize.Width + 30 > Width - pad * 2;
        var valueTop = stacked ? Y + pad + labelSize.Height + 2 : Y + pad;
        var box = new RectangleF(Left, Y, Width, valueTop - Y + valueSize.Height + noteH + pad);

        using (var fill = new SolidBrush(warning ? Color.FromArgb(253, 236, 236) : Color.FromArgb(236, 246, 240)))
            G.FillRectangle(fill, box);
        using (var edge = new Pen(warning ? AgrisTheme.Danger : AgrisTheme.BrandGreenDark, 1.2f))
            G.DrawRectangle(edge, box.X, box.Y, box.Width, box.Height);

        var labelY = stacked ? Y + pad : Y + pad + (valueSize.Height - labelSize.Height) / 2;
        PrintText.Draw(G, label, Big, Brushes.Black, Left + pad, labelY);
        PrintText.DrawRight(G, value, Highlight, warning ? Brushes.DarkRed : Brushes.Black, Right - pad, valueTop);
        if (noteH > 0)
            PrintText.DrawRight(G, note, SmallItalic, warning ? Brushes.DarkRed : Brushes.DimGray, Right - pad,
                valueTop + valueSize.Height + 2);
        Y = box.Bottom + 8;
    }

    /// <summary>Mã vạch bên trái + "Ngày in" bên phải.</summary>
    public void BarcodeAndPrintedAt(string? barcode)
    {
        var top = Y;
        PrintText.DrawRight(G, PrintText.L("print.field_printed_at") + ": " + DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
            SmallItalic, Brushes.Gray, Right, top);
        if (string.IsNullOrWhiteSpace(barcode)) { Y += 20; return; }

        if (!Code128Renderer.Draw(G, barcode, (int)Left, (int)top, 300, 48))
        {
            using var fallback = new Font("Consolas", 11, FontStyle.Bold);
            G.DrawString(barcode, fallback, Brushes.Black, Left, top);
        }
        using var font = new Font("Consolas", 9);
        G.DrawString(barcode, font, Brushes.Black, Left, top + 51);
        Y = top + 70;
    }

    /// <summary>Khối chữ ký: các cột chia đều, tiêu đề tự xuống dòng trong cột của nó, chừa chỗ ký.</summary>
    public void Signatures(params string[] titles)
    {
        const float gap = 18;
        Y += 18;
        var colW = (Width - gap * (titles.Length - 1)) / titles.Length;
        var note = PrintText.L("print.sig_sign_note");
        float tallest = 0;
        for (var i = 0; i < titles.Length; i++)
        {
            var x = Left + i * (colW + gap);
            var h = PrintText.DrawCenteredWrapped(G, titles[i], Signature, Brushes.Black, x, colW, Y);
            h += PrintText.DrawCenteredWrapped(G, note, SmallItalic, Brushes.Black, x, colW, Y + h + 2) + 2;
            tallest = Math.Max(tallest, h);
        }
        Y += tallest + 70; // chỗ ký
    }

    public void Dispose()
    {
        foreach (var f in new[] { Company, Address, Title, Subtitle, Label, Value, Big, Highlight, Small, SmallItalic, Signature })
            f.Dispose();
        _separator.Dispose();
    }
}

/// <summary>Ô "nhãn : giá trị" trong <see cref="TicketCanvas.Rows"/>; Font null → font giá trị mặc định.</summary>
internal readonly record struct TicketCell(string Label, string? Value, Font? Font = null);

internal sealed record TicketRow(TicketCell Left, TicketCell? Right = null, bool Wide = false);

/// <summary>
/// Phiếu cân mía — mẫu nhà máy Campuchia. Nhãn song ngữ theo BU của tài khoản (ví dụ KSK: Khmer/Việt),
/// ô trống in "Không có".
/// </summary>
internal sealed class PhieuCanGdiRenderer(PhieuCanData d)
{
    public void Render(Graphics g, Rectangle b)
    {
        using var c = new TicketCanvas(g, b);
        var ton = PrintText.L("print.unit_ton");
        string? Ton(decimal? v) => v.HasValue ? $"{v:N3} {ton}" : null;
        string? Time(DateTime? t) => t?.ToString("dd/MM/yyyy HH:mm");

        c.Centered(PrintText.V(d.CompanyName), c.Company, 2);
        if (!string.IsNullOrWhiteSpace(d.CompanyAddress)) c.Centered(d.CompanyAddress, c.Address, 2);
        c.Y += 6;
        c.Centered(PrintText.L("print.weigh_title"), c.Title, 12);

        // ── Thông tin chuyến ──
        c.Rows(
        [
            new(new(PrintText.L("print.weigh_ticket_no"), d.PhieuCanSo),
                new(PrintText.L("print.field_date"), d.Ngay?.ToString("dd/MM/yyyy"))),
            new(new(PrintText.L("print.dangtai_so_dangtai"), d.SoDangTai),
                new(PrintText.L("print.field_so_xe_short"), d.SoXe)),
            new(new(PrintText.L("print.field_chu_mia"), d.TenChuMia), Wide: true),
            new(new(PrintText.L("print.field_hop_dong"), d.HopDongSo),
                new(PrintText.L("print.dangtai_lenh_don"), d.LenhDon)),
            new(new(PrintText.L("print.field_cong_thu_hoach"), d.CongThuHoach),
                new(PrintText.L("print.field_cong_boc_xep"), d.CongBocXep)),
            new(new(PrintText.L("print.field_giong_mia"), d.GiongMia),
                new(PrintText.L("print.field_loai_mia"), d.LoaiMia)),
            new(new(PrintText.L("print.field_hinh_thuc_thu_hoach"), d.HinhThucThuHoach), Wide: true)
        ]);

        c.Separator();

        // ── Kết quả cân ──
        c.Rows(
        [
            new(new(PrintText.L("print.weigh_gross"), Ton(d.TrongLuongXi), c.Big),
                new(PrintText.L("print.weigh_gross_time"), Time(d.ThoiGianCanXi))),
            new(new(PrintText.L("print.weigh_tare"), Ton(d.TrongLuongBi), c.Big),
                new(PrintText.L("print.weigh_tare_time"), Time(d.ThoiGianCanBi))),
            new(new(PrintText.L("print.weigh_tc_paid"), d.TapChatThanhToan.HasValue ? $"{d.TapChatThanhToan:N2} %" : null),
                // CCS thanh toán: FRM chưa có kết quả CCS → "Không có", không in 0.
                new(PrintText.L("print.weigh_ccs_paid"), d.CcsThanhToan is > 0m ? $"{d.CcsThanhToan:N2}" : null)),
            new(new(PrintText.L("print.field_ghi_chu"), d.GhiChu), Wide: true)
        ]);

        c.Y += 4;
        c.HighlightBox(PrintText.L("print.weigh_paid_weight"),
            d.TrongLuongThanhToan.HasValue ? $"{d.TrongLuongThanhToan:N3} {ton}" : PrintText.L("print.weigh_waiting_policy"),
            d.ChinhSachThuMia);

        c.Separator(4, 10);

        // ── Loại mía ──
        var x = c.Left;
        foreach (var (key, on) in new[] { ("reg.burnt_cane", d.IsMiaChay), ("reg.cane_whole", d.IsMiaCay), ("reg.cane_cut", d.IsMiaKhuc) })
            x = DrawCheckbox(g, PrintText.L(key), on, c.Small, x, c.Y) + 28;
        c.Y += 30;

        c.BarcodeAndPrintedAt(d.Barcode);
        c.Signatures(PrintText.L("print.sig_receiver"), PrintText.L("print.sig_deliverer"), PrintText.L("print.sig_nguoi_can"));
    }

    /// <summary>Ô tick + chữ; trả toạ độ x cuối chữ.</summary>
    private static float DrawCheckbox(Graphics g, string text, bool isChecked, Font font, float x, float y)
    {
        const int boxSize = 13;
        g.DrawRectangle(Pens.Black, x, y + 2, boxSize, boxSize);
        if (isChecked)
        {
            using var pen = new Pen(Color.Black, 2f);
            g.DrawLine(pen, x + 2, y + 9, x + 5, y + 13);
            g.DrawLine(pen, x + 5, y + 13, x + 11, y + 4);
        }
        PrintText.Draw(g, text, font, Brushes.Black, x + boxSize + 5, y);
        return x + boxSize + 5 + PrintText.Measure(g, text, font).Width;
    }
}

/// <summary>
/// Phiếu cân vật tư (luồng ERP / WMS): chứng từ ERP (Shipment, TRIP, hướng nhập/xuất), khách hàng/NCC,
/// hàng hóa, kho, vận chuyển và số cân kg. Không in mục chỉ dành cho mía (chủ mía, lệnh đốn, tạp chất, CCS…).
/// </summary>
internal sealed class PhieuCanVatTuGdiRenderer(PhieuCanVatTuData d)
{
    public void Render(Graphics g, Rectangle b)
    {
        using var c = new TicketCanvas(g, b);
        var kg = PrintText.L("print.unit_kg");
        string? Kg(decimal? v) => v.HasValue ? $"{v:N3} {kg}" : null; // số cân kg thống nhất N3
        string? Time(DateTime? t) => t?.ToString("dd/MM/yyyy HH:mm");

        c.Centered(PrintText.V(d.CompanyName), c.Company, 2);
        if (!string.IsNullOrWhiteSpace(d.CompanyAddress)) c.Centered(d.CompanyAddress, c.Address, 2);
        c.Y += 6;
        c.Centered(PrintText.L("print.mat_title"), c.Title, 2);
        if (!string.IsNullOrWhiteSpace(d.Huong))
            c.Centered("— " + d.Huong.ToUpperInvariant() + " —", c.Subtitle, 4);
        c.Y += 8;

        // ── Chứng từ + xe ──
        c.Rows(
        [
            new(new(PrintText.L("print.weigh_ticket_no"), d.PhieuCanSo),
                new(PrintText.L("print.field_date"), d.Ngay?.ToString("dd/MM/yyyy"))),
            new(new(PrintText.L("print.dangtai_so_dangtai"), d.SoDangTai),
                new(PrintText.L("print.field_so_xe_short"), d.SoXe)),
            new(new(PrintText.L("print.mat_shipment"), d.Shipment),
                new(PrintText.L("print.mat_trip"), d.SoTrip)),
            new(new(PrintText.L("print.mat_direction"), d.Huong),
                new(PrintText.L("print.mat_driver"), d.LaiXe))
        ]);

        c.Separator(4, 10);

        // ── Hàng hóa + đối tác ──
        c.Rows(
        [
            new(new(PrintText.L("print.mat_customer"), d.KhachHangNcc), Wide: true),
            new(new(PrintText.L("print.mat_item"),
                string.IsNullOrWhiteSpace(d.MaHang) ? d.HangHoa : $"{d.HangHoa} ({d.MaHang})"), Wide: true),
            new(new(PrintText.L("print.mat_warehouse"), d.Kho), Wide: true),
            new(new(PrintText.L("print.mat_transporter"), d.NhaVanChuyen), Wide: true)
        ]);

        c.Separator(4, 10);

        // ── Kết quả cân ──
        c.Rows(
        [
            new(new(PrintText.L("print.mat_weight_in"), Kg(d.TrongLuongVao), c.Big),
                new(PrintText.L("print.mat_time_in"), Time(d.ThoiGianVao))),
            new(new(PrintText.L("print.mat_weight_out"), Kg(d.TrongLuongRa), c.Big),
                new(PrintText.L("print.mat_time_out"), Time(d.ThoiGianRa)))
        ]);

        c.Y += 4;
        // Trọng lượng âm không bị che (không Math.Abs) — in rõ và cảnh báo để xử lý ngoại lệ.
        var negative = d.TrongLuongHang is < 0m;
        c.HighlightBox(PrintText.L("print.mat_net"),
            Kg(d.TrongLuongHang) ?? PrintText.None,
            negative ? PrintText.L("print.mat_net_negative") : null,
            warning: negative);

        c.Rows([new(new(PrintText.L("print.field_ghi_chu"), d.GhiChu), Wide: true)]);

        c.Separator(4, 12);
        c.BarcodeAndPrintedAt(d.Barcode);
        c.Signatures(PrintText.L("print.sig_deliverer"), PrintText.L("print.sig_receiver"), PrintText.L("print.sig_nguoi_can"));
    }
}
