using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.WinForms.Controls;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Forms;

/// <summary>
/// Popup xem trước + in phiếu đăng tài (THẺ ĐĂNG TÀI).
/// Hiển thị preview phiếu giống mẫu in thật: logo area, tiêu đề, thông tin chuyến, mã QR,
/// toolbar chọn máy in / số bản / nút Print.
/// Gọi <see cref="IWeighTicketPrintService.PrintAsync"/> khi bấm Print.
/// Lỗi in không ảnh hưởng dữ liệu đăng tài đã lưu (Req 8.5).
/// In lại gắn nhãn BẢN IN LẠI (Req 8.3).
/// </summary>
public sealed class TripReceiptPreviewForm : Form
{
    // ── Data ──
    private readonly TripReceiptData _data;
    private readonly IWeighTicketPrintService _printService;
    private readonly bool _isReprint;

    // ── Toolbar controls ──
    private readonly ComboBox _cboPrinter = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 260,
        AccessibleName = "Chọn máy in"
    };
    private readonly NumericUpDown _nudCopies = new()
    {
        Minimum = 1,
        Maximum = 10,
        Value = 1,
        Width = 55,
        AccessibleName = "Số bản in"
    };
    private readonly Button _btnPrint;
    private readonly Button _btnClose;

    // ── Preview panel (GDI+ vẽ phiếu) ──
    private readonly Panel _pnlPreview;

    // ── Printer list cache ──
    private IReadOnlyList<PrinterInfo>? _printers;

    /// <summary>Kết quả in (null nếu chưa in hoặc hủy).</summary>
    public PrintTicketResult? PrintResult { get; private set; }

    public TripReceiptPreviewForm(
        TripReceiptData data,
        IWeighTicketPrintService printService,
        bool isReprint)
    {
        _data = data;
        _printService = printService;
        _isReprint = isReprint;

        Text = isReprint ? Loc.T("receipt.title_reprint") : Loc.T("receipt.title");
        ClientSize = new Size(620, 700);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = AgrisTheme.Canvas;
        Font = AgrisTheme.Body();

        // ── Toolbar ──
        var toolbar = BuildToolbar();

        // ── Preview panel ──
        _pnlPreview = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(230, 230, 230),
            Padding = new Padding(20),
            AutoScroll = true
        };

        var receiptPanel = new DoubleBufferedPanel
        {
            Size = new Size(560, 560),
            BackColor = Color.White,
            Location = new Point(10, 10)
        };
        receiptPanel.Paint += OnPaintReceipt;
        _pnlPreview.Controls.Add(receiptPanel);

        // ── Buttons ──
        _btnPrint = GridBuilder.PrimaryButton("🖨 Print", new Size(100, 36));
        _btnPrint.Click += OnPrintClicked;
        _btnPrint.Dock = DockStyle.Right;

        _btnClose = GridBuilder.SecondaryButton(Loc.T("common.close"), new Size(80, 36));
        _btnClose.Click += (_, _) => Close();
        _btnClose.Dock = DockStyle.Right;

        var bottomBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            Padding = new Padding(10, 7, 10, 7),
            BackColor = AgrisTheme.SurfaceMuted
        };
        bottomBar.Controls.Add(_btnPrint);
        bottomBar.Controls.Add(_btnClose);

        Controls.Add(_pnlPreview);
        Controls.Add(toolbar);
        Controls.Add(bottomBar);

        Load += async (_, _) => await LoadPrintersAsync();
    }

    // ═══════════════════════════════════════════════════════════
    //  Toolbar
    // ═══════════════════════════════════════════════════════════

    private Panel BuildToolbar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = AgrisTheme.SurfaceMuted,
            Padding = new Padding(10, 6, 10, 6)
        };

        var lblPrinter = new Label
        {
            Text = Loc.T("receipt.label_printer"),
            AutoSize = true,
            Font = AgrisTheme.Body(9F),
            ForeColor = AgrisTheme.TextPrimary,
            Margin = new Padding(0, 6, 4, 0)
        };

        var lblCopies = new Label
        {
            Text = Loc.T("receipt.label_copies"),
            AutoSize = true,
            Font = AgrisTheme.Body(9F),
            ForeColor = AgrisTheme.TextPrimary,
            Margin = new Padding(12, 6, 4, 0)
        };

        _cboPrinter.Margin = new Padding(0, 2, 0, 0);
        _nudCopies.Margin = new Padding(0, 2, 0, 0);

        bar.Controls.AddRange([lblPrinter, _cboPrinter, lblCopies, _nudCopies]);

        return bar;
    }

    // ═══════════════════════════════════════════════════════════
    //  Load printers
    // ═══════════════════════════════════════════════════════════

    private async Task LoadPrintersAsync()
    {
        try
        {
            _printers = await _printService.GetAvailablePrintersAsync();

            _cboPrinter.Items.Clear();
            foreach (var p in _printers)
            {
                var display = p.IsReady ? p.Name : $"{p.Name}  {Loc.T("receipt.printer_not_ready")}";
                _cboPrinter.Items.Add(display);
            }

            // Chọn máy in sẵn sàng đầu tiên
            var firstReady = -1;
            for (var i = 0; i < _printers.Count; i++)
            {
                if (_printers[i].IsReady) { firstReady = i; break; }
            }
            if (_cboPrinter.Items.Count > 0)
                _cboPrinter.SelectedIndex = firstReady >= 0 ? firstReady : 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"{Loc.T("receipt.printer_error")}{Environment.NewLine}{ex.Message}",
                Loc.T("receipt.printer_error_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Print
    // ═══════════════════════════════════════════════════════════

    private async void OnPrintClicked(object? sender, EventArgs e)
    {
        if (_cboPrinter.SelectedItem is not string printerDisplay || _data.TripId is null)
        {
            MessageBox.Show(Loc.T("receipt.select_printer"), Loc.T("common.missing_info"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Lấy tên máy in gốc (bỏ hậu tố trạng thái nếu có)
        var printerName = printerDisplay.Contains("  (")
            ? printerDisplay[..printerDisplay.IndexOf("  (", StringComparison.Ordinal)]
            : printerDisplay;

        var config = new PrinterConfig(
            PrinterName: printerName,
            Copies: (int)_nudCopies.Value,
            TemplateCode: "DANG_TAI");

        _btnPrint.Enabled = false;
        try
        {
            PrintResult = await _printService.PrintAsync(
                _data.TripId.Value, config, _isReprint);

            if (PrintResult.IsSuccess)
            {
                var reprintNote = _isReprint ? Loc.T("receipt.print_reprint_note") : string.Empty;
                MessageBox.Show(
                    Loc.T("receipt.print_ok", reprintNote, PrintResult.TicketNumber),
                    Loc.T("receipt.title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(
                    Loc.T("receipt.print_failed", PrintResult.ErrorMessage),
                    Loc.T("receipt.print_error_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Loc.T("receipt.print_error", ex.Message),
                Loc.T("receipt.print_error_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);

            PrintResult = new PrintTicketResult(false, null, ex.Message);
        }
        finally
        {
            _btnPrint.Enabled = true;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  GDI+ vẽ phiếu preview
    // ═══════════════════════════════════════════════════════════

    private void OnPaintReceipt(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var pageWidth = ((Control)sender!).Width;
        var pageHeight = ((Control)sender).Height;
        var margin = 30;
        var contentWidth = pageWidth - margin * 2;
        var y = margin;

        // ── Viền phiếu — dùng màu BrandGreenDark ──
        using var borderPen = new Pen(AgrisTheme.BrandGreenDark, 2f);
        g.DrawRectangle(borderPen, 2, 2, pageWidth - 4, pageHeight - 4);

        // ── Company header — centered, bold ──
        using var companyFont = new Font(AgrisTheme.FontFamily, 12F, FontStyle.Bold);
        var companySz = g.MeasureString(_data.Factory ?? "", companyFont, contentWidth);
        var companyText = _data.Factory ?? "";
        g.DrawString(companyText, companyFont, Brushes.Black,
            margin + (contentWidth - companySz.Width) / 2, y);
        y += (int)companySz.Height + 2;

        // Factory address
        if (!string.IsNullOrWhiteSpace(_data.FactoryAddress))
        {
            using var addrFont = new Font(AgrisTheme.FontFamily, 8.5F, FontStyle.Italic);
            var addrSz = g.MeasureString(_data.FactoryAddress, addrFont, contentWidth);
            g.DrawString(_data.FactoryAddress, addrFont, Brushes.Black,
                margin + (contentWidth - addrSz.Width) / 2, y);
            y += (int)addrSz.Height + 4;
        }

        y += 8;

        // ── Title — centered, bold, large, system color ──
        using var titleFont = new Font(AgrisTheme.FontFamily, 18F, FontStyle.Bold);
        using var titleBrush = new SolidBrush(AgrisTheme.BrandGreenDark);
        var titleText = Loc.T("print.dangtai_title");
        var titleSz = g.MeasureString(titleText, titleFont);
        g.DrawString(titleText, titleFont, titleBrush,
            margin + (contentWidth - titleSz.Width) / 2, y);
        y += (int)titleSz.Height + 4;

        // ── Nhãn BẢN IN LẠI ──
        if (_isReprint)
        {
            using var reprintFont = new Font(AgrisTheme.FontFamily, 10F, FontStyle.Bold);
            using var reprintBrush = new SolidBrush(AgrisTheme.Danger);
            var reprintText = Loc.T("receipt.reprint_label");
            var reprintSz = g.MeasureString(reprintText, reprintFont);
            g.DrawString(reprintText, reprintFont, reprintBrush,
                margin + (contentWidth - reprintSz.Width) / 2, y);
            y += (int)reprintSz.Height + 4;
        }

        y += 10;

        // ── Fields — matching phiếu đăng tài mẫu ──
        using var labelFont = new Font(AgrisTheme.FontFamily, 9.5F, FontStyle.Regular);
        using var valueFont = new Font(AgrisTheme.FontFamily, 9.5F, FontStyle.Bold);
        using var valueBoldFont = new Font(AgrisTheme.FontFamily, 11F, FontStyle.Bold);
        using var textBrush = new SolidBrush(AgrisTheme.TextPrimary);
        using var italicFont = new Font(AgrisTheme.FontFamily, 9.5F, FontStyle.Italic);

        var labelX = margin;
        var valueX = margin + 140;
        var rightColX = margin + 310;
        var rightValX = rightColX + 90;
        var lineHeight = 22;

        // Row 1: Lệnh đốn số (bold)  +  Ngày tháng năm (right, italic)
        DrawFieldBold(g, _data.IsMaterial ? "Shipment" : Loc.T("print.dangtai_lenh_don"), _data.ShipmentNo, labelFont, valueBoldFont, textBrush, labelX, valueX, ref y, lineHeight);
        if (!string.IsNullOrWhiteSpace(_data.DateTime))
            g.DrawString(_data.DateTime, italicFont, textBrush, rightColX, y - lineHeight);

        // Row 2: Số đăng tài (bold)
        DrawFieldBold(g, Loc.T("print.dangtai_so_dangtai"), _data.TripNo, labelFont, valueBoldFont, textBrush, labelX, valueX, ref y, lineHeight);

        if (_data.IsMaterial)
        {
            // ── VẬT TƯ ──
            DrawField(g, "Khách hàng/NCC", _data.CustomerOrSupplier, labelFont, valueFont, textBrush, labelX, valueX, ref y, lineHeight);
            DrawField(g, "Hàng hóa", _data.Commodity, labelFont, valueFont, textBrush, labelX, valueX, ref y, lineHeight);
            DrawField(g, "Kho", _data.Warehouse, labelFont, valueFont, textBrush, labelX, valueX, ref y, lineHeight);
            DrawField(g, "Biển số xe", _data.Plate, labelFont, valueFont, textBrush, labelX, valueX, ref y, lineHeight);
            DrawFieldInline(g, Loc.T("print.field_loai_xe"), _data.VehicleType, labelFont, valueFont, textBrush, rightColX, rightValX, y - lineHeight);
            if (!string.IsNullOrWhiteSpace(_data.TrailerPlate))
                DrawField(g, "Rơ moóc", _data.TrailerPlate, labelFont, valueFont, textBrush, labelX, valueX, ref y, lineHeight);
            DrawField(g, "Nhà vận tải", _data.Transporter, labelFont, valueFont, textBrush, labelX, valueX, ref y, lineHeight);
        }
        else
        {
            // ── MÍA ──
            DrawField(g, Loc.T("print.field_chu_mia"), _data.SugarcaneOwner, labelFont, valueFont, textBrush, labelX, valueX, ref y, lineHeight);
            DrawField(g, Loc.T("print.field_hop_dong"), _data.ShipmentNo, labelFont, valueFont, textBrush, labelX, valueX, ref y, lineHeight);
            DrawField(g, Loc.T("print.field_so_xe"), _data.Plate, labelFont, valueFont, textBrush, labelX, valueX, ref y, lineHeight);
            DrawFieldInline(g, Loc.T("print.field_loai_xe"), _data.VehicleType, labelFont, valueFont, textBrush, rightColX, rightValX, y - lineHeight);
            DrawField(g, Loc.T("print.field_cong_thu_hoach"), _data.Transporter, labelFont, valueFont, textBrush, labelX, valueX, ref y, lineHeight);
            DrawFieldInline(g, Loc.T("print.field_cong_boc_xep"), null, labelFont, valueFont, textBrush, rightColX, rightValX, y - lineHeight);
            DrawField(g, Loc.T("print.field_giong_mia"), _data.SugarcaneVariety, labelFont, valueFont, textBrush, labelX, valueX, ref y, lineHeight);
            DrawFieldInline(g, Loc.T("print.field_loai_mia"), _data.SugarcaneType, labelFont, valueFont, textBrush, rightColX, rightValX, y - lineHeight);
            DrawField(g, Loc.T("print.field_hinh_thuc_thu_hoach"), _data.HarvestMethod, labelFont, valueFont, textBrush, labelX, valueX, ref y, lineHeight);
        }

        // Ghi chú (chung)
        DrawField(g, Loc.T("print.field_ghi_chu"), null, labelFont, valueFont, textBrush, labelX, valueX, ref y, lineHeight);

        // ── Barcode (Code 128) ──
        var barcodeValue = _data.ShipmentNo ?? _data.QrToken;
        if (!string.IsNullOrWhiteSpace(barcodeValue))
        {
            y += 12;
            if (!Printing.Code128Renderer.Draw(g, barcodeValue, labelX, y, 280, 45))
            {
                // Fallback: vẽ text nếu encode thất bại
                using var fallbackFont = new Font(AgrisTheme.MonoFamily, 11F, FontStyle.Bold);
                g.DrawString(barcodeValue, fallbackFont, Brushes.Black, labelX, y);
            }
            y += 48;

            // Barcode text below
            using var bcTextFont = new Font(AgrisTheme.MonoFamily, 9F);
            g.DrawString(barcodeValue, bcTextFont, Brushes.Black, labelX, y);
        }

        // ── Print timestamp — bottom right ──
        var printTime = System.DateTime.Now.ToString("dd/MM/yyyy HH:mm");
        var printLabel = Loc.T("print.dangtai_ngay_in", printTime);
        using var tsFont = new Font(AgrisTheme.FontFamily, 8.5F, FontStyle.Italic);
        using var tsBrush = new SolidBrush(AgrisTheme.TextMuted);
        var tsSz = g.MeasureString(printLabel, tsFont);
        g.DrawString(printLabel, tsFont, tsBrush,
            pageWidth - margin - tsSz.Width, pageHeight - margin - tsSz.Height - 4);
    }

    private static void DrawField(
        Graphics g, string label, string? value,
        Font labelFont, Font valueFont, Brush brush,
        int labelX, int valueX, ref int y, int lineHeight)
    {
        var displayValue = string.IsNullOrWhiteSpace(value) ? "" : value;
        g.DrawString(label, labelFont, brush, labelX, y);
        g.DrawString(": " + displayValue, valueFont, brush, valueX, y);
        y += lineHeight;
    }

    private static void DrawFieldBold(
        Graphics g, string label, string? value,
        Font labelFont, Font boldFont, Brush brush,
        int labelX, int valueX, ref int y, int lineHeight)
    {
        var displayValue = string.IsNullOrWhiteSpace(value) ? "" : value;
        g.DrawString(label, labelFont, brush, labelX, y);
        g.DrawString(": " + displayValue, boldFont, brush, valueX, y);
        y += lineHeight + 2;
    }

    private static void DrawFieldInline(
        Graphics g, string label, string? value,
        Font labelFont, Font valueFont, Brush brush,
        int labelX, int valueX, int y)
    {
        var displayValue = string.IsNullOrWhiteSpace(value) ? "" : value;
        g.DrawString(label, labelFont, brush, labelX, y);
        g.DrawString(": " + displayValue, valueFont, brush, valueX, y);
    }

    // ═══════════════════════════════════════════════════════════
    //  Double-buffered panel (tránh nhấp nháy khi vẽ GDI+)
    // ═══════════════════════════════════════════════════════════

    private sealed class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
    }
}

// ═══════════════════════════════════════════════════════════
//  Data record cho preview phiếu đăng tài
// ═══════════════════════════════════════════════════════════

/// <summary>
/// Dữ liệu hiển thị trên phiếu đăng tài.
/// Tổng hợp từ RegisterTripResult + các trường form.
/// </summary>
public sealed record TripReceiptData(
    Guid? TripId,
    string? TripNo,
    string? DateTime,
    string? Gate,
    string? Plate,
    string? TrailerPlate,
    string? Transporter,
    string? DriverName,
    string? DriverIdCard,
    string? ShipmentNo,
    string? Warehouse,
    string? Commodity,
    string? CustomerOrSupplier,
    string? SugarcaneType,
    string? SugarcaneOwner,
    string? SugarcaneVariety,
    string? HarvestMethod,
    string? Station,
    string? CropSeason,
    string? QrToken,
    int? QueueSequenceNumber,
    decimal? ImpurityPercent = null,
    decimal? CcsValue = null,
    decimal? BrixValue = null,
    decimal? PolValue = null,
    decimal? PolDocValue = null,
    string? Factory = null,
    string? FactoryAddress = null,
    string? VehicleType = null,
    bool IsMaterial = false);
