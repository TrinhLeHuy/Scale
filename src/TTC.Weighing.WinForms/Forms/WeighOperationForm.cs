using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.Queries;
using TTC.Weighing.Application.UseCases.Integration;
using TTC.Weighing.Application.UseCases.Weighing;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.WinForms.Controls;
using TTC.Weighing.WinForms.Helpers;
using TTC.Weighing.WinForms.Presenters;
using TTC.Weighing.WinForms.Services;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Theme;

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
        Font = new Font(AgrisTheme.FontFamily, 11F),
        AccessibleName = "Barcode QR input"
    };
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
    private readonly Button _btnSync = GridBuilder.SecondaryButton(Loc.T("weigh.btn_sync"), new Size(110, 36));
    private readonly Button _btnReset = GridBuilder.SecondaryButton(Loc.T("weigh.btn_reset"), new Size(120, 36));

    // ── Nút cân lại (chỉ hiện khi có quyền weighing.reweigh.request — Req 10.1) ──
    private readonly Button _btnReweigh = GridBuilder.SecondaryButton(Loc.T("weigh.btn_reweigh"), new Size(110, 36));

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
    private Panel _pnlSugarcaneType = null!;

    // ── Lưới xe đã cân (panel dưới) ──
    private readonly DataGridView _dgvWeighed = GridBuilder.ReadOnlyGrid(Loc.T("weigh.section_grid"));
    private readonly Button _btnTabPending = GridBuilder.SecondaryButton(Loc.T("weigh.tab_pending"), new Size(100, 28));
    private readonly Button _btnTabWeighIn = GridBuilder.PrimaryButton(Loc.T("weigh.tab_weigh_in"), new Size(100, 28));
    private readonly Button _btnTabBoth = GridBuilder.SecondaryButton(Loc.T("weigh.tab_both"), new Size(140, 28));
    private readonly DateTimePicker _dtpFrom = new() { Format = DateTimePickerFormat.Short, Width = 120, AccessibleName = Loc.T("reg.filter_from") };
    private readonly DateTimePicker _dtpTo = new() { Format = DateTimePickerFormat.Short, Width = 120, AccessibleName = Loc.T("reg.filter_to") };

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

    // ── Camera AI ──
    private readonly ICameraAiService _cameraAiService;
    private readonly Development.SimulatedCameraAiService? _simulatedCameraAi;
    private readonly CameraAiStatusControl _cameraAiStatus = new();

    // ── Event bus ──
    private readonly TripEventBus _eventBus;
    private Guid? _currentTripId;
    private int _currentTripVersion;
    private Guid _currentWeighbridgeId;
    private Guid _currentLaneId;
    private CheckpointType _currentCheckpoint;
    private int _currentSegmentIndex;
    private Guid? _sessionId;
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
        TripEventBus eventBus)
    {
        _presenter = presenter;
        _siteContext = siteContext;
        _currentUser = currentUser;
        _simulatedScale = scaleSignalGateway as Development.SimulatedScaleSignalGateway;
        _cameraAiService = cameraAiService;
        _simulatedCameraAi = cameraAiService as Development.SimulatedCameraAiService;
        _eventBus = eventBus;

        Text = Loc.T("weigh.title");
        MinimumSize = new Size(1100, 720);
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
        _btnReset.Click += OnResetClicked;
        _btnReweigh.Click += OnReweighClicked;
        _btnManualWeight.Click += OnManualWeightClicked;
        _txtBarcode.KeyDown += OnBarcodeKeyDown;

        // Nút In và Sync chỉ bật sau cân lần 2 thành công (Req 4.7)
        _btnPrint.Enabled = false;
        _btnSync.Enabled = false;

        // Nút Cân lại chỉ hiện khi có quyền weighing.reweigh.request (Req 10.1)
        _btnReweigh.Visible = _currentUser.HasPermission(
            RequestReweighHandler.RequiredPermission, _siteContext.CurrentSiteId);
        _btnReweigh.Enabled = false;

        // Nút Nhập tay chỉ hiện khi có quyền weighing.weight.manual (Req 11.1)
        _btnManualWeight.Visible = _currentUser.HasPermission(
            ApproveManualWeightHandler.RequiredPermission, _siteContext.CurrentSiteId);
        _btnManualWeight.Enabled = false;

        _cameraIn.ApplyPositionLabel();
        _cameraOut.ApplyPositionLabel();

        // Tab filter và date picker → reload lưới xe đã cân (Req 1.2, 8.6)
        _btnTabPending.Click += OnTabPendingClicked;
        _btnTabWeighIn.Click += OnTabWeighInClicked;
        _btnTabBoth.Click += OnTabBothClicked;
        _dtpFrom.ValueChanged += OnDateFilterChanged;
        _dtpTo.ValueChanged += OnDateFilterChanged;

        // Không tự động mở phiên khi Load — chờ nhân viên quét QR.
        // Bắt đầu poll trạng thái thiết bị ngay khi mở form (Req 13.1).
        // Tải lưới xe đã cân hôm nay (Req 1.2).
        Load += async (_, _) =>
        {
            _deviceTimer.Start();
            _txtBarcode.Focus();
            await LoadWeighedTripsAsync();
        };

        // Tạm dừng timer khi tab không hiển thị để giảm tải CPU/IO.
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
        var hasSimBar = _simulatedScale is not null;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = hasSimBar ? 5 : 4,
            Padding = new Padding(8, 6, 8, 0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 65));   // Top: trip info + weight + camera
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, FormLayout.ActionRowHeight));   // Action bar
        if (hasSimBar)
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));   // Sim bar
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 35));   // Grid
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));   // Device status bar

        var row = 0;
        root.Controls.Add(BuildTopArea(), 0, row++);
        root.Controls.Add(BuildActionBar(), 0, row++);

        if (hasSimBar)
            root.Controls.Add(BuildSimulationBar(), 0, row++);

        root.Controls.Add(BuildGridArea(), 0, row++);
        root.Controls.Add(BuildDeviceStatusBar(), 0, row);

        Controls.Add(root);
    }

    /// <summary>
    /// Thanh giả lập cân xe — chỉ hiện khi chạy Preview mode.
    /// Cho phép demo thay đổi trọng lượng mà không cần thiết bị thật.
    /// </summary>
    private Panel BuildSimulationBar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = AgrisTheme.WarningSoft,
            Padding = new Padding(6, 3, 6, 3)
        };

        var lbl = new Label
        {
            Text = Loc.T("weigh.sim_label"),
            AutoSize = true,
            Font = AgrisTheme.Body(9F, FontStyle.Bold),
            ForeColor = AgrisTheme.Warning,
            Margin = new Padding(0, 8, 6, 0)
        };

        var btnEmpty = GridBuilder.SecondaryButton(Loc.T("weigh.sim_empty"), new Size(130, 30));
        btnEmpty.Click += (_, _) => _simulatedScale!.SetWeight(0m);

        var btn15 = GridBuilder.SecondaryButton(Loc.T("weigh.sim_15t"), new Size(110, 30));
        btn15.Click += (_, _) => _simulatedScale!.SetWeight(15_000m);

        var btn30 = GridBuilder.SecondaryButton(Loc.T("weigh.sim_30t"), new Size(120, 30));
        btn30.Click += (_, _) => _simulatedScale!.SetWeight(30_050m);

        var btn45 = GridBuilder.SecondaryButton(Loc.T("weigh.sim_45t"), new Size(120, 30));
        btn45.Click += (_, _) => _simulatedScale!.SetWeight(45_200m);

        var nudCustom = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 80_000,
            DecimalPlaces = 3,
            Increment = 100,
            Width = 120,
            Value = 30_050,
            Margin = new Padding(8, 4, 4, 0),
            AccessibleName = Loc.T("weigh.sim_custom_label")
        };
        var btnCustom = GridBuilder.SecondaryButton(Loc.T("weigh.sim_custom"), new Size(80, 30));
        btnCustom.Click += (_, _) => _simulatedScale!.SetWeight(nudCustom.Value);

        bar.Controls.AddRange([lbl, btnEmpty, btn15, btn30, btn45, nudCustom, btnCustom]);

        return bar;
    }

    private TableLayoutPanel BuildTopArea()
    {
        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));  // Trip info
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));  // Weight display
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 10));  // Camera Vào
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 10));  // Camera Ra
        top.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        top.Controls.Add(BuildTripInfoPanel(), 0, 0);
        top.Controls.Add(BuildWeightPanel(), 1, 0);

        // Camera Vào — cột riêng 10%
        var camInCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(1) };
        _cameraIn.Dock = DockStyle.Fill;
        camInCard.Controls.Add(_cameraIn);
        camInCard.Controls.Add(GridBuilder.SectionHeader("Cam Vào"));
        top.Controls.Add(camInCard, 2, 0);

        // Camera Ra — cột riêng 10%
        var camOutCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(1) };
        _cameraOut.Dock = DockStyle.Fill;
        camOutCard.Controls.Add(_cameraOut);
        camOutCard.Controls.Add(GridBuilder.SectionHeader("Cam Ra"));
        top.Controls.Add(camOutCard, 3, 0);

        return top;
    }

    /// <summary>Panel trái: thông tin chuyến — tự động điền khi quét QR.</summary>
    private Card BuildTripInfoPanel()
    {
        var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(1), AutoScroll = true };
        var header = GridBuilder.SectionHeader(Loc.T("weigh.section_trip"));
        var table = FormLayout.FieldTable();

        FormLayout.AddFullWidth(table, _txtBarcode);
        FormLayout.AddField(table, Loc.T("field.trip_no"), _txtRegNo);
        FormLayout.AddField(table, Loc.T("field.plate"), _txtPlate);
        FormLayout.AddField(table, Loc.T("field.customer"), _txtCustomer);
        FormLayout.AddField(table, Loc.T("field.commodity"), _txtCommodity);
        FormLayout.AddField(table, Loc.T("field.warehouse"), _txtWarehouse);
        FormLayout.AddField(table, Loc.T("field.shipment"), _txtShipment);

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
        FormLayout.AddFullWidth(table, _pnlSugarcaneType, 38);

        // Ghi chú cân
        FormLayout.AddField(table, "Ghi chú", _txtWeighNotes);

        FormLayout.AddFullWidth(table, _lblBarcodeStatus);

        card.Controls.Add(table);
        card.Controls.Add(header);
        return card;
    }

    /// <summary>Panel giữa: số cân realtime + checklist điều kiện + thông tin cân vào/ra + dung sai.</summary>
    private Card BuildWeightPanel()
    {
        var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(1), AutoScroll = true };
        var header = GridBuilder.SectionHeader(Loc.T("weigh.section_weight"));

        var table = FormLayout.FieldTable();
        FormLayout.AddField(table, Loc.T("field.weight_in"), _txtWeightIn);
        FormLayout.AddField(table, Loc.T("field.time_in"), _txtTimeIn);
        FormLayout.AddField(table, Loc.T("field.weight_out"), _txtWeightOut);
        FormLayout.AddField(table, Loc.T("field.time_out"), _txtTimeOut);
        FormLayout.AddField(table, Loc.T("field.net_weight"), _txtNetWeight);
        FormLayout.AddField(table, Loc.T("field.tolerance"), _txtTolerance);
        FormLayout.AddFullWidth(table, _lblToleranceRange);

        // Controls thêm theo thứ tự ngược (WinForms Dock stacking):
        // table (Fill) → _lblCcsWarning (Bottom) → _lblQrMessage (Bottom) → _checklist (Top) → _display (Top) → header (Top)
        card.Controls.Add(table);
        card.Controls.Add(_lblCcsWarning);
        card.Controls.Add(_lblQrMessage);
        card.Controls.Add(_checklist);
        card.Controls.Add(_display);
        card.Controls.Add(header);
        return card;
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
        var bar = FormLayout.ActionBar(_btnSave, _btnPrint, _btnSync, _btnReweigh, _btnManualWeight, _btnReset);
        bar.Dock = DockStyle.Fill;
        card.Controls.Add(bar);
        return card;
    }

    /// <summary>Lưới danh sách xe đã cân + tab lọc + bộ lọc ngày.</summary>
    private Card BuildGridArea()
    {
        var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(1) };
        var header = GridBuilder.SectionHeader(Loc.T("weigh.section_grid"));

        var filterBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 38,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = AgrisTheme.SurfaceMuted,
            Padding = new Padding(6, 4, 6, 4)
        };

        _btnTabPending.Margin = new Padding(0, 0, 4, 0);
        _btnTabWeighIn.Margin = new Padding(0, 0, 4, 0);
        _btnTabBoth.Margin = new Padding(0, 0, 12, 0);

        var lblDate = new Label
        {
            Text = Loc.T("weigh.filter_date"),
            AutoSize = true,
            Font = AgrisTheme.Body(9F),
            ForeColor = AgrisTheme.TextMuted,
            Margin = new Padding(0, 6, 4, 0)
        };

        _dtpFrom.Value = DateTime.Today.AddDays(-7);
        _dtpTo.Value = DateTime.Today;

        // Default: tab "Chờ cân" active
        _btnTabPending.BackColor = AgrisTheme.BrandGreen;
        _btnTabPending.ForeColor = AgrisTheme.TextOnBrand;
        _btnTabWeighIn.BackColor = AgrisTheme.Surface;
        _btnTabWeighIn.ForeColor = AgrisTheme.TextPrimary;

        filterBar.Controls.AddRange([_btnTabPending, _btnTabWeighIn, _btnTabBoth, lblDate, _dtpFrom, _dtpTo]);

        ConfigureWeighGrid();
        GridBuilder.AttachEmptyState(_dgvWeighed, Loc.T("weigh.no_trips"));
        _dgvWeighed.CellClick += OnWeighGridCellClick;

        card.Controls.Add(_dgvWeighed);
        card.Controls.Add(filterBar);
        card.Controls.Add(header);
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
            GridBuilder.Column(Loc.T("col.trailer"), "Trailer", 80),
            GridBuilder.Column(Loc.T("col.time_in"), "TimeIn", 120),
            GridBuilder.Column(Loc.T("col.weight_in_col"), "WeightIn", 100),
            GridBuilder.Column(Loc.T("col.time_out"), "TimeOut", 120),
            GridBuilder.Column(Loc.T("col.weight_out_col"), "WeightOut", 100),
            GridBuilder.Column(Loc.T("col.net_col"), "NetWeight", 100),
            GridBuilder.Column(Loc.T("col.customer"), "Customer", 130),
            GridBuilder.Column(Loc.T("col.commodity"), "Commodity", 100),
            GridBuilder.Column(Loc.T("col.sync_status"), "SyncStatus", 90));
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

        await HandleQrScanAsync(qrContent);
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
    internal async Task AutoScanQrAsync(string qrContent)
    {
        OnResetClicked(this, EventArgs.Empty);
        _txtBarcode.Enabled = true;
        _txtBarcode.Text = qrContent;
        // Defer scan để Load event (LoadWeighedTripsAsync) kịp release _dbLock trước
        await Task.Yield();
        await HandleQrScanAsync(qrContent);
    }

    private async Task HandleQrScanAsync(string qrContent)
    {
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

                    MessageBox.Show(
                        Loc.T("weigh.impurity_blocked_dialog"),
                        Loc.T("weigh.impurity_blocked_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);

                    _txtBarcode.Enabled = true;
                    _txtBarcode.SelectAll();
                    _txtBarcode.Focus();
                    return;
                }

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
            _currentTripVersion = scanResult.TripVersion!.Value;
            _currentWeighbridgeId = weighbridgeId.Value;
            _currentCheckpoint = checkpoint;
            _currentSegmentIndex = segmentIndex;
            _sessionId = openResult.SessionId;
            _requiredStabilityWindow = openResult.StabilityWindow;

            _lblQrMessage.Text = Loc.T("weigh.qr_scanning", checkpoint);
            _lblQrMessage.ForeColor = AgrisTheme.BrandGreen;
            _btnSave.Enabled = true;
            _btnReweigh.Enabled = true; // Bật nút cân lại khi đã có chuyến (Req 10.1)
            _btnManualWeight.Enabled = true; // Bật nút nhập tay khi đã có chuyến (Req 11.1)
            _txtBarcode.Enabled = false; // Khóa input khi đang cân

            _signalTimer.Start();
            _conditionTimer.Start();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ShowError(Loc.T("weigh.qr_error"), ex); }
        finally { _dbLock.Release(); SetBusy(false); }
    }

    /// <summary>
    /// Xác định phân đoạn cân tiếp theo bằng cách kiểm tra preconditions.
    /// Nếu trip đã có lượt cân lần 1 hợp lệ (PrecedingWeightsSatisfied cho segment 1) → segment = 1.
    /// OpenWeighSessionHandler sẽ resolve chính xác vai trò từ profile.
    /// </summary>
    private async Task<int> DetermineSegmentIndexAsync(QrScanResult scan, CancellationToken ct)
    {
        if (scan.TripId is null) return 0;

        // Kiểm tra preconditions cho segment 1 (cân lần 2).
        // Nếu PrecedingWeightsSatisfied = true → đã có lượt cân lần 1 → đây là cân lần 2.
        var preconditions = await _presenter.CheckPreconditionsAsync(
            scan.TripId.Value, WeightRole.Tare, segmentIndex: 1, ct);

        return preconditions.PrecedingWeightsSatisfied ? 1 : 0;
    }

    private void PopulateTripInfo(QrScanResult scan)
    {
        _txtRegNo.Text = scan.TripNo ?? "";
        _txtPlate.Text = scan.Plate ?? "";
        _txtCustomer.Text = scan.CustomerOrOwner ?? "";
        _txtCommodity.Text = scan.Commodity ?? "";
        _txtWarehouse.Text = scan.Warehouse ?? "";
        _txtShipment.Text = scan.ShipmentNo ?? "";

        // Hiện panel loại mía nếu profile là mía (CANE_IN)
        var isSugarcane = scan.Commodity?.Contains("CANE", StringComparison.OrdinalIgnoreCase) == true
            || scan.Commodity?.Contains("mía", StringComparison.OrdinalIgnoreCase) == true
            || string.IsNullOrWhiteSpace(scan.Commodity); // Mặc định hiện cho mía
        _pnlSugarcaneType.Visible = isSugarcane;
    }

    /// <summary>
    /// Load dữ liệu cân lần 1 (Gross) lên form khi chuẩn bị cân lần 2.
    /// Lấy WeighEvent hiệu lực cho segment 0 từ server.
    /// </summary>
    private async Task LoadFirstWeighDataAsync(Guid tripId)
    {
        try
        {
            var grossEvent = await _presenter.GetEffectiveWeighEventAsync(
                tripId, WeightRole.Gross, segmentIndex: 0, _cts.Token);

            if (grossEvent is not null)
            {
                _txtWeightIn.Text = $"{grossEvent.NormalizedKg:N3}";
                _txtTimeIn.Text = grossEvent.MeasuredAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");
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
        if (!string.IsNullOrEmpty(frm.SugarcaneType))
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

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (_sessionId is null || _currentTripId is null) return;

        // Validation: phiếu mía phải chọn loại mía + nhập ghi chú
        if (_pnlSugarcaneType.Visible)
        {
            var hasSelection = _rbMiaCay.Checked || _rbMiaKhuc.Checked || _rbMiaChay.Checked
                || _rbMiaOpt4.Checked || _rbMiaOpt5.Checked;

            if (!hasSelection)
            {
                MessageBox.Show("Vui lòng chọn loại mía trước khi xác nhận cân.",
                    "Thiếu thông tin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(_txtWeighNotes.Text))
            {
                MessageBox.Show("Bạn chưa điền ghi chú cho mía. Vui lòng nhập ghi chú.",
                    "Thiếu ghi chú", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _txtWeighNotes.Focus();
                return;
            }
        }

        SetBusy(true);
        try
        {
            var result = await _presenter.CommitAsync(BuildCommand(dryRun: false), _cts.Token);

            if (!result.IsCommitted)
            {
                // Cập nhật checklist với kết quả chốt cân thất bại để hiển thị điều kiện chưa đạt (Req 1.7)
                _checklist.UpdateFromDecision(result.Decision);

                MessageBox.Show(
                    Loc.T("weigh.commit_blocked", result.BlockReason),
                    Loc.T("weigh.commit_blocked_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            StopTimers();
            _btnSave.Enabled = false;

            // Cập nhật checklist trạng thái cuối cùng (tất cả đạt)
            _checklist.UpdateFromDecision(result.Decision);

            // Cập nhật thông tin cân trên form
            if (_currentSegmentIndex == 0)
            {
                _txtWeightIn.Text = $"{result.NormalizedKg:N3}";
                _txtTimeIn.Text = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
            }
            else
            {
                _txtWeightOut.Text = $"{result.NormalizedKg:N3}";
                _txtTimeOut.Text = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");

                // Hiển thị TL hàng nếu có cả cân vào và cân ra (Req 4.4)
                // Net do server tính theo hướng profile. UI chỉ hiển thị chênh lệch tham khảo.
                if (decimal.TryParse(_txtWeightIn.Text, out var weightIn) && result.NormalizedKg.HasValue)
                {
                    var net = Math.Abs(result.NormalizedKg.Value - weightIn);
                    _txtNetWeight.Text = $"{net:N3}";
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
            _lblQrMessage.Text = Loc.T("weigh.committed", _currentCheckpoint, $"{result.NormalizedKg:N3}") + lifecycleNote;
            _lblQrMessage.ForeColor = AgrisTheme.Success;

            // Sau cân lần 1: mở lại barcode để quét QR cho cân lần 2
            if (_currentSegmentIndex == 0)
            {
                _txtBarcode.Enabled = true;
                _txtBarcode.Clear();
                _txtBarcode.Focus();
            }

            // Refresh lưới xe đã cân sau mỗi lần chốt cân thành công (Req 1.2)
            await LoadWeighedTripsAsync();

            // Lưu QR content trước khi clear
            var qrForNextStep = _lastQrContent;

            MessageBox.Show(
                Loc.T("weigh.committed", _currentCheckpoint, $"{result.NormalizedKg:N3}"),
                Loc.T("weigh.committed_title"), MessageBoxButtons.OK, MessageBoxIcon.Information);

            // Sau cân lần 1: mở lại barcode sau khi user đóng dialog
            if (_currentSegmentIndex == 0)
            {
                _txtBarcode.Enabled = true;
                _txtBarcode.Clear();
            }

            // Refresh lưới — KHÔNG publish event bus (F5 chỉ lưu, F7 mới nhảy tab)
            await LoadWeighedTripsAsync();
        }
        catch (Exception ex)
        {
            ShowError(Loc.T("weigh.commit_error"), ex);
        }
        finally { SetBusy(false); }
    }

    private async void OnPrintClicked(object? sender, EventArgs e)
    {
        if (_currentTripId is null)
        {
            MessageBox.Show(Loc.T("weigh.print_no_trip"), Loc.T("common.no_data"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // In lại nếu phiếu đã được in trước đó cho chuyến này (Req 5.3)
        var isReprint = _ticketPrinted;

        SetBusy(true);
        try
        {
            // Hiển thị dialog chọn máy in → gọi service in phiếu (Req 5.1, 5.2)
            // Lỗi in không ảnh hưởng dữ liệu cân đã lưu (Req 5.4)
            var result = await PrintHelper.ShowPrintDialogAndPrintAsync(
                _presenter.PrintService,
                _currentTripId.Value,
                isReprint,
                this);

            // null = người dùng hủy dialog
            if (result is null)
                return;

            if (result.IsSuccess)
            {
                _ticketPrinted = true;

                // Cập nhật số phiếu trên form nếu có
                if (!string.IsNullOrWhiteSpace(result.TicketNumber))
                    _txtTicketNo.Text = result.TicketNumber;
            }
        }
        catch (Exception ex)
        {
            // Lỗi in KHÔNG ảnh hưởng dữ liệu cân đã lưu (Req 5.4)
            ShowError(Loc.T("weigh.print_error"), ex);
        }
        finally { SetBusy(false); }
    }

    private async void OnSyncClicked(object? sender, EventArgs e)
    {
        if (_currentTripId is null)
        {
            MessageBox.Show(Loc.T("weigh.sync_no_trip"), Loc.T("common.no_data"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SetBusy(true);
        try
        {
            var shipmentNo = string.IsNullOrWhiteSpace(_txtShipment.Text) || _txtShipment.Text == "—"
                ? null
                : _txtShipment.Text;

            var cmd = new SubmitTripResultCommand(
                TripId: _currentTripId.Value,
                Destination: "ERP",
                Operation: "WEIGH_RESULT",
                ShipmentNo: shipmentNo,
                CorrelationId: Guid.NewGuid().ToString("N"));

            var result = await _presenter.SyncErpAsync(cmd, _cts.Token);

            if (!result.IsQueued)
            {
                MessageBox.Show(
                    Loc.T("weigh.sync_cannot", result.Note),
                    Loc.T("weigh.btn_sync"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var statusLabel = result.Status switch
            {
                IntegrationMessageStatus.Held => Loc.T("weigh.sync_held"),
                IntegrationMessageStatus.New => Loc.T("weigh.sync_new"),
                _ => result.Status.ToString()
            };

            var message = $"{Loc.T("weigh.sync_ok")}{Environment.NewLine}" +
                          $"{Loc.T("col.status")}: {statusLabel}";

            if (!string.IsNullOrWhiteSpace(result.Note))
                message += $"{Environment.NewLine}{result.Note}";

            MessageBox.Show(message, Loc.T("weigh.btn_sync"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            ShowError(Loc.T("weigh.sync_error"), ex);
        }
        finally { SetBusy(false); }
    }

    /// <summary>
    /// Cân lại sau sự cố (Req 10.1–10.6).
    /// Hiển thị dialog chọn vai trò cân, nhập lý do bắt buộc, đính kèm tài liệu sự cố.
    /// Gọi presenter.RequestReweighAsync, xử lý REWEIGH_AFTER_POST và Trip Paused.
    /// </summary>
    private async void OnReweighClicked(object? sender, EventArgs e)
    {
        if (_currentTripId is null)
        {
            MessageBox.Show(Loc.T("weigh.reweigh_no_trip"), Loc.T("common.no_data"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // ── Dialog chọn vai trò cân (Gross/Tare), nhập lý do, đính kèm tài liệu ──
        using var dialog = new Form
        {
            Text = Loc.T("weigh.reweigh_title"),
            Size = new Size(440, 340),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = AgrisTheme.Canvas,
            Padding = new Padding(16)
        };

        var table = FormLayout.FieldTable();

        var cmbRole = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 200,
            AccessibleName = Loc.T("weigh.reweigh_field_role")
        };
        cmbRole.Items.AddRange([
            new ReweighRoleItem(Loc.T("weigh.reweigh_role_gross"), WeightRole.Gross),
            new ReweighRoleItem(Loc.T("weigh.reweigh_role_tare"), WeightRole.Tare)
        ]);
        cmbRole.SelectedIndex = 0;

        var txtReason = new TextBox
        {
            Multiline = true,
            Height = 80,
            Width = 360,
            ScrollBars = ScrollBars.Vertical,
            PlaceholderText = Loc.T("weigh.reweigh_reason_placeholder"),
            AccessibleName = Loc.T("weigh.reweigh_field_reason")
        };

        var txtDocRef = new TextBox
        {
            Width = 360,
            PlaceholderText = Loc.T("weigh.reweigh_doc_placeholder"),
            AccessibleName = Loc.T("weigh.reweigh_field_doc")
        };

        FormLayout.AddField(table, Loc.T("weigh.reweigh_field_role"), cmbRole);
        FormLayout.AddField(table, Loc.T("weigh.reweigh_field_reason"), txtReason);
        FormLayout.AddField(table, Loc.T("weigh.reweigh_field_doc"), txtDocRef);

        var btnOk = GridBuilder.PrimaryButton(Loc.T("weigh.reweigh_submit"), new Size(120, 36));
        var btnCancel = GridBuilder.SecondaryButton(Loc.T("common.cancel"), new Size(80, 36));

        btnOk.DialogResult = DialogResult.OK;
        btnCancel.DialogResult = DialogResult.Cancel;
        dialog.AcceptButton = btnOk;
        dialog.CancelButton = btnCancel;

        var actionBar = FormLayout.ActionBar(btnOk, btnCancel);
        FormLayout.AddFullWidth(table, actionBar, FormLayout.ActionRowHeight);

        dialog.Controls.Add(table);

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        // Validate lý do bắt buộc (Req 10.1)
        var reason = txtReason.Text.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            MessageBox.Show(Loc.T("weigh.reweigh_reason_required"), Loc.T("common.missing_info"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var selectedRole = ((ReweighRoleItem)cmbRole.SelectedItem!).Role;
        var documentRef = string.IsNullOrWhiteSpace(txtDocRef.Text) ? null : txtDocRef.Text.Trim();

        SetBusy(true);
        try
        {
            var cmd = new RequestReweighCommand(
                TripId: _currentTripId.Value,
                Role: selectedRole,
                SegmentIndex: _currentSegmentIndex,
                Reason: reason,
                DocumentRef: documentRef,
                CorrelationId: Guid.NewGuid().ToString("N"));

            var result = await _presenter.RequestReweighAsync(cmd, _cts.Token);

            if (!result.IsSuccess)
            {
                // ── Req 10.6: Trip đang Paused → yêu cầu Resume trước ──
                if (result.FailureReason?.Contains("PAUSED", StringComparison.OrdinalIgnoreCase) == true)
                {
                    MessageBox.Show(
                        Loc.T("weigh.reweigh_paused"),
                        Loc.T("weigh.reweigh_paused_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                MessageBox.Show(
                    Loc.T("weigh.reweigh_denied", result.FailureReason),
                    Loc.T("weigh.reweigh_denied_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // ── Req 10.2: Kiểm tra REWEIGH_AFTER_POST ──
            if (result.FailureReason?.Contains("REWEIGH_AFTER_POST", StringComparison.OrdinalIgnoreCase) == true
                || result.FailureReason?.Contains("điều chỉnh", StringComparison.OrdinalIgnoreCase) == true)
            {
                MessageBox.Show(
                    Loc.T("weigh.reweigh_after_post"),
                    "REWEIGH_AFTER_POST", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            // ── Req 10.5: Thông báo cần chạy lại đối soát ──
            MessageBox.Show(
                Loc.T("weigh.reweigh_ok", result.ApprovalId),
                Loc.T("weigh.reweigh_ok_title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            ShowError(Loc.T("weigh.reweigh_error"), ex);
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
            MessageBox.Show(Loc.T("weigh.manual_no_trip"), Loc.T("common.no_data"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // ── Dialog nhập tay: số cân, đơn vị, vai trò cân, lý do bắt buộc, mã phê duyệt bắt buộc ──
        using var dialog = new Form
        {
            Text = Loc.T("weigh.manual_title"),
            Size = new Size(460, 420),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = AgrisTheme.Canvas,
            Padding = new Padding(16)
        };

        var table = FormLayout.FieldTable();

        var numWeight = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 999_999.999m,
            DecimalPlaces = 3,
            Increment = 0.5m,
            Width = 200,
            AccessibleName = Loc.T("weigh.manual_field_weight")
        };

        var cmbUnit = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 200,
            AccessibleName = Loc.T("weigh.manual_field_unit")
        };
        cmbUnit.Items.AddRange(["kg", Loc.T("weigh.unit_ton")]);
        cmbUnit.SelectedIndex = 0; // Mặc định kg

        var cmbRole = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 200,
            AccessibleName = Loc.T("weigh.manual_field_role")
        };
        cmbRole.Items.AddRange([
            new ReweighRoleItem(Loc.T("weigh.reweigh_role_gross"), WeightRole.Gross),
            new ReweighRoleItem(Loc.T("weigh.reweigh_role_tare"), WeightRole.Tare)
        ]);
        cmbRole.SelectedIndex = 0;

        var txtReason = new TextBox
        {
            Multiline = true,
            Height = 70,
            Width = 380,
            ScrollBars = ScrollBars.Vertical,
            PlaceholderText = Loc.T("weigh.manual_reason_placeholder"),
            AccessibleName = Loc.T("weigh.manual_field_reason")
        };

        var txtApprovalRef = new TextBox
        {
            Width = 380,
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

        // ── Validate bắt buộc trước khi gọi server (Req 11.2) ──
        var reason = txtReason.Text.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            MessageBox.Show(Loc.T("weigh.manual_reason_required"), Loc.T("common.missing_info"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var approvalRef = txtApprovalRef.Text.Trim();
        if (string.IsNullOrWhiteSpace(approvalRef))
        {
            MessageBox.Show(Loc.T("weigh.manual_approval_required"), Loc.T("common.missing_info"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (numWeight.Value <= 0)
        {
            MessageBox.Show(Loc.T("weigh.manual_weight_required"), Loc.T("weigh.manual_weight_invalid"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var selectedRole = ((ReweighRoleItem)cmbRole.SelectedItem!).Role;
        var rawUom = cmbUnit.SelectedItem!.ToString()!;
        var rawWeight = numWeight.Value;

        SetBusy(true);
        try
        {
            var cmd = new ApproveManualWeightCommand(
                TripId: _currentTripId.Value,
                WeighbridgeId: _currentWeighbridgeId,
                Checkpoint: _currentCheckpoint,
                SegmentIndex: _currentSegmentIndex,
                Role: selectedRole,
                RawWeight: rawWeight,
                RawUom: rawUom,
                Reason: reason,
                ApprovalRef: approvalRef,
                CorrelationId: Guid.NewGuid().ToString("N"));

            var result = await _presenter.ManualWeightAsync(cmd, _cts.Token);

            if (!result.IsSuccess)
            {
                // Req 11.3: Trip đang Paused → từ chối
                // Req 11.4: Unknown transaction → từ chối
                // Server-side đã kiểm tra và trả FailureReason cụ thể.
                MessageBox.Show(
                    Loc.T("weigh.manual_denied", result.FailureReason),
                    Loc.T("weigh.reweigh_denied_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Req 11.5: Audit log ghi đầy đủ ở server-side (ApproveManualWeightHandler)
            MessageBox.Show(
                Loc.T("weigh.manual_ok", $"{rawWeight:N3}", rawUom, selectedRole, approvalRef),
                Loc.T("weigh.manual_ok_title"), MessageBoxButtons.OK, MessageBoxIcon.Information);

            // Refresh lưới sau nhập tay thành công
            await LoadWeighedTripsAsync();
        }
        catch (Exception ex)
        {
            ShowError(Loc.T("weigh.manual_error"), ex);
        }
        finally { SetBusy(false); }
    }

    /// <summary>
    /// Tiếp tục (F7) — chuyển sang tab tiếp theo cho chuyến đang chọn.
    /// Sau cân lần 1 → Tạp chất (UI-05). Sau cân lần 2 → Cổng ra (UI-11).
    /// </summary>
    private void OnContinueClicked(object? sender, EventArgs e)
    {
        if (_currentTripId is null)
        {
            MessageBox.Show(Loc.T("common.no_trip_continue"),
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
        _currentWeighbridgeId = Guid.Empty;
        _currentLaneId = Guid.Empty;
        _currentCheckpoint = default;
        _currentSegmentIndex = 0;
        _ticketPrinted = false;

        // Reset tất cả fields
        foreach (var ctrl in new[]
        {
            _txtRegNo, _txtPlate,
            _txtCustomer, _txtCommodity, _txtWarehouse,
            _txtShipment,
            _txtWeightIn, _txtTimeIn, _txtWeightOut, _txtTimeOut,
            _txtNetWeight, _txtTolerance
        })
        {
            ctrl.Clear();
        }

        _lblToleranceRange.Text = string.Empty;
        _lblQrMessage.Text = Loc.T("weigh.qr_start");
        _lblQrMessage.ForeColor = AgrisTheme.BrandGreen;
        _lblCcsWarning.Text = string.Empty;
        _lblCcsWarning.Visible = false;

        // Reset checkboxes loại mía
        _rbMiaCay.Checked = false;
        _rbMiaKhuc.Checked = false;
        _rbMiaChay.Checked = false;
        _rbMiaOpt4.Checked = false;
        _rbMiaOpt5.Checked = false;
        _txtWeighNotes.Clear();
        _pnlSugarcaneType.Visible = false;
        _display.Update(null, "kg", ScaleDeviceStatus.Unknown, false, TimeSpan.Zero, null);
        _checklist.Reset();
        _btnSave.Enabled = false;
        _btnPrint.Enabled = false;
        _btnSync.Enabled = false;
        _btnReweigh.Enabled = false;
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
                var pendingRows = await _presenter.LoadPendingTripsAsync(siteId, _cts.Token);
                _dgvWeighed.SuspendLayout();
                try
                {
                    _dgvWeighed.DataSource = null;
                    _dgvWeighed.DataSource = pendingRows;
                }
                finally
                {
                    _dgvWeighed.ResumeLayout();
                }
                return;
            }

            // Mode 1/2: Cân vào / Cân cả vào và ra
            var weighbridgeId = _currentWeighbridgeId != Guid.Empty
                ? _currentWeighbridgeId
                : Guid.Empty;

            var filter = new WeighedTripFilter
            {
                FromUtc = ToUtcStartOfDay(_dtpFrom.Value),
                ToUtc = ToUtcStartOfDay(_dtpTo.Value.AddDays(1)),
                FirstWeightOnly = _gridMode == 1,
                CompletedOnly = _gridMode == 2
            };

            var rows = await _presenter.LoadWeighedTripsAsync(
                siteId, weighbridgeId, filter, _cts.Token);

            _dgvWeighed.SuspendLayout();
            try
            {
                _dgvWeighed.DataSource = null;
                _dgvWeighed.DataSource = rows;
            }
            finally
            {
                _dgvWeighed.ResumeLayout();
            }
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
        UpdateGridTabStyles();
        await LoadWeighedTripsAsync();
    }

    private async void OnTabWeighInClicked(object? sender, EventArgs e)
    {
        _gridMode = 1;
        UpdateGridTabStyles();
        await LoadWeighedTripsAsync();
    }

    private async void OnTabBothClicked(object? sender, EventArgs e)
    {
        _gridMode = 2;
        UpdateGridTabStyles();
        await LoadWeighedTripsAsync();
    }

    private void UpdateGridTabStyles()
    {
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

        // Chỉ xử lý ở tab "Chờ cân" — các tab khác chỉ hiển thị lịch sử
        if (_gridMode != 0) return;

        var row = _dgvWeighed.Rows[e.RowIndex];
        var tripNo = row.Cells["RegNo"]?.Value?.ToString();
        if (string.IsNullOrWhiteSpace(tripNo)) return;

        await AutoScanQrAsync(tripNo);
    }

    private async void OnDateFilterChanged(object? sender, EventArgs e)
    {
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
        _btnReweigh.Enabled = !busy && _currentTripId is not null;
        _btnManualWeight.Enabled = !busy && _currentTripId is not null;
        _btnReset.Enabled = !busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    private static void ShowError(string title, Exception ex)
        => MessageBox.Show($"{title}.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
            Loc.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);

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
        var offset = TimeZoneInfo.Local.GetUtcOffset(startOfDay);
        return new DateTimeOffset(startOfDay, offset).ToUniversalTime();
    }
}