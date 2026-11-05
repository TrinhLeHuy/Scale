using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.Queries;
using TTC.Weighing.Application.UseCases.Integration;
using TTC.Weighing.Application.UseCases.Trips;
using TTC.Weighing.Application.UseCases.Weighing;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.WinForms.Controls;
using TTC.Weighing.WinForms.Helpers;
using TTC.Weighing.WinForms.Presenters;
using TTC.Weighing.WinForms.Services;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Theme;
using TTC.Weighing.Domain.Common;

namespace TTC.Weighing.WinForms.Forms;

/// <summary>
/// UI-03 — Cân hợp nhất. Bố cục theo quy trình thực tế nhà máy:
/// - Panel trái: thông tin chuyến (read-only sau quét QR)
/// - Panel giữa: số cân realtime + thông tin cân vào/ra + dung sai
/// - Panel phải: camera AI + hình ảnh cân vào/ra
/// - Panel dưới: lưới xe đã cân + tab lọc
/// - Thanh trạng thái: Barcode, Đồng hồ cân, LPR, Camera
///
/// Luồng: Quét QR → hệ thống load thông tin → hiện số cân → Lưu phiếu (F5) → In phiếu (F6)
/// Form không tự tính toán điều kiện chốt cân — server quyết định (R-007).
/// </summary>
public sealed class WeighOperationForm : Form
{
    private static readonly TimeSpan SignalPollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ConditionPollInterval = TimeSpan.FromMilliseconds(3000);

    private readonly WeighOperationPresenter _presenter;
    private readonly ISiteContext _siteContext;
    private readonly ICurrentUser _currentUser;
    private readonly CancellationTokenSource _cts = new();

    // ── Thông tin chuyến (panel trái — read-only) ──
    private readonly TextBox _txtBarcode = new()
    {
        PlaceholderText = Loc.T("weigh.scan_placeholder"),
        Font = new Font(AgrisTheme.FontFamily, 12F, FontStyle.Bold),
        BackColor = Color.FromArgb(255, 255, 220),
        BorderStyle = BorderStyle.FixedSingle,
        AccessibleName = "Nhập số đăng tài hoặc biển số xe"
    };
    /// <summary>Khung viền chung của ô nhập mã + nút ▼ (để popup danh sách xe canh theo).</summary>
    private readonly Panel _barcodeHost = new();
    private readonly Button _btnTripDropdown = new()
    {
        Text = "▼",
        Width = 30,
        FlatStyle = FlatStyle.Flat,
        Cursor = Cursors.Hand,
        TabStop = false
    };
    private readonly Button _btnWeighMode = new()
    {
        Text = "🔵 CÂN VÀO",
        Width = 100,
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(0, 120, 215),
        ForeColor = Color.White,
        Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
        Cursor = Cursors.Hand,
        TabStop = false
    };
    private bool _isWeighOutMode; // false = cân vào, true = cân ra
    /// <summary>Danh sách xe trong nhà máy (nút ▼) — thẻ 2 dòng dùng chung, xem <see cref="TripPickerList"/>.</summary>
    private readonly ListBox _lstTripPopup = new() { Visible = false };
    private TripPickerList? _tripPicker;
    /// <summary>Gõ vào ô nhập → chờ ngắn rồi lọc danh sách xe (không tra DB theo từng phím).</summary>
    private readonly System.Windows.Forms.Timer _suggestTimer = new() { Interval = 250 };
    /// <summary>Tăng mỗi lần tra gợi ý — kết quả về trễ của lần tra cũ bị bỏ.</summary>
    private int _suggestVersion;
    private readonly TextBox _txtTicketNo = ReadOnlyField(Loc.T("field.ticket_no"));
    private readonly TextBox _txtRegNo = ReadOnlyField(Loc.T("field.trip_no"));
    private readonly TextBox _txtPlate = ReadOnlyField(Loc.T("field.plate"));
    private readonly TextBox _txtTrailer = ReadOnlyField(Loc.T("field.trailer"));
    private readonly TextBox _txtCustomer = ReadOnlyField(Loc.T("field.customer"));
    private readonly TextBox _txtCommodity = ReadOnlyField(Loc.T("field.commodity"));
    private readonly TextBox _txtTransporter = ReadOnlyField(Loc.T("field.transporter"));
    private readonly TextBox _txtWarehouse = ReadOnlyField(Loc.T("field.warehouse"));
    private readonly TextBox _txtDriver = ReadOnlyField(Loc.T("field.driver"));
    private readonly TextBox _txtShipment = ReadOnlyField(Loc.T("field.shipment"));
    /// <summary>Mía: hiển thị mã lệnh đốn (= _txtShipment) ở đầu panel. Chỉ để xem, logic vẫn đọc _txtShipment.</summary>
    private readonly TextBox _txtLenhDonCode = ReadOnlyField(Loc.T("field.lenhdon_code"));
    // ── Riêng luồng vật tư (giống màn Đăng tài vật tư) ──
    private readonly TextBox _txtDirection = ReadOnlyField(Loc.T("field.direction"));
    private readonly TextBox _txtErpTripNumber = ReadOnlyField(Loc.T("reg.field_trip_number"));

    /// <summary>Dòng chỉ hiện ở luồng vật tư / luồng mía (ẩn cả nhãn lẫn ô, dòng co về 0).</summary>
    private readonly List<(TableLayoutPanel Table, Control Field)> _materialOnlyRows = [];
    private readonly List<(TableLayoutPanel Table, Control Field)> _sugarcaneOnlyRows = [];
    private readonly Dictionary<Control, float> _flowRowHeights = [];
    private readonly TextBox _txtTotalOrdered = ReadOnlyField(Loc.T("field.total_ordered"));
    private readonly TextBox _txtSoTrip = ReadOnlyField(Loc.T("field.trip_count"));
    private readonly Label _lblBarcodeStatus = new()
    {
        Text = Loc.T("field.barcode_status"),
        Font = AgrisTheme.Body(9F, FontStyle.Bold),
        ForeColor = AgrisTheme.Success,
        AutoSize = true,
        AccessibleName = Loc.T("field.barcode_status")
    };

    // ── Số cân + thông tin cân (panel giữa) ──
    private readonly WeighDisplayControl _display = new() { Dock = DockStyle.Top, Height = 120 };
    private readonly WeighConditionChecklist _checklist = new() { Dock = DockStyle.Top, Height = 120 };
    private readonly TextBox _txtWeightIn = ReadOnlyField(Loc.T("field.weight_in"));
    private readonly TextBox _txtTimeIn = ReadOnlyField(Loc.T("field.time_in"));
    private readonly TextBox _txtWeightOut = ReadOnlyField(Loc.T("field.weight_out"));
    private readonly TextBox _txtTimeOut = ReadOnlyField(Loc.T("field.time_out"));
    private readonly TextBox _txtNetWeight = ReadOnlyField(Loc.T("field.net_weight"));
    private readonly TextBox _txtTolerance = ReadOnlyField(Loc.T("field.tolerance"));
    // ── Số thanh toán theo chính sách FRM (lấy khi bấm Xác nhận cân lần 2, hiện TRƯỚC khi lưu) ──
    private readonly TextBox _txtPolicyName = ReadOnlyField(Loc.T("field.policy_name"));
    private readonly TextBox _txtTcThanhToan = ReadOnlyField(Loc.T("field.tc_thanh_toan"));
    private readonly TextBox _txtCcsThanhToan = ReadOnlyField(Loc.T("field.ccs_thanh_toan"));
    private readonly TextBox _txtTlThanhToan = ReadOnlyField(Loc.T("field.tl_thanh_toan"));
    private readonly Button _btnTripMore = GridBuilder.SecondaryButton("📋  " + Loc.T("weigh.btn_trip_more"), new Size(220, 36));
    private readonly Button _btnPolicyPreview = GridBuilder.SecondaryButton("↻  " + Loc.T("weigh.btn_policy_preview"), new Size(220, 36));
    private readonly IApiCallLogQueries _apiLogs;
    private Font? _paymentRegularFont;
    private Font? _paymentBoldFont;
    /// <summary>Kết quả quét chuyến gần nhất — dữ liệu đăng tài cho popup Xem thêm.</summary>
    private QrScanResult? _lastScan;
    private readonly Label _lblToleranceRange = new()
    {
        Font = AgrisTheme.Body(9F),
        ForeColor = AgrisTheme.TextMuted,
        AutoSize = true,
        AccessibleName = Loc.T("field.tolerance_range")
    };
    private readonly Label _lblQrMessage = new()
    {
        Text = Loc.T("weigh.qr_start"),
        Font = AgrisTheme.Body(11F, FontStyle.Italic),
        ForeColor = AgrisTheme.BrandGreen,
        TextAlign = ContentAlignment.MiddleCenter,
        Dock = DockStyle.Bottom,
        Height = 42,
        AccessibleName = Loc.T("weigh.qr_start")
    };

    // ── Nút hành động ──
    private readonly Button _btnSave = GridBuilder.PrimaryButton("Xác nhận (F5)", new Size(130, 36));
    private readonly Button _btnPrint = GridBuilder.SecondaryButton(Loc.T("weigh.btn_print"), new Size(120, 36));
    private readonly Button _btnSync = GridBuilder.SecondaryButton("🔄 " + Loc.T("weigh.btn_retry_frm"), new Size(140, 36));
    private readonly Button _btnReset = GridBuilder.SecondaryButton(Loc.T("weigh.btn_reset"), new Size(120, 36));


    // ── Nút nhập tay số cân (chỉ hiện khi có quyền weighing.weight.manual — Req 11.1) ──
    private readonly Button _btnManualWeight = GridBuilder.SecondaryButton(Loc.T("weigh.btn_manual"), new Size(110, 36));

    // ── Tiếp tục (F7) — chuyển sang bước tiếp theo ──
    private readonly Button _btnContinue = GridBuilder.PrimaryButton(Loc.T("common.btn_continue"), new Size(130, 36));

    // ── Camera + hình ảnh (panel phải) ──
    private readonly CameraViewControl _cameraIn = new() { CameraPosition = "In" };
    private readonly CameraViewControl _cameraOut = new() { CameraPosition = "Out" };

    // ── Loại mía (hiện khi phiếu mía — bắt buộc chọn 1) ──
    private readonly RadioButton _rbMiaCay = new() { Text = "Mía cây", AutoSize = true };
    private readonly RadioButton _rbMiaKhuc = new() { Text = "Mía khúc", AutoSize = true };
    private readonly RadioButton _rbMiaChay = new() { Text = "Mía cháy", AutoSize = true };
    private readonly RadioButton _rbMiaOpt4 = new() { Text = "Tùy chọn 4", AutoSize = true };
    private readonly RadioButton _rbMiaOpt5 = new() { Text = "Tùy chọn 5", AutoSize = true };
    private readonly TextBox _txtWeighNotes = new() { AccessibleName = "Ghi chú cân", PlaceholderText = "Ghi chú..." };

    /// <summary>Ghi chú người cân gõ — gửi DigiScale (phiếu cân mía / oracle-tickets vật tư) và in trên phiếu cân.</summary>
    private string? WeighNote => _txtWeighNotes.Text.NullIfEmpty();
    private Panel _pnlSugarcaneType = null!;

    // ── Lưới xe đã cân (panel dưới) ──
    private readonly DataGridView _dgvWeighed = GridBuilder.ReadOnlyGrid(Loc.T("weigh.section_grid"));

    // ── Phân trang + thanh cuộn lớn + phóng to lưới (giống lưới chuyến bên Đăng tài) ──
    private readonly GridPager _pager = new("weigh.trips", defaultPageSize: 20);
    private IReadOnlyList<WeighedTripRow> _allWeighedRows = [];
    /// <summary>Tìm kiếm + lọc cột của lưới xe đã cân (như Dữ liệu FRM / ERP).</summary>
    private GridListFilter<WeighedTripRow>? _weighFilter;
    private GridScrollBar? _gridScroll;
    private readonly Button _btnExpandGrid = GridBuilder.SecondaryButton(Loc.T("grid.expand"), new Size(150, 40));

    // ── Tách lưới Mía / Vật tư + xuất Excel ──
    private readonly TripCategorySwitch _gridCategory = new("weigh.trips");
    private readonly Button _btnExportExcel = GridBuilder.ExportExcelButton();
    private bool _gridExpanded;
    private const float TopAreaPercent = 58F; // lưới đủ chỗ cho thanh lọc + vài dòng + thanh phân trang
    private TableLayoutPanel _root = null!;
    private Control _topArea = null!;
    private Control _actionBarCard = null!;
    private readonly Button _btnTabPending = GridBuilder.SecondaryButton(Loc.T("weigh.tab_pending"), new Size(100, 28));
    private readonly Button _btnTabWeighIn = GridBuilder.PrimaryButton(Loc.T("weigh.tab_weigh_in"), new Size(100, 28));
    /// <summary>Tab "Đo tạp chất" (mía): đã cân vào, CHƯA có kết quả tạp chất. Có kết quả → sang "Chờ cân ra".</summary>
    private readonly Button _btnTabImpurity = GridBuilder.SecondaryButton(Loc.T("weigh.tab_impurity"), new Size(120, 28));
    private const int GRID_MODE_IMPURITY = 3;
    /// <summary>Làm mới lưới nhanh khi đang xem tab Đo tạp chất / Chờ cân ra — nhận kết quả tạp chất từ trạm khác.</summary>
    private readonly System.Windows.Forms.Timer _gridLiveTimer = new() { Interval = 1500 };
    private bool _gridLiveBusy;
    private string? _gridFingerprint;
    private readonly Button _btnTabBoth = GridBuilder.SecondaryButton(Loc.T("weigh.tab_both"), new Size(140, 28));
    private readonly DateTimePicker _dtpFrom = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 150, AccessibleName = Loc.T("reg.filter_from") };
    private readonly DateTimePicker _dtpTo = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 150, AccessibleName = Loc.T("reg.filter_to") };

    // ── Cảnh báo CCS thiếu chữ đường (Req 4.3) ──
    private readonly Label _lblCcsWarning = new()
    {
        Text = string.Empty,
        Font = AgrisTheme.Body(9F, FontStyle.Bold),
        ForeColor = AgrisTheme.Warning,
        AutoSize = true,
        Visible = false,
        Dock = DockStyle.Bottom,
        Padding = new Padding(4, 2, 4, 2),
        AccessibleName = Loc.T("weigh.ccs_warning")
    };

    // ── Thanh trạng thái thiết bị (Req 13.1) ──
    private readonly DeviceStatusBar _deviceStatusBar = new();

    // ── Timers & state ──
    private readonly System.Windows.Forms.Timer _signalTimer = new();
    private readonly System.Windows.Forms.Timer _conditionTimer = new();
    private readonly System.Windows.Forms.Timer _deviceTimer = new();

    // ── Guard chống chồng chéo khi tick handler async chưa xong ──
    private bool _signalPollInFlight;
    private bool _devicePollInFlight;

    // ── Giả lập (null khi chạy với thiết bị thật) ──
    private readonly Development.SimulatedScaleSignalGateway? _simulatedScale;
    private readonly Services.ScaleSignalRouter? _scaleRouter;

    // ── Camera AI ──
    private readonly ICameraAiService _cameraAiService;
    private readonly Development.SimulatedCameraAiService? _simulatedCameraAi;
    private readonly CameraAiStatusControl _cameraAiStatus = new();

    // ── Event bus ──
    private readonly TripEventBus _eventBus;
    private Guid? _currentTripId;
    private int _currentTripVersion;
    private string? _currentSourceSystem;
    private string? _currentFrmLenhDonId;
    private string? _currentFrmMaLenhDon;
    private string? _currentFrmLenhDonBu;
    private Guid _currentWeighbridgeId;
    private Guid _currentLaneId;

    /// <summary>True nếu đang cân mía (FRM), false nếu vật tư (ERP).</summary>
    private bool IsUnitTon => _currentSourceSystem != "ERP";

    /// <summary>Chuyển kg sang đơn vị hiển thị (tấn cho mía, kg cho vật tư).</summary>
    private decimal ToDisplayWeight(decimal kg) => IsUnitTon ? kg / 1000m : kg;

    /// <summary>Đơn vị hiển thị.</summary>
    private string WeightUnitLabel => IsUnitTon ? "tấn" : "kg";

    /// <summary>
    /// Format trọng lượng hiển thị. Mía (tấn) giữ 3 số lẻ = chính xác tới kg (30.050 kg → 30,050 tấn);
    /// vật tư (kg) số nguyên. Không làm tròn mất phần lẻ.
    /// </summary>
    private string FormatWeight(decimal kg) => IsUnitTon ? $"{kg / 1000m:N3}" : $"{kg:N0}";

    /// <summary>Số kg lượt cân 1 của chuyến đang mở — dùng tính TL hàng, không đọc lại từ ô hiển thị.</summary>
    private decimal? _firstWeighKg;

    /// <summary>Cập nhật label đơn vị (tấn/kg) trên các trường cân.</summary>
    private void UpdateWeightUnitLabels()
    {
        var unit = WeightUnitLabel;
        _txtWeightIn.AccessibleName = $"TL cân vào ({unit})";
        _txtWeightOut.AccessibleName = $"TL cân ra ({unit})";
        _txtNetWeight.AccessibleName = $"TL hàng ({unit})";

        // Cập nhật label trong FormLayout table — tìm label theo text pattern "(tấn)" hoặc "(kg)"
        static void UpdateLabel(Control parent, string fieldName, string newUnit)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is Label lbl && lbl.Text.Contains("TL cân vào"))
                    lbl.Text = $"TL cân vào ({newUnit})";
                else if (c is Label lbl2 && lbl2.Text.Contains("TL cân ra"))
                    lbl2.Text = $"TL cân ra ({newUnit})";
                else if (c is Label lbl3 && lbl3.Text.Contains("TL hàng"))
                    lbl3.Text = $"TL hàng ({newUnit})";
                if (c.HasChildren) UpdateLabel(c, fieldName, newUnit);
            }
        }
        UpdateLabel(this, "", unit);
    }
    private CheckpointType _currentCheckpoint;
    private int _currentSegmentIndex;
    private Guid? _sessionId;

    // ── Chính sách từ DigiScale (lấy khi cân lần 2) ──
    private decimal? _policyCcsChinhSach;
    private decimal? _policyTapChatCS;
    private string? _policyChinhSachThuMia;

    // ── Cân lần 2 mía: phải đẩy DigiScale + có chính sách TRƯỚC khi lưu cân ──
    private bool _currentQrIsTemp;
    /// <summary>Phiên cân đã được DigiScale nhận (có chính sách) — tránh đẩy trùng khi bấm F5 lại.</summary>
    private Guid? _prePushedSessionId;
    private TTC.Weighing.Infrastructure.Adapters.TicketResult? _prePushedResult;
    private decimal? _prePushedKg;
    private TimeSpan? _requiredStabilityWindow;
    private bool _isBusy;
    private bool _conditionCheckInFlight;
    private bool _ticketPrinted; // true khi phiếu đã in lần đầu — in tiếp sẽ là isReprint
    private bool _weighInOnly = true; // Tab lọc: true = "Cân vào", false = "Cân cả vào và ra"
    /// <summary>0 = Chờ cân, 1 = Cân vào, 2 = Cân cả vào và ra</summary>
    private int _gridMode; // 0 = pending (default khi mở form)
    private string? _lastQrContent; // Lưu QR content từ lần scan cuối để truyền qua event bus

    /// <summary>Serialize DB operations trong cùng form — tránh EF Core "second operation" error.</summary>
    private readonly SemaphoreSlim _dbLock = new(1, 1);

    public WeighOperationForm(
        WeighOperationPresenter presenter,
        ISiteContext siteContext,
        ICurrentUser currentUser,
        IScaleSignalGateway scaleSignalGateway,
        ICameraAiService cameraAiService,
        TripEventBus eventBus,
        IApiCallLogQueries apiLogs,
        ICameraConnectionService cameras)
    {
        _apiLogs = apiLogs;
        // Camera Vào / Ra của màn Cân — theo tab "Bàn cân" ở màn Kết nối camera.
        _cameraIn.StartLive(cameras, CameraConnectionSettings.StationWeigh);
        _cameraOut.StartLive(cameras, CameraConnectionSettings.StationWeigh);
        _presenter = presenter;
        _siteContext = siteContext;
        _currentUser = currentUser;
        // Nguồn số nhập tay (đã duyệt): gateway có thể là router COM bọc ngoài.
        _scaleRouter = scaleSignalGateway as Services.ScaleSignalRouter;
        _simulatedScale = scaleSignalGateway as Development.SimulatedScaleSignalGateway ?? _scaleRouter?.Manual;
        _cameraAiService = cameraAiService;
        _simulatedCameraAi = cameraAiService as Development.SimulatedCameraAiService;
        _eventBus = eventBus;

        Text = Loc.T("weigh.title");
        // Chiều cao tối thiểu phải nhỏ hơn vùng tab thực tế (cửa sổ − ~160px dải thương hiệu/menu/tab/status).
        // 860 lớn hơn vùng tab → form tràn xuống dưới, thanh phân trang ở chân lưới bị khuất (chỉ thấy khi phóng to lưới).
        // Phần form nhập / số cân tự cuộn khi thiếu chỗ.
        MinimumSize = new Size(1280, 640);
        BackColor = AgrisTheme.Canvas;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        BuildLayout();

        _signalTimer.Interval = (int)SignalPollInterval.TotalMilliseconds;
        _signalTimer.Tick += OnSignalTick;
        _conditionTimer.Interval = (int)ConditionPollInterval.TotalMilliseconds;
        _conditionTimer.Tick += OnConditionTick;
        _deviceTimer.Interval = 2000; // Poll thiết bị mỗi 2 giây (Req 13.1)
        _deviceTimer.Tick += OnDeviceStatusTick;

        // Subscribe event bus — refresh lưới khi tab khác thay đổi chuyến
        _eventBus.TripChanged += OnTripChangedFromOtherForm;

        _btnSave.Click += OnSaveClicked;
        _btnPrint.Click += OnPrintClicked;
        _btnSync.Click += OnSyncClicked;
        // Máy quét QR (số đăng tài) — quét ở đâu trên màn cũng được, không cần bấm vào ô nhập trước.
        QrScanner.Register(this, () => _txtBarcode, OnQrScanned);
        _btnReset.Click += OnResetClicked;
        _btnManualWeight.Click += OnManualWeightClicked;
        _txtBarcode.KeyDown += OnBarcodeKeyDown;

        // Nút In và Sync chỉ bật sau cân lần 2 thành công (Req 4.7)
        _btnPrint.Enabled = false;
        _btnSync.Enabled = false;


        // Nút Nhập tay chỉ hiện khi có quyền weighing.weight.manual (Req 11.1)
        _btnManualWeight.Visible = _currentUser.HasPermission(
            ApproveManualWeightHandler.RequiredPermission, _siteContext.CurrentSiteId);
        _btnManualWeight.Enabled = false;

        _cameraIn.ApplyPositionLabel();
        _cameraOut.ApplyPositionLabel();

        // Tab filter và date picker → reload lưới xe đã cân (Req 1.2, 8.6)
        _btnTabPending.Click += OnTabPendingClicked;
        _btnTabWeighIn.Click += OnTabWeighInClicked;
        _btnTabImpurity.Click += async (_, _) =>
        {
            _gridMode = GRID_MODE_IMPURITY;
            _pager.Reset();
            UpdateGridTabStyles();
            await LoadWeighedTripsAsync();
        };
        _gridLiveTimer.Tick += async (_, _) => await GridLiveTickAsync();
        _btnTabBoth.Click += OnTabBothClicked;
        _dtpFrom.ValueChanged += OnDateFilterChanged;
        _dtpTo.ValueChanged += OnDateFilterChanged;

        // Không tự động mở phiên khi Load — chờ nhân viên quét QR.
        // Bắt đầu poll trạng thái thiết bị ngay khi mở form (Req 13.1).
        // Tải lưới xe đã cân hôm nay (Req 1.2).
        Load += async (_, _) =>
        {
            _deviceTimer.Start();
            _gridLiveTimer.Start();
            _txtBarcode.Focus();
            await LoadWeighedTripsAsync();
        };

        // Tạm dừng timer khi tab không hiển thị để giảm tải CPU/IO.
        // Ẩn device status bar khi tab không active — tránh chiếm không gian tab khác.
        VisibleChanged += (_, _) =>
        {
            if (Visible)
            {
                _deviceTimer.Start();
                if (_sessionId is not null) { _signalTimer.Start(); _conditionTimer.Start(); }
            }
            else
            {
                _signalTimer.Stop();
                _conditionTimer.Stop();
                _deviceTimer.Stop();
            }
            _deviceStatusBar.Visible = Visible;
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  Keyboard shortcuts
    // ═══════════════════════════════════════════════════════════

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.F5: _btnSave.PerformClick(); return true;
            case Keys.F6: _btnPrint.PerformClick(); return true;
            case Keys.Escape: _btnReset.PerformClick(); return true;
            default: return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Layout
    // ═══════════════════════════════════════════════════════════

    private void BuildLayout()
    {
        // Thanh giả lập cân xe đã bỏ khỏi màn hình vận hành. Gateway giả lập (nếu có) vẫn
        // nhận số từ hộp thoại "Nhập tay" — quy trình nhập tay có lý do + phê duyệt, không phải nút bấm tự do.
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8, 6, 8, 0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, TopAreaPercent));   // Top: trip info + weight + camera
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, FormLayout.ActionRowHeight));   // Action bar
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100 - TopAreaPercent));   // Grid

        _root = root;
        _topArea = BuildTopArea();
        _actionBarCard = BuildActionBar();

        root.Controls.Add(_topArea, 0, 0);
        root.Controls.Add(_actionBarCard, 0, 1);
        root.Controls.Add(BuildGridArea(), 0, 2);

        Controls.Add(root);
        ApplyFlowLayout(); // bố cục ban đầu theo tab Mía/Vật tư đã nhớ
    }

    /// <summary>
    /// Phóng to lưới: ẩn thông tin chuyến + số cân + camera + thanh nút để lưới chiếm toàn màn hình.
    /// Bấm lại để thu về. Phím tắt F5/F7 vẫn hoạt động vì chỉ ẩn giao diện, không đổi trạng thái phiên cân.
    /// </summary>
    private void ToggleGridExpanded()
    {
        _gridExpanded = !_gridExpanded;

        _root.SuspendLayout();
        _root.RowStyles[0] = _gridExpanded ? new RowStyle(SizeType.Absolute, 0) : new RowStyle(SizeType.Percent, TopAreaPercent);
        _root.RowStyles[1] = _gridExpanded ? new RowStyle(SizeType.Absolute, 0) : new RowStyle(SizeType.Absolute, FormLayout.ActionRowHeight);
        _root.RowStyles[2] = _gridExpanded ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Percent, 100 - TopAreaPercent);
        _topArea.Visible = !_gridExpanded;
        _actionBarCard.Visible = !_gridExpanded;
        _root.ResumeLayout(true);

        _btnExpandGrid.Text = _gridExpanded ? Loc.T("grid.collapse") : Loc.T("grid.expand");
        _btnExpandGrid.AccessibleName = _btnExpandGrid.Text;

        // Phân trang chỉ khi phóng to lưới; chế độ thường hiện mọi dòng và cuộn dọc.
        _pager.PagingEnabled = _gridExpanded;
        BindCurrentPage();
        _gridScroll?.Sync();

        // Thu về thì đưa con trỏ lại ô quét QR để người vận hành quét tiếp ngay.
        if (!_gridExpanded && _txtBarcode.Enabled)
            _txtBarcode.Focus();
    }

    /// <summary>
    /// Bind trang hiện tại. Giữ vị trí cuộn khi lưới nạp lại theo timer / sau chốt cân.
    /// </summary>
    private void BindCurrentPage()
    {
        var firstRow = _dgvWeighed.FirstDisplayedScrollingRowIndex;

        _dgvWeighed.SuspendLayout();
        try
        {
            _dgvWeighed.DataSource = null;
            // Chế độ thường: toàn bộ danh sách, cuộn dọc. Phóng to lưới: phân trang.
            _dgvWeighed.DataSource = _pager.Page(_weighFilter?.Apply(_allWeighedRows) ?? _allWeighedRows);
        }
        finally
        {
            _dgvWeighed.ResumeLayout();
        }

        if (firstRow > 0 && firstRow < _dgvWeighed.Rows.Count)
        {
            try { _dgvWeighed.FirstDisplayedScrollingRowIndex = firstRow; }
            catch (InvalidOperationException) { /* lưới chưa layout xong */ }
        }

        _gridScroll?.Sync();
    }

    private TableLayoutPanel BuildTopArea()
    {
        // Bố cục: [Thông tin chuyến (hẹp)] | [Cam Vào | Cam Ra]  (trên)
        //                                   [Trọng lượng       ]  (dưới)
        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));  // Trip info
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));  // Camera Vào (+ nửa trái Trọng lượng)
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));  // Camera Ra (+ nửa phải Trọng lượng)
        top.RowStyles.Add(new RowStyle(SizeType.Percent, 100));       // Camera — phần còn lại
        // Trọng lượng: chỉ tiêu đề + số cân (WeighDisplayControl cao tối thiểu 120). TL / TG xem ở "Xem thêm thông tin cân".
        // Giữ thấp để camera Vào / Ra to hơn: 44 tiêu đề + ~130 số cân (số 40pt + đơn vị cùng dòng).
        top.RowStyles.Add(new RowStyle(SizeType.Absolute, 176));

        var tripInfo = BuildTripInfoPanel();
        top.Controls.Add(tripInfo, 0, 0);
        top.SetRowSpan(tripInfo, 2);

        // Camera Vào / Ra: không thêm tiêu đề khu vực — CameraViewControl đã có dòng tên camera + trạng thái.
        // FillFrame: ảnh phủ kín khung (cắt mép thừa), không để dải đen hai bên.
        var camInCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(1) };
        _cameraIn.Dock = DockStyle.Fill;
        _cameraIn.FillFrame = true;
        camInCard.Controls.Add(_cameraIn);
        top.Controls.Add(camInCard, 1, 0);

        var camOutCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(1) };
        _cameraOut.Dock = DockStyle.Fill;
        _cameraOut.FillFrame = true;
        camOutCard.Controls.Add(_cameraOut);
        top.Controls.Add(camOutCard, 2, 0);

        // Trọng lượng — dưới camera, rộng bằng cả hai camera
        var weight = BuildWeightPanel();
        top.Controls.Add(weight, 1, 1);
        top.SetColumnSpan(weight, 2);

        return top;
    }

    /// <summary>Panel trái: thông tin chuyến — tự động điền khi quét QR.</summary>
    private Card BuildTripInfoPanel()
    {
        var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(1), AutoScroll = true };
        var header = GridBuilder.SectionHeader(Loc.T("weigh.section_trip"));
        var table = FormLayout.FieldTable();

        FormLayout.AddFullWidth(table, BuildBarcodeWithDropdown());
        // Mía: Mã lệnh đốn đứng đầu (ô hiển thị theo _txtShipment). Vật tư: Shipment Number giữ chỗ cũ bên dưới.
        _txtShipment.TextChanged += (_, _) => _txtLenhDonCode.Text = _txtShipment.Text;
        AddFlowField(table, Loc.T("field.lenhdon_code"), _txtLenhDonCode, _sugarcaneOnlyRows);
        // Số đăng tài KHÔNG hiện riêng — đã nhập / quét ở ô trên cùng. Ô vẫn giữ (ngoài layout) để mang giá trị khi lưu / in.
        FormLayout.AddField(table, Loc.T("field.plate"), _txtPlate);

        // Vật tư: hiển thị theo màn Đăng tài vật tư (Hướng, Số TRIP, Shipment, KH/NCC, Hàng hóa, Kho, NVC, Lái xe).
        AddFlowField(table, Loc.T("field.direction"), _txtDirection, _materialOnlyRows);
        AddFlowField(table, Loc.T("reg.field_trip_number"), _txtErpTripNumber, _materialOnlyRows);
        // Khách hàng/NCC: chỉ hiện ở mía; vật tư xem ở "Xem thêm thông tin chuyến" (ô vẫn mang giá trị khi lưu / in).
        AddFlowField(table, Loc.T("field.customer"), _txtCustomer, _sugarcaneOnlyRows);
        FormLayout.AddField(table, Loc.T("field.commodity"), _txtCommodity);
        // Kho / Nhà vận chuyển / Lái xe (vật tư) KHÔNG hiện trên khung — đã có ở "Xem thêm thông tin chuyến".
        // Shipment Number cũng không hiện (xem ở "Xem thêm"). Các ô vẫn giữ (ngoài layout) để mang giá trị
        // khi lưu / đẩy ERP / in phiếu; _txtShipment còn cấp Mã lệnh đốn cho mía.

        // Xem thêm: toàn bộ dữ liệu chuyến (đăng tài, lệnh đốn, tạp chất, CCS, chính sách) —
        // đặt trên thanh tiêu đề như màn Đăng tài (luôn nhìn thấy, không bị cuộn mất).
        _btnTripMore.Enabled = false;
        _btnTripMore.Click += async (_, _) => await ShowTripMoreAsync();
        _btnTripMore.Dock = DockStyle.Fill;
        _btnTripMore.Margin = Padding.Empty;
        // Cùng kiểu nút "Xem thêm thông tin chuyến" ở màn Đăng tài: viền + chữ xanh thương hiệu, chữ đậm.
        _btnTripMore.Text = Loc.T("weigh.btn_trip_more");
        _btnTripMore.FlatStyle = FlatStyle.Flat;
        _btnTripMore.FlatAppearance.BorderColor = AgrisTheme.BrandGreen;
        _btnTripMore.FlatAppearance.BorderSize = 1;
        _btnTripMore.ForeColor = AgrisTheme.BrandGreen;
        _btnTripMore.BackColor = AgrisTheme.Surface;
        _btnTripMore.Font = AgrisTheme.Body(9F, FontStyle.Bold);
        _btnTripMore.TextAlign = ContentAlignment.MiddleCenter;
        _btnTripMore.Cursor = Cursors.Hand;
        var headerAction = new Panel
        {
            Dock = DockStyle.Right,
            Width = 290,
            Padding = new Padding(0, 4, 8, 4),
            BackColor = Color.Transparent
        };
        headerAction.Controls.Add(_btnTripMore);
        header.Controls.Add(headerAction);

        // Loại mía (chỉ hiện khi phiếu mía — ẩn mặc định, hiện sau khi scan QR)
        _pnlSugarcaneType = new Panel
        {
            Dock = DockStyle.Fill,
            Visible = false
        };
        var lblLoaiMia = new Label
        {
            Text = "Loại mía (*)",
            Font = AgrisTheme.Body(9F),
            ForeColor = AgrisTheme.TextMuted,
            AutoSize = true,
            Margin = new Padding(0, 6, 8, 0)
        };
        var rbFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = false,
            Height = 30
        };
        rbFlow.Controls.Add(lblLoaiMia);
        foreach (var rb in new[] { _rbMiaCay, _rbMiaKhuc, _rbMiaChay, _rbMiaOpt4, _rbMiaOpt5 })
        {
            rb.Margin = new Padding(0, 4, 14, 4);
            rb.Font = AgrisTheme.Body(9.5F);
            rbFlow.Controls.Add(rb);
        }
        _pnlSugarcaneType.Controls.Add(rbFlow);
        // Loại mía đã lưu từ FRM (LoaiMiaName) khi đăng tài — không cần chọn thủ công tại trạm cân.
        // Giữ field declarations để tránh lỗi tham chiếu, chỉ không thêm vào layout.
        // FormLayout.AddFullWidth(table, _pnlSugarcaneType, 38);

        // Ghi chú cân
        FormLayout.AddField(table, Loc.T("field.notes"), _txtWeighNotes);

        // Bỏ dòng "BARCODE ●" (trạng thái đầu đọc đã có ở thanh trạng thái thiết bị) → gọn khung, không phải cuộn.

        // Dòng thông báo cân (Xem chuyến..., Đã lưu cân..., cảnh báo, lỗi quét) ở cuối khung Thông tin chuyến.
        // Câu hướng dẫn mặc định ("Nhập số đăng tài...") không hiện; có thông báo thật mới hiện.
        _lblQrMessage.AutoEllipsis = true;
        void SyncQrMessageVisible()
            => _lblQrMessage.Visible = !string.IsNullOrWhiteSpace(_lblQrMessage.Text)
                                       && _lblQrMessage.Text != Loc.T("weigh.qr_start");
        _lblQrMessage.TextChanged += (_, _) => SyncQrMessageVisible();
        SyncQrMessageVisible();

        card.Controls.Add(table);
        card.Controls.Add(_lblQrMessage);
        card.Controls.Add(header);
        return card;
    }

    /// <summary>Tạo panel TextBox + nút ▼ dropdown chọn xe trong nhà máy.</summary>
    private Panel BuildBarcodeWithDropdown()
    {
        // Ba phần cùng chiều cao, cùng mép trên/dưới: [CÂN VÀO/RA] [ô nhập ........ | ▼].
        // TextBox một dòng không giãn cao theo Dock, nên đặt nó không viền trong một khung nền vàng
        // và căn giữa theo chiều dọc; khung ngoài 1px làm viền chung cho cả ô nhập lẫn nút ▼.
        var panel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };

        _btnWeighMode.Dock = DockStyle.Left;
        _btnWeighMode.Width = 120;
        _btnWeighMode.FlatAppearance.BorderSize = 0;
        _btnWeighMode.TextAlign = ContentAlignment.MiddleCenter;
        _btnWeighMode.Click += (_, _) => ToggleWeighMode();

        _barcodeHost.Dock = DockStyle.Fill;
        _barcodeHost.Padding = new Padding(1);
        _barcodeHost.BackColor = AgrisTheme.TextMuted; // màu viền

        var inputArea = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = _txtBarcode.BackColor,
            Padding = new Padding(8, 0, 4, 0),
            Cursor = Cursors.IBeam
        };
        _txtBarcode.BorderStyle = BorderStyle.None;
        _txtBarcode.Tag = ThemeApplier.KeepBorderTag;
        _txtBarcode.Dock = DockStyle.Top;
        inputArea.Controls.Add(_txtBarcode);
        // Căn lại khi khung đổi cỡ hoặc khi theme đổi font làm ô nhập đổi chiều cao.
        void CenterInput() =>
            inputArea.Padding = new Padding(8, Math.Max(0, (inputArea.Height - _txtBarcode.Height) / 2), 4, 0);
        inputArea.Resize += (_, _) => CenterInput();
        _txtBarcode.SizeChanged += (_, _) => CenterInput();
        inputArea.Click += (_, _) => _txtBarcode.Focus();

        var separator = new Panel { Dock = DockStyle.Right, Width = 1, BackColor = AgrisTheme.TextMuted };

        _btnTripDropdown.Dock = DockStyle.Right;
        _btnTripDropdown.Width = 44;
        _btnTripDropdown.FlatAppearance.BorderSize = 0;
        _btnTripDropdown.BackColor = AgrisTheme.Surface;
        _btnTripDropdown.Margin = Padding.Empty;

        _barcodeHost.Controls.Add(inputArea);
        _barcodeHost.Controls.Add(separator);
        _barcodeHost.Controls.Add(_btnTripDropdown);

        // Khe 6px giữa nút chế độ và ô nhập.
        var gap = new Panel { Dock = DockStyle.Left, Width = 6, BackColor = Color.Transparent };

        panel.Controls.Add(_barcodeHost);
        panel.Controls.Add(gap);
        panel.Controls.Add(_btnWeighMode);

        _btnTripDropdown.Click += async (_, _) =>
        {
            if (_lstTripPopup.Visible) { _lstTripPopup.Visible = false; _txtBarcode.Focus(); return; }
            await ShowTripDropdownAsync();
        };
        _lstTripPopup.Click += (_, _) => SelectTripFromPopup();
        // Vạch màu theo chế độ: xanh = cân vào, cam = cân ra (cùng màu nút chế độ).
        _tripPicker = new TripPickerList(_lstTripPopup)
        {
            Accent = () => _isWeighOutMode ? Color.FromArgb(232, 120, 0) : Color.FromArgb(0, 120, 215)
        };
        _lstTripPopup.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.Handled = true; SelectTripFromPopup(); }
            else if (e.KeyCode == Keys.Escape) { _lstTripPopup.Visible = false; _txtBarcode.Focus(); }
        };
        _txtBarcode.GotFocus += (_, _) => _lstTripPopup.Visible = false;

        // Gõ số đăng tài / biển số → hiện ngay danh sách xe khớp (con trỏ vẫn ở ô nhập).
        // Tắt bộ gõ tiếng Việt: bộ gõ giữ chữ tới khi bấm Space → gợi ý không hiện ngay khi gõ.
        FieldTile.DisableIme(_txtBarcode);
        _txtBarcode.TextChanged += (_, _) =>
        {
            _suggestTimer.Stop();
            if (!_txtBarcode.Focused) return; // gán chữ bằng code (quét, chọn, reset) → không gợi ý
            if (QrScanner.ScanInProgress) return; // máy quét đang gõ → quét xong tự tra, không gợi ý
            if (string.IsNullOrWhiteSpace(_txtBarcode.Text)) { _suggestVersion++; _lstTripPopup.Visible = false; return; }
            _suggestTimer.Start();
        };
        _suggestTimer.Tick += async (_, _) =>
        {
            _suggestTimer.Stop();
            if (!_txtBarcode.Focused || !_txtBarcode.Enabled || _isBusy || QrScanner.ScanInProgress) return;
            await ShowTripDropdownAsync(_txtBarcode.Text.Trim(), focus: false);
        };
        _txtBarcode.KeyDown += (_, e) =>
        {
            if (!_lstTripPopup.Visible) return;
            if (e.KeyCode == Keys.Down && _lstTripPopup.Items.Count > 0)
            {
                e.Handled = true; e.SuppressKeyPress = true;
                _lstTripPopup.Focus();
                _lstTripPopup.SelectedIndex = 0;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true; e.SuppressKeyPress = true;
                HideTripSuggestions();
            }
        };
        // Rời ô nhập sang chỗ khác (không phải danh sách) → đóng gợi ý.
        _txtBarcode.Leave += (_, _) =>
        {
            if (!IsHandleCreated || IsDisposed) return;
            BeginInvoke(() => { if (!_lstTripPopup.Focused) HideTripSuggestions(); });
        };
        Disposed += (_, _) => _suggestTimer.Dispose();

        return panel;
    }

    private void HideTripSuggestions()
    {
        _suggestTimer.Stop();
        _suggestVersion++;
        _lstTripPopup.Visible = false;
    }

    private void ToggleWeighMode()
    {
        _isWeighOutMode = !_isWeighOutMode;
        UpdateWeighModeButton();
    }

    private void UpdateWeighModeButton()
    {
        UpdateWeightStageRows();
        if (_isWeighOutMode)
        {
            _btnWeighMode.Text = "🟠 CÂN RA";
            _btnWeighMode.BackColor = Color.FromArgb(232, 120, 0);
        }
        else
        {
            _btnWeighMode.Text = "🔵 CÂN VÀO";
            _btnWeighMode.BackColor = Color.FromArgb(0, 120, 215);
        }
    }

    /// <param name="query">Chuỗi đang gõ để lọc (null = nút ▼, hiện tất cả).</param>
    /// <param name="focus">false = gợi ý khi gõ, con trỏ ở lại ô nhập.</param>
    private async Task ShowTripDropdownAsync(string? query = null, bool focus = true)
    {
        var version = ++_suggestVersion;
        try
        {
            var filter = new TripRegistrationFilter { Tab = TripGridTab.InFactory };
            var allTrips = await _presenter.ListTripsAsync(_siteContext.CurrentSiteId, filter, _cts.Token);
            if (version != _suggestVersion) return; // đã gõ tiếp / đã chọn xe trong lúc chờ
            if (!focus && (!_txtBarcode.Focused || !_txtBarcode.Enabled)) return;

            // Lọc theo mode: cân vào = chưa có lượt cân (lifecycle Registered/TempRegistered)
            //                 cân ra = đã cân lần 1 (lifecycle InProgress)
            var filtered = _isWeighOutMode
                ? allTrips.Where(t => t.Lifecycle == Domain.Enums.TripLifecycle.InProgress).ToArray()
                : allTrips.Where(t => t.Lifecycle is Domain.Enums.TripLifecycle.Registered
                    or Domain.Enums.TripLifecycle.TempRegistered).ToArray();

            if (!string.IsNullOrWhiteSpace(query))
                filtered = TripPickerList.Filter(filtered, query);

            // Thẻ 2 dòng, ngay dưới khung nhập (xem TripPickerList).
            _tripPicker?.Show(_barcodeHost, filtered, focus);
        }
        catch { _lstTripPopup.Visible = false; }
    }

    private void SelectTripFromPopup()
    {
        var trip = _tripPicker?.Selected;
        _lstTripPopup.Visible = false;
        if (trip is null) return;

        _txtBarcode.Text = trip.TripNo;

        // Trigger scan giống nhấn Enter
        _ = HandleQrScanAsync(trip.TripNo, enforceMode: true);
    }

    /// <summary>Panel giữa: số cân realtime + checklist điều kiện + thông tin cân vào/ra + dung sai.</summary>
    private Card BuildWeightPanel()
    {
        var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(1), AutoScroll = true };
        var header = GridBuilder.SectionHeader(Loc.T("weigh.section_weight"));

        // Khung chính chỉ 4 dòng: TL / TG cân vào, TL / TG cân ra. Phần còn lại (TL hàng, dung sai,
        // chính sách thanh toán mía) ở popup "Xem thêm thông tin cân" — gọn khung, không phải cuộn.
        // Theo lượt cân (xem UpdateWeightStageRows):
        //  - Cân vào: TL + TG cân vào.
        //  - Cân ra: TL + TG cân ra; mía thêm TL thanh toán + tạp chất thanh toán theo chính sách FRM.
        var table = FormLayout.FieldTable();
        _weightTable = table;
        FormLayout.AddField(table, Loc.T("field.weight_in"), _txtWeightIn);
        FormLayout.AddField(table, Loc.T("field.time_in"), _txtTimeIn);
        FormLayout.AddField(table, Loc.T("field.weight_out"), _txtWeightOut);
        FormLayout.AddField(table, Loc.T("field.time_out"), _txtTimeOut);
        // Thứ tự: TL hàng → Tạp chất thanh toán → TL thanh toán (tính từ TL hàng và tạp chất).
        FormLayout.AddField(table, Loc.T("field.net_weight"), _txtNetWeight);
        FormLayout.AddField(table, Loc.T("field.tc_thanh_toan"), _txtTcThanhToan);
        FormLayout.AddField(table, Loc.T("field.tl_thanh_toan"), _txtTlThanhToan);

        // Ô TL hàng, dung sai, chính sách vẫn giữ (ngoài layout) — luồng cân vẫn ghi / đọc giá trị như cũ.
        _btnPolicyPreview.Enabled = false;
        _btnPolicyPreview.Click += async (_, _) =>
        {
            _btnPolicyPreview.Enabled = false;
            try { await LoadOrPreviewPolicyAsync(allowPreview: true, forceRefresh: true); }
            finally { _btnPolicyPreview.Enabled = _sessionId is not null && _currentSegmentIndex > 0; }
        };

        // Nút "Xem thêm thông tin cân" trên thanh tiêu đề (cùng kiểu nút "Xem thêm thông tin chuyến").
        var btnWeighMore = new Button
        {
            Text = Loc.T("weigh.btn_weigh_more"),
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            FlatStyle = FlatStyle.Flat,
            ForeColor = AgrisTheme.BrandGreen,
            BackColor = AgrisTheme.Surface,
            Font = AgrisTheme.Body(9F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand
        };
        btnWeighMore.FlatAppearance.BorderColor = AgrisTheme.BrandGreen;
        btnWeighMore.FlatAppearance.BorderSize = 1;
        btnWeighMore.Click += (_, _) => ShowWeighMoreDialog();
        var headerAction = new Panel
        {
            Dock = DockStyle.Right,
            Width = 260,
            Padding = new Padding(0, 4, 8, 4),
            BackColor = Color.Transparent
        };
        headerAction.Controls.Add(btnWeighMore);
        header.Controls.Add(headerAction);
        UpdateWeightStageRows(); // mặc định: lượt cân vào

        // Controls thêm theo thứ tự ngược (WinForms Dock stacking):
        // table (Fill) → _lblCcsWarning (Bottom) → _lblQrMessage (Bottom) → _checklist (Top) → _display (Top) → header (Top)
        // Khung Trọng lượng chỉ còn số cân: TL / TG cân vào ra, TL thanh toán... KHÔNG hiện trên khung,
        // xem ở nút "Xem thêm thông tin cân". Bảng vẫn dựng (ngoài layout) để các ô mang giá trị cho popup / lưu / in.
        _display.Dock = DockStyle.Fill;
        card.Controls.Add(_display);
        card.Controls.Add(_lblCcsWarning);
        // Dòng thông báo cân (_lblQrMessage) đã chuyển xuống cuối khung "Thông tin chuyến" — khung Trọng lượng không phải cuộn.
        // Checklist điều kiện chốt cân (QR hợp lệ, Vị trí AI, Cân ổn định, Điều kiện trước) — TẠM ẨN theo yêu cầu.
        // Server vẫn đánh giá đủ điều kiện khi chốt; điều kiện chưa đạt vẫn báo bằng thông báo chặn.
        // Hiện lại: bỏ dòng Visible = false.
        _checklist.Visible = false;
        card.Controls.Add(_checklist);
        card.Controls.Add(header);
        return card;
    }

    /// <summary>
    /// Popup "Xem thêm thông tin cân": đủ số cân của chuyến đang mở — TL / TG vào ra, TL hàng, dung sai;
    /// mía thêm chính sách thanh toán (tên CS, TC / CCS / TL thanh toán) + nút xem trước chính sách.
    /// Chỉ đọc, lấy đúng giá trị đang có trên form (không tính lại ở UI).
    /// </summary>
    private void ShowWeighMoreDialog()
    {
        var isMaterial = _currentSourceSystem is not null
            ? _currentSourceSystem == TripCategoryExtensions.MATERIAL_SOURCE_SYSTEM
            : _gridCategory.Category == TripCategory.Material;

        using var popup = new Form
        {
            Text = Loc.T("weigh.weigh_more_title"),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            BackColor = AgrisTheme.Surface,
            Font = AgrisTheme.Body(),
            AutoScroll = true,
            ClientSize = new Size(720, isMaterial ? 440 : 720)
        };

        var table = FormLayout.FieldTable();
        table.Dock = DockStyle.Top;
        table.Padding = new Padding(12, 10, 12, 10);

        // Mọi ô cùng một cỡ chữ lớn, dễ đọc (không chép font của ô gốc — ô gốc ẩn nên có thể chưa được áp theme).
        var valueFont = AgrisTheme.Body(13F);
        var keyFont = AgrisTheme.Body(13F, FontStyle.Bold);
        var mirrors = new List<(TextBox Source, TextBox Copy)>();
        void Mirror(string caption, TextBox source, bool key = false)
        {
            var copy = ReadOnlyField(caption);
            copy.Font = key ? keyFont : valueFont;
            copy.Dock = DockStyle.Fill;
            copy.Margin = new Padding(0, 6, 0, 6);
            FormLayout.AddField(table, caption, copy);
            mirrors.Add((source, copy));
        }

        Mirror(Loc.T("field.weight_in"), _txtWeightIn);
        Mirror(Loc.T("field.time_in"), _txtTimeIn);
        Mirror(Loc.T("field.weight_out"), _txtWeightOut);
        Mirror(Loc.T("field.time_out"), _txtTimeOut);
        Mirror(Loc.T("field.net_weight"), _txtNetWeight, key: true);
        Mirror(Loc.T("field.tolerance"), _txtTolerance);
        var lblRange = new Label
        {
            AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            Font = AgrisTheme.Body(10.5F), ForeColor = AgrisTheme.TextSecondary
        };
        FormLayout.AddFullWidth(table, lblRange, 30);

        Button? btnPreview = null;
        if (!isMaterial)
        {
            var policyNote = GridBuilder.Notice(Loc.T("weigh.policy_section_note"), AgrisTheme.Info);
            policyNote.AutoEllipsis = false;
            FormLayout.AddFullWidth(table, policyNote, 48);
            Mirror(Loc.T("field.policy_name"), _txtPolicyName);
            Mirror(Loc.T("field.tc_thanh_toan"), _txtTcThanhToan);
            Mirror(Loc.T("field.ccs_thanh_toan"), _txtCcsThanhToan);
            Mirror(Loc.T("field.tl_thanh_toan"), _txtTlThanhToan, key: true);
            btnPreview = GridBuilder.SecondaryButton(_btnPolicyPreview.Text, new Size(340, 38));
            btnPreview.AutoSize = true; // chữ dài (Lào / Khmer) không bị cắt
            btnPreview.Dock = DockStyle.Left;
            FormLayout.AddFullWidth(table, btnPreview, 44);
        }

        void Refresh()
        {
            foreach (var (source, copy) in mirrors)
            {
                copy.Text = string.IsNullOrWhiteSpace(source.Text) ? "—" : source.Text;
                copy.ForeColor = source.ForeColor;
            }
            lblRange.Text = _lblToleranceRange.Text;
            if (btnPreview is not null) btnPreview.Enabled = _btnPolicyPreview.Enabled;
        }

        if (btnPreview is not null)
        {
            btnPreview.Click += async (_, _) =>
            {
                btnPreview.Enabled = false;
                _btnPolicyPreview.PerformClick();
                // Chờ lượt xem trước chính sách chạy xong (nút gốc bật lại) rồi cập nhật popup.
                while (!popup.IsDisposed && !_btnPolicyPreview.Enabled && _sessionId is not null && _currentSegmentIndex > 0)
                    await Task.Delay(200);
                if (!popup.IsDisposed) Refresh();
            };
        }

        var btnClose = GridBuilder.SecondaryButton(Loc.T("common.close"), new Size(120, 38));
        btnClose.Click += (_, _) => popup.Close();
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 56, FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12, 9, 12, 8), BackColor = AgrisTheme.SurfaceMuted
        };
        bar.Controls.Add(btnClose);

        popup.Controls.Add(table);
        popup.Controls.Add(bar);
        popup.CancelButton = btnClose;
        Refresh();

        // Cao vừa đủ nội dung (không cuộn); màn thấp thì tối đa 92% vùng làm việc.
        var work = Screen.FromControl(this).WorkingArea;
        var height = table.GetPreferredSize(new Size(popup.ClientSize.Width, 0)).Height + bar.Height + 8;
        popup.ClientSize = new Size(Math.Min(720, work.Width - 40), Math.Min(height, (int)(work.Height * 0.92)));
        popup.ShowDialog(this);
    }

    private TableLayoutPanel? _weightTable;
    /// <summary>Đang xem lại chuyến đã cân đủ (bấm dòng tab "Đã cân đủ") — không phải lượt cân.</summary>
    private bool _viewingCompleted;

    /// <summary>
    /// Khung Trọng lượng theo lượt: cân vào chỉ hiện TL / TG cân vào; cân ra hiện TL / TG cân ra,
    /// mía thêm TL thanh toán + tạp chất thanh toán (chính sách FRM). Đủ số liệu xem ở "Xem thêm thông tin cân".
    /// </summary>
    private void UpdateWeightStageRows()
    {
        if (_weightTable is not { } t) return;
        var isMaterial = _currentSourceSystem is not null
            ? _currentSourceSystem == TripCategoryExtensions.MATERIAL_SOURCE_SYSTEM
            : _gridCategory.Category == TripCategory.Material;
        var weighOut = _isWeighOutMode;
        // Xem lại chuyến "Đã cân đủ": hiện cả cân vào, cân ra và số thanh toán.
        var all = _viewingCompleted;

        // Xem lại "Đã cân đủ": mía chỉ hiện Tạp chất thanh toán + TL thanh toán (số cân vào / ra có trên lưới
        // và "Xem thêm thông tin cân"); vật tư không có thanh toán → hiện số cân vào / ra.
        var completedMia = all && !isMaterial;
        var completedMaterial = all && isMaterial;

        SetFlowRowVisible(t, _txtWeightIn, completedMaterial || (!all && !weighOut));
        SetFlowRowVisible(t, _txtTimeIn, completedMaterial || (!all && !weighOut));
        SetFlowRowVisible(t, _txtWeightOut, completedMaterial || (!all && weighOut));
        SetFlowRowVisible(t, _txtTimeOut, completedMaterial || (!all && weighOut));
        SetFlowRowVisible(t, _txtNetWeight, completedMia);
        SetFlowRowVisible(t, _txtTcThanhToan, completedMia || (!all && weighOut && !isMaterial));
        SetFlowRowVisible(t, _txtTlThanhToan, completedMia || (!all && weighOut && !isMaterial));
    }

    /// <summary>Thêm dòng nhãn + ô và ghi nhận dòng thuộc riêng một luồng (mía / vật tư).</summary>
    private static void AddFlowField(TableLayoutPanel table, string caption, Control field,
        List<(TableLayoutPanel Table, Control Field)> flowRows)
    {
        FormLayout.AddField(table, caption, field);
        flowRows.Add((table, field));
    }

    /// <summary>
    /// Bố cục theo luồng: vật tư hiện thông tin như màn Đăng tài vật tư, ẩn phần thanh toán chính sách FRM;
    /// mía giữ nguyên như cũ. Chưa chọn chuyến → theo tab Mía/Vật tư của lưới.
    /// </summary>
    private void ApplyFlowLayout()
    {
        var isMaterial = _currentSourceSystem is not null
            ? _currentSourceSystem == TripCategoryExtensions.MATERIAL_SOURCE_SYSTEM
            : _gridCategory.Category == TripCategory.Material;

        foreach (var (table, field) in _materialOnlyRows) SetFlowRowVisible(table, field, isMaterial);
        foreach (var (table, field) in _sugarcaneOnlyRows) SetFlowRowVisible(table, field, !isMaterial);
        if (isMaterial) _lblCcsWarning.Visible = false; // cảnh báo chữ đường chỉ dành cho mía
        // Không hiện tên trung gian DigiScale: mía đẩy FRM, vật tư đẩy ERP.
        _btnSync.Text = "🔄 " + Loc.T(isMaterial ? "weigh.btn_retry_erp" : "weigh.btn_retry_frm");
        UpdateWeightStageRows();
        ApplyGridColumnsForFlow();
    }

    /// <summary>
    /// Lưới theo tab Mía / Vật tư: mía đổi cột "Shipment" thành "Mã lệnh đốn" và ẩn cột Số TRIP;
    /// vật tư giữ nguyên.
    /// </summary>
    private void ApplyGridColumnsForFlow()
    {
        var isMaterialGrid = _gridCategory.Category == TripCategory.Material;
        foreach (DataGridViewColumn c in _dgvWeighed.Columns)
            if (c.DataPropertyName == "SyncStatus")
                using (Loc.UseSystem(GridSystem)) c.HeaderText = Loc.T("col.sync_status");
        foreach (DataGridViewColumn col in _dgvWeighed.Columns)
        {
            if (col.DataPropertyName == "TripNumber") col.Visible = isMaterialGrid;
            else if (col.DataPropertyName is nameof(WeighedTripRow.ImpurityActual) or nameof(WeighedTripRow.ImpurityPaid)
                     or nameof(WeighedTripRow.CcsActual) or nameof(WeighedTripRow.CcsPaid))
                col.Visible = !isMaterialGrid; // chỉ lưới mía
            else if (col.DataPropertyName == "ShipmentNo")
                col.HeaderText = isMaterialGrid ? Loc.T("col.shipment") : Loc.T("col.lenhdon_code");
        }
    }

    private void SetFlowRowVisible(TableLayoutPanel table, Control field, bool visible)
    {
        var row = table.GetRow(field);
        if (row < 0 || row >= table.RowStyles.Count) return;

        var style = table.RowStyles[row];
        if (!_flowRowHeights.ContainsKey(field)) _flowRowHeights[field] = style.Height;

        table.SuspendLayout();
        style.Height = visible ? _flowRowHeights[field] : 0;
        field.Visible = visible;
        if (table.GetControlFromPosition(0, row) is { } caption && caption != field)
            caption.Visible = visible;
        table.ResumeLayout(true);
    }

    /// <summary>
    /// Bổ sung thông tin vật tư từ dữ liệu ERP đã kéo về (Số TRIP, Hướng, NVC, Kho, Lái xe…).
    /// Kết nối DB riêng / API — không đụng DbContext của form. Chuyến đã đổi trong lúc chờ → bỏ qua.
    /// </summary>
    private async Task LoadMaterialDetailsAsync(Guid? tripId, string? shipment)
    {
        try
        {
            TTC.Weighing.Infrastructure.Adapters.ErpOrderLine? line = null;
            if (!string.IsNullOrWhiteSpace(shipment))
            {
                try { line = await _presenter.LookupErpOrderAsync(_presenter.DefaultBusinessUnit, shipment.Trim()); }
                catch { /* ERP chưa kéo về / mất kết nối → hiện phần có trên chuyến */ }
            }

            var transporter = await _presenter.ResolveTransporterAsync(tripId, line, _cts.Token);
            var warehouse = await _presenter.ResolveWarehouseAsync(tripId, line, _cts.Token);

            if (IsDisposed || _currentSourceSystem != TripCategoryExtensions.MATERIAL_SOURCE_SYSTEM
                || (_lastScan?.TripId ?? _currentTripId) != tripId)
                return;

            _txtErpTripNumber.Text = line?.TripNumber ?? shipment ?? "";
            _txtDirection.Text = line is null ? "" : Loc.T(line.IsShipConfirmFlow ? "reg.direction_out" : "reg.direction_in");
            _txtTransporter.Text = transporter ?? "";
            if (!string.IsNullOrWhiteSpace(warehouse)) _txtWarehouse.Text = warehouse;
            if (string.IsNullOrWhiteSpace(_txtDriver.Text)) _txtDriver.Text = line?.DriverName ?? "";
            if (string.IsNullOrWhiteSpace(_txtCustomer.Text)) _txtCustomer.Text = line?.VendorName ?? line?.CustomerName ?? "";
            var item = line?.ItemDescription ?? line?.ItemName;
            if (!string.IsNullOrWhiteSpace(item)) _txtCommodity.Text = item;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _lblQrMessage.Text = "⚠ ERP: " + ex.Message;
        }
    }

    /// <summary>Panel phải: camera AI + hình ảnh cân vào/ra + kết quả AI phân tích.</summary>
    private Card BuildCameraPanel()
    {
        var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(1) };
        var header = GridBuilder.SectionHeader(Loc.T("weigh.section_camera"));

        var camLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        camLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        camLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        camLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _cameraIn.Dock = DockStyle.Fill;
        _cameraIn.Margin = new Padding(2);
        _cameraOut.Dock = DockStyle.Fill;
        _cameraOut.Margin = new Padding(2);

        camLayout.Controls.Add(_cameraIn, 0, 0);
        camLayout.Controls.Add(_cameraOut, 1, 0);

        // AI Camera Status panel (dưới camera)
        _cameraAiStatus.Dock = DockStyle.Bottom;
        _cameraAiStatus.Height = 130;

        // Simulation controls cho camera AI (chỉ hiện khi Preview)
        Panel? simPanel = null;
        if (_simulatedCameraAi is not null)
        {
            simPanel = BuildCameraAiSimBar();
            simPanel.Dock = DockStyle.Bottom;
        }

        card.Controls.Add(camLayout);
        if (simPanel is not null)
            card.Controls.Add(simPanel);
        card.Controls.Add(_cameraAiStatus);
        card.Controls.Add(header);
        return card;
    }

    /// <summary>
    /// Thanh giả lập camera AI — nút bấm mô phỏng các kịch bản: có người, xe lệch, thùng đóng.
    /// </summary>
    private Panel BuildCameraAiSimBar()
    {
        var bar = new FlowLayoutPanel
        {
            Height = 36,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = AgrisTheme.WarningSoft,
            Padding = new Padding(4, 2, 4, 2)
        };

        var lbl = new Label
        {
            Text = Loc.T("weigh.sim_ai_label"),
            AutoSize = true,
            Font = AgrisTheme.Body(8.5F, FontStyle.Bold),
            ForeColor = AgrisTheme.Warning,
            Margin = new Padding(0, 7, 4, 0)
        };

        var btnNormal = GridBuilder.SecondaryButton(Loc.T("weigh.sim_ai_normal"), new Size(100, 28));
        btnNormal.Font = AgrisTheme.Body(8.5F);
        btnNormal.Click += (_, _) => { _simulatedCameraAi!.ResetToNormal(); PollCameraAi(); };

        var btnPerson = GridBuilder.SecondaryButton(Loc.T("weigh.sim_ai_person"), new Size(90, 28));
        btnPerson.Font = AgrisTheme.Body(8.5F);
        btnPerson.Click += (_, _) => { _simulatedCameraAi!.SetPersonDetected(true); PollCameraAi(); };

        var btnMisalign = GridBuilder.SecondaryButton(Loc.T("weigh.sim_ai_misalign"), new Size(80, 28));
        btnMisalign.Font = AgrisTheme.Body(8.5F);
        btnMisalign.Click += (_, _) => { _simulatedCameraAi!.SetAlignment(VehicleAlignmentStatus.Misaligned); PollCameraAi(); };

        var btnClosed = GridBuilder.SecondaryButton(Loc.T("weigh.sim_ai_closed"), new Size(95, 28));
        btnClosed.Font = AgrisTheme.Body(8.5F);
        btnClosed.Click += (_, _) => { _simulatedCameraAi!.SetCargoStatus(CargoStatus.Closed); PollCameraAi(); };

        var btnNoVehicle = GridBuilder.SecondaryButton(Loc.T("weigh.sim_ai_no_vehicle"), new Size(80, 28));
        btnNoVehicle.Font = AgrisTheme.Body(8.5F);
        btnNoVehicle.Click += (_, _) => { _simulatedCameraAi!.SetAlignment(VehicleAlignmentStatus.NoVehicle); PollCameraAi(); };

        bar.Controls.AddRange([lbl, btnNormal, btnPerson, btnMisalign, btnClosed, btnNoVehicle]);
        return bar;
    }

    /// <summary>Thanh nút: Lưu phiếu (F5), In phiếu (F6), Sync ERP, Cân lại, Nhập tay, Lượt mới (Esc).</summary>
    private Card BuildActionBar()
    {
        var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(8, 2, 8, 2) };
        var bar = FormLayout.ActionBar(_btnSave, _btnPrint, _btnSync, _btnManualWeight, _btnReset);
        bar.Dock = DockStyle.Fill;
        card.Controls.Add(bar);
        return card;
    }

    /// <summary>Lưới danh sách xe đã cân + tab lọc + bộ lọc ngày.</summary>
    private Card BuildGridArea()
    {
        var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(1) };
        var header = GridBuilder.SectionHeader(Loc.T("weigh.section_grid"));

        _btnTabPending.Margin = new Padding(0, 0, 4, 0);
        _btnTabWeighIn.Margin = new Padding(0, 0, 4, 0);
        _btnTabImpurity.Margin = new Padding(0, 0, 4, 0);
        _btnTabBoth.Margin = new Padding(0, 0, 12, 0);

        var lblDate = GridToolbar.FilterLabel(Loc.T("weigh.filter_date"));
        _dtpFrom.Margin = new Padding(0, 4, 6, 0);
        _dtpTo.Margin = new Padding(0, 4, 0, 0);

        _dtpFrom.Value = VnTime.Today.AddDays(-7);
        _dtpTo.Value = VnTime.Today;

        // Default: tab "Chờ cân" active
        _btnTabPending.BackColor = AgrisTheme.BrandGreen;
        _btnTabPending.ForeColor = AgrisTheme.TextOnBrand;
        _btnTabWeighIn.BackColor = AgrisTheme.Surface;
        _btnTabWeighIn.ForeColor = AgrisTheme.TextPrimary;

        _btnExpandGrid.Margin = new Padding(8, 0, 0, 0);
        _btnExpandGrid.Click += (_, _) => ToggleGridExpanded();

        _btnExportExcel.Margin = new Padding(8, 0, 0, 0);
        _btnExportExcel.Click += async (_, _) => await ExportWeighedGridAsync();

        _gridCategory.CategoryChanged += async (_, _) =>
        {
            _pager.Reset();
            GridBuilder.ApplyCategoryHeader(card, header, Loc.T("weigh.section_grid"), _gridCategory.Category);
            UpdateGridTabStyles(); // tab "Đo tạp chất" chỉ có ở Mía
            if (_currentTripId is null && _sessionId is null) ApplyFlowLayout(); // chưa chọn chuyến → bố cục theo tab
            else ApplyGridColumnsForFlow(); // lưới luôn theo tab đang chọn
            await LoadWeighedTripsAsync();
        };

        // Tìm kiếm + lọc theo cột (bấm tiêu đề) như bảng Dữ liệu FRM / ERP — lọc trên toàn bộ danh sách.
        _weighFilter = new GridListFilter<WeighedTripRow>(_dgvWeighed,
            () => { _pager.Reset(); BindCurrentPage(); },
            (prop, row) => prop == nameof(WeighedTripRow.SyncStatus)
                ? SyncStatusDisplay(row.SyncStatus?.ToString()).Text
                : null);

        // Hàng 1: nhóm Mía/Vật tư + các tab trạng thái. Hàng 2: khoảng ngày + ô tìm kiếm cạnh nhau
        // (giống màn Đăng tài). Bên phải: Xóa lọc, số dòng, Xuất Excel, Phóng to.
        _weighFilter.SearchBox.Margin = new Padding(8, 3, 0, 0);
        var filterBar = new GridToolbar(
            [_gridCategory, GridToolbar.Divider(), _btnTabPending, _btnTabImpurity, _btnTabWeighIn, _btnTabBoth,
             GridToolbar.Group(lblDate, _dtpFrom, _dtpTo, _weighFilter.SearchBox)],
            [_weighFilter.ClearButton, _weighFilter.CountLabel, _btnExportExcel, _btnExpandGrid]);
        filterBar.LeftItems.SetFlowBreak(_btnTabBoth, true);

        GridBuilder.ApplyCategoryHeader(card, header, Loc.T("weigh.section_grid"), _gridCategory.Category);

        ConfigureWeighGrid();
        GridBuilder.AttachEmptyState(_dgvWeighed, Loc.T("weigh.no_trips"));
        _dgvWeighed.CellClick += OnWeighGridCellClick;

        _pager.PageChanged += (_, _) =>
        {
            BindCurrentPage();
            if (_dgvWeighed.Rows.Count > 0)
                _dgvWeighed.FirstDisplayedScrollingRowIndex = 0;
        };

        // Chế độ thường không phân trang (lưới cuộn dọc); bật khi Phóng to lưới.
        _pager.PagingEnabled = false;

        // Thứ tự Add ngược thứ tự dock: lưới (Fill) → pager (Bottom) → thanh lọc → tiêu đề.
        card.Controls.Add(_dgvWeighed);
        card.Controls.Add(_pager);
        card.Controls.Add(filterBar);
        card.Controls.Add(header);

        _gridScroll = GridScrollBar.Attach(_dgvWeighed);
        return card;
    }

    private void ConfigureWeighGrid()
    {
        _dgvWeighed.Columns.AddRange(
            GridBuilder.Column(Loc.T("col.reg_no"), "RegNo", 100),
            GridBuilder.Column(Loc.T("col.ticket_no"), "TicketNo", 110),
            GridBuilder.Column(Loc.T("col.trip_number"), "TripNumber", 80),
            GridBuilder.Column(Loc.T("col.shipment"), "ShipmentNo", 100),
            GridBuilder.Column(Loc.T("col.plate"), "Plate", 80),
            GridBuilder.Column(Loc.T("col.time_in"), "TimeIn", 120),
            GridBuilder.Column(Loc.T("col.weight_in_col"), "WeightIn", 100, "N3"),
            GridBuilder.Column(Loc.T("col.time_out"), "TimeOut", 120),
            GridBuilder.Column(Loc.T("col.weight_out_col"), "WeightOut", 100, "N3"),
            GridBuilder.Column(Loc.T("col.net_col"), "NetWeight", 100, "N3"),
            GridBuilder.Column(Loc.T("col.customer"), "Customer", 130),
            GridBuilder.Column(Loc.T("col.commodity"), "Commodity", 100),
            // Mía: tạp chất / CCS thực tế (đo tại trạm) và thanh toán (chính sách FRM). Ẩn ở tab Vật tư.
            GridBuilder.Column(Loc.T("col.impurity_actual"), nameof(WeighedTripRow.ImpurityActual), 95, "N2"),
            GridBuilder.Column(Loc.T("col.impurity_paid"), nameof(WeighedTripRow.ImpurityPaid), 95, "N2"),
            GridBuilder.Column(Loc.T("col.ccs_actual"), nameof(WeighedTripRow.CcsActual), 85, "N2"),
            GridBuilder.Column(Loc.T("col.ccs_paid"), nameof(WeighedTripRow.CcsPaid), 85, "N2"),
            GridBuilder.Column(Loc.T("col.sync_status"), "SyncStatus", 90));

        // Cột Đồng bộ: hiện text thân thiện thay vì enum/null
        _dgvWeighed.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            if (_dgvWeighed.Columns[e.ColumnIndex].DataPropertyName != "SyncStatus") return;

            var status = e.Value?.ToString();
            var (text, color) = SyncStatusDisplay(status);
            if (text is not null)
            {
                e.Value = text;
                if (e.CellStyle is not null)
                    e.CellStyle.ForeColor = color;
            }
            e.FormattingApplied = true;
        };
    }

    /// <summary>Chữ + màu hiển thị cho trạng thái đồng bộ — dùng chung cho lưới và file Excel.</summary>
    /// Tab Mía → "Đã đẩy lên FRM", tab Vật tư → "Đã đẩy lên ERP".
    private (string? Text, Color Color) SyncStatusDisplay(string? status)
    {
        using var _ = Loc.UseSystem(GridSystem);
        return status switch
        {
            null or "" => (Loc.T("sync.not_pushed"), AgrisTheme.Warning),
            "Posted" or "Acknowledged" => (Loc.T("sync.pushed"), AgrisTheme.Success),
            "Failed" or "Unknown" => (Loc.T("sync.error"), AgrisTheme.Danger),
            "Held" or "New" => (Loc.T("sync.waiting"), AgrisTheme.Info),
            _ => (null, AgrisTheme.TextPrimary)
        };
    }

    /// <summary>Hệ thống đích của lưới đang xem (theo tab Mía / Vật tư).</summary>
    private string GridSystem => _gridCategory.Category == TripCategory.Material ? Loc.SYSTEM_ERP : Loc.SYSTEM_FRM;

    /// <summary>Xuất toàn bộ danh sách đang lọc (mọi trang) của lưới xe đã cân ra Excel.</summary>
    private async Task ExportWeighedGridAsync()
    {
        var tab = _gridMode switch
        {
            0 => _btnTabPending.Text,
            1 => _btnTabWeighIn.Text,
            GRID_MODE_IMPURITY => _btnTabImpurity.Text,
            _ => _btnTabBoth.Text
        };

        var filters = new List<string>
        {
            Loc.T("export.filter_category", _gridCategory.CategoryLabel),
            Loc.T("export.filter_tab", tab)
        };

        // Tab "Chờ cân" không lọc theo ngày.
        if (_gridMode != 0)
            filters.Add(Loc.T("export.filter_range", _dtpFrom.Value.ToString("dd/MM/yyyy"), _dtpTo.Value.ToString("dd/MM/yyyy")));

        try
        {
            await ExportHelper.ExportGridAsync(
                this, _dgvWeighed, _allWeighedRows,
                new ExportHelper.ExportRequest(
                    $"{Loc.T("weigh.section_grid")} - {_gridCategory.CategoryLabel}",
                    $"xe-da-can_{_gridCategory.FileSuffix}",
                    filters,
                    _gridCategory.CategoryLabel),
                (property, _, raw) => property == nameof(WeighedTripRow.SyncStatus)
                    ? SyncStatusDisplay(raw?.ToString()).Text ?? raw
                    : raw,
                _btnExportExcel);
        }
        catch (Exception ex)
        {
            Helpers.AppMessageBox.Show(this, $"{Loc.T("export.error")}{Environment.NewLine}{ex.Message}",
                Loc.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private DeviceStatusBar BuildDeviceStatusBar()
    {
        _deviceStatusBar.Dock = DockStyle.Fill;
        return _deviceStatusBar;
    }

    // ═══════════════════════════════════════════════════════════
    //  Barcode input → QR scan flow (Req 1.1, 1.3, 1.4, 1.5)
    // ═══════════════════════════════════════════════════════════

    private async void OnBarcodeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter) return;

        e.Handled = true;
        e.SuppressKeyPress = true;

        var qrContent = _txtBarcode.Text.Trim();
        if (string.IsNullOrWhiteSpace(qrContent)) return;

        await HandleQrScanAsync(qrContent, enforceMode: true);
    }

    /// <summary>
    /// Luồng chính khi nhận barcode input:
    /// 1. Gọi presenter.ScanQrAsync → decode + validate + lookup trip
    /// 2. Phân loại kết quả: QR hợp lệ, QR tạm, QR lỗi
    /// 3. Nếu hợp lệ: điền thông tin, mở phiên cân, bắt đầu poll
    /// </summary>
    /// <summary>
    /// Quét QR từ bên ngoài (event bus auto-navigate). Điền QR vào barcode + trigger scan.
    /// Reset trạng thái cũ trước để không dính dữ liệu chuyến trước (cross-tab navigate).
    /// </summary>
    /// <summary>
    /// Máy quét QR (số đăng tài trên phiếu) khi con trỏ KHÔNG ở ô nhập mã: điền vào ô và quét như bấm Enter.
    /// Đang có phiên cân (ô nhập khóa) → không đổi chuyến giữa chừng, chỉ nhắc bấm Lượt mới.
    /// </summary>
    private async void OnQrScanned(string content)
    {
        if (_isBusy)
        {
            // Không bỏ im lặng: báo để người cân biết cần quét lại.
            _lblQrMessage.Text = "⚠ " + Loc.T("scan.busy_retry", content);
            _lblQrMessage.ForeColor = AgrisTheme.Warning;
            return;
        }
        if (!_txtBarcode.Enabled)
        {
            _lblQrMessage.Text = "⚠ " + Loc.T("scan.busy_finish_first", content);
            _lblQrMessage.ForeColor = AgrisTheme.Warning;
            return;
        }
        _lstTripPopup.Visible = false;
        _txtBarcode.Text = content;
        await HandleQrScanAsync(content, enforceMode: true);
    }

    internal async Task AutoScanQrAsync(string qrContent)
    {
        OnResetClicked(this, EventArgs.Empty);
        _txtBarcode.Enabled = true;
        _txtBarcode.Text = qrContent;
        // Defer scan để Load event (LoadWeighedTripsAsync) kịp release _dbLock trước
        await Task.Yield();
        await HandleQrScanAsync(qrContent);
    }

    /// <param name="enforceMode">
    /// true = người trực cân tự quét/nhập → kiểm tra lượt cân khớp chế độ CÂN VÀO / CÂN RA.
    /// false = hệ thống tự điều hướng (sau tạp chất, chuyển tab) → tự xác định lượt cân.
    /// </param>
    private async Task HandleQrScanAsync(string qrContent, bool enforceMode = false)
    {
        HideTripSuggestions();
        if (_isBusy) return;

        _lastQrContent = qrContent; // Lưu QR để truyền qua event bus sau commit

        SetBusy(true);
        StopTimers();
        await _dbLock.WaitAsync(_cts.Token);

        try
        {
            var siteId = _siteContext.CurrentSiteId;
            var scanResult = await _presenter.ScanQrAsync(qrContent, siteId, _cts.Token);

            // ── QR tạm: cho phép cân bình thường (cân offline → sau đẩy lên hệ thống chính) ──
            // Chỉ cảnh báo nhẹ trên UI, không chặn.
            _currentQrIsTemp = scanResult.QrType == QrTokenType.Temp;
            if (scanResult.QrType == QrTokenType.Temp)
            {
                _lblQrMessage.Text = Loc.T("weigh.qr_temp_warning");
                _lblQrMessage.ForeColor = AgrisTheme.Warning;
                // Tiếp tục flow — không return
            }

            // ── QR không hợp lệ / hết hạn / thu hồi (Req 1.3) ──
            if (!scanResult.IsSuccess)
            {
                var errorDisplay = !string.IsNullOrEmpty(scanResult.ErrorCode)
                    ? $"[{scanResult.ErrorCode}] {scanResult.ErrorMessage}"
                    : scanResult.ErrorMessage ?? Loc.T("weigh.qr_invalid");

                // Lệnh đốn có trên FRM nhưng chưa đăng tài → báo đúng lý do (không chỉ "không tìm thấy").
                if (scanResult.ErrorCode == "NOT_FOUND"
                    && _presenter.FindLenhDonByCodeOrPlate(qrContent) is { } ldNoReg)
                {
                    errorDisplay = Loc.T("weigh.lenhdon_not_registered",
                        ldNoReg.MaLD, ldNoReg.VehiclePlate ?? qrContent.Trim());
                    Helpers.AppMessageBox.Show(this, errorDisplay, Loc.T("weigh.lenhdon_not_registered_title"),
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                _lblQrMessage.Text = errorDisplay;
                _lblQrMessage.ForeColor = AgrisTheme.Danger;
                _txtBarcode.SelectAll();
                _txtBarcode.Focus();
                return;
            }

            // ── QR hợp lệ — điền thông tin chuyến (Req 1.1) ──
            PopulateTripInfo(scanResult);

            // Xác định bàn cân từ AssignedWeighbridgeId trong QR (Req 1.5)
            // QR tạm có thể không có bàn cân gán sẵn → lấy bàn cân đầu tiên tại site.
            var weighbridgeId = scanResult.AssignedWeighbridgeId;
            if (weighbridgeId is null || weighbridgeId == Guid.Empty)
            {
                var defaultBridge = await _presenter.GetDefaultWeighbridgeAsync(_cts.Token);
                weighbridgeId = defaultBridge;
            }

            if (weighbridgeId is null || weighbridgeId == Guid.Empty)
            {
                _lblQrMessage.Text = Loc.T("weigh.no_weighbridge");
                _lblQrMessage.ForeColor = AgrisTheme.Danger;
                return;
            }

            // Xác định segment: segment 0 = cân lần 1, segment > 0 = cân lần 2+
            // DetermineSegmentIndexAsync kiểm tra preconditions để xác định lần cân.
            var segmentIndex = await DetermineSegmentIndexAsync(scanResult, _cts.Token);
            var checkpoint = segmentIndex == 0
                ? CheckpointType.FirstWeight
                : CheckpointType.SecondWeight;

            // ── Chặn sai lượt cân theo chế độ đang chọn (CÂN VÀO / CÂN RA) ──
            if (await ValidateWeighStageAsync(scanResult, segmentIndex, enforceMode) is { } stageError)
            {
                _lblQrMessage.Text = stageError.Message;
                _lblQrMessage.ForeColor = AgrisTheme.Danger;
                Helpers.AppMessageBox.Show(this, stageError.Message, stageError.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _txtBarcode.SelectAll();
                _txtBarcode.Focus();
                return;
            }

            // ── QR chuyến hết hạn / thu hồi → chặn ngay khi quét, không để tới lúc Xác nhận mới báo lỗi ──
            if (await _presenter.GetTripQrStateAsync(scanResult.TripId!.Value, _cts.Token) is { IsValid: false } qrState
                && qrState.Type != "Temp")
            {
                _lblQrMessage.Text = Loc.T("weigh.qr_expired_short");
                _lblQrMessage.ForeColor = AgrisTheme.Danger;
                DigiScalePopup.ShowAlert(this, AlertKind.Error, Loc.T("weigh.qr_expired_title"),
                    reason: qrState.ExpiresAt is { } exp
                        ? Loc.T("weigh.qr_expired_reason", exp.ToVnTime().ToString("dd/MM/yyyy HH:mm"))
                        : Loc.T("weigh.qr_invalid_reason"),
                    action: Loc.T("weigh.qr_expired_action"),
                    facts:
                    [
                        (Loc.T("alert.fact_trip"), scanResult.TripNo),
                        (Loc.T("alert.fact_plate"), scanResult.Plate),
                        (Loc.T("alert.fact_lenhdon"), scanResult.FrmMaLenhDon ?? scanResult.ShipmentNo)
                    ]);
                _txtBarcode.Enabled = true;
                _txtBarcode.SelectAll();
                _txtBarcode.Focus();
                return;
            }

            // ── Cân lần 2: FRM sync + precondition check (Req 4.1, 4.2, 4.3) ──
            if (segmentIndex > 0)
            {
                var tripId = scanResult.TripId!.Value;

                // Load dữ liệu cân lần 1 lên form
                await LoadFirstWeighDataAsync(tripId);

                // Gọi FRM để đồng bộ thông tin chuyến tại thời điểm quét QR (Req 4.1)
                // FRM sync là optional — lỗi kết nối không chặn cân (offline mode).
                try
                {
                    var frmResult = await _presenter.SyncFrmAsync(tripId, siteId, _cts.Token);
                    if (frmResult.IsSuccess)
                        UpdateTripInfoFromFrm(frmResult);
                }
                catch (Exception frmEx)
                {
                    System.Diagnostics.Debug.WriteLine($"FRM sync skipped: {frmEx.Message}");
                }

                // Kiểm tra điều kiện trước lượt cân lần 2 (Req 4.2, 4.3)
                var preconditions = await _presenter.CheckPreconditionsAsync(
                    tripId, WeightRole.Tare, segmentIndex, _cts.Token);

                // Req 4.2: Chặn cân nếu chưa đo tạp chất
                if (!preconditions.ImpuritySatisfied)
                {
                    _lblQrMessage.Text = Loc.T("weigh.impurity_blocked");
                    _lblQrMessage.ForeColor = AgrisTheme.Danger;

                    // Hộp thông báo chuẩn của app (lý do + cách xử lý + thông tin xe), không dùng MessageBox thô.
                    DigiScalePopup.ShowAlert(this, AlertKind.Warning,
                        Loc.T("weigh.impurity_blocked_title"),
                        reason: Loc.T("weigh.impurity_blocked_dialog"),
                        action: Loc.T("weigh.impurity_blocked_action"),
                        facts:
                        [
                            (Loc.T("alert.fact_trip"), scanResult.TripNo),
                            (Loc.T("alert.fact_plate"), scanResult.Plate),
                            (Loc.T("alert.fact_lenhdon"), scanResult.FrmMaLenhDon ?? scanResult.ShipmentNo)
                        ]);

                    _txtBarcode.Enabled = true;
                    _txtBarcode.SelectAll();
                    _txtBarcode.Focus();
                    return;
                }

                // Check kho vận tạm bỏ — chưa có camera kiểm soát, sẽ thêm lại sau
                // if (!preconditions.WarehouseSatisfied) { ... }

                // Req 4.3: Cảnh báo thiếu chữ đường nhưng cho phép tiếp tục
                if (preconditions.CcsPending)
                {
                    _lblCcsWarning.Text = Loc.T("weigh.ccs_warning");
                    _lblCcsWarning.Visible = true;
                }
                else
                {
                    _lblCcsWarning.Visible = false;
                }

                // Chính sách thu mua sẽ được lấy SAU khi cân lần 2 thành công
                // (trong bước push phiếu cân với IsQuetCS = "Yes" + trọng lượng thực).
            }
            else
            {
                _lblCcsWarning.Visible = false;
            }

            // Mở phiên cân trên bàn cân tương ứng
            var openResult = await _presenter.OpenSessionAsync(
                new OpenWeighSessionCommand(
                    TripId: scanResult.TripId!.Value,
                    ExpectedTripVersion: scanResult.TripVersion!.Value,
                    WeighbridgeId: weighbridgeId.Value,
                    Checkpoint: checkpoint,
                    SegmentIndex: segmentIndex,
                    CorrelationId: Guid.NewGuid().ToString("N")),
                _cts.Token);

            if (!openResult.IsSuccess)
            {
                _lblQrMessage.Text = Loc.T("weigh.session_error", openResult.FailureReason);
                _lblQrMessage.ForeColor = AgrisTheme.Warning;
                return;
            }

            // Lưu trạng thái phiên cân cho các thao tác sau (poll, commit, reset)
            _currentTripId = scanResult.TripId!.Value;
            _ticketPrinted = false; // lượt cân mới: phiếu của lượt này chưa in
            _currentTripVersion = scanResult.TripVersion!.Value;
            _currentWeighbridgeId = weighbridgeId.Value;
            _currentCheckpoint = checkpoint;
            _currentSegmentIndex = segmentIndex;
            _viewingCompleted = false; // đang cân thật → khung theo lượt
            _isWeighOutMode = segmentIndex > 0;
            UpdateWeighModeButton();
            _sessionId = openResult.SessionId;
            _requiredStabilityWindow = openResult.StabilityWindow;

            // Loại mía lấy từ FRM — không cần hiện panel chọn thủ công
            // if (segmentIndex > 0)
            //     _pnlSugarcaneType.Visible = false;

            _lblQrMessage.Text = Loc.T("weigh.qr_scanning", CheckpointLabel(checkpoint));
            _lblQrMessage.ForeColor = AgrisTheme.BrandGreen;
            _btnSave.Enabled = true;
            _btnManualWeight.Enabled = true; // Bật nút nhập tay khi đã có chuyến (Req 11.1)
            _txtBarcode.Enabled = false; // Khóa input khi đang cân

            // Giả lập: mỗi lượt cân (vào/ra, mía/vật tư) bắt đầu chưa có số — chỉ có số sau khi Nhập tay.
            _simulatedScale?.Clear();
            _display.Update(null, "kg", ScaleDeviceStatus.Unknown, false, TimeSpan.Zero, null);

            _signalTimer.Start();
            _conditionTimer.Start();

            // ── Cảnh báo không kết nối được đầu cân ngay khi mở phiên (không đợi tới lúc bấm Xác nhận) ──
            await WarnIfScaleDisconnectedAsync();

            // ── Cân lần 2 mía (đã đo tạp chất): hiện chính sách + số thanh toán TRƯỚC khi xác nhận ──
            if (segmentIndex > 0 && _currentSourceSystem != "ERP" && !_currentQrIsTemp)
            {
                _btnPolicyPreview.Enabled = true;
                await LoadOrPreviewPolicyAsync(allowPreview: true);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ShowError(Loc.T("weigh.qr_error"), ex); }
        finally { _dbLock.Release(); SetBusy(false); }
    }

    /// <summary>
    /// Kiểm tra lượt cân có khớp chế độ đang chọn:
    /// - CÂN VÀO mà xe đã cân vào → lỗi (không lấy phiếu cân vào lần 2).
    /// - CÂN RA mà xe chưa cân vào → lỗi.
    /// - Xe đã cân ra (đủ 2 lượt) → lỗi xe đã cân ra.
    /// Trả null nếu hợp lệ.
    /// </summary>
    private async Task<(string Title, string Message)?> ValidateWeighStageAsync(
        QrScanResult scan, int segmentIndex, bool enforceMode)
    {
        if (scan.TripId is not { } tripId) return null;
        var plate = scan.Plate ?? scan.TripNo ?? "—";

        if (segmentIndex > 0)
        {
            // Lượt cân không lưu theo segment — tra theo vai trò. Đã cân ra = có ĐỦ cả Gross và Tare hiệu lực
            // (mía: vào Gross, ra Tare; vật tư xuất: vào Tare, ra Gross). Lượt sau cùng là lượt cân ra.
            var gross = await _presenter.GetEffectiveWeighEventAsync(tripId, WeightRole.Gross, 0, _cts.Token);
            var tare = await _presenter.GetEffectiveWeighEventAsync(tripId, WeightRole.Tare, 0, _cts.Token);

            if (gross is not null && tare is not null)
            {
                var outAt = gross.MeasuredAt > tare.MeasuredAt ? gross.MeasuredAt : tare.MeasuredAt;
                return (Loc.T("weigh.already_out_title"),
                    Loc.T("weigh.already_out_msg", plate, outAt.ToVnTime().ToString("dd/MM/yyyy HH:mm")));
            }

            if (enforceMode && !_isWeighOutMode)
            {
                var weighedIn = gross ?? tare;
                return (Loc.T("weigh.already_in_title"),
                    Loc.T("weigh.already_in_msg", plate,
                        weighedIn?.MeasuredAt.ToVnTime().ToString("dd/MM/yyyy HH:mm") ?? "—"));
            }
        }
        else if (enforceMode && _isWeighOutMode)
        {
            return (Loc.T("weigh.not_in_yet_title"), Loc.T("weigh.not_in_yet_msg", plate));
        }

        return null;
    }

    /// <summary>Đọc đầu cân một lần; mất tín hiệu / thiết bị lỗi → hiện cảnh báo cho người trực cân.</summary>
    private async Task WarnIfScaleDisconnectedAsync()
    {
        try
        {
            // Giả lập chưa có số cân: không phải mất kết nối — nhắc nhập tay, không bật popup.
            // Bàn cân đã nối cổng COM thì đọc thật và cảnh báo khi mất tín hiệu.
            if (_simulatedScale is { TargetWeight: null } && _scaleRouter?.HasSerial(_currentWeighbridgeId) != true)
            {
                _lblQrMessage.Text = Loc.T("weigh.scale_no_data_hint");
                _lblQrMessage.ForeColor = AgrisTheme.Warning;
                return;
            }

            var view = await _presenter.ReadLiveWeightAsync(_currentWeighbridgeId, _cts.Token);
            if (view.DeviceStatus == ScaleDeviceStatus.Ok && view.SignalFresh) return;

            _lblQrMessage.Text = Loc.T("weigh.scale_disconnected_msg");
            _lblQrMessage.ForeColor = AgrisTheme.Danger;
            Helpers.AppMessageBox.Show(this, Loc.T("weigh.scale_disconnected_msg"), Loc.T("weigh.scale_disconnected_title"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Helpers.AppMessageBox.Show(this, Loc.T("weigh.scale_disconnected_msg") + "\n\n" + ex.Message,
                Loc.T("weigh.scale_disconnected_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// Xác định phân đoạn cân tiếp theo bằng cách kiểm tra preconditions.
    /// Nếu trip đã có lượt cân lần 1 hợp lệ (PrecedingWeightsSatisfied cho segment 1) → segment = 1.
    /// OpenWeighSessionHandler sẽ resolve chính xác vai trò từ profile.
    /// </summary>
    private async Task<int> DetermineSegmentIndexAsync(QrScanResult scan, CancellationToken ct)
    {
        if (scan.TripId is null) return 0;

        // Kiểm tra xem đã có lượt cân nào (effective) chưa.
        // Nếu đã có → đây là cân lần 2 (segment 1).
        var existingEvent = await _presenter.GetEffectiveWeighEventAsync(
            scan.TripId.Value, WeightRole.Gross, segmentIndex: 0, ct);

        if (existingEvent is not null) return 1;

        // Fallback: check Tare segment 0 (trường hợp profile mapping khác)
        var existingTare = await _presenter.GetEffectiveWeighEventAsync(
            scan.TripId.Value, WeightRole.Tare, segmentIndex: 0, ct);

        return existingTare is not null ? 1 : 0;
    }

    private void PopulateTripInfo(QrScanResult scan)
    {
        _lastScan = scan;
        _secondWeighKgForPolicy = null;
        _btnPolicyPreview.Enabled = false;
        _btnTripMore.Enabled = scan.TripId is not null;
        _txtRegNo.Text = scan.TripNo ?? "";
        _txtPlate.Text = scan.Plate ?? "";
        // Mía: chủ mía trống → khách hàng doanh nghiệp của lệnh đốn.
        _txtCustomer.Text = scan.CustomerOrOwner.NullIfEmpty()
            ?? (scan.FrmLenhDonId is not null || scan.FrmMaLenhDon is not null
                ? _presenter.FindLenhDonForTrip(scan.FrmLenhDonId, scan.Plate)?.OwnerName : null)
            ?? "";
        _txtCommodity.Text = _presenter.CommodityForTrip(scan.FrmLenhDonId, scan.FrmMaLenhDon ?? scan.ShipmentNo)
                             ?? scan.Commodity ?? "";
        _txtWarehouse.Text = scan.Warehouse ?? "";
        _txtShipment.Text = scan.ShipmentNo ?? "";
        _txtDriver.Text = scan.DriverRef ?? "";
        _txtTransporter.Clear();
        _txtDirection.Clear();
        _txtErpTripNumber.Text = scan.ShipmentNo ?? "";
        _currentSourceSystem = scan.SourceSystem;
        _display.DisplayAsTon = _currentSourceSystem != "ERP"; // Mía = tấn, Vật tư = kg
        UpdateWeightUnitLabels();
        ApplyFlowLayout();
        if (_currentSourceSystem == TripCategoryExtensions.MATERIAL_SOURCE_SYSTEM)
            _ = LoadMaterialDetailsAsync(scan.TripId, scan.ShipmentNo);
        _currentFrmLenhDonId = scan.FrmLenhDonId;
        _currentFrmMaLenhDon = scan.FrmMaLenhDon;
        _currentFrmLenhDonBu = scan.FrmLenhDonBu;

        // Chuyến mới → bỏ chính sách / kết quả đẩy của chuyến trước.
        if (scan.TripId != _currentTripId)
        {
            _policyTapChatCS = null;
            _policyCcsChinhSach = null;
            _policyChinhSachThuMia = null;
            _prePushedSessionId = null;
            _prePushedResult = null;
            _prePushedKg = null;
            ShowPaymentFigures(null, null, null, null);
        }

        // Hiện panel loại mía chỉ ở cân lần 1 (segment 0). Cân ra không cần chọn lại.
        var isSugarcane = _currentSourceSystem == "FRM"
            || scan.Commodity?.Contains("CANE", StringComparison.OrdinalIgnoreCase) == true
            || scan.Commodity?.Contains("mía", StringComparison.OrdinalIgnoreCase) == true
            || string.IsNullOrWhiteSpace(scan.Commodity);
        // Loại mía lấy từ FRM (LoaiMiaName) khi đăng tài — không cần chọn tại trạm cân.
        // _pnlSugarcaneType.Visible = isSugarcane && _currentSegmentIndex == 0;
    }

    /// <summary>
    /// Load dữ liệu cân lần 1 (Gross) lên form khi chuẩn bị cân lần 2.
    /// Lấy WeighEvent hiệu lực cho segment 0 từ server.
    /// </summary>
    private async Task LoadFirstWeighDataAsync(Guid tripId)
    {
        try
        {
            // Tìm WeighEvent hiệu lực segment 0 — role tùy theo profile (Gross cho mía, Tare cho vật tư xuất)
            var grossEvent = await _presenter.GetEffectiveWeighEventAsync(
                tripId, WeightRole.Gross, segmentIndex: 0, _cts.Token);

            // Fallback: nếu không có Gross ở segment 0, thử Tare (profile xuất vật tư)
            grossEvent ??= await _presenter.GetEffectiveWeighEventAsync(
                tripId, WeightRole.Tare, segmentIndex: 0, _cts.Token);

            if (grossEvent is not null)
            {
                _firstWeighKg = grossEvent.NormalizedKg;
                _txtWeightIn.Text = FormatWeight(grossEvent.NormalizedKg);
                _txtTimeIn.Text = grossEvent.MeasuredAt.ToVnTime().ToString("dd/MM/yyyy HH:mm:ss");
            }
        }
        catch
        {
            // Lỗi load dữ liệu cân cũ không chặn cân lần 2
        }
    }

    /// <summary>
    /// Cập nhật thông tin chuyến từ FRM sync khi cân lần 2 (Req 4.1).
    /// Ghi đè SugarcaneType, SugarcaneVariety, SugarcaneOwner nếu FRM trả giá trị mới.
    /// </summary>
    private void UpdateTripInfoFromFrm(FrmSyncResult frm)
    {
        // Hàng hóa mía = Mía cháy / khúc / cây theo lệnh đốn; chỉ dùng loại mía FRM khi lệnh đốn chưa có cờ.
        var caneLabel = _presenter.CommodityForTrip(_currentFrmLenhDonId, _currentFrmMaLenhDon);
        if (caneLabel is not null)
            _txtCommodity.Text = caneLabel;
        else if (!string.IsNullOrEmpty(frm.SugarcaneType))
            _txtCommodity.Text = frm.SugarcaneType;

        if (!string.IsNullOrEmpty(frm.SugarcaneOwner))
            _txtCustomer.Text = frm.SugarcaneOwner;
    }

    // ═══════════════════════════════════════════════════════════
    //  Signal & condition polling (unchanged logic)
    // ═══════════════════════════════════════════════════════════

    private async void OnSignalTick(object? sender, EventArgs e)
    {
        if (_sessionId is null || _isBusy || _currentTripId is null || _signalPollInFlight) return;

        _signalPollInFlight = true;
        try
        {
            var view = await _presenter.ReadLiveWeightAsync(_currentWeighbridgeId, _cts.Token);
            _display.Update(view.NormalizedKg, view.Uom, view.DeviceStatus,
                view.SignalFresh, view.StableDuration, _requiredStabilityWindow);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StopTimers();
            ShowError(Loc.T("weigh.signal_error"), ex);
        }
        finally
        {
            _signalPollInFlight = false;
        }
    }

    private async void OnConditionTick(object? sender, EventArgs e)
    {
        if (_sessionId is null || _isBusy || _currentTripId is null || _conditionCheckInFlight) return;

        _conditionCheckInFlight = true;
        try
        {
            var result = await _presenter.CommitAsync(BuildCommand(dryRun: true), _cts.Token);
            // Dry run: cập nhật checklist điều kiện chốt cân từ kết quả server (Req 1.2, 1.7)
            _checklist.UpdateFromDecision(result.Decision);
        }
        catch (OperationCanceledException) { }
        catch { /* Lỗi tạm thời khi đánh giá thử */ }
        finally { _conditionCheckInFlight = false; }
    }

    /// <summary>
    /// Poll trạng thái thiết bị mỗi 2 giây (Req 13.1, 13.4).
    /// Cập nhật DeviceStatusBar hiển thị trạng thái kết nối barcode, đồng hồ cân, camera.
    /// Hiển thị lỗi cụ thể (Fault, Overload, Underload) trên thanh trạng thái.
    /// </summary>
    private async void OnDeviceStatusTick(object? sender, EventArgs e)
    {
        if (_devicePollInFlight) return;

        _devicePollInFlight = true;
        try
        {
            var siteId = _siteContext.CurrentSiteId;
            Guid? laneId = _currentWeighbridgeId != Guid.Empty
                ? _currentWeighbridgeId
                : null;

            // Chạy song song device status và camera AI để giảm thời gian chờ mỗi tick
            var deviceTask = _presenter.GetDeviceStatusAsync(siteId, laneId, _cts.Token);
            var cameraTask = PollCameraAiAsync();

            var devices = await deviceTask;
            _deviceStatusBar.UpdateStatus(devices);

            await cameraTask;
        }
        catch (OperationCanceledException) { }
        catch
        {
            // Lỗi tạm thời khi poll trạng thái thiết bị — không crash form.
            // DeviceStatusBar giữ nguyên trạng thái hiển thị trước đó.
        }
        finally
        {
            _devicePollInFlight = false;
        }
    }

    /// <summary>Poll AI camera và cập nhật CameraAiStatusControl.</summary>
    private async Task PollCameraAiAsync()
    {
        try
        {
            var result = await _cameraAiService.AnalyzeAsync(
                _currentWeighbridgeId, _cts.Token);
            _cameraAiStatus.UpdateResult(result);
        }
        catch (OperationCanceledException) { }
        catch
        {
            _cameraAiStatus.UpdateResult(null);
        }
    }

    /// <summary>Poll AI camera đồng bộ (gọi từ nút giả lập).</summary>
    private async void PollCameraAi()
    {
        await PollCameraAiAsync();
    }

    // ═══════════════════════════════════════════════════════════
    //  Event handlers
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Cân vào mía: xe nặng hơn ngưỡng (SugarcaneFlow:WeighInWarnTon, mặc định 30 tấn) → chỉ CẢNH BÁO,
    /// bấm "Đã hiểu" rồi vẫn cân tiếp (không chặn).
    /// </summary>
    private void WarnIfHeavyWeighIn(decimal kg)
    {
        var ton = (Program.AppConfiguration is { } cfg
            ? Microsoft.Extensions.Configuration.ConfigurationBinder.GetValue<decimal?>(cfg, "SugarcaneFlow:WeighInWarnTon")
            : null) ?? 30m;
        if (ton <= 0 || kg <= ton * 1000m) return;

        var title = Loc.T("weigh.heavy_weighin_title", ton.ToString("N0"));
        _lblQrMessage.Text = "⚠ " + title;
        _lblQrMessage.ForeColor = AgrisTheme.Warning;
        Helpers.DigiScalePopup.ShowAlert(this, Helpers.AlertKind.Warning, title,
            Loc.T("weigh.heavy_weighin_reason", (kg / 1000m).ToString("N3"), ton.ToString("N0")),
            Loc.T("weigh.heavy_weighin_action"),
            [(Loc.T("alert.fact_trip"), _txtRegNo.Text), (Loc.T("alert.fact_plate"), _txtPlate.Text),
             (Loc.T("alert.fact_lenhdon"), _txtShipment.Text.NullIfEmpty()),
             (Loc.T("alert.fact_weight1"), $"{kg:N3} kg")]);
    }

    /// <summary>Hệ thống đích hiển thị trên thông báo: mía → FRM, vật tư → ERP.</summary>
    private string TargetSystem => _currentSourceSystem == TripCategoryExtensions.MATERIAL_SOURCE_SYSTEM
        ? Loc.SYSTEM_ERP : Loc.SYSTEM_FRM;

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        using var systemScope = Loc.UseSystem(TargetSystem);
        if (_sessionId is null || _currentTripId is null) return;

        // Loại mía đã lưu từ FRM khi đăng tài — bỏ validation chọn thủ công.

        SetBusy(true);
        try
        {
            // ── Mía (FRM, QR chính thức): ĐẨY DIGISCALE TRƯỚC, không lỗi mới lưu cân.
            //    Lần 1: DigiScale phải nhận phiếu. Lần 2: phải nhận phiếu VÀ trả chính sách.
            var isWeighOut = _currentSegmentIndex > 0;
            var requiresPushFirst = _currentSourceSystem != "ERP" && !_currentQrIsTemp;
            TTC.Weighing.Infrastructure.Adapters.TicketResult? prePushed = null;
            var prePushedMaLenhDon = "—";

            if (requiresPushFirst)
            {
                if (_prePushedSessionId == _sessionId && _prePushedResult is not null)
                {
                    // DigiScale đã nhận phiếu cho phiên này ở lần bấm trước (lưu local lúc đó bị chặn) — không đẩy lại.
                    prePushed = _prePushedResult;
                }
                else
                {
                    var dry = await _presenter.CommitAsync(BuildCommand(dryRun: true), _cts.Token);
                    _checklist.UpdateFromDecision(dry.Decision);

                    if (!dry.Decision.CanCommit || dry.NormalizedKg is not { } kgToPush)
                    {
                        Helpers.AppMessageBox.Show(
                            Loc.T("weigh.commit_blocked", dry.BlockReason),
                            Loc.T("weigh.commit_blocked_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    if (!isWeighOut) WarnIfHeavyWeighIn(kgToPush);

                    var (gateResult, gateMaLenhDon) = await PushSugarcaneWeighAsync(kgToPush, isWeighOut);

                    if (!gateResult.IsSuccess || (isWeighOut && !gateResult.HasPolicy))
                    {
                        ShowPushGateFailure(gateResult, gateMaLenhDon, kgToPush, isWeighOut);
                        return; // KHÔNG lưu cân
                    }

                    prePushed = gateResult;
                    prePushedMaLenhDon = gateMaLenhDon;
                    _prePushedSessionId = _sessionId;
                    _prePushedResult = gateResult;
                    _prePushedKg = kgToPush;
                }
            }
            else if (_currentSourceSystem != "ERP" && !isWeighOut)
            {
                // Mía QR tạm (không đẩy DigiScale trước): vẫn cảnh báo xe nặng khi cân vào.
                var dry = await _presenter.CommitAsync(BuildCommand(dryRun: true), _cts.Token);
                if (dry.Decision.CanCommit && dry.NormalizedKg is { } kgIn) WarnIfHeavyWeighIn(kgIn);
            }

            // Cân lần 2 mía: chính sách FRM đã về → hiện CCS / tạp chất / TL thanh toán, người cân xác nhận rồi mới lưu.
            if (isWeighOut && prePushed is { HasPolicy: true } policy)
            {
                var sentKg = _prePushedKg ?? 0m;
                var paidKg = PaymentWeightKg(_firstWeighKg, sentKg, policy.TapChatCS);
                ShowPaymentFigures(policy.ChinhSachThuMia, policy.TapChatCS, policy.CcsChinhSach, paidKg);

                if (!ConfirmPaymentBeforeSave(policy, sentKg, paidKg))
                {
                    _lblQrMessage.Text = Loc.T("weigh.policy_confirm_cancelled");
                    _lblQrMessage.ForeColor = AgrisTheme.Warning;
                    return; // chưa lưu — bấm F5 lại sẽ dùng lại chính sách đã lấy, không đẩy trùng
                }
            }

            var result = await _presenter.CommitAsync(BuildCommand(dryRun: false), _cts.Token);

            if (!result.IsCommitted && prePushed is not null)
            {
                // Hiếm: DigiScale đã nhận nhưng tín hiệu cân đổi ngay trước khi lưu local.
                _checklist.UpdateFromDecision(result.Decision);
                Helpers.AppMessageBox.Show(
                    Loc.T("weigh.prepushed_commit_blocked", result.BlockReason, isWeighOut ? "2" : "1"),
                    Loc.T("weigh.commit_blocked_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!result.IsCommitted)
            {
                // Cập nhật checklist với kết quả chốt cân thất bại để hiển thị điều kiện chưa đạt (Req 1.7)
                _checklist.UpdateFromDecision(result.Decision);

                Helpers.AppMessageBox.Show(
                    Loc.T("weigh.commit_blocked", result.BlockReason),
                    Loc.T("weigh.commit_blocked_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            StopTimers();
            _btnSave.Enabled = false;

            // Giả lập: đã chốt lượt này → xóa số cân để lượt sau phải nhập tay lại.
            _simulatedScale?.Clear();
            _display.Update(null, "kg", ScaleDeviceStatus.Unknown, false, TimeSpan.Zero, null);

            // Cập nhật checklist trạng thái cuối cùng (tất cả đạt)
            _checklist.UpdateFromDecision(result.Decision);

            // Cập nhật thông tin cân trên form (hiển thị tấn)
            if (_currentSegmentIndex == 0)
            {
                _firstWeighKg = result.NormalizedKg;
                _txtWeightIn.Text = FormatWeight(result.NormalizedKg ?? 0);
                _txtTimeIn.Text = VnTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
            }
            else
            {
                _txtWeightOut.Text = FormatWeight(result.NormalizedKg ?? 0);
                _txtTimeOut.Text = VnTime.Now.ToString("dd/MM/yyyy HH:mm:ss");

                // Hiển thị TL hàng nếu có cả cân vào và cân ra (Req 4.4)
                // Net do server tính theo hướng profile. UI chỉ hiển thị chênh lệch tham khảo.
                if (_firstWeighKg is { } firstKg && result.NormalizedKg is { } secondKg)
                {
                    // Chênh lệch tham khảo giữa 2 lượt cân (lớn trừ nhỏ — vật tư xuất cân bì trước).
                    var netKg = firstKg >= secondKg ? firstKg - secondKg : secondKg - firstKg;
                    _txtNetWeight.Text = FormatWeight(netKg);
                    _txtNetWeight.Font = new Font(_txtNetWeight.Font, FontStyle.Bold);
                }

                // Bật nút In phiếu (F6) và nút Sync ERP sau cân lần 2 thành công (Req 4.7)
                _btnPrint.Enabled = true;
                _btnSync.Enabled = true;
            }

            _sessionId = null;

            // Hiển thị thông báo
            var lifecycleNote = _currentSegmentIndex > 0
                ? Loc.T("weigh.committed_note_done")
                : Loc.T("weigh.committed_note_next");
            _lblQrMessage.Text = Loc.T("weigh.committed", CheckpointLabel(_currentCheckpoint), $"{FormatWeight(result.NormalizedKg ?? 0)} {WeightUnitLabel}") + lifecycleNote;
            _lblQrMessage.ForeColor = AgrisTheme.Success;

            // Sau cân lần 1: mở lại barcode cho chuyến tiếp
            if (_currentSegmentIndex == 0)
            {
                // Loại mía đã có từ FRM (LoaiMiaName) khi đăng tài — không cần lưu từ form.

                _txtBarcode.Enabled = true;
                _txtBarcode.Clear();
                _txtBarcode.Focus();
            }

            // Refresh lưới xe đã cân sau mỗi lần chốt cân thành công (Req 1.2)
            await LoadWeighedTripsAsync();

            // Lưu QR content trước khi clear
            var qrForNextStep = _lastQrContent;

            // Mía: popup DigiScale bên dưới đã báo số cân + kết quả đẩy → bỏ hộp thoại "Hoàn tất" trùng.
            // Vật tư cân lần 2 vẫn báo lưu cân trước (popup oracle-tickets hiện riêng / có thể không có đơn ERP).
            if (_currentSegmentIndex > 0 && _currentSourceSystem == "ERP")
            {
                Helpers.AppMessageBox.Show(
                    Loc.T("weigh.committed", CheckpointLabel(_currentCheckpoint), $"{FormatWeight(result.NormalizedKg ?? 0)} {WeightUnitLabel}"),
                    Loc.T("weigh.committed_title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            // Sau cân lần 1: mở lại barcode sau khi user đóng dialog
            if (_currentSegmentIndex == 0)
            {
                _txtBarcode.Enabled = true;
                _txtBarcode.Clear();

                // Sau cân lần 1: mía → tab "Đo tạp chất" (chờ đo); vật tư → "Chờ cân ra".
                if (_btnTabImpurity.Visible) _btnTabImpurity.PerformClick();
                else _btnTabWeighIn.PerformClick();
            }

            // Mã phiếu cân (PC + niên vụ + số): sinh ở lượt cân đầu tiên, cân ra dùng lại đúng mã đó (mía + vật tư).
            if (_currentTripId is { } ticketTripId)
            {
                try { _txtTicketNo.Text = await _presenter.EnsureTicketNumberAsync(ticketTripId, _cts.Token) ?? _txtTicketNo.Text; }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _lblQrMessage.Text = "⚠ " + Loc.T("weigh.ticket_no_failed", ex.Message);
                    _lblQrMessage.ForeColor = AgrisTheme.Warning;
                }
            }

            // Refresh lưới — KHÔNG publish event bus (F5 chỉ lưu, F7 mới nhảy tab)
            await LoadWeighedTripsAsync();

            // Push phiếu cân lên DigiScale — CẢ lần 1 (cân vào) và lần 2 (cân ra)
            {
                var isCânRa = _currentSegmentIndex > 0;
                try
                {
                    // Push FRM phiếu cân (mía) — lệnh đốn/số phiếu lấy từ DB theo chuyến (PushSugarcaneWeighAsync)
                    if (_currentSourceSystem != "ERP")
                    {
                        // Mía QR chính thức đã đẩy DigiScale TRƯỚC khi lưu → dùng lại kết quả, không đẩy lần nữa.
                        var maLenhDon = prePushedMaLenhDon;
                        var pushResult = prePushed;
                        if (pushResult is null)
                        {
                            (pushResult, maLenhDon) = await PushSugarcaneWeighAsync(result.NormalizedKg ?? 0, isCânRa);
                        }

                        if (prePushed is not null)
                        {
                            _prePushedSessionId = null;
                            _prePushedResult = null;

                            // Số chốt local khác số đã gửi DigiScale (lệch ở mức hiển thị tấn) → nhắc đẩy lại.
                            if (_prePushedKg is { } sentKg && result.NormalizedKg is { } savedKg
                                && Math.Round(sentKg / 1000m, 2) != Math.Round(savedKg / 1000m, 2))
                            {
                                Helpers.AppMessageBox.Show(
                                    Loc.T("weigh.prepushed_weight_mismatch",
                                        FormatWeight(sentKg), FormatWeight(savedKg), WeightUnitLabel),
                                    Loc.T("common.warning"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            }
                            _prePushedKg = null;
                        }

                        // Lưu chính sách vào state (dùng cho phiếu in)
                        if (pushResult.HasPolicy)
                        {
                            _policyTapChatCS = pushResult.TapChatCS;
                            _policyCcsChinhSach = pushResult.CcsChinhSach;
                            _policyChinhSachThuMia = pushResult.ChinhSachThuMia;
                        }
                        else if (isCânRa && pushResult.IsNoPolicy)
                        {
                            _policyChinhSachThuMia = Loc.T("weigh.policy_none_short");
                        }

                        // Cân ra có chính sách → đẩy tiếp đủ số liệu thanh toán TRƯỚC, rồi báo MỘT lần (kết quả cả hai bước).
                        (bool Ok, string Detail)? payment = null;
                        if (isCânRa && pushResult.IsSuccess && pushResult.HasPolicy && _currentTripId is { } paidTripId)
                            payment = await PushPaymentFiguresAsync(paidTripId, pushResult);

                        // Cột "Đồng bộ" trên lưới: ghi kết quả đẩy DigiScale của lượt cân này.
                        var synced = pushResult.IsSuccess && (!isCânRa || pushResult.HasPolicy) && payment is not { Ok: false };
                        if (_currentTripId is { } syncTripId)
                            try { await _presenter.RecordDigiScaleSyncAsync(syncTripId, isCânRa ? "WEIGH_OUT" : "WEIGH_IN", synced); }
                            catch { /* chỉ là trạng thái hiển thị */ }

                        ShowWeighResult(pushResult, maLenhDon, result.NormalizedKg ?? 0, isCânRa, payment);
                        await LoadWeighedTripsAsync();

                        // Hoàn thành (cân ra) + đã đẩy DigiScale → hiện phiếu cân để in.
                        if (isCânRa && synced) await ShowTicketAfterCompletionAsync();
                    }
                    else
                    {
                        // ERP/Vật tư — chỉ push oracle-tickets khi cân lần 2 (cân ra)
                        if (isCânRa)
                        {
                            try
                            {
                                // Vật tư: _txtWeightIn hiện kg, lấy trực tiếp
                                var weightInKg = decimal.TryParse(_txtWeightIn.Text.Replace(",", ""), out var wiVal) ? wiVal : 0m;
                                var weightOutKg = result.NormalizedKg ?? 0;
                                var netKg = Math.Abs(weightInKg - weightOutKg);
                                var bu = _presenter.DefaultBusinessUnit;
                                var shipmentForErp = _txtShipment.Text.Trim();

                                TTC.Weighing.Infrastructure.Adapters.ErpOrderLine? erpLine = null;
                                try { erpLine = await _presenter.LookupErpOrderAsync(bu, shipmentForErp); }
                                catch { }

                                if (erpLine is not null)
                                {
                                    // WMS cũng là luồng SO → CONFIRM; ưu tiên actionCodeRequest ERP trả về.
                                    var txnTypeName = erpLine.EffectiveTransactionTypeName;
                                    var txnTypeCode = erpLine.EffectiveTransactionTypeCode;
                                    var actionCode = erpLine.EffectiveActionCode;
                                    // Kho: lưu trên chuyến lúc đăng tài → ERP order-line; không có → null.
                                    var warehouseName = await _presenter.ResolveWarehouseAsync(_currentTripId, erpLine, _cts.Token);
                                    var orgCode = WeighOperationPresenter.WarehouseOrganizationCode(erpLine.OrganizationCode, warehouseName) ?? "";
                                    var erpBu = erpLine.BusinessUnit ?? bu;

                                    var erpPushResult = await _presenter.PushOracleTicketAsync(
                                        txnTypeName, txnTypeCode, actionCode,
                                        shipmentForErp, shipmentForErp,
                                        weightInKg, weightOutKg, netKg,
                                        (await _presenter.EnsureTicketNumberAsync(_currentTripId!.Value, _cts.Token)) ?? _txtRegNo.Text.Trim(), orgCode, erpBu,
                                        _txtRegNo.Text.Trim(),
                                        erpLine.ItemNumber, erpLine.ItemDescription ?? erpLine.ItemName, netKg,
                                        await _presenter.ResolveTransporterAsync(_currentTripId, erpLine, _cts.Token),
                                        _txtPlate.Text.Trim(),
                                        _txtDriver.Text.Trim().Length > 0 ? _txtDriver.Text.Trim() : erpLine.DriverName,
                                        erpLine.TruckOwner,
                                        erpLine.VendorName, erpLine.VendorNumber, erpLine.VendorSiteCode,
                                        _cts.Token, warehouseName, note: WeighNote);

                                    if (erpPushResult.IsSuccess)
                                    {
                                        _lblQrMessage.Text = "✅ Đã đẩy lên ERP";
                                        _lblQrMessage.ForeColor = AgrisTheme.Success;

                                        if (_currentTripId.HasValue)
                                            try { await _presenter.RecordErpSyncAsync(_currentTripId.Value, "WEIGH_OUT", null); } catch { }

                                        DigiScalePopup.Show(this,
                                            "CÂN LẦN 2 VẬT TƯ — ĐÃ ĐẨY LÊN ERP",
                                            $"Số đăng tài: {_txtRegNo.Text}\nBiển số: {_txtPlate.Text}\nShipment: {shipmentForErp}\nTrọng lượng: {FormatWeight(result.NormalizedKg ?? 0)} {WeightUnitLabel}",
                                            true, "OK");

                                        // Hoàn thành + đã đẩy DigiScale → hiện phiếu cân vật tư để in.
                                        await ShowTicketAfterCompletionAsync();
                                    }
                                    else
                                    {
                                        _lblQrMessage.Text = "⚠ ERP: " + erpPushResult.ShortText();
                                        _lblQrMessage.ForeColor = AgrisTheme.Warning;

                                        DigiScalePopup.Show(this,
                                            "CÂN LẦN 2 VẬT TƯ — LỖI ĐẨY LÊN ERP",
                                            $"Số đăng tài: {_txtRegNo.Text}\nBiển số: {_txtPlate.Text}",
                                            false, erpPushResult.ErrorMessage ?? "");
                                    }
                                }
                                else
                                {
                                    _lblQrMessage.Text = "⚠ Không tìm thấy ERP order. Lưu local.";
                                    _lblQrMessage.ForeColor = AgrisTheme.Warning;
                                }
                            }
                            catch (Exception erpEx)
                            {
                                _lblQrMessage.Text = "⚠ ERP: " + DigiScaleErrors.Describe(erpEx).Reason;
                                _lblQrMessage.ForeColor = AgrisTheme.Warning;
                            }
                        }
                        // Cân lần 1 vật tư: không push, chỉ lưu local — hiện popup thông báo
                        if (!isCânRa)
                        {
                            DigiScalePopup.Show(this,
                                "CÂN LẦN 1 VẬT TƯ THÀNH CÔNG",
                                $"Số đăng tài: {_txtRegNo.Text}\nBiển số: {_txtPlate.Text}\nTrọng lượng: {FormatWeight(result.NormalizedKg ?? 0)} {WeightUnitLabel}\n\nĐẩy lên ERP khi cân lần 2.",
                                true, "OK");
                        }
                    }
                }
                catch (Exception pushEx)
                {
                    _lblQrMessage.Text = "⚠ " + DigiScaleErrors.Describe(pushEx).Reason;
                    _lblQrMessage.ForeColor = AgrisTheme.Warning;
                }

                // Oracle-tickets push đã xử lý trong block trên cho cả lần 1 và lần 2
            }

            // Cân lần 1 xong → tự chuyển màn hình sang CÂN RA cho chính chuyến này.
            // Hẹn sau khi handler này kết thúc (finally → SetBusy(false)) vì HandleQrScanAsync bỏ qua khi đang bận.
            if (_currentSegmentIndex == 0 && _currentTripId is { } committedTripId
                && !string.IsNullOrWhiteSpace(qrForNextStep))
            {
                BeginInvoke(new Action(async () => await ContinueToWeighOutAsync(qrForNextStep, committedTripId)));
            }
        }
        catch (Exception ex)
        {
            ShowError(Loc.T("weigh.commit_error"), ex);
        }
        finally { SetBusy(false); }
    }

    /// <summary>
    /// Đẩy phiếu cân mía lên DigiScale (new_phieucan). Cân lần 2 gửi IsQuetCS = Yes để lấy chính sách.
    /// Trả kèm mã lệnh đốn để hiển thị thông báo.
    /// </summary>
    private async Task<(TTC.Weighing.Infrastructure.Adapters.TicketResult Result, string MaLenhDon)> PushSugarcaneWeighAsync(
        decimal normalizedKg, bool isCânRa)
        => _currentTripId is { } tripId
            ? await PushSugarcaneWeighAsync(tripId, isCânRa, normalizedKg, VnTime.NowOffset)
            : (new TTC.Weighing.Infrastructure.Adapters.TicketResult(false, Loc.T("weigh.digiscale_no_trip")), "—");

    /// <summary>
    /// Đẩy một lượt cân mía của chuyến lên DigiScale. Lệnh đốn, số phiếu lấy TỪ DB theo chuyến
    /// (không lấy từ form). Lần 2 gửi IsQuetCS = Yes. Nếu DigiScale báo chưa có lần cân 1 (CR-04)
    /// thì tự đẩy bù lần 1 từ số đã lưu rồi gửi lại lần 2 một lần.
    /// </summary>
    private async Task<(TTC.Weighing.Infrastructure.Adapters.TicketResult Result, string MaLenhDon)> PushSugarcaneWeighAsync(
        Guid tripId, bool isCânRa, decimal normalizedKg, DateTimeOffset measuredAt)
    {
        DigiScaleWeighContext? ctx;
        try { ctx = await _presenter.GetDigiScaleContextAsync(tripId, _cts.Token, ensureTicket: true); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (new TTC.Weighing.Infrastructure.Adapters.TicketResult(false, Loc.T("weigh.digiscale_ctx_error", ex.Message)), "—");
        }

        if (ctx is null)
            return (new TTC.Weighing.Infrastructure.Adapters.TicketResult(false, Loc.T("weigh.digiscale_ctx_error", "—")), "—");

        if (!ctx.HasLenhDon)
            return (new TTC.Weighing.Infrastructure.Adapters.TicketResult(false, Loc.T("weigh.digiscale_no_lenhdon", ctx.TripNo)), ctx.TripNo);

        string? maBanCan = null;
        if (_currentWeighbridgeId != Guid.Empty)
        {
            try { maBanCan = await _presenter.GetWeighbridgeCodeAsync(_currentWeighbridgeId, _cts.Token); }
            catch (Exception ex) when (ex is not OperationCanceledException) { /* mã bàn cân không bắt buộc */ }
        }

        var result = await SendPhieuCanAsync(ctx, isCânRa, normalizedKg, measuredAt, maBanCan);

        // DigiScale chưa có lần cân 1 của phiếu này → đẩy bù lần 1 (số + giờ đã lưu) rồi gửi lại lần 2.
        if (isCânRa && !result.IsSuccess && IsNotWeighedIn(result)
            && ctx.FirstKg is { } firstKg && ctx.FirstAt is { } firstAt)
        {
            var first = await SendPhieuCanAsync(ctx, false, firstKg, firstAt, maBanCan);
            if (!first.IsSuccess)
                return (first with { ErrorMessage = Loc.T("weigh.digiscale_first_backfill_failed", first.ErrorMessage ?? "—") }, ctx.MaLenhDon!);

            result = await SendPhieuCanAsync(ctx, true, normalizedKg, measuredAt, maBanCan);
        }

        return (result, ctx.MaLenhDon!);
    }

    /// <summary>
    /// Sau khi cân lần 2 đã có chính sách: gửi lại phiếu cân lần 2 kèm ĐỦ số liệu thanh toán
    /// (TL thanh toán, tạp chất thanh toán, CCS thanh toán, tạp chất/CCS chính sách) — lần đầu gửi để lấy
    /// chính sách các trường này còn = 0. Cùng số phiếu + lần cân nên DigiScale cập nhật phiếu đó.
    /// TL thanh toán = (TL xe vào − TL xe ra) × (1 − tạp chất chính sách %), cùng công thức phiếu in.
    /// Số cân lấy từ DB. TL hàng ≤ 0 thì không gửi (không dùng trị tuyệt đối che số âm).
    /// </summary>
    private async Task<(bool Ok, string Detail)> PushPaymentFiguresAsync(
        Guid tripId, TTC.Weighing.Infrastructure.Adapters.TicketResult policy)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        DigiScaleWeighContext? ctx;
        try { ctx = await _presenter.GetDigiScaleContextAsync(tripId, _cts.Token, ensureTicket: true); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return (false, ex.Message); }

        if (ctx is not { HasLenhDon: true, FirstKg: { } firstKg, SecondKg: { } secondKg, SecondAt: { } secondAt })
            return (false, Loc.T("weigh.retry_no_weigh"));

        var netKg = firstKg - secondKg;
        if (netKg <= 0)
            return (false, Loc.T("weigh.final_push_net_invalid", FormatWeight(netKg)));

        // Mọi số gửi đi / dùng tính: làm tròn 2 chữ số thập phân (Num).
        var tcPolicy = Num.R2(policy.TapChatCS ?? 0m);
        var tlThanhToanTon = Num.R2(Num.R2(netKg / 1000m) * (1m - tcPolicy / 100m));
        var ccsText = Num.S2(policy.CcsChinhSach, "0");

        string? maBanCan = null;
        if (_currentWeighbridgeId != Guid.Empty)
        {
            try { maBanCan = await _presenter.GetWeighbridgeCodeAsync(_currentWeighbridgeId, _cts.Token); }
            catch (Exception ex) when (ex is not OperationCanceledException) { }
        }

        TTC.Weighing.Infrastructure.Adapters.TicketResult sent;
        try
        {
            sent = await _presenter.PushPhieuCanAsync(
                "100000001", "2",
                Program.LoggedInUserName, Program.LoggedInUserAccount,
                ctx.LenhDonId!, ctx.MaLenhDon!,
                Num.S2(secondKg / 1000m),
                secondAt.ToVnTime().DateTime,
                ctx.SoPhieuCan, maBanCan,
                Num.S2(tlThanhToanTon),
                Num.S2(tcPolicy),
                ccsText,
                ctx.LenhDonBu, _cts.Token,
                isQuetCS: false,
                tapChatCs: Num.S2(tcPolicy),
                ccsChinhSach: ccsText,
                ghiChu: WeighNote);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (false, ex.Message);
        }

        var detail = Loc.T("weigh.final_push_detail",
            tlThanhToanTon.ToString("N3"), tcPolicy.ToString("N2"),
            policy.CcsChinhSach?.ToString("N2") ?? Loc.T("weigh.ccs_not_available"));
        return sent.IsSuccess ? (true, detail) : (false, sent.ErrorMessage ?? detail);
    }

    /// <summary>
    /// TL thanh toán (kg) = (TL cân vào − TL cân ra) × (1 − tạp chất chính sách %), cùng công thức số đẩy DigiScale
    /// và phiếu in. Null nếu chưa có lần 1 hoặc TL hàng ≤ 0 (không dùng trị tuyệt đối che số âm).
    /// </summary>
    private static decimal? PaymentWeightKg(decimal? firstWeighKg, decimal secondKg, decimal? tapChatPolicy)
    {
        if (firstWeighKg is not { } firstKg) return null;
        var netKg = firstKg - secondKg;
        if (netKg <= 0) return null;
        // Cùng cách tính với số gửi FRM: tấn làm tròn 2 → × (1 − tạp chất % làm tròn 2) → làm tròn 2 → về kg.
        var tc = Num.R2(tapChatPolicy ?? 0m);
        return Num.R2(Num.R2(netKg / 1000m) * (1m - tc / 100m)) * 1000m;
    }

    /// <summary>
    /// Hiện chính sách thu mua của chuyến lên panel cân:
    /// 1. Đã lưu (bắt trước đó / chuyến đã cân xong) → hiện luôn.
    /// 2. Chưa có và <paramref name="allowPreview"/> → gọi FRM xem trước (IsQuetCS = Yes) với số cân hiện tại.
    /// TL thanh toán tính theo số cân hiện tại; khi bấm Xác nhận app bắt lại chính sách với số chốt.
    /// </summary>
    private async Task LoadOrPreviewPolicyAsync(bool allowPreview, bool forceRefresh = false)
    {
        if (_currentTripId is not { } tripId || string.IsNullOrWhiteSpace(_txtRegNo.Text)) return;
        try
        {
            if (!forceRefresh && await _presenter.GetStoredPolicyForTripAsync(tripId, _txtRegNo.Text.Trim(), _cts.Token) is { } stored)
            {
                ApplyPolicy(stored.ChinhSachThuMua, stored.TapChatCS, stored.CcsChinhSach,
                    _secondWeighKgForPolicy ?? (stored.WeightTon is { } t ? t * 1000m : null));
                return;
            }
            if (!allowPreview || !_presenter.CanPreviewPolicy) return;

            var live = await _presenter.ReadLiveWeightAsync(_currentWeighbridgeId, _cts.Token);
            if (live.NormalizedKg is not { } kg || kg <= 0 || !live.SignalFresh)
            {
                _txtPolicyName.Text = Loc.T("weigh.policy_preview_need_weight");
                return;
            }

            _txtPolicyName.Text = Loc.T("weigh.policy_preview_loading");
            string? maBanCan = null;
            try { maBanCan = await _presenter.GetWeighbridgeCodeAsync(_currentWeighbridgeId, _cts.Token); } catch { }
            var preview = await _presenter.PreviewPolicyAsync(tripId, kg, maBanCan,
                Program.LoggedInUserAccount, Program.LoggedInUserName, _cts.Token);

            if (preview is { HasPolicy: true })
            {
                ApplyPolicy(preview.ChinhSachThuMua, preview.TapChatCS, preview.CcsChinhSach, kg);
                _lblQrMessage.Text = Loc.T("weigh.policy_preview_ok");
                _lblQrMessage.ForeColor = AgrisTheme.Success;
            }
            else
            {
                ShowPaymentFigures(null, null, null, null);
                _txtPolicyName.Text = Loc.T("weigh.policy_preview_none");
                var reason = DigiScaleErrors.Describe(preview?.Error ?? Loc.T("weigh.digiscale_no_lenhdon", _txtRegNo.Text)).Reason;
                _lblQrMessage.Text = Loc.T("weigh.policy_preview_failed", reason);
                _lblQrMessage.ForeColor = AgrisTheme.Warning;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _txtPolicyName.Text = Loc.T("weigh.policy_preview_none");
            _lblQrMessage.Text = Loc.T("weigh.policy_preview_failed", ex.Message);
            _lblQrMessage.ForeColor = AgrisTheme.Warning;
        }
    }

    /// <summary>Số cân lần 2 dùng tính TL thanh toán khi xem lại chuyến đã cân xong.</summary>
    private decimal? _secondWeighKgForPolicy;

    private void ApplyPolicy(string? name, decimal? tapChat, decimal? ccs, decimal? secondKg)
    {
        _policyTapChatCS = tapChat;
        _policyCcsChinhSach = ccs;
        _policyChinhSachThuMia = name;
        ShowPaymentFigures(name, tapChat, ccs,
            secondKg is { } kg ? PaymentWeightKg(_firstWeighKg, kg, tapChat) : null);
    }

    /// <summary>Tên lượt cân cho người vận hành (không hiện tên enum FirstWeight / SecondWeight).</summary>
    private static string CheckpointLabel(CheckpointType checkpoint) => checkpoint switch
    {
        CheckpointType.FirstWeight => Loc.T("weigh.cp_first"),
        CheckpointType.SecondWeight => Loc.T("weigh.cp_second"),
        _ => checkpoint.ToString()
    };

    /// <summary>Hiện chính sách + tạp chất / CCS / TL thanh toán trên panel cân. Null = chưa có ("—").</summary>
    private void ShowPaymentFigures(string? policyName, decimal? tapChat, decimal? ccs, decimal? paidKg)
    {
        _txtPolicyName.Text = string.IsNullOrWhiteSpace(policyName) ? "—" : policyName;
        _txtTcThanhToan.Text = tapChat is { } tc ? $"{tc:N2} %" : "—";
        _txtCcsThanhToan.Text = ccs is { } c ? c.ToString("N2") : (tapChat is null ? "—" : Loc.T("weigh.ccs_not_available"));
        _txtTlThanhToan.Text = paidKg is { } kg ? FormatWeight(kg) : "—";
        // Font cố định (tạo một lần từ font gốc của ô) — không dựng lại theo cỡ hiện tại, tránh chữ to dần mỗi lần gọi.
        _paymentRegularFont ??= _txtTcThanhToan.Font;
        _paymentBoldFont ??= new Font(_paymentRegularFont, FontStyle.Bold);
        var bold = tapChat is not null;
        foreach (var box in new[] { _txtTcThanhToan, _txtCcsThanhToan, _txtTlThanhToan })
        {
            box.Font = bold ? _paymentBoldFont : _paymentRegularFont;
            box.ForeColor = bold ? AgrisTheme.BrandTealDark : AgrisTheme.ReadOnlyText;
        }
    }

    /// <summary>Hộp xác nhận trước khi lưu cân lần 2: chính sách + số thanh toán người cân cần biết.</summary>
    private bool ConfirmPaymentBeforeSave(TTC.Weighing.Infrastructure.Adapters.TicketResult policy, decimal secondKg, decimal? paidKg)
    {
        var unit = WeightUnitLabel;
        var facts = new List<(string, string?)>
        {
            (Loc.T("alert.fact_trip"), _txtRegNo.Text),
            (Loc.T("alert.fact_plate"), _txtPlate.Text),
            (Loc.T("alert.fact_lenhdon"), _currentFrmMaLenhDon ?? _txtShipment.Text),
            (Loc.T("field.policy_name"), string.IsNullOrWhiteSpace(policy.ChinhSachThuMia) ? "—" : policy.ChinhSachThuMia),
            (Loc.T("alert.fact_weight1"), _firstWeighKg is { } w1 ? $"{FormatWeight(w1)} {unit}" : "—"),
            (Loc.T("alert.fact_weight2"), $"{FormatWeight(secondKg)} {unit}"),
            (Loc.T("field.net_weight"), _firstWeighKg is { } first ? $"{FormatWeight(first - secondKg)} {unit}" : "—"),
        };

        // Khối nổi bật: 3 số thanh toán người cân cần biết trước khi lưu.
        var payment = string.Join(Environment.NewLine,
            Loc.T("weigh.policy_confirm_tc", policy.TapChatCS?.ToString("N2") ?? "—"),
            Loc.T("weigh.policy_confirm_ccs", policy.CcsChinhSach?.ToString("N2") ?? Loc.T("weigh.ccs_not_available")),
            Loc.T("weigh.policy_confirm_tl", paidKg is { } kg ? FormatWeight(kg) : "—", unit));

        return DigiScalePopup.ShowConfirm(this,
            Loc.T("weigh.policy_confirm_title").ToUpper(),
            Loc.T("weigh.policy_confirm_yes"),
            Loc.T("weigh.policy_confirm_no"),
            facts,
            Loc.T("weigh.policy_confirm_question"),
            [(Loc.T("weigh.policy_confirm_section"), payment)]);
    }

    /// <summary>
    /// Xem thêm thông tin chuyến — mọi dữ liệu sẽ đẩy lên DigiScale: đăng tài, lệnh đốn FRM, cân vào/ra,
    /// tạp chất, CCS và số thanh toán theo chính sách. Chỉ đọc.
    /// </summary>
    private async Task ShowTripMoreAsync()
    {
        if (_currentTripId is not { } tripId) return;
        var scan = _lastScan;

        Domain.Entities.QualitySample? impurity = null, ccs = null;
        DigiScaleWeighContext? ctx = null;
        await _dbLock.WaitAsync(_cts.Token);
        try
        {
            impurity = await _presenter.GetEffectiveSampleAsync(tripId, Domain.Enums.QualitySampleType.Impurity, _cts.Token);
            ccs = await _presenter.GetEffectiveSampleAsync(tripId, Domain.Enums.QualitySampleType.Ccs, _cts.Token);
            ctx = await _presenter.GetDigiScaleContextAsync(tripId, _cts.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { /* hiện phần còn lại */ }
        finally { _dbLock.Release(); }

        // Vật tư (ERP/WMS): chỉ thông tin vật tư — không có lệnh đốn, tạp chất, CCS, chính sách FRM.
        if (_currentSourceSystem == TripCategoryExtensions.MATERIAL_SOURCE_SYSTEM)
        {
            await ShowMaterialTripMoreAsync(tripId, scan, ctx);
            return;
        }

        // Chưa có chính sách trên form → lấy bản đã lưu (không gọi FRM).
        if (_policyTapChatCS is null)
        {
            _secondWeighKgForPolicy ??= ctx?.SecondKg;
            await LoadOrPreviewPolicyAsync(allowPreview: false);
        }

        // Lệnh đốn theo ID lưu trên chuyến (DB) — không đoán theo biển số khi chuyến đã có lệnh đốn.
        var ld = _presenter.FindLenhDonForTrip(ctx?.LenhDonId ?? _currentFrmLenhDonId, _txtPlate.Text);
        var tripNo = scan?.TripNo ?? ctx?.TripNo ?? _txtRegNo.Text;
        var info = LenhDonDetails.Values(ld);
        string? I(string key) => info.TryGetValue(key, out var v) ? v : null;
        string Kg(decimal? v) => v is { } x ? $"{x:N2} kg" : "—";

        var sections = new List<(string Title, List<(string Label, string? Value)> Rows)>
        {
            (Loc.T("weigh.more_sec_registration"), [
                (Loc.T("field.trip_no"), tripNo),
                (Loc.T("field.plate"), scan?.Plate ?? _txtPlate.Text),
                (Loc.T("field.sugarcane_owner"), scan?.SugarcaneOwner ?? scan?.CustomerOrOwner ?? _txtCustomer.Text),
                (Loc.T("field.sugarcane_type"), scan?.SugarcaneType ?? ld?.LoaiMiaName),
                (Loc.T("field.sugarcane_variety"), scan?.SugarcaneVariety ?? ld?.GiongMiaName),
                (Loc.T("field.station"), ld?.TramName),
                (Loc.T("reg.field_crop_investment"), ld?.VudautuName),
                (Loc.T("field.commodity"), _txtCommodity.Text),
                (Loc.T("field.driver"), scan?.DriverRef),
            ]),
            (Loc.T("weigh.more_sec_lenhdon"), ld is null ? [(Loc.T("reg.detail_ma_ld"), ctx?.MaLenhDon ?? scan?.FrmMaLenhDon)] : [
                (Loc.T("reg.detail_ma_ld"), I("ma_ld")),
                (Loc.T("reg.detail_status"), I("status")),
                (Loc.T("reg.detail_validity"), I("validity")),
                (Loc.T("reg.detail_contract_type"), I("contract_type")),
                (Loc.T("reg.detail_contract"), I("contract")),
                (Loc.T("reg.detail_customer_company"), I("customer_company")),
                (Loc.T("reg.detail_burnt"), I("burnt")),
                (Loc.T("reg.detail_harvest"), I("harvest")),
                (Loc.T("reg.detail_cane_form"), I("cane_form")),
                (Loc.T("reg.detail_harvest_partner"), I("harvest_partner")),
                (Loc.T("reg.detail_plot"), I("plot")),
                (Loc.T("reg.detail_temp_card"), I("temp_card")),
                (Loc.T("reg.detail_used_plate"), I("used_plate")),
            ]),
            (Loc.T("weigh.more_sec_weigh"), [
                (Loc.T("field.weight_in"), _txtWeightIn.Text),
                (Loc.T("field.time_in"), _txtTimeIn.Text),
                (Loc.T("field.weight_out"), _txtWeightOut.Text),
                (Loc.T("field.time_out"), _txtTimeOut.Text),
                (Loc.T("field.net_weight"), _txtNetWeight.Text),
            ]),
            (Loc.T("weigh.more_sec_impurity"), impurity is null ? [(Loc.T("weigh.more_impurity_pct"), Loc.T("weigh.more_not_measured"))] : [
                (Loc.T("weigh.more_sample_before"), Kg(impurity.SampleBeforeKg)),
                (Loc.T("weigh.more_sample_after"), Kg(impurity.SampleAfterKg)),
                (Loc.T("weigh.more_impurity_pct"), impurity.ImpurityPercent is { } p ? $"{p:N2} %" : "—"),
                (Loc.T("weigh.more_source"), impurity.Source),
                (Loc.T("weigh.more_received_at"), impurity.ReceivedAt?.ToVnTime().ToString("dd/MM/yyyy HH:mm")),
            ]),
            (Loc.T("weigh.more_sec_ccs"), ccs is null ? [("CCS", Loc.T("weigh.more_not_measured"))] : [
                ("CCS", ccs.CcsValue?.ToString("N2") ?? Loc.T("ccs.pending_value")),
                (Loc.T("weigh.more_received_at"), ccs.ReceivedAt?.ToVnTime().ToString("dd/MM/yyyy HH:mm")),
            ]),
            (Loc.T("weigh.more_sec_payment"), [
                (Loc.T("field.policy_name"), _txtPolicyName.Text),
                (Loc.T("field.tc_thanh_toan"), _txtTcThanhToan.Text),
                (Loc.T("field.ccs_thanh_toan"), _txtCcsThanhToan.Text),
                (Loc.T("field.tl_thanh_toan"), _txtTlThanhToan.Text),
            ]),
        };

        ShowTripInfoPopup(tripNo, sections, ctx?.MaLenhDon ?? ld?.MaLD);
    }

    /// <summary>
    /// Xem thêm thông tin chuyến vật tư: đăng tài + chứng từ ERP (Shipment/TRIP, loại GD, nguồn Oracle/WMS…)
    /// + kết quả cân kg. Trọng lượng hàng theo hướng (nhập: vào − ra; xuất: ra − vào), không lấy trị tuyệt đối.
    /// </summary>
    private async Task ShowMaterialTripMoreAsync(Guid tripId, QrScanResult? scan, DigiScaleWeighContext? ctx)
    {
        var shipment = _txtShipment.Text.NullIfEmpty() ?? scan?.ShipmentNo;
        TTC.Weighing.Infrastructure.Adapters.ErpOrderLine? line = null;
        string? warehouse = null, transporter = null;
        try
        {
            if (shipment is not null)
                line = await _presenter.LookupErpOrderAsync(_presenter.DefaultBusinessUnit, shipment);
            warehouse = await _presenter.ResolveWarehouseAsync(tripId, line, _cts.Token);
            transporter = await _presenter.ResolveTransporterAsync(tripId, line, _cts.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { /* hiện phần có trên chuyến */ }

        var tripNo = scan?.TripNo ?? ctx?.TripNo ?? _txtRegNo.Text;
        var isOutbound = line?.IsShipConfirmFlow
            ?? string.Equals(_txtDirection.Text, Loc.T("reg.direction_out"), StringComparison.Ordinal);
        var direction = line is null && _txtDirection.TextLength == 0
            ? null
            : Loc.T(isOutbound ? "reg.direction_out" : "reg.direction_in");
        decimal? net = ctx is { FirstKg: { } first, SecondKg: { } second }
            ? (isOutbound ? second - first : first - second)
            : null;

        string? Kg(decimal? v) => v is { } x ? $"{x:N3} kg" : null; // số cân kg thống nhất N3
        string? Qty(decimal? v) => v is { } x ? $"{x:N0} {line?.Uom}".Trim() : null;
        string? Time(DateTimeOffset? t) => t?.ToVnTime().ToString("dd/MM/yyyy HH:mm:ss");
        var item = line?.ItemDescription ?? line?.ItemName ?? _txtCommodity.Text.NullIfEmpty();
        var itemCode = line?.ItemNumber ?? line?.ItemCode;

        var sections = new List<(string Title, List<(string Label, string? Value)> Rows)>
        {
            (Loc.T("weigh.more_sec_registration"), [
                (Loc.T("field.trip_no"), tripNo),
                (Loc.T("field.plate"), scan?.Plate ?? _txtPlate.Text),
                (Loc.T("field.direction"), direction),
                (Loc.T("field.customer"), _txtCustomer.Text.NullIfEmpty() ?? line?.VendorName ?? line?.CustomerName),
                (Loc.T("field.commodity"), itemCode is null ? item : $"{item} ({itemCode})"),
                (Loc.T("field.warehouse"), warehouse ?? _txtWarehouse.Text.NullIfEmpty()),
                (Loc.T("field.transporter"), transporter ?? _txtTransporter.Text.NullIfEmpty()),
                (Loc.T("field.driver"), _txtDriver.Text.NullIfEmpty() ?? scan?.DriverRef ?? line?.DriverName),
            ]),
            (Loc.T("weigh.more_sec_erp"), line is null ? [(Loc.T("field.shipment"), shipment), (Loc.T("weigh.more_erp_status"), Loc.T("weigh.more_erp_not_found"))] : [
                (Loc.T("field.shipment"), line.ShipmentNumber ?? shipment),
                (Loc.T("reg.field_trip_number"), line.TripNumber),
                (Loc.T("col.source"), line.SourceLabel),
                (Loc.T("erp.col_txn_type"), string.Join(" - ", new[] { line.TransactionTypeCode, line.TransactionTypeName }.Where(s => !string.IsNullOrWhiteSpace(s)))),
                ("Action", line.EffectiveActionCode),
                (Loc.T("erp.col_reference"), line.ReferenceNumber),
                (Loc.T("erp.col_total_qty"), Qty(line.TotalQuantity)),
                (Loc.T("erp.col_remaining_qty"), Qty(line.RemainingQuantity)),
                (Loc.T("erp.col_txn_date"), PresenterErpDate(line)),
                ("Org Code", line.OrganizationCode),
                ("BU", line.BusinessUnit),
            ]),
            (Loc.T("weigh.more_sec_weigh"), [
                (Loc.T("print.mat_weight_in"), Kg(ctx?.FirstKg)),
                (Loc.T("print.mat_time_in"), Time(ctx?.FirstAt)),
                (Loc.T("print.mat_weight_out"), Kg(ctx?.SecondKg)),
                (Loc.T("print.mat_time_out"), Time(ctx?.SecondAt)),
                (Loc.T("print.mat_net"), net is < 0m ? $"{Kg(net)}  ⚠ {Loc.T("print.mat_net_negative")}" : Kg(net)),
            ]),
        };

        ShowTripInfoPopup(tripNo, sections, null);
    }

    private static string? PresenterErpDate(TTC.Weighing.Infrastructure.Adapters.ErpOrderLine line)
        => TripRegistrationPresenter.ErpDate(line)?.ToString("dd/MM/yyyy HH:mm");

    /// <summary>Popup chỉ đọc: các khối tiêu đề + "nhãn / giá trị", nút xem log API của chuyến.</summary>
    private void ShowTripInfoPopup(string tripNo, List<(string Title, List<(string Label, string? Value)> Rows)> sections,
        string? maLdForLog)
    {
        using var popup = new Form
        {
            Text = "📋 " + Loc.T("weigh.more_title", string.IsNullOrWhiteSpace(tripNo) ? "—" : tripNo),
            Size = new Size(760, 860),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = AgrisTheme.Surface,
            AutoScroll = true
        };
        var table = FormLayout.FieldTable();
        table.Dock = DockStyle.Top;
        table.Padding = new Padding(12, 8, 12, 8);

        foreach (var (title, rows) in sections)
        {
            FormLayout.AddFullWidth(table, GridBuilder.SectionHeader(title), 44);
            foreach (var (label, value) in rows)
                FormLayout.AddField(table, label, new TextBox
                {
                    Text = string.IsNullOrWhiteSpace(value) ? "—" : value,
                    ReadOnly = true,
                    Dock = DockStyle.Fill,
                    Font = AgrisTheme.Body(11F),
                    BackColor = AgrisTheme.ReadOnlyBg,
                    ForeColor = AgrisTheme.ReadOnlyText,
                    BorderStyle = BorderStyle.FixedSingle,
                    TabStop = false
                });
        }

        var btnLog = GridBuilder.PrimaryButton("📜  " + Loc.T("weigh.btn_trip_api_log"), new Size(240, 42));
        btnLog.Click += (_, _) => ShowTripApiLog(tripNo, maLdForLog, popup);
        var btnClose = GridBuilder.SecondaryButton(Loc.T("common.close"), new Size(120, 42));
        btnClose.DialogResult = DialogResult.Cancel;
        FormLayout.AddFullWidth(table, FormLayout.ActionBar(btnLog, btnClose), FormLayout.ActionRowHeight);
        popup.Controls.Add(table);
        popup.CancelButton = btnClose;
        popup.ShowDialog(this);
    }

    /// <summary>Nhật ký đẩy / kéo API của một chuyến (mọi ngày): đăng tài, cân, tạp chất, CCS, bắt chính sách.</summary>
    private void ShowTripApiLog(string tripNo, string? maLenhDon, IWin32Window owner)
    {
        if (string.IsNullOrWhiteSpace(tripNo)) return;
        using var form = new ApiLogForm(_apiLogs)
        {
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(1500, 900),
            ShowInTaskbar = false
        };
        form.FocusTrip(tripNo, maLenhDon);
        form.ShowDialog(owner);
    }

    private static bool IsNotWeighedIn(TTC.Weighing.Infrastructure.Adapters.TicketResult r)
        => r.ErrorMessage?.Contains("NOT_WEIGHED_IN", StringComparison.OrdinalIgnoreCase) == true
           || r.ErrorMessage?.Contains("CR_04", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Gửi một phiếu cân mía. FRM nhận đơn vị TẤN; giờ cân là giờ thực tế của lượt cân.</summary>
    private async Task<TTC.Weighing.Infrastructure.Adapters.TicketResult> SendPhieuCanAsync(
        DigiScaleWeighContext ctx, bool isCânRa, decimal normalizedKg, DateTimeOffset measuredAt, string? maBanCan)
    {
        try
        {
            return await _presenter.PushPhieuCanAsync(
                isCânRa ? "100000001" : "100000000", // LoaiCan: cân ra / cân vào
                isCânRa ? "2" : "1",
                Program.LoggedInUserName, Program.LoggedInUserAccount,
                ctx.LenhDonId!, ctx.MaLenhDon!,
                Num.S2(normalizedKg / 1000m),
                measuredAt.ToVnTime().DateTime,
                ctx.SoPhieuCan, // mã phiếu cân PC… (cân vào / ra cùng mã)
                maBanCan,
                "0", "0", "0",
                ctx.LenhDonBu, _cts.Token,
                isQuetCS: isCânRa,
                ghiChu: WeighNote);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Mất kết nối / timeout: coi như DigiScale chưa nhận.
            return new TTC.Weighing.Infrastructure.Adapters.TicketResult(false, ex.Message);
        }
    }

    /// <summary>Các dòng thông tin chung trên hộp thông báo cân mía.</summary>
    private List<(string, string?)> WeighFacts(string maLenhDon, decimal kg, bool isWeighOut) =>
    [
        (Loc.T("alert.fact_trip"), _txtRegNo.Text),
        (Loc.T("alert.fact_plate"), _txtPlate.Text),
        (Loc.T("alert.fact_lenhdon"), maLenhDon),
        (isWeighOut ? Loc.T("alert.fact_weight2") : Loc.T("alert.fact_weight1"), $"{FormatWeight(kg)} {WeightUnitLabel}")
    ];

    /// <summary>Lý do + cách xử lý khi đẩy cân mía chưa thành công (hoặc lần 2 không có chính sách).</summary>
    private static FriendlyError DescribePush(TTC.Weighing.Infrastructure.Adapters.TicketResult push, bool isWeighOut)
        => push.IsSuccess && isWeighOut && !push.HasPolicy
            ? new FriendlyError(Loc.T("digi.err_policy_empty"), Loc.T("digi.act_no_policy"))
            : push.Describe() ?? new FriendlyError(Loc.T("digi.err_unknown"), Loc.T("digi.act_retry"));

    /// <summary>Hộp thông báo sau khi lưu cân mía + đẩy DigiScale (thành công / chưa đẩy được).</summary>
    /// <param name="payment">Kết quả đẩy số liệu thanh toán (cân lần 2 có chính sách); null nếu không áp dụng.</param>
    private void ShowWeighResult(TTC.Weighing.Infrastructure.Adapters.TicketResult push, string maLenhDon, decimal kg, bool isWeighOut,
        (bool Ok, string Detail)? payment = null)
    {
        var facts = WeighFacts(maLenhDon, kg, isWeighOut);

        if (push.IsSuccess && (!isWeighOut || push.HasPolicy))
        {
            if (isWeighOut)
            {
                facts.Add((Loc.T("alert.fact_policy_tc"), $"{push.TapChatCS:N2} %"));
                facts.Add((Loc.T("alert.fact_policy_ccs"), push.CcsChinhSach?.ToString("N2") ?? Loc.T("weigh.ccs_not_available")));
            }

            // Phiếu cân đã lên DigiScale nhưng số liệu thanh toán chưa đẩy được → cảnh báo, nêu rõ cần Đẩy lại Digi.
            if (payment is { Ok: false } failedPayment)
            {
                DigiScalePopup.ShowAlert(this, AlertKind.Warning,
                    Loc.T("weigh.push2_payment_failed_title"),
                    reason: failedPayment.Detail, action: Loc.T("weigh.push2_payment_failed_action"), facts: facts);
                _lblQrMessage.Text = Loc.T("weigh.final_push_failed", failedPayment.Detail);
                _lblQrMessage.ForeColor = AgrisTheme.Warning;
                return;
            }

            if (payment is { Ok: true } okPayment)
                facts.Add((Loc.T("alert.fact_payment"), okPayment.Detail));

            DigiScalePopup.ShowAlert(this, AlertKind.Success,
                isWeighOut ? Loc.T("weigh.push2_ok_title") : Loc.T("weigh.push1_ok_title"),
                facts: facts, message: Loc.T("weigh.pushed_ok_detail"));

            _lblQrMessage.Text = Loc.T("weigh.pushed_ok");
            _lblQrMessage.ForeColor = AgrisTheme.Success;
            return;
        }

        var error = DescribePush(push, isWeighOut);
        DigiScalePopup.ShowError(this,
            isWeighOut ? Loc.T("weigh.push2_failed_title") : Loc.T("weigh.push1_failed_title"),
            error, facts);

        _lblQrMessage.Text = "⛔ " + error.Reason.Replace(Environment.NewLine, " ");
        _lblQrMessage.ForeColor = AgrisTheme.Danger;
    }

    /// <summary>Báo lý do KHÔNG lưu cân: DigiScale từ chối / mất kết nối, hoặc (lần 2) không có chính sách.</summary>
    private void ShowPushGateFailure(
        TTC.Weighing.Infrastructure.Adapters.TicketResult push, string maLenhDon, decimal kg, bool isWeighOut)
    {
        var lanCan = isWeighOut ? "2" : "1";
        var error = DescribePush(push, isWeighOut);

        DigiScalePopup.ShowAlert(this, AlertKind.Error,
            Loc.T("weigh.gate_not_saved_title", lanCan),
            error.Reason,
            (error.Action is null ? "" : error.Action + Environment.NewLine) + Loc.T("weigh.gate_not_saved_hint", lanCan),
            WeighFacts(maLenhDon, kg, isWeighOut));

        _lblQrMessage.Text = Loc.T("weigh.gate_not_saved_bar", lanCan) + " — " + error.Reason.Replace(Environment.NewLine, " ");
        _lblQrMessage.ForeColor = AgrisTheme.Danger;
    }

    /// <summary>
    /// Sau khi chốt cân vào: mở luôn phiên CÂN RA cho cùng chuyến — TL/TG cân vào nạp lại từ server,
    /// người vận hành chỉ cần bấm "Nhập tay" (hoặc chờ số cân ổn định) rồi Xác nhận (F5).
    /// <para>
    /// Mọi điều kiện vẫn do server quyết định qua đúng luồng quét QR (HandleQrScanAsync).
    /// Mía chưa đo tạp chất thì KHÔNG chuyển (quy tắc 8) — giữ màn hình sẵn sàng cho xe kế tiếp.
    /// Phiên cân ra đang mở không khoá bàn cân: quét xe khác thì server tự giải phóng phiên cũ.
    /// </para>
    /// </summary>
    private async Task ContinueToWeighOutAsync(string qrContent, Guid tripId)
    {
        if (IsDisposed || _cts.IsCancellationRequested || _isBusy) return;

        // Người dùng đã quét xe khác trong lúc chờ → không chen ngang.
        if (_currentTripId != tripId || _sessionId is not null) return;

        bool impurityOk;
        await _dbLock.WaitAsync(_cts.Token);
        try
        {
            var pre = await _presenter.CheckPreconditionsAsync(tripId, WeightRole.Tare, 1, _cts.Token);
            impurityOk = pre.ImpuritySatisfied;
        }
        catch (OperationCanceledException) { return; }
        catch
        {
            // Không kiểm tra được → giữ cách cũ (quét lại thủ công khi xe quay về cân ra).
            return;
        }
        finally { _dbLock.Release(); }

        if (!impurityOk)
        {
            _lblQrMessage.Text = Loc.T("weigh.auto_out_wait_impurity");
            _lblQrMessage.ForeColor = AgrisTheme.Warning;
            return;
        }

        _txtBarcode.Enabled = true;
        _txtBarcode.Text = qrContent;
        await HandleQrScanAsync(qrContent);

        if (_sessionId is null || !_isWeighOutMode) return;

        _lblQrMessage.Text = Loc.T("weigh.auto_out_ready");
        _lblQrMessage.ForeColor = AgrisTheme.Info;

        // Đưa con trỏ vào nút Nhập tay (nếu có quyền) → Enter/Space là mở hộp nhập số cân lần 2.
        if (_btnManualWeight is { Visible: true, Enabled: true })
            _btnManualWeight.Focus();
    }

    private async void OnPrintClicked(object? sender, EventArgs e)
    {
        if (_currentTripId is null)
        {
            Helpers.AppMessageBox.Show(Loc.T("weigh.print_no_trip"), Loc.T("common.no_data"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Xem / in lại phiếu (nút In phiếu, F6) cần quyền quản lý. Phiếu tự hiện sau khi cân ra thì không.
        if (!ManagerAccess.RequireReprint(this, _txtRegNo.Text)) return;

        // Phiếu đã hiện / in trước đó → bản này là BẢN IN LẠI (như Đăng tài).
        // Chưa từng hiện (vd đẩy DigiScale lỗi nên phiếu không tự hiện) → vẫn là bản in đầu.
        try { await ShowTicketAsync(isReprint: _ticketPrinted); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ShowError(Loc.T("common.error"), ex); }
    }

    /// <summary>
    /// Cân lần 2 xong + đã đẩy DigiScale thành công → tự hiện phiếu cân để in (như Đăng tài).
    /// Lỗi xem trước / máy in không ảnh hưởng số cân đã lưu.
    /// </summary>
    private async Task ShowTicketAfterCompletionAsync()
    {
        using var systemScope = Loc.UseSystem(TargetSystem);
        try { await ShowTicketAsync(); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _lblQrMessage.Text = "⚠ " + Loc.T("weigh.auto_print_failed", ex.Message);
            _lblQrMessage.ForeColor = AgrisTheme.Warning;
        }
    }

    /// <summary>Xem trước + in phiếu cân của chuyến đang mở (mía hoặc vật tư).</summary>
    /// <param name="isReprint">true = in nhãn "BẢN IN LẠI" trên phiếu và tiêu đề cửa sổ.</param>
    private async Task ShowTicketAsync(bool isReprint = false)
    {
        if (_currentTripId is null)
        {
            Helpers.AppMessageBox.Show(Loc.T("weigh.print_no_trip"), Loc.T("common.no_data"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Quyền quản lý cho xem / in lại kiểm ở OnPrintClicked; phiếu tự hiện sau cân ra đi thẳng vào đây.
        // Bản in lại vẫn đánh dấu "BẢN IN LẠI" và ghi audit.

        // Số cân lấy từ DB (ô trên form đã làm tròn hiển thị — không dùng để in/đẩy).
        DigiScaleWeighContext? weighCtx = null;
        try { weighCtx = await _presenter.GetDigiScaleContextAsync(_currentTripId.Value, _cts.Token); }
        catch (Exception ex) when (ex is not OperationCanceledException) { /* in với dữ liệu có được */ }
        // Phiếu in đúng mã phiếu cân đã gửi DigiScale.
        if (weighCtx?.TicketNo is { } printTicketNo) _txtTicketNo.Text = printTicketNo;

        // Vật tư (ERP/WMS) in phiếu vật tư riêng — không dùng mẫu phiếu cân mía.
        if (_currentSourceSystem == TripCategoryExtensions.MATERIAL_SOURCE_SYSTEM)
        {
            await PrintMaterialTicketAsync(weighCtx, isReprint);
            _ticketPrinted = true;
            return;
        }

        // Phiếu mía in tấn. Lần 1 = xổi (cả xe), lần 2 = bì.
        var grossTon = (weighCtx?.FirstKg ?? 0m) / 1000m;
        var tareTon = (weighCtx?.SecondKg ?? 0m) / 1000m;
        var netTon = weighCtx is { FirstKg: { } fKg, SecondKg: { } sKg } ? (fKg - sKg) / 1000m : 0m;

        // Lấy tạp chất từ DB
        decimal? impurityPct = null;
        try { impurityPct = await _presenter.GetEffectiveImpurityAsync(_currentTripId.Value, _cts.Token); }
        catch { /* lỗi → null */ }

        // Nếu chưa có chính sách → gọi API lấy chính sách trước khi in (gửi lại lần 2 đúng số/giờ đã lưu)
        if (!_policyTapChatCS.HasValue && weighCtx is { SecondKg: { } secondKg, SecondAt: { } secondAt }
            && _currentSourceSystem != "ERP")
        {
            try
            {
                var (policyResult, _) = await PushSugarcaneWeighAsync(weighCtx.TripId, true, secondKg, secondAt);

                if (policyResult.HasPolicy)
                {
                    _policyTapChatCS = policyResult.TapChatCS;
                    _policyCcsChinhSach = policyResult.CcsChinhSach;
                    _policyChinhSachThuMia = policyResult.ChinhSachThuMia;
                    ShowPaymentFigures(policyResult.ChinhSachThuMia, policyResult.TapChatCS, policyResult.CcsChinhSach,
                        PaymentWeightKg(weighCtx.FirstKg, secondKg, policyResult.TapChatCS));
                }
                else if (policyResult.ErrorMessage is not null && policyResult.ErrorMessage.Contains("NO_POLICY"))
                {
                    // DigiScale trả NO_POLICY — lệnh đốn chưa có chính sách thu mua
                    _policyChinhSachThuMia = Loc.T("weigh.policy_none_short");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[POLICY] IsSuccess={policyResult.IsSuccess} TapChatCS={policyResult.TapChatCS} Response={policyResult.ErrorMessage?[..Math.Min(500, policyResult.ErrorMessage?.Length ?? 0)]}");
                }
            }
            catch { /* API lỗi → in phiếu với dữ liệu hiện có */ }
        }

        // Lấy thêm thông tin từ FRM cache
        var ldForPrint = _presenter.FindLenhDonForTrip(weighCtx?.LenhDonId ?? _currentFrmLenhDonId, _txtPlate.Text);
        var sugarcaneForm = ldForPrint?.SugarcaneForm ?? (IsMiaChay: false, IsMiaCay: true, IsMiaKhuc: false);

        var data = new Printing.PhieuCanData(
            // Nhà máy theo lệnh đốn FRM (TenNhaMay), không có thì theo đơn vị của tài khoản đăng nhập.
            CompanyName: ldForPrint?.TenNhaMay ?? _siteContext.CurrentSiteName,
            CompanyAddress: _siteContext.CurrentSiteAddress,
            PhieuCanSo: _txtTicketNo.Text.NullIfEmpty(),
            SoDangTai: _txtRegNo.Text.NullIfEmpty(),
            Ngay: VnTime.Now,
            // Chủ mía trống → khách hàng doanh nghiệp của lệnh đốn.
            TenChuMia: _txtCustomer.Text.NullIfEmpty() ?? ldForPrint?.OwnerName,
            HopDongSo: ldForPrint?.HopDongDauTuMiaName,
            LenhDon: _txtShipment.Text.NullIfEmpty() ?? ldForPrint?.MaLD,
            SoXe: _txtPlate.Text.NullIfEmpty(),
            CongThuHoach: ldForPrint?.HarvestPartner,
            CongBocXep: ldForPrint?.DoiTacBocXepName,
            GiongMia: ldForPrint?.GiongMiaName,
            // Loại mía = Mía cháy / Mía khúc / Mía cây theo lệnh đốn (song ngữ theo BU).
            LoaiMia: Printing.PrintText.CaneForm(ldForPrint) ?? _txtCommodity.Text.NullIfEmpty() ?? ldForPrint?.LoaiMiaName,
            HinhThucThuHoach: Printing.PrintText.HarvestMethod(ldForPrint?.ThuHoachMay),
            TrongLuongXi: grossTon > 0 ? grossTon : null,
            ThoiGianCanXi: weighCtx?.FirstAt?.ToVnTime().DateTime,
            TrongLuongBi: tareTon > 0 ? tareTon : null,
            ThoiGianCanBi: weighCtx?.SecondAt?.ToVnTime().DateTime,
            TapChatThanhToan: _policyTapChatCS ?? impurityPct,
            // Cùng cách tính với số gửi FRM (làm tròn 2 chữ số ở từng bước).
            TrongLuongThanhToan: netTon > 0 && _policyTapChatCS.HasValue
                ? Num.R2(Num.R2(netTon) * (1m - Num.R2(_policyTapChatCS.Value) / 100m)) : null,
            GhiChu: _txtWeighNotes.Text.NullIfEmpty(),
            // Mía cháy = FRM Miachay; Mía khúc = thu hoạch máy; Mía cây = thu hoạch thủ công
            IsMiaChay: sugarcaneForm.IsMiaChay,
            IsMiaCay: sugarcaneForm.IsMiaCay,
            IsMiaKhuc: sugarcaneForm.IsMiaKhuc,
            Barcode: _txtRegNo.Text.NullIfEmpty(),
            ChinhSachThuMia: _policyChinhSachThuMia,
            CcsThanhToan: _policyCcsChinhSach);

        using var preview = new WeighTicketPreviewForm(data, isReprint);
        preview.ShowDialog(this);

        _ticketPrinted = true;
    }

    /// <summary>
    /// Phiếu cân vật tư: thông tin chứng từ ERP như màn Đăng tài vật tư + số cân kg.
    /// Trọng lượng hàng theo hướng (nhập: vào − ra; xuất: ra − vào), không lấy trị tuyệt đối.
    /// </summary>
    private async Task PrintMaterialTicketAsync(DigiScaleWeighContext? weighCtx, bool isReprint = false)
    {
        var shipment = _txtShipment.Text.NullIfEmpty();
        TTC.Weighing.Infrastructure.Adapters.ErpOrderLine? line = null;
        if (shipment is not null)
        {
            try { line = await _presenter.LookupErpOrderAsync(_presenter.DefaultBusinessUnit, shipment); }
            catch { /* ERP chưa kéo về → in phần có trên chuyến */ }
        }

        var isOutbound = line?.IsShipConfirmFlow
            ?? string.Equals(_txtDirection.Text, Loc.T("reg.direction_out"), StringComparison.Ordinal);
        var direction = line is null && _txtDirection.TextLength == 0
            ? null
            : Printing.PrintText.L(isOutbound ? "reg.direction_out" : "reg.direction_in");

        decimal? net = weighCtx is { FirstKg: { } first, SecondKg: { } second }
            ? (isOutbound ? second - first : first - second)
            : null;

        var data = new Printing.PhieuCanVatTuData(
            CompanyName: _siteContext.CurrentSiteName,
            CompanyAddress: _siteContext.CurrentSiteAddress,
            PhieuCanSo: _txtTicketNo.Text.NullIfEmpty(),
            SoDangTai: _txtRegNo.Text.NullIfEmpty(),
            Ngay: VnTime.Now,
            SoXe: _txtPlate.Text.NullIfEmpty(),
            Shipment: shipment,
            SoTrip: line?.TripNumber ?? _txtErpTripNumber.Text.NullIfEmpty(),
            Huong: direction,
            KhachHangNcc: _txtCustomer.Text.NullIfEmpty() ?? line?.VendorName ?? line?.CustomerName,
            HangHoa: line?.ItemDescription ?? line?.ItemName ?? _txtCommodity.Text.NullIfEmpty(),
            MaHang: line?.ItemNumber ?? line?.ItemCode,
            Kho: await _presenter.ResolveWarehouseAsync(_currentTripId, line, _cts.Token) ?? _txtWarehouse.Text.NullIfEmpty(),
            NhaVanChuyen: await _presenter.ResolveTransporterAsync(_currentTripId, line, _cts.Token) ?? _txtTransporter.Text.NullIfEmpty(),
            LaiXe: _txtDriver.Text.NullIfEmpty() ?? line?.DriverName,
            TrongLuongVao: weighCtx?.FirstKg,
            ThoiGianVao: weighCtx?.FirstAt?.ToVnTime().DateTime,
            TrongLuongRa: weighCtx?.SecondKg,
            ThoiGianRa: weighCtx?.SecondAt?.ToVnTime().DateTime,
            TrongLuongHang: net,
            GhiChu: _txtWeighNotes.Text.NullIfEmpty(),
            Barcode: _txtRegNo.Text.NullIfEmpty());

        using var preview = new WeighTicketPreviewForm(data, isReprint);
        preview.ShowDialog(this);
    }

    private async void OnSyncClicked(object? sender, EventArgs e)
    {
        using var systemScope = Loc.UseSystem(TargetSystem);
        if (_currentTripId is null)
        {
            Helpers.AppMessageBox.Show("Chọn chuyến trước khi đẩy lại.", "Thiếu thông tin",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SetBusy(true);
        try
        {
            if (_currentSourceSystem == "ERP")
            {
                // ═══ VẬT TƯ → đẩy lại oracle-tickets ═══
                var weightInKg = decimal.TryParse(_txtWeightIn.Text.Replace(",", "").Replace(".", ""), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var wiVal) ? wiVal : 0m;
                var weightOutKg = decimal.TryParse(_txtWeightOut.Text.Replace(",", "").Replace(".", ""), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var woVal) ? woVal : 0m;
                var netKg = Math.Abs(weightInKg - weightOutKg);
                var bu = _presenter.DefaultBusinessUnit;
                var shipmentForErp = _txtShipment.Text.Trim();

                TTC.Weighing.Infrastructure.Adapters.ErpOrderLine? erpLine = null;
                try { erpLine = await _presenter.LookupErpOrderAsync(bu, shipmentForErp); }
                catch { }

                if (erpLine is null)
                {
                    Helpers.AppMessageBox.Show("Không tìm thấy ERP order-line.\nKhông thể đẩy lại.", "ERP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // WMS cũng là luồng SO → CONFIRM; ưu tiên actionCodeRequest ERP trả về.
                var txnTypeName = erpLine.EffectiveTransactionTypeName;
                var txnTypeCode = erpLine.EffectiveTransactionTypeCode;
                var actionCode = erpLine.EffectiveActionCode;
                var erpBu = erpLine.BusinessUnit ?? bu;
                var warehouseName = await _presenter.ResolveWarehouseAsync(_currentTripId, erpLine, _cts.Token);
                var orgCode = WeighOperationPresenter.WarehouseOrganizationCode(erpLine.OrganizationCode, warehouseName) ?? "";

                var erpResult = await _presenter.PushOracleTicketAsync(
                    txnTypeName, txnTypeCode, actionCode,
                    shipmentForErp, shipmentForErp,
                    weightInKg, weightOutKg, netKg,
                    (await _presenter.EnsureTicketNumberAsync(_currentTripId!.Value, _cts.Token)) ?? _txtRegNo.Text.Trim(), orgCode, erpBu,
                    _txtRegNo.Text.Trim(),
                    erpLine.ItemNumber, erpLine.ItemDescription ?? erpLine.ItemName, netKg,
                    await _presenter.ResolveTransporterAsync(_currentTripId, erpLine, _cts.Token),
                    _txtPlate.Text.Trim(),
                    _txtDriver.Text.Trim().Length > 0 ? _txtDriver.Text.Trim() : erpLine.DriverName,
                    erpLine.TruckOwner, erpLine.VendorName, erpLine.VendorNumber, erpLine.VendorSiteCode,
                    _cts.Token, warehouseName, note: WeighNote);

                DigiScalePopup.Show(this,
                    erpResult.IsSuccess ? "ĐẨY LẠI ERP THÀNH CÔNG" : "ĐẨY LẠI ERP THẤT BẠI",
                    $"Số đăng tài: {_txtRegNo.Text}\nBiển số: {_txtPlate.Text}\nShipment: {shipmentForErp}\nTrọng lượng: {netKg:N0} kg",
                    erpResult.IsSuccess, erpResult.ErrorMessage ?? "");

                if (erpResult.IsSuccess && _currentTripId.HasValue)
                    try { await _presenter.RecordErpSyncAsync(_currentTripId.Value, "RETRY_PUSH", null); } catch { }
            }
            else
            {
                // ═══ MÍA → đẩy lại đủ chuỗi: lần 1 rồi lần 2 (kèm lấy chính sách), số + giờ lấy từ DB ═══
                var ctx = await _presenter.GetDigiScaleContextAsync(_currentTripId.Value, _cts.Token, ensureTicket: true);
                if (ctx is null || ctx.FirstKg is null || ctx.FirstAt is null)
                {
                    DigiScalePopup.ShowAlert(this, AlertKind.Error, Loc.T("weigh.retry_failed_title"),
                        Loc.T("weigh.retry_no_weigh"), Loc.T("digi.act_weigh_first"));
                    return;
                }

                var facts = new List<(string, string?)>
                {
                    (Loc.T("alert.fact_trip"), ctx.TripNo),
                    (Loc.T("alert.fact_plate"), _txtPlate.Text),
                };

                var (first, maLenhDon) = await PushSugarcaneWeighAsync(ctx.TripId, false, ctx.FirstKg.Value, ctx.FirstAt.Value);
                facts.Add((Loc.T("alert.fact_lenhdon"), maLenhDon));
                facts.Add((Loc.T("alert.fact_weight1"), $"{ctx.FirstKg.Value / 1000m:N3} tấn — "
                    + (first.IsSuccess ? Loc.T("digi.ok") : Loc.T("digi.failed"))));

                TTC.Weighing.Infrastructure.Adapters.TicketResult final = first;
                var isWeighOut = false;

                if (ctx.SecondKg is { } secondKg && ctx.SecondAt is { } secondAt)
                {
                    // Lần 1 lỗi (có thể DigiScale đã có sẵn) vẫn gửi lần 2 — DigiScale sẽ báo CR-04 nếu thật sự thiếu.
                    var (second, _) = await PushSugarcaneWeighAsync(ctx.TripId, true, secondKg, secondAt);
                    facts.Add((Loc.T("alert.fact_weight2"), $"{secondKg / 1000m:N3} tấn — "
                        + (second.IsSuccess ? Loc.T("digi.ok") : Loc.T("digi.failed"))));
                    final = second;
                    isWeighOut = true;

                    if (second.HasPolicy)
                    {
                        _policyTapChatCS = second.TapChatCS;
                        _policyCcsChinhSach = second.CcsChinhSach;
                        _policyChinhSachThuMia = second.ChinhSachThuMia;
                        ShowPaymentFigures(second.ChinhSachThuMia, second.TapChatCS, second.CcsChinhSach,
                            PaymentWeightKg(ctx.FirstKg, secondKg, second.TapChatCS));
                        facts.Add((Loc.T("alert.fact_policy_tc"), $"{second.TapChatCS:N2} %"));
                        facts.Add((Loc.T("alert.fact_policy_ccs"), second.CcsChinhSach?.ToString("N2") ?? Loc.T("weigh.ccs_not_available")));

                        // Đẩy đủ số liệu thanh toán (cùng bước như lúc cân xong).
                        var (paidOk, paidDetail) = await PushPaymentFiguresAsync(ctx.TripId, second);
                        facts.Add((Loc.T("alert.fact_payment"), (paidOk ? Loc.T("digi.ok") : Loc.T("digi.failed")) + " — " + paidDetail));
                        if (!paidOk) final = second with { IsSuccess = false, ErrorMessage = paidDetail };
                    }
                    else if (second.IsNoPolicy)
                    {
                        _policyChinhSachThuMia = Loc.T("weigh.policy_none_short");
                    }
                }

                var allOk = final.IsSuccess && (!isWeighOut || final.HasPolicy) && (isWeighOut || first.IsSuccess);

                // Cột "Đồng bộ" trên lưới: ghi kết quả đẩy lại.
                try { await _presenter.RecordDigiScaleSyncAsync(ctx.TripId, "RETRY_PUSH", allOk); }
                catch { /* chỉ là trạng thái hiển thị */ }
                await LoadWeighedTripsAsync();

                if (allOk)
                {
                    DigiScalePopup.ShowAlert(this, AlertKind.Success, Loc.T("weigh.retry_ok_title"), facts: facts);
                }
                else
                {
                    // Lỗi cần báo: lượt lỗi cuối cùng (lần 2 nếu có, không thì lần 1).
                    var failed = isWeighOut && !final.IsSuccess ? final : !first.IsSuccess ? first : final;
                    DigiScalePopup.ShowError(this, Loc.T("weigh.retry_failed_title"),
                        DescribePush(failed, isWeighOut && ReferenceEquals(failed, final)), facts);
                }
            }
        }
        catch (Exception ex)
        {
            DigiScalePopup.ShowError(this, Loc.T("weigh.retry_failed_title"), DigiScaleErrors.Describe(ex),
                [(Loc.T("alert.fact_trip"), _txtRegNo.Text), (Loc.T("alert.fact_plate"), _txtPlate.Text)]);
        }
        finally { SetBusy(false); }
    }

    /// <summary>Helper record cho ComboBox hiển thị vai trò cân.</summary>
    private sealed record ReweighRoleItem(string DisplayText, WeightRole Role)
    {
        public override string ToString() => DisplayText;
    }

    /// <summary>
    /// Nhập tay số cân khi thiết bị sự cố (Req 11.1–11.5).
    /// Hiển thị dialog nhập: số cân, đơn vị, vai trò, lý do (bắt buộc), phê duyệt (bắt buộc).
    /// Gọi presenter.ManualWeightAsync. Server từ chối nếu Trip đang Paused hoặc có Unknown.
    /// Ghi audit log đầy đủ (server-side trong ApproveManualWeightHandler).
    /// </summary>
    private async void OnManualWeightClicked(object? sender, EventArgs e)
    {
        if (_currentTripId is null)
        {
            Helpers.AppMessageBox.Show(Loc.T("weigh.manual_no_trip"), Loc.T("common.no_data"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // ── Dialog nhập tay: số cân, đơn vị, vai trò cân, lý do bắt buộc, mã phê duyệt bắt buộc ──
        using var dialog = new Form
        {
            Text = Loc.T("weigh.manual_title"),
            Size = new Size(800, 680),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = AgrisTheme.Canvas,
            Padding = new Padding(16),
            Font = AgrisTheme.Body(12F)
        };

        var table = FormLayout.FieldTable();

        var numWeight = new NumericUpDown
        {
            Minimum = 0,
            Maximum = IsUnitTon ? 999_999m : 999_999_999m,
            // Tấn nhập được 3 số lẻ (14,200 tấn = 14.200 kg) — trước đây 0 số lẻ nên tự làm tròn số chẵn.
            DecimalPlaces = IsUnitTon ? 3 : 0,
            Increment = IsUnitTon ? 0.1m : 100m,
            Dock = DockStyle.Fill,
            Font = AgrisTheme.Body(12F, FontStyle.Bold),
            AccessibleName = Loc.T("weigh.manual_field_weight")
        };

        var cmbUnit = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Fill,
            Font = AgrisTheme.Body(11F),
            AccessibleName = Loc.T("weigh.manual_field_unit")
        };
        cmbUnit.Items.AddRange(["kg", Loc.T("weigh.unit_ton")]);
        // Mía = đơn vị tấn mặc định, vật tư = kg
        cmbUnit.SelectedIndex = _currentSourceSystem != "ERP" ? 1 : 0;
        // Đổi đơn vị → đổi số lẻ cho phép (tấn 3 số lẻ, kg số nguyên)
        cmbUnit.SelectedIndexChanged += (_, _) =>
        {
            var isTon = cmbUnit.SelectedIndex == 1;
            numWeight.DecimalPlaces = isTon ? 3 : 0;
            numWeight.Increment = isTon ? 0.1m : 100m;
        };

        var cmbRole = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Fill,
            Font = AgrisTheme.Body(11F),
            AccessibleName = Loc.T("weigh.manual_field_role")
        };
        cmbRole.Items.AddRange([
            new ReweighRoleItem(Loc.T("weigh.reweigh_role_gross"), WeightRole.Gross),
            new ReweighRoleItem(Loc.T("weigh.reweigh_role_tare"), WeightRole.Tare)
        ]);
        cmbRole.SelectedIndex = _currentSegmentIndex > 0 ? 1 : 0;

        var txtReason = new TextBox
        {
            Multiline = true,
            Height = 80,
            Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Vertical,
            Font = AgrisTheme.Body(11F),
            PlaceholderText = Loc.T("weigh.manual_reason_placeholder"),
            AccessibleName = Loc.T("weigh.manual_field_reason")
        };

        var txtApprovalRef = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = AgrisTheme.Body(11F),
            PlaceholderText = Loc.T("weigh.manual_approval_placeholder"),
            AccessibleName = Loc.T("weigh.manual_field_approval")
        };

        FormLayout.AddField(table, Loc.T("weigh.manual_field_weight"), numWeight);
        FormLayout.AddField(table, Loc.T("weigh.manual_field_unit"), cmbUnit);
        FormLayout.AddField(table, Loc.T("weigh.manual_field_role"), cmbRole);
        FormLayout.AddField(table, Loc.T("weigh.manual_field_reason"), txtReason);
        FormLayout.AddField(table, Loc.T("weigh.manual_field_approval"), txtApprovalRef);

        var notice = GridBuilder.Notice(Loc.T("weigh.manual_notice"));
        FormLayout.AddFullWidth(table, notice);

        var btnOk = GridBuilder.PrimaryButton(Loc.T("weigh.manual_submit"), new Size(140, 36));
        var btnCancel = GridBuilder.SecondaryButton(Loc.T("common.cancel"), new Size(80, 36));

        btnOk.DialogResult = DialogResult.OK;
        btnCancel.DialogResult = DialogResult.Cancel;
        dialog.AcceptButton = btnOk;
        dialog.CancelButton = btnCancel;

        var actionBar = FormLayout.ActionBar(btnOk, btnCancel);
        FormLayout.AddFullWidth(table, actionBar, FormLayout.ActionRowHeight);

        dialog.Controls.Add(table);

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        // ── Validate — tạm bỏ ràng buộc lý do + phê duyệt ──
        var reason = txtReason.Text.Trim();
        var approvalRef = txtApprovalRef.Text.Trim();

        if (numWeight.Value <= 0)
        {
            Helpers.AppMessageBox.Show(Loc.T("weigh.manual_weight_required"), Loc.T("weigh.manual_weight_invalid"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var rawWeight = numWeight.Value;
        var rawUom = cmbUnit.SelectedItem!.ToString()!;

        // Chuyển sang kg nếu nhập tấn
        var weightKg = rawUom == "kg" ? rawWeight : rawWeight * 1000m;

        // Nhập tay số cân bắt buộc user quản lý xác nhận (user + mật khẩu).
        var approval = ApprovalDialog.Request(this, ApprovalLevel.Manager,
            Loc.T("approval.action_manual_weight",
                _txtPlate.Text, rawUom == "kg" ? rawWeight.ToString("N0") : rawWeight.ToString("N3"), rawUom));
        if (approval is null)
        {
            _lblQrMessage.Text = Loc.T("approval.cancelled");
            _lblQrMessage.ForeColor = AgrisTheme.Warning;
            return;
        }

        // Ghi chú do người cân tự gõ — KHÔNG tự điền người duyệt / lý do nhập tay vào ô Ghi chú.
        _ = reason;
        _ = approvalRef; // Mã phê duyệt nhập tay cũ được thay bằng xác thực user quản lý.

        // Set vào simulated scale — hiện lên display, user bấm Xác nhận (F5) để chốt
        if (_simulatedScale is not null)
        {
            _simulatedScale.SetWeight(weightKg);
            _lblQrMessage.Text = Loc.T("weigh.manual_set_hint",
                rawUom == "kg" ? rawWeight.ToString("N0") : rawWeight.ToString("N3"), rawUom, approval.UserName);
            _lblQrMessage.ForeColor = AgrisTheme.Warning;
        }
        else
        {
            Helpers.AppMessageBox.Show(Loc.T("weigh.manual_no_simulated"), Loc.T("common.error"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// Tiếp tục (F7) — chuyển sang tab tiếp theo cho chuyến đang chọn.
    /// Sau cân lần 1 → Tạp chất (UI-05). Sau cân lần 2 → Cổng ra (UI-11).
    /// </summary>
    private void OnContinueClicked(object? sender, EventArgs e)
    {
        if (_currentTripId is null)
        {
            Helpers.AppMessageBox.Show(Loc.T("common.no_trip_continue"),
                Loc.T("common.missing_info"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _eventBus.Publish(new TripChangedEventArgs
        {
            TripId = _currentTripId.Value,
            TripNo = _txtRegNo.Text,
            Plate = _txtPlate.Text,
            QrContent = _lastQrContent,
            ChangeType = _currentSegmentIndex == 0
                ? TripChangeType.FirstWeighCommitted
                : TripChangeType.SecondWeighCommitted,
            SourceForm = "WeighOperationForm"
        });
    }

    private void OnResetClicked(object? sender, EventArgs e)
    {
        StopTimers();
        _sessionId = null;
        _currentTripId = null;
        _currentTripVersion = 0;
        _currentSourceSystem = null;
        _display.DisplayAsTon = true; // Reset về mặc định tấn
        _firstWeighKg = null;
        UpdateWeightUnitLabels();
        _currentFrmLenhDonId = null;
        _currentFrmMaLenhDon = null;
        _currentFrmLenhDonBu = null;
        _currentWeighbridgeId = Guid.Empty;
        _currentLaneId = Guid.Empty;
        _currentCheckpoint = default;
        _currentSegmentIndex = 0;
        _viewingCompleted = false;
        _isWeighOutMode = false;
        UpdateWeighModeButton();
        _policyCcsChinhSach = null;
        _policyTapChatCS = null;
        _policyChinhSachThuMia = null;
        _currentQrIsTemp = false;
        _prePushedSessionId = null;
        _prePushedResult = null;
        _prePushedKg = null;
        _ticketPrinted = false;

        // Reset tất cả fields
        foreach (var ctrl in new[]
        {
            _txtRegNo, _txtPlate, _txtTicketNo,
            _txtCustomer, _txtCommodity, _txtWarehouse,
            _txtShipment,
            _txtDirection, _txtErpTripNumber, _txtTransporter, _txtDriver,
            _txtWeightIn, _txtTimeIn, _txtWeightOut, _txtTimeOut,
            _txtNetWeight, _txtTolerance
        })
        {
            ctrl.Clear();
        }
        ApplyFlowLayout(); // chưa có chuyến → theo tab Mía/Vật tư đang chọn
        ShowPaymentFigures(null, null, null, null);
        _lastScan = null;
        _btnTripMore.Enabled = false;
        _btnPolicyPreview.Enabled = false;
        _secondWeighKgForPolicy = null;

        _lblToleranceRange.Text = string.Empty;
        _lblQrMessage.Text = Loc.T("weigh.qr_start");
        _lblQrMessage.ForeColor = AgrisTheme.BrandGreen;
        _lblCcsWarning.Text = string.Empty;
        _lblCcsWarning.Visible = false;

        // Reset radio buttons loại mía (giữ để tránh lỗi tham chiếu, panel luôn ẩn)
        _rbMiaCay.Checked = false;
        _rbMiaKhuc.Checked = false;
        _rbMiaChay.Checked = false;
        _rbMiaOpt4.Checked = false;
        _rbMiaOpt5.Checked = false;
        _txtWeighNotes.Clear();
        _simulatedScale?.Clear();
        _display.Update(null, "kg", ScaleDeviceStatus.Unknown, false, TimeSpan.Zero, null);
        _checklist.Reset();
        _btnSave.Enabled = false;
        _btnPrint.Enabled = false;
        _btnSync.Enabled = false;
        _btnManualWeight.Enabled = false;
        _txtBarcode.Enabled = true;
        _txtBarcode.Clear();
        _txtBarcode.Focus();
    }

    private EvaluateAndCommitWeightCommand BuildCommand(bool dryRun) => new(
        SessionId: _sessionId!.Value,
        LaneId: _currentLaneId,
        CorrelationId: Guid.NewGuid().ToString("N"),
        IdempotencyKey: $"{_sessionId}:{_currentCheckpoint}",
        DryRun: dryRun);

    // ═══════════════════════════════════════════════════════════
    //  Lưới xe đã cân — IWeighOperationQueries (Req 1.2, 8.6)
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Tải danh sách xe đã cân từ IWeighOperationQueries qua presenter.
    /// Filter theo ngày (dtpFrom, dtpTo) và tab (Cân vào / Cân cả vào và ra).
    /// </summary>
    private async Task LoadWeighedTripsAsync()
    {
        await _dbLock.WaitAsync(_cts.Token);
        try
        {
            var siteId = _siteContext.CurrentSiteId;

            // Mode 0: Chờ cân — query trips chưa cân từ Dashboard
            if (_gridMode == 0)
            {
                _allWeighedRows = await _presenter.LoadPendingTripsAsync(siteId, _gridCategory.Category, _cts.Token);
                BindCurrentPage();
                return;
            }

            // Mode 1/2: Cân vào / Cân cả vào và ra
            var weighbridgeId = _currentWeighbridgeId != Guid.Empty
                ? _currentWeighbridgeId
                : await _presenter.GetDefaultWeighbridgeAsync(_cts.Token) ?? Guid.Empty;

            var isMia = _gridCategory.Category != TripCategory.Material;
            if (!isMia && _gridMode == GRID_MODE_IMPURITY) { _gridMode = 1; UpdateGridTabStyles(); } // vật tư không đo tạp chất

            var filter = new WeighedTripFilter
            {
                FromUtc = ToUtcStartOfDay(_dtpFrom.Value),
                ToUtc = ToUtcStartOfDay(_dtpTo.Value.AddDays(1)),
                FirstWeightOnly = _gridMode is 1 or GRID_MODE_IMPURITY,
                CompletedOnly = _gridMode == 2,
                Category = _gridCategory.Category
            };

            var rows = await _presenter.LoadWeighedTripsAsync(siteId, weighbridgeId, filter, _cts.Token);
            // Mía đã cân vào: chưa có tạp chất → tab "Đo tạp chất"; có rồi → "Chờ cân ra".
            if (isMia && _gridMode == GRID_MODE_IMPURITY)
                rows = rows.Where(r => r.ImpurityActual is null).ToList();
            else if (isMia && _gridMode == 1)
                rows = rows.Where(r => r.ImpurityActual is not null).ToList();
            _allWeighedRows = rows;
            _gridFingerprint = GridFingerprint(rows);
            BindCurrentPage();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // Lỗi tải lưới không ảnh hưởng thao tác cân — chỉ log và hiển thị lưới trống.
            System.Diagnostics.Debug.WriteLine($"Lỗi tải lưới xe đã cân: {ex.Message}");
        }
        finally
        {
            _dbLock.Release();
        }
    }

    private async void OnTabPendingClicked(object? sender, EventArgs e)
    {
        _gridMode = 0;
        _pager.Reset();
        UpdateGridTabStyles();
        await LoadWeighedTripsAsync();
    }

    private async void OnTabWeighInClicked(object? sender, EventArgs e)
    {
        _gridMode = 1;
        _pager.Reset();
        UpdateGridTabStyles();
        await LoadWeighedTripsAsync();
    }

    private async void OnTabBothClicked(object? sender, EventArgs e)
    {
        _gridMode = 2;
        _pager.Reset();
        UpdateGridTabStyles();
        await LoadWeighedTripsAsync();
    }

    /// <summary>Dấu vân tay danh sách (chuyến + tạp chất + số cân) — chỉ vẽ lại lưới khi thật sự đổi.</summary>
    private static string GridFingerprint(IEnumerable<WeighedTripRow> rows)
        => string.Join('|', rows.Select(r => $"{r.RegNo}:{r.ImpurityActual}:{r.WeightIn}:{r.WeightOut}:{r.SyncStatus}"));

    /// <summary>
    /// Làm mới lưới mỗi 1,5s khi đang xem tab Đo tạp chất / Chờ cân ra: trạm tạp chất ở máy khác ghi kết quả
    /// thì xe chuyển tab gần như ngay (cùng máy thì nhận qua event bus, không chờ timer).
    /// Bỏ lượt khi form đang bận (đang cân / đang tải) — không tranh DbContext với thao tác cân.
    /// </summary>
    private async Task GridLiveTickAsync()
    {
        if (_gridLiveBusy || IsDisposed || !Visible) return;
        if (_gridMode is not (1 or GRID_MODE_IMPURITY) || _gridCategory.Category == TripCategory.Material) return;
        if (_isBusy || _dbLock.CurrentCount == 0) return;

        _gridLiveBusy = true;
        try
        {
            await _dbLock.WaitAsync(_cts.Token);
            IReadOnlyList<WeighedTripRow> rows;
            try
            {
                var weighbridgeId = _currentWeighbridgeId != Guid.Empty
                    ? _currentWeighbridgeId
                    : await _presenter.GetDefaultWeighbridgeAsync(_cts.Token) ?? Guid.Empty;
                var all = await _presenter.LoadWeighedTripsAsync(_siteContext.CurrentSiteId, weighbridgeId,
                    new WeighedTripFilter
                    {
                        FromUtc = ToUtcStartOfDay(_dtpFrom.Value),
                        ToUtc = ToUtcStartOfDay(_dtpTo.Value.AddDays(1)),
                        FirstWeightOnly = true,
                        Category = _gridCategory.Category
                    }, _cts.Token);
                rows = (_gridMode == GRID_MODE_IMPURITY
                    ? all.Where(r => r.ImpurityActual is null)
                    : all.Where(r => r.ImpurityActual is not null)).ToList();
            }
            finally { _dbLock.Release(); }

            var fp = GridFingerprint(rows);
            if (fp == _gridFingerprint) return; // không đổi → không vẽ lại (không nháy, không mất dòng chọn)
            _gridFingerprint = fp;
            _allWeighedRows = rows;
            BindCurrentPage();
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Làm mới lưới: {ex.Message}"); }
        finally { _gridLiveBusy = false; }
    }

    private void UpdateGridTabStyles()
    {
        _btnTabImpurity.Visible = _gridCategory.Category != TripCategory.Material;
        _btnTabImpurity.BackColor = _gridMode == GRID_MODE_IMPURITY ? AgrisTheme.BrandGreen : AgrisTheme.Surface;
        _btnTabImpurity.ForeColor = _gridMode == GRID_MODE_IMPURITY ? AgrisTheme.TextOnBrand : AgrisTheme.TextPrimary;
        _btnTabPending.BackColor = _gridMode == 0 ? AgrisTheme.BrandGreen : AgrisTheme.Surface;
        _btnTabPending.ForeColor = _gridMode == 0 ? AgrisTheme.TextOnBrand : AgrisTheme.TextPrimary;
        _btnTabWeighIn.BackColor = _gridMode == 1 ? AgrisTheme.BrandGreen : AgrisTheme.Surface;
        _btnTabWeighIn.ForeColor = _gridMode == 1 ? AgrisTheme.TextOnBrand : AgrisTheme.TextPrimary;
        _btnTabBoth.BackColor = _gridMode == 2 ? AgrisTheme.BrandGreen : AgrisTheme.Surface;
        _btnTabBoth.ForeColor = _gridMode == 2 ? AgrisTheme.TextOnBrand : AgrisTheme.TextPrimary;
    }

    /// <summary>Click dòng trong lưới → nếu tab "Chờ cân", auto scan TripNo để load lên form.</summary>
    private async void OnWeighGridCellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;

        var row = _dgvWeighed.Rows[e.RowIndex];
        var tripNo = row.Cells["RegNo"]?.Value?.ToString();
        if (string.IsNullOrWhiteSpace(tripNo)) return;

        // Tab "Đã cân đủ" (2): populate form từ grid data trực tiếp (không scan QR)
        if (_gridMode == 2)
        {
            PopulateFromCompletedRow(row);
            return;
        }

        // Tab "Chờ cân vào" (0) / "Chờ cân ra" (1): scan QR → mở session
        await AutoScanQrAsync(tripNo);
    }

    /// <summary>Điền thông tin từ dòng xe đã cân đủ lên form — để xem/in phiếu lại.</summary>
    private void PopulateFromCompletedRow(DataGridViewRow row)
    {
        // Lấy data từ grid columns
        var regNo = row.Cells["RegNo"]?.Value?.ToString() ?? "";
        var ticketNo = row.Cells["TicketNo"]?.Value?.ToString() ?? "";
        var plate = row.Cells["Plate"]?.Value?.ToString() ?? "";
        var customer = row.Cells["Customer"]?.Value?.ToString() ?? "";
        var commodity = row.Cells["Commodity"]?.Value?.ToString() ?? "";

        // Luồng theo tab Mía/Vật tư của lưới (lưới đã lọc theo nguồn FRM/ERP) — không đoán theo tên hàng.
        var isMia = _gridCategory.Category != TripCategory.Material;
        _currentSourceSystem = isMia ? "FRM" : "ERP";
        _display.DisplayAsTon = isMia;
        _viewingCompleted = true; // khung Trọng lượng hiện đủ vào / ra / thanh toán
        UpdateWeightUnitLabels();
        ApplyFlowLayout();

        _txtRegNo.Text = regNo;
        _txtTicketNo.Text = ticketNo;
        _txtPlate.Text = plate;
        _txtCustomer.Text = customer;
        _txtCommodity.Text = commodity;

        // Shipment từ grid
        var shipment = row.Cells["ShipmentNo"]?.Value?.ToString() ?? "";
        _txtShipment.Text = shipment;

        // Grid data luôn ở tấn — vật tư cần convert ngược sang kg
        decimal ConvertFromGrid(decimal gridTon) => isMia ? gridTon : gridTon * 1000m;
        string FormatFromGrid(decimal gridTon) => isMia ? $"{gridTon:N3}" : $"{ConvertFromGrid(gridTon):N0}";
        _firstWeighKg = row.Cells["WeightIn"]?.Value is decimal firstTon ? firstTon * 1000m : null;

        if (row.Cells["WeightIn"]?.Value is decimal wiTon)
            _txtWeightIn.Text = FormatFromGrid(wiTon);
        if (row.Cells["TimeIn"]?.Value is DateTimeOffset tiDto)
            _txtTimeIn.Text = tiDto.ToVnTime().ToString("dd/MM/yyyy HH:mm:ss");
        if (row.Cells["WeightOut"]?.Value is decimal woTon)
            _txtWeightOut.Text = FormatFromGrid(woTon);
        if (row.Cells["TimeOut"]?.Value is DateTimeOffset toDto)
            _txtTimeOut.Text = toDto.ToVnTime().ToString("dd/MM/yyyy HH:mm:ss");
        if (row.Cells["NetWeight"]?.Value is decimal netTon)
        {
            _txtNetWeight.Text = FormatFromGrid(netTon);
            _txtNetWeight.Font = new Font(_txtNetWeight.Font, FontStyle.Bold);
        }

        // Lấy TripId từ grid data (nếu có)
        if (row.DataBoundItem is WeighedTripRow tripRow)
            _currentTripId = tripRow.TripId;

        // Xóa trạng thái của chuyến trước — tránh đẩy/in nhầm lệnh đốn, chính sách của chuyến khác.
        // Lệnh đốn khi đẩy/in được đọc lại từ DB theo _currentTripId.
        _currentFrmLenhDonId = null;
        _currentFrmMaLenhDon = null;
        _currentFrmLenhDonBu = null;
        _policyTapChatCS = null;
        _policyCcsChinhSach = null;
        _policyChinhSachThuMia = null;
        _currentQrIsTemp = false;
        _prePushedSessionId = null;
        _prePushedResult = null;
        _prePushedKg = null;
        ShowPaymentFigures(null, null, null, null);
        _lastScan = null;
        _btnTripMore.Enabled = _currentTripId is not null;
        _btnPolicyPreview.Enabled = false;

        // Vật tư: bổ sung Số TRIP / Hướng / NVC / Kho / Lái xe từ ERP như màn Đăng tài.
        _txtDirection.Clear();
        _txtTransporter.Clear();
        _txtDriver.Clear();
        _txtErpTripNumber.Text = shipment;
        if (!isMia)
            _ = LoadMaterialDetailsAsync(_currentTripId, shipment);

        // Chuyến đã cân: hiện lại chính sách đã bắt (không gọi FRM lại).
        _secondWeighKgForPolicy = row.Cells["WeightOut"]?.Value is decimal outTon ? outTon * 1000m : null;
        if (isMia) _ = LoadOrPreviewPolicyAsync(allowPreview: false);

        // Bật In phiếu + Đẩy lại Digi
        _btnPrint.Enabled = true;
        _btnSync.Enabled = true;
        _ticketPrinted = true;

        _lblQrMessage.Text = $"📋 Xem chuyến {regNo} — bấm In phiếu (F6) hoặc {_btnSync.Text.Replace("🔄 ", "")}";
        _lblQrMessage.ForeColor = AgrisTheme.Info;
    }

    private async void OnDateFilterChanged(object? sender, EventArgs e)
    {
        _pager.Reset();
        await LoadWeighedTripsAsync();
    }

    // ═══════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════

    private void StopTimers()
    {
        _signalTimer.Stop();
        _conditionTimer.Stop();
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        _btnSave.Enabled = !busy && _sessionId is not null;
        _btnPrint.Enabled = !busy && _btnPrint.Enabled;
        _btnSync.Enabled = !busy && _btnSync.Enabled;
        _btnManualWeight.Enabled = !busy && _currentTripId is not null;
        _btnReset.Enabled = !busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    private static void ShowError(string title, Exception ex)
    {
        // Lỗi EF chỉ ghi "See the inner exception" — hiện nguyên nhân gốc (lỗi DB cụ thể).
        var root = ex;
        while (root.InnerException is not null) root = root.InnerException;
        var detail = ReferenceEquals(root, ex) ? ex.Message : $"{ex.Message}{Environment.NewLine}→ {root.Message}";
        Helpers.AppMessageBox.Show($"{title}.{Environment.NewLine}{Environment.NewLine}{detail}",
            Loc.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private static TextBox ReadOnlyField(string name)
    {
        var txt = new TextBox
        {
            ReadOnly = true,
            BackColor = AgrisTheme.ReadOnlyBg,
            ForeColor = AgrisTheme.ReadOnlyText,
            BorderStyle = BorderStyle.FixedSingle,
            TabStop = false,
            Cursor = Cursors.Arrow,
            AccessibleName = name
        };
        // Ngăn focus vào ô — click vào sẽ tự chuyển focus sang control kế tiếp
        txt.Enter += (_, _) => txt.Parent?.SelectNextControl(txt, true, true, true, true);
        return txt;
    }

    /// <summary>
    /// Xử lý event từ form khác — refresh lưới xe cân khi có thay đổi liên quan.
    /// </summary>
    private async void OnTripChangedFromOtherForm(object? sender, TripChangedEventArgs e)
    {
        if (InvokeRequired)
        {
            Invoke(() => OnTripChangedFromOtherForm(sender, e));
            return;
        }

        // Refresh lưới xe cân khi có xe mới đăng tài hoặc thay đổi trạng thái
        try
        {
            await LoadWeighedTripsAsync();
        }
        catch { /* Lỗi tạm — không crash form */ }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _eventBus.TripChanged -= OnTripChangedFromOtherForm;
        _gridLiveTimer.Stop();
        _gridLiveTimer.Dispose();
        StopTimers();
        _deviceTimer.Stop();
        _cts.Cancel();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _signalTimer.Dispose();
            _conditionTimer.Dispose();
            _deviceTimer.Dispose();
            _cts.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>
    /// Chuyển ngày local sang UTC cho bộ lọc.
    /// DateTimePicker trả DateTime Kind = Local nên phải quy đổi bằng offset của chính ngày đó.
    /// </summary>
    private static DateTimeOffset ToUtcStartOfDay(DateTime localDate)
    {
        var startOfDay = DateTime.SpecifyKind(localDate.Date, DateTimeKind.Unspecified);
        var offset = VnTime.OffsetAt(startOfDay);
        return new DateTimeOffset(startOfDay, offset).ToUniversalTime();
    }
}
