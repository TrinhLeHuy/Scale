using TTC.Weighing.Application.Queries;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Controls;

/// <summary>
/// Hiển thị mốc thời gian checkpoint dạng timeline ngang (Req 7.1).
/// Mỗi mốc: vòng tròn có trạng thái + label + thời gian, nối bằng đường kẻ.
/// Dữ liệu đến từ queries; control không tự đánh giá trạng thái.
/// </summary>
public sealed class TimelineControl : UserControl
{
    private IReadOnlyList<CheckpointRow> _checkpoints = [];

    // Kích thước vẽ
    private const int CircleRadius = 14;
    private const int CircleDiameter = CircleRadius * 2;
    private const int LineThickness = 3;
    private const int TopPadding = 12;
    private const int LabelGap = 8;
    private const int TimeGap = 4;

    public TimelineControl()
    {
        DoubleBuffered = true;
        BackColor = AgrisTheme.Surface;
        Height = 110;
        Dock = DockStyle.Top;
        AccessibleName = Loc.T("control.timeline_accessible");

        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw,
            true);
    }

    /// <summary>
    /// Cập nhật danh sách checkpoint và vẽ lại timeline.
    /// </summary>
    public void UpdateCheckpoints(IReadOnlyList<CheckpointRow> checkpoints)
    {
        if (InvokeRequired)
        {
            Invoke(() => UpdateCheckpoints(checkpoints));
            return;
        }

        _checkpoints = checkpoints ?? [];
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var items = BuildDisplayItems();
        if (items.Count == 0) return;

        // Tính toán vị trí ngang cho từng mốc, chia đều trong vùng hiển thị.
        var usableWidth = ClientSize.Width - 40; // padding trái phải 20px
        var spacing = items.Count > 1 ? usableWidth / (items.Count - 1) : 0;
        var startX = 20;
        var centerY = TopPadding + CircleRadius;

        // Vẽ đường nối giữa các mốc.
        if (items.Count > 1)
        {
            for (var i = 0; i < items.Count - 1; i++)
            {
                var x1 = startX + i * spacing + CircleRadius;
                var x2 = startX + (i + 1) * spacing - CircleRadius;

                // Đường nối màu xanh nếu mốc trước đã Done, ngược lại xám.
                var lineColor = items[i].Status == CheckpointStatus.Done
                    ? AgrisTheme.Success
                    : AgrisTheme.Border;

                using var linePen = new Pen(lineColor, LineThickness);
                g.DrawLine(linePen, x1, centerY, x2, centerY);
            }
        }

        // Vẽ từng mốc.
        using var labelFont = AgrisTheme.Body(8.5F, FontStyle.Bold);
        using var timeFont = AgrisTheme.Body(7.5F);
        using var symbolFont = AgrisTheme.Body(10F, FontStyle.Bold);

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var cx = startX + i * spacing;

            DrawCheckpoint(g, item, cx, centerY, labelFont, timeFont, symbolFont);
        }
    }

    private void DrawCheckpoint(
        Graphics g,
        DisplayItem item,
        int cx,
        int cy,
        Font labelFont,
        Font timeFont,
        Font symbolFont)
    {
        var (fillColor, borderColor, symbol, symbolColor) = GetVisual(item.Status);

        // Vẽ vòng tròn nền.
        var circleRect = new Rectangle(cx - CircleRadius, cy - CircleRadius, CircleDiameter, CircleDiameter);

        using (var fillBrush = new SolidBrush(fillColor))
            g.FillEllipse(fillBrush, circleRect);

        using (var borderPen = new Pen(borderColor, 2))
            g.DrawEllipse(borderPen, circleRect);

        // Vẽ ký hiệu trạng thái bên trong vòng tròn.
        var symbolSize = TextRenderer.MeasureText(symbol, symbolFont);
        var symbolX = cx - symbolSize.Width / 2;
        var symbolY = cy - symbolSize.Height / 2;

        TextRenderer.DrawText(g, symbol, symbolFont,
            new Point(symbolX, symbolY), symbolColor);

        // Vẽ label bên dưới vòng tròn.
        var labelY = cy + CircleRadius + LabelGap;
        var labelSize = TextRenderer.MeasureText(item.Label, labelFont);

        TextRenderer.DrawText(g, item.Label, labelFont,
            new Rectangle(cx - 60, labelY, 120, labelSize.Height),
            AgrisTheme.TextPrimary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.WordEllipsis);

        // Vẽ thời gian bên dưới label (nếu có).
        if (item.Time is not null)
        {
            var timeText = item.Time.Value.LocalDateTime.ToString("HH:mm dd/MM");
            var timeY = labelY + labelSize.Height + TimeGap;

            TextRenderer.DrawText(g, timeText, timeFont,
                new Rectangle(cx - 60, timeY, 120, 16),
                AgrisTheme.TextMuted,
                TextFormatFlags.HorizontalCenter);
        }
    }

    /// <summary>Trả về (nền, viền, ký hiệu, màu ký hiệu) theo trạng thái checkpoint.</summary>
    private static (Color Fill, Color Border, string Symbol, Color SymbolColor) GetVisual(
        CheckpointStatus status) => status switch
    {
        CheckpointStatus.Done => (AgrisTheme.SuccessSoft, AgrisTheme.Success, "✓", AgrisTheme.Success),
        CheckpointStatus.Active => (AgrisTheme.WarningSoft, AgrisTheme.Warning, "●", AgrisTheme.Warning),
        CheckpointStatus.Rejected => (AgrisTheme.DangerSoft, AgrisTheme.Danger, "✕", AgrisTheme.Danger),
        CheckpointStatus.Waived => (AgrisTheme.DangerSoft, AgrisTheme.Danger, "⊗", AgrisTheme.Danger),
        _ => (AgrisTheme.SurfaceMuted, AgrisTheme.Border, "◯", AgrisTheme.Neutral) // Pending
    };

    /// <summary>Xây dựng danh sách hiển thị từ checkpoint data — dynamic theo profile.</summary>
    private IReadOnlyList<DisplayItem> BuildDisplayItems()
    {
        // Hiển thị đúng thứ tự checkpoint được trả từ queries (đã tùy theo FRM/ERP)
        if (_checkpoints.Count == 0)
            return [];

        var items = new List<DisplayItem>(_checkpoints.Count);

        foreach (var cp in _checkpoints)
        {
            var time = cp.CompletedAt ?? cp.StartedAt;
            items.Add(new DisplayItem(CheckpointLabel(cp.Type), cp.Status, time));
        }

        return items;
    }

    /// <summary>Nhãn checkpoint đa ngôn ngữ.</summary>
    private static string CheckpointLabel(CheckpointType type) => type switch
    {
        CheckpointType.Registration => Loc.T("checkpoint.registration"),
        CheckpointType.GateIn => Loc.T("checkpoint.gate_in"),
        CheckpointType.FirstWeight => Loc.T("checkpoint.first_weight"),
        CheckpointType.SecondWeight => Loc.T("checkpoint.second_weight"),
        CheckpointType.ImpuritySample => Loc.T("checkpoint.impurity"),
        CheckpointType.CcsSample => Loc.T("checkpoint.ccs"),
        CheckpointType.WarehouseIn => Loc.T("checkpoint.warehouse_in"),
        CheckpointType.WarehouseOut => Loc.T("checkpoint.warehouse_out"),
        CheckpointType.Reconciliation => Loc.T("checkpoint.reconciliation"),
        CheckpointType.Sync => Loc.T("checkpoint.sync"),
        CheckpointType.GateOut => Loc.T("checkpoint.gate_out"),
        _ => type.ToString()
    };

    private sealed record DisplayItem(string Label, CheckpointStatus Status, DateTimeOffset? Time);
}
