using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.WinForms.Controls;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Printing;
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

    /// <summary>Khổ xem trước phiếu đăng tài (rộng × cao = 1 : 0.8).</summary>
    private const int ReceiptWidth = 860;
    private const double ReceiptAspect = 0.8;

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
        // Khổ rộng để nhãn song ngữ (Khmer/Lào + Việt) và giá trị dài hiển thị đủ; phần thừa thì cuộn.
        var receiptSize = new Size(ReceiptWidth, (int)(ReceiptWidth * ReceiptAspect));
        var work = Screen.FromPoint(Cursor.Position).WorkingArea;
        ClientSize = new Size(Math.Min(receiptSize.Width + 80, work.Width - 40),
            Math.Min(receiptSize.Height + 160, (int)(work.Height * 0.92)));
        MinimumSize = new Size(600, 480);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
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
            Size = receiptSize,
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
                // Service chỉ ghi nhận phiếu + audit; in ra máy đúng mẫu đang xem trước.
                using (var doc = new System.Drawing.Printing.PrintDocument())
                {
                    doc.PrinterSettings.PrinterName = printerName;
                    doc.PrinterSettings.Copies = (short)_nudCopies.Value;
                    doc.DocumentName = Text;
                    doc.PrintPage += (_, pe) =>
                    {
                        // Cùng tỉ lệ khung như xem trước; khổ giấy hẹp thì chữ tự xuống dòng thay vì bị cắt.
                        var area = pe.MarginBounds;
                        var height = Math.Min(area.Height, (int)(area.Width * ReceiptAspect));
                        RenderReceipt(pe.Graphics!, new Rectangle(area.Left, area.Top, area.Width, height));
                    };
                    doc.Print();
                }

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
        => RenderReceipt(e.Graphics, ((Control)sender!).Width, ((Control)sender).Height);

    /// <summary>
    /// Vẽ phiếu đăng tài. Nhãn song ngữ theo BU (ví dụ KSK: Khmer/Việt), ô trống in "Không có",
    /// cột giá trị tự giãn theo nhãn dài nhất để nhãn song ngữ không đè lên giá trị.
    /// </summary>
    private void RenderReceipt(Graphics g, int pageWidth, int pageHeight)
        => RenderReceipt(g, new Rectangle(0, 0, pageWidth, pageHeight));

    /// <summary>
    /// Bố cục hai cột "nhãn : giá trị" tự xuống dòng (dùng chung <see cref="TicketCanvas"/> với phiếu cân):
    /// nhãn song ngữ dài (Khmer/Lào + Việt) xuống dòng trong cột nhãn, giá trị dài xuống dòng trong cột giá trị —
    /// không đè lên cột bên cạnh, không tràn mép phiếu. Tên dài (chủ mía, KH/NCC, kho...) chiếm cả chiều ngang.
    /// </summary>
    private void RenderReceipt(Graphics g, Rectangle bounds)
    {
        using var c = new TicketCanvas(g, bounds);
        using var titleBrush = new SolidBrush(AgrisTheme.BrandGreenDark);
        using var reprintBrush = new SolidBrush(AgrisTheme.Danger);

        // ── Nhà máy + tiêu đề ──
        c.Centered(PrintText.V(_data.Factory), c.Company, 2);
        if (!string.IsNullOrWhiteSpace(_data.FactoryAddress)) c.Centered(_data.FactoryAddress, c.Address, 2);
        c.Y += 6;
        c.Centered(PrintText.L("print.dangtai_title"), c.Title, 2, titleBrush);
        if (_isReprint) c.Centered(PrintText.L("receipt.reprint_label"), c.Subtitle, 2, reprintBrush);
        c.Y += 10;

        var date = new TicketCell(PrintText.L("print.field_date"), _data.DateTime);
        List<TicketRow> rows = _data.IsMaterial
            ?
            [
                new(new(PrintText.L("print.field_shipment"), _data.ShipmentNo, c.Big), date),
                new(new(PrintText.L("print.dangtai_so_dangtai"), _data.TripNo, c.Big),
                    new(PrintText.L("print.field_plate"), _data.Plate, c.Big)),
                new(new(PrintText.L("print.field_customer"), _data.CustomerOrSupplier), Wide: true),
                new(new(PrintText.L("print.field_commodity"), _data.Commodity), Wide: true),
                new(new(PrintText.L("print.field_warehouse"), _data.Warehouse), Wide: true),
                new(new(PrintText.L("print.field_transporter"), _data.Transporter),
                    // ERP không có loại xe / rơ moóc → in lái xe cạnh nhà vận chuyển.
                    new(PrintText.L("field.driver"), _data.DriverName)),
                new(new(PrintText.L("print.field_ghi_chu"), _data.Notes), Wide: true)
            ]
            :
            [
                new(new(PrintText.L("print.dangtai_lenh_don"), _data.ShipmentNo, c.Big), date),
                new(new(PrintText.L("print.dangtai_so_dangtai"), _data.TripNo, c.Big),
                    new(PrintText.L("print.field_so_xe"), _data.Plate, c.Big)),
                new(new(PrintText.L("print.field_chu_mia"), _data.SugarcaneOwner), Wide: true),
                new(new(PrintText.L("print.field_hop_dong"), _data.HopDong),
                    new(PrintText.L("print.field_loai_xe"), _data.VehicleType)),
                new(new(PrintText.L("print.field_cong_thu_hoach"), _data.HarvestPartner ?? _data.Transporter),
                    new(PrintText.L("print.field_cong_boc_xep"), _data.LoadingPartner)),
                new(new(PrintText.L("print.field_giong_mia"), _data.SugarcaneVariety),
                    new(PrintText.L("print.field_loai_mia"), _data.SugarcaneType)),
                new(new(PrintText.L("print.field_hinh_thuc_thu_hoach"), _data.HarvestMethod), Wide: true),
                new(new(PrintText.L("print.field_ghi_chu"), _data.Notes), Wide: true)
            ];
        c.Rows(rows);

        // ── Mã vạch (Code 128) + ngày in ──
        c.Separator(6, 12);
        c.BarcodeAndPrintedAt(_data.ShipmentNo ?? _data.QrToken);
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
    bool IsMaterial = false,
    string? HarvestPartner = null,   // Công thu hoạch
    string? LoadingPartner = null,   // Công bốc xếp
    string? HopDong = null,          // Hợp đồng (đầu tư mía)
    string? Notes = null);
