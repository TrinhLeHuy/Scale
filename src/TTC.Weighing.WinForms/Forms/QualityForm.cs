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
    private readonly Guid _siteId;
    private readonly Guid _sampleScaleId;
    private readonly CancellationTokenSource _cts = new();

    // ── Trạng thái workflow ──
    private Guid? _currentTripId;
    private ImpurityWorkflowStep _workflowStep = ImpurityWorkflowStep.WaitingForQr;
    private decimal? _sampleBeforeKg;
    private decimal? _sampleAfterKg;

    // ── QR Scan ──
    private readonly TextBox _txtBarcode = new()
    {
        Dock = DockStyle.Fill,
        Font = AgrisTheme.Mono(12F),
        PlaceholderText = Loc.T("quality.scan_placeholder"),
        AccessibleName = "Barcode scan input"
    };

    // ── Thông tin chuyến (read-only) ──
    private readonly TextBox _txtPlate = ReadOnlyField(Loc.T("field.plate"));
    private readonly TextBox _txtOwner = ReadOnlyField(Loc.T("field.sugarcane_owner"));
    private readonly TextBox _txtSugarcaneType = ReadOnlyField(Loc.T("field.sugarcane_type"));
    private readonly TextBox _txtSugarcaneVariety = ReadOnlyField(Loc.T("field.sugarcane_variety"));
    private readonly TextBox _txtTripNo = ReadOnlyField(Loc.T("field.trip_no"));
    private readonly TextBox _txtCommodity = ReadOnlyField(Loc.T("field.commodity"));
    private readonly Label _lblQrStatus = new()
    {
        Text = Loc.T("quality.qr_start"),
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
        Height = 40,
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
        Height = 24,
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
        AutoSize = true,
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
        ISiteContext siteContext,
        ISampleScaleGateway sampleScaleGateway,
        TripEventBus eventBus)
    {
        _presenter = presenter;
        _currentUser = currentUser;
        _tripDetail = tripDetail;
        _siteId = siteContext.CurrentSiteId;
        _simulatedScale = sampleScaleGateway as Development.SimulatedSampleScaleGateway;
        _eventBus = eventBus;

        // SampleScaleId lấy từ cấu hình site. Dùng deterministic GUID dựa trên SiteId
        // cho đến khi D-chưa duyệt chốt cơ chế ánh xạ thiết bị.
        _sampleScaleId = _siteId;

        Text = Loc.T("quality.title");
        MinimumSize = new Size(980, 760);
        BackColor = AgrisTheme.Canvas;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        BuildLayout();
        WireEvents();

        _scaleTimer.Interval = (int)ScalePollInterval.TotalMilliseconds;
        _scaleTimer.Tick += OnScaleTimerTick;

        Load += (_, _) => _txtBarcode.Focus();

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

        // Layout 3 cột: Thông tin xe 35% | Cân tạp chất 40% | CCS & Lịch sử 25%
        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = AgrisTheme.Canvas,
            Padding = new Padding(8)
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        top.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        top.Controls.Add(BuildScanCard(), 0, 0);
        top.Controls.Add(BuildImpurityCard(), 1, 0);

        // Cột phải: CCS trên + Lịch sử dưới
        var rightPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        rightPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rightPanel.Controls.Add(BuildCcsCard(), 0, 0);
        rightPanel.Controls.Add(BuildHistoryCard(), 0, 1);

        top.Controls.Add(rightPanel, 2, 0);

        Controls.Add(top);
    }

    private void WireEvents()
    {
        _txtBarcode.KeyDown += OnBarcodeKeyDown;
        _btnRecordImpurity.Click += async (_, _) => await RecordImpurityFromAutoWeighAsync();
        _btnResetWorkflow.Click += (_, _) => ResetWorkflow();
        _btnContinue.Click += OnContinueClicked;
        _btnManualEntry.Click += (_, _) => ToggleManualEntry();
        _btnSubmitManual.Click += async (_, _) => await RecordImpurityManualAsync();
        _numManualBefore.ValueChanged += (_, _) => UpdateManualPreview();
        _numManualAfter.ValueChanged += (_, _) => UpdateManualPreview();
    }

    private void ConfigureGrid()
    {
        _grid.Columns.AddRange(
            GridBuilder.Column(Loc.T("col.type"), nameof(QualitySampleRow.Type), 80),
            GridBuilder.Column(Loc.T("col.status"), nameof(QualitySampleRow.Status), 85),
            GridBuilder.Column(Loc.T("col.revision"), nameof(QualitySampleRow.Revision), 50),
            GridBuilder.Column(Loc.T("col.effective"), nameof(QualitySampleRow.IsEffective), 60),
            GridBuilder.Column(Loc.T("col.sample_before"), nameof(QualitySampleRow.SampleBeforeKg), 95, "N3"),
            GridBuilder.Column(Loc.T("col.sample_after"), nameof(QualitySampleRow.SampleAfterKg), 95, "N3"),
            GridBuilder.Column(Loc.T("col.impurity_pct"), nameof(QualitySampleRow.ImpurityPercent), 90, "N4"),
            GridBuilder.Column(Loc.T("col.ccs"), nameof(QualitySampleRow.CcsValue), 80, "N2"),
            GridBuilder.Column(Loc.T("col.small_sample_warning"),
                nameof(QualitySampleRow.BelowMinimumSampleWarning), 105),
            GridBuilder.Column(Loc.T("col.source"), nameof(QualitySampleRow.Source), 70),
            GridBuilder.Column(Loc.T("col.received_at"), nameof(QualitySampleRow.ReceivedAt), 120));

        GridBuilder.AttachEmptyState(_grid, Loc.T("quality.history_empty"));
    }

    /// <summary>Card quét QR + hiển thị thông tin chuyến mía.</summary>
    private Card BuildScanCard()
    {
        var table = FormLayout.FieldTable();

        FormLayout.AddField(table, Loc.T("field.scan_qr"), _txtBarcode);
        FormLayout.AddFullWidth(table, _lblQrStatus, 30);
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
        FormLayout.AddFullWidth(table, _lblWorkflowStatus, 40);
        FormLayout.AddFullWidth(table, _lblScaleReading, 64);
        FormLayout.AddFullWidth(table, _lblStableProgress, 26);

        // Kết quả mẫu trước/sau
        var samplePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = false,
            Height = 30
        };
        _lblSampleBefore.Margin = new Padding(0, 6, 24, 0);
        _lblSampleAfter.Margin = new Padding(0, 6, 0, 0);
        samplePanel.Controls.Add(_lblSampleBefore);
        samplePanel.Controls.Add(_lblSampleAfter);
        FormLayout.AddFullWidth(table, samplePanel, 34);

        // Preview tạp chất
        _lblPreview.Dock = DockStyle.Fill;
        _lblPreview.Font = AgrisTheme.Body(10F, FontStyle.Bold);
        _lblPreview.TextAlign = ContentAlignment.MiddleLeft;
        FormLayout.AddFullWidth(table, _lblPreview, 30);

        // Nút hành động
        var actionBar = FormLayout.ActionBar(_btnRecordImpurity, _btnManualEntry, _btnContinue, _btnResetWorkflow);
        FormLayout.AddFullWidth(table, actionBar, 52);

        // Panel nhập tay (ẩn mặc định)
        FormLayout.AddFullWidth(table, _pnlManualEntry, 320);

        // ── Giả lập cân mẫu (chỉ hiện khi chạy Preview mode) ──
        if (_simulatedScale is not null)
        {
            var simBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Height = 38,
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

        card.AutoSize = true;
        card.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        return card;
    }

    private void BuildManualEntryPanel()
    {
        var table = FormLayout.FieldTable();

        FormLayout.AddField(table, Loc.T("quality.manual_field_before"), _numManualBefore);
        FormLayout.AddField(table, Loc.T("quality.manual_field_after"), _numManualAfter);
        FormLayout.AddField(table, Loc.T("field.reason"), _txtManualReason);
        FormLayout.AddField(table, Loc.T("field.approval_ref"), _txtManualApproval);

        // Nút ghi nhập tay — đặt ngay sau các field
        FormLayout.AddFullWidth(table, FormLayout.ActionBar(_btnSubmitManual), 48);

        var notice = GridBuilder.Notice(Loc.T("quality.manual_notice"));

        // Layout: notice (Top) → table with fields + button (Fill)
        table.Dock = DockStyle.Top;
        table.AutoSize = true;
        notice.Dock = DockStyle.Top;

        _pnlManualEntry.Controls.Add(table);
        _pnlManualEntry.Controls.Add(notice);
    }

    private Card BuildCcsCard()
    {
        var table = FormLayout.FieldTable();

        FormLayout.AddField(table, Loc.T("field.ccs_value"), _numCcs);

        _chkCcsPending.Text = Loc.T("quality.ccs_pending_label");
        _chkCcsPending.AutoSize = true;
        _chkCcsPending.CheckedChanged += (_, _) => _numCcs.Enabled = !_chkCcsPending.Checked;
        FormLayout.AddFullWidth(table, _chkCcsPending, 30);

        _btnRecordCcs.Click += async (_, _) => await RecordCcsAsync();
        FormLayout.AddFullWidth(table, FormLayout.ActionBar(_btnRecordCcs), 52);

        return FormLayout.TitledCard(
            Loc.T("quality.section_ccs"),
            table,
            Loc.T("quality.ccs_section_notice"));
    }

    private Card BuildHistoryCard()
        => FormLayout.TitledCard(Loc.T("quality.section_history"), _grid);

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
        _pnlManualEntry.Visible = !_pnlManualEntry.Visible;

        if (_pnlManualEntry.Visible)
        {
            // Dừng auto-weigh khi chuyển sang nhập tay
            _scaleTimer.Stop();
            _btnManualEntry.Text = Loc.T("quality.btn_hide_manual");
        }
        else
        {
            _btnManualEntry.Text = Loc.T("quality.btn_manual");
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

    private void HandleImpurityResult(RecordImpuritySampleResult result)
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

        MessageBox.Show(Loc.T("quality.record_ok", $"{result.ImpurityPercent:N4}") + warning,
            Loc.T("quality.record_ok_title"), MessageBoxButtons.OK, MessageBoxIcon.Information);

        _ = LoadSamplesAsync();

        // Thông báo các tab khác → MainForm tự nhảy sang Kho vận
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
        _numManualBefore.Value = 0;
        _numManualAfter.Value = 0;
        _txtManualReason.Clear();
        _txtManualApproval.Clear();

        _grid.DataSource = null;

        SetWorkflowStep(ImpurityWorkflowStep.WaitingForQr);
        _txtBarcode.Focus();
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
