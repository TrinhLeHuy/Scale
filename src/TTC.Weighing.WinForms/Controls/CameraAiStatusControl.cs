using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Theme;
using TTC.Weighing.Domain.Common;

namespace TTC.Weighing.WinForms.Controls;

/// <summary>
/// Hiển thị kết quả AI camera tại bàn cân: phát hiện người, vị trí xe, thùng hàng.
/// Mỗi mục có icon trạng thái (✓/⚠/✗) và text mô tả.
/// Cập nhật qua <see cref="UpdateResult"/>.
/// </summary>
public sealed class CameraAiStatusControl : UserControl
{
    private readonly Label _lblPerson;
    private readonly Label _lblAlignment;
    private readonly Label _lblCargo;
    private readonly Label _lblTimestamp;

    public CameraAiStatusControl()
    {
        BackColor = AgrisTheme.Surface;
        Dock = DockStyle.Fill;
        Padding = new Padding(6, 4, 6, 4);

        var header = new Label
        {
            Text = Loc.T("control.ai_header"),
            Font = AgrisTheme.Body(9F, FontStyle.Bold),
            ForeColor = AgrisTheme.BrandGreenDark,
            Dock = DockStyle.Top,
            Height = 26,
            AccessibleName = Loc.T("control.ai_header")
        };

        _lblPerson = CreateStatusLabel(Loc.T("control.ai_person"));
        _lblAlignment = CreateStatusLabel(Loc.T("control.ai_alignment"));
        _lblCargo = CreateStatusLabel(Loc.T("control.ai_cargo"));
        _lblTimestamp = new Label
        {
            Text = string.Empty,
            Font = AgrisTheme.Caption(),
            ForeColor = AgrisTheme.TextMuted,
            Dock = DockStyle.Bottom,
            Height = 20,
            TextAlign = ContentAlignment.MiddleRight
        };

        // Stack bottom-up (WinForms Dock stacking)
        Controls.Add(_lblTimestamp);
        Controls.Add(_lblCargo);
        Controls.Add(_lblAlignment);
        Controls.Add(_lblPerson);
        Controls.Add(header);

        // Default state
        ShowWaitingState();
    }

    /// <summary>Cập nhật kết quả phân tích AI. Thread-safe.</summary>
    public void UpdateResult(CameraAiAnalysisResult? result)
    {
        if (InvokeRequired)
        {
            Invoke(() => UpdateResult(result));
            return;
        }

        if (result is null)
        {
            ShowWaitingState();
            return;
        }

        // ── Người trên xe ──
        if (result.PersonDetected)
        {
            SetStatus(_lblPerson, Loc.T("control.ai_person_detected"),
                AgrisTheme.Danger, $"({result.PersonConfidence:P0})");
        }
        else
        {
            SetStatus(_lblPerson, Loc.T("control.ai_person_clear"),
                AgrisTheme.Success, $"({result.PersonConfidence:P0})");
        }

        // ── Vị trí xe ──
        var (alignText, alignColor) = result.Alignment switch
        {
            VehicleAlignmentStatus.Aligned => (Loc.T("control.ai_aligned"), AgrisTheme.Success),
            VehicleAlignmentStatus.SlightlyMisaligned => (Loc.T("control.ai_slight_misalign"), AgrisTheme.Warning),
            VehicleAlignmentStatus.Misaligned => (Loc.T("control.ai_misaligned"), AgrisTheme.Danger),
            VehicleAlignmentStatus.NoVehicle => (Loc.T("control.ai_no_vehicle"), AgrisTheme.TextMuted),
            _ => (Loc.T("control.ai_unknown"), AgrisTheme.TextMuted)
        };
        SetStatus(_lblAlignment, alignText, alignColor, $"({result.AlignmentConfidence:P0})");

        // ── Thùng hàng ──
        var (cargoText, cargoColor) = result.CargoStatus switch
        {
            CargoStatus.Open => (Loc.T("control.ai_cargo_open"), AgrisTheme.Success),
            CargoStatus.Closed => (Loc.T("control.ai_cargo_closed"), AgrisTheme.Danger),
            CargoStatus.PartiallyOpen => (Loc.T("control.ai_cargo_partial"), AgrisTheme.Warning),
            CargoStatus.Empty => (Loc.T("control.ai_cargo_empty"), AgrisTheme.TextMuted),
            _ => (Loc.T("control.ai_unknown"), AgrisTheme.TextMuted)
        };
        SetStatus(_lblCargo, cargoText, cargoColor, $"({result.CargoConfidence:P0})");

        // ── Timestamp ──
        _lblTimestamp.Text = $"Model {result.ModelVersion} — {result.AnalyzedAt.ToVnTime():HH:mm:ss}";
    }

    private void ShowWaitingState()
    {
        SetStatus(_lblPerson, Loc.T("control.ai_waiting"), AgrisTheme.TextMuted, string.Empty);
        SetStatus(_lblAlignment, Loc.T("control.ai_waiting"), AgrisTheme.TextMuted, string.Empty);
        SetStatus(_lblCargo, Loc.T("control.ai_waiting"), AgrisTheme.TextMuted, string.Empty);
        _lblTimestamp.Text = string.Empty;
    }

    private static void SetStatus(Label lbl, string text, Color color, string confidence)
    {
        lbl.Text = string.IsNullOrEmpty(confidence) ? text : $"{text}  {confidence}";
        lbl.ForeColor = color;
    }

    private static Label CreateStatusLabel(string accessibleName) => new()
    {
        Text = $"— {accessibleName}: chờ...",
        Font = AgrisTheme.Body(9.5F),
        ForeColor = AgrisTheme.TextMuted,
        Dock = DockStyle.Top,
        Height = 28,
        Padding = new Padding(2, 3, 0, 0),
        AccessibleName = accessibleName
    };
}
