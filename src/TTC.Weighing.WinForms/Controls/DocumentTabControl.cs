using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Controls;

/// <summary>
/// TabControl kiểu tài liệu: mỗi màn hình là một tab có nút đóng, đóng được bằng chuột giữa.
/// Dùng thay MDI để người vận hành chuyển màn hình nhanh mà không phải sắp xếp cửa sổ.
/// </summary>
public sealed class DocumentTabControl : TabControl
{
    private const int CloseGlyphSize = 14;
    private const int CloseGlyphPadding = 9;

    private int _hoveredCloseIndex = -1;

    /// <summary>Phát khi người dùng bấm nút đóng của một tab.</summary>
    public event Action<TabPage>? TabCloseRequested;

    public DocumentTabControl()
    {
        DrawMode = TabDrawMode.OwnerDrawFixed;
        SizeMode = TabSizeMode.Fixed;
        ItemSize = new Size(238, 34);
        Padding = new Point(0, 0);
        Dock = DockStyle.Fill;
        Font = AgrisTheme.Body();
        AccessibleName = Loc.T("control.tab_accessible");

        SetStyle(
            ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw, true);
    }

    /// <summary>
    /// Chặn WM_ERASEBKGND (0x0014) — nguyên nhân chính gây chớp nháy tab strip.
    /// OnPaintBackground đã vẽ nền nên không cần hệ thống xóa trước.
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0014) // WM_ERASEBKGND
        {
            m.Result = (IntPtr)1; // nói với Windows "đã xóa rồi"
            return;
        }

        base.WndProc(ref m);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Vẽ nền cho cả dải tab và vùng nội dung để không lộ màu xám hệ thống.
        using var brush = new SolidBrush(AgrisTheme.Canvas);
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // UserPaint mode: phải vẽ toàn bộ tab strip thủ công.
        // Vẽ nền trước, rồi vẽ từng tab item.
        OnPaintBackground(e);

        if (TabPages.Count == 0) return;

        for (var i = 0; i < TabPages.Count; i++)
        {
            var args = new DrawItemEventArgs(
                e.Graphics, Font, GetTabRect(i), i,
                i == SelectedIndex ? DrawItemState.Selected : DrawItemState.Default);
            OnDrawItem(args);
        }
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        var page = TabPages[e.Index];
        var bounds = GetTabRect(e.Index);
        var isSelected = SelectedIndex == e.Index;

        var background = isSelected ? AgrisTheme.Surface : AgrisTheme.SurfaceMuted;
        using (var brush = new SolidBrush(background))
            e.Graphics.FillRectangle(brush, bounds);

        using (var pen = new Pen(AgrisTheme.Border))
            e.Graphics.DrawLine(pen, bounds.Right - 1, bounds.Top + 6, bounds.Right - 1, bounds.Bottom - 6);

        // Vạch nhấn màu thương hiệu cho tab đang chọn.
        if (isSelected)
        {
            using var accent = new SolidBrush(AgrisTheme.BrandGreen);
            e.Graphics.FillRectangle(accent, bounds.Left, bounds.Top, bounds.Width, 3);
        }

        var closeRect = GetCloseRect(bounds);

        var textRect = new Rectangle(
            bounds.Left + 12,
            bounds.Top + 4,
            bounds.Width - closeRect.Width - 26,
            bounds.Height - 6);

        TextRenderer.DrawText(
            e.Graphics, page.Text,
            AgrisTheme.Body(9.5F, isSelected ? FontStyle.Bold : FontStyle.Regular),
            textRect,
            isSelected ? AgrisTheme.TextPrimary : AgrisTheme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        DrawCloseGlyph(e.Graphics, closeRect, _hoveredCloseIndex == e.Index);
    }

    private static void DrawCloseGlyph(Graphics graphics, Rectangle rect, bool hovered)
    {
        if (hovered)
        {
            using var hover = new SolidBrush(AgrisTheme.Danger);
            graphics.FillRectangle(hover, rect);
        }

        var color = hovered ? Color.White : AgrisTheme.TextMuted;
        using var pen = new Pen(color, 1.6f);

        const int inset = 4;
        graphics.DrawLine(pen,
            rect.Left + inset, rect.Top + inset,
            rect.Right - inset, rect.Bottom - inset);
        graphics.DrawLine(pen,
            rect.Right - inset, rect.Top + inset,
            rect.Left + inset, rect.Bottom - inset);
    }

    private static Rectangle GetCloseRect(Rectangle tabBounds) => new(
        tabBounds.Right - CloseGlyphSize - CloseGlyphPadding,
        tabBounds.Top + (tabBounds.Height - CloseGlyphSize) / 2 + 1,
        CloseGlyphSize,
        CloseGlyphSize);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var index = FindCloseButtonAt(e.Location);

        if (index != _hoveredCloseIndex)
        {
            _hoveredCloseIndex = index;
            // Chỉ invalidate vùng tab strip, không invalidate toàn bộ control (gây flicker content).
            InvalidateTabStrip();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_hoveredCloseIndex != -1)
        {
            _hoveredCloseIndex = -1;
            InvalidateTabStrip();
        }

        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            var index = FindCloseButtonAt(e.Location);
            if (index >= 0)
            {
                TabCloseRequested?.Invoke(TabPages[index]);
                return;
            }
        }

        // Chuột giữa đóng tab, giống thói quen trên trình duyệt.
        if (e.Button == MouseButtons.Middle)
        {
            var index = FindTabAt(e.Location);
            if (index >= 0)
            {
                TabCloseRequested?.Invoke(TabPages[index]);
                return;
            }
        }

        base.OnMouseDown(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.W) && SelectedTab is not null)
        {
            TabCloseRequested?.Invoke(SelectedTab);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private int FindCloseButtonAt(Point location)
    {
        for (var i = 0; i < TabPages.Count; i++)
        {
            if (GetCloseRect(GetTabRect(i)).Contains(location))
                return i;
        }

        return -1;
    }

    private int FindTabAt(Point location)
    {
        for (var i = 0; i < TabPages.Count; i++)
        {
            if (GetTabRect(i).Contains(location))
                return i;
        }

        return -1;
    }

    /// <summary>Invalidate chỉ vùng tab strip (hàng tiêu đề), không chạm vào nội dung tab bên dưới.</summary>
    private void InvalidateTabStrip()
    {
        if (TabPages.Count == 0) return;

        var stripHeight = GetTabRect(0).Bottom + 2;
        Invalidate(new Rectangle(0, 0, Width, stripHeight), false);
    }
}
