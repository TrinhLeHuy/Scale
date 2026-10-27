using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.Queries;
using Microsoft.Extensions.Configuration;
using TTC.Weighing.Application.UseCases.Quality;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Domain.Rules;
using TTC.Weighing.WinForms.Controls;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Presenters;
using TTC.Weighing.WinForms.Services;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Forms;

/// <summary>
/// UI-05 — Mía và chất lượng. Cân tạp chất tự động và tiếp nhận chữ đường.
/// Thiếu tạp chất CHẶN cân lần 2; thiếu chữ đường chỉ CẢNH BÁO (R-013, R-014).
///
/// Luồng: Quét QR → hiển thị thông tin chuyến → auto-weigh mẫu trước →
///         auto-weigh mẫu sau → tính tạp chất → ghi kết quả.
/// Form không tự tính toán nghiệp vụ — server quyết định.
/// </summary>
public sealed class QualityForm : Form
{
    /// <summary>Thời gian ổn định tối thiểu (giây) để tự động ghi kết quả cân mẫu.</summary>
    private const int StableThresholdSeconds = 3;

    /// <summary>Khoảng cách giữa các lần poll cân mẫu (ms).</summary>
    private static readonly TimeSpan ScalePollInterval = TimeSpan.FromMilliseconds(750);

    private readonly QualityPresenter _presenter;
    private readonly ICurrentUser _currentUser;
    private readonly ITripDetailQueries _tripDetail;
    private readonly ITripRegistrationQueries _tripRegistrationQueries;
    private readonly Guid _siteId;
    private readonly Guid _sampleScaleId;
    private readonly CancellationTokenSource _cts = new();

    // ── Trạng thái workflow ──
    private Guid? _currentTripId;
    private string? _currentFrmLenhDonId;
    private string? _currentFrmMaLenhDon;
    private string? _currentFrmLenhDonBu;
    private ImpurityWorkflowStep _workflowStep = ImpurityWorkflowStep.WaitingForQr;
    private decimal? _sampleBeforeKg;
    private decimal? _sampleAfterKg;

    // ── Đo tạp chất: tối đa 3 lần cân = lần 1 (mẫu trước) + lần 2 (mẫu sau, < lần 1) + 1 lần điều chỉnh ──
    private int _weighingCount;           // số lần cân đã xác nhận (0..3)
    private int? _adjustTarget;           // đang cân lại lần 1 hoặc lần 2
    private int? _adjustedWeighing;       // đã điều chỉnh lần nào (1/2) — chỉ 1 lần
    private decimal? _adjustOldKg;        // số cũ trước khi điều chỉnh (ghi vào lý do)
    private bool _usedManual;             // có lần cân nhập tay → nguồn MANUAL
    private ApprovalResult? _manualApproval; // Trưởng đơn vị đã duyệt nhập tay cho chuyến này
    private decimal? _pendingManualKg;    // số nhập tay khi không có cân giả lập (thiết bị thật)

    private readonly Button _btnConfirmWeighing =
        GridBuilder.PrimaryButton(Loc.T("quality.btn_confirm_weighing", 1), new Size(200, 34));
    private readonly Button _btnAdjust =
        GridBuilder.SecondaryButton(Loc.T("quality.btn_adjust"), new Size(170, 34));

    // ── Hai ô lần cân (bấm chọn lần cân; đã cân thì bấm để điều chỉnh = lần cân thứ 3) ──
    private readonly WeighingSlotCard _slot1 = new(1, Loc.T("quality.slot1_title"), Loc.T("quality.slot1_sub"));
    private readonly WeighingSlotCard _slot2 = new(2, Loc.T("quality.slot2_title"), Loc.T("quality.slot2_sub"));
    private readonly CheckBox _chkAutoCapture = new()
    {
        Text = Loc.T("quality.auto_capture"),
        Checked = true,
        AutoSize = true,
        Cursor = Cursors.Hand,
        Margin = new Padding(12, 10, 0, 0)
    };
    /// <summary>Đang xem lại kết quả đã ghi (bấm từ lịch sử) — ô lần cân chỉ hiển thị.</summary>
    private bool _reviewMode;
    /// <summary>Nguồn số của từng lần cân (true = nhập tay) — hiện nhãn trên ô.</summary>
    private readonly bool[] _slotManual = new bool[3];
    private bool _manualForNext;
    // Tự chốt khi ổn định: chỉ chốt khi số đã thay đổi so với lần chốt trước (hoặc cân đã về 0) — không chốt trùng mẫu.
    private decimal? _lastCapturedKg;
    private bool _sawEmptySinceCapture = true;
    private const decimal EMPTY_SCALE_KG = 0.05m;
    private const decimal SAME_READING_KG = 0.02m;

    // ── Tìm chuyến ──
    private readonly TextBox _txtBarcode = new()
    {
        Dock = DockStyle.Fill,
        Font = AgrisTheme.Mono(12F),
        BackColor = Color.FromArgb(255, 255, 220),
        BorderStyle = BorderStyle.FixedSingle,
        PlaceholderText = Loc.T("quality.scan_placeholder"),
        AccessibleName = "Nhập số đăng tài hoặc biển số xe",
        AutoCompleteMode = AutoCompleteMode.None,
        AutoCompleteSource = AutoCompleteSource.None
    };
    private readonly Button _btnTripDropdown = new()
    {
        Text = "▼",
        Width = 30,
        Dock = DockStyle.Right,
        FlatStyle = FlatStyle.Flat,
        Cursor = Cursors.Hand,
        TabStop = false
    };
    private readonly ListBox _lstTripPopup = new()
    {
        Visible = false,
        BorderStyle = BorderStyle.FixedSingle,
        IntegralHeight = false,
        Font = AgrisTheme.Mono(9F)
    };
    private TripRegistrationRow[] _popupTrips = [];

    // ── Thông tin chuyến (read-only) ──
    private readonly TextBox _txtPlate = ReadOnlyField(Loc.T("field.plate"));
    private readonly TextBox _txtOwner = ReadOnlyField(Loc.T("field.sugarcane_owner"));
    private readonly TextBox _txtSugarcaneType = ReadOnlyField(Loc.T("field.sugarcane_type"));
    private readonly TextBox _txtSugarcaneVariety = ReadOnlyField(Loc.T("field.sugarcane_variety"));
    private readonly TextBox _txtTripNo = ReadOnlyField(Loc.T("field.trip_no"));
    private readonly TextBox _txtCommodity = ReadOnlyField(Loc.T("field.commodity"));
    private readonly Label _lblQrStatus = new()
    {
        Text = Loc.T("quality.qr_hint"),
        Font = AgrisTheme.Body(10F, FontStyle.Italic),
        ForeColor = AgrisTheme.BrandGreen,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        AccessibleName = "Trạng thái QR"
    };

    // ── Auto-weigh tạp chất ──
    private readonly Label _lblWorkflowStatus = new()
    {
        Text = Loc.T("quality.workflow_waiting_qr"),
        Font = AgrisTheme.Body(11F, FontStyle.Bold),
        ForeColor = AgrisTheme.Info,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Height = 48,
        AccessibleName = "Trạng thái workflow đo tạp chất"
    };

    private readonly Label _lblScaleReading = new()
    {
        Text = Loc.T("quality.scale_no_signal"),
        Font = AgrisTheme.Mono(24F, FontStyle.Bold),
        ForeColor = AgrisTheme.ScaleDisplayIdle,
        BackColor = AgrisTheme.ScaleDisplayBack,
        TextAlign = ContentAlignment.MiddleCenter,
        Dock = DockStyle.Fill,
        Height = 60,
        AccessibleName = "Số cân mẫu hiện tại"
    };

    private readonly Label _lblStableProgress = new()
    {
        Text = string.Empty,
        Font = AgrisTheme.Body(9.5F),
        ForeColor = AgrisTheme.TextMuted,
        TextAlign = ContentAlignment.MiddleCenter,
        Dock = DockStyle.Fill,
        Height = 30,
        AccessibleName = "Tiến trình ổn định"
    };

    private readonly Label _lblSampleBefore = new()
    {
        Text = Loc.T("quality.sample_before"),
        Font = AgrisTheme.Body(10F),
        ForeColor = AgrisTheme.TextPrimary,
        AutoSize = true,
        AccessibleName = "Khối lượng mẫu trước"
    };

    private readonly Label _lblSampleAfter = new()
    {
        Text = Loc.T("quality.sample_after"),
        Font = AgrisTheme.Body(10F),
        ForeColor = AgrisTheme.TextPrimary,
        AutoSize = true,
        AccessibleName = "Khối lượng mẫu sau"
    };

    private readonly Label _lblPreview = new();

    private readonly Button _btnRecordImpurity =
        GridBuilder.PrimaryButton(Loc.T("quality.btn_record"), new Size(200, 34));

    private readonly Button _btnResetWorkflow =
        GridBuilder.SecondaryButton(Loc.T("quality.btn_reset"), new Size(130, 34));

    private readonly Button _btnPrintImpurity =
        GridBuilder.SecondaryButton("In phiếu", new Size(100, 34));

    private readonly Button _btnRetryDigi =
        GridBuilder.SecondaryButton("🔄 Đẩy lại Digi", new Size(140, 34));

    // ── Tiếp tục (F7) — chuyển sang bước tiếp theo ──
    private readonly Button _btnContinue = GridBuilder.PrimaryButton(Loc.T("common.btn_continue"), new Size(130, 36));

    // ── Nhập tay ──
    private readonly Button _btnManualEntry =
        GridBuilder.SecondaryButton(Loc.T("quality.btn_manual"), new Size(100, 34));

    private readonly NumericUpDown _numManualBefore = NumericKg();
    private readonly NumericUpDown _numManualAfter = NumericKg();
    private readonly TextBox _txtManualReason = new()
    {
        PlaceholderText = "Lý do nhập tay (bắt buộc)...",
        Dock = DockStyle.Fill,
        AccessibleName = "Lý do nhập tay"
    };
    private readonly TextBox _txtManualApproval = new()
    {
        PlaceholderText = "Mã phê duyệt (không bắt buộc)...",
        Dock = DockStyle.Fill,
        AccessibleName = "Mã phê duyệt nhập tay"
    };
    private readonly Button _btnSubmitManual =
        GridBuilder.PrimaryButton(Loc.T("quality.btn_manual_record"), new Size(200, 34));
    private readonly Panel _pnlManualEntry = new()
    {
        Dock = DockStyle.Top,
        Height = 280,
        Visible = false,
        BackColor = AgrisTheme.WarningSoft,
        Padding = new Padding(8)
    };

    // ── CCS ──
    private readonly NumericUpDown _numCcs = new()
    {
        DecimalPlaces = 2, Maximum = 30m, Minimum = 0m, Increment = 0.1m, Width = 140
    };
    private readonly CheckBox _chkCcsPending = new();
    private readonly Button _btnRecordCcs =
        GridBuilder.PrimaryButton(Loc.T("quality.btn_ccs"), new Size(200, 34));

    // ── Lịch sử ──
    private readonly DataGridView _grid = GridBuilder.ReadOnlyGrid("Lịch sử mẫu chất lượng");

    // ── Phân trang + thanh cuộn lớn + phóng to lưới (giống Đăng tài / Cân hợp nhất) ──
    private readonly GridPager _pager = new("quality.samples", defaultPageSize: 20);
    private IReadOnlyList<QualitySampleRow> _allSamples = [];
    private GridScrollBar? _gridScroll;
    private readonly Button _btnExpandGrid = GridBuilder.SecondaryButton(Loc.T("grid.expand"), new Size(150, 40));
    private bool _gridExpanded;
    private const float TopAreaPercent = 62F;
    private TableLayoutPanel _root = null!;
    private Control _scanCard = null!;
    private Control _impurityCard = null!;
    /// <summary>Ngày đo — lịch sử chỉ hiện mẫu của đúng ngày này (ngày nào đo ngày đó, không chọn khoảng ngày).</summary>
    private readonly DateTimePicker _dtpHistDate = new()
    {
        Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 140,
        Font = AgrisTheme.Body(10.5F, FontStyle.Bold)
    };
    private readonly Button _btnHistToday = GridBuilder.SecondaryButton(Loc.T("ccs.ux_today"), new Size(96, 32));

    // ── Timer ──
    private readonly System.Windows.Forms.Timer _scaleTimer = new();
    private bool _scaleTimerInFlight;

    // ── Giả lập (null khi chạy với thiết bị thật) ──
    private readonly Development.SimulatedSampleScaleGateway? _simulatedScale;

    // ── Event bus ──
    private readonly TripEventBus _eventBus;
    private string? _lastQrContent;
    private readonly ISiteContext _siteContext;

    public QualityForm(
        QualityPresenter presenter,
        ICurrentUser currentUser,
        ITripDetailQueries tripDetail,
        ITripRegistrationQueries tripRegistrationQueries,
        ISiteContext siteContext,
        ISampleScaleGateway sampleScaleGateway,
        TripEventBus eventBus)
    {
        _presenter = presenter;
        _currentUser = currentUser;
        _tripDetail = tripDetail;
        _tripRegistrationQueries = tripRegistrationQueries;
        _siteId = siteContext.CurrentSiteId;
        _siteContext = siteContext;
        _simulatedScale = sampleScaleGateway as Development.SimulatedSampleScaleGateway;
        _eventBus = eventBus;

        // SampleScaleId lấy từ cấu hình site. Dùng deterministic GUID dựa trên SiteId
        // cho đến khi D-chưa duyệt chốt cơ chế ánh xạ thiết bị.
        _sampleScaleId = _siteId;

        Text = Loc.T("quality.title");
        MinimumSize = new Size(1100, 860);
        BackColor = AgrisTheme.Canvas;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        BuildLayout();
        WireEvents();

        _scaleTimer.Interval = (int)ScalePollInterval.TotalMilliseconds;
        _scaleTimer.Tick += OnScaleTimerTick;

        Load += async (_, _) =>
        {
            _txtBarcode.Focus();
            // Thẻ quy tắc hiện đúng số Portal ngay khi mở màn (không đợi quét xe).
            try { await PortalScaleRules.RefreshAsync(_cts.Token); } catch (OperationCanceledException) { }
            RefreshRuleChips();
            await LoadAllSamplesAsync();
        };

        // Tạm dừng timer khi tab không hiển thị để giảm tải CPU/IO.
        VisibleChanged += (_, _) =>
        {
            if (Visible)
            {
                if (_currentTripId is not null
                    && _workflowStep is not ImpurityWorkflowStep.ReadyToRecord
                    and not ImpurityWorkflowStep.WaitingForQr)
                {
                    _scaleTimer.Start();
                }
            }
            else
            {
                _scaleTimer.Stop();
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
            case Keys.Escape:
                ResetWorkflow();
                return true;
            case Keys.F7:
                _btnContinue.PerformClick();
                return true;
            default:
                return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Layout
    // ═══════════════════════════════════════════════════════════

    private void BuildLayout()
    {
        ConfigureGrid();
        BuildManualEntryPanel();

        // Layout: hàng trên (thông tin xe + cân tạp chất), hàng dưới (lịch sử mẫu full width)
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = AgrisTheme.Canvas,
            Padding = new Padding(2),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        // Cân tạp chất là nơi thao tác chính → chiếm ~68% bề ngang; thông tin chuyến chỉ đọc nên gọn bên trái.
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        // Phần trên (tìm chuyến + cân tạp chất) là chỗ thao tác chính → được nhiều chỗ hơn;
        // lưới lịch sử gọn lại, cần xem nhiều dòng thì bấm "Phóng to lưới".
        root.RowStyles.Add(new RowStyle(SizeType.Percent, TopAreaPercent));        // Hàng trên: form + cân
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100 - TopAreaPercent));  // Hàng dưới: lịch sử full width
        _root = root;

        var scanCard = BuildScanCard();
        _scanCard = scanCard;
        scanCard.Dock = DockStyle.Fill; // Fill hết row, không để trống
        scanCard.Margin = new Padding(0, 0, 1, 1);
        root.Controls.Add(scanCard, 0, 0);

        var impurityCard = BuildImpurityCard();
        _impurityCard = impurityCard;
        impurityCard.Dock = DockStyle.Fill; // Fill hết row, bằng chiều cao với scanCard
        impurityCard.Margin = new Padding(1, 0, 0, 1);
        root.Controls.Add(impurityCard, 1, 0);

        // Lịch sử mẫu — full width cả 2 cột
        var historyCard = BuildHistoryCard();
        historyCard.Dock = DockStyle.Fill;
        historyCard.Margin = new Padding(0, 1, 0, 0);
        root.Controls.Add(historyCard, 0, 1);
        root.SetColumnSpan(historyCard, 2);

        Controls.Add(root);
    }

    private void WireEvents()
    {
        _txtBarcode.KeyDown += OnBarcodeKeyDown;
        _btnRecordImpurity.Click += async (_, _) => await RecordImpurityFromAutoWeighAsync();
        _btnResetWorkflow.Click += (_, _) => ResetWorkflow();
        _btnPrintImpurity.Click += (_, _) => PrintImpurityTicket();
        _btnRetryDigi.Click += async (_, _) => await RetryPushDigiImpurityAsync();
        _btnContinue.Click += OnContinueClicked;
        _btnManualEntry.Click += (_, _) => EnterManualWeighing();
        _btnConfirmWeighing.Click += async (_, _) => await ConfirmWeighingAsync();

        _numManualBefore.ValueChanged += (_, _) => UpdateManualPreview();
        _numManualAfter.ValueChanged += (_, _) => UpdateManualPreview();
    }

    private void ConfigureGrid()
    {
        _grid.Columns.AddRange(
            GridBuilder.Column(Loc.T("col.reg_no"), nameof(QualitySampleRow.TripNo), 90),
            GridBuilder.Column(Loc.T("col.type"), nameof(QualitySampleRow.Type), 70),
            GridBuilder.Column(Loc.T("col.status"), nameof(QualitySampleRow.Status), 70),
            GridBuilder.Column(Loc.T("col.revision"), nameof(QualitySampleRow.Revision), 40),
            GridBuilder.Column(Loc.T("col.effective"), nameof(QualitySampleRow.IsEffective), 55),
            // Mẫu tạp chất chỉ vài kg → hiện 2 số lẻ (11,80 kg); N0 làm tròn thành 12 kg, sai lệch tỷ lệ tạp chất.
            GridBuilder.Column(Loc.T("col.sample_before"), nameof(QualitySampleRow.SampleBeforeKg), 95, "N2"),
            GridBuilder.Column(Loc.T("col.sample_after"), nameof(QualitySampleRow.SampleAfterKg), 95, "N2"),
            GridBuilder.Column(Loc.T("col.impurity_pct"), nameof(QualitySampleRow.ImpurityPercent), 90, "N4"),
            GridBuilder.Column(Loc.T("col.ccs"), nameof(QualitySampleRow.CcsValue), 80, "#,##0.##"),
            GridBuilder.Column(Loc.T("col.small_sample_warning"),
                nameof(QualitySampleRow.BelowMinimumSampleWarning), 80),
            GridBuilder.Column(Loc.T("col.source"), nameof(QualitySampleRow.Source), 65),
            GridBuilder.Column(Loc.T("col.received_at"), nameof(QualitySampleRow.ReceivedAt), 120));

        // Căn giữa tất cả cột
        foreach (DataGridViewColumn col in _grid.Columns)
            col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

        // Cột "Nhận lúc" — format ngày giờ local, không hiện offset
        var receivedCol = _grid.Columns[nameof(QualitySampleRow.ReceivedAt)];
        if (receivedCol is not null)
            receivedCol.DefaultCellStyle.Format = "dd/MM/yyyy HH:mm";

        // Cột "Số đăng tài" in đậm xanh, căn giữa → gợi ý clickable
        var tripNoCol = _grid.Columns[nameof(QualitySampleRow.TripNo)];
        if (tripNoCol is not null)
        {
            tripNoCol.DefaultCellStyle.ForeColor = AgrisTheme.BrandTeal;
            tripNoCol.DefaultCellStyle.Font = AgrisTheme.Body(9.5F, FontStyle.Underline | FontStyle.Bold);
        }

        // Cursor tay khi hover → báo hiệu dòng có thể bấm
        _grid.CellMouseEnter += (_, e) =>
        {
            if (e.RowIndex >= 0) _grid.Cursor = Cursors.Hand;
        };
        _grid.CellMouseLeave += (_, e) =>
        {
            _grid.Cursor = Cursors.Default;
        };

        // Tooltip hướng dẫn
        _grid.ShowCellToolTips = true;
        _grid.CellFormatting += OnHistoryGridCellFormatting;

        GridBuilder.AttachEmptyState(_grid, Loc.T("quality.history_empty"));

        _grid.CellClick += OnHistoryGridCellClick;
    }

    /// <summary>Danh sách cột gộp theo nhóm chuyến — chỉ hiện text ở dòng đầu nhóm.</summary>
    private static readonly HashSet<string> MergedColumnNames =
    [
        nameof(QualitySampleRow.TripNo),
        nameof(QualitySampleRow.Status),
        nameof(QualitySampleRow.Revision),
        nameof(QualitySampleRow.IsEffective),
        nameof(QualitySampleRow.BelowMinimumSampleWarning),
        nameof(QualitySampleRow.Source),
        nameof(QualitySampleRow.ReceivedAt),
    ];

    /// <summary>
    /// CellFormatting: tô nền theo nhóm chuyến + gộp cột (ẩn text dòng 2+ cùng TripId).
    /// </summary>
    private void OnHistoryGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0) return;
        var row = _grid.Rows[e.RowIndex];
        if (row.DataBoundItem is not QualitySampleRow sample) return;

        // ── Xác định nhóm chuyến (group index) để tô nền xen kẽ ──
        var groupIndex = 0;
        Guid? prevTid = null;
        for (var i = 0; i <= e.RowIndex; i++)
        {
            if (_grid.Rows[i].DataBoundItem is QualitySampleRow s)
            {
                if (s.TripId != prevTid) { groupIndex++; prevTid = s.TripId; }
            }
        }

        // Nền: chuyến đang chọn = InfoSoft, còn lại xen kẽ theo nhóm
        if (_currentTripId.HasValue && sample.TripId == _currentTripId)
            row.DefaultCellStyle.BackColor = AgrisTheme.InfoSoft;
        else
            row.DefaultCellStyle.BackColor = groupIndex % 2 == 0 ? AgrisTheme.Surface : AgrisTheme.SurfaceMuted;

        // Dòng trước cùng chuyến?
        var isSameGroupAsPrev = e.RowIndex > 0
            && _grid.Rows[e.RowIndex - 1].DataBoundItem is QualitySampleRow prev
            && prev.TripId == sample.TripId;

        var colName = _grid.Columns[e.ColumnIndex].DataPropertyName;

        // ── Cột merged: dòng 2+ cùng nhóm → ẩn text ──
        if (MergedColumnNames.Contains(colName))
        {
            if (colName == nameof(QualitySampleRow.TripNo))
                row.Cells[e.ColumnIndex].ToolTipText = Loc.T("quality.grid_click_hint");

            if (isSameGroupAsPrev)
            {
                e.Value = "";
                e.FormattingApplied = true;
                return;
            }

            // Dòng đầu nhóm: format ReceivedAt → local
            if (colName == nameof(QualitySampleRow.ReceivedAt) && e.Value is DateTimeOffset dto)
            {
                e.Value = dto.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
                e.FormattingApplied = true;
            }
            return;
        }

        // ── Cột ReceivedAt ngoài merged (nếu có) ──
        if (colName == nameof(QualitySampleRow.ReceivedAt) && e.Value is DateTimeOffset dtoOther)
        {
            e.Value = dtoOther.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
            e.FormattingApplied = true;
        }
    }

    /// <summary>Card quét QR + hiển thị thông tin chuyến mía.</summary>
    private Card BuildScanCard()
    {
        var table = FormLayout.FieldTable();

        // TextBox + nút ▼ dropdown
        var barcodePanel = new Panel { Dock = DockStyle.Fill, Height = 36 };
        _txtBarcode.Dock = DockStyle.Fill;
        _btnTripDropdown.FlatAppearance.BorderSize = 1;
        barcodePanel.Controls.Add(_txtBarcode);
        barcodePanel.Controls.Add(_btnTripDropdown);

        _btnTripDropdown.Click += async (_, _) => await ShowTripDropdownAsync();
        _lstTripPopup.Click += (_, _) => SelectTripFromPopup();
        _lstTripPopup.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.Handled = true; SelectTripFromPopup(); }
            else if (e.KeyCode == Keys.Escape) { _lstTripPopup.Visible = false; _txtBarcode.Focus(); }
        };

        FormLayout.AddField(table, Loc.T("field.scan_qr"), barcodePanel);
        FormLayout.AddFullWidth(table, _lblQrStatus, 36);
        FormLayout.AddField(table, Loc.T("field.trip_no"), _txtTripNo);
        FormLayout.AddField(table, Loc.T("field.plate"), _txtPlate);
        FormLayout.AddField(table, Loc.T("field.sugarcane_owner"), _txtOwner);
        FormLayout.AddField(table, Loc.T("field.sugarcane_type"), _txtSugarcaneType);
        FormLayout.AddField(table, Loc.T("field.sugarcane_variety"), _txtSugarcaneVariety);
        FormLayout.AddField(table, Loc.T("field.commodity"), _txtCommodity);

        return FormLayout.TitledCard(Loc.T("quality.section_scan"), table);
    }

    /// <summary>Card cân tạp chất với auto-weigh workflow 2 bước.</summary>
    private Card BuildImpurityCard()
    {
        var table = FormLayout.FieldTable();

        // Quy tắc đo dạng 4 thẻ nhỏ (2 × 2) thay cho một dòng ghi chú dài bị cắt "…".
        FormLayout.AddFullWidth(table, BuildRuleChips(), 84);

        // Hiển thị số cân mẫu realtime
        FormLayout.AddFullWidth(table, _lblWorkflowStatus, 48);
        FormLayout.AddFullWidth(table, _lblScaleReading, 70);
        FormLayout.AddFullWidth(table, _lblStableProgress, 32);

        // Hai ô lần cân cạnh nhau: ô đang cân viền xanh đậm; đã cân thì bấm vào để điều chỉnh (lần cân thứ 3).
        var slotRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        slotRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        slotRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        slotRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _slot1.Dock = DockStyle.Fill;
        _slot2.Dock = DockStyle.Fill;
        _slot1.Margin = new Padding(0, 0, 6, 0);
        _slot2.Margin = new Padding(6, 0, 0, 0);
        slotRow.Controls.Add(_slot1, 0, 0);
        slotRow.Controls.Add(_slot2, 1, 0);
        _slot1.Click += (_, _) => OnSlotClicked(1);
        _slot2.Click += (_, _) => OnSlotClicked(2);
        FormLayout.AddFullWidth(table, slotRow, WeighingSlotCard.PreferredCardHeight + 8);

        // Kết quả tạm tính (tạp chất %, số lần cân, ngưỡng)
        _lblPreview.Dock = DockStyle.Fill;
        _lblPreview.Font = AgrisTheme.Body(11F, FontStyle.Bold);
        _lblPreview.TextAlign = ContentAlignment.MiddleLeft;
        FormLayout.AddFullWidth(table, _lblPreview, 40);

        // Nút cân: Xác nhận (khi tắt tự chốt / cân thật chưa ổn định) · Nhập tay · Tự chốt khi ổn định.
        // Không còn nút "Điều chỉnh (lần 3)" riêng — bấm thẳng vào ô lần cân cần sửa.
        _btnAdjust.Visible = false;
        _chkAutoCapture.CheckedChanged += (_, _) => RefreshSlots();
        // Thanh nút chia cột theo %, luôn một hàng (không xuống dòng bị cắt ở màn hình hẹp).
        _chkAutoCapture.Dock = DockStyle.Fill;
        _chkAutoCapture.AutoSize = false;
        _chkAutoCapture.Margin = new Padding(12, 0, 0, 0);
        FormLayout.AddFullWidth(table, PercentBar([40, 25, 35], _btnConfirmWeighing, _btnManualEntry, _chkAutoCapture), 52);
        FormLayout.AddFullWidth(table,
            PercentBar([31, 20, 25, 24], _btnRecordImpurity, _btnPrintImpurity, _btnRetryDigi, _btnResetWorkflow), 52);

        // Nhập tay = đưa 1 số lên cân giả lập (hoặc giữ chờ xác nhận khi dùng cân thật) — không còn thanh nút giả lập cố định.

        _btnRecordImpurity.Enabled = false;
        _btnConfirmWeighing.Enabled = false;
        _btnAdjust.Enabled = false;

        // Ẩn nút nhập tay nếu không có quyền
        _btnManualEntry.Visible = _currentUser.HasPermission(
            RecordImpuritySampleHandler.ManualEntryPermission, _siteId);

        // Bọc table để card tự co giãn theo nội dung
        table.Dock = DockStyle.Top;
        table.AutoSize = true;

        var card = FormLayout.TitledCard(Loc.T("quality.section_impurity"), table);

        card.AutoScroll = true;
        return card;
    }

    // ── Thẻ quy tắc đo (số liệu lấy từ Quy tắc cân trên Portal) ──
    private readonly ChipLabel _chipRuleOrder = RuleChip("\uE74A");
    private readonly ChipLabel _chipRuleMin = RuleChip("\uE9D2");
    private readonly ChipLabel _chipRuleMax = RuleChip("\uE72C");
    private readonly ChipLabel _chipRuleThreshold = RuleChip("\uE7BA");
    private readonly ToolTip _ruleTip = new() { InitialDelay = 200 };

    private static ChipLabel RuleChip(string glyph) => new()
    {
        IconGlyph = glyph,
        Dock = DockStyle.Fill,
        Height = 34,
        Margin = new Padding(0, 0, 8, 6),
        Font = AgrisTheme.Ui(9F, FontStyle.Bold),
        ForeColor = AgrisTheme.TextSecondary,
        ChipBackColor = AgrisTheme.SurfaceMuted,
        ChipBorderColor = AgrisTheme.Border
    };

    /// <summary>Lưới 2 × 2 thẻ quy tắc — mỗi thẻ một ý ngắn, không bao giờ bị cắt giữa câu.</summary>
    private TableLayoutPanel BuildRuleChips()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(0, 6, 0, 0) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.Controls.Add(_chipRuleOrder, 0, 0);
        grid.Controls.Add(_chipRuleMin, 1, 0);
        grid.Controls.Add(_chipRuleMax, 0, 1);
        grid.Controls.Add(_chipRuleThreshold, 1, 1);
        RefreshRuleChips();
        return grid;
    }

    /// <summary>Cập nhật thẻ quy tắc theo Quy tắc cân mới nhất (gọi sau mỗi lần kéo Portal). Chưa cài → thẻ màu cảnh báo.</summary>
    private void RefreshRuleChips()
    {
        var s = SugarcaneFlowSettings.Current;

        void Set(ChipLabel chip, string text, bool warn)
        {
            chip.Text = text;
            chip.ForeColor = warn ? AgrisTheme.Warning : AgrisTheme.TextSecondary;
            chip.ChipBackColor = warn ? AgrisTheme.WarningSoft : AgrisTheme.SurfaceMuted;
            chip.ChipBorderColor = warn ? Color.FromArgb(240, 214, 160) : AgrisTheme.Border;
            chip.Invalidate();
            _ruleTip.SetToolTip(chip, text);
        }

        Set(_chipRuleOrder, Loc.T("quality.rule_order"), false);
        Set(_chipRuleMin, s.MinSampleKg is { } min
            ? Loc.T("quality.rule_min", min.ToString("N2"))
            : Loc.T("quality.rule_min_none"), s.MinSampleKg is null);
        Set(_chipRuleMax, Loc.T("quality.rule_max"), false);
        Set(_chipRuleThreshold, !s.PortalRulesLoaded
                ? Loc.T("quality.rule_threshold_not_loaded")
                : s.ImpurityThresholdPercent is { } t
                    ? Loc.T("quality.rule_threshold", t.ToString("N2"))
                    : Loc.T("quality.rule_threshold_none"),
            !s.PortalRulesLoaded || s.ImpurityThresholdPercent is null);
    }

    /// <summary>Một hàng control chia bề ngang theo phần trăm; nút giãn đầy ô.</summary>
    private static TableLayoutPanel PercentBar(int[] percents, params Control[] controls)
    {
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = controls.Length, RowCount = 1,
            Padding = new Padding(0, 6, 0, 6), Margin = Padding.Empty
        };
        bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        for (var i = 0; i < controls.Length; i++)
        {
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, percents[i]));
            var c = controls[i];
            c.Dock = DockStyle.Fill;
            c.MinimumSize = Size.Empty;
            if (c is Button) c.Margin = new Padding(i == 0 ? 0 : 6, 0, 0, 0);
            bar.Controls.Add(c, i, 0);
        }
        return bar;
    }

    private void BuildManualEntryPanel()
    {
        var table = FormLayout.FieldTable();

        var notice = GridBuilder.Notice(Loc.T("quality.manual_notice"));
        FormLayout.AddFullWidth(table, notice, 40);

        FormLayout.AddField(table, Loc.T("quality.manual_field_before"), _numManualBefore);
        FormLayout.AddField(table, Loc.T("quality.manual_field_after"), _numManualAfter);
        FormLayout.AddField(table, Loc.T("field.reason"), _txtManualReason);
        FormLayout.AddField(table, Loc.T("field.approval_ref"), _txtManualApproval);

        // Nút ghi nhập tay
        FormLayout.AddFullWidth(table, FormLayout.ActionBar(_btnSubmitManual), 48);

        table.Dock = DockStyle.Fill;
        _pnlManualEntry.Controls.Add(table);
    }

    private Card BuildCcsCard()
    {
        var table = FormLayout.FieldTable();

        FormLayout.AddField(table, Loc.T("field.ccs_value"), _numCcs);

        _chkCcsPending.Text = Loc.T("quality.ccs_pending_label");
        _chkCcsPending.AutoSize = true;
        _chkCcsPending.CheckedChanged += (_, _) => _numCcs.Enabled = !_chkCcsPending.Checked;
        FormLayout.AddFullWidth(table, _chkCcsPending, 36);

        _btnRecordCcs.Click += async (_, _) => await RecordCcsAsync();
        FormLayout.AddFullWidth(table, FormLayout.ActionBar(_btnRecordCcs), 52);

        return FormLayout.TitledCard(
            Loc.T("quality.section_ccs"),
            table,
            Loc.T("quality.ccs_section_notice"));
    }

    private Card BuildHistoryCard()
    {
        var filterBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 54,
            FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(8, 6, 8, 6)
        };
        var lblDate = new Label
        {
            Text = "📅  " + Loc.T("quality.hist_date"), AutoSize = true,
            Font = AgrisTheme.Body(9.5F, FontStyle.Bold), ForeColor = AgrisTheme.TextSecondary,
            Margin = new Padding(0, 9, 8, 0)
        };
        _dtpHistDate.Value = DateTime.Today;
        _dtpHistDate.MaxDate = DateTime.Today.AddDays(1);
        _dtpHistDate.Margin = new Padding(0, 4, 8, 0);
        _btnHistToday.Margin = new Padding(0, 2, 0, 0);
        _btnHistToday.MaximumSize = new Size(100, 36);
        _btnHistToday.Click += (_, _) => _dtpHistDate.Value = DateTime.Today;

        _btnExpandGrid.Margin = new Padding(16, 0, 0, 0);
        _btnExpandGrid.Click += (_, _) => ToggleGridExpanded();

        filterBar.Controls.AddRange([lblDate, _dtpHistDate, _btnHistToday, _btnExpandGrid]);

        // Đổi ngày đo → tải lại mẫu của đúng ngày đó
        _dtpHistDate.ValueChanged += async (_, _) => { _pager.Reset(); await LoadAllSamplesAsync(); };

        _pager.PageChanged += (_, _) =>
        {
            BindCurrentPage();
            if (_grid.Rows.Count > 0)
                _grid.FirstDisplayedScrollingRowIndex = 0;
        };

        _grid.Dock = DockStyle.Fill;

        var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(1) };
        var header = GridBuilder.SectionHeader(Loc.T("quality.section_history"));

        // Gợi ý clickable dưới header
        var hintLabel = new Label
        {
            Text = Loc.T("quality.grid_click_hint"),
            Font = AgrisTheme.Caption(),
            ForeColor = AgrisTheme.TextMuted,
            AutoSize = true,
            Dock = DockStyle.Top,
            Padding = new Padding(8, 0, 0, 2)
        };

        // Chế độ thường không phân trang (lưới cuộn dọc); bật khi Phóng to lưới.
        _pager.PagingEnabled = false;

        // Thứ tự Add ngược thứ tự dock: grid (Fill) → pager (Bottom) → filterBar → hint → header.
        card.Controls.Add(_grid);
        card.Controls.Add(_pager);
        card.Controls.Add(filterBar);
        card.Controls.Add(hintLabel);
        card.Controls.Add(header);

        _gridScroll = GridScrollBar.Attach(_grid);

        return card;
    }

    /// <summary>
    /// Bind trang hiện tại của lưới lịch sử. Giữ vị trí cuộn khi nạp lại.
    /// </summary>
    private void BindCurrentPage()
    {
        var firstRow = _grid.FirstDisplayedScrollingRowIndex;

        // Chế độ thường: toàn bộ danh sách, cuộn dọc. Phóng to lưới: phân trang.
        GridBuilder.BindAndColorize(_grid, _pager.Page(_allSamples), r => r.Status.ToString());

        if (firstRow > 0 && firstRow < _grid.Rows.Count)
        {
            try { _grid.FirstDisplayedScrollingRowIndex = firstRow; }
            catch (InvalidOperationException) { /* lưới chưa layout xong */ }
        }

        _gridScroll?.Sync();
    }

    /// <summary>Nạp danh sách mới (tập khác) → về trang 1 rồi bind.</summary>
    private void ShowSamples(IReadOnlyList<QualitySampleRow> samples)
    {
        _allSamples = samples;
        _pager.Reset();
        BindCurrentPage();
    }

    /// <summary>
    /// Phóng to lưới: ẩn khung tìm chuyến + cân tạp chất để lưới chiếm toàn màn hình. Bấm lại để thu về.
    /// </summary>
    private void ToggleGridExpanded()
    {
        _gridExpanded = !_gridExpanded;

        _root.SuspendLayout();
        _root.RowStyles[0] = _gridExpanded ? new RowStyle(SizeType.Absolute, 0) : new RowStyle(SizeType.Percent, TopAreaPercent);
        _root.RowStyles[1] = _gridExpanded ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Percent, 100 - TopAreaPercent);
        _scanCard.Visible = !_gridExpanded;
        _impurityCard.Visible = !_gridExpanded;
        _root.ResumeLayout(true);

        _btnExpandGrid.Text = _gridExpanded ? Loc.T("grid.collapse") : Loc.T("grid.expand");
        _btnExpandGrid.AccessibleName = _btnExpandGrid.Text;

        // Phân trang chỉ khi phóng to lưới; chế độ thường hiện mọi dòng và cuộn dọc.
        _pager.PagingEnabled = _gridExpanded;
        BindCurrentPage();

        // Thu về → đưa con trỏ lại ô quét QR để quét tiếp ngay.
        if (!_gridExpanded && _txtBarcode.Enabled)
            _txtBarcode.Focus();
    }

    /// <summary>Load tất cả quality samples trong khung ngày (không chỉ 1 chuyến).</summary>
    private async Task LoadAllSamplesAsync()
    {
        try
        {
            var fromUtc = _dtpHistDate.Value.Date.ToUniversalTime();
            var toUtc = _dtpHistDate.Value.Date.AddDays(1).ToUniversalTime();

            _allSamples = await _tripDetail.ListAllSamplesAsync(_siteId, fromUtc, toUtc, _cts.Token);
            BindCurrentPage();
        }
        catch (Exception ex)
        {
            ShowError(Loc.T("quality.load_error"), ex);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  QR Scan
    // ═══════════════════════════════════════════════════════════

    private async void OnBarcodeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter) return;
        e.Handled = true;
        e.SuppressKeyPress = true;

        var qrContent = _txtBarcode.Text.Trim();
        if (string.IsNullOrWhiteSpace(qrContent)) return;

        _lastQrContent = qrContent;
        await ScanQrAsync(qrContent);
    }

    /// <summary>
    /// Quét QR từ bên ngoài (event bus auto-navigate). Điền QR vào barcode + trigger scan.
    /// Reset trạng thái cũ trước để không dính dữ liệu chuyến trước.
    /// </summary>
    internal async Task AutoScanQrAsync(string qrContent)
    {
        ResetWorkflow();
        _lastQrContent = qrContent;
        _txtBarcode.Text = qrContent;
        await Task.Yield();
        await ScanQrAsync(qrContent);
    }

    /// <param name="fromHistory">true = mở lại từ lưới lịch sử để xem/in → không hỏi đo lại.</param>
    private async Task ScanQrAsync(string qrContent, bool fromHistory = false)
    {
        _lblQrStatus.Text = Loc.T("quality.scan_searching");
        _lblQrStatus.ForeColor = AgrisTheme.Info;

        try
        {
            var result = await _presenter.ScanQrAsync(qrContent, _siteId, _cts.Token);

            if (!result.IsSuccess)
            {
                _lblQrStatus.Text = $"❌ {result.ErrorCode}: {result.ErrorMessage}";
                _lblQrStatus.ForeColor = AgrisTheme.Danger;
                _txtBarcode.SelectAll();
                _txtBarcode.Focus();
                return;
            }

            // Điền thông tin chuyến
            _currentTripId = result.TripId;
            _currentFrmLenhDonId = result.FrmLenhDonId;
            _currentFrmMaLenhDon = result.FrmMaLenhDon;
            _currentFrmLenhDonBu = result.FrmLenhDonBu;
            _txtTripNo.Text = result.TripNo ?? "—";
            _txtPlate.Text = result.Plate ?? "—";
            _txtOwner.Text = result.SugarcaneOwner ?? result.CustomerOrOwner ?? "—";
            _txtSugarcaneType.Text = result.SugarcaneType ?? "—";
            _txtSugarcaneVariety.Text = result.SugarcaneVariety ?? "—";
            _txtCommodity.Text = _presenter.CommodityForTrip(result.FrmLenhDonId, result.FrmMaLenhDon ?? result.ShipmentNo)
                                 ?? result.Commodity ?? "—";

            _lblQrStatus.Text = Loc.T("quality.scan_ok", result.TripNo, result.Plate);
            _lblQrStatus.ForeColor = AgrisTheme.Success;

            if (fromHistory)
            {
                await LoadSamplesAsync();
                return;
            }

            // Kiểm tra trước khi đo: mẫu đã đo / đủ số lần, xe vượt khối lượng cấu hình, cân mẫu mất kết nối.
            if (!await CheckBeforeMeasureAsync(result.TripId!.Value, result.Plate ?? result.TripNo ?? "—"))
            {
                await LoadSamplesAsync();
                return;
            }

            // Bắt đầu workflow đo tạp chất
            StartAutoWeighWorkflow();

            // Tải lịch sử mẫu
            await LoadSamplesAsync();
        }
        catch (Exception ex)
        {
            _lblQrStatus.Text = $"Lỗi: {ex.Message}";
            _lblQrStatus.ForeColor = AgrisTheme.Danger;
        }
    }

    /// <summary>
    /// Kiểm tra trước khi bắt đầu đo tạp chất. Trả false nếu không đo (đủ số lần / người dùng không đo lại).
    /// - Mẫu đã đo tạp chất → cảnh báo, hỏi đo lại; đã đo đủ số lần tối đa → chặn.
    /// - Xe vượt khối lượng cấu hình (vd. > 30 tấn) → cảnh báo.
    /// - Cân mẫu không có tín hiệu → cảnh báo.
    /// </summary>
    private async Task<bool> CheckBeforeMeasureAsync(Guid tripId, string plate)
    {
        // Lấy quy tắc cân mới nhất từ Portal (ngưỡng tạp chất, khối lượng tối đa, cân lần 1 tối thiểu).
        await PortalScaleRules.RefreshAsync(_cts.Token);
        var settings = SugarcaneFlowSettings.Current;
        RefreshRuleChips();
        if (!settings.PortalRulesLoaded)
        {
            MessageBox.Show(this, Loc.T("scale_rules.not_loaded_msg", PortalScaleRules.LastError ?? "—"),
                Loc.T("scale_rules.not_loaded_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        else if (settings.ImpurityThresholdPercent is null)
        {
            MessageBox.Show(this, Loc.T("quality.threshold_not_configured_scan_msg"),
                Loc.T("quality.threshold_not_configured_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        var detail = await _tripDetail.GetAsync(tripId, _cts.Token);

        // 1. Mỗi xe chỉ một kết quả tạp chất (tối đa 3 lần cân: 2 lần + 1 điều chỉnh) → đã có thì chặn.
        if (detail?.QualitySamples.FirstOrDefault(s =>
                s.Type == Domain.Enums.QualitySampleType.Impurity && s.IsEffective) is { } effective)
        {
            MessageBox.Show(this,
                Loc.T("quality.already_measured_block_msg", plate, effective.ImpurityPercent?.ToString("N4") ?? "—"),
                Loc.T("quality.already_measured_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        // 2. Xe vượt tham số khối lượng (cân vào)
        if (settings.MaxVehicleMassTon is { } maxTon && detail is not null)
        {
            var grossKg = detail.WeighEvents
                .Where(w => w.IsEffective && w.Role == WeightRole.Gross)
                .Select(w => (decimal?)w.NormalizedKg).FirstOrDefault();
            if (grossKg is { } kg && kg / 1000m > maxTon)
            {
                MessageBox.Show(this, Loc.T("quality.over_mass_msg", plate, (kg / 1000m).ToString("N3"), maxTon.ToString("N3")),
                    Loc.T("quality.over_mass_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // 3. Cân mẫu mất kết nối
        try
        {
            var view = await _presenter.ReadSampleScaleAsync(_sampleScaleId, _cts.Token);
            if (view.WeightKg is null || !view.SignalFresh)
                MessageBox.Show(this, Loc.T("quality.scale_disconnected_msg"), Loc.T("quality.scale_disconnected_title"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            MessageBox.Show(this, Loc.T("quality.scale_disconnected_msg") + "\n\n" + ex.Message,
                Loc.T("quality.scale_disconnected_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        return true;
    }

    /// <summary>
    /// Kiểm tra ngưỡng tạp chất trước khi lưu. Chưa cài ngưỡng → cảnh báo, hỏi lưu tiếp.
    /// Vượt ngưỡng → cảnh báo, hỏi có lưu kết quả không. Trả true nếu lưu.
    /// </summary>
    private bool ConfirmImpurityThreshold(decimal sampleBeforeKg, decimal sampleAfterKg)
    {
        var calc = ImpurityCalculator.Calculate(sampleBeforeKg, sampleAfterKg,
            SugarcaneFlowSettings.Current.MinSampleKg ?? 0m);
        if (!calc.IsValid || calc.ImpurityPercent is not { } percent) return true; // Handler sẽ báo lỗi cụ thể

        var settings = SugarcaneFlowSettings.Current;
        var threshold = settings.ImpurityThresholdPercent;
        if (threshold is null)
        {
            var msgKey = settings.PortalRulesLoaded
                ? "quality.threshold_not_configured_msg"
                : "quality.threshold_not_loaded_msg";
            return MessageBox.Show(this, Loc.T(msgKey, percent.ToString("N4")),
                Loc.T("quality.threshold_not_configured_title"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
                == DialogResult.Yes;
        }

        if (percent > threshold.Value)
        {
            return MessageBox.Show(this,
                Loc.T("quality.over_threshold_msg", _txtPlate.Text, percent.ToString("N4"), threshold.Value.ToString("N2")),
                Loc.T("quality.over_threshold_title"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        return true;
    }

    // ═══════════════════════════════════════════════════════════
    //  Auto-weigh workflow 2 bước
    // ═══════════════════════════════════════════════════════════

    /// <summary>Bắt đầu đo tạp chất cho chuyến vừa chọn: chờ cân lần 1.</summary>
    private void StartAutoWeighWorkflow()
    {
        _sampleBeforeKg = null;
        _sampleAfterKg = null;
        _weighingCount = 0;
        _adjustTarget = null;
        _adjustedWeighing = null;
        _adjustOldKg = null;
        _usedManual = false;
        _manualApproval = null;
        _pendingManualKg = null;
        _lblSampleBefore.Text = Loc.T("quality.sample_before");
        _lblSampleBefore.ForeColor = AgrisTheme.TextPrimary;
        _lblSampleAfter.Text = Loc.T("quality.sample_after");
        _lblSampleAfter.ForeColor = AgrisTheme.TextPrimary;
        _lblPreview.Text = string.Empty;
        _btnRecordImpurity.Enabled = false;
        ResetSlotTracking();

        SetWorkflowStep(ImpurityWorkflowStep.WaitingSampleBefore);
        _scaleTimer.Start();
    }

    /// <summary>Xoá dấu vết của lượt trước: nguồn số từng lần, mốc tự chốt. Cân phải về 0 trước lần tự chốt đầu.</summary>
    private void ResetSlotTracking()
    {
        _reviewMode = false;
        Array.Clear(_slotManual);
        _manualForNext = false;
        _lastCapturedKg = null;
        _sawEmptySinceCapture = false;
    }

    /// <summary>
    /// Vẽ lại 2 ô lần cân theo trạng thái luồng:
    /// - Ô đang chờ cân (lần 1 → lần 2, hoặc ô đang điều chỉnh) = Active.
    /// - Ô đã có số: còn lượt điều chỉnh → Done (bấm để sửa); hết lượt / xem lại → Final.
    /// - Lần 2 khi chưa có lần 1 → Locked.
    /// </summary>
    private void RefreshSlots()
    {
        var auto = _chkAutoCapture.Checked;
        var canAdjust = !_reviewMode && _currentTripId is not null && _adjustedWeighing is null
                        && _sampleBeforeKg is not null && _sampleAfterKg is not null
                        && _workflowStep is ImpurityWorkflowStep.ReadyToRecord or ImpurityWorkflowStep.Adjusting;

        void Render(WeighingSlotCard card, int n, decimal? kg)
        {
            var activeNow = !_reviewMode && (NextWeighingNumber == n || (NextWeighingNumber == 3 && _adjustTarget == n));
            string? tag = null;
            Color? tagColor = null;
            if (_adjustedWeighing == n) { tag = Loc.T("quality.slot_tag_adjusted"); tagColor = AgrisTheme.Warning; }
            else if (kg is not null) { tag = _slotManual[n] ? Loc.T("quality.slot_tag_manual") : Loc.T("quality.slot_tag_scale"); tagColor = _slotManual[n] ? AgrisTheme.Warning : AgrisTheme.Info; }

            if (activeNow)
            {
                card.SetContent(WeighingSlotState.Active,
                    kg is { } old ? $"{old:N2} kg" : "— kg",
                    NextWeighingNumber == 3
                        ? Loc.T(auto ? "quality.slot_status_adjusting_auto" : "quality.slot_status_adjusting")
                        : Loc.T(auto ? "quality.slot_status_active_auto" : "quality.slot_status_active"),
                    NextWeighingNumber == 3 ? Loc.T("quality.slot_tag_adjusting") : tag,
                    NextWeighingNumber == 3 ? AgrisTheme.Warning : tagColor);
            }
            else if (kg is { } v)
            {
                card.SetContent(canAdjust && _workflowStep == ImpurityWorkflowStep.ReadyToRecord ? WeighingSlotState.Done : WeighingSlotState.Final,
                    $"{v:N2} kg",
                    _reviewMode ? Loc.T("quality.slot_status_review")
                        : canAdjust ? Loc.T("quality.slot_status_click_adjust")
                        : _adjustedWeighing is not null ? Loc.T("quality.slot_status_no_more_adjust")
                        : Loc.T("quality.slot_status_done"),
                    tag, tagColor);
            }
            else
            {
                card.SetContent(WeighingSlotState.Locked, "— kg",
                    _currentTripId is null ? Loc.T("quality.slot_status_wait_qr") : Loc.T("quality.slot_status_wait_prev"));
            }
        }

        Render(_slot1, 1, _sampleBeforeKg);
        Render(_slot2, 2, _sampleAfterKg);
    }

    /// <summary>
    /// Bấm vào ô lần cân. Đủ 2 lần + còn lượt → điều chỉnh ô đó (lần cân thứ 3, hỏi xác nhận).
    /// Đang điều chỉnh → đổi sang ô kia. Ô đang cân / chưa tới lượt → chỉ nhắc thao tác.
    /// </summary>
    private void OnSlotClicked(int n)
    {
        if (_reviewMode || _currentTripId is null) return;

        if (_workflowStep == ImpurityWorkflowStep.Adjusting)
        {
            if (_adjustTarget != n) { _adjustTarget = n; SetWorkflowStep(ImpurityWorkflowStep.Adjusting); }
            else
            {
                // Bấm lại ô đang điều chỉnh = bỏ điều chỉnh, giữ nguyên 2 số cũ (chưa tính lượt thứ 3).
                _adjustTarget = null;
                FinalizeAutoWeigh();
            }
            return;
        }

        if (_workflowStep == ImpurityWorkflowStep.ReadyToRecord && _adjustedWeighing is null
            && _sampleBeforeKg is not null && _sampleAfterKg is not null)
        {
            var oldKg = n == 1 ? _sampleBeforeKg : _sampleAfterKg;
            var ok = Helpers.DigiScalePopup.ShowConfirm(this,
                Loc.T("quality.adjust_confirm_title", n).ToUpper(),
                Loc.T("quality.adjust_confirm_yes", n),
                Loc.T("common.cancel"),
                [
                    (Loc.T("field.plate"), _txtPlate.Text),
                    (Loc.T("quality.slot1_title"), $"{_sampleBeforeKg:N2} kg"),
                    (Loc.T("quality.slot2_title"), $"{_sampleAfterKg:N2} kg"),
                ],
                Loc.T("quality.adjust_confirm_msg", n, oldKg?.ToString("N2") ?? "—"));
            if (!ok) return;
            StartAdjustment(n);
            return;
        }

        if (NextWeighingNumber != n && NextWeighingNumber is 1 or 2)
        {
            _lblStableProgress.Text = Loc.T("quality.slot_hint_order", NextWeighingNumber);
            _lblStableProgress.ForeColor = AgrisTheme.Warning;
        }
    }

    /// <summary>Số thứ tự lần cân đang chờ xác nhận (1, 2 hoặc 3 = điều chỉnh).</summary>
    private int NextWeighingNumber => _workflowStep switch
    {
        ImpurityWorkflowStep.WaitingSampleBefore => 1,
        ImpurityWorkflowStep.WaitingSampleAfter => 2,
        ImpurityWorkflowStep.Adjusting => 3,
        _ => 0
    };

    private void SetWorkflowStep(ImpurityWorkflowStep step)
    {
        _workflowStep = step;

        (_lblWorkflowStatus.Text, _lblWorkflowStatus.ForeColor) = step switch
        {
            ImpurityWorkflowStep.WaitingForQr =>
                (Loc.T("quality.workflow_waiting_qr"), AgrisTheme.TextMuted),
            ImpurityWorkflowStep.WaitingSampleBefore =>
                (Loc.T("quality.step_weigh1"), AgrisTheme.Info),
            ImpurityWorkflowStep.WaitingSampleAfter =>
                (Loc.T("quality.step_weigh2", _sampleBeforeKg?.ToString("N2") ?? "—"), AgrisTheme.Info),
            ImpurityWorkflowStep.Adjusting =>
                (Loc.T("quality.step_adjust", _adjustTarget ?? 0), AgrisTheme.Warning),
            ImpurityWorkflowStep.ReadyToRecord =>
                (Loc.T("quality.workflow_ready"), AgrisTheme.Success),
            _ => ("", AgrisTheme.TextMuted)
        };

        // Nút theo bước: xác nhận khi đang chờ cân; điều chỉnh chỉ khi đủ 2 lần và chưa điều chỉnh.
        var waiting = NextWeighingNumber > 0;
        _btnConfirmWeighing.Enabled = waiting;
        _btnConfirmWeighing.Text = NextWeighingNumber == 3
            ? Loc.T("quality.btn_confirm_adjust", _adjustTarget ?? 0)
            : Loc.T("quality.btn_confirm_weighing", waiting ? NextWeighingNumber : Math.Min(_weighingCount + 1, 2));
        _btnAdjust.Enabled = false;
        _btnManualEntry.Enabled = waiting && _currentUser.HasPermission(
            RecordImpuritySampleHandler.ManualEntryPermission, _siteId);
        RefreshSlots();
    }

    /// <summary>
    /// Nhập tay MỘT số cho lần cân đang chờ (lần 1, lần 2 hoặc lần điều chỉnh), cho phép số lẻ.
    /// Có cân giả lập → đưa số lên cân giả lập; cân thật → giữ số chờ bấm Xác nhận.
    /// Lần nhập tay đầu tiên của chuyến cần Trưởng đơn vị xác nhận (user + mật khẩu).
    /// </summary>
    /// <summary>Đang mở hộp thoại / đang chốt — tự chốt tạm dừng (timer WinForms vẫn chạy khi có hộp thoại).</summary>
    private bool _captureBlocked;

    private void EnterManualWeighing()
    {
        _captureBlocked = true;
        try { EnterManualWeighingCore(); }
        finally { _captureBlocked = false; }
    }

    private void EnterManualWeighingCore()
    {
        var n = NextWeighingNumber;
        if (_currentTripId is null || n == 0) return;

        if (_manualApproval is null)
        {
            _manualApproval = ApprovalDialog.Request(this, ApprovalLevel.UnitHead,
                Loc.T("approval.action_manual_impurity_one", _txtPlate.Text));
            if (_manualApproval is null)
            {
                _lblWorkflowStatus.Text = Loc.T("approval.cancelled");
                _lblWorkflowStatus.ForeColor = AgrisTheme.Warning;
                return;
            }
        }

        using var dlg = new Form
        {
            Text = Loc.T("quality.manual_one_title", n),
            Size = new Size(460, 230),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = AgrisTheme.Surface
        };
        var table = FormLayout.FieldTable();
        var num = new NumericUpDown
        {
            Minimum = 0, Maximum = 99999m, DecimalPlaces = 2, Increment = 0.01m,
            Dock = DockStyle.Fill, Font = AgrisTheme.Body(14F, FontStyle.Bold),
            ThousandsSeparator = true
        };
        FormLayout.AddField(table, Loc.T("quality.manual_one_field", n), num);
        var ok = GridBuilder.PrimaryButton(Loc.T("quality.manual_one_ok"), new Size(160, 40));
        ok.DialogResult = DialogResult.OK;
        var cancel = GridBuilder.SecondaryButton(Loc.T("common.cancel"), new Size(100, 40));
        cancel.DialogResult = DialogResult.Cancel;
        FormLayout.AddFullWidth(table, FormLayout.ActionBar(ok, cancel), FormLayout.ActionRowHeight);
        dlg.Controls.Add(table);
        dlg.AcceptButton = ok;
        dlg.CancelButton = cancel;
        dlg.Shown += (_, _) => { num.Select(0, num.Text.Length); num.Focus(); };

        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (num.Value <= 0)
        {
            MessageBox.Show(this, Loc.T("quality.weighing_no_value"), Loc.T("common.warning"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _usedManual = true;
        _manualForNext = true;
        if (_simulatedScale is not null)
        {
            _simulatedScale.SetWeight(num.Value);
            _pendingManualKg = null;
        }
        else
        {
            // Chưa có thiết bị: giữ số nhập tay chờ Xác nhận, timer không ghi đè hiển thị.
            _pendingManualKg = num.Value;
        }

        // Hiện số nhập tay lên ô số cân ngay, không chờ tín hiệu thiết bị.
        _lblScaleReading.Text = $"{num.Value:N2} kg";
        _lblScaleReading.ForeColor = AgrisTheme.Warning;

        _lblStableProgress.Text = Loc.T("quality.manual_one_set", num.Value.ToString("N2"), n);
        _lblStableProgress.ForeColor = AgrisTheme.Warning;
        _btnConfirmWeighing.Focus();
    }

    /// <summary>
    /// Xác nhận số trên cân cho lần cân đang chờ:
    /// - Lần 1: mẫu trước, ≥ khối lượng tối thiểu.
    /// - Lần 2: mẫu sau, PHẢI NHỎ HƠN lần 1.
    /// - Lần 3 (điều chỉnh): thay đúng lần 1 hoặc lần 2 đã chọn, kiểm tra lại lần 2 &lt; lần 1.
    /// Số không hợp lệ thì không tính là một lần cân, cân lại.
    /// </summary>
    private async Task ConfirmWeighingAsync(decimal? autoKg = null)
    {
        if (_captureBlocked) return;
        _captureBlocked = true;
        try { await ConfirmWeighingCoreAsync(autoKg); }
        finally { _captureBlocked = false; }
    }

    private async Task ConfirmWeighingCoreAsync(decimal? autoKg)
    {
        var n = NextWeighingNumber;
        if (_currentTripId is null || n == 0) return;

        decimal? kg = autoKg ?? _pendingManualKg;
        if (kg is null)
        {
            try
            {
                var view = await _presenter.ReadSampleScaleAsync(_sampleScaleId, _cts.Token);
                if (view.WeightKg is { } w && view.SignalFresh) kg = w;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { }
        }

        if (kg is not { } value || value <= 0)
        {
            MessageBox.Show(this, Loc.T("quality.weighing_no_value"), Loc.T("common.warning"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Mốc tự chốt: số này đã được xét (dù hợp lệ hay không) → không tự chốt lại cho tới khi số đổi / cân về 0.
        _lastCapturedKg = value;
        _sawEmptySinceCapture = false;
        var fromManual = _manualForNext;
        _manualForNext = false;

        var minSampleKg = SugarcaneFlowSettings.Current.MinSampleKg;
        var target = n == 3 ? _adjustTarget ?? 0 : n;
        var before = target == 1 ? value : _sampleBeforeKg;
        var after = target == 2 ? value : _sampleAfterKg;

        // Lần 1 (hoặc điều chỉnh lần 1): không dưới khối lượng mẫu tối thiểu (Portal; chưa có thì không kiểm).
        if (target == 1 && minSampleKg is { } minKg && value < minKg)
        {
            MessageBox.Show(this,
                Loc.T("quality.sample_below_min_msg", value.ToString("N2"), minKg.ToString("N2")),
                Loc.T("quality.sample_below_min_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Lần 2 phải nhỏ hơn lần 1 (áp dụng cả khi điều chỉnh lần 1 hoặc lần 2).
        if (before is { } b && after is { } a && a >= b)
        {
            MessageBox.Show(this,
                Loc.T("quality.weighing_second_not_less", a.ToString("N2"), b.ToString("N2")),
                Loc.T("quality.invalid_measurement_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _pendingManualKg = null;
        if (target is 1 or 2) _slotManual[target] = fromManual;

        if (target == 1)
        {
            if (n == 3) _adjustOldKg = _sampleBeforeKg;
            _sampleBeforeKg = value;
            _lblSampleBefore.Text = Loc.T("quality.sample_before_value", value.ToString("N2"));
            _lblSampleBefore.ForeColor = AgrisTheme.Success;
        }
        else
        {
            if (n == 3) _adjustOldKg = _sampleAfterKg;
            _sampleAfterKg = value;
            _lblSampleAfter.Text = Loc.T("quality.sample_after_value", value.ToString("N2"));
            _lblSampleAfter.ForeColor = AgrisTheme.Success;
        }

        _weighingCount++;
        if (n == 3)
        {
            _adjustedWeighing = _adjustTarget;
            _adjustTarget = null;
        }

        if (_sampleBeforeKg is not null && _sampleAfterKg is not null)
            FinalizeAutoWeigh();
        else
            SetWorkflowStep(ImpurityWorkflowStep.WaitingSampleAfter);
    }

    /// <summary>
    /// Lần cân thứ 3: điều chỉnh lần 1 hoặc lần 2 (chỉ một trong hai, chỉ một lần) — mở từ thao tác bấm vào ô lần cân.
    /// Cân phải đổi số (lấy mẫu ra / đặt lại) mới tự chốt, không chốt lại đúng số cũ.
    /// </summary>
    private void StartAdjustment(int target)
    {
        if (_weighingCount != 2 || _adjustedWeighing is not null || target is not (1 or 2)) return;

        _adjustTarget = target;
        _btnRecordImpurity.Enabled = false;
        SetWorkflowStep(ImpurityWorkflowStep.Adjusting);
        _scaleTimer.Start();
    }

    private async void OnScaleTimerTick(object? sender, EventArgs e)
    {
        if (_currentTripId is null || _scaleTimerInFlight) return;
        if (_workflowStep is ImpurityWorkflowStep.ReadyToRecord
            or ImpurityWorkflowStep.WaitingForQr) return;
        // Đang có số nhập tay chờ xác nhận → giữ nguyên số đó trên màn hình.
        if (_pendingManualKg is not null) return;

        _scaleTimerInFlight = true;
        try
        {
            var view = await _presenter.ReadSampleScaleAsync(_sampleScaleId, _cts.Token);
            UpdateScaleDisplay(view);
            await TryAutoCaptureAsync(view);
        }
        catch (OperationCanceledException) { }
        catch
        {
            // Lỗi tạm thời đọc cân mẫu — giữ nguyên hiển thị, thử lại lần sau.
        }
        finally
        {
            _scaleTimerInFlight = false;
        }
    }

    /// <summary>
    /// Tự chốt lần cân đang chọn khi số cân ổn định đủ <see cref="StableThresholdSeconds"/> giây.
    /// Chỉ chốt khi số đã THAY ĐỔI so với lần chốt trước (đặt mẫu khác) hoặc cân đã về 0 (lấy mẫu ra rồi đặt lại) —
    /// tránh lấy trùng số lần 1 cho lần 2. Số không hợp lệ (lần 2 ≥ lần 1, dưới tối thiểu) báo một lần rồi chờ số mới.
    /// </summary>
    private async Task TryAutoCaptureAsync(SampleScaleView view)
    {
        if (view.WeightKg is not { } kg || !view.SignalFresh) return;
        if (kg <= EMPTY_SCALE_KG) { _sawEmptySinceCapture = true; return; }
        if (!_chkAutoCapture.Checked || _captureBlocked || _reviewMode || NextWeighingNumber == 0) return;

        var isNewSample = _sawEmptySinceCapture
            || (_lastCapturedKg is { } last && Math.Abs(kg - last) > SAME_READING_KG);
        if (!isNewSample)
        {
            if (view.IsStable)
            {
                _lblStableProgress.Text = Loc.T("quality.auto_wait_new_sample");
                _lblStableProgress.ForeColor = AgrisTheme.TextMuted;
            }
            return;
        }

        if (!view.IsStable) return;
        var secs = view.StableDuration.TotalSeconds;
        if (secs < StableThresholdSeconds)
        {
            _lblStableProgress.Text = Loc.T("quality.auto_countdown",
                (StableThresholdSeconds - secs).ToString("F1"), NextWeighingNumber == 3 ? _adjustTarget ?? 0 : NextWeighingNumber);
            _lblStableProgress.ForeColor = AgrisTheme.Info;
            return;
        }

        await ConfirmWeighingAsync(kg);
    }

    private void UpdateScaleDisplay(SampleScaleView view)
    {
        if (view.WeightKg is null || !view.SignalFresh)
        {
            _lblScaleReading.Text = Loc.T("quality.scale_no_signal");
            _lblScaleReading.ForeColor = AgrisTheme.ScaleDisplayIdle;
            _lblStableProgress.Text = view.WeightKg is null
                ? Loc.T("quality.scale_not_ready")
                : Loc.T("quality.scale_stale");
            return;
        }

        _lblScaleReading.Text = $"{view.WeightKg:N2} kg";
        _lblScaleReading.ForeColor = view.IsStable
            ? AgrisTheme.Success
            : AgrisTheme.Warning;

        if (view.IsStable)
        {
            var stableSecs = view.StableDuration.TotalSeconds;
            _lblStableProgress.Text = Loc.T("quality.scale_stable", stableSecs.ToString("F1"), StableThresholdSeconds.ToString());
            _lblStableProgress.ForeColor = stableSecs >= StableThresholdSeconds
                ? AgrisTheme.Success
                : AgrisTheme.Info;
        }
        else
        {
            _lblStableProgress.Text = Loc.T("quality.scale_measuring");
            _lblStableProgress.ForeColor = AgrisTheme.TextMuted;
        }
    }

    private void FinalizeAutoWeigh()
    {
        if (_sampleBeforeKg is null || _sampleAfterKg is null) return;

        // Lần 2 < lần 1 và lần 1 ≥ tối thiểu đã kiểm khi xác nhận từng lần cân → ở đây chỉ tính và hiển thị.
        var calculation = ImpurityCalculator.Calculate(
            _sampleBeforeKg.Value, _sampleAfterKg.Value, SugarcaneFlowSettings.Current.MinSampleKg ?? 0m);
        if (!calculation.IsValid)
        {
            _lblPreview.Text = Loc.T("quality.preview_invalid", calculation.InvalidReason);
            _lblPreview.ForeColor = AgrisTheme.Danger;
            _btnRecordImpurity.Enabled = false;
            return;
        }

        var threshold = SugarcaneFlowSettings.Current.ImpurityThresholdPercent;
        var overThreshold = threshold is { } t && calculation.ImpurityPercent > t;

        _lblPreview.Text = Loc.T("quality.preview_result", $"{calculation.ImpurityPercent:N4}")
            + " · " + Loc.T("quality.weighing_count_info", _weighingCount, 3)
            + (overThreshold ? " · " + Loc.T("quality.preview_over_threshold", threshold!.Value.ToString("N2")) : string.Empty);
        _lblPreview.ForeColor = overThreshold ? AgrisTheme.Warning : AgrisTheme.Success;

        _scaleTimer.Stop();
        _btnRecordImpurity.Enabled = true;
        SetWorkflowStep(ImpurityWorkflowStep.ReadyToRecord);
    }

    // ═══════════════════════════════════════════════════════════
    //  Record impurity (auto-weigh)
    // ═══════════════════════════════════════════════════════════

    private async Task RecordImpurityFromAutoWeighAsync()
    {
        if (_currentTripId is null || _sampleBeforeKg is null || _sampleAfterKg is null) return;

        if (!ConfirmImpurityThreshold(_sampleBeforeKg.Value, _sampleAfterKg.Value)) return;

        _btnRecordImpurity.Enabled = false;
        try
        {
            // Lý do lưu kèm: ai duyệt nhập tay, lần cân nào được điều chỉnh (số cũ → số mới).
            var notes = new List<string>();
            if (_usedManual && _manualApproval is not null)
                notes.Add(Loc.T("quality.reason_manual", _manualApproval.DisplayName, _manualApproval.UserName));
            if (_adjustedWeighing is { } adj)
            {
                var newKg = adj == 1 ? _sampleBeforeKg : _sampleAfterKg;
                notes.Add(Loc.T("quality.reason_adjusted", adj,
                    _adjustOldKg?.ToString("N2") ?? "—", newKg?.ToString("N2") ?? "—"));
            }

            var result = await _presenter.RecordImpurityAsync(
                new RecordImpuritySampleCommand(
                    TripId: _currentTripId.Value,
                    SampleBeforeKg: _sampleBeforeKg.Value,
                    SampleAfterKg: _sampleAfterKg.Value,
                    Source: _usedManual ? "MANUAL" : "DEVICE",
                    CorrelationId: Guid.NewGuid().ToString("N"),
                    ApprovalRef: _manualApproval?.ApprovalRef,
                    Reason: notes.Count > 0 ? string.Join(" | ", notes) : null,
                    WeighingCount: _weighingCount,
                    AdjustedWeighing: _adjustedWeighing),
                _cts.Token);

            HandleImpurityResult(result);
        }
        catch (Exception ex)
        {
            ShowError(Loc.T("quality.record_error"), ex);
            _btnRecordImpurity.Enabled = true;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Manual entry
    // ═══════════════════════════════════════════════════════════

    private void UpdateManualPreview()
    {
        var calculation = ImpurityCalculator.Calculate(_numManualBefore.Value, _numManualAfter.Value,
            SugarcaneFlowSettings.Current.MinSampleKg ?? 0m);

        if (!calculation.IsValid)
        {
            _lblPreview.Text = Loc.T("quality.preview_manual") + Loc.T("quality.preview_invalid", calculation.InvalidReason);
            _lblPreview.ForeColor = AgrisTheme.Danger;
            return;
        }

        _lblPreview.Text = Loc.T("quality.preview_manual") + Loc.T("quality.preview_result", $"{calculation.ImpurityPercent:N4}")
            + (calculation.BelowMinimumSampleWarning ? Loc.T("quality.preview_small_warning") : string.Empty);

        _lblPreview.ForeColor = calculation.BelowMinimumSampleWarning
            ? AgrisTheme.Warning
            : AgrisTheme.Success;
    }

    private async void HandleImpurityResult(RecordImpuritySampleResult result)
    {
        if (!result.IsSuccess)
        {
            MessageBox.Show(result.FailureReason, Loc.T("quality.record_failed_title"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _btnRecordImpurity.Enabled = true;
            return;
        }

        var warning = result.BelowMinimumSampleWarning
            ? Loc.T("quality.record_ok_warning")
            : string.Empty;

        _ = LoadSamplesAsync();

        // Push phiếu tạp chất lên DigiScale + popup
        try
        {
            // Hiện trạng thái đang đẩy
            _lblWorkflowStatus.Text = "⏳ Đang đẩy tạp chất lên DigiScale...";
            _lblWorkflowStatus.ForeColor = AgrisTheme.Info;
            System.Windows.Forms.Application.DoEvents(); // Refresh UI ngay

            var shipment = _txtTripNo.Text.Trim();
            var beforeKg = _sampleBeforeKg ?? _numManualBefore.Value;
            var afterKg = _sampleAfterKg ?? _numManualAfter.Value;

            // Lấy LenhDon từ scan result (đã lưu khi chọn xe) — fallback cache
            var ldForPush = _presenter.FindLenhDonForTrip(_currentFrmLenhDonId, _txtPlate.Text);
            var lenhDonId = _currentFrmLenhDonId ?? ldForPush?.SourceKey ?? shipment;
            var maLenhDon = _currentFrmMaLenhDon ?? ldForPush?.MaLD ?? shipment;
            var lenhDonBu = _currentFrmLenhDonBu ?? ldForPush?.BusinessUnit;

            // Lần đo = số lần đo tạp chất của chuyến (tối đa 3) — trước đây luôn gửi "1".
            var lanDo = (result.MeasurementNumber ?? 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var pushResult = await _presenter.PushTapChatAsync(
                lenhDonId, maLenhDon, "1", lanDo, DateTime.Now,
                beforeKg.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                afterKg.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                (result.ImpurityPercent ?? 0m).ToString("G", System.Globalization.CultureInfo.InvariantCulture),
                lenhDonBu, _cts.Token);

            var digiOk = pushResult.IsSuccess;
            var debugInfo = $"BU={lenhDonBu}, LenhDon={lenhDonId}, MaLD={maLenhDon}";
            Helpers.DigiScalePopup.Show(this,
                "GHI TẠP CHẤT THÀNH CÔNG",
                $"Chuyến: {_txtTripNo.Text}\nBiển số: {_txtPlate.Text}\nTạp chất: {result.ImpurityPercent:N4} %\n\n{debugInfo}",
                digiOk, pushResult.ErrorMessage ?? (digiOk ? "OK" : "Lỗi"));
        }
        catch (Exception pushEx)
        {
            Helpers.DigiScalePopup.Show(this,
                "GHI TẠP CHẤT THÀNH CÔNG",
                $"Chuyến: {_txtTripNo.Text}\nBiển số: {_txtPlate.Text}\nTạp chất: {result.ImpurityPercent:N4} %",
                false, $"Lỗi push: {pushEx.Message}");
        }

        // Thông báo các tab khác
        if (_currentTripId.HasValue)
        {
            _eventBus.Publish(new TripChangedEventArgs
            {
                TripId = _currentTripId.Value,
                TripNo = _txtTripNo.Text,
                Plate = _txtPlate.Text,
                QrContent = _lastQrContent,
                ChangeType = TripChangeType.ImpurityRecorded,
                SourceForm = "QualityForm"
            });
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  CCS
    // ═══════════════════════════════════════════════════════════

    private async Task RecordCcsAsync()
    {
        if (_currentTripId is null)
        {
            MessageBox.Show(Loc.T("quality.no_trip"), Loc.T("quality.no_trip_title"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _btnRecordCcs.Enabled = false;
        try
        {
            var result = await _presenter.RecordCcsAsync(
                new ReceiveCcsResultCommand(
                    TripId: _currentTripId.Value,
                    CcsValue: _chkCcsPending.Checked ? null : _numCcs.Value,
                    Source: "FRM",
                    SourceReportsPending: _chkCcsPending.Checked,
                    CorrelationId: Guid.NewGuid().ToString("N")),
                _cts.Token);

            if (!result.IsSuccess)
            {
                MessageBox.Show(result.FailureReason, Loc.T("quality.ccs_failed_title"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            MessageBox.Show(Loc.T("quality.ccs_ok", result.Status),
                Loc.T("quality.record_ok_title"), MessageBoxButtons.OK, MessageBoxIcon.Information);

            await LoadSamplesAsync();
        }
        catch (Exception ex)
        {
            ShowError(Loc.T("quality.ccs_error"), ex);
        }
        finally
        {
            _btnRecordCcs.Enabled = true;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Data loading
    // ═══════════════════════════════════════════════════════════

    private async Task LoadSamplesAsync()
    {
        if (_currentTripId is null) return;

        try
        {
            var detail = await _tripDetail.GetAsync(_currentTripId.Value, _cts.Token);
            // Chuyển sang xem mẫu của một chuyến → tập dữ liệu khác, về trang 1.
            ShowSamples(detail?.QualitySamples ?? []);
        }
        catch (Exception ex)
        {
            ShowError(Loc.T("quality.load_error"), ex);
        }
    }

    /// <summary>
    /// Click dòng trong lưới lịch sử mẫu → điền thông tin chuyến + số liệu lên form để xem / in lại phiếu.
    /// Lock form không cho sửa — bấm Làm mới (Esc) để nhập mới.
    /// </summary>
    private async void OnHistoryGridCellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        if (_grid.Rows[e.RowIndex].DataBoundItem is not QualitySampleRow row) return;
        if (row.TripId is null) return;

        // Load trip info nếu chuyến khác
        var tripNo = row.TripNo;
        if (string.IsNullOrWhiteSpace(tripNo)) return;

        if (_currentTripId != row.TripId)
        {
            _txtBarcode.Text = tripNo;
            _lastQrContent = tripNo;
            await ScanQrAsync(tripNo, fromHistory: true);
        }

        // Điền số liệu tạp chất từ dòng Impurity hiệu lực
        // Tìm dòng Impurity effective cho chuyến này
        QualitySampleRow? impurityRow = null;
        QualitySampleRow? ccsRow = null;
        for (var i = 0; i < _grid.Rows.Count; i++)
        {
            if (_grid.Rows[i].DataBoundItem is not QualitySampleRow s) continue;
            if (s.TripId != row.TripId) continue;
            if (s.Type == Domain.Enums.QualitySampleType.Impurity && s.IsEffective)
                impurityRow = s;
            if (s.Type == Domain.Enums.QualitySampleType.Ccs && s.IsEffective)
                ccsRow = s;
        }

        if (impurityRow is not null)
        {
            _sampleBeforeKg = impurityRow.SampleBeforeKg;
            _sampleAfterKg = impurityRow.SampleAfterKg;

            _lblSampleBefore.Text = impurityRow.SampleBeforeKg.HasValue
                ? Loc.T("quality.sample_before_value", $"{impurityRow.SampleBeforeKg:N0}")
                : Loc.T("quality.sample_before");
            _lblSampleAfter.Text = impurityRow.SampleAfterKg.HasValue
                ? Loc.T("quality.sample_after_value", $"{impurityRow.SampleAfterKg:N0}")
                : Loc.T("quality.sample_after");

            if (impurityRow.ImpurityPercent.HasValue)
            {
                _lblPreview.Text = Loc.T("quality.preview_result", $"{impurityRow.ImpurityPercent:N4}");
                _lblPreview.ForeColor = AgrisTheme.BrandGreen;
            }
        }

        // Lock form — chỉ xem/in, không cho sửa
        _btnRecordImpurity.Enabled = false;
        _btnManualEntry.Enabled = false;
        _scaleTimer.Stop();
        _weighingCount = 0;
        _adjustedWeighing = null;
        _reviewMode = true;
        SetWorkflowStep(ImpurityWorkflowStep.ReadyToRecord);
        _lblWorkflowStatus.Text = Loc.T("quality.review_status", tripNo);
        _lblWorkflowStatus.ForeColor = AgrisTheme.Info;
        _btnRecordImpurity.Enabled = false;
        _btnConfirmWeighing.Enabled = false;
        _btnAdjust.Enabled = false;
        _btnManualEntry.Enabled = false;
    }

    /// <summary>
    /// Tiếp tục (F7) — chuyển sang tab Kho vận (UI-04) cho chuyến đang chọn.
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
            TripNo = _txtTripNo.Text,
            Plate = _txtPlate.Text,
            QrContent = _lastQrContent,
            ChangeType = TripChangeType.ImpurityRecorded,
            SourceForm = "QualityForm"
        });
    }

    // ═══════════════════════════════════════════════════════════
    //  Workflow reset
    // ═══════════════════════════════════════════════════════════

    private void ResetWorkflow()
    {
        _scaleTimer.Stop();
        _currentTripId = null;
        _currentFrmLenhDonId = null;
        _currentFrmMaLenhDon = null;
        _currentFrmLenhDonBu = null;
        _sampleBeforeKg = null;
        _sampleAfterKg = null;
        _weighingCount = 0;
        _adjustTarget = null;
        _adjustedWeighing = null;
        _adjustOldKg = null;
        _usedManual = false;
        _manualApproval = null;
        _pendingManualKg = null;

        _txtBarcode.Clear();
        _txtTripNo.Clear();
        _txtPlate.Clear();
        _txtOwner.Clear();
        _txtSugarcaneType.Clear();
        _txtSugarcaneVariety.Clear();
        _txtCommodity.Clear();

        _lblQrStatus.Text = Loc.T("quality.qr_start");
        _lblQrStatus.ForeColor = AgrisTheme.BrandGreen;

        _lblScaleReading.Text = Loc.T("quality.scale_no_signal");
        _lblScaleReading.ForeColor = AgrisTheme.ScaleDisplayIdle;
        _lblStableProgress.Text = string.Empty;
        _lblSampleBefore.Text = Loc.T("quality.sample_before");
        _lblSampleBefore.ForeColor = AgrisTheme.TextPrimary;
        _lblSampleAfter.Text = Loc.T("quality.sample_after");
        _lblSampleAfter.ForeColor = AgrisTheme.TextPrimary;
        _lblPreview.Text = string.Empty;
        _btnRecordImpurity.Enabled = false;

        _pnlManualEntry.Visible = false;
        _btnManualEntry.Text = Loc.T("quality.btn_manual");
        _btnManualEntry.Enabled = _currentUser.HasPermission(
            RecordImpuritySampleHandler.ManualEntryPermission, _siteId);
        _numManualBefore.Value = 0;
        _numManualAfter.Value = 0;
        _txtManualReason.Clear();
        _txtManualApproval.Clear();

        ShowSamples([]);

        ResetSlotTracking();
        SetWorkflowStep(ImpurityWorkflowStep.WaitingForQr);
        _txtBarcode.Focus();
    }

    // ═══════════════════════════════════════════════════════════
    //  Trip dropdown — chọn xe đang trong nhà máy
    // ═══════════════════════════════════════════════════════════

    private async Task ShowTripDropdownAsync()
    {
        try
        {
            var filter = new TripRegistrationFilter { Tab = TripGridTab.InFactory };
            var trips = await _tripRegistrationQueries.ListAsync(_siteId, filter, _cts.Token);
            _popupTrips = trips.ToArray();

            if (_popupTrips.Length == 0) { _lstTripPopup.Visible = false; return; }

            _lstTripPopup.Items.Clear();
            foreach (var t in _popupTrips)
                _lstTripPopup.Items.Add($"{t.Plate ?? "?",-15} | {t.TripNo,-25} | {t.CustomerOrSupplier ?? ""}");

            var screenPos = _txtBarcode.PointToScreen(new Point(0, _txtBarcode.Height));
            var formPos = FindForm()!.PointToClient(screenPos);
            _lstTripPopup.Location = formPos;
            _lstTripPopup.Width = Math.Max(_txtBarcode.Width + _btnTripDropdown.Width, 500);
            _lstTripPopup.Height = Math.Min(_popupTrips.Length * 20 + 4, 300);

            if (_lstTripPopup.Parent != FindForm())
            {
                FindForm()!.Controls.Add(_lstTripPopup);
                _lstTripPopup.BringToFront();
            }
            _lstTripPopup.Visible = true;
            _lstTripPopup.Focus();
        }
        catch { _lstTripPopup.Visible = false; }
    }

    private void SelectTripFromPopup()
    {
        var idx = _lstTripPopup.SelectedIndex;
        _lstTripPopup.Visible = false;
        if (idx < 0 || idx >= _popupTrips.Length) return;

        var trip = _popupTrips[idx];
        _txtBarcode.Text = trip.TripNo;
        _lastQrContent = trip.TripNo;
        _ = ScanQrAsync(trip.TripNo);
    }

    // ═══════════════════════════════════════════════════════════
    //  In phiếu tạp chất
    // ═══════════════════════════════════════════════════════════

    /// <summary>Đẩy lại kết quả tạp chất lên DigiScale cho chuyến đang chọn.</summary>
    private async Task RetryPushDigiImpurityAsync()
    {
        if (_currentTripId is null)
        {
            MessageBox.Show("Chọn chuyến trước khi đẩy lại.", "Thiếu thông tin",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Lấy impurity data từ grid
        decimal? beforeKg = null, afterKg = null;
        for (var i = 0; i < _grid.Rows.Count; i++)
        {
            if (_grid.Rows[i].DataBoundItem is not QualitySampleRow s) continue;
            if (s.TripId != _currentTripId) continue;
            if (s.Type == Domain.Enums.QualitySampleType.Impurity && s.IsEffective)
            {
                beforeKg = s.SampleBeforeKg;
                afterKg = s.SampleAfterKg;
                break;
            }
        }

        if (!beforeKg.HasValue)
        {
            beforeKg = _sampleBeforeKg;
            afterKg = _sampleAfterKg;
        }

        var tripNo = _txtTripNo.Text;
        var plate = _txtPlate.Text;
        var shipment = tripNo;

        _btnRetryDigi.Enabled = false;
        _lblWorkflowStatus.Text = "⏳ Đang đẩy lại tạp chất lên DigiScale...";
        _lblWorkflowStatus.ForeColor = AgrisTheme.Info;

        try
        {
            var ld = _presenter.FindLenhDonForTrip(_currentFrmLenhDonId, plate);
            var pushResult = await _presenter.PushTapChatAsync(
                _currentFrmLenhDonId ?? ld?.SourceKey ?? shipment,
                _currentFrmMaLenhDon ?? ld?.MaLD ?? shipment,
                "1", "1", DateTime.Now,
                (beforeKg ?? 0m).ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                (afterKg ?? 0m).ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                Program.LoggedInUserName,
                _currentFrmLenhDonBu ?? ld?.BusinessUnit,
                _cts.Token);

            if (pushResult.IsSuccess)
            {
                _lblWorkflowStatus.Text = $"✅ Đẩy DigiScale thành công cho {tripNo}";
                _lblWorkflowStatus.ForeColor = AgrisTheme.Success;
                Helpers.DigiScalePopup.Show(this,
                    "ĐẨY LẠI TẠP CHẤT THÀNH CÔNG",
                    $"Chuyến: {tripNo}\nBiển số: {plate}\nMẫu trước: {beforeKg:N0} kg | Mẫu sau: {afterKg:N0} kg",
                    true, "OK");
            }
            else
            {
                _lblWorkflowStatus.Text = $"❌ Đẩy lại thất bại: {pushResult.ErrorMessage}";
                _lblWorkflowStatus.ForeColor = AgrisTheme.Danger;
                Helpers.DigiScalePopup.Show(this,
                    "ĐẨY LẠI TẠP CHẤT THẤT BẠI",
                    $"Chuyến: {tripNo}\nBiển số: {plate}",
                    false, pushResult.ErrorMessage ?? "Lỗi không xác định");
            }
        }
        catch (Exception ex)
        {
            _lblWorkflowStatus.Text = $"❌ Lỗi: {ex.Message}";
            _lblWorkflowStatus.ForeColor = AgrisTheme.Danger;
            MessageBox.Show($"Lỗi đẩy lại DigiScale:\n{ex.Message}", "Lỗi",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnRetryDigi.Enabled = true;
        }
    }

    private void PrintImpurityTicket()
    {
        if (_currentTripId is null)
        {
            MessageBox.Show(Loc.T("quality.print_no_trip"),
                Loc.T("quality.no_trip_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Lấy thông tin chuyến từ form fields
        var tripNo = _txtTripNo.Text;
        var plate = _txtPlate.Text;
        var owner = _txtOwner.Text;
        var sugarcaneType = _txtSugarcaneType.Text;
        var variety = _txtSugarcaneVariety.Text;
        var commodity = _txtCommodity.Text;

        // Lấy mẫu tạp chất hiệu lực từ grid — tìm dòng Impurity + IsEffective = true
        decimal? beforeKg = null, afterKg = null, impurityPct = null;
        decimal? ccsValue = null;
        DateTimeOffset? sampleTime = null;

        for (var i = 0; i < _grid.Rows.Count; i++)
        {
            if (_grid.Rows[i].DataBoundItem is not QualitySampleRow sample) continue;
            if (sample.TripId != _currentTripId) continue;

            if (sample.Type == Domain.Enums.QualitySampleType.Impurity && sample.IsEffective)
            {
                beforeKg = sample.SampleBeforeKg;
                afterKg = sample.SampleAfterKg;
                impurityPct = sample.ImpurityPercent;
                sampleTime = sample.ReceivedAt;
            }
            if (sample.Type == Domain.Enums.QualitySampleType.Ccs && sample.IsEffective)
            {
                ccsValue = sample.CcsValue;
            }
        }

        // Fallback: nếu không tìm thấy trong grid, dùng giá trị auto-weigh hiện tại
        if (!beforeKg.HasValue && _sampleBeforeKg.HasValue)
        {
            beforeKg = _sampleBeforeKg;
            afterKg = _sampleAfterKg;
            if (beforeKg > 0 && afterKg.HasValue)
                impurityPct = (beforeKg.Value - afterKg.Value) / beforeKg.Value * 100m;
        }

        // Dữ liệu in: lệnh đốn FRM bổ sung nhà máy / loại mía; ô "—" của form coi như trống → "Không có".
        static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) || s.Trim() == "—" ? null : s.Trim();
        var ldPrint = _presenter.FindLenhDonForTrip(_currentFrmLenhDonId, plate);
        var ticket = new ImpurityTicketData(
            Factory: ldPrint?.TenNhaMay ?? _siteContext.CurrentSiteName,
            TripNo: Clean(tripNo), Plate: Clean(plate), Owner: Clean(owner),
            CaneForm: Printing.PrintText.CaneForm(ldPrint) ?? Clean(commodity) ?? Clean(sugarcaneType),
            Variety: Clean(variety) ?? ldPrint?.GiongMiaName,
            BeforeKg: beforeKg, AfterKg: afterKg, ImpurityPct: impurityPct, Ccs: ccsValue,
            SampleTime: sampleTime, PrintedAt: DateTime.Now);

        using var previewForm = new Form
        {
            Text = Loc.T("quality.print_form_title", tripNo),
            ClientSize = new Size(720, 780),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = AgrisTheme.Canvas,
            Font = AgrisTheme.Body()
        };

        var pnlPreview = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(230, 230, 230),
            Padding = new Padding(15)
        };

        var receiptPanel = new DoubleBufferedPanel
        {
            Size = new Size(660, 640),
            BackColor = Color.White,
            Location = new Point(10, 10)
        };
        receiptPanel.Paint += (_, e) => DrawImpurityTicket(e.Graphics, new Rectangle(0, 0, receiptPanel.Width, receiptPanel.Height), ticket);
        pnlPreview.Controls.Add(receiptPanel);

        var btnPrint = GridBuilder.PrimaryButton(Loc.T("common.print"), new Size(120, 36));
        var btnClose = GridBuilder.SecondaryButton(Loc.T("common.close"), new Size(100, 36));

        btnPrint.Click += (_, _) =>
        {
            try
            {
                // In đúng mẫu đang xem trước (cùng hàm vẽ).
                using var printDoc = new System.Drawing.Printing.PrintDocument();
                printDoc.DocumentName = previewForm.Text;
                printDoc.PrintPage += (_, pe) => DrawImpurityTicket(pe.Graphics!,
                    new Rectangle(pe.MarginBounds.Left, pe.MarginBounds.Top, pe.MarginBounds.Width, Math.Min(pe.MarginBounds.Height, 640)), ticket);
                using var dialog = new PrintDialog { Document = printDoc, UseEXDialog = true };
                if (dialog.ShowDialog(previewForm) == DialogResult.OK)
                    printDoc.Print();
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.T("quality.print_error", ex.Message),
                    Loc.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        btnClose.Click += (_, _) => previewForm.Close();

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
            BackColor = AgrisTheme.SurfaceMuted
        };
        toolbar.Controls.Add(btnClose);
        toolbar.Controls.Add(btnPrint);

        previewForm.Controls.Add(pnlPreview);
        previewForm.Controls.Add(toolbar);
        previewForm.ShowDialog(this);
    }

    /// <summary>Số liệu phiếu tạp chất — chụp lúc bấm In.</summary>
    private sealed record ImpurityTicketData(
        string? Factory, string? TripNo, string? Plate, string? Owner, string? CaneForm, string? Variety,
        decimal? BeforeKg, decimal? AfterKg, decimal? ImpurityPct, decimal? Ccs,
        DateTimeOffset? SampleTime, DateTime PrintedAt);

    /// <summary>
    /// Vẽ phiếu đo tạp chất (xem trước + in thật). Nhãn song ngữ theo BU, ô trống in "Không có".
    /// </summary>
    private static void DrawImpurityTicket(Graphics g, Rectangle area, ImpurityTicketData d)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        const int margin = 24;
        var lx = area.Left + margin;
        var contentWidth = area.Width - margin * 2;
        float y = area.Top + margin;

        using var borderPen = new Pen(AgrisTheme.BrandGreenDark, 2f);
        g.DrawRectangle(borderPen, area.Left + 2, area.Top + 2, area.Width - 4, area.Height - 4);

        const string family = "Segoe UI";
        using var companyFont = new Font(family, 12F, FontStyle.Bold);
        using var titleFont = new Font(family, 18F, FontStyle.Bold);
        using var labelFont = new Font(family, 11F, FontStyle.Regular);
        using var valueFont = new Font(family, 11F, FontStyle.Bold);
        using var bigFont = new Font(family, 14F, FontStyle.Bold);
        using var impurityFont = new Font(family, 18F, FontStyle.Bold);
        using var tsFont = new Font(family, 9.5F, FontStyle.Italic);
        using var titleBrush = new SolidBrush(AgrisTheme.BrandGreenDark);
        using var textBrush = new SolidBrush(AgrisTheme.TextPrimary);
        using var impurityBrush = new SolidBrush(d.ImpurityPct.HasValue ? AgrisTheme.Danger : AgrisTheme.TextMuted);
        using var tsBrush = new SolidBrush(AgrisTheme.TextMuted);
        using var sepPen = new Pen(AgrisTheme.Border, 1f);

        y += Printing.PrintText.DrawCentered(g, Printing.PrintText.V(d.Factory), companyFont, Brushes.Black, lx, contentWidth, y) + 6;
        y += Printing.PrintText.DrawCentered(g, Printing.PrintText.L("quality.print_title"), titleFont, titleBrush, lx, contentWidth, y) + 14;

        var labels = new[]
        {
            Printing.PrintText.L("quality.print_label_reg_no"), Printing.PrintText.L("quality.print_label_plate"),
            Printing.PrintText.L("quality.print_label_owner"), Printing.PrintText.L("quality.print_label_sugarcane_type"),
            Printing.PrintText.L("quality.print_label_variety"), Printing.PrintText.L("quality.print_label_before"),
            Printing.PrintText.L("quality.print_label_after"), Printing.PrintText.L("quality.print_label_impurity"),
            Printing.PrintText.L("receipt.field_ccs"), Printing.PrintText.L("quality.print_label_time")
        };
        var vx = lx + Printing.PrintText.LabelWidth(g, labelFont, 160, labels);
        var lh = Math.Max(28, (int)Math.Ceiling(Printing.PrintText.Measure(g, "Ág ភ្ជ", valueFont).Height) + 6);

        void Row(string label, string? value, Font font, Brush brush, int height)
        {
            Printing.PrintText.Field(g, label, value, labelFont, font, brush, lx, vx, y);
            y += Math.Max(height, lh);
        }

        Row(labels[0], d.TripNo, valueFont, textBrush, lh);
        Row(labels[1], d.Plate, valueFont, textBrush, lh);
        Row(labels[2], d.Owner, valueFont, textBrush, lh);
        Row(labels[3], d.CaneForm, valueFont, textBrush, lh);
        Row(labels[4], d.Variety, valueFont, textBrush, lh);

        y += 6;
        g.DrawLine(sepPen, lx, y, area.Right - margin, y);
        y += 10;

        Row(labels[5], d.BeforeKg.HasValue ? $"{d.BeforeKg:N3} kg" : null, bigFont, textBrush, 32);
        Row(labels[6], d.AfterKg.HasValue ? $"{d.AfterKg:N3} kg" : null, bigFont, textBrush, 32);
        Row(labels[7], d.ImpurityPct.HasValue ? $"{d.ImpurityPct:N4} %" : null, impurityFont, impurityBrush, 36);
        if (d.Ccs.HasValue)
            Row(labels[8], $"{d.Ccs:N2}", bigFont, textBrush, 32);

        y += 6;
        g.DrawLine(sepPen, lx, y, area.Right - margin, y);
        y += 10;

        var timeStr = (d.SampleTime?.ToLocalTime().DateTime ?? d.PrintedAt).ToString("dd/MM/yyyy HH:mm:ss");
        Row(labels[9], timeStr, valueFont, textBrush, lh);

        if (!string.IsNullOrWhiteSpace(d.TripNo))
        {
            y += 10;
            Printing.Code128Renderer.Draw(g, d.TripNo, lx, (int)y, 320, 50);
            y += 54;
            using var bcFont = new Font(AgrisTheme.MonoFamily, 10F);
            g.DrawString(d.TripNo, bcFont, Brushes.Black, lx, y);
        }

        var printLabel = Printing.PrintText.L("print.field_printed_at") + ": " + (d.PrintedAt.ToString("dd/MM/yyyy HH:mm"));
        var tsSz = Printing.PrintText.Measure(g, printLabel, tsFont);
        Printing.PrintText.DrawRight(g, printLabel, tsFont, tsBrush, area.Right - margin, area.Bottom - margin - tsSz.Height - 4);
    }

    /// <summary>Double-buffered panel for GDI+ preview.</summary>
    private sealed class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════

    private static NumericUpDown NumericKg() => new()
    {
        DecimalPlaces = 0,
        Maximum = 100_000m,
        Minimum = 0m,
        Increment = 1m,
        Width = 160
    };

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
        txt.Enter += (_, _) => txt.Parent?.SelectNextControl(txt, true, true, true, true);
        return txt;
    }

    private static void ShowError(string title, Exception ex)
        => MessageBox.Show($"{title}.{Environment.NewLine}{ex.Message}",
            Loc.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _scaleTimer.Stop();
        _cts.Cancel();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _scaleTimer.Dispose();
            _cts.Dispose();
        }
        base.Dispose(disposing);
    }

    // ═══════════════════════════════════════════════════════════
    //  Workflow step enum
    // ═══════════════════════════════════════════════════════════

    private enum ImpurityWorkflowStep
    {
        /// <summary>Chờ quét QR chuyến.</summary>
        WaitingForQr,

        /// <summary>Bước 1: Chờ đặt mẫu trước lên cân.</summary>
        WaitingSampleBefore,

        /// <summary>Bước 1: Cân có tín hiệu, đang chờ ổn định.</summary>
        StabilizingBefore,

        /// <summary>Bước 1 hoàn thành: đã ghi mẫu trước, chờ lấy ra.</summary>
        RecordedBefore,

        /// <summary>Bước 2: Chờ đặt mẫu sau lên cân.</summary>
        WaitingSampleAfter,

        /// <summary>Bước 2: Cân có tín hiệu, đang chờ ổn định.</summary>
        StabilizingAfter,

        /// <summary>Đã đo xong cả 2 mẫu, sẵn sàng ghi kết quả.</summary>
        ReadyToRecord,

        /// <summary>Lần cân thứ 3: cân lại lần 1 hoặc lần 2 đã chọn.</summary>
        Adjusting
    }
}
