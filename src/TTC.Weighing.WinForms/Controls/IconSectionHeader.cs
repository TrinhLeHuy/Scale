using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Controls;

/// <summary>
/// Tiêu đề khu vực trong thẻ: ô icon bo tròn (font icon Windows), tiêu đề đậm, phụ đề nhạt,
/// vùng hành động bên phải (nút nhỏ). Máy không có font icon → bỏ ô icon, giữ chữ.
/// </summary>
public sealed class IconSectionHeader : Panel
{
    private readonly string _glyph;
    private readonly Color _accent;
    private string _title;
    private string? _subtitle;

    /// <summary>Vùng chứa nút bên phải tiêu đề.</summary>
    public FlowLayoutPanel Actions { get; } = new()
    {
        Dock = DockStyle.Right,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        FlowDirection = FlowDirection.RightToLeft,
        WrapContents = false,
        Padding = new Padding(0, 10, 12, 0),
        BackColor = Color.Transparent
    };

    public IconSectionHeader(string glyph, string title, Color accent, string? subtitle = null)
    {
        _glyph = glyph;
        _title = title;
        _accent = accent;
        _subtitle = subtitle;

        Dock = DockStyle.Top;
        Height = subtitle is null ? 52 : 60;
        BackColor = AgrisTheme.CardHeader;
        AccessibleName = title;
        AccessibleRole = AccessibleRole.Grouping;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Controls.Add(Actions);
    }

    public void SetSubtitle(string? subtitle)
    {
        _subtitle = subtitle;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var x = 14;
        if (AgrisTheme.Icon(13F) is { } iconFont)
        {
            var box = new Rectangle(x, (Height - 34) / 2, 34, 34);
            using (var path = Shapes.RoundedRect(box, 9))
            using (var fill = new SolidBrush(AgrisTheme.Blend(AgrisTheme.Surface, _accent, 0.14f)))
                g.FillPath(fill, path);
            TextRenderer.DrawText(g, _glyph, iconFont, box, _accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            x = box.Right + 12;
        }

        var right = Actions.Visible && Actions.Controls.Count > 0 ? Actions.Left - 8 : Width - 12;
        var titleFont = AgrisTheme.Body(11F, FontStyle.Bold);
        var flags = TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;

        if (string.IsNullOrEmpty(_subtitle))
        {
            TextRenderer.DrawText(g, _title, titleFont, new Rectangle(x, 0, right - x, Height), AgrisTheme.TextPrimary,
                flags | TextFormatFlags.VerticalCenter);
        }
        else
        {
            var subFont = AgrisTheme.Caption();
            var th = TextRenderer.MeasureText("Ág", titleFont, Size.Empty, TextFormatFlags.NoPadding).Height;
            var sh = TextRenderer.MeasureText("Ág", subFont, Size.Empty, TextFormatFlags.NoPadding).Height;
            var top = (Height - th - sh - 2) / 2;
            TextRenderer.DrawText(g, _title, titleFont, new Rectangle(x, top, right - x, th), AgrisTheme.TextPrimary, flags);
            TextRenderer.DrawText(g, _subtitle, subFont, new Rectangle(x, top + th + 2, right - x, sh), AgrisTheme.TextMuted, flags);
        }

        // Vạch nhấn trái theo màu khu vực + đường phân cách với thân thẻ.
        using (var accentBar = new SolidBrush(_accent))
            g.FillRectangle(accentBar, 0, 0, 4, Height);
        using var pen = new Pen(AgrisTheme.BorderStrong);
        g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }
}
