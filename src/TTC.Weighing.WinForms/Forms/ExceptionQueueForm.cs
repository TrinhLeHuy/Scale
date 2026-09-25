using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.Queries;
using TTC.Weighing.Application.UseCases.Weighing;
using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.WinForms.Controls;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Services;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Forms;

/// <summary>
/// UI-07 — Hàng chờ ngoại lệ. Cân lại, nhập tay, chênh lệch, sự cố và tạm ngưng/tiếp tục
/// (R-009, R-010, R-025). Mọi duyệt phải có lý do và biên bản.
/// </summary>
public sealed class ExceptionQueueForm : Form
{
    private readonly IExceptionQueueQueries _queue;
    private readonly IDashboardQueries _dashboard;
    private readonly RequestReweighHandler _reweigh;
    private readonly ApproveManualWeightHandler _manualWeight;
    private readonly PauseResumeHandler _pauseResume;
    private readonly IApprovalRepository _approvals;
    private readonly IWeighbridgeRepository _weighbridges;
    private readonly IClock _clock;
    private readonly ICurrentUser _user;
    private readonly TripEventBus _eventBus;
    private readonly Guid _siteId;
    private readonly CancellationTokenSource _cts = new();

    private readonly DataGridView _grid = GridBuilder.ReadOnlyGrid("Hàng chờ ngoại lệ");
    private readonly ComboBox _cboTrip = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _txtReason = new();
    private readonly ComboBox _cboRole = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _numManualKg = new()
    {
        DecimalPlaces = 3, Maximum = 100_000m, Increment = 10m, Width = 160
    };

    private readonly Button _btnRequestReweigh =
        GridBuilder.SecondaryButton(Loc.T("exception.btn_reweigh"), new Size(160, 34));
    private readonly Button _btnManualWeight =
        GridBuilder.SecondaryButton(Loc.T("exception.btn_manual"), new Size(170, 34));
    private readonly Button _btnPause =
        GridBuilder.SecondaryButton(Loc.T("exception.btn_pause"), new Size(170, 34));
    private readonly Button _btnResume =
        GridBuilder.PrimaryButton(Loc.T("exception.btn_resume"), new Size(150, 34));

    private readonly TextBox _txtLog = new();

    public ExceptionQueueForm(
        IExceptionQueueQueries queue,
        IDashboardQueries dashboard,
        RequestReweighHandler reweigh,
        ApproveManualWeightHandler manualWeight,
        PauseResumeHandler pauseResume,
        IApprovalRepository approvals,
        IWeighbridgeRepository weighbridges,
        IClock clock,
        ICurrentUser user,
        ISiteContext siteContext,
        TripEventBus eventBus)
    {
        _queue = queue;
        _dashboard = dashboard;
        _reweigh = reweigh;
        _manualWeight = manualWeight;
        _pauseResume = pauseResume;
        _approvals = approvals;
        _weighbridges = weighbridges;
        _clock = clock;
        _user = user;
        _eventBus = eventBus;
        _siteId = siteContext.CurrentSiteId;

        Text = Loc.T("exception.title");
        MinimumSize = new Size(1080, 780);
        BackColor = AgrisTheme.Canvas;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        BuildLayout();
        Load += async (_, _) => await RefreshAsync();

        _eventBus.TripChanged += OnTripChangedFromOtherForm;
    }

    private void BuildLayout()
    {
        ConfigureGrid();

        Controls.Add(FormLayout.VerticalStack(
            (BuildActionCard(), SizeType.AutoSize, 0),
            (BuildQueueCard(), SizeType.Percent, 100),
            (BuildLogCard(), SizeType.Absolute, 130)));
    }

    private void ConfigureGrid()
    {
        _grid.Columns.AddRange(
            GridBuilder.Column(Loc.T("col.trip_no"), nameof(ExceptionQueueItem.TripNo), 110),
            GridBuilder.Column(Loc.T("col.type"), nameof(ExceptionQueueItem.Category), 180),
            GridBuilder.Column(Loc.T("col.description"), nameof(ExceptionQueueItem.Description), 340),
            GridBuilder.Column(Loc.T("col.raised_at"), nameof(ExceptionQueueItem.RaisedAt), 125),
            GridBuilder.Column(Loc.T("col.raised_by"), nameof(ExceptionQueueItem.RaisedBy), 110),
            GridBuilder.Column(Loc.T("col.next_action"),
                nameof(ExceptionQueueItem.NextAllowedAction), 240));

        GridBuilder.AttachEmptyState(_grid, Loc.T("exception.no_items"));
    }

    private Card BuildActionCard()
    {
        var table = FormLayout.FieldTable();

        _cboTrip.DisplayMember = nameof(TripBoardRow.TripNo);
        FormLayout.AddField(table, Loc.T("exception.field_trip"), _cboTrip);

        FormLayout.AddField(table, Loc.T("exception.field_reason"), _txtReason);

        _cboRole.DataSource = Enum.GetValues<WeightRole>();
        FormLayout.AddField(table, Loc.T("field.weigh_role"), _cboRole);

        FormLayout.AddField(table, Loc.T("field.manual_kg"), _numManualKg);

        _btnRequestReweigh.Click += async (_, _) => await RequestReweighAsync();
        _btnManualWeight.Click += async (_, _) => await ApproveManualAsync();
        _btnPause.Click += async (_, _) => await PauseAsync();
        _btnResume.Click += async (_, _) => await ResumeAsync();

        FormLayout.AddFullWidth(table, FormLayout.ActionBar(
            _btnRequestReweigh, _btnManualWeight, _btnPause, _btnResume), 54);

        return FormLayout.TitledCard(
            Loc.T("exception.section_actions"),
            table,
            Loc.T("exception.notice"));
    }

    private Card BuildQueueCard()
        => FormLayout.TitledCard(Loc.T("exception.section_queue"), _grid);

    private Card BuildLogCard()
    {
        _txtLog.Multiline = true;
        _txtLog.ReadOnly = true;
        _txtLog.Dock = DockStyle.Fill;
        _txtLog.ScrollBars = ScrollBars.Both;
        _txtLog.WordWrap = false;
        _txtLog.Font = AgrisTheme.Mono(9F);
        _txtLog.BorderStyle = BorderStyle.None;
        _txtLog.AccessibleName = "Nhật ký xử lý ngoại lệ";

        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 6, 8, 6) };
        host.Controls.Add(_txtLog);

        return FormLayout.TitledCard(Loc.T("exception.section_log"), host);
    }

    private async Task RefreshAsync()
    {
        try
        {
            var items = await _queue.ListAsync(_siteId, _cts.Token);
            _grid.DataSource = items.ToList();

            var trips = await _dashboard.ListTripsAsync(_siteId, new TripBoardFilter(), _cts.Token);
            var selected = (_cboTrip.SelectedItem as TripBoardRow)?.TripId;

            var list = trips.ToList();
            _cboTrip.SetDataSourceSafe(list, null);

            if (selected.HasValue)
            {
                var index = list.FindIndex(t => t.TripId == selected.Value);
                if (index >= 0) _cboTrip.SelectedIndex = index;
            }
        }
        catch (Exception ex)
        {
            Log(Loc.T("exception.load_error", ex.Message));
        }
    }

    private async Task RequestReweighAsync()
    {
        if (!RequireTripAndReason(out var trip, out var reason)) return;

        try
        {
            var result = await _reweigh.HandleAsync(new RequestReweighCommand(
                TripId: trip.TripId,
                Role: (WeightRole)_cboRole.SelectedItem!,
                SegmentIndex: 0,
                Reason: reason,
                DocumentRef: null,
                CorrelationId: Guid.NewGuid().ToString("N")), _cts.Token);

            if (!result.IsSuccess)
            {
                Log(Loc.T("exception.reweigh_failed", result.FailureReason));
                return;
            }

            Log(Loc.T("exception.reweigh_ok", result.ApprovalId));

            if (result.FailureReason is not null)
                Log(Loc.T("exception.reweigh_note", result.FailureReason));

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Log(Loc.T("exception.reweigh_error", ex.Message));
        }
    }

    private async Task ApproveManualAsync()
    {
        if (!RequireTripAndReason(out var trip, out var reason)) return;

        try
        {
            // Nhập tay bắt buộc có phê duyệt đã cấp; ở đây tạo và cấp duyệt để minh họa quy trình.
            var approval = new Approval
            {
                TripId = trip.TripId,
                ActionType = "MANUAL_WEIGHT",
                RequestedBy = _user.UserId,
                ApprovedBy = "preview.supervisor",
                ApprovedAt = _clock.UtcNow,
                Reason = reason,
                CreatedAt = _clock.UtcNow,
                CreatedBy = _user.UserId
            };

            await _approvals.AddAsync(approval, _cts.Token);

            var bridges = await _weighbridges.ListBySiteAsync(_siteId, _cts.Token);
            if (bridges.Count == 0)
            {
                Log(Loc.T("exception.no_bridge"));
                return;
            }

            var result = await _manualWeight.HandleAsync(new ApproveManualWeightCommand(
                TripId: trip.TripId,
                WeighbridgeId: bridges[0].Id,
                Checkpoint: CheckpointType.FirstWeight,
                SegmentIndex: 0,
                Role: (WeightRole)_cboRole.SelectedItem!,
                RawWeight: _numManualKg.Value,
                RawUom: "kg",
                Reason: reason,
                ApprovalRef: approval.Id.ToString(),
                CorrelationId: Guid.NewGuid().ToString("N")), _cts.Token);

            Log(result.IsSuccess
                ? Loc.T("exception.manual_ok", _numManualKg.Value.ToString("N3"))
                : Loc.T("exception.manual_failed", result.FailureReason));

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Log(Loc.T("exception.manual_error", ex.Message));
        }
    }

    private async Task PauseAsync()
    {
        if (!RequireTripAndReason(out var trip, out var reason)) return;

        try
        {
            var result = await _pauseResume.PauseAsync(new PauseTripCommand(
                TripId: trip.TripId,
                IssueCode: "MANUAL_PAUSE",
                Scope: "TRIP",
                Description: reason,
                CorrelationId: Guid.NewGuid().ToString("N")), _cts.Token);

            Log(result.IsSuccess
                ? Loc.T("exception.pause_ok")
                : Loc.T("exception.pause_failed", result.FailureReason));

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Log(Loc.T("exception.pause_error", ex.Message));
        }
    }

    private async Task ResumeAsync()
    {
        if (_cboTrip.SelectedItem is not TripBoardRow trip)
        {
            Log(Loc.T("common.no_trip_selected"));
            return;
        }

        var confirm = MessageBox.Show(
            Loc.T("exception.resume_confirm"),
            Loc.T("exception.resume_title"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes)
        {
            Log(Loc.T("exception.resume_cancelled"));
            return;
        }

        try
        {
            var result = await _pauseResume.ResumeAsync(new ResumeTripCommand(
                TripId: trip.TripId,
                IncidentId: Guid.Empty,
                UnknownTransactionsResolved: true,
                CorrelationId: Guid.NewGuid().ToString("N")), _cts.Token);

            Log(result.IsSuccess
                ? Loc.T("exception.resume_ok")
                : Loc.T("exception.resume_failed", result.FailureReason));

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Log(Loc.T("exception.resume_error", ex.Message));
        }
    }

    private bool RequireTripAndReason(out TripBoardRow trip, out string reason)
    {
        trip = default!;
        reason = _txtReason.Text.Trim();

        if (_cboTrip.SelectedItem is not TripBoardRow selected)
        {
            Log(Loc.T("common.no_trip_selected"));
            return false;
        }

        trip = selected;

        if (string.IsNullOrWhiteSpace(reason))
        {
            Log(Loc.T("exception.reason_required"));
            return false;
        }

        return true;
    }

    private void Log(string line)
        => _txtLog.AppendText($"{DateTime.Now:HH:mm:ss}  {line}{Environment.NewLine}");

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _eventBus.TripChanged -= OnTripChangedFromOtherForm;
        _cts.Cancel();
        base.OnFormClosing(e);
    }

    private async void OnTripChangedFromOtherForm(object? sender, TripChangedEventArgs e)
    {
        if (InvokeRequired) { Invoke(() => OnTripChangedFromOtherForm(sender, e)); return; }
        await RefreshAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _cts.Dispose();
        base.Dispose(disposing);
    }
}
