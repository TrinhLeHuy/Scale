using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Controls;

/// <summary>
/// Thẻ một thiết bị: dải màu trạng thái bên trái, ô biểu tượng theo nhóm, tên thiết bị, dòng phụ
/// (hãng · kết nối), dòng chi tiết (mã USB / cổng), hàng nhãn (trạng thái + vai trò trong phần mềm).
/// Vẽ tay để gọn và đồng nhất; nội dung đầy đủ nằm trong tooltip.
/// </summary>
public sealed class DeviceCard : Control
{
    public static int CARD_WIDTH => Theme.UiScale.Px(330);
    public static int CARD_HEIGHT => Theme.UiScale.Px(118);

    /// <summary>Nhãn nhỏ ở hàng dưới (trạng thái / vai trò).</summary>
    public sealed record Pill(string Text, Color Fore, Color Back);

    private readonly string _glyph;
    private readonly Color _accent;
    private readonly Color _healthColor;
    private readonly string _title;
    private readonly string? _subtitle;
    private readonly string? _detail;
    private readonly IReadOnlyList<Pill> _pills;
    private bool _hover;

    public DeviceCard(string glyph, Color accent, Color healthColor, string title, string? subtitle, string? detail,
        IReadOnlyList<Pill> pills)
    {
        _glyph = glyph;
        _accent = accent;
        _healthColor = healthColor;
        _title = title;
        _subtitle = subtitle;
        _detail = detail;
        _pills = pills;

        Size = new Size(CARD_WIDTH, CARD_HEIGHT);
        Margin = new Padding(0, 0, 12, 12);
        BackColor = AgrisTheme.Surface;
        AccessibleRole = AccessibleRole.Grouping;
        AccessibleName = title;
        AccessibleDescription = string.Join(", ", new[] { subtitle, detail }.Concat(pills.Select(p => p.Text))
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(_hover ? AgrisTheme.SurfaceHover : AgrisTheme.Surface);

        using (var border = new Pen(_hover ? AgrisTheme.BorderStrong : AgrisTheme.Border))
            g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        using (var bar = new SolidBrush(_healthColor))
            g.FillRectangle(bar, 0, 0, 4, Height);

        // Ô biểu tượng
        var icon = new Rectangle(16, 14, 40, 40);
        using (var path = Shapes.RoundedRect(icon, 10))
        using (var fill = new SolidBrush(AgrisTheme.Blend(AgrisTheme.Surface, _accent, 0.14f)))
            g.FillPath(fill, path);
        if (AgrisTheme.Icon(15F) is { } iconFont)
            TextRenderer.DrawText(g, _glyph, iconFont, icon, _accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        // Chữ
        var textLeft = icon.Right + 12;
        var textWidth = Width - textLeft - 12;
        const TextFormatFlags line = TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix
                                     | TextFormatFlags.NoPadding;
        TextRenderer.DrawText(g, _title, AgrisTheme.Body(10.5F, FontStyle.Bold),
            new Rectangle(textLeft, 12, textWidth, 22), AgrisTheme.TextPrimary, line);
        if (!string.IsNullOrWhiteSpace(_subtitle))
            TextRenderer.DrawText(g, _subtitle, AgrisTheme.Body(9F),
                new Rectangle(textLeft, 35, textWidth, 18), AgrisTheme.TextSecondary, line);
        if (!string.IsNullOrWhiteSpace(_detail))
            TextRenderer.DrawText(g, _detail, AgrisTheme.Mono(8.5F),
                new Rectangle(textLeft, 54, textWidth, 16), AgrisTheme.TextMuted, line);

        // Nhãn
        var x = 16;
        var y = Height - 34;
        var pillFont = AgrisTheme.Body(8.5F, FontStyle.Bold);
        foreach (var pill in _pills)
        {
            var w = TextRenderer.MeasureText(g, pill.Text, pillFont, Size.Empty, TextFormatFlags.NoPadding).Width + 18;
            w = Math.Min(w, Width - x - 12);
            if (w < 40) break;
            var r = new Rectangle(x, y, w, 22);
            using (var path = Shapes.RoundedRect(r, 11))
            using (var back = new SolidBrush(pill.Back))
                g.FillPath(back, path);
            TextRenderer.DrawText(g, pill.Text, pillFont, r, pill.Fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            x = r.Right + 6;
        }
    }
}
