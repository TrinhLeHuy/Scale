using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.Queries;
using TTC.Weighing.Application.UseCases.Quality;
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
    private readonly DateTimePicker _dtpHistFrom = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 150 };
    private readonly DateTimePicker _dtpHistTo = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 150 };

    // ── Timer ──
    private readonly System.Windows.Forms.Timer _scaleTimer = new();
    private bool _scaleTimerInFlight;

    // ── Giả lập (null khi chạy với thiết bị thật) ──
    private readonly Development.SimulatedSampleScaleGateway? _simulatedScale;

    // ── Event bus ──
    private readonly TripEventBus _eventBus;
    private string? _lastQrContent;

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
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));  // Hàng trên: form + cân
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));  // Hàng dưới: lịch sử full width

        var scanCard = BuildScanCard();
        scanCard.Dock = DockStyle.Fill; // Fill hết row, không để trống
        scanCard.Margin = new Padding(0, 0, 1, 1);
        root.Controls.Add(scanCard, 0, 0);

        var impurityCard = BuildImpurityCard();
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
        _btnManualEntry.Click += (_, _) => ToggleManualEntry();
        _btnSubmitManual.Click += async (_, _) => await RecordImpurityManualAsync();
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
            GridBuilder.Column(Loc.T("col.sample_before"), nameof(QualitySampleRow.SampleBeforeKg), 95, "N3"),
            GridBuilder.Column(Loc.T("col.sample_after"), nameof(QualitySampleRow.SampleAfterKg), 95, "N3"),
            GridBuilder.Column(Loc.T("col.impurity_pct"), nameof(QualitySampleRow.ImpurityPercent), 90, "N4"),
            GridBuilder.Column(Loc.T("col.ccs"), nameof(QualitySampleRow.CcsValue), 80, "N2"),
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

        // Hiển thị số cân mẫu realtime
        FormLayout.AddFullWidth(table, _lblWorkflowStatus, 48);
        FormLayout.AddFullWidth(table, _lblScaleReading, 70);
        FormLayout.AddFullWidth(table, _lblStableProgress, 32);

        // Kết quả mẫu trước/sau
        var samplePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = false,
            Height = 36
        };
        _lblSampleBefore.Margin = new Padding(0, 6, 24, 0);
        _lblSampleAfter.Margin = new Padding(0, 6, 0, 0);
        samplePanel.Controls.Add(_lblSampleBefore);
        samplePanel.Controls.Add(_lblSampleAfter);
        FormLayout.AddFullWidth(table, samplePanel, 40);

        // Preview tạp chất
        _lblPreview.Dock = DockStyle.Fill;
        _lblPreview.Font = AgrisTheme.Body(10F, FontStyle.Bold);
        _lblPreview.TextAlign = ContentAlignment.MiddleLeft;
        FormLayout.AddFullWidth(table, _lblPreview, 36);

        // Nút hành động
        var actionBar = FormLayout.ActionBar(_btnRecordImpurity, _btnManualEntry, _btnPrintImpurity, _btnRetryDigi, _btnResetWorkflow);
        FormLayout.AddFullWidth(table, actionBar, 52);

        // Panel nhập tay (không hiện inline — dùng popup)
        // FormLayout.AddFullWidth(table, _pnlManualEntry, 290);

        // ── Giả lập cân mẫu (chỉ hiện khi chạy Preview mode) ──
        if (_simulatedScale is not null)
        {
            var simBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Height = 46,
                BackColor = AgrisTheme.WarningSoft,
                Padding = new Padding(4, 2, 4, 2)
            };

            var lblSim = new Label
            {
                Text = Loc.T("quality.sim_label"),
                AutoSize = true,
                Font = AgrisTheme.Body(9F, FontStyle.Bold),
                ForeColor = AgrisTheme.Warning,
                Margin = new Padding(0, 8, 4, 0)
            };

            var btnSim85 = GridBuilder.SecondaryButton(Loc.T("quality.sim_8_5"), new Size(130, 30));
            btnSim85.Click += (_, _) => _simulatedScale.SetWeight(8.500m);

            var btnSim0 = GridBuilder.SecondaryButton(Loc.T("quality.sim_0"), new Size(140, 30));
            btnSim0.Click += (_, _) => _simulatedScale.SetWeight(0m);

            var btnSim72 = GridBuilder.SecondaryButton(Loc.T("quality.sim_7_2"), new Size(130, 30));
            btnSim72.Click += (_, _) => _simulatedScale.SetWeight(7.200m);

            var btnSim50 = GridBuilder.SecondaryButton(Loc.T("quality.sim_5_0"), new Size(130, 30));
            btnSim50.Click += (_, _) => _simulatedScale.SetWeight(5.000m);

            simBar.Controls.AddRange([lblSim, btnSim85, btnSim0, btnSim72, btnSim50]);
            FormLayout.AddFullWidth(table, simBar, 42);
        }

        _btnRecordImpurity.Enabled = false;

        // Ẩn nút nhập tay nếu không có quyền
        _btnManualEntry.Visible = _currentUser.HasPermission(
            RecordImpuritySampleHandler.ManualEntryPermission, _siteId);

        // Bọc table để card tự co giãn theo nội dung
        table.Dock = DockStyle.Top;
        table.AutoSize = true;

        var card = FormLayout.TitledCard(
            Loc.T("quality.section_impurity"),
            table,
            $"Ngưỡng cảnh báo mẫu nhỏ theo tài liệu là {ImpurityCalculator.DocumentReferenceMinSampleKg} kg. " +
            "Đây là cảnh báo, không phải lỗi cứng. " +
            $"Cân mẫu tự động ghi khi ổn định ≥ {StableThresholdSeconds} giây.");

        card.AutoScroll = true;
        return card;
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
            Dock = DockStyle.Top, Height = 42,
            FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(4, 2, 4, 2)
        };
        var lblFrom = new Label { Text = "Từ:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) };
        _dtpHistFrom.Value = DateTime.Today.AddDays(-7);
        var lblTo = new Label { Text = "→", AutoSize = true, Margin = new Padding(4, 6, 4, 0) };
        _dtpHistTo.Value = DateTime.Today;
        filterBar.Controls.AddRange([lblFrom, _dtpHistFrom, lblTo, _dtpHistTo]);

        // Tự động load khi thay đổi khung ngày
        _dtpHistFrom.ValueChanged += async (_, _) => await LoadAllSamplesAsync();
        _dtpHistTo.ValueChanged += async (_, _) => await LoadAllSamplesAsync();

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

        // Thứ tự: header (Top) → hint (Top) → filterBar (Top) → grid (Fill)
        card.Controls.Add(_grid);
        card.Controls.Add(filterBar);
        card.Controls.Add(hintLabel);
        card.Controls.Add(header);

        return card;
    }

    /// <summary>Load tất cả quality samples trong khung ngày (không chỉ 1 chuyến).</summary>
    private async Task LoadAllSamplesAsync()
    {
        try
        {
            var fromUtc = _dtpHistFrom.Value.Date.ToUniversalTime();
            var toUtc = _dtpHistTo.Value.Date.AddDays(1).ToUniversalTime();

            var samples = await _tripDetail.ListAllSamplesAsync(_siteId, fromUtc, toUtc, _cts.Token);
            GridBuilder.BindAndColorize(_grid, samples, r => r.Status.ToString());
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

    private async Task ScanQrAsync(string qrContent)
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
            _txtCommodity.Text = result.Commodity ?? "—";

            _lblQrStatus.Text = Loc.T("quality.scan_ok", result.TripNo, result.Plate);
            _lblQrStatus.ForeColor = AgrisTheme.Success;

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

    // ═══════════════════════════════════════════════════════════
    //  Auto-weigh workflow 2 bước
    // ═══════════════════════════════════════════════════════════

    private void StartAutoWeighWorkflow()
    {
        _sampleBeforeKg = null;
        _sampleAfterKg = null;
        _lblSampleBefore.Text = Loc.T("quality.sample_before");
        _lblSampleAfter.Text = Loc.T("quality.sample_after");
        _lblPreview.Text = string.Empty;
        _btnRecordImpurity.Enabled = false;

        SetWorkflowStep(ImpurityWorkflowStep.WaitingSampleBefore);
        _scaleTimer.Start();
    }

    private void SetWorkflowStep(ImpurityWorkflowStep step)
    {
        _workflowStep = step;

        (_lblWorkflowStatus.Text, _lblWorkflowStatus.ForeColor) = step switch
        {
            ImpurityWorkflowStep.WaitingForQr =>
                (Loc.T("quality.workflow_waiting_qr"), AgrisTheme.TextMuted),
            ImpurityWorkflowStep.WaitingSampleBefore =>
                (Loc.T("quality.workflow_step1_place"), AgrisTheme.Info),
            ImpurityWorkflowStep.StabilizingBefore =>
                (Loc.T("quality.workflow_step1_stabilizing"), AgrisTheme.Warning),
            ImpurityWorkflowStep.RecordedBefore =>
                (Loc.T("quality.workflow_step1_done"), AgrisTheme.Info),
            ImpurityWorkflowStep.WaitingSampleAfter =>
                (Loc.T("quality.workflow_step2_place"), AgrisTheme.Info),
            ImpurityWorkflowStep.StabilizingAfter =>
                (Loc.T("quality.workflow_step2_stabilizing"), AgrisTheme.Warning),
            ImpurityWorkflowStep.ReadyToRecord =>
                (Loc.T("quality.workflow_ready"), AgrisTheme.Success),
            _ => ("", AgrisTheme.TextMuted)
        };
    }

    private async void OnScaleTimerTick(object? sender, EventArgs e)
    {
        if (_currentTripId is null || _scaleTimerInFlight) return;
        if (_workflowStep is ImpurityWorkflowStep.ReadyToRecord
            or ImpurityWorkflowStep.WaitingForQr) return;

        _scaleTimerInFlight = true;
        try
        {
            var view = await _presenter.ReadSampleScaleAsync(_sampleScaleId, _cts.Token);
            UpdateScaleDisplay(view);
            ProcessAutoWeighStep(view);
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

        _lblScaleReading.Text = $"{view.WeightKg:N3} kg";
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

    private void ProcessAutoWeighStep(SampleScaleView view)
    {
        if (view.WeightKg is null || !view.SignalFresh) return;

        var isStableEnough = view.IsStable
            && view.StableDuration.TotalSeconds >= StableThresholdSeconds;

        switch (_workflowStep)
        {
            case ImpurityWorkflowStep.WaitingSampleBefore:
                if (view.IsStable && view.WeightKg > 0)
                    SetWorkflowStep(ImpurityWorkflowStep.StabilizingBefore);
                break;

            case ImpurityWorkflowStep.StabilizingBefore:
                if (!view.IsStable || view.WeightKg <= 0)
                {
                    SetWorkflowStep(ImpurityWorkflowStep.WaitingSampleBefore);
                    break;
                }

                if (isStableEnough)
                {
                    _sampleBeforeKg = view.WeightKg.Value;
                    _lblSampleBefore.Text = Loc.T("quality.sample_before_value", _sampleBeforeKg.Value.ToString("N3"));
                    _lblSampleBefore.ForeColor = AgrisTheme.Success;
                    SetWorkflowStep(ImpurityWorkflowStep.RecordedBefore);
                }
                break;

            case ImpurityWorkflowStep.RecordedBefore:
                // Chờ nhân viên lấy mẫu 1 ra (cân giảm về gần 0) rồi đặt mẫu 2
                if (view.WeightKg < _sampleBeforeKg * 0.3m)
                {
                    SetWorkflowStep(ImpurityWorkflowStep.WaitingSampleAfter);
                }
                else
                {
                    // Hiển thị trạng thái chờ — nhân viên chưa lấy mẫu ra
                    var threshold = _sampleBeforeKg.Value * 0.3m;
                    _lblStableProgress.Text = Loc.T("quality.wait_remove_sample", view.WeightKg.Value.ToString("N3"), threshold.ToString("N3"));
                    _lblStableProgress.ForeColor = AgrisTheme.Warning;
                }
                break;

            case ImpurityWorkflowStep.WaitingSampleAfter:
                if (view.IsStable && view.WeightKg > 0)
                    SetWorkflowStep(ImpurityWorkflowStep.StabilizingAfter);
                break;

            case ImpurityWorkflowStep.StabilizingAfter:
                if (!view.IsStable || view.WeightKg <= 0)
                {
                    SetWorkflowStep(ImpurityWorkflowStep.WaitingSampleAfter);
                    break;
                }

                if (isStableEnough)
                {
                    _sampleAfterKg = view.WeightKg.Value;
                    _lblSampleAfter.Text = Loc.T("quality.sample_after_value", _sampleAfterKg.Value.ToString("N3"));
                    _lblSampleAfter.ForeColor = AgrisTheme.Success;

                    _scaleTimer.Stop();
                    FinalizeAutoWeigh();
                }
                break;
        }
    }

    private void FinalizeAutoWeigh()
    {
        if (_sampleBeforeKg is null || _sampleAfterKg is null) return;

        var calculation = ImpurityCalculator.Calculate(_sampleBeforeKg.Value, _sampleAfterKg.Value);

        if (!calculation.IsValid)
        {
            _lblPreview.Text = Loc.T("quality.preview_invalid", calculation.InvalidReason);
            _lblPreview.ForeColor = AgrisTheme.Danger;
            _btnRecordImpurity.Enabled = false;

            // Mẫu sau > mẫu trước → từ chối, cần lấy lại mẫu
            MessageBox.Show(
                calculation.InvalidReason,
                Loc.T("quality.invalid_measurement_title"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);

            // Reset bước 2 để lấy lại mẫu
            _sampleAfterKg = null;
            _lblSampleAfter.Text = Loc.T("quality.sample_after");
            _lblSampleAfter.ForeColor = AgrisTheme.TextPrimary;
            SetWorkflowStep(ImpurityWorkflowStep.WaitingSampleAfter);
            _scaleTimer.Start();
            return;
        }

        _lblPreview.Text = Loc.T("quality.preview_result", $"{calculation.ImpurityPercent:N4}")
            + (calculation.BelowMinimumSampleWarning ? Loc.T("quality.preview_small_warning") : string.Empty);

        _lblPreview.ForeColor = calculation.BelowMinimumSampleWarning
            ? AgrisTheme.Warning
            : AgrisTheme.Success;

        _btnRecordImpurity.Enabled = true;
        SetWorkflowStep(ImpurityWorkflowStep.ReadyToRecord);
    }

    // ═══════════════════════════════════════════════════════════
    //  Record impurity (auto-weigh)
    // ═══════════════════════════════════════════════════════════

    private async Task RecordImpurityFromAutoWeighAsync()
    {
        if (_currentTripId is null || _sampleBeforeKg is null || _sampleAfterKg is null) return;

        _btnRecordImpurity.Enabled = false;
        try
        {
            var result = await _presenter.RecordImpurityAsync(
                new RecordImpuritySampleCommand(
                    TripId: _currentTripId.Value,
                    SampleBeforeKg: _sampleBeforeKg.Value,
                    SampleAfterKg: _sampleAfterKg.Value,
                    Source: "DEVICE",
                    CorrelationId: Guid.NewGuid().ToString("N")),
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

    private void ToggleManualEntry()
    {
        // Dừng auto-weigh khi nhập tay
        _scaleTimer.Stop();

        // Hiện popup nhập tay thay vì panel inline
        using var popup = new Form
        {
            Text = "📝 Nhập tay tạp chất",
            Size = new Size(780, 600),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = AgrisTheme.Surface,
        };

        var table = FormLayout.FieldTable();
        table.Dock = DockStyle.Top;
        table.Padding = new Padding(12, 8, 12, 8);

        var notice = GridBuilder.Notice(Loc.T("quality.manual_notice"));
        FormLayout.AddFullWidth(table, notice, 36);

        var numBefore = new NumericUpDown
        {
            Minimum = 0, Maximum = 99999.999m, DecimalPlaces = 3,
            Increment = 0.1m, Dock = DockStyle.Fill,
            Font = AgrisTheme.Body(12F, FontStyle.Bold),
            Value = _numManualBefore.Value
        };
        var numAfter = new NumericUpDown
        {
            Minimum = 0, Maximum = 99999.999m, DecimalPlaces = 3,
            Increment = 0.1m, Dock = DockStyle.Fill,
            Font = AgrisTheme.Body(12F, FontStyle.Bold),
            Value = _numManualAfter.Value
        };
        var txtReason = new TextBox
        {
            Dock = DockStyle.Fill, Font = AgrisTheme.Body(11F),
            Text = _txtManualReason.Text,
            PlaceholderText = _txtManualReason.PlaceholderText
        };
        var txtApproval = new TextBox
        {
            Dock = DockStyle.Fill, Font = AgrisTheme.Body(11F),
            Text = _txtManualApproval.Text,
            PlaceholderText = _txtManualApproval.PlaceholderText
        };

        // Preview tạp chất realtime
        var lblPreview = new Label
        {
            Dock = DockStyle.Fill, Font = AgrisTheme.Body(10F, FontStyle.Bold),
            ForeColor = AgrisTheme.BrandGreen, TextAlign = ContentAlignment.MiddleLeft,
            Text = ""
        };
        void UpdatePreview()
        {
            var calc = ImpurityCalculator.Calculate(numBefore.Value, numAfter.Value);
            lblPreview.Text = calc.IsValid
                ? $"Tạp chất dự kiến: {calc.ImpurityPercent:N4} %"
                : $"❌ {calc.InvalidReason}";
            lblPreview.ForeColor = calc.IsValid ? AgrisTheme.BrandGreen : AgrisTheme.Danger;
        }
        numBefore.ValueChanged += (_, _) => UpdatePreview();
        numAfter.ValueChanged += (_, _) => UpdatePreview();
        UpdatePreview();

        FormLayout.AddField(table, Loc.T("quality.manual_field_before"), numBefore);
        FormLayout.AddField(table, Loc.T("quality.manual_field_after"), numAfter);
        FormLayout.AddFullWidth(table, lblPreview, 36);
        FormLayout.AddField(table, Loc.T("field.reason"), txtReason);
        FormLayout.AddField(table, Loc.T("field.approval_ref"), txtApproval);

        var btnOk = GridBuilder.PrimaryButton(Loc.T("quality.btn_manual_record"), new Size(200, 42));
        btnOk.DialogResult = DialogResult.OK;
        var btnCancel = GridBuilder.SecondaryButton(Loc.T("common.cancel"), new Size(120, 42));
        btnCancel.DialogResult = DialogResult.Cancel;
        FormLayout.AddFullWidth(table, FormLayout.ActionBar(btnOk, btnCancel), FormLayout.ActionRowHeight);

        popup.Controls.Add(table);
        popup.AcceptButton = btnOk;
        popup.CancelButton = btnCancel;

        if (popup.ShowDialog(this) == DialogResult.OK)
        {
            // Sync values vào form fields rồi gọi record
            _numManualBefore.Value = numBefore.Value;
            _numManualAfter.Value = numAfter.Value;
            _txtManualReason.Text = txtReason.Text;
            _txtManualApproval.Text = txtApproval.Text;
            _ = RecordImpurityManualAsync();
        }
        else
        {
            // Resume auto-weigh nếu đang trong workflow
            if (_currentTripId is not null
                && _workflowStep is not ImpurityWorkflowStep.ReadyToRecord
                    and not ImpurityWorkflowStep.WaitingForQr)
            {
                _scaleTimer.Start();
            }
        }
    }

    private void UpdateManualPreview()
    {
        var calculation = ImpurityCalculator.Calculate(_numManualBefore.Value, _numManualAfter.Value);

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

    private async Task RecordImpurityManualAsync()
    {
        if (_currentTripId is null)
        {
            MessageBox.Show(Loc.T("quality.no_trip"), Loc.T("quality.no_trip_title"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(_txtManualReason.Text))
        {
            MessageBox.Show(Loc.T("quality.manual_reason_required"), Loc.T("quality.manual_reason_title"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtManualReason.Focus();
            return;
        }

        // Mã phê duyệt optional — khi demo offline có thể bỏ trống

        _btnSubmitManual.Enabled = false;
        try
        {
            var result = await _presenter.RecordImpurityAsync(
                new RecordImpuritySampleCommand(
                    TripId: _currentTripId.Value,
                    SampleBeforeKg: _numManualBefore.Value,
                    SampleAfterKg: _numManualAfter.Value,
                    Source: "MANUAL",
                    CorrelationId: Guid.NewGuid().ToString("N"),
                    ApprovalRef: _txtManualApproval.Text.Trim(),
                    Reason: _txtManualReason.Text.Trim()),
                _cts.Token);

            HandleImpurityResult(result);
        }
        catch (Exception ex)
        {
            ShowError(Loc.T("quality.manual_record_error"), ex);
        }
        finally
        {
            _btnSubmitManual.Enabled = true;
        }
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
            var ldForPush = _presenter.FindLenhDonByPlate(_txtPlate.Text);
            var lenhDonId = _currentFrmLenhDonId ?? ldForPush?.SourceKey ?? shipment;
            var maLenhDon = _currentFrmMaLenhDon ?? ldForPush?.MaLD ?? shipment;
            var lenhDonBu = _currentFrmLenhDonBu ?? ldForPush?.BusinessUnit;

            var pushResult = await _presenter.PushTapChatAsync(
                lenhDonId, maLenhDon, "1", "1", DateTime.Now,
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
            var samples = detail?.QualitySamples ?? [];

            GridBuilder.BindAndColorize(_grid, samples, r => r.Status.ToString());
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
            await ScanQrAsync(tripNo);
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
                ? Loc.T("quality.sample_before_value", $"{impurityRow.SampleBeforeKg:N3}")
                : Loc.T("quality.sample_before");
            _lblSampleAfter.Text = impurityRow.SampleAfterKg.HasValue
                ? Loc.T("quality.sample_after_value", $"{impurityRow.SampleAfterKg:N3}")
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
        _lblWorkflowStatus.Text = $"📋 Xem lại chuyến {tripNo} — bấm Làm mới (Esc) để nhập mới";
        _lblWorkflowStatus.ForeColor = AgrisTheme.Info;
        SetWorkflowStep(ImpurityWorkflowStep.ReadyToRecord);
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

        _grid.DataSource = null;

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
            var ld = _presenter.FindLenhDonByPlate(plate);
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
                    $"Chuyến: {tripNo}\nBiển số: {plate}\nMẫu trước: {beforeKg:N3} kg | Mẫu sau: {afterKg:N3} kg",
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

        // GDI+ preview form giống phiếu đăng tài
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

        // Preview panel (GDI+)
        var pnlPreview = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(230, 230, 230),
            Padding = new Padding(15)
        };

        var receiptPanel = new DoubleBufferedPanel
        {
            Size = new Size(660, 620),
            BackColor = Color.White,
            Location = new Point(10, 10)
        };

        // Capture values for paint closure
        var capturedTripNo = tripNo;
        var capturedPlate = plate;
        var capturedOwner = owner;
        var capturedType = sugarcaneType;
        var capturedVariety = variety;
        var capturedCommodity = commodity;
        var capturedBefore = beforeKg;
        var capturedAfter = afterKg;
        var capturedImpurity = impurityPct;
        var capturedCcs = ccsValue;
        var capturedSampleTime = sampleTime;

        receiptPanel.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var pageWidth = receiptPanel.Width;
            var pageHeight = receiptPanel.Height;
            var margin = 24;
            var contentWidth = pageWidth - margin * 2;
            var y = margin;

            // ── Viền ──
            using var borderPen = new Pen(AgrisTheme.BrandGreenDark, 2f);
            g.DrawRectangle(borderPen, 2, 2, pageWidth - 4, pageHeight - 4);

            // ── Tiêu đề ──
            using var titleFont = new Font(AgrisTheme.FontFamily, 18F, FontStyle.Bold);
            using var titleBrush = new SolidBrush(AgrisTheme.BrandGreenDark);
            var titleText = Loc.T("quality.print_title");
            var titleSz = g.MeasureString(titleText, titleFont);
            g.DrawString(titleText, titleFont, titleBrush,
                margin + (contentWidth - titleSz.Width) / 2, y);
            y += (int)titleSz.Height + 14;

            // ── Thông tin chuyến ──
            using var labelFont = new Font(AgrisTheme.FontFamily, 11F, FontStyle.Regular);
            using var valueFont = new Font(AgrisTheme.FontFamily, 11F, FontStyle.Bold);
            using var textBrush = new SolidBrush(AgrisTheme.TextPrimary);
            var lx = margin;
            var vx = margin + 160;
            var lh = 28;

            DrawFieldLocal(g, Loc.T("quality.print_label_reg_no"), capturedTripNo, labelFont, valueFont, textBrush, lx, vx, ref y, lh);
            DrawFieldLocal(g, Loc.T("quality.print_label_plate"), capturedPlate, labelFont, valueFont, textBrush, lx, vx, ref y, lh);
            DrawFieldLocal(g, Loc.T("quality.print_label_owner"), capturedOwner, labelFont, valueFont, textBrush, lx, vx, ref y, lh);
            DrawFieldLocal(g, Loc.T("quality.print_label_sugarcane_type"), capturedType, labelFont, valueFont, textBrush, lx, vx, ref y, lh);
            DrawFieldLocal(g, Loc.T("quality.print_label_variety"), capturedVariety, labelFont, valueFont, textBrush, lx, vx, ref y, lh);

            // ── Separator ──
            y += 6;
            using var sepPen = new Pen(AgrisTheme.Border, 1f);
            g.DrawLine(sepPen, margin, y, pageWidth - margin, y);
            y += 10;

            // ── Kết quả tạp chất — in lớn ──
            using var bigFont = new Font(AgrisTheme.FontFamily, 14F, FontStyle.Bold);

            DrawFieldLocal(g, Loc.T("quality.print_label_before"),
                capturedBefore.HasValue ? $"{capturedBefore:N3} kg" : "—",
                labelFont, bigFont, textBrush, lx, vx, ref y, 32);

            DrawFieldLocal(g, Loc.T("quality.print_label_after"),
                capturedAfter.HasValue ? $"{capturedAfter:N3} kg" : "—",
                labelFont, bigFont, textBrush, lx, vx, ref y, 32);

            // Tạp chất — nổi bật
            using var impurityFont = new Font(AgrisTheme.FontFamily, 18F, FontStyle.Bold);
            using var impurityBrush = new SolidBrush(
                capturedImpurity.HasValue ? AgrisTheme.Danger : AgrisTheme.TextMuted);
            DrawFieldLocal(g, Loc.T("quality.print_label_impurity"),
                capturedImpurity.HasValue ? $"{capturedImpurity:N4} %" : "—",
                labelFont, impurityFont, impurityBrush, lx, vx, ref y, 36);

            // CCS nếu có
            if (capturedCcs.HasValue)
            {
                y += 4;
                DrawFieldLocal(g, Loc.T("receipt.field_ccs"),
                    $"{capturedCcs:N2}",
                    labelFont, bigFont, textBrush, lx, vx, ref y, 32);
            }

            // ── Separator ──
            y += 6;
            g.DrawLine(sepPen, margin, y, pageWidth - margin, y);
            y += 10;

            // ── Thời gian đo ──
            var timeStr = capturedSampleTime.HasValue
                ? capturedSampleTime.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss")
                : DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
            DrawFieldLocal(g, Loc.T("quality.print_label_time"), timeStr,
                labelFont, valueFont, textBrush, lx, vx, ref y, lh);

            // ── Barcode ──
            if (!string.IsNullOrWhiteSpace(capturedTripNo))
            {
                y += 10;
                Printing.Code128Renderer.Draw(g, capturedTripNo, lx, y, 320, 50);
                y += 54;
                using var bcFont = new Font(AgrisTheme.MonoFamily, 10F);
                g.DrawString(capturedTripNo, bcFont, Brushes.Black, lx, y);
            }

            // ── Ngày in — bottom right ──
            var printLabel = Loc.T("print.dangtai_ngay_in", DateTime.Now.ToString("dd/MM/yyyy HH:mm"));
            using var tsFont = new Font(AgrisTheme.FontFamily, 9.5F, FontStyle.Italic);
            using var tsBrush = new SolidBrush(AgrisTheme.TextMuted);
            var tsSz = g.MeasureString(printLabel, tsFont);
            g.DrawString(printLabel, tsFont, tsBrush,
                pageWidth - margin - tsSz.Width, pageHeight - margin - tsSz.Height - 4);
        };

        pnlPreview.Controls.Add(receiptPanel);

        // ── Toolbar ──
        var btnPrint = GridBuilder.PrimaryButton(Loc.T("common.print"), new Size(120, 36));
        var btnClose = GridBuilder.SecondaryButton(Loc.T("common.close"), new Size(100, 36));

        btnPrint.Click += (_, _) =>
        {
            try
            {
                var printContent = BuildImpurityPrintContent(
                    capturedTripNo, capturedPlate, capturedOwner, capturedType, capturedVariety,
                    capturedBefore, capturedAfter, capturedImpurity, capturedCcs, capturedSampleTime);

                var printDoc = new System.Drawing.Printing.PrintDocument();
                printDoc.PrintPage += (s, pe) =>
                {
                    pe.Graphics!.DrawString(printContent, AgrisTheme.Mono(10F), Brushes.Black,
                        new RectangleF(40, 40, pe.PageBounds.Width - 80, pe.PageBounds.Height - 80));
                };
                var dialog = new PrintDialog { Document = printDoc };
                if (dialog.ShowDialog() == DialogResult.OK)
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

    /// <summary>Build nội dung text cho PrintDocument (in thật qua máy in).</summary>
    private static string BuildImpurityPrintContent(
        string? tripNo, string? plate, string? owner, string? sugarcaneType, string? variety,
        decimal? beforeKg, decimal? afterKg, decimal? impurityPct, decimal? ccsValue,
        DateTimeOffset? sampleTime)
    {
        var lblRegNo = Loc.T("quality.print_label_reg_no");
        var lblPlate = Loc.T("quality.print_label_plate");
        var lblOwner = Loc.T("quality.print_label_owner");
        var lblType = Loc.T("quality.print_label_sugarcane_type");
        var lblVariety = Loc.T("quality.print_label_variety");
        var lblBefore = Loc.T("quality.print_label_before");
        var lblAfter = Loc.T("quality.print_label_after");
        var lblImpurity = Loc.T("quality.print_label_impurity");
        var lblTime = Loc.T("quality.print_label_time");
        var timeStr = sampleTime.HasValue
            ? sampleTime.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss")
            : DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");

        return $"""
        {Loc.T("quality.print_title")}
        ═══════════════════════════════════════
        {lblRegNo,-14}: {tripNo}
        {lblPlate,-14}: {plate}
        {lblOwner,-14}: {owner}
        {lblType,-14}: {sugarcaneType}
        {lblVariety,-14}: {variety}
        ───────────────────────────────────────
        {lblBefore,-14}: {(beforeKg.HasValue ? $"{beforeKg:N3} kg" : "—")}
        {lblAfter,-14}: {(afterKg.HasValue ? $"{afterKg:N3} kg" : "—")}
        {lblImpurity,-14}: {(impurityPct.HasValue ? $"{impurityPct:N4} %" : "—")}
        {(ccsValue.HasValue ? $"\n        CCS            : {ccsValue:N2}" : "")}
        ───────────────────────────────────────
        {lblTime,-14}: {timeStr}
        ═══════════════════════════════════════
        """;
    }

    private static void DrawFieldLocal(
        Graphics g, string label, string? value,
        Font labelFont, Font valueFont, Brush brush,
        int labelX, int valueX, ref int y, int lineHeight)
    {
        var displayValue = string.IsNullOrWhiteSpace(value) ? "—" : value;
        g.DrawString(label, labelFont, brush, labelX, y);
        g.DrawString(": " + displayValue, valueFont, brush, valueX, y);
        y += lineHeight;
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
        DecimalPlaces = 3,
        Maximum = 100_000m,
        Minimum = 0m,
        Increment = 0.5m,
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
        ReadyToRecord
    }
}
