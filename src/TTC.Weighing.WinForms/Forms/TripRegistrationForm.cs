using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.Queries;
using TTC.Weighing.Application.UseCases.Trips;
using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.WinForms.Controls;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Presenters;
using TTC.Weighing.WinForms.Services;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Forms;

/// <summary>
/// UI-02 — Đăng tài. Bố cục mới theo wireframe:
/// Panel trái (form đăng tài), panel phải trên (2 camera), panel phải dưới (thông báo),
/// thanh nút hành động, lưới chuyến (placeholder — task 6.4), thanh trạng thái.
///
/// Form gọi TripRegistrationPresenter — không xử lý business logic trực tiếp.
/// Req 6.1, 6.2, 6.3, 6.4, 6.5.
/// </summary>
public sealed class TripRegistrationForm : Form
{
    private readonly TripRegistrationPresenter _presenter;
    private readonly TripEventBus _eventBus;
    private readonly CancellationTokenSource _cts = new();

    // ── Form fields (panel trái) ──
    // Chỉ Biển số + Lệnh đốn là editable (tra cứu). Còn lại read-only, lấy từ FRM.
    private readonly TextBox _txtPlate = new() { AccessibleName = Loc.T("field.plate") };
    private readonly Button _btnPlateDropdown = new() { Text = "▼", AccessibleName = "Mở danh sách biển số" };
    private readonly ListBox _lstPlatePopup = new()
    {
        Visible = false,
        BorderStyle = BorderStyle.FixedSingle,
        IntegralHeight = false
    };
    private readonly DateTimePicker _dtpFrmFrom = new() { AccessibleName = "Từ ngày FRM" };
    private readonly DateTimePicker _dtpFrmTo = new() { AccessibleName = "Đến ngày FRM" };
    private string[] _allPlates = [];
    private string[] _filteredPlates = [];

    private readonly TextBox _txtShipment = new() { AccessibleName = Loc.T("reg.field_shipment") };
    private readonly TextBox _txtTripNo = new() { ReadOnly = true, AccessibleName = Loc.T("field.trip_no") };
    private readonly TextBox _txtDateTime = new() { ReadOnly = true, AccessibleName = Loc.T("field.date_time") };

    // ── Trường FRM (cho phép nhập tay khi offline — tự điền từ API khi có kết nối) ──
    private readonly TextBox _txtCustomerOrSupplier = new() { AccessibleName = "Đối tác vận chuyển" };
    private readonly TextBox _txtStation = new() { AccessibleName = "Trạm" };
    private readonly TextBox _txtCropSeason = new() { AccessibleName = "Vụ đầu tư" };
    private readonly TextBox _txtHarvestMethod = new() { AccessibleName = "Hình thức thu hoạch" };
    private readonly TextBox _txtSugarcaneOwner = new() { AccessibleName = "Chủ mía" };
    private readonly TextBox _txtSugarcaneType = new() { AccessibleName = "Loại mía" };
    private readonly TextBox _txtSugarcaneVariety = new() { AccessibleName = "Giống mía" };
    private readonly TextBox _txtVehicleType = new() { AccessibleName = "Loại xe" };
    private readonly TextBox _txtMaThe = new() { ReadOnly = true, AccessibleName = "Mã thẻ xe" };
    private readonly TextBox _txtMaHopDong = new() { ReadOnly = true, AccessibleName = "Mã hợp đồng" };
    private readonly TextBox _txtTaiTrong = new() { ReadOnly = true, AccessibleName = "Tải trọng" };
    private readonly TextBox _txtNotes = new() { AccessibleName = Loc.T("field.notes") };

    // ── Chọn loại nghiệp vụ (Mía nhập / Vật tư) ──
    private readonly ComboBox _cboProfileType = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        AccessibleName = Loc.T("field.profile_type")
    };

    // ── Các trường dành riêng cho vật tư (ẩn khi chọn mía) ──
    private readonly TextBox _txtCommodity = new() { AccessibleName = Loc.T("field.commodity") };
    private readonly TextBox _txtWarehouse = new() { AccessibleName = "Kho" };
    private readonly RadioButton _rbMaterialIn = new() { Text = "Nhập", AutoSize = true, Checked = true };
    private readonly RadioButton _rbMaterialOut = new() { Text = "Xuất", AutoSize = true };
    private readonly TextBox _txtTripNumber = new() { AccessibleName = "Số TRIP", PlaceholderText = "Số TRIP / Shipment" };
    private readonly TextBox _txtMaterialPlate = new() { AccessibleName = "Biển số", PlaceholderText = "Biển số xe" };
    private readonly Button _btnCheckErp = GridBuilder.PrimaryButton("Kiểm tra", new Size(100, 30));
    private readonly TextBox _txtMaterialShipment = new() { AccessibleName = "Shipment Number", PlaceholderText = "Shipment Number" };
    private readonly TextBox _txtMaterialPlateDetail = new() { AccessibleName = "Biển số xe", PlaceholderText = "Biển số xe" };
    private readonly TextBox _txtTrailer = new() { AccessibleName = "Rơ moóc", PlaceholderText = "Rơ moóc (nếu có)" };
    private readonly TextBox _txtTransporter = new() { AccessibleName = "Nhà vận chuyển", PlaceholderText = "Nhà vận chuyển" };
    private readonly TextBox _txtDriver = new() { AccessibleName = "Lái xe" };
    private readonly TextBox _txtMaterialCustomer = new() { AccessibleName = "Khách hàng/NCC", PlaceholderText = "Khách hàng/NCC" };
    private readonly TextBox _txtReceiveDate = new() { AccessibleName = "Ngày nhận hàng", PlaceholderText = "Ngày nhận hàng" };
    private readonly TextBox _txtMaterialNotes = new() { AccessibleName = "Ghi chú" };

    // ── Panel chứa các trường mía (để ẩn/hiện) ──
    private Panel _pnlSugarcaneFields = null!;
    // ── Panel chi tiết mía (expand/collapse) ──
    private Panel _pnlDetailFields = null!;
    private readonly Button _btnExpandDetails = new() { Text = "📋 Xem thêm thông tin chuyến", AutoSize = false, Height = 44, Padding = new Padding(4, 0, 4, 0) };
    // ── Panel chứa các trường vật tư (để ẩn/hiện) ──
    private Panel _pnlMaterialFields = null!;

    // ── Action buttons ──
    private readonly Button _btnSave = GridBuilder.PrimaryButton(Loc.T("reg.btn_register"), new Size(140, 36));
    private readonly Button _btnPrint = GridBuilder.SecondaryButton(Loc.T("reg.btn_print"), new Size(130, 36));
    private readonly Button _btnRetryDigi = GridBuilder.SecondaryButton("🔄 Đẩy lại Digi", new Size(140, 36));
    private readonly Button _btnCancel = GridBuilder.SecondaryButton("Hủy phiếu", new Size(110, 36));
    private readonly Button _btnReset = GridBuilder.SecondaryButton(Loc.T("reg.btn_reset"), new Size(130, 36));

    // ── Camera views (panel phải trên) ──
    private readonly CameraViewControl _cameraIn = new() { CameraPosition = "In" };
    private readonly CameraViewControl _cameraOut = new() { CameraPosition = "Out" };

    // ── Notification panel (panel phải dưới) ──
    private readonly RichTextBox _rtbNotifications = new();

    // ── Trip grid (Req 7.1) ──
    private readonly DataGridView _dgvTrips = GridBuilder.ReadOnlyGrid(Loc.T("reg.section_grid"));

    // ── Grid filter tabs (Req 7.2) ──
    private readonly Button _btnTabInFactory = GridBuilder.PrimaryButton(Loc.T("reg.tab_in_factory"), new Size(140, 30));
    private readonly Button _btnTabExited = GridBuilder.SecondaryButton(Loc.T("reg.tab_exited"), new Size(100, 30));
    private readonly Button _btnTabCancelled = GridBuilder.SecondaryButton(Loc.T("reg.tab_cancelled"), new Size(120, 30));

    // ── Date range filter (Req 7.3) ──
    private readonly DateTimePicker _dtpFrom = new()
    {
        Format = DateTimePickerFormat.Custom,
        CustomFormat = "dd/MM/yyyy",
        Width = 150,
        AccessibleName = Loc.T("reg.filter_from")
    };
    private readonly DateTimePicker _dtpTo = new()
    {
        Format = DateTimePickerFormat.Custom,
        CustomFormat = "dd/MM/yyyy",
        Width = 150,
        AccessibleName = Loc.T("reg.filter_to")
    };

    // ── Auto-refresh timer (Req 7.5) ──
    private readonly System.Windows.Forms.Timer _gridRefreshTimer = new() { Interval = 30_000 };
    private bool _gridRefreshInFlight;

    // ── Active grid tab state ──
    private TripGridTab _activeTab = TripGridTab.InFactory;

    // ── Status bar ──
    private readonly StatusStrip _statusBar = new();
    private readonly ToolStripStatusLabel _lblServerStatus = new();
    private readonly ToolStripStatusLabel _lblBarcodeStatus = new();
    private readonly ToolStripStatusLabel _lblLprStatus = new();
    private readonly ToolStripStatusLabel _lblCameraStatus = new();

    // ── State ──
    private PlanEntry? _selectedPlan;
    private TTC.Weighing.Infrastructure.Adapters.ErpOrderLine? _lastErpOrderLine;
    private bool _tempQrChosen; // true khi user đã chọn phát QR tạm qua Kiểm tra
    private bool _isBusy;
    private Guid? _lastRegisteredTripId;
    private string? _lastQrToken;
    private int? _lastQueueSequenceNumber;
    private IReadOnlyList<WorkflowProfile>? _availableProfiles;

    /// <summary>Serialize DB operations trong cùng form — tránh EF Core "second operation" error.</summary>
    private readonly SemaphoreSlim _dbLock = new(1, 1);

    /// <summary>Biển số đã lookup thành công — tránh lookup trùng khi OnPlateLeave fire sau SelectPlateFromPopup.</summary>
    private string? _lastLookedUpPlate;
    /// <summary>Đang lookup — chặn re-entry.</summary>
    private bool _lookupInProgress;

    public TripRegistrationForm(TripRegistrationPresenter presenter, TripEventBus eventBus)
    {
        _presenter = presenter;
        _eventBus = eventBus;

        Text = Loc.T("reg.title");
        MinimumSize = new Size(1280, 860);
        BackColor = AgrisTheme.Canvas;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        BuildLayout();
        InitializeVehicleTypeCombo();
        ConfigureGridColumns();
        InitializeGridTabs();
        InitializeDateFilter();
        InitializeGridRefreshTimer();
        WireEventHandlers();
        ResetForm();

        // Realtime: refresh lưới khi form khác thay đổi chuyến
        _eventBus.TripChanged += OnTripChangedFromOtherForm;

        Load += async (_, _) =>
        {
            await LoadProfilesAsync();
            LoadPlateComboBox();
            await LoadGridAsync();
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  Layout
    // ═══════════════════════════════════════════════════════════

    private void BuildLayout()
    {
        // Root: 3 rows — top area | action bar | grid
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = AgrisTheme.Canvas,
            Padding = new Padding(10, 8, 10, 0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 75));   // Top area (form + camera)
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, FormLayout.ActionRowHeight));   // Action bar
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 25));   // Grid area

        root.Controls.Add(BuildTopArea(), 0, 0);
        root.Controls.Add(BuildActionBar(), 0, 1);
        root.Controls.Add(BuildGridArea(), 0, 2);

        Controls.Add(root);

        // ListBox popup cho autocomplete biển số — thêm sau root để nằm trên cùng
        _lstPlatePopup.Font = AgrisTheme.Body(10F);
        Controls.Add(_lstPlatePopup);
        _lstPlatePopup.BringToFront();
    }

    /// <summary>
    /// Top area: Form nhập liệu 60% | Camera Vào 20% | Camera Ra 20%.
    /// </summary>
    private TableLayoutPanel BuildTopArea()
    {
        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = AgrisTheme.Canvas
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));  // Form nhập liệu
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));  // Camera Vào
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));  // Camera Ra
        top.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // Cột trái: form đăng tài (60%)
        top.Controls.Add(BuildFormCard(), 0, 0);

        // Cột giữa: Camera Vào (20%)
        var camInCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(1) };
        _cameraIn.Dock = DockStyle.Fill;
        _cameraIn.ApplyPositionLabel();
        camInCard.Controls.Add(_cameraIn);
        camInCard.Controls.Add(GridBuilder.SectionHeader("Camera Vào"));
        top.Controls.Add(camInCard, 1, 0);

        // Cột phải: Camera Ra (20%)
        var camOutCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(1) };
        _cameraOut.Dock = DockStyle.Fill;
        _cameraOut.ApplyPositionLabel();
        camOutCard.Controls.Add(_cameraOut);
        camOutCard.Controls.Add(GridBuilder.SectionHeader("Camera Ra"));
        top.Controls.Add(camOutCard, 2, 0);

        return top;
    }

    /// <summary>Form đăng tài — phần trên: tìm nhanh, phần dưới: chi tiết chuyến.</summary>
    private Card BuildFormCard()
    {
        var card = new Card
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(1),
            AutoScroll = true
        };

        var header = GridBuilder.SectionHeader(Loc.T("reg.section_info"));

        var table = FormLayout.FieldTable();

        // ═══════════════════════════════════════════════════════════
        //  LOG THÔNG BÁO (nổi bật, trên cùng)
        // ═══════════════════════════════════════════════════════════
        _rtbNotifications.ReadOnly = true;
        _rtbNotifications.BackColor = Color.FromArgb(255, 255, 230); // vàng nhạt nổi bật
        _rtbNotifications.BorderStyle = BorderStyle.FixedSingle;
        _rtbNotifications.Font = AgrisTheme.Mono(9.5F);
        _rtbNotifications.ForeColor = AgrisTheme.TextPrimary;
        _rtbNotifications.ScrollBars = RichTextBoxScrollBars.Vertical;
        _rtbNotifications.Dock = DockStyle.Fill;
        _rtbNotifications.Height = 56;
        _rtbNotifications.AccessibleName = Loc.T("reg.section_notification");
        FormLayout.AddFullWidth(table, _rtbNotifications, 62);

        // ═══════════════════════════════════════════════════════════
        //  PANEL MÍA NHẬP (FRM) — ẩn khi chọn Vật tư
        // ═══════════════════════════════════════════════════════════

        // Chọn loại nghiệp vụ (Mía nhập / Vật tư)
        _cboProfileType.Items.AddRange(new object[] { Loc.T("profile.sugarcane"), Loc.T("profile.material") });
        _cboProfileType.SelectedIndex = 0;
        _cboProfileType.SelectedIndexChanged += (_, _) => OnProfileTypeChanged();
        FormLayout.AddField(table, Loc.T("field.profile_type"), _cboProfileType);

        var caneTable = FormLayout.FieldTable();

        // Khung ngày lọc xe FRM
        var dateRangePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = false,
            Margin = Padding.Empty
        };
        _dtpFrmFrom.Format = DateTimePickerFormat.Custom;
        _dtpFrmFrom.CustomFormat = "dd/MM/yyyy";
        _dtpFrmFrom.Width = 130;
        _dtpFrmFrom.Value = DateTime.Today;
        _dtpFrmFrom.ValueChanged += (_, _) => RefreshPlateFilter();
        var lblTo = new Label
        {
            Text = "→",
            AutoSize = true,
            TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
            Margin = new Padding(6, 6, 6, 0)
        };
        _dtpFrmTo.Format = DateTimePickerFormat.Custom;
        _dtpFrmTo.CustomFormat = "dd/MM/yyyy";
        _dtpFrmTo.Width = 130;
        _dtpFrmTo.Value = DateTime.Today;
        _dtpFrmTo.ValueChanged += (_, _) => RefreshPlateFilter();
        dateRangePanel.Controls.Add(_dtpFrmFrom);
        dateRangePanel.Controls.Add(lblTo);
        dateRangePanel.Controls.Add(_dtpFrmTo);
        FormLayout.AddField(caneTable, Loc.T("reg.frm_date_range"), dateRangePanel);

        // Biển số xe + ▼ dropdown (ô tìm kiếm chính)
        var platePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        platePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        platePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
        platePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _txtPlate.Font = AgrisTheme.Body(12F, FontStyle.Bold);
        _txtPlate.Dock = DockStyle.Fill;
        _txtPlate.PlaceholderText = Loc.T("field.plate");
        _txtPlate.BackColor = Color.FromArgb(255, 255, 220);
        _txtPlate.BorderStyle = BorderStyle.FixedSingle;
        _btnPlateDropdown.Dock = DockStyle.Fill;
        _btnPlateDropdown.FlatStyle = FlatStyle.Flat;
        _btnPlateDropdown.FlatAppearance.BorderSize = 1;
        _btnPlateDropdown.Cursor = Cursors.Hand;
        _btnPlateDropdown.TabStop = false;
        platePanel.Controls.Add(_txtPlate, 0, 0);
        platePanel.Controls.Add(_btnPlateDropdown, 1, 0);
        FormLayout.AddField(caneTable, "Tìm nhanh", platePanel);

        // Lệnh đốn / Shipment
        _txtShipment.Font = AgrisTheme.Body(10F);
        _txtShipment.Dock = DockStyle.Fill;
        _txtShipment.PlaceholderText = Loc.T("reg.field_shipment");
        FormLayout.AddField(caneTable, Loc.T("reg.field_shipment_short"), _txtShipment);

        // ═══════════════════════════════════════════════════════════
        //  PHẦN CHI TIẾT (tự điền khi chọn xe, cho phép sửa tay)
        // ═══════════════════════════════════════════════════════════

        // Số đăng tài (auto)
        _txtTripNo.ReadOnly = true;
        _txtTripNo.BackColor = AgrisTheme.ReadOnlyBg;
        _txtTripNo.ForeColor = AgrisTheme.ReadOnlyText;
        _txtTripNo.BorderStyle = BorderStyle.FixedSingle;
        _txtTripNo.TabStop = false;
        _txtTripNo.Cursor = Cursors.Arrow;
        _txtTripNo.PlaceholderText = Loc.T("reg.auto_generate");
        _txtTripNo.Enter += (_, _) => _txtTripNo.Parent?.SelectNextControl(_txtTripNo, true, true, true, true);
        FormLayout.AddField(caneTable, Loc.T("field.trip_no"), _txtTripNo);

        // Ngày giờ (auto)
        _txtDateTime.ReadOnly = true;
        _txtDateTime.BackColor = AgrisTheme.ReadOnlyBg;
        _txtDateTime.ForeColor = AgrisTheme.ReadOnlyText;
        _txtDateTime.BorderStyle = BorderStyle.FixedSingle;
        _txtDateTime.TabStop = false;
        _txtDateTime.Cursor = Cursors.Arrow;
        _txtDateTime.PlaceholderText = Loc.T("reg.auto_datetime");
        _txtDateTime.Enter += (_, _) => _txtDateTime.Parent?.SelectNextControl(_txtDateTime, true, true, true, true);
        FormLayout.AddField(caneTable, Loc.T("field.date_time"), _txtDateTime);

        // Trường FRM (ẩn — tự điền từ FRM cache khi chọn xe, lưu đầy đủ vào DB)
        // Không hiển thị trên form vì chưa có dữ liệu FRM LenhDon.
        // Khi cần hiện lại: bỏ comment các dòng FormLayout.AddField bên dưới.
        // Chi tiết FRM hiện ở panel "Xem thêm" bên dưới
        _txtMaHopDong.BackColor = AgrisTheme.ReadOnlyBg;

        // Ghi chú (ngay sau phần tìm nhanh)
        FormLayout.AddField(caneTable, Loc.T("field.notes"), _txtNotes);

        // ── Nút "Xem thêm" — bấm hiện popup chi tiết chuyến xe ──
        _btnExpandDetails.FlatStyle = FlatStyle.Flat;
        _btnExpandDetails.FlatAppearance.BorderColor = AgrisTheme.BrandGreen;
        _btnExpandDetails.FlatAppearance.BorderSize = 1;
        _btnExpandDetails.Font = AgrisTheme.Body(10F, FontStyle.Bold);
        _btnExpandDetails.ForeColor = AgrisTheme.BrandGreen;
        _btnExpandDetails.BackColor = AgrisTheme.Surface;
        _btnExpandDetails.Cursor = Cursors.Hand;
        _btnExpandDetails.TextAlign = ContentAlignment.MiddleCenter;
        _btnExpandDetails.Text = "📋 Xem thêm thông tin chuyến";
        _btnExpandDetails.Size = new Size(300, 44);
        _btnExpandDetails.Click += (_, _) => ShowDetailPopup();
        FormLayout.AddFullWidth(caneTable, _btnExpandDetails, 54);
        _btnExpandDetails.Margin = new Padding(4, 4, 4, 4);

        // ── Panel chi tiết (ẩn — dữ liệu binding vẫn hoạt động, chỉ không hiện inline) ──
        _pnlDetailFields = new Panel { Dock = DockStyle.Fill, Visible = false };
        var detailTable = FormLayout.FieldTable();

        _txtSugarcaneOwner.Font = AgrisTheme.Body(10F); _txtSugarcaneOwner.Dock = DockStyle.Fill; _txtSugarcaneOwner.PlaceholderText = "Chủ mía";
        FormLayout.AddField(detailTable, "Chủ mía", _txtSugarcaneOwner);
        _txtSugarcaneType.Font = AgrisTheme.Body(10F); _txtSugarcaneType.Dock = DockStyle.Fill; _txtSugarcaneType.PlaceholderText = "Loại mía";
        FormLayout.AddField(detailTable, "Loại mía", _txtSugarcaneType);
        _txtSugarcaneVariety.Font = AgrisTheme.Body(10F); _txtSugarcaneVariety.Dock = DockStyle.Fill; _txtSugarcaneVariety.PlaceholderText = "Giống mía";
        FormLayout.AddField(detailTable, "Giống mía", _txtSugarcaneVariety);
        _txtStation.Font = AgrisTheme.Body(10F); _txtStation.Dock = DockStyle.Fill; _txtStation.PlaceholderText = "Trạm";
        FormLayout.AddField(detailTable, "Trạm", _txtStation);
        _txtCropSeason.Font = AgrisTheme.Body(10F); _txtCropSeason.Dock = DockStyle.Fill; _txtCropSeason.PlaceholderText = "Vụ đầu tư";
        FormLayout.AddField(detailTable, "Vụ đầu tư", _txtCropSeason);
        _txtVehicleType.Font = AgrisTheme.Body(10F); _txtVehicleType.Dock = DockStyle.Fill; _txtVehicleType.PlaceholderText = "Loại xe";
        FormLayout.AddField(detailTable, "Loại xe", _txtVehicleType);
        _txtTaiTrong.Font = AgrisTheme.Body(10F); _txtTaiTrong.Dock = DockStyle.Fill; _txtTaiTrong.ReadOnly = false; _txtTaiTrong.BackColor = SystemColors.Window; _txtTaiTrong.PlaceholderText = "Tải trọng (T)";
        FormLayout.AddField(detailTable, "Tải trọng (T)", _txtTaiTrong);
        _txtMaThe.Font = AgrisTheme.Body(10F); _txtMaThe.Dock = DockStyle.Fill; _txtMaThe.ReadOnly = false; _txtMaThe.BackColor = SystemColors.Window; _txtMaThe.PlaceholderText = "Mã thẻ xe";
        FormLayout.AddField(detailTable, "Mã thẻ xe", _txtMaThe);

        detailTable.Dock = DockStyle.Fill;
        _pnlDetailFields.Controls.Add(detailTable);
        // Detail fields ẩn — hiện qua popup ShowDetailPopup(), không inline trong form

        caneTable.Dock = DockStyle.Fill;
        _pnlSugarcaneFields = new Panel { Dock = DockStyle.Fill, Visible = true };
        _pnlSugarcaneFields.Controls.Add(caneTable);

        // Panel trường vật tư (ẩn khi chọn mía — hiện khi chọn Vật tư)
        _pnlMaterialFields = new Panel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowOnly,
            Visible = false
        };
        var materialTable = FormLayout.FieldTable();

        // Radio Nhập / Xuất
        var directionPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = false
        };
        directionPanel.Controls.Add(_rbMaterialIn);
        directionPanel.Controls.Add(_rbMaterialOut);
        FormLayout.AddField(materialTable, "Hướng", directionPanel);

        // Số TRIP + Biển số + Nút Kiểm tra (cùng 1 hàng)
        var tripCheckPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        tripCheckPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40)); // Số TRIP
        tripCheckPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60)); // label
        tripCheckPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35)); // Biển số
        tripCheckPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8));  // spacer
        tripCheckPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100)); // nút
        tripCheckPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _txtTripNumber.Font = AgrisTheme.Body(10F);
        _txtTripNumber.Dock = DockStyle.Fill;
        _txtMaterialPlate.Font = AgrisTheme.Body(10F);
        _txtMaterialPlate.Dock = DockStyle.Fill;
        var lblBienSo = new Label
        {
            Text = "Biển số",
            Dock = DockStyle.Fill,
            TextAlign = System.Drawing.ContentAlignment.MiddleRight,
            Font = AgrisTheme.Body(9F),
            ForeColor = AgrisTheme.TextMuted
        };
        tripCheckPanel.Controls.Add(_txtTripNumber, 0, 0);
        tripCheckPanel.Controls.Add(lblBienSo, 1, 0);
        tripCheckPanel.Controls.Add(_txtMaterialPlate, 2, 0);
        tripCheckPanel.Controls.Add(new Panel { Dock = DockStyle.Fill }, 3, 0);
        tripCheckPanel.Controls.Add(_btnCheckErp, 4, 0);
        FormLayout.AddField(materialTable, "Số TRIP", tripCheckPanel);

        // Thông tin chi tiết ERP (chỉ hiện inline: Shipment + Biển số xe)
        FormLayout.AddField(materialTable, "Shipment Number", _txtMaterialShipment);
        FormLayout.AddField(materialTable, "Biển số xe", _txtMaterialPlateDetail);
        FormLayout.AddField(materialTable, Loc.T("field.notes"), _txtMaterialNotes);

        // Nút "Xem thêm" cho vật tư — popup hiện toàn bộ chi tiết ERP
        var btnExpandMaterial = new Button
        {
            Text = "📋 Xem thêm thông tin chuyến",
            AutoSize = false,
            Height = 38,
            Width = 280,
            FlatStyle = FlatStyle.Flat,
            Font = AgrisTheme.Body(10F, FontStyle.Bold),
            ForeColor = AgrisTheme.BrandGreen,
            BackColor = AgrisTheme.Surface,
            Cursor = Cursors.Hand,
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = new Padding(4, 0, 4, 0)
        };
        btnExpandMaterial.FlatAppearance.BorderColor = AgrisTheme.BrandGreen;
        btnExpandMaterial.FlatAppearance.BorderSize = 1;
        btnExpandMaterial.Click += (_, _) => ShowMaterialDetailPopup();
        FormLayout.AddFullWidth(materialTable, btnExpandMaterial, 48);
        btnExpandMaterial.Margin = new Padding(4, 4, 4, 4);

        // Wire event: Kiểm tra ERP
        _btnCheckErp.Click += async (_, _) => await CheckErpAsync();

        materialTable.Dock = DockStyle.Fill;
        _pnlMaterialFields.Controls.Add(materialTable);
        _pnlMaterialFields.Dock = DockStyle.Fill;

        // Ghi chú đã chuyển lên phần tìm nhanh
        // Thông báo đã chuyển lên trên cùng

        // Layout: card chứa header (Top) → table header rows (Top) → panels (Fill)
        // Dùng Dock stacking thay vì TableLayoutPanel Percent — đơn giản, không bị co thiếu
        card.Controls.Add(_pnlMaterialFields);  // Fill — ẩn mặc định
        card.Controls.Add(_pnlSugarcaneFields); // Fill — hiện mặc định (override material)
        card.Controls.Add(table);               // Top — notification + profile type rows
        table.Dock = DockStyle.Top;
        table.AutoSize = true;
        card.Controls.Add(header);              // Top — tiêu đề "Thông tin đăng tài"

        return card;
    }

    /// <summary>
    /// Chuyển đổi giữa mía nhập và vật tư — ẩn/hiện trường tương ứng.
    /// Dùng WorkflowProfile để phân biệt nghiệp vụ (R-001: "Một lõi, nhiều profile").
    /// </summary>
    private void OnProfileTypeChanged()
    {
        var isSugarcane = _cboProfileType.SelectedIndex == 0;
        _pnlSugarcaneFields.Visible = isSugarcane;
        _pnlMaterialFields.Visible = !isSugarcane;
        // Dock.Fill — hiển thị panel nào visible sẽ tự lấp đầy, không cần chỉnh row height
    }

    /// <summary>Hiện popup chi tiết chuyến xe — 10 field editable, font lớn, rõ ràng.</summary>
    private void ShowDetailPopup()
    {
        using var popup = new Form
        {
            Text = "📋 Thông tin chi tiết chuyến xe",
            Size = new Size(620, 640),
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

        // Tạo các textbox riêng cho popup, sync dữ liệu khi OK
        var fields = new (string Label, TextBox Source, TextBox Popup)[]
        {
            ("Chủ mía", _txtSugarcaneOwner, MakePopupTxt(_txtSugarcaneOwner)),
            ("Loại mía", _txtSugarcaneType, MakePopupTxt(_txtSugarcaneType)),
            ("Giống mía", _txtSugarcaneVariety, MakePopupTxt(_txtSugarcaneVariety)),
            ("Trạm", _txtStation, MakePopupTxt(_txtStation)),
            ("Vụ đầu tư", _txtCropSeason, MakePopupTxt(_txtCropSeason)),
            ("Loại xe", _txtVehicleType, MakePopupTxt(_txtVehicleType)),
            ("Tải trọng (T)", _txtTaiTrong, MakePopupTxt(_txtTaiTrong)),
            ("Mã thẻ xe", _txtMaThe, MakePopupTxt(_txtMaThe)),
        };

        foreach (var (label, _, txt) in fields)
            FormLayout.AddField(table, label, txt);

        var btnOk = GridBuilder.PrimaryButton("Lưu thay đổi", new Size(160, 42));
        btnOk.DialogResult = DialogResult.OK;
        var btnCancel = GridBuilder.SecondaryButton("Đóng", new Size(120, 42));
        btnCancel.DialogResult = DialogResult.Cancel;

        var actionBar = FormLayout.ActionBar(btnOk, btnCancel);
        FormLayout.AddFullWidth(table, actionBar, FormLayout.ActionRowHeight);

        popup.Controls.Add(table);
        popup.AcceptButton = btnOk;
        popup.CancelButton = btnCancel;

        if (popup.ShowDialog(this) == DialogResult.OK)
        {
            // Sync ngược từ popup → form fields
            foreach (var (_, source, txt) in fields)
                source.Text = txt.Text;
        }

        static TextBox MakePopupTxt(TextBox source) => new()
        {
            Text = source.Text,
            Font = AgrisTheme.Body(11F),
            Dock = DockStyle.Fill,
            PlaceholderText = source.PlaceholderText,
            ReadOnly = source.ReadOnly,
            BackColor = source.ReadOnly ? AgrisTheme.ReadOnlyBg : Color.FromArgb(255, 255, 248),
        };
    }

    /// <summary>Popup chi tiết chuyến vật tư (ERP) — hiện toàn bộ thông tin đơn hàng + nhập tay.</summary>
    private void ShowMaterialDetailPopup()
    {
        using var popup = new Form
        {
            Text = "📋 Thông tin chi tiết chuyến vật tư",
            Size = new Size(620, 620),
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

        var fields = new (string Label, TextBox Source, TextBox Popup)[]
        {
            ("Rơ moóc", _txtTrailer, MakeTxt(_txtTrailer)),
            ("Nhà vận chuyển", _txtTransporter, MakeTxt(_txtTransporter)),
            ("Lái xe", _txtDriver, MakeTxt(_txtDriver)),
            ("Kho", _txtWarehouse, MakeTxt(_txtWarehouse)),
            ("Hàng hóa", _txtCommodity, MakeTxt(_txtCommodity)),
            ("Khách hàng/NCC", _txtMaterialCustomer, MakeTxt(_txtMaterialCustomer)),
            ("Ngày nhận hàng", _txtReceiveDate, MakeTxt(_txtReceiveDate)),
        };

        foreach (var (label, _, txt) in fields)
            FormLayout.AddField(table, label, txt);

        var btnOk = GridBuilder.PrimaryButton("Lưu thay đổi", new Size(160, 42));
        btnOk.DialogResult = DialogResult.OK;
        var btnCancel = GridBuilder.SecondaryButton("Đóng", new Size(120, 42));
        btnCancel.DialogResult = DialogResult.Cancel;

        var actionBar = FormLayout.ActionBar(btnOk, btnCancel);
        FormLayout.AddFullWidth(table, actionBar, FormLayout.ActionRowHeight);

        popup.Controls.Add(table);
        popup.AcceptButton = btnOk;
        popup.CancelButton = btnCancel;

        if (popup.ShowDialog(this) == DialogResult.OK)
        {
            foreach (var (_, source, txt) in fields)
                source.Text = txt.Text;
        }

        static TextBox MakeTxt(TextBox source) => new()
        {
            Text = source.Text,
            Font = AgrisTheme.Body(11F),
            Dock = DockStyle.Fill,
            PlaceholderText = source.PlaceholderText,
            ReadOnly = source.ReadOnly,
            BackColor = source.ReadOnly ? AgrisTheme.ReadOnlyBg : Color.FromArgb(255, 255, 248),
        };
    }

    /// <summary>
    /// Kiểm tra thông tin đơn hàng từ ERP theo Số TRIP.
    /// Biển số nhập tay (ERP không có biển số).
    /// Nếu tìm thấy → điền chi tiết (NVC, Kho, Hàng hóa, KH), bật nút Lưu.
    /// Nếu không → hỏi phát QR tạm.
    /// </summary>
    private async Task CheckErpAsync()
    {
        var tripNumber = _txtTripNumber.Text.Trim();
        var plate = _txtMaterialPlate.Text.Trim();

        if (string.IsNullOrWhiteSpace(tripNumber) && string.IsNullOrWhiteSpace(plate))
        {
            MessageBox.Show("Vui lòng nhập Số TRIP hoặc Biển số trước khi kiểm tra.",
                "Thiếu thông tin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        AppendNotification($"Kiểm tra ERP: TRIP={tripNumber}, Biển số={plate}...");

        try
        {
            var bu = Program.ActiveOrganizationUnitCode ?? "TTCS";
            TTC.Weighing.Infrastructure.Adapters.ErpOrderLine? erpLine = null;
            try
            {
                // Tìm theo số TRIP/shipment trước, nếu không có thì tìm theo biển số
                var searchKey = !string.IsNullOrWhiteSpace(tripNumber) ? tripNumber : null;
                var searchPlate = !string.IsNullOrWhiteSpace(plate) ? plate : null;
                erpLine = await _presenter.LookupErpOrderAsync(bu, searchKey, searchPlate, FormToken);
            }
            catch { /* API lỗi → erpLine remains null */ }

            if (erpLine is not null)
            {
                _lastErpOrderLine = erpLine;
                _txtMaterialShipment.Text = erpLine.ShipmentNumber ?? "";

                // Biển số: ưu tiên từ ERP (type 06 có plate), fallback nhập tay
                var erpPlate = erpLine.PlateNumber;
                if (!string.IsNullOrWhiteSpace(erpPlate))
                {
                    _txtMaterialPlateDetail.Text = erpPlate;
                    _txtMaterialPlate.Text = erpPlate;
                    _txtPlate.Text = erpPlate;
                }
                else
                {
                    _txtMaterialPlateDetail.Text = plate;
                    _txtPlate.Text = plate;
                }

                // Auto-fill số TRIP nếu user chỉ nhập biển số
                if (string.IsNullOrWhiteSpace(tripNumber) && !string.IsNullOrWhiteSpace(erpLine.ShipmentNumber))
                    _txtTripNumber.Text = erpLine.ShipmentNumber;

                _txtTransporter.Text = erpLine.TransporterName ?? erpLine.Transporter ?? "";
                _txtDriver.Text = erpLine.DriverName ?? "";
                _txtWarehouse.Text = !string.IsNullOrWhiteSpace(erpLine.WarehouseCode)
                    ? $"{erpLine.WarehouseCode} - {erpLine.WarehouseName}"
                    : erpLine.WarehouseName ?? "";
                _txtCommodity.Text = erpLine.ItemDescription ?? erpLine.ItemName ?? "";
                _txtMaterialCustomer.Text = erpLine.VendorName ?? erpLine.CustomerName ?? "";
                _txtReceiveDate.Text = erpLine.TransactionDate ?? "";

                _txtShipment.Text = erpLine.ShipmentNumber ?? tripNumber;
                _btnSave.Enabled = true;

                var typeInfo = !string.IsNullOrWhiteSpace(erpLine.TransactionTypeName)
                    ? $" [{erpLine.TransactionTypeCode} - {erpLine.TransactionTypeName}]"
                    : "";
                AppendNotification($"✅ ERP: {erpLine.ShipmentNumber} — {erpLine.ItemDescription ?? erpLine.ItemName}{typeInfo}");
                AppendNotification($"   KH/NCC: {erpLine.VendorName ?? erpLine.CustomerName} | Org: {erpLine.OrganizationCode} | Biển số: {erpLine.PlateNumber ?? "(không có)"}");
            }
            else
            {
                AppendNotification("❌ Không tìm thấy đơn hàng trên ERP.");

                var answer = MessageBox.Show(
                    $"Không tìm thấy đơn hàng cho TRIP={tripNumber}, Biển số={plate} trên ERP.\n\n" +
                    "Bạn có muốn cho xe vào và phát QR tạm?",
                    "Không tìm thấy đơn hàng",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (answer == DialogResult.Yes)
                {
                    _txtMaterialShipment.Text = tripNumber;
                    _txtMaterialPlateDetail.Text = plate;
                    _txtPlate.Text = plate;
                    _txtShipment.Text = tripNumber;
                    _btnSave.Enabled = true;
                    AppendNotification("📋 Đã chấp nhận QR tạm. Điền thông tin và bấm Đăng tài (F4).");
                }
            }
        }
        catch (Exception ex)
        {
            AppendNotification($"⚠ Lỗi kiểm tra ERP: {ex.Message}");
            MessageBox.Show($"Lỗi kết nối ERP:\n{ex.Message}\n\nChỉ cho phép phát QR tạm.",
                "Lỗi ERP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// Tải danh sách hồ sơ nghiệp vụ đã duyệt từ server, populate vào ComboBox.
    /// </summary>
    private async Task LoadProfilesAsync()
    {
        try
        {
            _availableProfiles = await _presenter.GetApprovedProfilesAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            AppendNotification(Loc.T("reg.load_profiles_error", ex.Message));
            _availableProfiles = [];
        }
    }

    /// <summary>Tải danh sách biển số FRM vào ComboBox autocomplete.</summary>
    private void LoadPlateComboBox()
    {
        try
        {
            var plates = _presenter.GetAllPlates();
            _allPlates = plates.ToArray();

            if (plates.Count == 0)
            {
                // Xe (the-xe/xe-van-chuyen) có thể rỗng — lệnh đốn vẫn dùng được qua GetPlatesInDateRange
                RefreshPlateFilter();
                return;
            }

            // Lọc theo khung ngày mặc định
            RefreshPlateFilter();
            var stats = _presenter.FrmCacheStats;
            AppendNotification($"✅ Đã tải {plates.Count} biển số xe từ FRM. (Có thẻ: {stats.withCard}, Chỉ XVC: {stats.withoutCard})");
        }
        catch (Exception ex)
        {
            AppendNotification($"⚠ Lỗi tải biển số: {ex.Message}");
        }
    }

    /// <summary>
    /// Lọc danh sách biển số theo khung ngày (Từ — Đến).
    /// Chỉ hiện xe có thẻ FRM hiệu lực trong khoảng đã chọn.
    /// </summary>
    private async void RefreshPlateFilter()
    {
        var from = _dtpFrmFrom.Value.Date;
        var to = _dtpFrmTo.Value.Date.AddDays(1).AddSeconds(-1);

        try
        {
            _filteredPlates = await _presenter.GetPlatesInDateRangeAsync(from, to, _cts.Token);
        }
        catch
        {
            _filteredPlates = _presenter.GetPlatesInDateRange(from, to);
        }

        // Fallback: nếu thẻ xe rỗng, tìm biển số từ lệnh đốn đã duyệt trong khung ngày (theo NgayCap hoặc NgayCapNhat)
        if (_filteredPlates.Length == 0)
        {
            var ldPlates = _presenter.GetLenhDonPlatesInDateRange(from, to);
            if (ldPlates.Length > 0)
                _filteredPlates = ldPlates;
        }

        _lstPlatePopup.Visible = false;

        AppendNotification($"🔍 {_filteredPlates.Length} xe hiệu lực từ {from:dd/MM/yyyy} đến {_dtpFrmTo.Value:dd/MM/yyyy}");
    }

    /// <summary>
    /// Trả về WorkflowProfile tương ứng với loại nghiệp vụ đã chọn trên ComboBox.
    /// Mía nhập → tìm profile CANE_IN; Vật tư → tìm profile MATERIAL_*.
    /// </summary>
    private WorkflowProfile? GetSelectedProfile()
    {
        if (_availableProfiles is null || _availableProfiles.Count == 0)
            return null;

        var isSugarcane = _cboProfileType.SelectedIndex == 0;

        return isSugarcane
            ? _availableProfiles.FirstOrDefault(p =>
                p.Code.StartsWith("CANE", StringComparison.OrdinalIgnoreCase))
            : _availableProfiles.FirstOrDefault(p =>
                p.Code.StartsWith("MATERIAL", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Panel camera: 2 camera views xếp ngang (Camera Vào + Camera Ra).
    /// Req 10.1, 10.3, 10.4.
    /// </summary>
    private Card BuildCameraPanel()
    {
        var card = new Card
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(1)
        };

        var header = GridBuilder.SectionHeader(Loc.T("reg.section_camera"));

        var cameraContainer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = AgrisTheme.Surface
        };
        cameraContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        cameraContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        cameraContainer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _cameraIn.Dock = DockStyle.Fill;
        _cameraIn.Margin = new Padding(4, 4, 2, 4);

        _cameraOut.Dock = DockStyle.Fill;
        _cameraOut.Margin = new Padding(2, 4, 4, 4);

        cameraContainer.Controls.Add(_cameraIn, 0, 0);
        cameraContainer.Controls.Add(_cameraOut, 1, 0);

        card.Controls.Add(cameraContainer);
        card.Controls.Add(header);

        return card;
    }

    /// <summary>
    /// Panel thông báo: RichTextBox ghi nhận sự kiện trong phiên.
    /// Req 6.4.
    /// </summary>
    private Card BuildNotificationPanel()
    {
        var card = new Card
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(1)
        };

        var header = GridBuilder.SectionHeader(Loc.T("reg.section_notification"));

        _rtbNotifications.Dock = DockStyle.Fill;
        _rtbNotifications.ReadOnly = true;
        _rtbNotifications.BackColor = AgrisTheme.Surface;
        _rtbNotifications.BorderStyle = BorderStyle.None;
        _rtbNotifications.Font = AgrisTheme.Mono(9F);
        _rtbNotifications.ForeColor = AgrisTheme.TextPrimary;
        _rtbNotifications.ScrollBars = RichTextBoxScrollBars.Vertical;
        _rtbNotifications.AccessibleName = Loc.T("reg.section_notification");

        card.Controls.Add(_rtbNotifications);
        card.Controls.Add(header);

        return card;
    }

    /// <summary>
    /// Thanh nút hành động: Đăng tài (F4), In phiếu (F5), Làm mới (Esc).
    /// </summary>
    private Card BuildActionBar()
    {
        var card = new Card
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 2, 8, 2)
        };

        var bar = FormLayout.ActionBar(_btnSave, _btnPrint, _btnRetryDigi, _btnCancel, _btnReset);
        bar.Dock = DockStyle.Fill;

        card.Controls.Add(bar);

        return card;
    }

    /// <summary>
    /// Lưới chuyến — tab lọc nhanh, bộ lọc khoảng ngày, DataGridView đầy đủ cột.
    /// Req 7.1, 7.2, 7.3.
    /// </summary>
    private Card BuildGridArea()
    {
        var card = new Card
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(1)
        };

        var header = GridBuilder.SectionHeader(Loc.T("reg.section_grid"));

        // ── Filter toolbar: tabs + date range ──
        var filterBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 42,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = AgrisTheme.SurfaceMuted,
            Padding = new Padding(8, 5, 8, 5)
        };

        _btnTabInFactory.Margin = new Padding(0, 0, 4, 0);
        _btnTabExited.Margin = new Padding(0, 0, 4, 0);
        _btnTabCancelled.Margin = new Padding(0, 0, 16, 0);

        var lblFrom = new Label
        {
            Text = Loc.T("reg.filter_from"),
            AutoSize = true,
            Font = AgrisTheme.Body(9F),
            ForeColor = AgrisTheme.TextMuted,
            Margin = new Padding(0, 6, 4, 0)
        };

        var lblTo = new Label
        {
            Text = "→",
            AutoSize = true,
            Font = AgrisTheme.Body(9F),
            ForeColor = AgrisTheme.TextMuted,
            Margin = new Padding(4, 6, 4, 0)
        };

        _dtpFrom.Margin = new Padding(0, 2, 0, 0);
        _dtpTo.Margin = new Padding(0, 2, 0, 0);

        filterBar.Controls.AddRange([
            _btnTabInFactory, _btnTabExited, _btnTabCancelled,
            lblFrom, _dtpFrom, lblTo, _dtpTo
        ]);

        GridBuilder.AttachEmptyState(_dgvTrips, Loc.T("reg.no_trips"));

        card.Controls.Add(_dgvTrips);
        card.Controls.Add(filterBar);
        card.Controls.Add(header);

        return card;
    }

    /// <summary>
    /// Thanh trạng thái: Server, Barcode, LPR, Camera.
    /// Req 6.5.
    /// </summary>
    private StatusStrip BuildStatusBar()
    {
        _statusBar.Dock = DockStyle.Fill;
        _statusBar.BackColor = AgrisTheme.SurfaceMuted;
        _statusBar.SizingGrip = false;
        _statusBar.Font = AgrisTheme.Body(8.5F);

        ConfigureStatusLabel(_lblServerStatus, "Server", AgrisTheme.Success);
        ConfigureStatusLabel(_lblBarcodeStatus, "Barcode", AgrisTheme.Success);
        ConfigureStatusLabel(_lblLprStatus, "LPR", AgrisTheme.Warning);
        ConfigureStatusLabel(_lblCameraStatus, "Camera", AgrisTheme.Success);

        _statusBar.Items.AddRange([
            _lblServerStatus,
            new ToolStripSeparator(),
            _lblBarcodeStatus,
            new ToolStripSeparator(),
            _lblLprStatus,
            new ToolStripSeparator(),
            _lblCameraStatus
        ]);

        return _statusBar;
    }

    // ═══════════════════════════════════════════════════════════
    //  Initialization
    // ═══════════════════════════════════════════════════════════

    private void InitializeVehicleTypeCombo()
    {
        // Không còn ComboBox — loại xe là read-only TextBox lấy từ FRM
    }

    // ═══════════════════════════════════════════════════════════
    //  Grid configuration (Req 7.1, 7.2, 7.3, 7.4, 7.5)
    // ═══════════════════════════════════════════════════════════

    /// <summary>Cấu hình cột cho lưới chuyến theo TripRegistrationRow.</summary>
    private void ConfigureGridColumns()
    {
        _dgvTrips.Columns.AddRange(
            GridBuilder.Column(Loc.T("col.reg_no"), nameof(TripRegistrationRow.TripNo), 100),
            GridBuilder.Column(Loc.T("col.shipment"), nameof(TripRegistrationRow.ShipmentNo), 100),
            GridBuilder.Column(Loc.T("col.plate"), nameof(TripRegistrationRow.Plate), 90),
            GridBuilder.Column(Loc.T("col.entry_time"), nameof(TripRegistrationRow.EntryTime), 120),
            GridBuilder.Column(Loc.T("col.exit_time"), nameof(TripRegistrationRow.ExitTime), 120),
            GridBuilder.Column(Loc.T("col.customer"), nameof(TripRegistrationRow.CustomerOrSupplier), 110),
            GridBuilder.Column(Loc.T("col.commodity"), nameof(TripRegistrationRow.Commodity), 90),
            GridBuilder.Column(Loc.T("col.vehicle_type"), nameof(TripRegistrationRow.VehicleType), 80),
            GridBuilder.Column("Trạng thái", nameof(TripRegistrationRow.ProcessStatus), 120));

        _dgvTrips.CellClick += OnGridCellClick;
    }

    /// <summary>Gắn event handler cho tab lọc nhanh (Req 7.2).</summary>
    private void InitializeGridTabs()
    {
        _btnTabInFactory.Click += async (_, _) => await SetActiveTabAsync(TripGridTab.InFactory);
        _btnTabExited.Click += async (_, _) => await SetActiveTabAsync(TripGridTab.Exited);
        _btnTabCancelled.Click += async (_, _) => await SetActiveTabAsync(TripGridTab.Cancelled);
    }

    /// <summary>Gắn event handler cho bộ lọc khoảng ngày (Req 7.3).</summary>
    private void InitializeDateFilter()
    {
        _dtpFrom.Value = DateTime.Today.AddDays(-7); // Mặc định 7 ngày gần nhất
        _dtpTo.Value = DateTime.Today.AddDays(1);

        _dtpFrom.ValueChanged += async (_, _) => await LoadGridAsync();
        _dtpTo.ValueChanged += async (_, _) => await LoadGridAsync();
    }

    /// <summary>Khởi tạo timer tự động refresh lưới mỗi 30 giây (Req 7.5).</summary>
    private void InitializeGridRefreshTimer()
    {
        _gridRefreshTimer.Tick += async (_, _) => await LoadGridAsync();
        _gridRefreshTimer.Start();

        // Tạm dừng timer khi tab không hiển thị để giảm tải CPU/IO.
        // Ẩn status bar khi tab không active — tránh chiếm không gian tab khác.
        VisibleChanged += (_, _) =>
        {
            if (Visible) _gridRefreshTimer.Start();
            else _gridRefreshTimer.Stop();
            _statusBar.Visible = Visible;
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  Keyboard shortcuts (Req 6.3) — task 6.5
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Phím tắt toàn form: F4 → Lưu phiếu, F5 → In phiếu, F6 → Xác nhận, Esc → Làm mới.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.F4:
                _btnSave.PerformClick();
                return true;
            case Keys.F5:
                _btnPrint.PerformClick();
                return true;
            case Keys.Escape:
                _btnReset.PerformClick();
                return true;
            default:
                return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Event handlers (task 6.5 — Req 5.2, 6.3, 6.4, 6.5, 8.3)
    // ═══════════════════════════════════════════════════════════

    /// <summary>Gắn event handlers cho các nút hành động. Gọi từ constructor.</summary>
    private void WireEventHandlers()
    {
        _txtPlate.TextChanged += OnPlateTextChanged;
        _txtPlate.KeyDown += OnPlateKeyDown;
        _txtPlate.Leave += OnPlateLeave;
        _btnPlateDropdown.Click += OnPlateDropdownClicked;
        _lstPlatePopup.Click += OnPlatePopupClick;
        _lstPlatePopup.KeyDown += OnPlatePopupKeyDown;
        _btnSave.Click += OnSaveClicked;
        _btnPrint.Click += OnPrintClicked;
        _btnRetryDigi.Click += async (_, _) => await RetryPushDangTaiDigiAsync();
        _btnCancel.Click += async (_, _) => await CancelSelectedTripAsync();
        _btnReset.Click += OnResetClicked;
    }

    /// <summary>
    /// Bấm nút ▼ → hiện toàn bộ danh sách biển số xe trong khung ngày (không cần gõ).
    /// </summary>
    private void OnPlateDropdownClicked(object? sender, EventArgs e)
    {
        // Vật tư không dùng dropdown biển số FRM
        if (_cboProfileType.SelectedIndex != 0)
        {
            _lstPlatePopup.Visible = false;
            return;
        }

        if (_lstPlatePopup.Visible)
        {
            _lstPlatePopup.Visible = false;
            return;
        }

        if (_filteredPlates.Length == 0)
        {
            AppendNotification("⚠ Không có xe nào hiệu lực trong khung ngày đã chọn.");
            return;
        }

        var toShow = _filteredPlates.Take(200).ToArray();

        ShowPlatePopup(toShow);
        _lstPlatePopup.Focus();
        if (_lstPlatePopup.Items.Count > 0)
            _lstPlatePopup.SelectedIndex = 0;
    }

    /// <summary>
    /// Mỗi ký tự gõ vào ô biển số → filter ngay lập tức, hiện ListBox popup bên dưới.
    /// </summary>
    private void OnPlateTextChanged(object? sender, EventArgs e)
    {
        // Vật tư không dùng autocomplete biển số FRM — biển số nhập tay
        if (_cboProfileType.SelectedIndex != 0)
        {
            _lstPlatePopup.Visible = false;
            return;
        }

        var text = _txtPlate.Text.Trim();

        if (text.Length == 0)
        {
            _lstPlatePopup.Visible = false;
            return;
        }

        var search = text.Replace(" ", "").Replace("-", "").Replace(".", "").ToUpperInvariant();

        // Tìm trong danh sách đã filter — fallback tìm trực tiếp trong cache nếu chưa load
        var source = _filteredPlates.Length > 0
            ? _filteredPlates
            : _presenter.GetPlatesInDateRange(_dtpFrmFrom.Value.Date, _dtpFrmTo.Value.Date.AddDays(1));

        var filtered = source
            .Where(p => p.Replace(" ", "").Replace("-", "").Replace(".", "")
                .ToUpperInvariant()
                .Contains(search))
            .Take(30)
            .ToArray();

        if (filtered.Length == 0)
        {
            _lstPlatePopup.Visible = false;
            return;
        }

        ShowPlatePopup(filtered);
    }

    /// <summary>Hiện ListBox popup ngay bên dưới ô biển số với danh sách đã cho.</summary>
    private void ShowPlatePopup(string[] plates)
    {
        _lstPlatePopup.Items.Clear();
        _lstPlatePopup.Items.AddRange(plates);

        // Đặt popup ngay bên dưới TextBox
        var screenPos = _txtPlate.Parent!.PointToScreen(new Point(0, _txtPlate.Parent.Height));
        var formPos = PointToClient(screenPos);
        _lstPlatePopup.Location = formPos;
        _lstPlatePopup.Width = _txtPlate.Parent.Width;
        _lstPlatePopup.Height = Math.Min(plates.Length * _lstPlatePopup.ItemHeight + 4, 250);
        _lstPlatePopup.BringToFront();
        _lstPlatePopup.Visible = true;
    }

    /// <summary>Mũi tên xuống → focus vào ListBox popup. Enter → chọn item đầu tiên hoặc lookup trực tiếp.</summary>
    private void OnPlateKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;

            if (_lstPlatePopup.Visible && _lstPlatePopup.Items.Count > 0)
            {
                // Popup mở → chọn item đầu tiên
                SelectPlateFromPopup(_lstPlatePopup.Items[0]?.ToString());
            }
            else
            {
                // Popup đóng → lookup biển số đã gõ
                _lstPlatePopup.Visible = false;
                var plate = _txtPlate.Text.Trim();
                if (!string.IsNullOrWhiteSpace(plate) && plate.Length >= 2)
                    _ = LookupAndPopulateAsync(plate);
            }
            return;
        }

        if (!_lstPlatePopup.Visible) return;

        if (e.KeyCode == Keys.Down)
        {
            _lstPlatePopup.Focus();
            if (_lstPlatePopup.Items.Count > 0)
                _lstPlatePopup.SelectedIndex = 0;
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            _lstPlatePopup.Visible = false;
            e.Handled = true;
        }
    }

    /// <summary>Click item trong popup → chọn biển số đó.</summary>
    private void OnPlatePopupClick(object? sender, EventArgs e)
    {
        if (_lstPlatePopup.SelectedItem is string plate)
            SelectPlateFromPopup(plate);
    }

    /// <summary>Enter hoặc mũi tên trong popup.</summary>
    private void OnPlatePopupKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && _lstPlatePopup.SelectedItem is string plate)
        {
            SelectPlateFromPopup(plate);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            _lstPlatePopup.Visible = false;
            _txtPlate.Focus();
            e.Handled = true;
        }
    }

    /// <summary>Chọn biển số từ popup → điền vào TextBox, ẩn popup, tự lookup.</summary>
    private async void SelectPlateFromPopup(string? plate)
    {
        if (string.IsNullOrWhiteSpace(plate)) return;

        _lstPlatePopup.Visible = false;
        _txtPlate.TextChanged -= OnPlateTextChanged; // Tạm tắt để setText không trigger lại popup
        _txtPlate.Text = plate;
        _txtPlate.SelectionStart = plate.Length;
        _txtPlate.TextChanged += OnPlateTextChanged;
        _txtPlate.Focus();

        await LookupAndPopulateAsync(plate);
    }

    /// <summary>
    /// Khi user rời ô biển số (gõ tay xong tab ra) → ẩn popup và tra cứu.
    /// Không delay — kiểm tra ngay. Skip nếu đã lookup rồi (tránh trùng với SelectPlateFromPopup/Enter).
    /// </summary>
    private async void OnPlateLeave(object? sender, EventArgs e)
    {
        // Nếu focus chuyển sang popup → không ẩn, không lookup
        await Task.Yield(); // Cho WinForms xử lý focus xong
        if (_lstPlatePopup.Focused || _lstPlatePopup.ContainsFocus) return;

        _lstPlatePopup.Visible = false;

        // Nếu đang lookup hoặc đã lookup biển số này rồi → skip
        if (_lookupInProgress) return;

        var plate = PlateText;
        if (string.IsNullOrWhiteSpace(plate) || plate.Length < 3) return;

        var normalizedPlate = NormalizePlate(plate);
        if (_lastLookedUpPlate is not null
            && string.Equals(_lastLookedUpPlate, normalizedPlate, StringComparison.Ordinal))
            return;

        await LookupAndPopulateAsync(plate);
    }

    /// <summary>
    /// Tra cứu biển số qua Presenter (cache) và tự điền thông tin.
    /// Thay thế OnCheckPlateClicked cũ.
    /// </summary>
    private async Task LookupAndPopulateAsync(string plate)
    {
        if (_lookupInProgress) return;
        _lookupInProgress = true;
        SetUiBusy(true);
        try
        {
            var normalizedPlate = NormalizePlate(plate);

            // 1. Tìm LenhDon theo biển số (từ cache — nhanh, không cần API)
            var lenhDon = _presenter.FindLenhDonByPlate(plate);
            if (lenhDon is not null)
            {
                // Điền dữ liệu lệnh đốn ngay lập tức
                _txtShipment.Text = lenhDon.MaLD;
                _txtVehicleType.Text = lenhDon.LoaixeName ?? "";
                _txtSugarcaneOwner.Text = lenhDon.KHName ?? "";
                _txtSugarcaneType.Text = lenhDon.LoaiMiaName ?? "";
                _txtSugarcaneVariety.Text = lenhDon.GiongMiaName ?? "";
                _txtStation.Text = lenhDon.TramName ?? "";
                _txtCropSeason.Text = lenhDon.VudautuName ?? "";
                _txtHarvestMethod.Text = lenhDon.DoiTacThuHoachName ?? "";
                _txtTaiTrong.Text = lenhDon.TaiTrong.HasValue ? $"{lenhDon.TaiTrong:N1}" : "";
                _txtMaThe.Text = lenhDon.Thexename ?? "";

                // Nhà vận chuyển từ the-xe (cache)
                var vehicles = _presenter.FindVehicleByPlate(plate);
                _txtCustomerOrSupplier.Text = vehicles.FirstOrDefault(v => v.DoiTacVanChuyenName is not null)?.DoiTacVanChuyenName ?? "";

                AppendNotification($"✅ Lệnh đốn: {lenhDon.MaLD} | Chủ mía: {lenhDon.KHName} | Loại mía: {lenhDon.LoaiMiaName} | Giống: {lenhDon.GiongMiaName} | Trạm: {lenhDon.TramName} | Loại xe: {lenhDon.LoaixeName}");

                // Đánh dấu đã lookup thành công
                _lastLookedUpPlate = normalizedPlate;

                // 2. Check lệnh đốn đã đăng tài → popup ngay
                var existingTrip = await _presenter.FindActiveTripByLenhDonAsync(lenhDon.SourceKey, FormToken);
                if (existingTrip is not null)
                {
                    AppendNotification($"⚠ Lệnh đốn {lenhDon.MaLD} đã được đăng tài (số: {existingTrip}).");
                    Helpers.DigiScalePopup.Show(this,
                        "XE ĐÃ ĐĂNG TÀI",
                        $"Biển số: {plate}\nLệnh đốn: {lenhDon.MaLD}\nSố đăng tài: {existingTrip}\n\nPhải hủy phiếu cũ trước khi đăng tài lại.",
                        false, "Lệnh đốn đã có phiếu đăng tài active");
                    return;
                }

                _selectedPlan = null; // Không dùng plan từ CheckPlanAsync — dữ liệu đã có từ lệnh đốn
            }
            else
            {
                AppendNotification($"❌ Không tìm thấy lệnh đốn cho biển số {plate}. Có thể đăng tài tạm.");
                _selectedPlan = null;
                _lastLookedUpPlate = normalizedPlate;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppendNotification($"⚠ Lỗi: {ex.Message}");
        }
        finally
        {
            _lookupInProgress = false;
            SetUiBusy(false);
        }
    }

    private static string NormalizePlate(string? plate)
        => plate is null
            ? string.Empty
            : plate.Replace(" ", "").Replace("-", "").Replace(".", "").ToUpperInvariant();

    /// <summary>
    /// Lưu phiếu (F4) — gọi RegisterAsync hoặc RegisterTempAsync tùy context (Req 3.1, 4.1).
    /// Nếu có kế hoạch đã chọn → đăng tài chính thức. Không có → phát QR tạm.
    /// </summary>
    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        var plate = PlateText;
        if (string.IsNullOrWhiteSpace(plate))
        {
            MessageBox.Show(Loc.T("reg.save_missing_plate"),
                Loc.T("common.missing_info"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SetUiBusy(true);
        try
        {
            // ── Cập nhật chuyến đã chọn từ lưới ──
            if (_lastRegisteredTripId.HasValue && _dgvTrips.CurrentRow?.DataBoundItem is TripRegistrationRow)
            {
                AppendNotification(Loc.T("reg.updating", _txtTripNo.Text));

                var vehicleType = GetSelectedVehicleType();

                var (ok, failReason) = await _presenter.UpdateTripAsync(
                    _lastRegisteredTripId.Value,
                    plate,
                    null, // trailerPlate — chưa có field trên form
                    _txtShipment.Text.Trim().Length > 0 ? _txtShipment.Text.Trim() : null,
                    _txtCustomerOrSupplier.Text.Trim().Length > 0 ? _txtCustomerOrSupplier.Text.Trim() : null,
                    _txtSugarcaneOwner.Text.Trim().Length > 0 ? _txtSugarcaneOwner.Text.Trim() : null,
                    _txtSugarcaneType.Text.Trim().Length > 0 ? _txtSugarcaneType.Text.Trim() : null,
                    _txtSugarcaneVariety.Text.Trim().Length > 0 ? _txtSugarcaneVariety.Text.Trim() : null,
                    vehicleType,
                    FormToken);

                if (!ok)
                {
                    AppendNotification($"Cập nhật thất bại: {failReason}");
                    MessageBox.Show(failReason ?? "Lỗi không xác định",
                        Loc.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                AppendNotification(Loc.T("reg.update_ok", _txtTripNo.Text));
                await LoadGridAsync();

                MessageBox.Show(Loc.T("reg.update_ok", _txtTripNo.Text),
                    Loc.T("common.success"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // ── Tạo chuyến mới từ dữ liệu đang gõ trên form ──
            RefreshDateTime();

            var isSugarcane = _cboProfileType.SelectedIndex == 0;
            // Mía: có lệnh đốn FRM → luôn chính thức.
            // Vật tư: có shipment number → chính thức. Chỉ tạm khi không có gì.
            var isTemp = isSugarcane
                ? _presenter.FindLenhDonByPlate(plate) is null
                : string.IsNullOrWhiteSpace(_txtShipment.Text.Trim()) && _selectedPlan is null;

            // ═══════════════════════════════════════════════════════════
            //  Validations cho mía (FRM) — check lệnh đốn + thẻ xe
            // ═══════════════════════════════════════════════════════════
            if (isSugarcane)
            {
                var ldCheck = _presenter.FindLenhDonByPlate(plate);

                if (ldCheck is not null)
                {
                    var now = DateTimeOffset.Now;

                    // 1. Lệnh đốn Nháp → chặn
                    if (ldCheck.StatusCodeName != "Đã duyệt" && ldCheck.StatusCodeName != null)
                    {
                        Helpers.DigiScalePopup.ShowWarning(this,
                            "Lệnh đốn chưa duyệt",
                            $"Lệnh đốn {ldCheck.MaLD} chưa ở trạng thái duyệt.\n\nTrạng thái hiện tại: {ldCheck.StatusCodeName}");
                        return;
                    }

                    // 2. Lệnh đốn hết hạn (tính đến phút)
                    if (ldCheck.NgayHetHan is not null && DateTimeOffset.TryParse(ldCheck.NgayHetHan, out var ngayHetHan) && ngayHetHan < now)
                    {
                        Helpers.DigiScalePopup.ShowWarning(this,
                            "Lệnh đốn hết hạn",
                            $"Lệnh đốn {ldCheck.MaLD} đã hết hạn.\n\nNgày hết hạn: {ngayHetHan:dd/MM/yyyy HH:mm}\nThời gian hiện tại: {now:dd/MM/yyyy HH:mm}");
                        return;
                    }

                    // 3. Lệnh đốn chưa tới ngày cấp
                    if (ldCheck.NgayCap is not null && DateTimeOffset.TryParse(ldCheck.NgayCap, out var ngayCap) && ngayCap > now)
                    {
                        Helpers.DigiScalePopup.ShowWarning(this,
                            "Lệnh đốn chưa hiệu lực",
                            $"Lệnh đốn {ldCheck.MaLD} có thời gian cấp lớn hơn thời gian hiện tại.\n\nNgày cấp: {ngayCap:dd/MM/yyyy HH:mm}\nThời gian hiện tại: {now:dd/MM/yyyy HH:mm}");
                        return;
                    }

                    // 4. Lệnh đốn đã đăng tài (check trong DB trips active)
                    var existingTrip = await _presenter.FindActiveTripByLenhDonAsync(ldCheck.SourceKey, FormToken);
                    if (existingTrip is not null)
                    {
                        Helpers.DigiScalePopup.ShowWarning(this,
                            "Lệnh đốn đã đăng tài",
                            $"Lệnh đốn {ldCheck.MaLD} đã được đăng tài.\n\nSố đăng tài: {existingTrip}\n\nMỗi lệnh đốn chỉ được đăng tài 1 lần\n(trừ khi phiếu cũ đã hủy hoặc xóa).");
                        return;
                    }

                    // 5. Check thẻ xe (từ FrmVehicleInfo)
                    var vehicles = _presenter.FindVehicleByPlate(plate);
                    if (vehicles.Count > 0)
                    {
                        var theXe = vehicles.FirstOrDefault(v => v.MaThe is not null);
                        if (theXe is not null)
                        {
                            var status = theXe.StatusName?.ToUpperInvariant() ?? "";
                            if (status.Contains("HỦY") || status.Contains("HẾT HẠN") || status.Contains("INACTIVE") || status.Contains("CANCEL"))
                            {
                                Helpers.DigiScalePopup.ShowWarning(this,
                                    "Thẻ xe không hợp lệ",
                                    $"Lệnh đốn có thẻ xe đã Hủy hoặc hết hạn.\n\nThẻ xe: {theXe.MaThe}\nTrạng thái: {theXe.StatusName}");
                                return;
                            }
                            if (status.Contains("NHÁP") || status.Contains("DRAFT"))
                            {
                                Helpers.DigiScalePopup.ShowWarning(this,
                                    "Thẻ xe chưa duyệt",
                                    $"Lệnh đốn có thẻ xe chưa được duyệt.\n\nThẻ xe: {theXe.MaThe}\nTrạng thái: {theXe.StatusName}");
                                return;
                            }
                        }
                    }
                }

                // 6. Xe chưa cân ra → chặn đăng tài tiếp
                var activeTrip = await _presenter.FindActiveTripByPlateAsync(plate, FormToken);
                if (activeTrip is not null)
                {
                    Helpers.DigiScalePopup.ShowWarning(this,
                        "Xe chưa cân ra",
                        $"Xe {plate} đang có chuyến chưa cân ra.\n\nSố đăng tài: {activeTrip}\n\nXe phải cân ra trước khi đăng tài tiếp.");
                    return;
                }
            }

            var cmd = BuildRegisterCommand(isTemp);

            AppendNotification(isTemp
                ? Loc.T("reg.registering_temp", plate)
                : Loc.T("reg.registering_full", plate, _selectedPlan?.ReferenceNo ?? _txtShipment.Text.Trim()));

            var result = isTemp
                ? await _presenter.RegisterTempAsync(cmd, FormToken)
                : await _presenter.RegisterAsync(cmd, FormToken);

            if (!result.IsSuccess)
            {
                AppendNotification(Loc.T("reg.reg_failed", result.FailureReason));
                MessageBox.Show(Loc.T("reg.reg_cannot", result.FailureReason),
                    Loc.T("reg.reg_failed_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Cập nhật form với kết quả
            SetTripNo(result.TripNo ?? string.Empty);
            _lastRegisteredTripId = result.TripId;
            _lastQrToken = result.QrToken;
            _lastQueueSequenceNumber = result.QueueSequenceNumber;

            var queueInfo = result.QueueSequenceNumber.HasValue
                ? Loc.T("reg.queue_info", result.QueueSequenceNumber)
                : string.Empty;

            AppendNotification(Loc.T("reg.reg_ok", result.TripNo, queueInfo, result.QrType));

            // Push phiếu đăng tài lên DigiScale — cho cả mía và vật tư
            string digiStatus = "";
            bool digiOk = false;

            if (!isTemp)
            {
                try
                {
                    if (isSugarcane)
                    {
                        // ═══ MÍA → push phiếu đăng tài FRM (new_phieudangtai) ═══
                        var ldForPush = _presenter.FindLenhDonByPlate(plate);
                        var lenhDonId = ldForPush?.SourceKey ?? "";
                        var maLenhDon = ldForPush?.MaLD ?? _txtShipment.Text.Trim();
                        var loaiXeMa = ldForPush?.LoaixeCode ?? "";

                        var pushResult = await _presenter.PushDangTaiAsync(
                            lenhDonId, maLenhDon, DateTime.Now, loaiXeMa,
                            result.TripNo ?? "", ldForPush?.UuTien, ldForPush?.BusinessUnit, FormToken);

                        AppendNotification($"📤 DigiScale (mía): BU={ldForPush?.BusinessUnit}, LenhDon={lenhDonId}, MaLenhDon={maLenhDon}, SoDangTai={result.TripNo}");

                        digiOk = pushResult.IsSuccess;
                        digiStatus = digiOk
                            ? $"✅ DigiScale: {pushResult.ErrorMessage}"
                            : $"❌ DigiScale: {pushResult.ErrorMessage}";
                    }
                    else
                    {
                        // ═══ VẬT TƯ → chưa có API đăng tài riêng trên DigiScale ═══
                        // DigiScale chỉ có new_phieudangtai (FRM/mía). Vật tư sẽ push phiếu cân (oracle-tickets) sau khi cân.
                        // Đăng tài vật tư chỉ lưu DB local — đẩy lên DigiScale khi có API.
                        digiOk = true;
                        digiStatus = "ℹ Vật tư: Đăng tài lưu local. Đẩy DigiScale khi cân.";
                        AppendNotification(digiStatus);
                    }

                    if (!string.IsNullOrEmpty(digiStatus))
                        AppendNotification(digiStatus);
                }
                catch (Exception pushEx)
                {
                    digiStatus = $"❌ DigiScale lỗi: {pushEx.Message}";
                    AppendNotification(digiStatus);
                }
            }
            else if (isTemp && isSugarcane)
            {
                // Mía tạm (temp QR, không có lệnh đốn) — skip DigiScale push, chỉ lưu DB local
                digiOk = true;
                digiStatus = "ℹ Thẻ xe tạm — không push DigiScale đăng tài (không có lệnh đốn)";
                AppendNotification(digiStatus);
            }
            else if (isTemp)
            {
                // Vật tư tạm — skip
                digiOk = true;
                digiStatus = "ℹ Vật tư tạm — không push DigiScale đăng tài";
                AppendNotification(digiStatus);
            }

            // Popup thông báo — hiện cả trạng thái push DigiScale
            ShowSuccessPopup(result.TripNo ?? "", plate, _txtShipment.Text,
                result.QrType?.ToString() ?? "", digiOk, digiStatus);

            // Thông báo các tab khác: xe mới đăng tài
            _eventBus.Publish(new TripChangedEventArgs
            {
                TripId = result.TripId ?? Guid.Empty,
                TripNo = result.TripNo,
                Plate = PlateText,
                QrContent = result.QrToken,
                ChangeType = TripChangeType.Registered,
                SourceForm = "TripRegistrationForm"
            });

            // Hiển thị QR token để nhân viên biết mã quét (hoặc copy cho test)
            if (!string.IsNullOrWhiteSpace(result.QrToken))
                AppendNotification(Loc.T("reg.qr_token", result.QrToken));

            // Refresh lưới
            await LoadGridAsync();

            // Hiện phiếu đăng tài (popup trên form hiện tại, không nhảy tab)
            if (result.TripId.HasValue)
                ShowTripReceiptPreview(result.TripId.Value, isReprint: false);
        }
        catch (OperationCanceledException)
        {
            // Form đang đóng.
        }
        catch (Exception ex)
        {
            AppendNotification(Loc.T("reg.reg_error", ex.Message));
            MessageBox.Show(Loc.T("reg.reg_error_dialog", ex.Message),
                Loc.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetUiBusy(false);
        }
    }

    /// <summary>
    /// In phiếu (F5) — hiện popup xem trước phiếu đăng tài, cho phép chọn máy in rồi in (Req 8.3).
    /// </summary>
    private void OnPrintClicked(object? sender, EventArgs e)
    {
        if (_lastRegisteredTripId is null)
        {
            MessageBox.Show(Loc.T("reg.print_no_trip"),
                Loc.T("reg.print_no_data"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ShowTripReceiptPreview(_lastRegisteredTripId.Value, isReprint: true);
    }

    /// <summary>
    /// Làm mới (Esc) — reset form về trạng thái trống.
    /// </summary>
    private void OnResetClicked(object? sender, EventArgs e)
    {
        ResetForm();
        AppendNotification(Loc.T("reg.reset_done"));
    }

    // ═══════════════════════════════════════════════════════════
    //  Cancel handler (Req 5.1, 5.2)
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Hủy đăng tài — yêu cầu xác nhận đăng nhập + lý do trước khi gọi CancelAsync (Req 5.2).
    /// Gọi từ context menu hoặc nút hủy trên lưới.
    /// </summary>
    internal async Task CancelSelectedTripAsync()
    {
        if (_dgvTrips.CurrentRow?.DataBoundItem is not TripRegistrationRow row)
        {
            MessageBox.Show(Loc.T("reg.cancel_no_trip"),
                Loc.T("reg.cancel_no_selection"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Kiểm tra trạng thái cho phép hủy
        if (row.Lifecycle is not (TripLifecycle.TempRegistered or TripLifecycle.Registered))
        {
            MessageBox.Show(Loc.T("reg.cancel_not_allowed", row.Lifecycle),
                Loc.T("reg.cancel_not_allowed_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Yêu cầu xác nhận đăng nhập + lý do (Req 5.2)
        var confirmUserId = ShowInputDialog(
            Loc.T("reg.cancel_confirm_title"),
            Loc.T("reg.cancel_confirm_prompt", row.TripNo, row.Plate),
            _presenter.OperatorName);

        if (string.IsNullOrWhiteSpace(confirmUserId))
        {
            AppendNotification(Loc.T("reg.cancel_no_identity"));
            return;
        }

        var reason = ShowInputDialog(
            Loc.T("reg.cancel_reason_title"),
            Loc.T("reg.cancel_reason_prompt"),
            string.Empty);

        if (string.IsNullOrWhiteSpace(reason))
        {
            AppendNotification(Loc.T("reg.cancel_no_reason"));
            return;
        }

        SetUiBusy(true);
        try
        {
            AppendNotification(Loc.T("reg.cancelling", row.TripNo));

            var cmd = new CancelTripCommand(
                TripId: row.TripId,
                ExpectedVersion: row.Version,
                Reason: reason,
                ConfirmUserId: confirmUserId,
                CorrelationId: Guid.NewGuid().ToString("N"));

            var result = await _presenter.CancelAsync(cmd, FormToken);

            if (!result.IsSuccess)
            {
                AppendNotification(Loc.T("reg.cancel_failed", result.FailureReason));
                MessageBox.Show(Loc.T("reg.cancel_cannot", result.FailureReason),
                    Loc.T("reg.cancel_failed_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var frmStatus = result.FrmNotifyStatus.HasValue
                ? $" (FRM: {result.FrmNotifyStatus})"
                : string.Empty;

            AppendNotification(Loc.T("reg.cancel_ok", row.TripNo) + frmStatus);

            // Refresh lưới
            await LoadGridAsync();

            MessageBox.Show(Loc.T("reg.cancel_ok", row.TripNo) + ".",
                Loc.T("reg.cancel_ok_title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            // Form đang đóng.
        }
        catch (Exception ex)
        {
            AppendNotification(Loc.T("reg.cancel_error", ex.Message));
            MessageBox.Show(Loc.T("reg.cancel_error_dialog", ex.Message),
                Loc.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetUiBusy(false);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  UI busy state (task 6.5)
    // ═══════════════════════════════════════════════════════════

    /// <summary>Bật/tắt trạng thái bận — vô hiệu hóa nút khi đang xử lý async.</summary>
    private void SetUiBusy(bool busy)
    {
        _isBusy = busy;
        _btnSave.Enabled = !busy;
        _btnPrint.Enabled = !busy;
        _txtPlate.Enabled = !busy;
        _btnReset.Enabled = !busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    // ═══════════════════════════════════════════════════════════
    //  Command builders (task 6.5)
    // ═══════════════════════════════════════════════════════════

    /// <summary>Tạo RegisterTripCommand từ các trường form hiện tại.</summary>
    private RegisterTripCommand BuildRegisterCommand(bool isTemp)
    {
        var profile = GetSelectedProfile();

        if (profile is null && _availableProfiles is { Count: > 0 })
            profile = _availableProfiles[0];

        var isSugarcane = _cboProfileType.SelectedIndex == 0;

        // Lấy dữ liệu từ LenhDon nếu form fields rỗng (mía)
        var ld = isSugarcane ? _presenter.FindLenhDonByPlate(PlateText) : null;

        // Vật tư: lấy từ panel vật tư, Mía: lấy từ panel mía + LenhDon
        var ownerOrCustomer = isSugarcane
            ? _txtCustomerOrSupplier.Text.Trim().NullIfEmpty() ?? ld?.KHName ?? _selectedPlan?.OwnerOrCustomer
            : _txtMaterialCustomer.Text.Trim().NullIfEmpty();

        var commodityType = isSugarcane
            ? _selectedPlan?.CommodityType ?? ld?.LoaiMiaName
            : _txtCommodity.Text.Trim().NullIfEmpty();

        var driverRef = isSugarcane
            ? null
            : _txtDriver.Text.Trim().NullIfEmpty();

        var referenceTripNo = isSugarcane
            ? _txtShipment.Text.Trim().NullIfEmpty() ?? ld?.MaLD ?? _selectedPlan?.ReferenceNo
            : _txtShipment.Text.Trim().NullIfEmpty() ?? _txtTripNumber.Text.Trim().NullIfEmpty();

        var planSource = isSugarcane ? (_selectedPlan?.Source ?? "FRM") : "ERP";

        return new RegisterTripCommand(
            SiteId: _presenter.SiteId,
            GateId: Guid.Empty,
            WorkflowProfileId: profile?.Id ?? Guid.Empty,
            WorkflowProfileVersion: profile?.ProfileVersion ?? 0,
            ReferenceTripNo: referenceTripNo,
            PlanSource: planSource,
            CommodityType: commodityType,
            OwnerOrCustomer: ownerOrCustomer,
            WarehouseCode: isSugarcane ? _selectedPlan?.WarehouseCode : _txtWarehouse.Text.Trim().NullIfEmpty(),
            TractorPlate: PlateText,
            TrailerPlate: isSugarcane ? null : _txtTrailer.Text.Trim().NullIfEmpty(),
            DriverRef: driverRef,
            VehicleType: GetSelectedVehicleType(),
            SugarcaneType: isSugarcane ? (_txtSugarcaneType.Text.Trim().NullIfEmpty() ?? ld?.LoaiMiaName) : null,
            SugarcaneVariety: isSugarcane ? (_txtSugarcaneVariety.Text.Trim().NullIfEmpty() ?? ld?.GiongMiaName) : null,
            SugarcaneOwner: isSugarcane ? (_txtSugarcaneOwner.Text.Trim().NullIfEmpty() ?? ld?.KHName) : ownerOrCustomer,
            HarvestMethod: isSugarcane ? (_txtHarvestMethod.Text.Trim().NullIfEmpty() ?? (ld?.DoiTacThuHoachName is not null ? "Thu hoạch" : null)) : null,
            Station: isSugarcane ? (_txtStation.Text.Trim().NullIfEmpty() ?? ld?.TramName) : null,
            CropSeason: isSugarcane ? (_txtCropSeason.Text.Trim().NullIfEmpty() ?? ld?.VudautuName) : null,
            FrmLenhDonId: ld?.SourceKey,
            FrmMaLenhDon: ld?.MaLD,
            FrmLenhDonBu: ld?.BusinessUnit,
            FrmLoaiXeCode: ld?.LoaixeCode,
            CorrelationId: Guid.NewGuid().ToString("N"),
            IssueTemporaryQrOnly: isTemp,
            OrganizationUnitCode: WinForms.Program.ActiveOrganizationUnitCode);
    }

    // ═══════════════════════════════════════════════════════════
    //  Print helper (Req 8.3, 8.5)
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Hiển thị popup xem trước phiếu đăng tài (THẺ ĐĂNG TÀI) rồi cho phép in.
    /// Giao diện preview giống mẫu in: logo TTC, tiêu đề, thông tin chuyến, QR, toolbar chọn máy in.
    /// Lỗi máy in không ảnh hưởng đăng tài đã lưu (Req 8.5).
    /// In lại gắn nhãn "BẢN IN LẠI" (Req 8.3).
    /// </summary>
    private void ShowTripReceiptPreview(Guid tripId, bool isReprint)
    {
        try
        {
            var data = BuildReceiptData(tripId);

            using var preview = new TripReceiptPreviewForm(
                data,
                _presenter.PrintService,
                isReprint);

            preview.ShowDialog(this);

            if (preview.PrintResult is { IsSuccess: true })
                AppendNotification(Loc.T("reg.print_ok", preview.PrintResult.TicketNumber));
        }
        catch (Exception ex)
        {
            // Đăng tài đã lưu DB thành công → không rollback vì lỗi in (Req 8.5)
            AppendNotification(Loc.T("reg.print_error", ex.Message));
        }
    }

    /// <summary>Đẩy lại phiếu đăng tài lên DigiScale — cho cả mía và vật tư.</summary>
    private async Task RetryPushDangTaiDigiAsync()
    {
        if (_lastRegisteredTripId is null)
        {
            MessageBox.Show("Chọn chuyến trước khi đẩy lại.", "Thiếu thông tin",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var plate = PlateText;
        var tripNo = _txtTripNo.Text.Trim();
        var isSugarcane = _cboProfileType.SelectedIndex == 0;

        if (!isSugarcane)
        {
            // Vật tư — chưa có API đăng tài riêng trên DigiScale
            AppendNotification("ℹ Vật tư: Đăng tài chỉ lưu local. Đẩy DigiScale khi cân (oracle-tickets).");
            MessageBox.Show("Vật tư không đẩy phiếu đăng tài lên DigiScale.\nPhiếu cân vật tư sẽ đẩy khi cân xong.",
                "Thông tin", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Mía — push phiếu đăng tài FRM
        var ld = _presenter.FindLenhDonByPlate(plate);
        var lenhDonId = ld?.SourceKey ?? _txtShipment.Text.Trim();
        var maLenhDon = ld?.MaLD ?? _txtShipment.Text.Trim();
        var loaiXe = ld?.LoaixeCode ?? "";
        var lenhDonBu = ld?.BusinessUnit;
        var uuTien = ld?.UuTien;

        AppendNotification($"⏳ Đang đẩy lại đăng tài lên DigiScale: {tripNo}...");

        try
        {
            var pushResult = await _presenter.PushDangTaiAsync(
                lenhDonId, maLenhDon, DateTime.Now, loaiXe, tripNo,
                uuTien, lenhDonBu, FormToken);

            if (pushResult.IsSuccess)
            {
                AppendNotification($"✅ Đẩy lại DigiScale thành công: {tripNo}");
                Helpers.DigiScalePopup.Show(this,
                    "ĐẨY LẠI ĐĂNG TÀI THÀNH CÔNG",
                    $"Số đăng tài: {tripNo}\nBiển số: {plate}",
                    true, "OK");
            }
            else
            {
                AppendNotification($"❌ Đẩy lại thất bại: {pushResult.ErrorMessage}");
                Helpers.DigiScalePopup.Show(this,
                    "ĐẨY LẠI ĐĂNG TÀI THẤT BẠI",
                    $"Số đăng tài: {tripNo}\nBiển số: {plate}",
                    false, pushResult.ErrorMessage ?? "Lỗi");
            }
        }
        catch (Exception ex)
        {
            AppendNotification($"❌ Lỗi: {ex.Message}");
            MessageBox.Show($"Lỗi đẩy lại DigiScale:\n{ex.Message}", "Lỗi",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Tổng hợp dữ liệu từ form fields + result để hiển thị trên phiếu đăng tài.
    /// </summary>
    private TripReceiptData BuildReceiptData(Guid tripId)
    {
        // Lấy dữ liệu từ LenhDon khi form fields rỗng (do ẩn)
        var ld = _presenter.FindLenhDonByPlate(PlateText);

        // Transporter: ưu tiên DoiTacVanChuyenName từ thẻ xe (FrmVehicleInfo),
        // fallback về DoiTacThuHoachName từ lệnh đốn (FrmLenhDonInfo).
        var vehicles = _presenter.FindVehicleByPlate(PlateText);
        var transporter = vehicles.FirstOrDefault(v => v.DoiTacVanChuyenName is not null)?.DoiTacVanChuyenName
                          ?? ld?.DoiTacThuHoachName;

        return new TripReceiptData(
            TripId: tripId,
            TripNo: _txtTripNo.Text.Trim().NullIfEmpty(),
            DateTime: _txtDateTime.Text.Trim().NullIfEmpty(),
            Gate: null,
            Plate: PlateText.NullIfEmpty(),
            TrailerPlate: null,
            Transporter: transporter,
            DriverName: null,
            DriverIdCard: null,
            ShipmentNo: _txtShipment.Text.Trim().NullIfEmpty() ?? ld?.MaLD,
            Warehouse: _txtWarehouse.Text.Trim().NullIfEmpty(),
            Commodity: _txtCommodity.Text.Trim().NullIfEmpty() ?? ld?.LoaiMiaName,
            CustomerOrSupplier: _txtCustomerOrSupplier.Text.Trim().NullIfEmpty() ?? ld?.KHName,
            SugarcaneType: _txtSugarcaneType.Text.Trim().NullIfEmpty() ?? ld?.LoaiMiaName,
            SugarcaneOwner: _txtSugarcaneOwner.Text.Trim().NullIfEmpty() ?? ld?.KHName,
            SugarcaneVariety: _txtSugarcaneVariety.Text.Trim().NullIfEmpty() ?? ld?.GiongMiaName,
            HarvestMethod: _txtHarvestMethod.Text.Trim().NullIfEmpty(),
            Station: _txtStation.Text.Trim().NullIfEmpty() ?? ld?.TramName,
            CropSeason: _txtCropSeason.Text.Trim().NullIfEmpty() ?? ld?.VudautuName,
            QrToken: _lastQrToken,
            QueueSequenceNumber: _lastQueueSequenceNumber,
            Factory: _presenter.SiteName,
            FactoryAddress: _presenter.SiteAddress,
            VehicleType: _txtVehicleType.Text.Trim().NullIfEmpty() ?? ld?.LoaixeName,
            IsMaterial: _cboProfileType.SelectedIndex != 0);
    }

    // ═══════════════════════════════════════════════════════════
    //  Input dialog helper
    // ═══════════════════════════════════════════════════════════

    /// <summary>Popup thông báo đăng tài thành công — to, nổi bật, nền xanh.</summary>
    private void ShowSuccessPopup(string tripNo, string plate, string lenhDon, string qrType, bool digiOk, string digiStatus)
    {
        using var popup = new Form
        {
            Text = digiOk ? "✅ ĐĂNG TÀI THÀNH CÔNG" : "⚠ ĐĂNG TÀI — CHƯA ĐẨY DIGI",
            Size = new Size(520, 400),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = digiOk ? Color.FromArgb(232, 255, 232) : Color.FromArgb(255, 255, 220)
        };

        var lblIcon = new Label
        {
            Text = digiOk ? "✅" : "⚠",
            Font = new Font(AgrisTheme.FontFamily, 48F),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 70,
            ForeColor = digiOk ? AgrisTheme.Success : AgrisTheme.Warning
        };

        var lblTitle = new Label
        {
            Text = digiOk ? "ĐĂNG TÀI THÀNH CÔNG" : "ĐĂNG TÀI — CHƯA ĐẨY DIGI",
            Font = new Font(AgrisTheme.FontFamily, 18F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 40,
            ForeColor = digiOk ? AgrisTheme.BrandGreen : AgrisTheme.Warning
        };

        var lblDetails = new Label
        {
            Text = $"Số đăng tài:  {tripNo}\n" +
                   $"Biển số:       {plate}\n" +
                   $"Lệnh đốn:    {lenhDon}\n" +
                   $"Loại QR:       {qrType}\n\n" +
                   $"DigiScale:     {digiStatus}",
            Font = new Font(AgrisTheme.FontFamily, 13F),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill,
            ForeColor = AgrisTheme.TextPrimary,
            Padding = new Padding(20)
        };

        var btnOk = new Button
        {
            Text = "OK",
            Size = new Size(160, 44),
            Font = new Font(AgrisTheme.FontFamily, 12F, FontStyle.Bold),
            BackColor = AgrisTheme.BrandGreen,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.OK,
            Anchor = AnchorStyles.Bottom
        };
        btnOk.FlatAppearance.BorderSize = 0;

        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 60,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(20, 8, 20, 8)
        };
        btnPanel.Controls.Add(btnOk);

        popup.AcceptButton = btnOk;
        popup.Controls.Add(lblDetails);
        popup.Controls.Add(lblTitle);
        popup.Controls.Add(lblIcon);
        popup.Controls.Add(btnPanel);

        popup.ShowDialog(this);
    }

    /// <summary>Hiển thị dialog nhập liệu đơn giản. Trả null nếu user bấm Cancel.</summary>
    private static string? ShowInputDialog(string title, string prompt, string defaultValue)
    {
        using var form = new Form
        {
            Text = title,
            Width = 420,
            Height = 200,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = AgrisTheme.Canvas
        };

        var lblPrompt = new Label
        {
            Text = prompt,
            Left = 12,
            Top = 12,
            Width = 380,
            Height = 80,
            Font = AgrisTheme.Body(9.5F)
        };

        var txtInput = new TextBox
        {
            Left = 12,
            Top = 96,
            Width = 380,
            Text = defaultValue ?? string.Empty,
            Font = AgrisTheme.Body(10F)
        };

        var btnOk = new Button
        {
            Text = Loc.T("common.confirm"),
            Left = 212,
            Top = 130,
            Width = 90,
            Height = 30,
            DialogResult = DialogResult.OK,
            BackColor = AgrisTheme.BrandGreen,
            ForeColor = AgrisTheme.TextOnBrand,
            FlatStyle = FlatStyle.Flat
        };

        var btnCancel = new Button
        {
            Text = Loc.T("common.cancel"),
            Left = 308,
            Top = 130,
            Width = 84,
            Height = 30,
            DialogResult = DialogResult.Cancel,
            FlatStyle = FlatStyle.Flat
        };

        form.AcceptButton = btnOk;
        form.CancelButton = btnCancel;
        form.Controls.AddRange([lblPrompt, txtInput, btnOk, btnCancel]);

        return form.ShowDialog() == DialogResult.OK ? txtInput.Text.Trim() : null;
    }

    /// <summary>Chuyển tab lọc, cập nhật giao diện tab và tải lại dữ liệu lưới.</summary>
    private async Task SetActiveTabAsync(TripGridTab tab)
    {
        _activeTab = tab;
        ApplyTabStyles();
        await LoadGridAsync();
    }

    /// <summary>Tô màu tab đang chọn theo trạng thái active/inactive.</summary>
    private void ApplyTabStyles()
    {
        ApplyTabButtonStyle(_btnTabInFactory, _activeTab == TripGridTab.InFactory);
        ApplyTabButtonStyle(_btnTabExited, _activeTab == TripGridTab.Exited);
        ApplyTabButtonStyle(_btnTabCancelled, _activeTab == TripGridTab.Cancelled);
    }

    private static void ApplyTabButtonStyle(Button btn, bool isActive)
    {
        if (isActive)
        {
            btn.BackColor = AgrisTheme.BrandGreen;
            btn.ForeColor = AgrisTheme.TextOnBrand;
            btn.Font = AgrisTheme.Body(9.5F, FontStyle.Bold);
        }
        else
        {
            btn.BackColor = AgrisTheme.Surface;
            btn.ForeColor = AgrisTheme.TextPrimary;
            btn.Font = AgrisTheme.Body(9.5F);
        }
    }

    /// <summary>
    /// Tải danh sách chuyến từ Presenter theo bộ lọc hiện tại (Req 7.1, 7.2, 7.3).
    /// Gọi bởi: tab change, date change, timer tick, và sau khi đăng tài/hủy.
    /// </summary>
    internal async Task LoadGridAsync()
    {
        if (_gridRefreshInFlight) return;

        _gridRefreshInFlight = true;
        await _dbLock.WaitAsync(_cts.Token);
        try
        {
            var filter = new TripRegistrationFilter
            {
                Tab = _activeTab,
                FromUtc = ToUtcStartOfDay(_dtpFrom.Value),
                ToUtc = ToUtcStartOfDay(_dtpTo.Value)
            };

            var rows = await _presenter.LoadGridAsync(filter, _cts.Token);

            GridBuilder.BindAndColorize(_dgvTrips, rows, r => r.Lifecycle.ToString());
        }
        catch (OperationCanceledException)
        {
            // Form đang đóng — bỏ qua.
        }
        catch (Exception ex)
        {
            AppendNotification(Loc.T("reg.grid_error", ex.Message));
        }
        finally
        {
            _gridRefreshInFlight = false;
            _dbLock.Release();
        }
    }

    /// <summary>
    /// Chọn dòng trong lưới → điền thông tin chuyến lên form (Req 7.4).
    /// </summary>
    /// <summary>
    /// CellClick — toggle: click dòng đang chọn → bỏ chọn + clear form.
    /// Click dòng khác → populate.
    /// </summary>
    private void OnGridCellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        if (_dgvTrips.Rows[e.RowIndex].DataBoundItem is not TripRegistrationRow row) return;

        // Toggle: click lại dòng đã chọn → bỏ chọn + clear form
        if (_lastRegisteredTripId == row.TripId)
        {
            _dgvTrips.ClearSelection();
            _lastRegisteredTripId = null;
            _lastQrToken = null;
            _txtTripNo.Clear();
            _txtShipment.Clear();
            _txtPlate.Clear();
            _txtDateTime.Clear();
            AppendNotification(Loc.T("common.no_trip_selected"));
            return;
        }

        PopulateFromGridRow(row);
    }

    /// <summary>Điền thông tin từ dòng lưới lên form để xem chi tiết hoặc thao tác tiếp.</summary>
    private async void PopulateFromGridRow(TripRegistrationRow row)
    {
        _txtTripNo.Text = row.TripNo;
        _txtShipment.Text = row.ShipmentNo ?? string.Empty;
        _txtPlate.Text = row.Plate ?? string.Empty;
        _txtDateTime.Text = row.EntryTime?.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") ?? string.Empty;
        _lastRegisteredTripId = row.TripId;
        _txtVehicleType.Text = row.VehicleType ?? string.Empty;

        // Lookup FRM cache theo biển số → điền lại thông tin đầy đủ
        if (!string.IsNullOrWhiteSpace(row.Plate))
        {
            _lastLookedUpPlate = null; // Reset để cho phép lookup lại
            await LookupAndPopulateAsync(row.Plate);
        }

        AppendNotification(Loc.T("reg.grid_selected", row.TripNo, row.Plate));
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

    // ═══════════════════════════════════════════════════════════
    //  Public methods for auto-fill from plan (Req 6.2)
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Tự động điền các trường từ kế hoạch FRM/ERP khi Nhân_viên_cổng chọn kế hoạch.
    /// Gọi bởi event handler khi chọn kế hoạch trong danh sách (task 6.5).
    /// </summary>
    public void PopulateFromPlan(PlanEntry plan)
    {
        _selectedPlan = plan;

        // Tất cả trường read-only — điền từ FRM API
        _txtShipment.Text = plan.CardCode ?? plan.ReferenceNo ?? string.Empty;
        _txtVehicleType.Text = plan.VehicleTypeName ?? string.Empty;
        _txtCustomerOrSupplier.Text = plan.OwnerOrCustomer ?? string.Empty;
        _txtMaHopDong.Text = plan.ContractCode ?? string.Empty;
        _txtStation.Text = plan.Station ?? string.Empty;
        _txtCropSeason.Text = plan.CropSeason ?? string.Empty;
        _txtHarvestMethod.Text = plan.HarvestMethod ?? string.Empty;
        _txtMaThe.Text = plan.CardCode ?? string.Empty;
        _txtTaiTrong.Text = plan.VehiclePayload.HasValue ? $"{plan.VehiclePayload:N1}" : string.Empty;
        _txtSugarcaneOwner.Text = plan.OwnerOrCustomer ?? string.Empty;
        _txtSugarcaneType.Text = plan.SugarcaneType ?? string.Empty;
        _txtSugarcaneVariety.Text = plan.SugarcaneVariety ?? string.Empty;

        AppendNotification($"✅ [{plan.Source}] {plan.VehiclePlate}" +
            $" | Thẻ: {plan.CardCode ?? "(chưa có thẻ)"}" +
            $" | Trạm: {plan.Station ?? "(N/A)"}" +
            $" | ĐTVC: {plan.OwnerOrCustomer ?? "(N/A)"}" +
            $" | Vụ: {plan.CropSeason ?? "(N/A)"}" +
            $" | HĐVC: {plan.ContractCode ?? "(N/A)"}" +
            $" | Xe: {plan.VehicleTypeName ?? "(N/A)"} {(plan.VehiclePayload.HasValue ? $"{plan.VehiclePayload:N1}T" : "")}");

        if (plan.CardCode is null)
            AppendNotification("⚠ Xe chưa có thẻ xe trong FRM — chỉ có thông tin từ danh mục xe vận chuyển. Các trường Trạm, ĐTVC, Vụ ĐT trống.");

        // Kiểm tra thời hạn hiệu lực thẻ xe
        var now = DateTimeOffset.Now;
        if (plan.ValidFrom.HasValue && now < plan.ValidFrom.Value)
        {
            var msg = $"⛔ Xe {plan.VehiclePlate} chưa đến thời hạn hiệu lực thẻ ({plan.ValidFrom.Value.ToLocalTime():dd/MM/yyyy HH:mm}). Không thể đăng tài.";
            AppendNotification(msg);
            MessageBox.Show(msg, "Cảnh báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _selectedPlan = null;
        }
        else if (plan.ValidTo.HasValue && now > plan.ValidTo.Value)
        {
            AppendNotification($"⚠ Thẻ xe {plan.CardCode} đã hết hạn ({plan.ValidTo.Value.ToLocalTime():dd/MM/yyyy HH:mm}).");
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Form state
    // ═══════════════════════════════════════════════════════════

    /// <summary>Reset form về trạng thái trống, chuẩn bị đăng tài mới.</summary>
    public void ResetForm()
    {
        _txtPlate.Clear();
        _txtTripNo.Clear();
        _txtShipment.Clear();
        _txtDateTime.Clear();
        _txtVehicleType.Clear();
        _txtCustomerOrSupplier.Clear();
        _txtMaHopDong.Clear();
        _txtStation.Clear();
        _txtCropSeason.Clear();
        _txtHarvestMethod.Clear();
        _txtMaThe.Clear();
        _txtTaiTrong.Clear();
        _txtSugarcaneOwner.Clear();
        _txtSugarcaneType.Clear();
        _txtSugarcaneVariety.Clear();
        _txtCommodity.Clear();
        _txtWarehouse.Clear();
        _txtNotes.Clear();
        _cboProfileType.SelectedIndex = 0;
        _selectedPlan = null;
        _lastErpOrderLine = null;
        _tempQrChosen = false;
        _lastRegisteredTripId = null;
        _lastQrToken = null;
        _lastQueueSequenceNumber = null;
        _lastLookedUpPlate = null;

        _activeTab = TripGridTab.InFactory;
        ApplyTabStyles();

        _txtPlate.Focus();
    }

    /// <summary>
    /// Lấy VehicleType từ ComboBox selection. Trả null khi chưa chọn.
    /// </summary>
    public VehicleType? GetSelectedVehicleType()
    {
        var text = _txtVehicleType.Text.ToUpperInvariant();

        // Nếu ô rỗng, thử lấy từ LenhDon đã chọn
        if (string.IsNullOrWhiteSpace(text))
        {
            var plate = PlateText;
            if (!string.IsNullOrWhiteSpace(plate))
            {
                var ld = _presenter.FindLenhDonByPlate(plate);
                if (ld?.LoaixeName is not null)
                    text = ld.LoaixeName.ToUpperInvariant();
            }
        }

        if (string.IsNullOrWhiteSpace(text)) return null; // Không có → báo lỗi bắt buộc

        if (text.Contains("CONTAINER") || text.Contains("CONT")) return VehicleType.Container;
        if (text.Contains("KÉO") || text.Contains("MÁY KÉO") || text.Contains("TRAILER")) return VehicleType.Trailer;
        return VehicleType.Truck;
    }

    // ═══════════════════════════════════════════════════════════
    //  Notification panel helpers
    // ═══════════════════════════════════════════════════════════

    /// <summary>Ghi nhận sự kiện vào panel thông báo. Thread-safe.</summary>
    public void AppendNotification(string message)
    {
        if (_rtbNotifications.InvokeRequired)
        {
            _rtbNotifications.Invoke(() => AppendNotification(message));
            return;
        }

        _rtbNotifications.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
        _rtbNotifications.ScrollToCaret();
    }

    // ═══════════════════════════════════════════════════════════
    //  Status bar helpers
    // ═══════════════════════════════════════════════════════════

    /// <summary>Cập nhật trạng thái kết nối trên thanh trạng thái. Thread-safe.</summary>
    public void UpdateConnectionStatus(string device, bool isOnline)
    {
        var action = () =>
        {
            var label = device switch
            {
                "Server" => _lblServerStatus,
                "Barcode" => _lblBarcodeStatus,
                "LPR" => _lblLprStatus,
                "Camera" => _lblCameraStatus,
                _ => null
            };

            if (label is null) return;

            var dot = isOnline ? "🟢" : "🔴";
            var color = isOnline ? AgrisTheme.Success : AgrisTheme.Danger;
            label.Text = $"{device}: {dot}";
            label.ForeColor = color;
        };

        if (_statusBar.InvokeRequired)
            _statusBar.Invoke(action);
        else
            action();
    }

    // ═══════════════════════════════════════════════════════════
    //  Accessors for event binding (task 6.5)
    // ═══════════════════════════════════════════════════════════

    /// <summary>TextBox biển số xe — gõ là gợi ý ngay.</summary>
    internal TextBox PlateTextBox => _txtPlate;

    /// <summary>Nút Lưu phiếu (F4) — event handler gắn ở task 6.5.</summary>
    internal Button SaveButton => _btnSave;

    /// <summary>Nút In phiếu (F5) — event handler gắn ở task 6.5.</summary>
    internal Button PrintButton => _btnPrint;

    /// <summary>Nút Làm mới (Esc).</summary>
    internal Button ResetButton => _btnReset;

    /// <summary>Camera Vào control — dùng để polling status.</summary>
    internal CameraViewControl CameraInView => _cameraIn;

    /// <summary>Camera Ra control — dùng để polling status.</summary>
    internal CameraViewControl CameraOutView => _cameraOut;

    /// <summary>DataGridView lưới chuyến — dùng cho event handlers.</summary>
    internal DataGridView TripGrid => _dgvTrips;

    /// <summary>Tab đang chọn trên lưới.</summary>
    internal TripGridTab ActiveGridTab => _activeTab;

    /// <summary>Biển số đã nhập/chọn — mía lấy từ _txtPlate, vật tư lấy từ _txtMaterialPlate.</summary>
    internal string PlateText => _cboProfileType.SelectedIndex == 0
        ? _txtPlate.Text.Trim()
        : (_txtMaterialPlate.Text.Trim().Length > 0 ? _txtMaterialPlate.Text.Trim() : _txtPlate.Text.Trim());

    /// <summary>Ghi chú đã nhập.</summary>
    internal string NotesText => _txtNotes.Text.Trim();

    /// <summary>Cổng đã nhập.</summary>
    internal string GateText => string.Empty;

    /// <summary>Lệnh đốn / Shipment đã nhập.</summary>
    internal string ShipmentText => _txtShipment.Text.Trim();

    /// <summary>Kế hoạch đã chọn (null nếu chưa chọn).</summary>
    internal PlanEntry? SelectedPlan => _selectedPlan;

    /// <summary>Presenter instance for event handlers.</summary>
    internal TripRegistrationPresenter Presenter => _presenter;

    /// <summary>CancellationToken cho async operations.</summary>
    internal CancellationToken FormToken => _cts.Token;

    /// <summary>Gán số đăng tài sau khi RegisterAsync thành công.</summary>
    internal void SetTripNo(string tripNo)
    {
        _txtTripNo.Text = tripNo;
    }

    /// <summary>Cập nhật ngày giờ hiện tại.</summary>
    internal void RefreshDateTime()
    {
        _txtDateTime.Text = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
    }

    // ═══════════════════════════════════════════════════════════
    //  Private helpers
    // ═══════════════════════════════════════════════════════════

    private static void ConfigureStatusLabel(ToolStripStatusLabel label, string device, Color color)
    {
        var dot = color == AgrisTheme.Success ? "🟢"
            : color == AgrisTheme.Warning ? "🟡"
            : "🔴";

        label.Text = $"{device}: {dot}";
        label.ForeColor = color;
        label.Margin = new Padding(4, 0, 4, 0);
        label.Font = new Font(AgrisTheme.FontFamily, 8.5F);
    }

    // ═══════════════════════════════════════════════════════════
    //  Cleanup
    // ═══════════════════════════════════════════════════════════

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _eventBus.TripChanged -= OnTripChangedFromOtherForm;
        _gridRefreshTimer.Stop();
        _cts.Cancel();
        base.OnFormClosing(e);
    }

    /// <summary>Refresh lưới khi form khác thay đổi chuyến (cân xong, cổng xong…).</summary>
    private async void OnTripChangedFromOtherForm(object? sender, TripChangedEventArgs e)
    {
        if (InvokeRequired)
        {
            Invoke(() => OnTripChangedFromOtherForm(sender, e));
            return;
        }

        // Không refresh nếu chính form này gây ra sự kiện
        if (e.SourceForm == nameof(TripRegistrationForm)) return;

        await LoadGridAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _gridRefreshTimer.Dispose();
            _cts.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>Extension nhỏ cho string — dùng nội bộ trong file form.</summary>
internal static class TripRegistrationFormStringExtensions
{
    /// <summary>Trả null nếu chuỗi rỗng hoặc chỉ có khoảng trắng. Dùng cho nullable command fields.</summary>
    public static string? NullIfEmpty(this string value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
