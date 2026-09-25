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

    // ── Trường FRM (read-only — tự điền từ API) ──
    private readonly TextBox _txtCustomerOrSupplier = new() { ReadOnly = true, AccessibleName = "Đối tác vận chuyển" };
    private readonly TextBox _txtStation = new() { ReadOnly = true, AccessibleName = "Trạm" };
    private readonly TextBox _txtCropSeason = new() { ReadOnly = true, AccessibleName = "Vụ đầu tư" };
    private readonly TextBox _txtHarvestMethod = new() { ReadOnly = true, AccessibleName = "Hình thức thu hoạch" };
    private readonly TextBox _txtSugarcaneOwner = new() { ReadOnly = true, AccessibleName = "Chủ mía" };
    private readonly TextBox _txtSugarcaneType = new() { ReadOnly = true, AccessibleName = "Loại mía" };
    private readonly TextBox _txtSugarcaneVariety = new() { ReadOnly = true, AccessibleName = "Giống mía" };
    private readonly TextBox _txtVehicleType = new() { ReadOnly = true, AccessibleName = "Loại xe" };
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
    private readonly TextBox _txtCommodity = new() { ReadOnly = true, AccessibleName = Loc.T("field.commodity") };
    private readonly TextBox _txtWarehouse = new() { ReadOnly = true, AccessibleName = "Kho" };

    // ── Panel chứa các trường mía (để ẩn/hiện) ──
    private Panel _pnlSugarcaneFields = null!;
    // ── Panel chứa các trường vật tư (để ẩn/hiện) ──
    private Panel _pnlMaterialFields = null!;

    // ── Action buttons ──
    private readonly Button _btnSave = GridBuilder.PrimaryButton(Loc.T("reg.btn_register"), new Size(140, 36));
    private readonly Button _btnPrint = GridBuilder.SecondaryButton(Loc.T("reg.btn_print"), new Size(130, 36));
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
        Format = DateTimePickerFormat.Short,
        Width = 130,
        AccessibleName = Loc.T("reg.filter_from")
    };
    private readonly DateTimePicker _dtpTo = new()
    {
        Format = DateTimePickerFormat.Short,
        Width = 130,
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
    private bool _tempQrChosen; // true khi user đã chọn phát QR tạm qua Kiểm tra
    private bool _isBusy;
    private Guid? _lastRegisteredTripId;
    private string? _lastQrToken;
    private int? _lastQueueSequenceNumber;
    private IReadOnlyList<WorkflowProfile>? _availableProfiles;

    /// <summary>Serialize DB operations trong cùng form — tránh EF Core "second operation" error.</summary>
    private readonly SemaphoreSlim _dbLock = new(1, 1);

    public TripRegistrationForm(TripRegistrationPresenter presenter, TripEventBus eventBus)
    {
        _presenter = presenter;
        _eventBus = eventBus;

        Text = Loc.T("reg.title");
        MinimumSize = new Size(1100, 760);
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
        // Root: 4 rows — top area | action bar | grid | status bar
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = AgrisTheme.Canvas,
            Padding = new Padding(10, 8, 10, 0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 70));   // Top area (form + camera)
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, FormLayout.ActionRowHeight));   // Action bar
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 30));   // Grid area
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));   // Status bar

        root.Controls.Add(BuildTopArea(), 0, 0);
        root.Controls.Add(BuildActionBar(), 0, 1);
        root.Controls.Add(BuildGridArea(), 0, 2);
        root.Controls.Add(BuildStatusBar(), 0, 3);

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

    /// <summary>Form đăng tài — tất cả các trường theo Req 6.1.</summary>
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

        // Chọn loại nghiệp vụ (Mía nhập / Vật tư)
        _cboProfileType.Items.AddRange(new object[] { Loc.T("profile.sugarcane"), Loc.T("profile.material") });
        _cboProfileType.SelectedIndex = 0;
        _cboProfileType.SelectedIndexChanged += (_, _) => OnProfileTypeChanged();
        FormLayout.AddField(table, Loc.T("field.profile_type"), _cboProfileType);

        // Khung ngày lọc xe FRM (Từ ngày — Đến ngày)
        var dateRangePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = false,
            Margin = Padding.Empty
        };
        _dtpFrmFrom.Format = DateTimePickerFormat.Short;
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

        _dtpFrmTo.Format = DateTimePickerFormat.Short;
        _dtpFrmTo.Width = 130;
        _dtpFrmTo.Value = DateTime.Today;
        _dtpFrmTo.ValueChanged += (_, _) => RefreshPlateFilter();

        dateRangePanel.Controls.Add(_dtpFrmFrom);
        dateRangePanel.Controls.Add(lblTo);
        dateRangePanel.Controls.Add(_dtpFrmTo);
        FormLayout.AddField(table, Loc.T("reg.frm_date_range"), dateRangePanel);

        // Biển số + Lệnh đốn (cùng 1 hàng — nhập 1 trong 2 để tra cứu)
        var plateShipmentPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        plateShipmentPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); // plate textbox
        plateShipmentPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30)); // dropdown btn
        plateShipmentPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 12)); // spacer
        plateShipmentPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90)); // shipment label
        plateShipmentPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); // shipment textbox
        plateShipmentPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _txtPlate.Font = AgrisTheme.Body(10F);
        _txtPlate.Dock = DockStyle.Fill;
        _txtPlate.PlaceholderText = Loc.T("field.plate");

        _btnPlateDropdown.Dock = DockStyle.Fill;
        _btnPlateDropdown.FlatStyle = FlatStyle.Flat;
        _btnPlateDropdown.FlatAppearance.BorderSize = 1;
        _btnPlateDropdown.Cursor = Cursors.Hand;
        _btnPlateDropdown.TabStop = false;

        var lblShipmentInline = new Label
        {
            Text = Loc.T("reg.field_shipment_short"),
            Dock = DockStyle.Fill,
            TextAlign = System.Drawing.ContentAlignment.MiddleRight,
            Font = AgrisTheme.Body(9F),
            ForeColor = AgrisTheme.TextMuted,
            Margin = new Padding(4, 0, 4, 0)
        };

        _txtShipment.Font = AgrisTheme.Body(10F);
        _txtShipment.Dock = DockStyle.Fill;
        _txtShipment.PlaceholderText = Loc.T("reg.field_shipment");

        plateShipmentPanel.Controls.Add(_txtPlate, 0, 0);
        plateShipmentPanel.Controls.Add(_btnPlateDropdown, 1, 0);
        plateShipmentPanel.Controls.Add(new Panel { Dock = DockStyle.Fill }, 2, 0); // spacer
        plateShipmentPanel.Controls.Add(lblShipmentInline, 3, 0);
        plateShipmentPanel.Controls.Add(_txtShipment, 4, 0);

        FormLayout.AddField(table, Loc.T("field.plate"), plateShipmentPanel);

        // Số đăng tài (auto, read-only — tự sinh khi lưu)
        _txtTripNo.ReadOnly = true;
        _txtTripNo.BackColor = AgrisTheme.ReadOnlyBg;
        _txtTripNo.ForeColor = AgrisTheme.ReadOnlyText;
        _txtTripNo.BorderStyle = BorderStyle.FixedSingle;
        _txtTripNo.TabStop = false;
        _txtTripNo.Cursor = Cursors.Arrow;
        _txtTripNo.PlaceholderText = Loc.T("reg.auto_generate");
        _txtTripNo.Enter += (_, _) => _txtTripNo.Parent?.SelectNextControl(_txtTripNo, true, true, true, true);
        FormLayout.AddField(table, Loc.T("field.trip_no"), _txtTripNo);

        // Ngày giờ (auto, read-only — tự ghi khi lưu)
        _txtDateTime.ReadOnly = true;
        _txtDateTime.BackColor = AgrisTheme.ReadOnlyBg;
        _txtDateTime.ForeColor = AgrisTheme.ReadOnlyText;
        _txtDateTime.BorderStyle = BorderStyle.FixedSingle;
        _txtDateTime.TabStop = false;
        _txtDateTime.Cursor = Cursors.Arrow;
        _txtDateTime.PlaceholderText = Loc.T("reg.auto_datetime");
        _txtDateTime.Enter += (_, _) => _txtDateTime.Parent?.SelectNextControl(_txtDateTime, true, true, true, true);
        FormLayout.AddField(table, Loc.T("field.date_time"), _txtDateTime);

        // Loại xe (read-only — tự điền từ FRM)
        _txtVehicleType.BackColor = AgrisTheme.ReadOnlyBg;
        FormLayout.AddField(table, "Loại xe", _txtVehicleType);

        // ── Trường FRM (read-only — tự điền khi chọn biển số) ──
        _txtCustomerOrSupplier.BackColor = AgrisTheme.ReadOnlyBg;
        FormLayout.AddField(table, "Đối tác vận chuyển", _txtCustomerOrSupplier);

        _txtMaHopDong.BackColor = AgrisTheme.ReadOnlyBg;
        FormLayout.AddField(table, "Mã hợp đồng VC", _txtMaHopDong);

        _txtStation.BackColor = AgrisTheme.ReadOnlyBg;
        FormLayout.AddField(table, "Trạm", _txtStation);

        _txtCropSeason.BackColor = AgrisTheme.ReadOnlyBg;
        FormLayout.AddField(table, "Vụ đầu tư", _txtCropSeason);

        _txtHarvestMethod.BackColor = AgrisTheme.ReadOnlyBg;
        FormLayout.AddField(table, "Hình thức thu hoạch", _txtHarvestMethod);

        _txtMaThe.BackColor = AgrisTheme.ReadOnlyBg;
        FormLayout.AddField(table, "Mã thẻ xe", _txtMaThe);

        _txtTaiTrong.BackColor = AgrisTheme.ReadOnlyBg;
        FormLayout.AddField(table, "Tải trọng (T)", _txtTaiTrong);

        // ── Panel trường mía: chờ LenhDon mirror → hiện tại trống ──
        // Chủ mía, Loại mía, Giống mía nằm trong LenhDon/ThuaDat — API chưa mirror.
        // Khi API bổ sung sẽ bật lại.
        _pnlSugarcaneFields = new Panel { Dock = DockStyle.Fill, Visible = false };

        // ── Panel trường dành riêng cho VẬT TƯ (ẩn khi chọn mía) ──
        _pnlMaterialFields = new Panel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowOnly,
            Visible = false
        };
        var materialTable = FormLayout.FieldTable();
        FormLayout.AddField(materialTable, Loc.T("field.commodity"), _txtCommodity);
        FormLayout.AddField(materialTable, Loc.T("field.warehouse"), _txtWarehouse);
        materialTable.Dock = DockStyle.Fill;
        _pnlMaterialFields.Controls.Add(materialTable);
        FormLayout.AddFullWidth(table, _pnlMaterialFields, FormLayout.FieldPanelHeight(2));

        // Ghi chú
        FormLayout.AddField(table, Loc.T("field.notes"), _txtNotes);

        // Thông báo (nhúng trong form card thay vì panel riêng)
        _rtbNotifications.ReadOnly = true;
        _rtbNotifications.BackColor = AgrisTheme.SurfaceMuted;
        _rtbNotifications.BorderStyle = BorderStyle.None;
        _rtbNotifications.Font = AgrisTheme.Mono(8.5F);
        _rtbNotifications.ForeColor = AgrisTheme.TextPrimary;
        _rtbNotifications.ScrollBars = RichTextBoxScrollBars.Vertical;
        _rtbNotifications.Dock = DockStyle.Fill;
        _rtbNotifications.Height = 80;
        _rtbNotifications.AccessibleName = Loc.T("reg.section_notification");
        FormLayout.AddFullWidth(table, _rtbNotifications, 90);

        card.Controls.Add(table);
        card.Controls.Add(header);

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

        // Cập nhật label Shipment
        // Mía: "Lệnh đốn / Shipment", Vật tư: "PO / Shipment"
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
                AppendNotification(_presenter.FrmCacheLoading
                    ? "⏳ Đang tải dữ liệu xe từ FRM..."
                    : "⚠ Chưa có dữ liệu xe FRM. Gõ biển số thủ công.");
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
    private void RefreshPlateFilter()
    {
        var from = _dtpFrmFrom.Value.Date;
        var to = _dtpFrmTo.Value.Date.AddDays(1).AddSeconds(-1);

        _filteredPlates = _presenter.GetPlatesInDateRange(from, to);
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

        var bar = FormLayout.ActionBar(_btnSave, _btnPrint, _btnReset);
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
            GridBuilder.Column(Loc.T("col.queue_seq"), nameof(TripRegistrationRow.QueueSequenceNumber), 70),
            GridBuilder.Column(Loc.T("col.entry_time"), nameof(TripRegistrationRow.EntryTime), 120),
            GridBuilder.Column(Loc.T("col.exit_time"), nameof(TripRegistrationRow.ExitTime), 120),
            GridBuilder.Column(Loc.T("col.transporter"), nameof(TripRegistrationRow.Transporter), 100),
            GridBuilder.Column(Loc.T("col.customer"), nameof(TripRegistrationRow.CustomerOrSupplier), 110),
            GridBuilder.Column(Loc.T("col.commodity"), nameof(TripRegistrationRow.Commodity), 90),
            GridBuilder.Column(Loc.T("col.vehicle_type"), nameof(TripRegistrationRow.VehicleType), 80));

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
        VisibleChanged += (_, _) =>
        {
            if (Visible) _gridRefreshTimer.Start();
            else _gridRefreshTimer.Stop();
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
        _btnReset.Click += OnResetClicked;
    }

    /// <summary>
    /// Bấm nút ▼ → hiện toàn bộ danh sách biển số xe trong khung ngày (không cần gõ).
    /// </summary>
    private void OnPlateDropdownClicked(object? sender, EventArgs e)
    {
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
        var text = _txtPlate.Text.Trim();

        if (text.Length == 0 || _filteredPlates.Length == 0)
        {
            _lstPlatePopup.Visible = false;
            return;
        }

        var search = text.Replace(" ", "").Replace("-", "").Replace(".", "").ToUpperInvariant();

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

    /// <summary>Mũi tên xuống → focus vào ListBox popup. Enter → chọn item đầu tiên.</summary>
    private void OnPlateKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_lstPlatePopup.Visible) return;

        if (e.KeyCode == Keys.Down)
        {
            _lstPlatePopup.Focus();
            if (_lstPlatePopup.Items.Count > 0)
                _lstPlatePopup.SelectedIndex = 0;
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Enter && _lstPlatePopup.Items.Count > 0)
        {
            SelectPlateFromPopup(_lstPlatePopup.Items[0]?.ToString());
            e.Handled = true;
            e.SuppressKeyPress = true;
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
        _txtPlate.Text = plate;
        _txtPlate.SelectionStart = plate.Length;
        _txtPlate.Focus();

        await LookupAndPopulateAsync(plate);
    }

    /// <summary>
    /// Khi user rời ô biển số (gõ tay xong tab ra) → ẩn popup và tra cứu.
    /// </summary>
    private async void OnPlateLeave(object? sender, EventArgs e)
    {
        await Task.Delay(200);

        _lstPlatePopup.Visible = false;

        var plate = PlateText;
        if (string.IsNullOrWhiteSpace(plate) || plate.Length < 5) return; // Biển số quá ngắn — chưa đủ để lookup

        if (_selectedPlan?.VehiclePlate is not null
            && string.Equals(NormalizePlate(_selectedPlan.VehiclePlate), NormalizePlate(plate), StringComparison.Ordinal))
            return;

        await LookupAndPopulateAsync(plate);
    }

    /// <summary>
    /// Tra cứu biển số qua Presenter (cache) và tự điền thông tin.
    /// Thay thế OnCheckPlateClicked cũ.
    /// </summary>
    private async Task LookupAndPopulateAsync(string plate)
    {
        SetUiBusy(true);
        try
        {
            AppendNotification(Loc.T("reg.checking_plate", plate));

            var result = await _presenter.CheckPlanAsync(plate, FormToken);

            if (!result.IsSuccess)
            {
                AppendNotification(Loc.T("reg.check_frm_error", result.ErrorMessage));
                return;
            }

            if (result.Plans.Count == 0)
            {
                AppendNotification(Loc.T("reg.no_plan_found", plate));
                _selectedPlan = null;
                _tempQrChosen = true;
                AppendNotification(Loc.T("reg.temp_qr_chosen"));
                return;
            }

            if (result.Plans.Count == 1)
            {
                PopulateFromPlan(result.Plans[0]);
            }
            else
            {
                // Nhiều thẻ xe cho cùng biển số → tự chọn cái có nhiều data nhất
                // Ưu tiên: có thẻ + có HDVC > có thẻ > chỉ xe-van-chuyen
                var best = result.Plans
                    .OrderByDescending(p => p.CardCode is not null ? 2 : 0)
                    .ThenByDescending(p => p.OwnerOrCustomer is not null ? 1 : 0)
                    .First();

                if (result.Plans.Count > 1)
                    AppendNotification($"ℹ {result.Plans.Count} kết quả cho {plate} — chọn bản có nhiều data nhất ({best.CardCode ?? best.ReferenceNo})");

                PopulateFromPlan(best);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppendNotification(Loc.T("reg.check_plate_error", ex.Message));
        }
        finally
        {
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

            var isTemp = _selectedPlan is null;
            var cmd = BuildRegisterCommand(isTemp);

            AppendNotification(isTemp
                ? Loc.T("reg.registering_temp", plate)
                : Loc.T("reg.registering_full", plate, _selectedPlan!.ReferenceNo));

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

        // QR tạm mà không tìm được profile theo prefix → lấy profile đầu tiên có sẵn
        if (profile is null && _availableProfiles is { Count: > 0 })
            profile = _availableProfiles[0];

        return new RegisterTripCommand(
            SiteId: _presenter.SiteId,
            GateId: Guid.Empty, // GateId chưa có cấu hình cụ thể — dùng Empty tạm
            WorkflowProfileId: profile?.Id ?? Guid.Empty,
            WorkflowProfileVersion: profile?.ProfileVersion ?? 0,
            ReferenceTripNo: _selectedPlan?.ReferenceNo,
            PlanSource: _selectedPlan?.Source,
            CommodityType: _selectedPlan?.CommodityType,
            OwnerOrCustomer: _selectedPlan?.OwnerOrCustomer,
            WarehouseCode: _selectedPlan?.WarehouseCode,
            TractorPlate: PlateText,
            TrailerPlate: null,
            DriverRef: null,
            VehicleType: GetSelectedVehicleType(),
            SugarcaneType: _txtSugarcaneType.Text.Trim().NullIfEmpty(),
            SugarcaneVariety: _txtSugarcaneVariety.Text.Trim().NullIfEmpty(),
            SugarcaneOwner: _txtSugarcaneOwner.Text.Trim().NullIfEmpty(),
            HarvestMethod: _txtHarvestMethod.Text.Trim().NullIfEmpty(),
            Station: _txtStation.Text.Trim().NullIfEmpty(),
            CropSeason: _txtCropSeason.Text.Trim().NullIfEmpty(),
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

    /// <summary>
    /// Tổng hợp dữ liệu từ form fields + result để hiển thị trên phiếu đăng tài.
    /// </summary>
    private TripReceiptData BuildReceiptData(Guid tripId)
    {
        return new TripReceiptData(
            TripId: tripId,
            TripNo: _txtTripNo.Text.Trim().NullIfEmpty(),
            DateTime: _txtDateTime.Text.Trim().NullIfEmpty(),
            Gate: null,
            Plate: PlateText.NullIfEmpty(),
            TrailerPlate: null,
            Transporter: null,
            DriverName: null,
            DriverIdCard: null,
            ShipmentNo: _txtShipment.Text.Trim().NullIfEmpty(),
            Warehouse: _txtWarehouse.Text.Trim().NullIfEmpty(),
            Commodity: _txtCommodity.Text.Trim().NullIfEmpty(),
            CustomerOrSupplier: _txtCustomerOrSupplier.Text.Trim().NullIfEmpty(),
            SugarcaneType: _txtSugarcaneType.Text.Trim().NullIfEmpty(),
            SugarcaneOwner: _txtSugarcaneOwner.Text.Trim().NullIfEmpty(),
            SugarcaneVariety: _txtSugarcaneVariety.Text.Trim().NullIfEmpty(),
            HarvestMethod: _txtHarvestMethod.Text.Trim().NullIfEmpty(),
            Station: _txtStation.Text.Trim().NullIfEmpty(),
            CropSeason: _txtCropSeason.Text.Trim().NullIfEmpty(),
            QrToken: _lastQrToken,
            QueueSequenceNumber: _lastQueueSequenceNumber);
    }

    // ═══════════════════════════════════════════════════════════
    //  Input dialog helper
    // ═══════════════════════════════════════════════════════════

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
            await LookupAndPopulateAsync(row.Plate);

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
        _tempQrChosen = false;
        _lastRegisteredTripId = null;
        _lastQrToken = null;
        _lastQueueSequenceNumber = null;

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
        if (string.IsNullOrWhiteSpace(text)) return null;
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

    /// <summary>Biển số đã nhập/chọn.</summary>
    internal string PlateText => _txtPlate.Text.Trim();

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
