using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.Queries;
using TTC.Weighing.WinForms.Controls;
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

    private readonly FlowLayoutPanel _pipelineRow = new();
    private readonly FlowLayoutPanel _metricRow = new();
    private readonly DataGridView _tripGrid = GridBuilder.ReadOnlyGrid(Loc.T("dashboard.trip_section"));
    private readonly DataGridView _deviceGrid = GridBuilder.ReadOnlyGrid(Loc.T("dashboard.device_section"));
    private readonly CheckBox _chkFirstWeightOnly = new();
    private readonly Button _btnRefresh = GridBuilder.SecondaryButton(Loc.T("common.refresh"), new Size(110, 30));
    private readonly Label _lblUpdatedAt = new();

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

        UpdateGridColumnHeaders(_deviceGrid, [
            Loc.T("col.device_code"), Loc.T("col.device_class"), Loc.T("col.lane"),
            Loc.T("col.enabled"), Loc.T("col.online"), Loc.T("col.last_seen")
        ]);

        // Repaint pipeline stages
        foreach (Control c in _pipelineRow.Controls)
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

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 122));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(BuildPipelineCard(), 0, 0);
        root.Controls.Add(BuildMetricPanel(), 0, 1);
        root.Controls.Add(BuildToolbar(), 0, 2);
        root.Controls.Add(BuildTables(), 0, 3);

        Controls.Add(root);
    }

    private Card BuildPipelineCard()
    {
        var card = new Card
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(14, 8, 14, 10)
        };

        var header = new Label
        {
            Text = Loc.T("dashboard.pipeline_header"),
            Dock = DockStyle.Top,
            Height = 22,
            Font = AgrisTheme.Body(10F, FontStyle.Bold),
            ForeColor = AgrisTheme.BrandGreenDark
        };

        _pipelineRow.Dock = DockStyle.Fill;
        _pipelineRow.FlowDirection = FlowDirection.LeftToRight;
        _pipelineRow.WrapContents = false;
        _pipelineRow.AutoScroll = true;

        _pipelineRow.Controls.AddRange(
        [
            _stageTemp, _stageRegistered, _stageInProgress, _stageWeighed,
            _stageReconciled, _stagePosted, _stageClosed
        ]);

        card.Controls.Add(_pipelineRow);
        card.Controls.Add(header);

        return card;
    }

    private Panel BuildMetricPanel()
    {
        _metricRow.Dock = DockStyle.Fill;
        _metricRow.FlowDirection = FlowDirection.LeftToRight;
        _metricRow.WrapContents = false;
        _metricRow.AutoScroll = true;
        _metricRow.BackColor = AgrisTheme.Canvas;
        _metricRow.Margin = new Padding(0, 0, 0, 10);

        _metricRow.Controls.AddRange(
        [
            _tileWaiting, _tileInside, _tileFirstOnly, _tilePaused,
            _tileReconciliation, _tileIntegration, _tileNet, _tileManual
        ]);

        return _metricRow;
    }

    private Panel BuildToolbar()
    {
        var toolbar = new Card
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 6, 12, 6),
            Margin = new Padding(0, 0, 0, 10)
        };

        _chkFirstWeightOnly.Text = Loc.T("dashboard.filter_first_only");
        _chkFirstWeightOnly.Location = new Point(14, 9);
        _chkFirstWeightOnly.AutoSize = true;
        _chkFirstWeightOnly.Font = AgrisTheme.Body();
        _chkFirstWeightOnly.ForeColor = AgrisTheme.TextPrimary;
        _chkFirstWeightOnly.CheckedChanged += async (_, _) => await RefreshAsync();

        _btnRefresh.Location = new Point(300, 5);
        _btnRefresh.Click += async (_, _) => await RefreshAsync();

        _lblUpdatedAt.Location = new Point(424, 11);
        _lblUpdatedAt.AutoSize = true;
        _lblUpdatedAt.Font = AgrisTheme.Caption();
        _lblUpdatedAt.ForeColor = AgrisTheme.TextMuted;

        toolbar.Controls.Add(_chkFirstWeightOnly);
        toolbar.Controls.Add(_btnRefresh);
        toolbar.Controls.Add(_lblUpdatedAt);

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
        tripCard.Controls.Add(_tripGrid);
        tripCard.Controls.Add(GridBuilder.SectionHeader(Loc.T("dashboard.trip_section")));

        var deviceCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(1) };
        deviceCard.Controls.Add(_deviceGrid);
        deviceCard.Controls.Add(GridBuilder.Notice(
            Loc.T("dashboard.device_notice")));
        deviceCard.Controls.Add(GridBuilder.SectionHeader(Loc.T("dashboard.device_section")));

        split.Panel1.Controls.Add(tripCard);
        split.Panel2.Controls.Add(deviceCard);
        split.SplitterDistance = 720;

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

        GridBuilder.AttachEmptyState(_tripGrid, Loc.T("dashboard.no_trips"));

        _tripGrid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            if (_tripGrid.Rows[e.RowIndex].DataBoundItem is TripBoardRow row)
                TripSelected?.Invoke(row.TripId);
        };
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
        try
        {
            _btnRefresh.Enabled = false;

            var overview = await _queries.GetOverviewAsync(_siteId, _cts.Token);
            RenderPipeline(overview.Lifecycle);
            RenderMetrics(overview);

            var trips = await _queries.ListTripsAsync(_siteId,
                new TripBoardFilter { FirstWeightOnly = _chkFirstWeightOnly.Checked }, _cts.Token);

            _tripGrid.DataSource = trips.ToList();
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
    /// Mã BU (công ty) của tài khoản đăng nhập. Null = admin/super → thấy tất cả.
    /// Mọi dữ liệu tạo mới ghi org unit code này. Mọi query filter theo code này.
    /// </summary>
    string? OrganizationUnitCode { get; }
}
