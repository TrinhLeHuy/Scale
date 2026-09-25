using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Presenters;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Controls;

/// <summary>
/// Thông tin chuyến, bàn cân và lượt cân đang thực hiện (UI-03).
/// Chỉ hiển thị; không cho sửa dữ liệu chuyến từ màn hình cân.
/// </summary>
public sealed class TripInfoControl : UserControl
{
    private readonly TableLayoutPanel _grid;
    private readonly Label _lblMessage;

    private readonly Label _tripNo = ValueLabel();
    private readonly Label _reference = ValueLabel();
    private readonly Label _plate = ValueLabel();
    private readonly Label _profile = ValueLabel();
    private readonly Label _bridge = ValueLabel();
    private readonly Label _checkpoint = ValueLabel();

    public TripInfoControl()
    {
        BackColor = AgrisTheme.Surface;
        Padding = new Padding(14, 10, 14, 10);

        _lblMessage = new Label
        {
            Dock = DockStyle.Fill,
            Font = AgrisTheme.Body(10F, FontStyle.Italic),
            ForeColor = AgrisTheme.Danger,
            TextAlign = ContentAlignment.MiddleLeft,
            Visible = false
        };

        _grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 3,
            BackColor = AgrisTheme.Surface
        };

        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        for (var i = 0; i < 3; i++)
            _grid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.34F));

        AddRow(0, Loc.T("control.trip_no"), _tripNo, Loc.T("control.trip_profile"), _profile);
        AddRow(1, Loc.T("control.trip_reference"), _reference, Loc.T("control.trip_bridge"), _bridge);
        AddRow(2, Loc.T("control.trip_plate"), _plate, Loc.T("control.trip_checkpoint"), _checkpoint);

        Controls.Add(_grid);
        Controls.Add(_lblMessage);
    }

    public void Render(WeighWorkspace workspace, string? profileCode, string? role)
    {
        if (InvokeRequired)
        {
            Invoke(() => Render(workspace, profileCode, role));
            return;
        }

        _lblMessage.Visible = false;
        _grid.Visible = true;

        _tripNo.Text = workspace.TripNo;
        _reference.Text = workspace.ReferenceNo ?? "—";
        _plate.Text = workspace.Plate ?? Loc.T("control.trip_plate_no_anpr");
        _profile.Text = profileCode ?? "—";
        _bridge.Text = workspace.WeighbridgeCode;

        _checkpoint.Text = role is null
            ? workspace.Checkpoint.ToString()
            : $"{workspace.Checkpoint}  ·  {Loc.T("control.trip_checkpoint_role", role)}";

        AccessibleDescription = Loc.T("control.trip_accessible_desc", workspace.TripNo, workspace.WeighbridgeCode);
    }

    public void RenderEmpty(string message)
    {
        if (InvokeRequired)
        {
            Invoke(() => RenderEmpty(message));
            return;
        }

        _grid.Visible = false;
        _lblMessage.Text = message;
        _lblMessage.Visible = true;

        AccessibleDescription = message;
    }

    private void AddRow(int row, string label1, Label value1, string label2, Label value2)
    {
        _grid.Controls.Add(CaptionLabel(label1), 0, row);
        _grid.Controls.Add(value1, 1, row);
        _grid.Controls.Add(CaptionLabel(label2), 2, row);
        _grid.Controls.Add(value2, 3, row);
    }

    private static Label CaptionLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        Font = AgrisTheme.Body(9F),
        ForeColor = AgrisTheme.TextMuted,
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static Label ValueLabel() => new()
    {
        Text = "—",
        Dock = DockStyle.Fill,
        Font = AgrisTheme.Body(10F, FontStyle.Bold),
        ForeColor = AgrisTheme.TextPrimary,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true
    };
}
