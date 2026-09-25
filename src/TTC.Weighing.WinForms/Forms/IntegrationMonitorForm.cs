using TTC.Weighing.Application.Queries;
using TTC.Weighing.Application.UseCases.Integration;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.WinForms.Controls;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Services;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Forms;

/// <summary>
/// UI-08 — Giám sát tích hợp. Không có nút gửi lại mù; UNKNOWN phải đối soát rồi kết luận
/// (R-024, 04_Man_hinh mục 1).
/// </summary>
public sealed class IntegrationMonitorForm : Form
{
    private readonly IIntegrationMonitorQueries _queries;
    private readonly IDashboardQueries _dashboard;
    private readonly SubmitTripResultHandler _submit;
    private readonly ResolveUnknownMessageHandler _resolve;
    private readonly TripEventBus _eventBus;
    private readonly Guid _siteId;
    private readonly CancellationTokenSource _cts = new();

    private readonly DataGridView _grid = GridBuilder.ReadOnlyGrid("Danh sách thông điệp tích hợp");
    private readonly CheckBox _chkOnlyAttention = new() { Checked = true };
    private readonly ComboBox _cboTrip = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _cboOperation = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _txtResolveReason = new();

    private readonly Button _btnQueue =
        GridBuilder.PrimaryButton(Loc.T("integration.btn_queue"), new Size(200, 34));
    private readonly Button _btnResolveAck =
        GridBuilder.SecondaryButton(Loc.T("integration.btn_resolve_ack"), new Size(190, 34));
    private readonly Button _btnResolveFailed =
        GridBuilder.SecondaryButton(Loc.T("integration.btn_resolve_failed"), new Size(170, 34));

    private readonly TextBox _txtLog = new();

    public IntegrationMonitorForm(
        IIntegrationMonitorQueries queries,
        IDashboardQueries dashboard,
        SubmitTripResultHandler submit,
        ResolveUnknownMessageHandler resolve,
        ISiteContext siteContext,
        TripEventBus eventBus)
    {
        _queries = queries;
        _dashboard = dashboard;
        _submit = submit;
        _resolve = resolve;
        _eventBus = eventBus;
        _siteId = siteContext.CurrentSiteId;

        Text = Loc.T("integration.title");
        MinimumSize = new Size(1120, 780);
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
            (BuildSubmitCard(), SizeType.AutoSize, 0),
            (BuildResolveCard(), SizeType.AutoSize, 0),
            (BuildListCard(), SizeType.Percent, 100),
            (BuildLogCard(), SizeType.Absolute, 120)));
    }

    private void ConfigureGrid()
    {
        _grid.Columns.AddRange(
            GridBuilder.Column(Loc.T("col.destination"), nameof(IntegrationMessageRow.Destination), 60),
            GridBuilder.Column(Loc.T("col.operation"), nameof(IntegrationMessageRow.Operation), 130),
            GridBuilder.Column(Loc.T("col.business_key"), nameof(IntegrationMessageRow.BusinessKey), 230),
            GridBuilder.Column(Loc.T("col.shipment"), nameof(IntegrationMessageRow.ShipmentNo), 85),
            GridBuilder.Column(Loc.T("col.status"), nameof(IntegrationMessageRow.Status), 90),
            GridBuilder.Column(Loc.T("col.attempts"), nameof(IntegrationMessageRow.Attempts), 70),
            GridBuilder.Column(Loc.T("col.last_attempt"), nameof(IntegrationMessageRow.LastAttemptAt), 115),
            GridBuilder.Column(Loc.T("col.manual_reconcile"),
                nameof(IntegrationMessageRow.RequiresManualReconciliation), 100),
            GridBuilder.Column(Loc.T("col.external_doc"),
                nameof(IntegrationMessageRow.ExternalDocumentRef), 125),
            GridBuilder.Column(Loc.T("col.message"),
                nameof(IntegrationMessageRow.BusinessErrorMessage), 280));

        GridBuilder.AttachEmptyState(_grid, Loc.T("integration.no_messages"));
    }

    private Card BuildSubmitCard()
    {
        var table = FormLayout.FieldTable();

        _cboTrip.DisplayMember = nameof(TripBoardRow.TripNo);
        FormLayout.AddField(table, Loc.T("integration.field_trip"), _cboTrip);

        _cboOperation.DataSource = new[]
        {
            "RECEIVE", "CONFIRM", "WEIGH_RESULT", "IMPURITY_RESULT", "CCS_RESULT"
        };
        FormLayout.AddField(table, Loc.T("integration.field_operation"), _cboOperation);

        _btnQueue.Click += async (_, _) => await QueueAsync();
        FormLayout.AddFullWidth(table, FormLayout.ActionBar(_btnQueue), 54);

        return FormLayout.TitledCard(
            Loc.T("integration.section_submit"),
            table,
            Loc.T("integration.submit_notice"));
    }

    private Card BuildResolveCard()
    {
        var table = FormLayout.FieldTable();

        FormLayout.AddField(table, Loc.T("integration.field_reason"), _txtResolveReason);

        _btnResolveAck.Click += async (_, _) =>
            await ResolveAsync(IntegrationMessageStatus.Acknowledged);
        _btnResolveFailed.Click += async (_, _) =>
            await ResolveAsync(IntegrationMessageStatus.Failed);

        FormLayout.AddFullWidth(table,
            FormLayout.ActionBar(_btnResolveAck, _btnResolveFailed), 54);

        return FormLayout.TitledCard(
            Loc.T("integration.section_resolve"),
            table,
            Loc.T("integration.resolve_notice"));
    }

    private Card BuildListCard()
    {
        var host = new Panel { Dock = DockStyle.Fill };

        _chkOnlyAttention.Text = Loc.T("integration.filter_attention");
        _chkOnlyAttention.AutoSize = true;
        _chkOnlyAttention.Margin = new Padding(0, 6, 0, 0);
        _chkOnlyAttention.CheckedChanged += async (_, _) => await RefreshAsync();

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 36,
            Padding = new Padding(10, 6, 8, 4),
            BackColor = AgrisTheme.Surface,
            WrapContents = false
        };
        toolbar.Controls.Add(_chkOnlyAttention);

        host.Controls.Add(_grid);
        host.Controls.Add(toolbar);

        return FormLayout.TitledCard(Loc.T("integration.section_messages"), host);
    }

    private Card BuildLogCard()
    {
        _txtLog.Multiline = true;
        _txtLog.ReadOnly = true;
        _txtLog.Dock = DockStyle.Fill;
        _txtLog.ScrollBars = ScrollBars.Both;
        _txtLog.WordWrap = false;
        _txtLog.Font = AgrisTheme.Mono(9F);
        _txtLog.BorderStyle = BorderStyle.None;
        _txtLog.AccessibleName = "Nhật ký tích hợp";

        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 6, 8, 6) };
        host.Controls.Add(_txtLog);

        return FormLayout.TitledCard(Loc.T("integration.section_log"), host);
    }

    private async Task RefreshAsync()
    {
        try
        {
            var rows = await _queries.ListAsync(_siteId, _chkOnlyAttention.Checked, _cts.Token);

            GridBuilder.BindAndColorize(_grid, rows, r => r.Status.ToString());

            var trips = await _dashboard.ListTripsAsync(_siteId, new TripBoardFilter(), _cts.Token);
            _cboTrip.SetDataSourceSafe(trips.ToList(), null);
        }
        catch (Exception ex)
        {
            Log(Loc.T("integration.load_error", ex.Message));
        }
    }

    private async Task QueueAsync()
    {
        if (_cboTrip.SelectedItem is not TripBoardRow trip)
        {
            Log(Loc.T("integration.no_trip"));
            return;
        }

        _btnQueue.Enabled = false;
        try
        {
            var destination = trip.ProfileCode.StartsWith("CANE", StringComparison.OrdinalIgnoreCase)
                ? "FRM" : "ERP";

            var result = await _submit.HandleAsync(new SubmitTripResultCommand(
                TripId: trip.TripId,
                Destination: destination,
                Operation: (string)_cboOperation.SelectedItem!,
                ShipmentNo: null,
                CorrelationId: Guid.NewGuid().ToString("N")), _cts.Token);

            Log(result.IsQueued
                ? Loc.T("integration.queue_ok", result.Status, result.BusinessKey, result.Note)
                : Loc.T("integration.queue_failed", result.Note));

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Log(Loc.T("integration.queue_error", ex.Message));
        }
        finally
        {
            _btnQueue.Enabled = true;
        }
    }

    private async Task ResolveAsync(IntegrationMessageStatus resolvedStatus)
    {
        if (_grid.CurrentRow?.DataBoundItem is not IntegrationMessageRow message)
        {
            Log(Loc.T("integration.no_message"));
            return;
        }

        var reason = _txtResolveReason.Text.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            Log(Loc.T("integration.reason_required"));
            return;
        }

        try
        {
            var (isSuccess, failureReason) = await _resolve.HandleAsync(
                new ResolveUnknownMessageCommand(
                    MessageId: message.Id,
                    ResolvedStatus: resolvedStatus,
                    ExternalDocumentRef: null,
                    Reason: reason,
                    CorrelationId: Guid.NewGuid().ToString("N")), _cts.Token);

            Log(isSuccess
                ? Loc.T("integration.resolve_ok", message.Operation, resolvedStatus)
                : Loc.T("integration.resolve_failed", failureReason));

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Log(Loc.T("integration.resolve_error", ex.Message));
        }
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
