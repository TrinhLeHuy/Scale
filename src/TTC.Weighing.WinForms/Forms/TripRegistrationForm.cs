using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.Queries;
using TTC.Weighing.Application.UseCases.Trips;
using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Infrastructure.Adapters;
using TTC.Weighing.WinForms.Controls;
using TTC.Weighing.WinForms.Helpers;
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
    private readonly Button _btnPlateDropdown = new() { Text = "▼", AccessibleName = Loc.T("reg.open_plate_list") };
    private readonly ListBox _lstPlatePopup = new()
    {
        Visible = false,
        BorderStyle = BorderStyle.FixedSingle,
        IntegralHeight = false
    };
    private readonly DateTimePicker _dtpFrmFrom = new() { AccessibleName = Loc.T("reg.frm_date_from") };
    private readonly DateTimePicker _dtpFrmTo = new() { AccessibleName = Loc.T("reg.frm_date_to") };

    // ── Khung ngày đơn hàng ERP (vật tư) — tương tự khung ngày lệnh đốn FRM ──
    private readonly DateTimePicker _dtpErpFrom = new()
    {
        Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 130,
        AccessibleName = Loc.T("reg.erp_date_from")
    };
    private readonly DateTimePicker _dtpErpTo = new()
    {
        Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 130,
        AccessibleName = Loc.T("reg.erp_date_to")
    };
    private readonly Button _btnErpTripDropdown = new()
    {
        Text = "▼", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, TabStop = false,
        AccessibleName = Loc.T("reg.erp_trip_list")
    };
    private string[] _filteredPlates = [];

    /// <summary>
    /// Lệnh đốn đăng tài được trong khung ngày — nguồn DUY NHẤT cho cả danh sách biển số và danh sách lệnh đốn.
    /// </summary>
    private RegistrableLenhDonList _registrable = RegistrableLenhDonList.Empty;
    private int _registrableRequestId;

    private readonly TextBox _txtShipment = new() { AccessibleName = Loc.T("reg.field_shipment") };

    // ── Tìm theo lệnh đốn (giống ô biển số): gõ/chọn mã lệnh đốn → tự điền biển số + thông tin ──
    private readonly Button _btnLenhDonDropdown = new() { Text = "▼", AccessibleName = Loc.T("reg.open_lenhdon_list") };
    private readonly ListBox _lstLenhDonPopup = new()
    {
        Visible = false,
        BorderStyle = BorderStyle.FixedSingle,
        IntegralHeight = false
    };
    private string[] _filteredLenhDonCodes = [];

    /// <summary>
    /// Lệnh đốn đang chọn (theo biển số hoặc theo mã). Một biển số có thể có nhiều lệnh đốn —
    /// đăng tài dùng ĐÚNG lệnh đốn này, không tra lại theo biển số.
    /// </summary>
    private FrmLenhDonInfo? _selectedLenhDon;
    private string? _lastLookedUpLenhDon;
    private readonly TextBox _txtTripNo = new() { ReadOnly = true, AccessibleName = Loc.T("field.trip_no") };
    private readonly TextBox _txtDateTime = new() { ReadOnly = true, AccessibleName = Loc.T("field.date_time") };

    // ── Trường FRM (cho phép nhập tay khi offline — tự điền từ API khi có kết nối) ──
    private readonly TextBox _txtCustomerOrSupplier = new() { AccessibleName = Loc.T("reg.field_transport_partner") };
    private readonly TextBox _txtStation = new() { AccessibleName = Loc.T("field.station") };
    private readonly TextBox _txtCropSeason = new() { AccessibleName = Loc.T("reg.field_crop_investment") };
    private readonly TextBox _txtHarvestMethod = new() { AccessibleName = Loc.T("field.harvest_method") };
    private readonly TextBox _txtSugarcaneOwner = new() { AccessibleName = Loc.T("field.sugarcane_owner") };
    private readonly TextBox _txtSugarcaneType = new() { AccessibleName = Loc.T("field.sugarcane_type") };
    private readonly TextBox _txtSugarcaneVariety = new() { AccessibleName = Loc.T("field.sugarcane_variety") };
    private readonly TextBox _txtVehicleType = new() { AccessibleName = Loc.T("field.vehicle_type") };
    private readonly TextBox _txtMaThe = new() { ReadOnly = true, AccessibleName = Loc.T("reg.field_card_code") };
    private readonly TextBox _txtMaHopDong = new() { ReadOnly = true, AccessibleName = Loc.T("reg.field_contract_code") };
    private readonly TextBox _txtTaiTrong = new() { ReadOnly = true, AccessibleName = Loc.T("reg.field_capacity") };
    private readonly TextBox _txtNotes = new() { AccessibleName = Loc.T("field.notes") };

    // ── Chọn loại nghiệp vụ (Mía nhập / Vật tư) ──
    private readonly ComboBox _cboProfileType = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        AccessibleName = Loc.T("field.profile_type")
    };

    // ── Các trường dành riêng cho vật tư (ẩn khi chọn mía) ──
    // Dữ liệu vật tư lấy nguyên từ ERP — chỉ đọc, không cho sửa trên app cân.
    private readonly TextBox _txtCommodity = new() { AccessibleName = Loc.T("field.commodity"), ReadOnly = true };
    private readonly TextBox _txtWarehouse = new() { AccessibleName = Loc.T("field.warehouse"), ReadOnly = true };
    private readonly RadioButton _rbMaterialIn = new() { Text = Loc.T("reg.direction_in"), AutoSize = true, Checked = true };
    private readonly RadioButton _rbMaterialOut = new() { Text = Loc.T("reg.direction_out"), AutoSize = true };
    private readonly TextBox _txtTripNumber = new() { AccessibleName = Loc.T("reg.field_trip_number"), PlaceholderText = Loc.T("reg.field_trip_number_placeholder") };
    private readonly TextBox _txtMaterialPlate = new() { AccessibleName = Loc.T("col.plate"), PlaceholderText = Loc.T("field.plate") };
    private readonly Button _btnCheckErp = GridBuilder.PrimaryButton(Loc.T("reg.btn_check"), new Size(100, 30));
    private readonly TextBox _txtMaterialShipment = new() { AccessibleName = Loc.T("field.shipment"), PlaceholderText = Loc.T("field.shipment") };
    private readonly TextBox _txtMaterialPlateDetail = new() { AccessibleName = Loc.T("field.plate"), PlaceholderText = Loc.T("field.plate") };
    private readonly TextBox _txtTransporter = new() { AccessibleName = Loc.T("field.transporter"), PlaceholderText = Loc.T("field.transporter"), ReadOnly = true };
    private readonly TextBox _txtDriver = new() { AccessibleName = Loc.T("field.driver"), ReadOnly = true };
    private readonly TextBox _txtMaterialCustomer = new() { AccessibleName = Loc.T("field.customer"), PlaceholderText = Loc.T("field.customer"), ReadOnly = true };
    private readonly TextBox _txtReceiveDate = new() { AccessibleName = Loc.T("reg.field_receive_date"), PlaceholderText = Loc.T("reg.field_receive_date"), ReadOnly = true };
    private readonly TextBox _txtMaterialNotes = new() { AccessibleName = Loc.T("field.notes") };

    // ── Panel chứa các trường mía (để ẩn/hiện) ──
    private Panel _pnlSugarcaneFields = null!;
    // ── Panel chi tiết mía (expand/collapse) ──
    private Panel _pnlDetailFields = null!;
    private readonly Button _btnExpandDetails = new() { Text = Loc.T("reg.btn_more_info"), AutoSize = false, Height = 44, Padding = new Padding(4, 0, 4, 0) };
    // ── Panel chứa các trường vật tư (để ẩn/hiện) ──
    private Panel _pnlMaterialFields = null!;

    // ── Action buttons ──
    private readonly Button _btnSave = GridBuilder.PrimaryButton(Loc.T("reg.btn_register"), new Size(140, 36));
    private readonly Button _btnPrint = GridBuilder.SecondaryButton(Loc.T("reg.btn_print"), new Size(130, 36));
    private readonly Button _btnRetryDigi = GridBuilder.SecondaryButton(Loc.T("reg.btn_retry_digi"), new Size(140, 36));
    private readonly Button _btnCancel = GridBuilder.SecondaryButton(Loc.T("reg.btn_cancel_ticket"), new Size(110, 36));
    private readonly Button _btnDelete = GridBuilder.SecondaryButton(Loc.T("reg.btn_delete_ticket"), new Size(110, 36));
    private readonly Button _btnReset = GridBuilder.SecondaryButton(Loc.T("reg.btn_reset"), new Size(130, 36));

    // ── Camera views (panel phải trên) ──
    private readonly CameraViewControl _cameraIn = new() { CameraPosition = "In" };
    private readonly CameraViewControl _cameraOut = new() { CameraPosition = "Out" };

    // ── Notification panel (panel phải dưới) ──
    private readonly RichTextBox _rtbNotifications = new();

    // ── Trip grid (Req 7.1) ──
    private readonly DataGridView _dgvTrips = GridBuilder.ReadOnlyGrid(Loc.T("reg.section_grid"));

    // ── Phân trang + thanh cuộn lớn + phóng to lưới ──
    private readonly GridPager _pager = new("reg.trips", defaultPageSize: 20);
    private IReadOnlyList<TripRegistrationRow> _allTripRows = [];
    private GridScrollBar? _gridScroll;
    private readonly Button _btnExpandGrid = GridBuilder.SecondaryButton(Loc.T("grid.expand"), new Size(150, 40));

    // ── Tách lưới Mía / Vật tư + xuất Excel ──
    private readonly TripCategorySwitch _gridCategory = new("reg.trips");
    private readonly Button _btnExportExcel = GridBuilder.ExportExcelButton();
    private bool _gridExpanded;
    private const float TopAreaPercent = 58F; // lưới đủ chỗ cho thanh lọc + vài dòng + thanh phân trang
    private TableLayoutPanel _root = null!;
    private Control _topArea = null!;
    private Control _actionBarCard = null!;

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
        // Chiều cao tối thiểu phải nhỏ hơn vùng tab thực tế (cửa sổ − ~160px dải thương hiệu/menu/tab/status).
        // 860 lớn hơn vùng tab → form tràn xuống dưới, thanh phân trang ở chân lưới bị khuất (chỉ thấy khi phóng to lưới).
        // Phần form nhập liệu tự cuộn khi thiếu chỗ.
        MinimumSize = new Size(1280, 640);
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
        // Lưới cần đủ chỗ cho vài dòng + thanh lọc + thanh phân trang; form nhập liệu tự cuộn nếu thiếu chỗ.
        root.RowStyles.Add(new RowStyle(SizeType.Percent, TopAreaPercent));   // Top area (form + camera)
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, FormLayout.ActionRowHeight));   // Action bar
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100 - TopAreaPercent));   // Grid area

        _root = root;
        _topArea = BuildTopArea();
        _actionBarCard = BuildActionBar();

        root.Controls.Add(_topArea, 0, 0);
        root.Controls.Add(_actionBarCard, 0, 1);
        root.Controls.Add(BuildGridArea(), 0, 2);

        Controls.Add(root);

        // ListBox popup cho autocomplete biển số — thêm sau root để nằm trên cùng
        _lstPlatePopup.Font = AgrisTheme.Body(10F);
        Controls.Add(_lstPlatePopup);
        _lstPlatePopup.BringToFront();

        _lstLenhDonPopup.Font = AgrisTheme.Body(10F);
        Controls.Add(_lstLenhDonPopup);
        _lstLenhDonPopup.BringToFront();
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
        camInCard.Controls.Add(GridBuilder.SectionHeader(Loc.T("reg.camera_in")));
        top.Controls.Add(camInCard, 1, 0);

        // Cột phải: Camera Ra (20%)
        var camOutCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(1) };
        _cameraOut.Dock = DockStyle.Fill;
        _cameraOut.ApplyPositionLabel();
        camOutCard.Controls.Add(_cameraOut);
        camOutCard.Controls.Add(GridBuilder.SectionHeader(Loc.T("reg.camera_out")));
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
        // Hai ô tìm ngang hàng: theo biển số HOẶC theo lệnh đốn.
        FormLayout.AddField(caneTable, Loc.T("reg.field_search_plate"), platePanel);

        // Lệnh đốn + ▼ dropdown — tìm được như biển số (gõ lọc / chọn danh sách)
        var lenhDonPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        lenhDonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        lenhDonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
        lenhDonPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _txtShipment.Font = AgrisTheme.Body(12F, FontStyle.Bold);
        _txtShipment.Dock = DockStyle.Fill;
        _txtShipment.PlaceholderText = Loc.T("reg.field_lenhdon_placeholder");
        _txtShipment.BackColor = Color.FromArgb(255, 255, 220);
        _txtShipment.BorderStyle = BorderStyle.FixedSingle;
        _btnLenhDonDropdown.Dock = DockStyle.Fill;
        _btnLenhDonDropdown.FlatStyle = FlatStyle.Flat;
        _btnLenhDonDropdown.FlatAppearance.BorderSize = 1;
        _btnLenhDonDropdown.Cursor = Cursors.Hand;
        _btnLenhDonDropdown.TabStop = false;
        lenhDonPanel.Controls.Add(_txtShipment, 0, 0);
        lenhDonPanel.Controls.Add(_btnLenhDonDropdown, 1, 0);
        FormLayout.AddField(caneTable, Loc.T("reg.field_search_lenhdon"), lenhDonPanel);

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
        _btnExpandDetails.Text = Loc.T("reg.btn_more_info");
        _btnExpandDetails.Font = AgrisTheme.Body(9F, FontStyle.Bold);
        _btnExpandDetails.Dock = DockStyle.Fill;
        _btnExpandDetails.Margin = Padding.Empty;
        _btnExpandDetails.AccessibleName = Loc.T("reg.more_info");
        // Nút nằm trên thanh tiêu đề (luôn nhìn thấy, không bị cắt khi khung thấp) và
        // mở đúng popup theo nghiệp vụ đang chọn: mía → chi tiết FRM, vật tư → chi tiết ERP.
        _btnExpandDetails.Click += (_, _) =>
        {
            if (_pnlMaterialFields.Visible) ShowMaterialDetailPopup();
            else ShowDetailPopup();
        };

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

        // Khung ngày đơn hàng ERP — giới hạn tra cứu + danh sách chọn nhanh Số TRIP (như khung ngày FRM).
        var erpDatePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = false,
            Margin = Padding.Empty
        };
        // Mặc định hôm nay → hôm nay, giống khung ngày lệnh đốn FRM.
        _dtpErpFrom.Value = DateTime.Today;
        _dtpErpTo.Value = DateTime.Today;
        erpDatePanel.Controls.Add(_dtpErpFrom);
        erpDatePanel.Controls.Add(new Label
        {
            Text = "→", AutoSize = true, TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(6, 6, 6, 0)
        });
        erpDatePanel.Controls.Add(_dtpErpTo);
        FormLayout.AddField(materialTable, Loc.T("reg.erp_date_range"), erpDatePanel);
        _btnErpTripDropdown.Click += async (_, _) => await ShowErpTripListAsync();

        // Số TRIP (+ ▼ danh sách trong khung ngày) + Biển số + Nút Kiểm tra (cùng 1 hàng)
        var tripCheckPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        tripCheckPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40)); // Số TRIP
        tripCheckPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30)); // ▼
        // Cột nhãn/nút đủ rộng cho cỡ chữ phóng to 1.5× — trước đó "Biển số" và "Kiểm tra" bị cắt.
        tripCheckPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90)); // label
        tripCheckPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35)); // Biển số
        tripCheckPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8));  // spacer
        tripCheckPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150)); // nút
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
        tripCheckPanel.Controls.Add(_btnErpTripDropdown, 1, 0);
        tripCheckPanel.Controls.Add(lblBienSo, 2, 0);
        tripCheckPanel.Controls.Add(_txtMaterialPlate, 3, 0);
        tripCheckPanel.Controls.Add(new Panel { Dock = DockStyle.Fill }, 4, 0);
        tripCheckPanel.Controls.Add(_btnCheckErp, 5, 0);
        FormLayout.AddField(materialTable, "Số TRIP", tripCheckPanel);

        // Thông tin chi tiết ERP (chỉ hiện inline: Shipment + Biển số xe)
        FormLayout.AddField(materialTable, "Shipment Number", _txtMaterialShipment);
        FormLayout.AddField(materialTable, "Biển số xe", _txtMaterialPlateDetail);
        FormLayout.AddField(materialTable, Loc.T("field.notes"), _txtMaterialNotes);

        // Nút "Xem thêm" dùng chung một nút trên thanh tiêu đề (xem _btnExpandDetails).

        // Wire event: Kiểm tra ERP
        _btnCheckErp.Click += async (_, _) => await CheckErpAsync();

        materialTable.Dock = DockStyle.Fill;
        _pnlMaterialFields.Controls.Add(materialTable);
        _pnlMaterialFields.Dock = DockStyle.Fill;

        // Ghi chú đã chuyển lên phần tìm nhanh
        // Thông báo đã chuyển lên trên cùng

        // Layout: tiêu đề (cố định, có nút "Xem thêm") + vùng nội dung CUỘN ĐƯỢC.
        // Hai panel mía/vật tư neo Top với chiều cao đúng bằng số dòng — khung thấp hơn nội dung
        // thì hiện thanh cuộn thay vì cắt mất các dòng cuối (trước đây Dock.Fill nên không bao giờ cuộn).
        caneTable.Dock = DockStyle.Top;
        materialTable.Dock = DockStyle.Top;
        _pnlSugarcaneFields.Dock = DockStyle.Top;
        _pnlSugarcaneFields.Height = TableHeight(caneTable);
        _pnlMaterialFields.AutoSize = false;
        _pnlMaterialFields.Dock = DockStyle.Top;
        _pnlMaterialFields.Height = TableHeight(materialTable);

        table.Dock = DockStyle.Top;
        table.AutoSize = true;

        var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = AgrisTheme.Surface };
        body.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8, BackColor = AgrisTheme.Surface }); // đệm đáy
        body.Controls.Add(_pnlMaterialFields);  // ẩn mặc định
        body.Controls.Add(_pnlSugarcaneFields); // hiện mặc định
        body.Controls.Add(table);               // notification + loại nghiệp vụ

        // Nút "Xem thêm thông tin chuyến" bên phải thanh tiêu đề.
        var headerAction = new Panel
        {
            Dock = DockStyle.Right,
            Width = 330,
            Padding = new Padding(0, 5, 10, 5),
            BackColor = Color.Transparent
        };
        headerAction.Controls.Add(_btnExpandDetails);
        header.Controls.Add(headerAction);

        card.AutoScroll = false;
        card.Controls.Add(body);
        card.Controls.Add(header);

        return card;
    }

    /// <summary>Chiều cao thật của bảng FieldTable (mọi dòng đều cao tuyệt đối).</summary>
    private static int TableHeight(TableLayoutPanel table)
        => (int)table.RowStyles.Cast<RowStyle>().Sum(r => r.SizeType == SizeType.Absolute ? r.Height : FormLayout.RowHeight)
           + table.Padding.Vertical + 4;

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

        // Lưới chuyến đi theo nghiệp vụ đang đăng tài (người dùng vẫn đổi tay được trên lưới).
        _gridCategory.SetCategory(isSugarcane ? TripCategory.Sugarcane : TripCategory.Material);
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

        // Ô sửa được (sync ngược về form khi Lưu) — trộn chung một danh sách với thông tin FRM chỉ đọc.
        var fields = new (string Label, TextBox Source, TextBox Popup)[]
        {
            (Loc.T("field.sugarcane_owner"), _txtSugarcaneOwner, MakePopupTxt(_txtSugarcaneOwner)),
            (Loc.T("field.sugarcane_type"), _txtSugarcaneType, MakePopupTxt(_txtSugarcaneType)),
            (Loc.T("field.sugarcane_variety"), _txtSugarcaneVariety, MakePopupTxt(_txtSugarcaneVariety)),
            (Loc.T("field.station"), _txtStation, MakePopupTxt(_txtStation)),
            (Loc.T("reg.field_crop_investment"), _txtCropSeason, MakePopupTxt(_txtCropSeason)),
            (Loc.T("field.vehicle_type"), _txtVehicleType, MakePopupTxt(_txtVehicleType)),
            (Loc.T("reg.field_capacity") + " (T)", _txtTaiTrong, MakePopupTxt(_txtTaiTrong)),
            (Loc.T("reg.field_card_code"), _txtMaThe, MakePopupTxt(_txtMaThe)),
        };
        Control Edit(int i) => fields[i].Popup;

        var ld = CurrentLenhDon(PlateText);
        var info = LenhDonDetailValues(ld);
        Control Info(string key) => ReadOnlyInfo(info.TryGetValue(key, out var v) ? v : null);

        // Một danh sách duy nhất, xếp theo nhóm: lệnh đốn → hợp đồng & chủ mía → mía → vùng → xe.
        // Không lặp: mỗi thông tin chỉ một dòng.
        var rows = new List<(string Label, Control Field)>();
        if (ld is not null)
        {
            rows.Add((Loc.T("reg.detail_ma_ld"), Info("ma_ld")));
            rows.Add((Loc.T("reg.detail_status"), Info("status")));
            rows.Add((Loc.T("reg.detail_validity"), Info("validity")));
            rows.Add((Loc.T("reg.detail_priority"), Info("priority")));
            rows.Add((Loc.T("reg.detail_contract_type"), Info("contract_type")));
            rows.Add((Loc.T("reg.detail_contract"), Info("contract")));
        }
        rows.Add((fields[0].Label, Edit(0)));                         // Chủ mía
        if (ld is not null) rows.Add((Loc.T("reg.detail_customer_company"), Info("customer_company")));
        rows.Add((fields[1].Label, Edit(1)));                         // Loại mía
        rows.Add((fields[2].Label, Edit(2)));                         // Giống mía
        if (ld is not null)
        {
            rows.Add((Loc.T("reg.detail_burnt"), Info("burnt")));
            rows.Add((Loc.T("reg.detail_harvest"), Info("harvest")));
            rows.Add((Loc.T("reg.detail_cane_form"), Info("cane_form")));
            rows.Add((Loc.T("reg.detail_harvest_partner"), Info("harvest_partner")));
            rows.Add((Loc.T("reg.detail_plot"), Info("plot")));
        }
        rows.Add((fields[3].Label, Edit(3)));                         // Trạm
        rows.Add((fields[4].Label, Edit(4)));                         // Vụ đầu tư
        rows.Add((fields[5].Label, Edit(5)));                         // Loại xe
        rows.Add((fields[6].Label, Edit(6)));                         // Tải trọng
        rows.Add((fields[7].Label, Edit(7)));                         // Mã thẻ xe
        if (ld is not null)
        {
            rows.Add((Loc.T("reg.detail_temp_card"), Info("temp_card")));
            if (ld.IsTemporaryCard)
            {
                rows.Add((Loc.T("reg.detail_temp_plate"), Info("temp_plate")));
                rows.Add((Loc.T("reg.detail_original_plate"), Info("original_plate")));
            }
            rows.Add((Loc.T("reg.detail_used_plate"), Info("used_plate")));
        }

        foreach (var (label, field) in rows)
            FormLayout.AddField(table, label, field);

        if (ld is null)
            FormLayout.AddFullWidth(table, GridBuilder.Notice(Loc.T("reg.detail_no_lenhdon")), 44);
        else if (ld.IsTemporaryCard)
            FormLayout.AddFullWidth(table,
                GridBuilder.Notice(Loc.T("reg.detail_temp_flow_note", ld.VehiclePlate ?? "—")), 56);

        var btnOk = GridBuilder.PrimaryButton("Lưu thay đổi", new Size(160, 42));
        btnOk.DialogResult = DialogResult.OK;
        var btnCancel = GridBuilder.SecondaryButton("Đóng", new Size(120, 42));
        btnCancel.DialogResult = DialogResult.Cancel;

        var actionBar = FormLayout.ActionBar(btnOk, btnCancel);
        FormLayout.AddFullWidth(table, actionBar, FormLayout.ActionRowHeight);

        popup.Size = new Size(760, 860);
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

    /// <summary>Ô chỉ đọc cho thông tin lấy từ FRM (không sửa trên app cân).</summary>
    private static TextBox ReadOnlyInfo(string? value) => new()
    {
        Text = string.IsNullOrWhiteSpace(value) ? "—" : value,
        ReadOnly = true,
        Dock = DockStyle.Fill,
        Font = AgrisTheme.Body(11F),
        BackColor = AgrisTheme.ReadOnlyBg,
        ForeColor = AgrisTheme.ReadOnlyText,
        BorderStyle = BorderStyle.FixedSingle,
        TabStop = false
    };

    /// <summary>
    /// Giá trị hiển thị của lệnh đốn FRM cho popup Xem thêm: hiệu lực, hợp đồng, mía cháy, thu hoạch,
    /// phân loại trên phiếu cân (cháy / khúc / cây), thẻ xe tạm và biển số dùng để đăng tài / cân.
    /// </summary>
    private static Dictionary<string, string?> LenhDonDetailValues(FrmLenhDonInfo? ld)
        => LenhDonDetails.Values(ld);

    /// <summary>
    /// Popup chi tiết chuyến vật tư (ERP) — CHỈ XEM. Dữ liệu luồng vật tư lấy nguyên từ ERP
    /// (nhà vận chuyển, lái xe, kho, hàng hóa, KH/NCC, ngày nhận) và không được sửa trên app cân.
    /// </summary>
    private void ShowMaterialDetailPopup()
    {
        using var popup = new Form
        {
            Text = "📋 " + Loc.T("reg.material_detail_title"),
            Size = new Size(620, 540),
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

        FormLayout.AddFullWidth(table, GridBuilder.Notice(Loc.T("reg.material_detail_readonly"), AgrisTheme.Info), 44);

        var fields = new (string Label, TextBox Source)[]
        {
            (Loc.T("field.transporter"), _txtTransporter),
            (Loc.T("field.driver"), _txtDriver),
            (Loc.T("field.warehouse"), _txtWarehouse),
            (Loc.T("field.commodity"), _txtCommodity),
            (Loc.T("field.customer"), _txtMaterialCustomer),
            (Loc.T("reg.field_receive_date"), _txtReceiveDate),
        };

        foreach (var (label, source) in fields)
        {
            FormLayout.AddField(table, label, new TextBox
            {
                Text = string.IsNullOrWhiteSpace(source.Text) ? Loc.T("print.none") : source.Text,
                Font = AgrisTheme.Body(11F),
                Dock = DockStyle.Fill,
                ReadOnly = true,
                TabStop = false,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = AgrisTheme.ReadOnlyBg,
                ForeColor = AgrisTheme.ReadOnlyText
            });
        }

        var btnClose = GridBuilder.SecondaryButton(Loc.T("common.close"), new Size(120, 42));
        btnClose.DialogResult = DialogResult.Cancel;
        FormLayout.AddFullWidth(table, FormLayout.ActionBar(btnClose), FormLayout.ActionRowHeight);

        popup.Controls.Add(table);
        popup.AcceptButton = btnClose;
        popup.CancelButton = btnClose;
        popup.ShowDialog(this);
    }

    /// <summary>
    /// ▼ cạnh Số TRIP: danh sách đơn hàng ERP trong khung ngày (đã kéo về máy). Chọn → điền TRIP/biển số và kiểm tra luôn.
    /// </summary>
    private async Task ShowErpTripListAsync()
    {
        if (_dtpErpFrom.Value.Date > _dtpErpTo.Value.Date)
        {
            AppendNotification(Loc.T("reg.erp_date_invalid"));
            return;
        }

        IReadOnlyList<TTC.Weighing.Infrastructure.Adapters.ErpOrderLine> lines;
        try
        {
            _btnErpTripDropdown.Enabled = false;
            lines = await _presenter.ListErpOrdersInRangeAsync(_dtpErpFrom.Value, _dtpErpTo.Value, FormToken);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            AppendNotification($"⚠ {ex.Message}");
            return;
        }
        finally
        {
            if (!IsDisposed) _btnErpTripDropdown.Enabled = true;
        }

        if (IsDisposed) return;
        if (lines.Count == 0)
        {
            AppendNotification(Loc.T("reg.erp_none_in_range",
                _dtpErpFrom.Value.ToString("dd/MM/yyyy"), _dtpErpTo.Value.ToString("dd/MM/yyyy")));
            return;
        }

        var menu = new ContextMenuStrip { Font = AgrisTheme.Body(10F), ShowImageMargin = false, MaximumSize = new Size(900, 520) };
        foreach (var line in lines.Take(200))
        {
            var trip = line.TripNumber ?? line.ShipmentNumber;
            var date = TripRegistrationPresenter.ErpDate(line)?.ToString("dd/MM") ?? "--/--";
            var text = $"{trip}   {line.PlateNumber ?? "—"}   {date}   {line.ItemDescription ?? line.ItemName ?? ""}";
            var item = new ToolStripMenuItem(text) { Tag = line };
            item.Click += async (_, _) =>
            {
                _txtTripNumber.Text = trip;
                _txtMaterialPlate.Text = line.PlateNumber ?? "";
                await CheckErpAsync();
            };
            menu.Items.Add(item);
        }
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(_btnErpTripDropdown, new Point(_btnErpTripDropdown.Width - menu.Width, _btnErpTripDropdown.Height));
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
                erpLine = await _presenter.LookupErpOrderAsync(bu, searchKey, searchPlate, FormToken,
                    _dtpErpFrom.Value, _dtpErpTo.Value);
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

                _txtTransporter.Text = erpLine.EffectiveTransporter ?? "";
                _txtDriver.Text = erpLine.DriverName ?? "";
                // Kho = tên tổ chức kho ERP (organizationName), ví dụ "126501 - Thanh pham - PROC".
                _txtWarehouse.Text = erpLine.EffectiveWarehouse ?? "";
                _txtCommodity.Text = erpLine.ItemDescription ?? erpLine.ItemName ?? "";
                _txtMaterialCustomer.Text = erpLine.VendorName ?? erpLine.CustomerName ?? "";
                _txtReceiveDate.Text = TripRegistrationPresenter.ErpDate(erpLine)?.ToString("dd/MM/yyyy HH:mm") ?? erpLine.TransactionDate ?? "";

                _txtShipment.Text = erpLine.ShipmentNumber ?? tripNumber;
                // Hướng theo luồng ERP: ship confirm (SO/TO) và WMS (luồng SO) là xuất, còn lại là nhập.
                _rbMaterialOut.Checked = erpLine.IsShipConfirmFlow;
                _rbMaterialIn.Checked = !erpLine.IsShipConfirmFlow;
                _btnSave.Enabled = true;

                var typeInfo = !string.IsNullOrWhiteSpace(erpLine.TransactionTypeName)
                    ? $" [{erpLine.TransactionTypeCode} - {erpLine.TransactionTypeName}]"
                    : "";
                AppendNotification($"✅ ERP: {erpLine.ShipmentNumber} — {erpLine.ItemDescription ?? erpLine.ItemName}{typeInfo}");
                AppendNotification($"   KH/NCC: {erpLine.VendorName ?? erpLine.CustomerName} | Org: {erpLine.OrganizationCode} | Biển số: {erpLine.PlateNumber ?? "(không có)"}");

                // Shipment đã đăng tài / xe chưa cân ra → báo ngay khi kiểm tra (F4 cũng chặn lại).
                await ValidateMaterialRegistrationAsync(_txtPlate.Text.Trim(), isTemp: false);
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
            // Danh sách biển số / lệnh đốn đều lấy từ lệnh đốn đăng tài được trong khung ngày.
            RefreshPlateFilter();
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

        // Hàm này được gọi nhiều lần liên tiếp (mở form, đổi Từ ngày, đổi Đến ngày). Trước đây các lần gọi
        // chạy chồng trên cùng DbContext → lỗi "second operation" → danh sách lệnh đốn bị gán rỗng âm thầm,
        // còn biển số lấy từ nhánh dự phòng → hai ô lệch nhau. Nay: tuần tự qua _dbLock, chỉ áp kết quả lần mới nhất.
        var requestId = ++_registrableRequestId;
        RegistrableLenhDonList result;
        try
        {
            await _dbLock.WaitAsync(_cts.Token);
            try
            {
                if (requestId != _registrableRequestId) return; // đã có lần gọi mới hơn
                result = await _presenter.GetRegistrableLenhDonAsync(from, to, _cts.Token);
            }
            finally { _dbLock.Release(); }
        }
        catch (OperationCanceledException) { return; } // tab đang đóng
        catch (ObjectDisposedException) { return; }
        catch (Exception ex)
        {
            result = RegistrableLenhDonList.Empty;
            AppendNotification($"⚠ {ex.Message}");
        }
        if (IsDisposed || requestId != _registrableRequestId) return;
        _registrable = result;

        // Cả hai ô cùng một nguồn → biển số nào hiện thì lệnh đốn của nó cũng hiện, và ngược lại.
        _filteredPlates = _registrable.Plates;
        _filteredLenhDonCodes = _registrable.Codes;
        _lstPlatePopup.Visible = false;
        _lstLenhDonPopup.Visible = false;

        AppendNotification(Loc.T("reg.search_range_info", _filteredPlates.Length, _filteredLenhDonCodes.Length,
            from.ToString("dd/MM/yyyy"), _dtpFrmTo.Value.ToString("dd/MM/yyyy")));

        // Trống → nói rõ lệnh đốn trong khung ngày bị loại vì lý do gì.
        if (_filteredLenhDonCodes.Length == 0
            && (_registrable.AlreadyRegistered + _registrable.NotApproved + _registrable.NoPlate) > 0)
        {
            AppendNotification(Loc.T("reg.registrable_excluded",
                _registrable.AlreadyRegistered, _registrable.NotApproved, _registrable.NoPlate));
        }
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

        // Nút Xóa phiếu: chữ đỏ để phân biệt với Hủy phiếu (xóa là thao tác khó hoàn tác).
        _btnDelete.ForeColor = AgrisTheme.Danger;
        _btnDelete.FlatAppearance.BorderColor = AgrisTheme.Danger;
        var bar = FormLayout.ActionBar(_btnSave, _btnPrint, _btnRetryDigi, _btnCancel, _btnDelete, _btnReset);
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

        // ── Thanh công cụ: Mía/Vật tư + tab + khoảng ngày | Xuất Excel + Phóng to ──
        _btnTabInFactory.Margin = new Padding(0, 0, 4, 0);
        _btnTabExited.Margin = new Padding(0, 0, 4, 0);
        _btnTabCancelled.Margin = new Padding(0, 0, 16, 0);

        var lblFrom = GridToolbar.FilterLabel(Loc.T("reg.filter_from"));
        var lblTo = GridToolbar.FilterLabel("→", new Padding(6, 9, 6, 0));

        _dtpFrom.Margin = new Padding(0, 4, 0, 0);
        _dtpTo.Margin = new Padding(0, 4, 0, 0);

        _btnExpandGrid.Margin = new Padding(8, 0, 0, 0);
        _btnExpandGrid.Click += (_, _) => ToggleGridExpanded();

        _btnExportExcel.Margin = new Padding(8, 0, 0, 0);
        _btnExportExcel.Click += async (_, _) => await ExportTripGridAsync();

        _gridCategory.CategoryChanged += async (_, _) =>
        {
            _pager.Reset();
            GridBuilder.ApplyCategoryHeader(card, header, Loc.T("reg.section_grid"), _gridCategory.Category);
            await LoadGridAsync();
        };

        var filterBar = new GridToolbar(
            [_gridCategory, GridToolbar.Divider(), _btnTabInFactory, _btnTabExited, _btnTabCancelled,
             GridToolbar.Group(lblFrom, _dtpFrom, lblTo, _dtpTo)],
            [_btnExportExcel, _btnExpandGrid]);

        GridBuilder.ApplyCategoryHeader(card, header, Loc.T("reg.section_grid"), _gridCategory.Category);

        GridBuilder.AttachEmptyState(_dgvTrips, Loc.T("reg.no_trips"));

        _pager.PageChanged += (_, _) =>
        {
            BindCurrentPage();
            if (_dgvTrips.Rows.Count > 0)
                _dgvTrips.FirstDisplayedScrollingRowIndex = 0;
        };

        // Chế độ thường không phân trang (lưới cuộn dọc); bật khi Phóng to lưới.
        _pager.PagingEnabled = false;

        // Thứ tự Add ngược thứ tự dock: lưới (Fill) → pager (Bottom) → thanh lọc → tiêu đề.
        card.Controls.Add(_dgvTrips);
        card.Controls.Add(_pager);
        card.Controls.Add(filterBar);
        card.Controls.Add(header);

        _gridScroll = GridScrollBar.Attach(_dgvTrips);

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

        _dtpFrom.ValueChanged += async (_, _) => { _pager.Reset(); await LoadGridAsync(); };
        _dtpTo.ValueChanged += async (_, _) => { _pager.Reset(); await LoadGridAsync(); };
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

        _txtShipment.TextChanged += OnLenhDonTextChanged;
        _txtShipment.KeyDown += OnLenhDonKeyDown;
        _txtShipment.Leave += OnLenhDonLeave;
        _btnLenhDonDropdown.Click += OnLenhDonDropdownClicked;
        _lstLenhDonPopup.Click += (_, _) => { if (_lstLenhDonPopup.SelectedItem is string c) SelectLenhDonFromPopup(c); };
        _lstLenhDonPopup.KeyDown += OnLenhDonPopupKeyDown;
        _btnSave.Click += OnSaveClicked;
        _btnPrint.Click += OnPrintClicked;
        _btnRetryDigi.Click += async (_, _) => await RetryPushDangTaiDigiAsync();
        _btnCancel.Click += async (_, _) => await CancelSelectedTripAsync();
        _btnDelete.Click += async (_, _) => await DeleteSelectedTripAsync();
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
            AppendNotification(Loc.T("reg.no_lenhdon_in_range"));
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

        // Gợi ý chỉ lấy từ danh sách lệnh đốn đăng tài được — cùng nguồn với ô lệnh đốn.
        // Biển số gõ tay ngoài danh sách vẫn tra cứu được (Enter / rời ô) để báo đúng lý do.
        var filtered = _filteredPlates
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

            // Thứ tự chọn lệnh đốn cho biển số:
            // 1) lệnh đốn đã chọn theo mã của đúng xe này;
            // 2) lệnh đốn đăng tài được trong khung ngày (ưu tiên trước) — đúng lệnh đang hiện ở ô lệnh đốn;
            // 3) tra toàn bộ cache (để báo đúng lý do: chưa duyệt, hết hạn, đã đăng tài...).
            var lenhDon = _selectedLenhDon is { } chosen && NormalizePlate(chosen.VehiclePlate) == normalizedPlate
                ? chosen
                : _registrable.Available.FirstOrDefault(ld => NormalizePlate(ld.VehiclePlate) == normalizedPlate)
                  ?? _presenter.FindLenhDonByPlate(plate);

            if (lenhDon is not null)
            {
                await ApplyLenhDonAsync(lenhDon, plate);
            }
            else
            {
                _selectedLenhDon = null;
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

    /// <summary>
    /// Điền form từ một lệnh đốn (chung cho tìm theo biển số và theo mã lệnh đốn),
    /// ghi nhớ lệnh đốn đã chọn, và cảnh báo nếu lệnh đốn đã có phiếu đăng tài active.
    /// </summary>
    /// <param name="viewOnly">true = chỉ điền để xem (bấm từ lưới chuyến): bỏ qua kiểm tra đăng tài và cảnh báo.</param>
    private async Task ApplyLenhDonAsync(FrmLenhDonInfo lenhDon, string? plate, bool viewOnly = false)
    {
        var effectivePlate = string.IsNullOrWhiteSpace(plate) ? lenhDon.VehiclePlate ?? "" : plate;

        _selectedLenhDon = lenhDon;
        _lastLookedUpLenhDon = lenhDon.MaLD;
        _lastLookedUpPlate = NormalizePlate(effectivePlate);

        SetTextSilently(_txtShipment, lenhDon.MaLD, OnLenhDonTextChanged);
        if (!string.IsNullOrWhiteSpace(effectivePlate))
            SetTextSilently(_txtPlate, effectivePlate, OnPlateTextChanged);

        _txtVehicleType.Text = lenhDon.LoaixeName ?? "";
        _txtSugarcaneOwner.Text = lenhDon.KHName ?? "";
        _txtSugarcaneType.Text = lenhDon.LoaiMiaName ?? "";
        _txtSugarcaneVariety.Text = lenhDon.GiongMiaName ?? "";
        _txtStation.Text = lenhDon.TramName ?? "";
        _txtCropSeason.Text = lenhDon.VudautuName ?? "";
        _txtHarvestMethod.Text = HarvestMethodText(lenhDon) ?? "";
        _txtTaiTrong.Text = lenhDon.TaiTrong.HasValue ? $"{lenhDon.TaiTrong:N1}" : "";
        // Chỉ mã thẻ thật; trạng thái thẻ xe tạm hiện ở dòng "Thẻ xe tạm" riêng (không ghép chữ vào mã thẻ).
        _txtMaThe.Text = lenhDon.Thexename ?? "";

        // Nhà vận chuyển từ the-xe (cache)
        var vehicles = string.IsNullOrWhiteSpace(effectivePlate) ? [] : _presenter.FindVehicleByPlate(effectivePlate);
        _txtCustomerOrSupplier.Text = vehicles.FirstOrDefault(v => v.DoiTacVanChuyenName is not null)?.DoiTacVanChuyenName ?? "";

        // Xem chuyến đã đăng tài: chỉ hiển thị, không kiểm tra / cảnh báo đăng tài.
        if (viewOnly) return;

        if (lenhDon.IsTemporaryCard)
            AppendNotification(Loc.T("reg.temp_card_notice", lenhDon.MaLD, lenhDon.SoXeTam ?? effectivePlate));
        if (lenhDon.MiaChay == true)
            AppendNotification(Loc.T("reg.burnt_cane_notice", lenhDon.MaLD, lenhDon.LoaiMiaChayName ?? "—"));

        AppendNotification($"✅ Lệnh đốn: {lenhDon.MaLD} | Biển số: {effectivePlate} | Chủ mía: {lenhDon.KHName} | Loại mía: {lenhDon.LoaiMiaName} | Giống: {lenhDon.GiongMiaName} | Trạm: {lenhDon.TramName} | Loại xe: {lenhDon.LoaixeName}");

        _selectedPlan = null; // Không dùng plan từ CheckPlanAsync — dữ liệu đã có từ lệnh đốn

        // Lệnh chưa duyệt / hết hạn / ngoài thời gian / xe không tồn tại / thẻ hủy → báo ngay khi tra cứu (F4 cũng chặn lại)
        if (ValidateLenhDonForRegistration(lenhDon, effectivePlate) is { } invalid
            && !await ShowRegistrationBlockedAsync(invalid, lenhDon, effectivePlate))
            return;

        // Lệnh đốn đã đăng tài → báo ngay
        var existingTrip = await _presenter.FindActiveTripByLenhDonAsync(lenhDon.SourceKey, FormToken);
        if (existingTrip is not null)
        {
            AppendNotification($"⚠ Lệnh đốn {lenhDon.MaLD} đã được đăng tài (số: {existingTrip}).");
            Helpers.DigiScalePopup.Show(this,
                "XE ĐÃ ĐĂNG TÀI",
                $"Biển số: {effectivePlate}\nLệnh đốn: {lenhDon.MaLD}\nSố đăng tài: {existingTrip}\n\nPhải hủy phiếu cũ trước khi đăng tài lại.",
                false, "Lệnh đốn đã có phiếu đăng tài active");
        }
    }

    /// <summary>
    /// Lý do DigiScale từ chối đăng tài, ngắn gọn và chỉ đúng đối tượng lỗi:
    /// DT-04 → thẻ xe nào chưa được duyệt; DT-03 → xe nào chưa có trong hợp đồng vận chuyển nào.
    /// Lệnh đốn "Đã duyệt" không có nghĩa thẻ xe / hợp đồng vận chuyển đã duyệt — nói rõ để người vận hành
    /// biết cần xử lý ở đâu trên FRM. Các lỗi khác → câu đã dịch chung.
    /// </summary>
    private static string DescribeRegistrationRejection(string? raw, FrmLenhDonInfo? ld, string plate)
    {
        var lines = Helpers.DigiScaleErrors.Describe(raw).Reason
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        var dt03 = Loc.T("digi.err_dt03");
        var dt04 = Loc.T("digi.err_dt04");

        // Lệnh đốn dùng THẺ XE TẠM: không có thẻ / hợp đồng vận chuyển trên FRM (DigiScale báo trạng thái thẻ '-1').
        // Không phải thẻ "Nháp" — nói đúng là DigiScale chưa nhận đăng tài cho lệnh đốn thẻ xe tạm.
        if (ld?.IsTemporaryCard == true && (lines.Contains(dt03) || lines.Contains(dt04)))
        {
            var others = lines.Where(l => l != dt03 && l != dt04);
            return string.Join(Environment.NewLine,
                new[] { Loc.T("reg.reject_temp_card", ld.MaLD, ld.VehiclePlate ?? plate) }.Concat(others));
        }

        var card = ld?.Thexename?.Trim();
        var contract = TripRegistrationPresenter.TransportContractOf(card);

        var cardLine = string.IsNullOrWhiteSpace(card) ? dt04 : Loc.T("reg.reject_card_draft", card);
        var result = lines
            .Select(line => line == dt04 ? cardLine
                : line == dt03 ? Loc.T("reg.reject_no_contract", plate, contract ?? "—")
                : line)
            .OrderBy(line => line == cardLine ? 0 : 1) // gốc vấn đề (thẻ xe) lên trước
            .ToList();

        return string.Join(Environment.NewLine, result);
    }

    /// <summary>
    /// Kiểm tra đăng tài vật tư (ERP), tương ứng các bước chặn của mía:
    /// 1) Shipment/TRIP ERP đã có chuyến active (chưa hủy/đóng) → mỗi chứng từ chỉ đăng tài 1 lần;
    /// 2) xe đang có chuyến chưa cân ra → phải cân ra trước.
    /// ERP không trả trạng thái duyệt / hạn chứng từ / thẻ xe nên không có các bước tương ứng của lệnh đốn.
    /// Trả true nếu được đăng tài.
    /// </summary>
    private async Task<bool> ValidateMaterialRegistrationAsync(string plate, bool isTemp)
    {
        if (!isTemp)
        {
            var shipment = _txtShipment.Text.Trim();
            var existingTrip = await _presenter.FindActiveTripByErpShipmentAsync(shipment, FormToken);
            if (existingTrip is not null)
            {
                var title = Loc.T("reg.erp_already_registered_title");
                var message = Loc.T("reg.erp_already_registered_msg", shipment, existingTrip);
                AppendNotification($"⛔ {title}: {message.Replace("\n", " ")}");
                Helpers.DigiScalePopup.ShowWarning(this, title, message);
                return false;
            }
        }

        var activeTrip = await _presenter.FindActiveTripByPlateAsync(plate, FormToken);
        if (activeTrip is not null)
        {
            var title = Loc.T("reg.vehicle_not_weighed_out_title");
            var message = Loc.T("reg.vehicle_not_weighed_out_msg", plate, activeTrip);
            AppendNotification($"⛔ {title}: {message.Replace("\n", " ")}");
            Helpers.DigiScalePopup.ShowWarning(this, title, message);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Kiểm tra lệnh đốn có đăng tài được không (dùng khi tra cứu và khi bấm Đăng tài):
    /// 1) thời điểm đăng tài phải nằm trong [Ngày cấp, Ngày hết hạn];
    /// 2) xe của lệnh đốn phải có trong danh mục xe vận chuyển trên Scale.
    /// Trả (tiêu đề, nội dung) lỗi, hoặc null nếu hợp lệ.
    /// </summary>
    private (string Title, string Message, bool VehicleMissing)? ValidateLenhDonForRegistration(FrmLenhDonInfo ld, string? plate)
    {
        var now = DateTimeOffset.Now;
        string Fmt(DateTimeOffset? d) => d?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "—";

        // 1. Lệnh đốn phải "Đã duyệt" (Nháp, Hủy, Đã tính tiền... đều chặn)
        if (!TripRegistrationPresenter.IsApproved(ld))
            return (Loc.T("reg.lenhdon_not_approved_title"),
                Loc.T("reg.lenhdon_not_approved_msg", ld.MaLD, ld.StatusCodeName ?? "—"), false);

        var (from, to) = TripRegistrationPresenter.RegistrationWindow(ld);

        // 2. Lệnh đốn hết hạn
        if (to is { } expiry && now > expiry)
            return (Loc.T("reg.lenhdon_expired_title"),
                Loc.T("reg.lenhdon_expired_msg", ld.MaLD, Fmt(expiry), now.ToString("dd/MM/yyyy HH:mm")), false);

        // 3. Chưa tới ngày cấp (ví dụ đăng tài 22/10, lệnh cấp 23/10)
        if (from is { } issued && now < issued)
            return (Loc.T("reg.lenhdon_out_of_window_title"),
                Loc.T("reg.lenhdon_out_of_window_msg", ld.MaLD, Fmt(issued), Fmt(to), now.ToString("dd/MM/yyyy HH:mm")), false);

        var vehiclePlate = string.IsNullOrWhiteSpace(plate) ? ld.VehiclePlate : plate;
        if (string.IsNullOrWhiteSpace(vehiclePlate)) return null;

        // Thẻ xe tạm: xe chưa có trong danh mục xe vận chuyển / thẻ xe FRM → không kiểm tra 4, 5.
        if (ld.IsTemporaryCard) return null;

        // 4. Xe phải có trong danh mục xe vận chuyển trên Scale
        if (!_presenter.HasVehicleCatalog)
        {
            // Chưa tải được danh mục xe → không kết luận "xe không tồn tại", chỉ nhắc.
            AppendNotification(Loc.T("reg.vehicle_catalog_not_loaded", vehiclePlate));
            return null;
        }

        var vehicles = _presenter.FindVehicleByPlate(vehiclePlate);
        if (vehicles.Count == 0)
            return (Loc.T("reg.vehicle_not_found_title"),
                Loc.T("reg.vehicle_not_found_msg", vehiclePlate, ld.MaLD), true);

        // 5. Thẻ xe phải đang hiệu lực (không hủy). Chỉ chặn khi CÓ bằng chứng thẻ không hiệu lực:
        //    danh mục thẻ FRM kéo về không đầy đủ, nên "không tìm thấy thẻ" KHÔNG được coi là lỗi
        //    (DigiScale là nơi kiểm tra cuối — lỗi DT-03/DT-04 được báo kèm mã thẻ / hợp đồng, xem OnSaveClicked).
        //    Tên thẻ trên lệnh đốn dạng "{HĐ vận chuyển}-{xe}-{mã thẻ}" → khớp theo mã thẻ ở cuối.
        var ldCard = _presenter.FindCardForLenhDon(ld.Thexename);
        if (ldCard is not null)
        {
            if (!IsCardActive(ldCard))
                return (Loc.T("reg.card_not_active_title"),
                    Loc.T("reg.card_not_active_msg", ldCard.MaThe ?? "—", vehiclePlate, ldCard.StatusName ?? "—"), false);
            return null;
        }

        var cards = vehicles.Where(v => !string.IsNullOrWhiteSpace(v.MaThe)).ToList();
        if (cards.Count > 0 && !cards.Any(IsCardActive))
            return (Loc.T("reg.card_not_active_title"),
                Loc.T("reg.card_not_active_msg", cards[0].MaThe ?? "—", vehiclePlate, cards[0].StatusName ?? "—"), false);

        return null;
    }

    /// <summary>
    /// Hình thức thu hoạch từ lệnh đốn FRM: "Thu hoạch máy" / "Thu hoạch thủ công", kèm "Mía cháy (thời gian)" nếu cháy.
    /// FRM không trả ThuHoachMay → fallback tên đối tác thu hoạch như trước.
    /// </summary>
    private static string? HarvestMethodText(FrmLenhDonInfo ld)
    {
        var method = ld.ThuHoachMay switch
        {
            true => Loc.T("reg.harvest_machine"),
            false => Loc.T("reg.harvest_manual"),
            null => ld.DoiTacThuHoachName
        };

        if (ld.MiaChay != true) return method;

        var burnt = string.IsNullOrWhiteSpace(ld.LoaiMiaChayName)
            ? Loc.T("reg.burnt_cane")
            : $"{Loc.T("reg.burnt_cane")} ({ld.LoaiMiaChayName})";
        return string.IsNullOrWhiteSpace(method) ? burnt : $"{method} - {burnt}";
    }

    /// <summary>Thẻ xe đang hiệu lực vận chuyển: không Hủy / hết hạn / Inactive / Nháp.</summary>
    private static bool IsCardActive(FrmVehicleInfo card)
    {
        var s = card.StatusName?.ToUpperInvariant() ?? "";
        return !(s.Contains("HỦY") || s.Contains("HUỶ") || s.Contains("HẾT HẠN") || s.Contains("INACTIVE")
                 || s.Contains("CANCEL") || s.Contains("NHÁP") || s.Contains("DRAFT"));
    }

    /// <summary>
    /// Báo lỗi không đăng tài được. Riêng "Xe không tồn tại": hỏi sync lại danh mục xe xuống app cân,
    /// sync xong kiểm tra lại. Trả true nếu sau khi sync lệnh đốn đã hợp lệ (cho đăng tài tiếp).
    /// </summary>
    private async Task<bool> ShowRegistrationBlockedAsync(
        (string Title, string Message, bool VehicleMissing) invalid, FrmLenhDonInfo ld, string? plate)
    {
        AppendNotification($"⛔ {invalid.Title}: {invalid.Message.Replace("\n", " ")}");

        if (!invalid.VehicleMissing)
        {
            Helpers.DigiScalePopup.ShowWarning(this, invalid.Title, invalid.Message);
            return false;
        }

        var answer = MessageBox.Show(this,
            invalid.Message + "\n\n" + Loc.T("reg.vehicle_sync_prompt"),
            invalid.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return false;

        AppendNotification(Loc.T("reg.vehicle_syncing"));
        try
        {
            await _presenter.RefreshFrmCacheAsync(FormToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Helpers.DigiScalePopup.ShowWarning(this, invalid.Title, Loc.T("reg.vehicle_sync_failed", ex.Message));
            return false;
        }

        if (ValidateLenhDonForRegistration(ld, plate) is { } stillInvalid)
        {
            AppendNotification($"⛔ {stillInvalid.Title}: {stillInvalid.Message.Replace("\n", " ")}");
            Helpers.DigiScalePopup.ShowWarning(this, stillInvalid.Title,
                (stillInvalid.VehicleMissing ? Loc.T("reg.vehicle_still_missing") + "\n\n" : "") + stillInvalid.Message);
            return false;
        }

        AppendNotification(Loc.T("reg.vehicle_sync_ok", plate ?? ld.VehiclePlate ?? "—"));
        return true;
    }

    /// <summary>Gán Text mà không kích hoạt autocomplete/lookup của ô đó.</summary>
    private static void SetTextSilently(TextBox box, string text, EventHandler handler)
    {
        box.TextChanged -= handler;
        box.Text = text;
        box.SelectionStart = text.Length;
        box.TextChanged += handler;
    }

    /// <summary>
    /// Lệnh đốn dùng cho đăng tài: lệnh đã chọn (nếu đúng xe đang nhập), nếu không thì tra theo biển số.
    /// </summary>
    private FrmLenhDonInfo? CurrentLenhDon(string? plate)
    {
        if (_selectedLenhDon is { } chosen
            && (string.IsNullOrWhiteSpace(plate) || NormalizePlate(chosen.VehiclePlate) == NormalizePlate(plate)))
            return chosen;

        return string.IsNullOrWhiteSpace(plate) ? null : _presenter.FindLenhDonByPlate(plate);
    }

    // ═══════════════════════════════════════════════════════════
    //  Tìm theo lệnh đốn — cùng cách dùng với ô biển số
    // ═══════════════════════════════════════════════════════════

    private bool IsSugarcaneMode => _cboProfileType.SelectedIndex == 0;

    private void OnLenhDonDropdownClicked(object? sender, EventArgs e)
    {
        if (!IsSugarcaneMode) { _lstLenhDonPopup.Visible = false; return; }

        if (_lstLenhDonPopup.Visible) { _lstLenhDonPopup.Visible = false; return; }

        if (_filteredLenhDonCodes.Length == 0)
        {
            AppendNotification(Loc.T("reg.no_lenhdon_in_range"));
            return;
        }

        ShowLenhDonPopup(_filteredLenhDonCodes.Take(200).ToArray());
        _lstLenhDonPopup.Focus();
        if (_lstLenhDonPopup.Items.Count > 0) _lstLenhDonPopup.SelectedIndex = 0;
    }

    private void OnLenhDonTextChanged(object? sender, EventArgs e)
    {
        // Gõ lại mã → bỏ lệnh đốn đã chọn trước đó (sẽ tra lại khi Enter/rời ô)
        if (_selectedLenhDon is not null
            && !string.Equals(_selectedLenhDon.MaLD, _txtShipment.Text.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            _selectedLenhDon = null;
            _lastLookedUpLenhDon = null;
        }

        if (!IsSugarcaneMode) { _lstLenhDonPopup.Visible = false; return; }

        var search = _txtShipment.Text.Trim();
        if (search.Length == 0) { _lstLenhDonPopup.Visible = false; return; }

        var filtered = _filteredLenhDonCodes
            .Where(c => c.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Take(30)
            .ToArray();

        if (filtered.Length == 0) { _lstLenhDonPopup.Visible = false; return; }
        ShowLenhDonPopup(filtered);
    }

    private void ShowLenhDonPopup(string[] codes)
    {
        _lstLenhDonPopup.Items.Clear();
        _lstLenhDonPopup.Items.AddRange(codes);

        var host = _txtShipment.Parent!;
        _lstLenhDonPopup.Location = PointToClient(host.PointToScreen(new Point(0, host.Height)));
        _lstLenhDonPopup.Width = host.Width;
        _lstLenhDonPopup.Height = Math.Min(codes.Length * _lstLenhDonPopup.ItemHeight + 4, 250);
        _lstLenhDonPopup.BringToFront();
        _lstLenhDonPopup.Visible = true;
    }

    private void OnLenhDonKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;

            if (_lstLenhDonPopup.Visible && _lstLenhDonPopup.Items.Count > 0)
                SelectLenhDonFromPopup(_lstLenhDonPopup.Items[0]?.ToString());
            else
            {
                _lstLenhDonPopup.Visible = false;
                _ = LookupByLenhDonAsync(_txtShipment.Text);
            }
            return;
        }

        if (!_lstLenhDonPopup.Visible) return;

        if (e.KeyCode == Keys.Down)
        {
            _lstLenhDonPopup.Focus();
            if (_lstLenhDonPopup.Items.Count > 0) _lstLenhDonPopup.SelectedIndex = 0;
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            _lstLenhDonPopup.Visible = false;
            e.Handled = true;
        }
    }

    private void OnLenhDonPopupKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && _lstLenhDonPopup.SelectedItem is string code)
        {
            SelectLenhDonFromPopup(code);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            _lstLenhDonPopup.Visible = false;
            _txtShipment.Focus();
            e.Handled = true;
        }
    }

    private async void SelectLenhDonFromPopup(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return;
        _lstLenhDonPopup.Visible = false;
        SetTextSilently(_txtShipment, code, OnLenhDonTextChanged);
        _txtShipment.Focus();
        await LookupByLenhDonAsync(code);
    }

    private async void OnLenhDonLeave(object? sender, EventArgs e)
    {
        await Task.Yield();
        if (_lstLenhDonPopup.Focused || _lstLenhDonPopup.ContainsFocus) return;
        _lstLenhDonPopup.Visible = false;

        if (!IsSugarcaneMode || _lookupInProgress) return;

        var code = _txtShipment.Text.Trim();
        if (code.Length < 3) return;
        if (string.Equals(_lastLookedUpLenhDon, code, StringComparison.OrdinalIgnoreCase)) return;

        await LookupByLenhDonAsync(code);
    }

    /// <summary>Tra mã lệnh đốn trong cache FRM → điền biển số + toàn bộ thông tin chuyến.</summary>
    private async Task LookupByLenhDonAsync(string code)
    {
        if (!IsSugarcaneMode || _lookupInProgress) return;
        code = code.Trim();
        if (code.Length == 0) return;

        _lookupInProgress = true;
        SetUiBusy(true);
        try
        {
            var lenhDon = _presenter.FindLenhDonByCode(code);
            if (lenhDon is null)
            {
                _selectedLenhDon = null;
                _lastLookedUpLenhDon = code;
                AppendNotification(Loc.T("reg.lenhdon_not_found", code));
                return;
            }

            if (string.IsNullOrWhiteSpace(lenhDon.VehiclePlate))
                AppendNotification(Loc.T("reg.lenhdon_no_plate", lenhDon.MaLD));

            await ApplyLenhDonAsync(lenhDon, lenhDon.VehiclePlate);
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
                ? CurrentLenhDon(plate) is null
                : string.IsNullOrWhiteSpace(_txtShipment.Text.Trim()) && _selectedPlan is null;

            // ═══════════════════════════════════════════════════════════
            //  Validations cho mía (FRM) — check lệnh đốn + thẻ xe
            // ═══════════════════════════════════════════════════════════
            if (isSugarcane)
            {
                var ldCheck = CurrentLenhDon(plate);

                if (ldCheck is not null)
                {
                    // 1–5. Chưa duyệt (Nháp) / hết hạn / ngoài thời gian đăng tài / xe không tồn tại / thẻ xe hủy
                    if (ValidateLenhDonForRegistration(ldCheck, plate) is { } invalid
                        && !await ShowRegistrationBlockedAsync(invalid, ldCheck, plate))
                        return;

                    // 4. Lệnh đốn đã đăng tài (check trong DB trips active)
                    var existingTrip = await _presenter.FindActiveTripByLenhDonAsync(ldCheck.SourceKey, FormToken);
                    if (existingTrip is not null)
                    {
                        Helpers.DigiScalePopup.ShowWarning(this,
                            "Lệnh đốn đã đăng tài",
                            $"Lệnh đốn {ldCheck.MaLD} đã được đăng tài.\n\nSố đăng tài: {existingTrip}\n\nMỗi lệnh đốn chỉ được đăng tài 1 lần\n(trừ khi phiếu cũ đã hủy hoặc xóa).");
                        return;
                    }
                    // (Thẻ xe hủy / chưa duyệt đã kiểm tra trong ValidateLenhDonForRegistration ở trên.)
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
            else if (!await ValidateMaterialRegistrationAsync(plate, isTemp))
            {
                // Vật tư (ERP): Shipment/TRIP đã đăng tài hoặc xe chưa cân ra → chặn như mía.
                return;
            }

            var cmd = BuildRegisterCommand(isTemp);

            AppendNotification(isTemp
                ? Loc.T("reg.registering_temp", plate)
                : Loc.T("reg.registering_full", plate, _selectedPlan?.ReferenceNo ?? _txtShipment.Text.Trim()));

            // Mía chính thức: đẩy phiếu đăng tài DigiScale TRƯỚC — DigiScale nhận thì mới lưu.
            var ldForPush = isSugarcane && !isTemp ? CurrentLenhDon(plate) : null;
            var maLenhDonForPush = ldForPush?.MaLD ?? _txtShipment.Text.Trim();

            PublishRegistrationBeforeSave? pushFirst = isSugarcane && !isTemp
                ? async (tripNo, ct) =>
                {
                    AppendNotification($"📤 DigiScale (mía): BU={ldForPush?.BusinessUnit}, LenhDon={ldForPush?.SourceKey}, MaLenhDon={maLenhDonForPush}, SoDangTai={tripNo}");
                    try
                    {
                        var push = await _presenter.PushDangTaiAsync(
                            ldForPush?.SourceKey ?? "", maLenhDonForPush, DateTime.Now, ldForPush?.LoaixeCode ?? "",
                            tripNo, ldForPush?.UuTien, ldForPush?.BusinessUnit, ct);
                        return push.IsSuccess ? null : push.ErrorMessage ?? "—";
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        return ex.Message; // mất kết nối / timeout → coi như DigiScale chưa nhận
                    }
                }
                : null;

            var result = isTemp
                ? await _presenter.RegisterTempAsync(cmd, FormToken)
                : await _presenter.RegisterAsync(cmd, FormToken, pushFirst);

            if (!result.IsSuccess && result.RejectedByUpstream)
            {
                // DigiScale từ chối → không có gì được lưu. Chỉ hiện lý do ngắn gọn (không JSON / HTTP / mã kỹ thuật);
                // nội dung gốc vẫn có trong Nhật ký API.
                var reason = DescribeRegistrationRejection(result.FailureReason, ldForPush, plate);
                AppendNotification(Loc.T("reg.digiscale_rejected_log", result.TripNo ?? "—", reason.Replace(Environment.NewLine, " · ")));
                Helpers.DigiScalePopup.ShowWarning(this, Loc.T("reg.digiscale_rejected_title"), reason);
                return;
            }

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
                if (isSugarcane)
                {
                    // MÍA: DigiScale đã nhận phiếu đăng tài trước khi lưu (xem pushFirst ở trên).
                    digiOk = true;
                    digiStatus = Loc.T("reg.digiscale_accepted");
                }
                else
                {
                    // ═══ VẬT TƯ → chưa có API đăng tài riêng trên DigiScale ═══
                    // Vật tư sẽ push phiếu cân (oracle-tickets) sau khi cân. Đăng tài vật tư chỉ lưu DB local.
                    digiOk = true;
                    digiStatus = "ℹ Vật tư: Đăng tài lưu local. Đẩy DigiScale khi cân.";
                }

                AppendNotification(digiStatus);
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

    /// <summary>
    /// Xóa phiếu đăng tài (mía + vật tư). Chưa cân → được xóa, lệnh đốn / Shipment đăng tài lại được.
    /// Đã cân → không được xóa. UI chặn sớm theo trạng thái; server kiểm tra lại theo lượt cân thực tế.
    /// </summary>
    internal async Task DeleteSelectedTripAsync()
    {
        if (_dgvTrips.CurrentRow?.DataBoundItem is not TripRegistrationRow row)
        {
            MessageBox.Show(Loc.T("reg.delete_no_trip"),
                Loc.T("reg.cancel_no_selection"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Chỉ phiếu chưa cân: đăng tài (tạm / chính thức) hoặc đã hủy. Đã cân lần 1 trở đi → chặn.
        if (row.Lifecycle is not (TripLifecycle.TempRegistered or TripLifecycle.Registered or TripLifecycle.Cancelled))
        {
            MessageBox.Show(Loc.T("reg.delete_weighed", row.TripNo),
                Loc.T("reg.delete_not_allowed_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var confirmUserId = ShowInputDialog(
            Loc.T("reg.delete_confirm_title"),
            Loc.T("reg.delete_confirm_prompt", row.TripNo, row.Plate),
            _presenter.OperatorName);
        if (string.IsNullOrWhiteSpace(confirmUserId))
        {
            AppendNotification(Loc.T("reg.cancel_no_identity"));
            return;
        }

        var reason = ShowInputDialog(Loc.T("reg.delete_reason_title"), Loc.T("reg.delete_reason_prompt"), string.Empty);
        if (string.IsNullOrWhiteSpace(reason))
        {
            AppendNotification(Loc.T("reg.delete_no_reason"));
            return;
        }

        SetUiBusy(true);
        try
        {
            var result = await _presenter.DeleteAsync(new DeleteTripCommand(
                TripId: row.TripId,
                ExpectedVersion: row.Version,
                Reason: reason,
                ConfirmUserId: confirmUserId,
                CorrelationId: Guid.NewGuid().ToString("N")), FormToken);

            if (!result.IsSuccess)
            {
                AppendNotification(Loc.T("reg.delete_failed", result.FailureReason ?? "—"));
                MessageBox.Show(Loc.T("reg.delete_failed", result.FailureReason ?? "—"),
                    Loc.T("reg.delete_not_allowed_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            AppendNotification(Loc.T("reg.delete_ok", row.TripNo));
            ResetForm();
            await LoadGridAsync();
            MessageBox.Show(Loc.T("reg.delete_ok", row.TripNo),
                Loc.T("reg.delete_ok_title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            // Form đang đóng.
        }
        catch (Exception ex)
        {
            AppendNotification(Loc.T("reg.delete_failed", ex.Message));
            MessageBox.Show(Loc.T("reg.delete_failed", ex.Message),
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
        var ld = isSugarcane ? CurrentLenhDon(PlateText) : null;

        // Vật tư: lấy từ panel vật tư, Mía: lấy từ panel mía + LenhDon
        var ownerOrCustomer = isSugarcane
            ? _txtCustomerOrSupplier.Text.Trim().NullIfEmpty() ?? ld?.KHName ?? _selectedPlan?.OwnerOrCustomer
            : _txtMaterialCustomer.Text.Trim().NullIfEmpty();

        // Mía: hàng hóa = Mía cháy / Mía khúc / Mía cây theo lệnh đốn (cháy, thu hoạch máy/thủ công).
        var commodityType = isSugarcane
            ? SugarcaneCommodity.Label(ld) ?? _selectedPlan?.CommodityType ?? ld?.LoaiMiaName
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
            TrailerPlate: null, // Bỏ rơ moóc: ERP không có, luồng vật tư không nhập tay
            DriverRef: driverRef,
            VehicleType: GetSelectedVehicleType(),
            SugarcaneType: isSugarcane ? (_txtSugarcaneType.Text.Trim().NullIfEmpty() ?? ld?.LoaiMiaName) : null,
            SugarcaneVariety: isSugarcane ? (_txtSugarcaneVariety.Text.Trim().NullIfEmpty() ?? ld?.GiongMiaName) : null,
            SugarcaneOwner: isSugarcane ? (_txtSugarcaneOwner.Text.Trim().NullIfEmpty() ?? ld?.KHName) : ownerOrCustomer,
            HarvestMethod: isSugarcane ? (_txtHarvestMethod.Text.Trim().NullIfEmpty() ?? (ld is null ? null : HarvestMethodText(ld))) : null,
            Station: isSugarcane ? (_txtStation.Text.Trim().NullIfEmpty() ?? ld?.TramName) : null,
            CropSeason: isSugarcane ? (_txtCropSeason.Text.Trim().NullIfEmpty() ?? ld?.VudautuName) : null,
            FrmLenhDonId: ld?.SourceKey,
            FrmMaLenhDon: ld?.MaLD,
            FrmLenhDonBu: ld?.BusinessUnit,
            FrmLoaiXeCode: ld?.LoaixeCode,
            // Vật tư: nhà vận chuyển kéo từ ERP (người dùng có thể sửa) — lưu trên chuyến để đẩy DigiScale khi cân xong.
            Transporter: isSugarcane ? null : _txtTransporter.Text.Trim().NullIfEmpty(),
            WarehouseName: isSugarcane ? null : _txtWarehouse.Text.Trim().NullIfEmpty(),
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

        // Mía — push phiếu đăng tài FRM. Ưu tiên đúng lệnh đốn của phiếu (ô Lệnh đốn), không đoán theo biển số.
        var ld = _presenter.FindLenhDonByCode(_txtShipment.Text) ?? CurrentLenhDon(plate);
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
        var isMaterial = _cboProfileType.SelectedIndex != 0;

        // Lấy dữ liệu từ LenhDon khi form fields rỗng (do ẩn). Vật tư không dùng lệnh đốn FRM.
        var ld = isMaterial ? null : _presenter.FindLenhDonByCode(_txtShipment.Text) ?? CurrentLenhDon(PlateText);

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
            // Vật tư: nhà vận chuyển / lái xe lấy nguyên từ ERP (ô vật tư); mía: theo thẻ xe / lệnh đốn FRM.
            Transporter: isMaterial ? _txtTransporter.Text.Trim().NullIfEmpty() : transporter,
            DriverName: isMaterial ? _txtDriver.Text.Trim().NullIfEmpty() : null,
            DriverIdCard: null,
            ShipmentNo: _txtShipment.Text.Trim().NullIfEmpty() ?? ld?.MaLD,
            Warehouse: _txtWarehouse.Text.Trim().NullIfEmpty(),
            Commodity: _txtCommodity.Text.Trim().NullIfEmpty() ?? ld?.LoaiMiaName,
            CustomerOrSupplier: isMaterial
                ? _txtMaterialCustomer.Text.Trim().NullIfEmpty()
                : _txtCustomerOrSupplier.Text.Trim().NullIfEmpty() ?? ld?.KHName,
            // Loại mía trên phiếu = Mía cháy / Mía khúc / Mía cây theo lệnh đốn (song ngữ theo BU).
            SugarcaneType: Printing.PrintText.CaneForm(ld)
                           ?? _txtSugarcaneType.Text.Trim().NullIfEmpty() ?? ld?.LoaiMiaName,
            SugarcaneOwner: _txtSugarcaneOwner.Text.Trim().NullIfEmpty() ?? ld?.KHName,
            SugarcaneVariety: _txtSugarcaneVariety.Text.Trim().NullIfEmpty() ?? ld?.GiongMiaName,
            HarvestMethod: Printing.PrintText.HarvestMethod(ld?.ThuHoachMay) ?? _txtHarvestMethod.Text.Trim().NullIfEmpty(),
            Station: _txtStation.Text.Trim().NullIfEmpty() ?? ld?.TramName,
            CropSeason: _txtCropSeason.Text.Trim().NullIfEmpty() ?? ld?.VudautuName,
            QrToken: _lastQrToken,
            QueueSequenceNumber: _lastQueueSequenceNumber,
            // Nhà máy theo lệnh đốn FRM (TenNhaMay), không có thì theo đơn vị của tài khoản đăng nhập.
            Factory: ld?.TenNhaMay ?? _presenter.SiteName,
            FactoryAddress: _presenter.SiteAddress,
            VehicleType: _txtVehicleType.Text.Trim().NullIfEmpty() ?? ld?.LoaixeName,
            IsMaterial: isMaterial,
            HarvestPartner: ld?.HarvestPartner ?? transporter,
            LoadingPartner: ld?.DoiTacBocXepName,
            HopDong: ld?.HopDongDauTuMiaName,
            Notes: (isMaterial ? _txtMaterialNotes.Text : _txtNotes.Text).Trim().NullIfEmpty());
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

    /// <summary>Xuất toàn bộ danh sách chuyến đang lọc (mọi trang) ra Excel.</summary>
    private async Task ExportTripGridAsync()
    {
        var tab = _activeTab switch
        {
            TripGridTab.Exited => _btnTabExited.Text,
            TripGridTab.Cancelled => _btnTabCancelled.Text,
            _ => _btnTabInFactory.Text
        };

        try
        {
            await ExportHelper.ExportGridAsync(
                this, _dgvTrips, _allTripRows,
                new ExportHelper.ExportRequest(
                    $"{Loc.T("reg.section_grid")} - {_gridCategory.CategoryLabel}",
                    $"dang-tai_{_gridCategory.FileSuffix}",
                    [
                        Loc.T("export.filter_category", _gridCategory.CategoryLabel),
                        Loc.T("export.filter_tab", tab),
                        Loc.T("export.filter_range", _dtpFrom.Value.ToString("dd/MM/yyyy"), _dtpTo.Value.ToString("dd/MM/yyyy"))
                    ],
                    _gridCategory.CategoryLabel),
                busyControl: _btnExportExcel);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"{Loc.T("export.error")}{Environment.NewLine}{ex.Message}",
                Loc.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Chuyển tab lọc, cập nhật giao diện tab và tải lại dữ liệu lưới.</summary>
    private async Task SetActiveTabAsync(TripGridTab tab)
    {
        _activeTab = tab;
        _pager.Reset();
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
                ToUtc = ToUtcStartOfDay(_dtpTo.Value),
                Category = _gridCategory.Category
            };

            _allTripRows = await _presenter.LoadGridAsync(filter, _cts.Token);
            BindCurrentPage();
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
    /// Bind trang hiện tại của lưới. Giữ vị trí cuộn và dòng đang chọn khi timer nạp lại,
    /// để người vận hành không bị nhảy về đầu bảng giữa chừng.
    /// </summary>
    private void BindCurrentPage()
    {
        var firstRow = _dgvTrips.FirstDisplayedScrollingRowIndex;
        var selectedId = _lastRegisteredTripId;

        // Chế độ thường: toàn bộ danh sách, cuộn dọc. Phóng to lưới: phân trang.
        var page = _pager.Page(_allTripRows);
        GridBuilder.BindAndColorize(_dgvTrips, page, r => r.Lifecycle.ToString());

        if (selectedId is { } id)
        {
            _dgvTrips.ClearSelection();
            foreach (DataGridViewRow gridRow in _dgvTrips.Rows)
            {
                if (gridRow.DataBoundItem is TripRegistrationRow r && r.TripId == id)
                {
                    gridRow.Selected = true;
                    break;
                }
            }
        }

        if (firstRow > 0 && firstRow < _dgvTrips.Rows.Count)
        {
            try { _dgvTrips.FirstDisplayedScrollingRowIndex = firstRow; }
            catch (InvalidOperationException) { /* lưới chưa layout xong */ }
        }

        _gridScroll?.Sync();
    }

    /// <summary>
    /// Phóng to lưới: ẩn vùng nhập liệu + camera + thanh nút để lưới chiếm toàn màn hình.
    /// Bấm lại để thu về.
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

    /// <summary>
    /// Bấm dòng lưới → CHỈ XEM thông tin chuyến đã đăng tài. Không chạy kiểm tra đăng tài
    /// (lệnh đốn hết hạn, xe đã đăng tài, thẻ xe...) và không bật popup cảnh báo.
    /// </summary>
    private async void PopulateFromGridRow(TripRegistrationRow row)
    {
        _txtTripNo.Text = row.TripNo;
        // Gán im lặng để không kích hoạt tra cứu biển số / lệnh đốn (kèm kiểm tra đăng tài).
        SetTextSilently(_txtShipment, row.ShipmentNo ?? string.Empty, OnLenhDonTextChanged);
        SetTextSilently(_txtPlate, row.Plate ?? string.Empty, OnPlateTextChanged);
        _txtDateTime.Text = row.EntryTime?.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") ?? string.Empty;
        _lastRegisteredTripId = row.TripId;
        _txtVehicleType.Text = row.VehicleType ?? string.Empty;

        // Điền lại thông tin đầy đủ: ưu tiên đúng lệnh đốn của phiếu, sau đó mới theo biển số
        _selectedLenhDon = null;
        _lastLookedUpPlate = null; // Reset để cho phép lookup lại
        _lastLookedUpLenhDon = null;
        var rowLenhDon = IsSugarcaneMode && !string.IsNullOrWhiteSpace(row.ShipmentNo)
            ? _presenter.FindLenhDonByCode(row.ShipmentNo)
            : null;

        rowLenhDon ??= IsSugarcaneMode && !string.IsNullOrWhiteSpace(row.Plate)
            ? _presenter.FindLenhDonByPlate(row.Plate)
            : null;

        if (rowLenhDon is not null)
            await ApplyLenhDonAsync(rowLenhDon, row.Plate, viewOnly: true);

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

    /// <summary>
    /// Reset form về trạng thái trống, chuẩn bị đăng tài mới — GIỮ NGUYÊN nghiệp vụ đang chọn.
    /// Đang ở Vật tư thì làm mới phần Vật tư và ở lại Vật tư; đang ở Mía thì làm mới phần Mía.
    /// (Trước đây luôn ép về Mía và không xoá các ô vật tư.)
    /// </summary>
    public void ResetForm()
    {
        // ── Phần vật tư (ERP) ──
        foreach (var txt in new[]
        {
            _txtTripNumber, _txtMaterialPlate, _txtMaterialShipment, _txtMaterialPlateDetail,
            _txtTransporter, _txtDriver, _txtMaterialCustomer, _txtReceiveDate,
            _txtMaterialNotes
        })
        {
            txt.Clear();
        }

        // ── Phần mía (FRM) + trường dùng chung ──
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

        // Lần đầu mở form chưa chọn nghiệp vụ → mặc định Mía. Các lần sau giữ nguyên lựa chọn.
        if (_cboProfileType.SelectedIndex < 0)
            _cboProfileType.SelectedIndex = 0;

        _selectedPlan = null;
        _lastErpOrderLine = null;
        _tempQrChosen = false;
        _lastRegisteredTripId = null;
        _lastQrToken = null;
        _lastQueueSequenceNumber = null;
        _lastLookedUpPlate = null;
        _selectedLenhDon = null;
        _lastLookedUpLenhDon = null;
        _lstLenhDonPopup.Visible = false;

        _activeTab = TripGridTab.InFactory;
        ApplyTabStyles();

        // Con trỏ về ô nhập đầu tiên của đúng nghiệp vụ đang làm.
        var isMaterial = _cboProfileType.SelectedIndex == 1;
        if (isMaterial) _txtTripNumber.Focus();
        else _txtPlate.Focus();
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
                var ld = CurrentLenhDon(plate);
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
        // Tác vụ nền (tải danh sách xe/lệnh đốn, đẩy DigiScale...) có thể xong SAU khi tab đã đóng —
        // khi đó khung thông báo đã bị dispose, bỏ qua thay vì ném lỗi.
        if (IsDisposed || Disposing || _rtbNotifications.IsDisposed) return;

        if (_rtbNotifications.InvokeRequired)
        {
            try { _rtbNotifications.BeginInvoke(() => AppendNotification(message)); }
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException) { }
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
