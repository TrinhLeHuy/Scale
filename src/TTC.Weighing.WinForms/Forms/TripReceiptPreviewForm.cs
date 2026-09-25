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
        var margin = 30;
        var contentWidth = pageWidth - margin * 2;
        var y = margin;

        // ── Viền phiếu ──
        using var borderPen = new Pen(AgrisTheme.BrandGreenDark, 2f);
        g.DrawRectangle(borderPen, 2, 2, pageWidth - 4, ((Control)sender).Height - 4);

        // ── Logo text (thay cho ảnh logo) ──
        using var logoFont = new Font(AgrisTheme.FontFamily, 16F, FontStyle.Bold);
        using var logoBrush = new SolidBrush(AgrisTheme.BrandGreen);
        g.DrawString("TTC", logoFont, logoBrush, margin, y);

        using var logoSubFont = new Font(AgrisTheme.FontFamily, 8F, FontStyle.Bold);
        g.DrawString("SUGAR", logoSubFont, logoBrush, margin, y + 24);
        y += 10;

        // ── Tiêu đề ──
        using var titleFont = new Font(AgrisTheme.FontFamily, 16F, FontStyle.Bold);
        var titleText = Loc.T("receipt.card_title");
        var titleSize = g.MeasureString(titleText, titleFont);
        var titleX = margin + (contentWidth - titleSize.Width) / 2;
        g.DrawString(titleText, titleFont, Brushes.Black, titleX, y);
        y += (int)titleSize.Height + 4;

        // ── Nhãn BẢN IN LẠI ──
        if (_isReprint)
        {
            using var reprintFont = new Font(AgrisTheme.FontFamily, 10F, FontStyle.Bold);
            using var reprintBrush = new SolidBrush(AgrisTheme.Danger);
            var reprintText = Loc.T("receipt.reprint_label");
            var reprintSize = g.MeasureString(reprintText, reprintFont);
            g.DrawString(reprintText, reprintFont, reprintBrush,
                margin + (contentWidth - reprintSize.Width) / 2, y);
            y += (int)reprintSize.Height + 4;
        }

        // ── QR placeholder (góc phải trên) ──
        var qrSize = 100;
        var qrX = pageWidth - margin - qrSize;
        var qrY = margin;
        DrawQrPlaceholder(g, qrX, qrY, qrSize, _data.QrToken);

        y += 12;

        // ── Thông tin phiếu ──
        using var labelFont = new Font(AgrisTheme.FontFamily, 9.5F, FontStyle.Regular);
        using var valueFont = new Font(AgrisTheme.FontFamily, 9.5F, FontStyle.Bold);
        using var textBrush = new SolidBrush(AgrisTheme.TextPrimary);

        var labelX = margin;
        var valueX = margin + 140;
        var lineHeight = 22;

        DrawField(g, Loc.T("receipt.field_date"), _data.DateTime, labelFont, valueFont, textBrush, labelX, valueX, ref y, lineHeight);
        DrawField(g, Loc.T("receipt.field_reg_no"), _data.TripNo, labelFont, valueFont, textBrush, labelX, valueX, ref y, lineHeight);
        DrawField(g, Loc.T("receipt.field_gate"), _data.Gate, labelFont, valueFont, textBrush, labelX, valueX, ref y, lineHeight);
        DrawField(g, Loc.T("receipt.field_transporter"), _data.Transporter, labelFont, valueFont, textBrush, labelX, valueX, ref y, lineHeight);
        DrawField(g, Loc.T("receipt.field_driver"), _data.DriverName, labelFont, valueFont, textBrush, labelX, valueX, ref y, lineHeight);
        DrawField(g, Loc.T("receipt.field_id_card"), _data.DriverIdCard, labelFont, valueFont, textBrush, labelX, valueX, ref y, lineHeight);
        DrawField(g, Loc.T("receipt.field_plate"), _data.Plate, labelFont, valueFont, textBrush, labelX, valueX, ref y, lineHeight);
        DrawField(g, Loc.T("receipt.field_trailer"), _data.TrailerPlate, labelFont, valueFont, textBrush, labelX, valueX, ref y, lineHeight);

        // ── Separator ──
        y += 6;
        using var sepPen = new Pen(AgrisTheme.Border, 1f);
        g.DrawLine(sepPen, margin, y, pageWidth - margin, y);
        y += 8;

        DrawField(g, Loc.T("receipt.field_order"), _data.ShipmentNo, labelFont, valueFont, textBrush, labelX, valueX, ref y, lineHeight);
        DrawField(g, Loc.T("receipt.field_warehouse"), _data.Warehouse, labelFont, valueFont, textBrush, labelX, valueX, ref y, lineHeight);
        DrawField(g, Loc.T("receipt.field_commodity"), _data.Commodity, labelFont, valueFont, textBrush, labelX, valueX, ref y, lineHeight);
        DrawField(g, Loc.T("receipt.field_customer"), _data.CustomerOrSupplier, labelFont, valueFont, textBrush, labelX, valueX, ref y, lineHeight);

        // ── Thông tin mía (nếu có) ──
        if (!string.IsNullOrWhiteSpace(_data.SugarcaneType) ||
            !string.IsNullOrWhiteSpace(_data.SugarcaneOwner))
        {
            y += 6;
            g.DrawLine(sepPen, margin, y, pageWidth - margin, y);
            y += 8;

            DrawField(g, Loc.T("receipt.field_sugarcane_type"), _data.SugarcaneType, labelFont, valueFont, textBrush, labelX, valueX, ref y, lineHeight);
            DrawField(g, Loc.T("receipt.field_sugarcane_owner"), _data.SugarcaneOwner, labelFont, valueFont, textBrush, labelX, valueX, ref y, lineHeight);
            DrawField(g, Loc.T("receipt.field_variety"), _data.SugarcaneVariety, labelFont, valueFont, textBrush, labelX, valueX, ref y, lineHeight);
            DrawField(g, Loc.T("receipt.field_harvest"), _data.HarvestMethod, labelFont, valueFont, textBrush, labelX, valueX, ref y, lineHeight);
            DrawField(g, Loc.T("receipt.field_station"), _data.Station, labelFont, valueFont, textBrush, labelX, valueX, ref y, lineHeight);
            DrawField(g, Loc.T("receipt.field_crop"), _data.CropSeason, labelFont, valueFont, textBrush, labelX, valueX, ref y, lineHeight);
        }

        // ── STT hàng chờ ──
        if (_data.QueueSequenceNumber.HasValue)
        {
            y += 10;
            using var seqFont = new Font(AgrisTheme.FontFamily, 12F, FontStyle.Bold);
            g.DrawString(Loc.T("receipt.seq_label", _data.QueueSequenceNumber), seqFont, logoBrush, labelX, y);
        }

        // ── Mã QR text ──
        if (!string.IsNullOrWhiteSpace(_data.QrToken))
        {
            y += 30;
            using var qrLabelFont = new Font(AgrisTheme.FontFamily, 8F, FontStyle.Italic);
            using var qrTextBrush = new SolidBrush(AgrisTheme.TextMuted);
            g.DrawString(Loc.T("receipt.qr_label", _data.QrToken), qrLabelFont, qrTextBrush, labelX, y);
        }
    }

    private static void DrawField(
        Graphics g, string label, string? value,
        Font labelFont, Font valueFont, Brush brush,
        int labelX, int valueX, ref int y, int lineHeight)
    {
        var displayValue = string.IsNullOrWhiteSpace(value) ? Loc.T("receipt.no_value") : value;
        g.DrawString(label, labelFont, brush, labelX, y);
        g.DrawString(displayValue, valueFont, brush, valueX, y);
        y += lineHeight;
    }

    /// <summary>
    /// Vẽ khung QR placeholder. Nếu cài thêm QRCoder NuGet thì thay bằng QR thật.
    /// </summary>
    private static void DrawQrPlaceholder(Graphics g, int x, int y, int size, string? qrToken)
    {
        // Vẽ khung
        using var framePen = new Pen(Color.Black, 1.5f);
        g.DrawRectangle(framePen, x, y, size, size);

        if (string.IsNullOrWhiteSpace(qrToken))
        {
            using var noQrFont = new Font(AgrisTheme.FontFamily, 8F, FontStyle.Italic);
            g.DrawString(Loc.T("receipt.no_qr"), noQrFont, Brushes.Gray,
                new RectangleF(x + 4, y + 20, size - 8, size - 24),
                new StringFormat { Alignment = StringAlignment.Center });
            return;
        }

        // Vẽ pattern giả lập QR (grid checkerboard) để nhìn giống QR code
        var cellCount = 21; // QR version 1 = 21x21 modules
        var cellSize = (float)size / cellCount;
        var hash = qrToken.GetHashCode();
        var rng = new Random(hash);

        using var blackBrush = new SolidBrush(Color.Black);

        // 3 finder patterns (góc trên-trái, trên-phải, dưới-trái)
        DrawFinderPattern(g, blackBrush, x, y, cellSize);
        DrawFinderPattern(g, blackBrush, x + (cellCount - 7) * cellSize, y, cellSize);
        DrawFinderPattern(g, blackBrush, x, y + (cellCount - 7) * cellSize, cellSize);

        // Random data modules
        for (var row = 0; row < cellCount; row++)
        {
            for (var col = 0; col < cellCount; col++)
            {
                // Skip finder pattern areas
                if ((row < 8 && col < 8) || (row < 8 && col >= cellCount - 8) || (row >= cellCount - 8 && col < 8))
                    continue;

                if (rng.Next(2) == 1)
                {
                    g.FillRectangle(blackBrush,
                        x + col * cellSize, y + row * cellSize,
                        cellSize, cellSize);
                }
            }
        }
    }

    private static void DrawFinderPattern(Graphics g, Brush brush, float x, float y, float cellSize)
    {
        // Outer 7x7 border
        g.FillRectangle(brush, x, y, 7 * cellSize, cellSize);
        g.FillRectangle(brush, x, y + 6 * cellSize, 7 * cellSize, cellSize);
        g.FillRectangle(brush, x, y, cellSize, 7 * cellSize);
        g.FillRectangle(brush, x + 6 * cellSize, y, cellSize, 7 * cellSize);

        // Inner 3x3 block
        g.FillRectangle(brush, x + 2 * cellSize, y + 2 * cellSize, 3 * cellSize, 3 * cellSize);
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
    int? QueueSequenceNumber);
