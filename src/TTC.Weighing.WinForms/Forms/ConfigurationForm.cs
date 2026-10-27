using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.Queries;
using TTC.Weighing.Domain.Entities;
using TTC.Weighing.WinForms.Controls;
using TTC.Weighing.WinForms.Localization;

namespace TTC.Weighing.WinForms.Forms;

/// <summary>
/// UI-09 — Thiết bị và cấu hình. Hiển thị ánh xạ làn/thiết bị, hồ sơ nghiệp vụ và các ngưỡng.
/// Ngưỡng để trống nghĩa là chưa duyệt và sẽ chặn chốt cân tự động (D-04/D-05).
/// </summary>
public sealed class ConfigurationForm : Form
{
    private readonly IWorkflowProfileRepository _profiles;
    private readonly IWeighbridgeRepository _weighbridges;
    private readonly IDashboardQueries _dashboard;
    private readonly Guid _siteId;
    private readonly CancellationTokenSource _cts = new();

    private readonly DataGridView _profileGrid = GridBuilder.ReadOnlyGrid("Hồ sơ nghiệp vụ");
    private readonly DataGridView _bridgeGrid = GridBuilder.ReadOnlyGrid("Bàn cân");
    private readonly DataGridView _deviceGrid = GridBuilder.ReadOnlyGrid("Thiết bị và ánh xạ làn");
    private readonly TextBox _txtThresholds = new();

    public ConfigurationForm(
        IWorkflowProfileRepository profiles,
        IWeighbridgeRepository weighbridges,
        IDashboardQueries dashboard,
        ISiteContext siteContext)
    {
        _profiles = profiles;
        _weighbridges = weighbridges;
        _dashboard = dashboard;
        _siteId = siteContext.CurrentSiteId;

        Text = Loc.T("config.title");
        MinimumSize = new Size(1060, 760);
        BackColor = TTC.Weighing.WinForms.Theme.AgrisTheme.Canvas;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        BuildLayout();
        Load += async (_, _) => await LoadAsync();
    }

    private void BuildLayout()
    {
        _profileGrid.Columns.AddRange(
            GridBuilder.Column(Loc.T("col.profile_code"), nameof(WorkflowProfile.Code), 140),
            GridBuilder.Column(Loc.T("col.profile_name"), nameof(WorkflowProfile.Name), 200),
            GridBuilder.Column(Loc.T("col.profile_version"), nameof(WorkflowProfile.ProfileVersion), 70),
            GridBuilder.Column(Loc.T("col.profile_source"), nameof(WorkflowProfile.SourceSystem), 70),
            GridBuilder.Column(Loc.T("col.profile_direction"), nameof(WorkflowProfile.Direction), 110),
            GridBuilder.Column(Loc.T("col.gate_required"), nameof(WorkflowProfile.GateCheckpointsRequired), 80),
            GridBuilder.Column(Loc.T("col.impurity_before_2nd"),
                nameof(WorkflowProfile.ImpurityRequiredBeforeSecondWeight), 120),
            GridBuilder.Column(Loc.T("col.warehouse_confirm"),
                nameof(WorkflowProfile.WarehouseConfirmationRequired), 100),
            GridBuilder.Column(Loc.T("col.approved_by"), nameof(WorkflowProfile.ApprovedBy), 120));

        _profileGrid.SelectionChanged += (_, _) => RenderThresholds();

        _bridgeGrid.Columns.AddRange(
            GridBuilder.Column(Loc.T("col.device_code"), nameof(Weighbridge.Code), 130),
            GridBuilder.Column(Loc.T("col.min_capacity"), nameof(Weighbridge.MinCapacityKg), 140, "N3"),
            GridBuilder.Column(Loc.T("col.max_capacity"), nameof(Weighbridge.MaxCapacityKg), 140, "N3"),
            GridBuilder.Column(Loc.T("col.resolution"), nameof(Weighbridge.ResolutionKg), 120, "N3"),
            GridBuilder.Column(Loc.T("col.out_of_service"), nameof(Weighbridge.IsOutOfService), 100));

        _deviceGrid.Columns.AddRange(
            GridBuilder.Column(Loc.T("col.device_code"), nameof(DeviceHealthRow.DeviceCode), 130),
            GridBuilder.Column(Loc.T("col.device_class"), nameof(DeviceHealthRow.DeviceClass), 120),
            GridBuilder.Column(Loc.T("col.lane"), nameof(DeviceHealthRow.LaneCode), 80),
            GridBuilder.Column(Loc.T("col.enabled"), nameof(DeviceHealthRow.IsEnabled), 60),
            GridBuilder.Column(Loc.T("col.last_seen"), nameof(DeviceHealthRow.LastSeenAt), 140));

        GridBuilder.AttachEmptyState(_profileGrid, Loc.T("config.no_profiles"));
        GridBuilder.AttachEmptyState(_bridgeGrid, Loc.T("config.no_bridges"));
        GridBuilder.AttachEmptyState(_deviceGrid, Loc.T("config.no_devices"));

        _txtThresholds.Multiline = true;
        _txtThresholds.ReadOnly = true;
        _txtThresholds.Dock = DockStyle.Fill;
        _txtThresholds.Font = new Font("Consolas", 9.5F);
        _txtThresholds.ScrollBars = ScrollBars.Vertical;
        _txtThresholds.AccessibleName = "Ngưỡng của hồ sơ nghiệp vụ đang chọn";

        var thresholdPanel = new Panel { Dock = DockStyle.Bottom, Height = 170 };
        thresholdPanel.Controls.Add(_txtThresholds);
        thresholdPanel.Controls.Add(GridBuilder.SectionHeader(Loc.T("config.section_thresholds")));

        var lower = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 520
        };

        var bridgePanel = new Panel { Dock = DockStyle.Fill };
        bridgePanel.Controls.Add(_bridgeGrid);
        bridgePanel.Controls.Add(GridBuilder.SectionHeader(Loc.T("config.section_bridges")));

        var devicePanel = new Panel { Dock = DockStyle.Fill };
        devicePanel.Controls.Add(_deviceGrid);
        devicePanel.Controls.Add(GridBuilder.SectionHeader(Loc.T("config.section_devices")));

        lower.Panel1.Controls.Add(bridgePanel);
        lower.Panel2.Controls.Add(devicePanel);

        var profilePanel = new Panel { Dock = DockStyle.Top, Height = 220 };
        profilePanel.Controls.Add(_profileGrid);
        profilePanel.Controls.Add(GridBuilder.SectionHeader(Loc.T("config.section_profiles")));

        Controls.Add(lower);
        Controls.Add(thresholdPanel);
        Controls.Add(profilePanel);
        Controls.Add(GridBuilder.Notice(
            Loc.T("config.notice")));
    }

    private async Task LoadAsync()
    {
        try
        {
            var profiles = await _profiles.ListApprovedAsync(_siteId, _cts.Token);
            _profileGrid.DataSource = profiles.ToList();

            var bridges = await _weighbridges.ListBySiteAsync(_siteId, _cts.Token);
            _bridgeGrid.DataSource = bridges.ToList();

            var overview = await _dashboard.GetOverviewAsync(_siteId, _cts.Token);
            _deviceGrid.DataSource = overview.Devices.ToList();

            RenderThresholds();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{Loc.T("config.load_error")}{Environment.NewLine}{ex.Message}",
                Loc.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RenderThresholds()
    {
        if (_profileGrid.CurrentRow?.DataBoundItem is not WorkflowProfile profile)
        {
            _txtThresholds.Text = string.Empty;
            return;
        }

        var t = profile.Thresholds;

        var lines = new List<string>
        {
            Loc.T("config.threshold_profile", profile.Code, profile.ProfileVersion),
            Loc.T("config.threshold_roles", string.Join(" -> ", profile.WeightRoleSequence)),
            Loc.T("config.threshold_checkpoints", string.Join(" -> ", profile.Checkpoints)),
            string.Empty,
            Format(Loc.T("config.threshold_stability"), t.StabilityWindow?.TotalSeconds, "giây", "profile"),
            Format(Loc.T("config.threshold_delta"), t.StabilityDeltaKg, "kg", "D-04"),
            Format(Loc.T("config.threshold_signal_age"), t.ScaleSignalMaxAge?.TotalMilliseconds, "ms", "D-04"),
            Format(Loc.T("config.threshold_ai_age"), t.AiEvidenceMaxAge?.TotalMilliseconds, "ms", "D-05"),
            Format(Loc.T("config.threshold_ai_confidence"), t.PositionConfidenceThreshold, string.Empty, "D-05")
        };

        if (!profile.IsApprovedForProduction)
        {
            lines.Add(string.Empty);
            lines.Add(Loc.T("config.threshold_not_approved"));
        }

        _txtThresholds.Lines = [.. lines];
    }

    private static string Format(string label, decimal? value, string unit, string decisionRef)
        => value is null
            ? $"{label,-32}: {Loc.T("config.threshold_pending", decisionRef)}"
            : $"{label,-32}: {value} {unit}".TrimEnd();

    private static string Format(string label, double? value, string unit, string decisionRef)
        => Format(label, value is null ? null : (decimal)value.Value, unit, decisionRef);

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _cts.Cancel();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _cts.Dispose();
        base.Dispose(disposing);
    }
}
