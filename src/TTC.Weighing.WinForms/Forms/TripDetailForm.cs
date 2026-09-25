using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.Queries;
using TTC.Weighing.Application.UseCases.Reconcile;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Domain.Rules;
using TTC.Weighing.WinForms.Controls;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Services;

namespace TTC.Weighing.WinForms.Forms;

/// <summary>
/// UI-06 — Chi tiết chuyến. Timeline checkpoint, mọi bản cân kể cả đã thay thế,
/// mẫu chất lượng, phiên đo, đối soát, phân bổ, tích hợp, audit và blocker (R-026).
/// Req 9.1–9.6: Hiển thị phân bổ ghép xe, nút phân bổ mở dialog chọn phương pháp.
/// </summary>
public sealed class TripDetailForm : Form
{
    private readonly ITripDetailQueries _queries;
    private readonly IDashboardQueries _dashboard;
    private readonly AllocateTripHandler _allocateHandler;
    private readonly TripEventBus _eventBus;
    private readonly Guid _siteId;
    private readonly CancellationTokenSource _cts = new();

    private readonly ComboBox _cboTrip = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ListBox _lstBlockers = new();
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly TimelineControl _timeline = new();
    private readonly Button _btnAllocate;

    private readonly DataGridView _checkpointGrid = GridBuilder.ReadOnlyGrid("Timeline checkpoint");
    private readonly DataGridView _weighGrid = GridBuilder.ReadOnlyGrid("Các lần cân");
    private readonly DataGridView _qualityGrid = GridBuilder.ReadOnlyGrid("Mẫu chất lượng");
    private readonly DataGridView _measurementGrid = GridBuilder.ReadOnlyGrid("Phiên đo");
    private readonly DataGridView _reconciliationGrid = GridBuilder.ReadOnlyGrid("Đối soát");
    private readonly DataGridView _allocationGrid = GridBuilder.ReadOnlyGrid("Phân bổ");
    private readonly DataGridView _integrationGrid = GridBuilder.ReadOnlyGrid("Tích hợp");
    private readonly DataGridView _auditGrid = GridBuilder.ReadOnlyGrid("Audit trail");

    private EventHandler? _tripChangedHandler;

    public TripDetailForm(
        ITripDetailQueries queries,
        IDashboardQueries dashboard,
        AllocateTripHandler allocateHandler,
        ISiteContext siteContext,
        TripEventBus eventBus)
    {
        _queries = queries;
        _dashboard = dashboard;
        _allocateHandler = allocateHandler;
        _eventBus = eventBus;
        _siteId = siteContext.CurrentSiteId;

        _btnAllocate = GridBuilder.PrimaryButton(Loc.T("detail.btn_allocate"), new Size(120, 34));
        _btnAllocate.AccessibleName = "Phân bổ trọng lượng ghép xe";
        _btnAllocate.Click += async (_, _) => await ShowAllocateDialogAsync();

        Text = Loc.T("detail.title");
        MinimumSize = new Size(1100, 760);
        BackColor = Color.FromArgb(243, 245, 248);
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        BuildLayout();
        Load += async (_, _) => await LoadTripsAsync();

        _eventBus.TripChanged += OnTripChangedFromOtherForm;
    }

    private readonly SemaphoreSlim _dbLock = new(1, 1);
    private bool _tripsLoaded;

    /// <summary>Mở trực tiếp một chuyến, dùng khi double-click từ UI-01.</summary>
    public async Task ShowTripAsync(Guid tripId)
    {
        // Defer để Load event kịp chạy xong trước
        await Task.Yield();

        if (!_tripsLoaded)
            await LoadTripsAsync();

        for (var i = 0; i < _cboTrip.Items.Count; i++)
        {
            if (_cboTrip.Items[i] is TripBoardRow row && row.TripId == tripId)
            {
                _cboTrip.SelectedIndex = i;
                return;
            }
        }
    }

    private void BuildLayout()
    {
        ConfigureGrids();

        // --- Header panel: chọn chuyến ---
        var header = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Color.White };
        _cboTrip.Location = new Point(110, 10);
        _cboTrip.Width = 420;
        _cboTrip.DisplayMember = nameof(TripBoardRow.TripNo);
        _tripChangedHandler = async (_, _) => await LoadDetailAsync();
        _cboTrip.SelectedIndexChanged += _tripChangedHandler;

        header.Controls.Add(new Label
        {
            Text = Loc.T("detail.label_trip"),
            Location = new Point(12, 14),
            AutoSize = true,
            ForeColor = Color.DimGray
        });
        header.Controls.Add(_cboTrip);

        // --- Timeline hiển thị mốc thời gian checkpoint (Req 7.1, 9.6) ---
        _timeline.Height = 110;
        _timeline.Dock = DockStyle.Top;

        _lstBlockers.Dock = DockStyle.Fill;
        _lstBlockers.ForeColor = Color.Firebrick;
        _lstBlockers.Font = new Font("Segoe UI", 9.5F);
        _lstBlockers.AccessibleName = "Danh sách blocker của chuyến";

        var blockerPanel = new Panel { Dock = DockStyle.Bottom, Height = 110 };
        blockerPanel.Controls.Add(_lstBlockers);
        blockerPanel.Controls.Add(GridBuilder.SectionHeader(Loc.T("detail.section_blocker")));

        AddTab(Loc.T("detail.tab_checkpoint"), _checkpointGrid);
        AddTab(Loc.T("detail.tab_weigh"), _weighGrid);
        AddTab(Loc.T("detail.tab_quality"), _qualityGrid);
        AddTab(Loc.T("detail.tab_measurement"), _measurementGrid);
        AddTab(Loc.T("detail.tab_reconciliation"), _reconciliationGrid);
        AddAllocationTab();
        AddTab(Loc.T("detail.tab_integration"), _integrationGrid);
        AddTab(Loc.T("detail.tab_audit"), _auditGrid);

        Controls.Add(_tabs);
        Controls.Add(blockerPanel);
        Controls.Add(_timeline);
        Controls.Add(header);
    }

    private void ConfigureGrids()
    {
        _checkpointGrid.Columns.AddRange(
            GridBuilder.Column(Loc.T("checkpoint.registration"), nameof(CheckpointRow.Type), 130),
            GridBuilder.Column(Loc.T("col.status"), nameof(CheckpointRow.Status), 90),
            GridBuilder.Column(Loc.T("col.started_at"), nameof(CheckpointRow.StartedAt), 130),
            GridBuilder.Column(Loc.T("col.completed_at"), nameof(CheckpointRow.CompletedAt), 130),
            GridBuilder.Column(Loc.T("col.confirmed_by"), nameof(CheckpointRow.ConfirmedBy), 110),
            GridBuilder.Column(Loc.T("col.waiver_reason"), nameof(CheckpointRow.WaiverReason), 200));

        _weighGrid.Columns.AddRange(
            GridBuilder.Column(Loc.T("col.revision"), nameof(WeighEventRow.Revision), 50),
            GridBuilder.Column(Loc.T("col.effective"), nameof(WeighEventRow.IsEffective), 60),
            GridBuilder.Column(Loc.T("field.weigh_role"), nameof(WeighEventRow.Role), 70),
            GridBuilder.Column(Loc.T("col.raw_weight"), nameof(WeighEventRow.RawWeight), 90, "N3"),
            GridBuilder.Column(Loc.T("col.raw_uom"), nameof(WeighEventRow.RawUom), 70),
            GridBuilder.Column(Loc.T("col.normalized_kg"), nameof(WeighEventRow.NormalizedKg), 100, "N3"),
            GridBuilder.Column(Loc.T("col.source"), nameof(WeighEventRow.Source), 70),
            GridBuilder.Column(Loc.T("col.measured_at"), nameof(WeighEventRow.MeasuredAt), 130),
            GridBuilder.Column(Loc.T("col.stable_window"), nameof(WeighEventRow.StableWindow), 100),
            GridBuilder.Column(Loc.T("col.evidence_count"), nameof(WeighEventRow.EvidenceCount), 80),
            GridBuilder.Column(Loc.T("field.reason"), nameof(WeighEventRow.Reason), 180),
            GridBuilder.Column(Loc.T("col.approval_ref"), nameof(WeighEventRow.ApprovalRef), 110));

        _qualityGrid.Columns.AddRange(
            GridBuilder.Column(Loc.T("col.type"), nameof(QualitySampleRow.Type), 80),
            GridBuilder.Column(Loc.T("col.status"), nameof(QualitySampleRow.Status), 90),
            GridBuilder.Column(Loc.T("col.revision"), nameof(QualitySampleRow.Revision), 50),
            GridBuilder.Column(Loc.T("col.effective"), nameof(QualitySampleRow.IsEffective), 60),
            GridBuilder.Column(Loc.T("col.sample_before"), nameof(QualitySampleRow.SampleBeforeKg), 90, "N3"),
            GridBuilder.Column(Loc.T("col.sample_after"), nameof(QualitySampleRow.SampleAfterKg), 90, "N3"),
            GridBuilder.Column(Loc.T("col.impurity_pct"), nameof(QualitySampleRow.ImpurityPercent), 90, "N4"),
            GridBuilder.Column(Loc.T("col.ccs"), nameof(QualitySampleRow.CcsValue), 80, "N2"),
            GridBuilder.Column(Loc.T("col.source"), nameof(QualitySampleRow.Source), 70));

        _measurementGrid.Columns.AddRange(
            GridBuilder.Column(Loc.T("col.item_code"), nameof(MeasurementSessionRow.ItemCode), 110),
            GridBuilder.Column(Loc.T("col.status"), nameof(MeasurementSessionRow.Status), 110),
            GridBuilder.Column(Loc.T("col.iot_mass"), nameof(MeasurementSessionRow.IotMassKg), 90, "N3"),
            GridBuilder.Column(Loc.T("col.iot_origin"), nameof(MeasurementSessionRow.IotMassOrigin), 110),
            GridBuilder.Column(Loc.T("col.bag_net"), nameof(MeasurementSessionRow.NetBagCount), 70),
            GridBuilder.Column(Loc.T("col.data_complete"), nameof(MeasurementSessionRow.DataComplete), 70),
            GridBuilder.Column(Loc.T("col.hold_reason"), nameof(MeasurementSessionRow.HoldReason), 220));

        _reconciliationGrid.Columns.AddRange(
            GridBuilder.Column(Loc.T("col.check_type"), nameof(ReconciliationCheckRow.CheckType), 170),
            GridBuilder.Column(Loc.T("col.verdict"), nameof(ReconciliationCheckRow.Verdict), 90),
            GridBuilder.Column(Loc.T("col.observed"), nameof(ReconciliationCheckRow.ObservedValue), 90, "N3"),
            GridBuilder.Column(Loc.T("col.expected"), nameof(ReconciliationCheckRow.ExpectedValue), 90, "N3"),
            GridBuilder.Column(Loc.T("col.difference"), nameof(ReconciliationCheckRow.Difference), 90, "N3"),
            GridBuilder.Column(Loc.T("col.detail"), nameof(ReconciliationCheckRow.Detail), 320));

        _allocationGrid.Columns.AddRange(
            GridBuilder.Column(Loc.T("col.owner_ref"), nameof(AllocationRow.OwnerRef), 140),
            GridBuilder.Column(Loc.T("col.allocated_kg"), nameof(AllocationRow.AllocatedKg), 110, "N3"),
            GridBuilder.Column(Loc.T("col.method"), nameof(AllocationRow.Method), 130),
            GridBuilder.Column(Loc.T("col.ratio"), nameof(AllocationRow.Ratio), 90, "N4"),
            GridBuilder.Column(Loc.T("col.rounding_adj"), nameof(AllocationRow.RoundingAdjustmentKg), 130, "N3"),
            GridBuilder.Column(Loc.T("col.is_approved"), nameof(AllocationRow.IsApproved), 80));

        _integrationGrid.Columns.AddRange(
            GridBuilder.Column(Loc.T("col.destination"), nameof(IntegrationMessageRow.Destination), 70),
            GridBuilder.Column(Loc.T("col.operation"), nameof(IntegrationMessageRow.Operation), 130),
            GridBuilder.Column(Loc.T("col.business_key"), nameof(IntegrationMessageRow.BusinessKey), 220),
            GridBuilder.Column(Loc.T("col.status"), nameof(IntegrationMessageRow.Status), 90),
            GridBuilder.Column(Loc.T("col.attempts"), nameof(IntegrationMessageRow.Attempts), 70),
            GridBuilder.Column(Loc.T("col.manual_reconcile"), nameof(IntegrationMessageRow.RequiresManualReconciliation), 100),
            GridBuilder.Column(Loc.T("col.message"), nameof(IntegrationMessageRow.BusinessErrorMessage), 260));

        _auditGrid.Columns.AddRange(
            GridBuilder.Column(Loc.T("col.occurred_at"), nameof(AuditRow.OccurredAt), 130),
            GridBuilder.Column(Loc.T("col.action_type"), nameof(AuditRow.ActionType), 170),
            GridBuilder.Column(Loc.T("col.actor"), nameof(AuditRow.Actor), 120),
            GridBuilder.Column(Loc.T("col.before"), nameof(AuditRow.BeforeJson), 200),
            GridBuilder.Column(Loc.T("col.after"), nameof(AuditRow.AfterJson), 200),
            GridBuilder.Column(Loc.T("field.reason"), nameof(AuditRow.Reason), 180));

        GridBuilder.AttachEmptyState(_checkpointGrid, Loc.T("detail.no_checkpoint"));
        GridBuilder.AttachEmptyState(_weighGrid, Loc.T("detail.no_weigh"));
        GridBuilder.AttachEmptyState(_qualityGrid, Loc.T("detail.no_quality"));
        GridBuilder.AttachEmptyState(_measurementGrid, Loc.T("detail.no_measurement"));
        GridBuilder.AttachEmptyState(_reconciliationGrid, Loc.T("detail.no_reconciliation"));
        GridBuilder.AttachEmptyState(_allocationGrid, Loc.T("detail.no_allocation"));
        GridBuilder.AttachEmptyState(_integrationGrid, Loc.T("detail.no_integration"));
        GridBuilder.AttachEmptyState(_auditGrid, Loc.T("detail.no_audit"));
    }

    private void AddTab(string title, DataGridView grid)
    {
        var page = new TabPage(title) { BackColor = Color.White };
        page.Controls.Add(grid);
        _tabs.TabPages.Add(page);
    }

    /// <summary>
    /// Tab Phân bổ: lưới phân bổ + nút "Phân bổ" ở trên (Req 9.1–9.6).
    /// </summary>
    private void AddAllocationTab()
    {
        var page = new TabPage(Loc.T("detail.tab_allocation")) { BackColor = Color.White };

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Color.White };
        _btnAllocate.Location = new Point(8, 6);
        toolbar.Controls.Add(_btnAllocate);
        toolbar.Controls.Add(GridBuilder.Caption(
            Loc.T("detail.allocate_caption"), 140, 12));

        page.Controls.Add(_allocationGrid);
        page.Controls.Add(toolbar);
        _tabs.TabPages.Add(page);
    }

    private async Task LoadTripsAsync()
    {
        await _dbLock.WaitAsync(_cts.Token);
        try
        {
            var trips = await _dashboard.ListTripsAsync(_siteId, new TripBoardFilter(), _cts.Token);
            _cboTrip.SetDataSourceSafe(trips.ToList(), _tripChangedHandler);
            _tripsLoaded = true;
            await LoadDetailAsync();
        }
        catch (Exception ex)
        {
            ShowError(Loc.T("detail.load_error"), ex);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    private async Task LoadDetailAsync()
    {
        if (_cboTrip.SelectedItem is not TripBoardRow trip) return;

        try
        {
            var detail = await _queries.GetAsync(trip.TripId, _cts.Token);
            if (detail is null) return;

            // Cập nhật TimelineControl từ checkpoint data (Req 7.1).
            _timeline.UpdateCheckpoints(detail.Checkpoints);

            // Batch gán DataSource cho tất cả grid trong SuspendLayout
            SuspendLayout();
            try
            {
                _checkpointGrid.DataSource = detail.Checkpoints.ToList();
                _weighGrid.DataSource = detail.WeighEvents.ToList();
                _qualityGrid.DataSource = detail.QualitySamples.ToList();
                _measurementGrid.DataSource = detail.MeasurementSessions.ToList();
                _reconciliationGrid.DataSource = detail.ReconciliationChecks.ToList();
                _allocationGrid.DataSource = detail.Allocations.ToList();
                _integrationGrid.DataSource = detail.IntegrationMessages.ToList();
                _auditGrid.DataSource = detail.AuditTrail.ToList();

                // Bản cân hết hiệu lực được làm mờ nhưng vẫn hiển thị.
                foreach (DataGridViewRow row in _weighGrid.Rows)
                {
                    if (row.DataBoundItem is WeighEventRow r && !r.IsEffective)
                    {
                        row.DefaultCellStyle.ForeColor = Color.Gray;
                        row.DefaultCellStyle.BackColor = Color.WhiteSmoke;
                    }
                }

                foreach (DataGridViewRow row in _reconciliationGrid.Rows)
                {
                    if (row.DataBoundItem is ReconciliationCheckRow r)
                        GridBuilder.ApplyStatusColor(row, r.Verdict.ToString());
                }

                foreach (DataGridViewRow row in _integrationGrid.Rows)
                {
                    if (row.DataBoundItem is IntegrationMessageRow r)
                        GridBuilder.ApplyStatusColor(row, r.Status.ToString());
                }

                // Highlight RoundingAdjustmentKg khác 0 để hiển thị minh bạch (Req 9.2, 9.6).
                foreach (DataGridViewRow row in _allocationGrid.Rows)
                {
                    if (row.DataBoundItem is AllocationRow alloc)
                    {
                        if (alloc.RoundingAdjustmentKg != 0m)
                        {
                            row.DefaultCellStyle.BackColor = Color.FromArgb(255, 255, 230);
                        }

                        if (!alloc.IsApproved)
                        {
                            row.DefaultCellStyle.ForeColor = Color.OrangeRed;
                        }
                    }
                }
            }
            finally
            {
                ResumeLayout(true);
            }

            _lstBlockers.Items.Clear();

            if (detail.Blockers.Count == 0)
                _lstBlockers.Items.Add(Loc.T("detail.no_blocker"));
            else
                foreach (var blocker in detail.Blockers)
                    _lstBlockers.Items.Add(blocker);
        }
        catch (Exception ex)
        {
            ShowError(Loc.T("detail.load_detail_error"), ex);
        }
    }

    /// <summary>
    /// Mở dialog chọn phương pháp phân bổ, nhập dòng chủ mía, và gọi AllocateTripHandler (Req 9.1–9.6).
    /// Validate: net âm/bằng 0 → từ chối, bắt buộc ApprovalRef.
    /// </summary>
    private async Task ShowAllocateDialogAsync()
    {
        if (_cboTrip.SelectedItem is not TripBoardRow trip)
        {
            MessageBox.Show(Loc.T("detail.select_trip"), Loc.T("common.info"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new AllocateTripDialog(trip.TripId, trip.TripNo);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            _btnAllocate.Enabled = false;

            var cmd = new AllocateTripCommand(
                TripId: trip.TripId,
                Method: dialog.SelectedMethod,
                Lines: dialog.AllocationLines,
                ApprovalRef: dialog.ApprovalRef,
                CorrelationId: Guid.NewGuid().ToString("N"));

            var result = await _allocateHandler.HandleAsync(cmd, _cts.Token);

            if (!result.IsSuccess)
            {
                MessageBox.Show(
                    Loc.T("detail.allocate_failed", result.FailureReason),
                    Loc.T("detail.allocate_failed_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            MessageBox.Show(
                Loc.T("detail.allocate_ok", $"{result.MeasuredTotalKg:N3}", $"{result.TotalAllocatedKg:N3}", result.Lines.Count),
                Loc.T("detail.tab_allocation"), MessageBoxButtons.OK, MessageBoxIcon.Information);

            // Refresh sau khi phân bổ.
            await LoadDetailAsync();
        }
        catch (Exception ex)
        {
            ShowError(Loc.T("detail.allocate_error"), ex);
        }
        finally
        {
            _btnAllocate.Enabled = true;
        }
    }

    private static void ShowError(string title, Exception ex)
        => MessageBox.Show($"{title}.{Environment.NewLine}{ex.Message}",
            Loc.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _eventBus.TripChanged -= OnTripChangedFromOtherForm;
        _cts.Cancel();
        base.OnFormClosing(e);
    }

    /// <summary>Refresh danh sách chuyến và chi tiết khi form khác thay đổi.</summary>
    private async void OnTripChangedFromOtherForm(object? sender, TripChangedEventArgs e)
    {
        if (InvokeRequired) { Invoke(() => OnTripChangedFromOtherForm(sender, e)); return; }
        await LoadTripsAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _cts.Dispose();
        base.Dispose(disposing);
    }
}
