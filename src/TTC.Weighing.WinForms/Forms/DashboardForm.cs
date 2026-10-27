using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.Queries;
using TTC.Weighing.WinForms.Controls;
using TTC.Weighing.WinForms.Helpers;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Services;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Forms;

/// <summary>
/// UI-01 — Tổng quan. Vẽ luồng nghiệp vụ theo vòng đời chuyến (02_Quy_tac 2.1) kèm các chỉ số
/// quan trọng: xe chờ, trong nhà máy, cân 1 chưa cân 2, chuyến PAUSED, chặn đối soát,
/// tích hợp cần xử lý (R-028).
/// </summary>
public sealed class DashboardForm : Form
{
    /// <summary>Giảm chớp nháy khi form render lần đầu trong tab.</summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style |= 0x02000000; // WS_CLIPCHILDREN
            return cp;
        }
    }
    private readonly IDashboardQueries _queries;
    private readonly ICurrentUser _user;
    private readonly TripEventBus _eventBus;
    private readonly Guid _siteId;
    private readonly CancellationTokenSource _cts = new();

    // Chặng của luồng nghiệp vụ
    private readonly PipelineStageBox _stageTemp = new(Loc.T("dashboard.stage_temp"), Loc.T("dashboard.stage_temp_desc"))
    { AttentionColor = AgrisTheme.Warning };
    private readonly PipelineStageBox _stageRegistered = new(Loc.T("dashboard.stage_registered"), Loc.T("dashboard.stage_registered_desc"));
    private readonly PipelineStageBox _stageInProgress = new(Loc.T("dashboard.stage_in_progress"), Loc.T("dashboard.stage_in_progress_desc"));
    private readonly PipelineStageBox _stageWeighed = new(Loc.T("dashboard.stage_weighed"), Loc.T("dashboard.stage_weighed_desc"));
    private readonly PipelineStageBox _stageReconciled = new(Loc.T("dashboard.stage_reconciled"), Loc.T("dashboard.stage_reconciled_desc"));
    private readonly PipelineStageBox _stagePosted = new(Loc.T("dashboard.stage_posted"), Loc.T("dashboard.stage_posted_desc"));
    private readonly PipelineStageBox _stageClosed = new(Loc.T("dashboard.stage_closed"), Loc.T("dashboard.stage_closed_desc")) { IsLast = true };

    // Chỉ số quan trọng
    private readonly MetricTile _tileWaiting = new(Loc.T("dashboard.tile_waiting"), AgrisTheme.Info);
    private readonly MetricTile _tileInside = new(Loc.T("dashboard.tile_inside"), AgrisTheme.BrandTeal);
    private readonly MetricTile _tileFirstOnly = new(Loc.T("dashboard.tile_first_only"), AgrisTheme.Warning);
    private readonly MetricTile _tilePaused = new(Loc.T("dashboard.tile_paused"), AgrisTheme.Danger);
    private readonly MetricTile _tileReconciliation = new(Loc.T("dashboard.tile_reconciliation"), AgrisTheme.Danger);
    private readonly MetricTile _tileIntegration = new(Loc.T("dashboard.tile_integration"), AgrisTheme.Danger);
    private readonly MetricTile _tileNet = new(Loc.T("dashboard.tile_net"), AgrisTheme.BrandGreen, "kg");
    private readonly MetricTile _tileManual = new(Loc.T("dashboard.tile_manual"), AgrisTheme.Warning, "lượt cân");

    private readonly DataGridView _tripGrid = GridBuilder.ReadOnlyGrid(Loc.T("dashboard.trip_section"));
    private readonly DataGridView _deviceGrid = GridBuilder.ReadOnlyGrid(Loc.T("dashboard.device_section"));
    private readonly CheckBox _chkFirstWeightOnly = new();
    private readonly Button _btnRefresh = GridBuilder.SecondaryButton(Loc.T("common.refresh"), new Size(110, 30));
    private readonly Label _lblUpdatedAt = new();

    // ── Tách lưới Mía / Vật tư + xuất Excel ──
    private readonly TripCategorySwitch _tripCategory = new("dashboard.trips");
    private readonly Button _btnExportExcel = GridBuilder.ExportExcelButton();
    private readonly Label _lblTripCount = new();
    private IReadOnlyList<TripBoardRow> _tripRows = [];

    public event Action<Guid>? TripSelected;

    public DashboardForm(IDashboardQueries queries, ICurrentUser user, ISiteContext siteContext,
        TripEventBus eventBus)
    {
        _queries = queries;
        _user = user;
        _eventBus = eventBus;
        _siteId = siteContext.CurrentSiteId;

        Text = Loc.T("dashboard.title");
        MinimumSize = new Size(1100, 720);
        BackColor = AgrisTheme.Canvas;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        BuildLayout();
        Load += async (_, _) => await RefreshAsync();

        // Realtime: refresh khi bất kỳ tab nào thay đổi chuyến
        _eventBus.TripChanged += OnTripChangedFromOtherForm;

        // Đổi ngôn ngữ runtime → cập nhật tất cả chuỗi trên form
        Loc.Changed += OnLanguageChanged;
    }

    /// <summary>Cập nhật toàn bộ chuỗi hiển thị khi đổi ngôn ngữ runtime.</summary>
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (InvokeRequired) { Invoke(() => OnLanguageChanged(sender, e)); return; }

        Text = Loc.T("dashboard.title");

        // Pipeline stages
        _stageTemp.Title = Loc.T("dashboard.stage_temp");
        _stageTemp.Subtitle = Loc.T("dashboard.stage_temp_desc");
        _stageRegistered.Title = Loc.T("dashboard.stage_registered");
        _stageRegistered.Subtitle = Loc.T("dashboard.stage_registered_desc");
        _stageInProgress.Title = Loc.T("dashboard.stage_in_progress");
        _stageInProgress.Subtitle = Loc.T("dashboard.stage_in_progress_desc");
        _stageWeighed.Title = Loc.T("dashboard.stage_weighed");
        _stageWeighed.Subtitle = Loc.T("dashboard.stage_weighed_desc");
        _stageReconciled.Title = Loc.T("dashboard.stage_reconciled");
        _stageReconciled.Subtitle = Loc.T("dashboard.stage_reconciled_desc");
        _stagePosted.Title = Loc.T("dashboard.stage_posted");
        _stagePosted.Subtitle = Loc.T("dashboard.stage_posted_desc");
        _stageClosed.Title = Loc.T("dashboard.stage_closed");
        _stageClosed.Subtitle = Loc.T("dashboard.stage_closed_desc");

        // Metric tiles
        _tileWaiting.SetCaption(Loc.T("dashboard.tile_waiting"));
        _tileInside.SetCaption(Loc.T("dashboard.tile_inside"));
        _tileFirstOnly.SetCaption(Loc.T("dashboard.tile_first_only"));
        _tilePaused.SetCaption(Loc.T("dashboard.tile_paused"));
        _tileReconciliation.SetCaption(Loc.T("dashboard.tile_reconciliation"));
        _tileIntegration.SetCaption(Loc.T("dashboard.tile_integration"));
        _tileNet.SetCaption(Loc.T("dashboard.tile_net"));
        _tileManual.SetCaption(Loc.T("dashboard.tile_manual"));

        // Checkbox + button
        _chkFirstWeightOnly.Text = Loc.T("dashboard.filter_first_only");
        _btnRefresh.Text = Loc.T("common.refresh");

        // Grid column headers
        UpdateGridColumnHeaders(_tripGrid, [
            Loc.T("col.trip_no"), Loc.T("col.reference_no"), Loc.T("col.plate"),
            Loc.T("col.profile"), Loc.T("col.lifecycle"), Loc.T("col.execution"),
            Loc.T("col.first_weight"), Loc.T("col.second_weight"), Loc.T("col.net_weight")
        ]);
        ApplyWeightUnit();

        UpdateGridColumnHeaders(_deviceGrid, [
            Loc.T("col.device_code"), Loc.T("col.device_class"), Loc.T("col.lane"),
            Loc.T("col.enabled"), Loc.T("col.online"), Loc.T("col.last_seen")
        ]);

        // Repaint pipeline stages
        if (_pipelineTable is not null)
            foreach (Control c in _pipelineTable.Controls)
                c.Invalidate();

        Invalidate(true);
    }

    private static void UpdateGridColumnHeaders(DataGridView grid, string[] headers)
    {
        for (var i = 0; i < Math.Min(grid.Columns.Count, headers.Length); i++)
            grid.Columns[i].HeaderText = headers[i];
    }

    private void BuildLayout()
    {
        ConfigureTripGrid();
        ConfigureDeviceGrid();

        // Bố cục dọc: luồng nghiệp vụ -> chỉ số -> hai bảng.
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = AgrisTheme.Canvas,
            Padding = new Padding(12, 8, 12, 12)
        };

        // Chiều cao từng hàng tính từ cỡ chữ thật (FontScale 1.5) — trước đây đặt cứng 122/118/44px
        // nên số lớn trong thẻ và chặng bị cắt mất nửa dưới.
        var pipelineCard = BuildPipelineCard();
        var metricRow = BuildMetricPanel();
        var toolbar = BuildToolbar();

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, pipelineCard.Height + pipelineCard.Margin.Vertical));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, metricRow.Height + metricRow.Margin.Vertical));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, toolbar.Height + toolbar.Margin.Vertical));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(pipelineCard, 0, 0);
        root.Controls.Add(metricRow, 0, 1);
        root.Controls.Add(toolbar, 0, 2);
        root.Controls.Add(BuildTables(), 0, 3);

        Controls.Add(root);
    }

    /// <summary>Bảng 1 hàng chia đều cột — các thẻ giãn theo bề ngang màn hình, không cuộn ngang.</summary>
    private static TableLayoutPanel EqualColumns(Control[] items, int height, Color back)
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = items.Length,
            RowCount = 1,
            Height = height,
            BackColor = back,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        for (var i = 0; i < items.Length; i++)
        {
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / items.Length));
            items[i].Dock = DockStyle.Fill;
            table.Controls.Add(items[i], i, 0);
        }

        return table;
    }

    private Card BuildPipelineCard()
    {
        var card = new Card
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 12),
            Padding = new Padding(1)
        };

        var header = GridBuilder.SectionHeader(Loc.T("dashboard.pipeline_header"));

        var stages = new Control[]
        {
            _stageTemp, _stageRegistered, _stageInProgress, _stageWeighed,
            _stageReconciled, _stagePosted, _stageClosed
        };
        foreach (var stage in stages)
            stage.Margin = new Padding(0, 0, 3, 0);

        var row = EqualColumns(stages, PipelineStageBox.PreferredBoxHeight, AgrisTheme.Surface);
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 10, 14, 12),
            BackColor = AgrisTheme.Surface
        };
        host.Controls.Add(row);

        card.Controls.Add(host);
        card.Controls.Add(header);
        card.Height = header.Height + PipelineStageBox.PreferredBoxHeight + host.Padding.Vertical + card.Padding.Vertical;
        _pipelineTable = row;

        return card;
    }

    private TableLayoutPanel? _pipelineTable;

    private Control BuildMetricPanel()
    {
        var tiles = new Control[]
        {
            _tileWaiting, _tileInside, _tileFirstOnly, _tilePaused,
            _tileReconciliation, _tileIntegration, _tileNet, _tileManual
        };
        foreach (var tile in tiles)
            tile.Margin = new Padding(0, 0, 10, 0);
        _tileManual.Margin = Padding.Empty;

        var row = EqualColumns(tiles, MetricTile.PreferredTileHeight, AgrisTheme.Canvas);
        row.Margin = new Padding(0, 0, 0, 12);
        return row;
    }

    private Control BuildToolbar()
    {
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Height = 56,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = AgrisTheme.Surface,
            Padding = new Padding(14, 7, 14, 7),
            Margin = new Padding(0, 0, 0, 12)
        };

        _chkFirstWeightOnly.Text = Loc.T("dashboard.filter_first_only");
        _chkFirstWeightOnly.AutoSize = true;
        _chkFirstWeightOnly.Font = AgrisTheme.Body();
        _chkFirstWeightOnly.ForeColor = AgrisTheme.TextPrimary;
        _chkFirstWeightOnly.Cursor = Cursors.Hand;
        _chkFirstWeightOnly.Margin = new Padding(0, 8, 20, 0);
        _chkFirstWeightOnly.CheckedChanged += async (_, _) => await RefreshAsync();

        _btnRefresh.Margin = new Padding(0, 0, 20, 0);
        _btnRefresh.Click += async (_, _) => await RefreshAsync();

        _lblUpdatedAt.AutoSize = true;
        _lblUpdatedAt.Font = AgrisTheme.Caption();
        _lblUpdatedAt.ForeColor = AgrisTheme.TextMuted;
        _lblUpdatedAt.Margin = new Padding(0, 11, 0, 0);

        toolbar.Controls.AddRange([_chkFirstWeightOnly, _btnRefresh, _lblUpdatedAt]);

        // Viền mảnh như các thẻ khác.
        toolbar.Paint += (_, e) =>
        {
            using var pen = new Pen(AgrisTheme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, toolbar.Width - 1, toolbar.Height - 1);
        };

        return toolbar;
    }

    private Control BuildTables()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 10,
            BackColor = AgrisTheme.Canvas
        };

        var tripCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(1) };
        var tripHeader = GridBuilder.SectionHeader(Loc.T("dashboard.trip_section"));

        _lblTripCount.AutoSize = true;
        _lblTripCount.Font = AgrisTheme.Body(9F, FontStyle.Bold);
        _lblTripCount.ForeColor = AgrisTheme.TextSecondary;
        _lblTripCount.Margin = new Padding(0, 10, 8, 0);

        _btnExportExcel.Click += async (_, _) => await ExportTripGridAsync();
        _tripCategory.CategoryChanged += async (_, _) =>
        {
            GridBuilder.ApplyCategoryHeader(tripCard, tripHeader, Loc.T("dashboard.trip_section"), _tripCategory.Category);
            ApplyWeightUnit();
            await RefreshAsync();
        };
        GridBuilder.ApplyCategoryHeader(tripCard, tripHeader, Loc.T("dashboard.trip_section"), _tripCategory.Category);

        var tripToolbar = new GridToolbar([_tripCategory], [_lblTripCount, _btnExportExcel]);

        // Thứ tự Add ngược thứ tự dock: lưới (Fill) → thanh công cụ → tiêu đề.
        tripCard.Controls.Add(_tripGrid);
        tripCard.Controls.Add(tripToolbar);
        tripCard.Controls.Add(tripHeader);

        var deviceCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(1) };
        deviceCard.Controls.Add(_deviceGrid);
        deviceCard.Controls.Add(GridBuilder.Notice(
            Loc.T("dashboard.device_notice")));
        deviceCard.Controls.Add(GridBuilder.SectionHeader(Loc.T("dashboard.device_section")));

        split.Panel1.Controls.Add(tripCard);
        split.Panel2.Controls.Add(deviceCard);
        split.SplitterDistance = 720;

        // Chia theo tỉ lệ 68/32 thay vì 720px cố định — màn rộng thì bảng thiết bị không bị
        // bóp còn các cột 1–2 ký tự. Người dùng kéo vách ngăn rồi thì giữ nguyên lựa chọn đó.
        var userMoved = false;
        split.SplitterMoved += (_, _) => { if (split.Focused) userMoved = true; };
        split.SizeChanged += (_, _) =>
        {
            if (userMoved || split.Width <= 0) return;
            var target = (int)(split.Width * 0.68);
            if (target > split.Panel1MinSize && split.Width - target > split.Panel2MinSize)
                split.SplitterDistance = target;
        };

        return split;
    }

    private void ConfigureTripGrid()
    {
        _tripGrid.Columns.AddRange(
            GridBuilder.Column(Loc.T("col.trip_no"), nameof(TripBoardRow.TripNo), 120),
            GridBuilder.Column(Loc.T("col.reference_no"), nameof(TripBoardRow.ReferenceNo), 110),
            GridBuilder.Column(Loc.T("col.plate"), nameof(TripBoardRow.Plate), 90),
            GridBuilder.Column(Loc.T("col.profile"), nameof(TripBoardRow.ProfileCode), 130),
            GridBuilder.Column(Loc.T("col.lifecycle"), nameof(TripBoardRow.Lifecycle), 100),
            GridBuilder.Column(Loc.T("col.execution"), nameof(TripBoardRow.ExecutionStatus), 75),
            GridBuilder.Column(Loc.T("col.first_weight"), nameof(TripBoardRow.FirstWeightKg), 100, "N3"),
            GridBuilder.Column(Loc.T("col.second_weight"), nameof(TripBoardRow.SecondWeightKg), 100, "N3"),
            GridBuilder.Column(Loc.T("col.net_weight"), nameof(TripBoardRow.VehicleNetKg), 100, "N3"));

        ApplyWeightUnit();

        // Dữ liệu luôn là kg; mía hiển thị tấn (N3), vật tư hiển thị kg (N0).
        _tripGrid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (!IsWeightColumn(_tripGrid.Columns[e.ColumnIndex].DataPropertyName)) return;
            if (e.Value is not decimal kg) return;
            e.Value = IsUnitTon ? (kg / 1000m).ToString("N3") : kg.ToString("N0");
            e.FormattingApplied = true;
        };

        GridBuilder.AttachEmptyState(_tripGrid, Loc.T("dashboard.no_trips"));

        _tripGrid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            if (_tripGrid.Rows[e.RowIndex].DataBoundItem is TripBoardRow row)
                TripSelected?.Invoke(row.TripId);
        };
    }

    /// <summary>Mía = tấn, vật tư = kg.</summary>
    private bool IsUnitTon => _tripCategory.Category != TripCategory.Material;

    private static bool IsWeightColumn(string propertyName) => propertyName is
        nameof(TripBoardRow.FirstWeightKg) or nameof(TripBoardRow.SecondWeightKg) or nameof(TripBoardRow.VehicleNetKg);

    /// <summary>Cập nhật tiêu đề + định dạng 3 cột số cân theo nhóm Mía/Vật tư đang chọn.</summary>
    private void ApplyWeightUnit()
    {
        var unit = Loc.T(IsUnitTon ? "print.unit_ton" : "print.unit_kg");
        foreach (DataGridViewColumn col in _tripGrid.Columns)
        {
            var key = col.DataPropertyName switch
            {
                nameof(TripBoardRow.FirstWeightKg) => "col.first_weight_unit",
                nameof(TripBoardRow.SecondWeightKg) => "col.second_weight_unit",
                nameof(TripBoardRow.VehicleNetKg) => "col.net_weight_unit",
                _ => null
            };
            if (key is null) continue;
            col.HeaderText = Loc.T(key, unit);
            col.DefaultCellStyle.Format = IsUnitTon ? "N3" : "N0"; // Excel xuất theo định dạng này
        }
    }

    private void ConfigureDeviceGrid()
    {
        _deviceGrid.Columns.AddRange(
            GridBuilder.Column(Loc.T("col.device_code"), nameof(DeviceHealthRow.DeviceCode), 110),
            GridBuilder.Column(Loc.T("col.device_class"), nameof(DeviceHealthRow.DeviceClass), 105),
            GridBuilder.Column(Loc.T("col.lane"), nameof(DeviceHealthRow.LaneCode), 60),
            GridBuilder.Column(Loc.T("col.enabled"), nameof(DeviceHealthRow.IsEnabled), 50),
            GridBuilder.Column(Loc.T("col.online"), nameof(DeviceHealthRow.IsOnline), 75),
            GridBuilder.Column(Loc.T("col.last_seen"), nameof(DeviceHealthRow.LastSeenAt), 125));

        GridBuilder.AttachEmptyState(_deviceGrid, Loc.T("dashboard.no_devices"));
    }

    private async Task RefreshAsync()
    {
        if (_cts.IsCancellationRequested) return;

        try
        {
            _btnRefresh.Enabled = false;

            var overview = await _queries.GetOverviewAsync(_siteId, _cts.Token);
            RenderPipeline(overview.Lifecycle);
            RenderMetrics(overview);

            var trips = await _queries.ListTripsAsync(_siteId,
                new TripBoardFilter
                {
                    FirstWeightOnly = _chkFirstWeightOnly.Checked,
                    Category = _tripCategory.Category
                }, _cts.Token);

            _tripRows = trips;
            _lblTripCount.Text = Loc.T("grid.row_count", trips.Count.ToString("N0"));
            _deviceGrid.DataSource = overview.Devices.ToList();

            GridBuilder.BindAndColorize(_tripGrid, trips, r =>
                r.ExecutionStatus == Domain.Enums.ExecutionStatus.Paused
                    ? "Paused"
                    : r.Lifecycle.ToString());

            _lblUpdatedAt.Text = Loc.T("dashboard.updated_at", DateTime.Now.ToString("HH:mm:ss"), _user.DisplayName);
        }
        catch (OperationCanceledException)
        {
            // Form đang đóng.
        }
        catch (ObjectDisposedException)
        {
            // CancellationTokenSource đã dispose — form đã đóng.
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{Loc.T("dashboard.load_error")}{Environment.NewLine}{ex.Message}",
                Loc.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnRefresh.Enabled = true;
        }
    }

    /// <summary>Xuất danh sách chuyến đang hiển thị (theo nhóm Mía/Vật tư) ra Excel.</summary>
    private async Task ExportTripGridAsync()
    {
        var filters = new List<string> { Loc.T("export.filter_category", _tripCategory.CategoryLabel) };
        if (_chkFirstWeightOnly.Checked)
            filters.Add(_chkFirstWeightOnly.Text);

        try
        {
            await ExportHelper.ExportGridAsync(
                this, _tripGrid, _tripRows,
                new ExportHelper.ExportRequest(
                    $"{Loc.T("dashboard.trip_section")} - {_tripCategory.CategoryLabel}",
                    $"tong-quan-chuyen_{_tripCategory.FileSuffix}",
                    filters,
                    _tripCategory.CategoryLabel),
                // Excel cùng đơn vị với lưới: mía đổi kg → tấn.
                mapValue: (prop, _, raw) => IsUnitTon && IsWeightColumn(prop) && raw is decimal kg ? kg / 1000m : raw,
                busyControl: _btnExportExcel);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"{Loc.T("export.error")}{Environment.NewLine}{ex.Message}",
                Loc.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RenderPipeline(LifecycleCounts counts)
    {
        _stageTemp.Count = counts.TempRegistered;
        _stageRegistered.Count = counts.Registered;
        _stageInProgress.Count = counts.InProgress;
        _stageWeighed.Count = counts.WeighingCompleted;
        _stageReconciled.Count = counts.Reconciled;
        _stagePosted.Count = counts.Posted;
        _stageClosed.Count = counts.Closed;
    }

    private void RenderMetrics(SiteOverview overview)
    {
        _tileWaiting.SetValue(overview.WaitingForWeighing);
        _tileInside.SetValue(overview.InsidePlant);
        _tileFirstOnly.SetValue(overview.FirstWeightOnly);
        _tilePaused.SetValue(overview.PausedTrips);
        _tileReconciliation.SetValue(overview.ReconciliationBlockers);
        _tileIntegration.SetValue(overview.IntegrationAttention);
        _tileManual.SetValue(overview.ManualInterventions);

        _tileNet.SetValue(overview.TotalVehicleNetKgInRange?.ToString("N0") ?? "—");
        _tileNet.SetNote(overview.TotalVehicleNetKgInRange is null
            ? Loc.T("dashboard.note_no_net")
            : Loc.T("dashboard.note_net_unit"));

        _tileWaiting.SetNote(Loc.T("dashboard.note_total_trips", overview.TotalTripsInRange));
        _tilePaused.SetNote(overview.PausedTrips == 0 ? Loc.T("dashboard.note_no_incidents") : Loc.T("dashboard.note_paused_action"));
        _tileIntegration.SetNote(overview.IntegrationAttention == 0 ? Loc.T("dashboard.note_no_integration") : Loc.T("dashboard.note_integration_action"));
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        Loc.Changed -= OnLanguageChanged;
        _eventBus.TripChanged -= OnTripChangedFromOtherForm;
        _cts.Cancel();
        base.OnFormClosing(e);
    }

    /// <summary>Refresh tổng quan khi form khác thay đổi chuyến (đăng tài, cân, đối soát…).</summary>
    private async void OnTripChangedFromOtherForm(object? sender, TripChangedEventArgs e)
    {
        if (InvokeRequired)
        {
            Invoke(() => OnTripChangedFromOtherForm(sender, e));
            return;
        }

        await RefreshAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _cts.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>
/// Nhà máy đang làm việc. Vận hành thật lấy từ cấu hình trạm và quyền của người dùng (R-002).
/// </summary>
public interface ISiteContext
{
    Guid CurrentSiteId { get; }
    string CurrentSiteCode { get; }

    /// <summary>
    /// Tên nhà máy (hiển thị trên phiếu in). Null = chưa tải.
    /// </summary>
    string? CurrentSiteName { get; }

    /// <summary>
    /// Địa chỉ nhà máy (hiển thị trên phiếu in). Null = chưa có.
    /// </summary>
    string? CurrentSiteAddress { get; }

    /// <summary>
    /// Mã BU (công ty) của tài khoản đăng nhập. Null = admin/super → thấy tất cả.
    /// Mọi dữ liệu tạo mới ghi org unit code này. Mọi query filter theo code này.
    /// </summary>
    string? OrganizationUnitCode { get; }
}
