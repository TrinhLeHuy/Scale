using System.Drawing.Drawing2D;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Controls;

/// <summary>
/// Thanh điều hướng dọc bên trái của shell: nhóm chức năng theo luồng nghiệp vụ,
/// mục đang mở được đánh dấu, thu gọn được về dạng chỉ icon (Ctrl+B).
/// Điều khiển được bằng bàn phím: Tab vào, mũi tên lên/xuống, Enter/Space để mở.
/// </summary>
public sealed class NavigationSidebar : Panel
{
    public static int ExpandedWidth => Theme.UiScale.Px(276);
    public static int CollapsedWidth => Theme.UiScale.Px(68);

    private readonly FlowLayoutPanel _list;
    private readonly ToolTip _toolTip = new() { InitialDelay = 300, ShowAlways = true };
    private readonly List<NavItem> _items = [];
    private readonly List<NavGroupLabel> _groups = [];
    private bool _collapsed;

    public NavigationSidebar()
    {
        Dock = DockStyle.Left;
        Width = ExpandedWidth;
        BackColor = AgrisTheme.NavBack;
        Padding = new Padding(0, 10, 0, 10);

        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        _list = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = AgrisTheme.NavBack,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        _list.Resize += (_, _) => SyncWidths();

        Controls.Add(_list);
        AccessibleRole = AccessibleRole.MenuBar;
    }

    /// <summary>Thu gọn về dạng chỉ icon.</summary>
    public bool Collapsed
    {
        get => _collapsed;
        set
        {
            if (_collapsed == value) return;
            _collapsed = value;

            SuspendLayout();
            Width = value ? CollapsedWidth : ExpandedWidth;

            foreach (var group in _groups)
                group.Collapsed = value;

            foreach (var item in _items)
            {
                item.Collapsed = value;
                _toolTip.SetToolTip(item, value ? item.Text : null);
            }

            ResumeLayout(true);
            SyncWidths();
        }
    }

    public void ToggleCollapsed() => Collapsed = !Collapsed;

    /// <summary>Thêm tiêu đề nhóm.</summary>
    public void AddGroup(string caption)
    {
        var group = new NavGroupLabel(caption) { Collapsed = _collapsed };
        _groups.Add(group);
        _list.Controls.Add(group);
        SyncWidths();
    }

    /// <summary>Thêm một mục điều hướng. <paramref name="key"/> dùng để đánh dấu mục đang mở.</summary>
    public NavItem AddItem(string text, string glyph, Action onClick, object? key = null)
    {
        var item = new NavItem(text, glyph) { Key = key, Collapsed = _collapsed };
        item.Click += (_, _) => onClick();
        item.KeyDown += OnItemKeyDown;

        if (_collapsed)
            _toolTip.SetToolTip(item, text);

        _items.Add(item);
        _list.Controls.Add(item);
        SyncWidths();
        return item;
    }

    /// <summary>Đánh dấu mục ứng với <paramref name="key"/> là đang mở (null = không mục nào).</summary>
    public void SetActive(object? key)
    {
        foreach (var item in _items)
            item.Active = key is not null && Equals(item.Key, key);
    }

    private void OnItemKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not NavItem current) return;

        var index = _items.IndexOf(current);
        var next = e.KeyCode switch
        {
            Keys.Down => Math.Min(_items.Count - 1, index + 1),
            Keys.Up => Math.Max(0, index - 1),
            Keys.Home => 0,
            Keys.End => _items.Count - 1,
            _ => -1
        };

        if (next < 0 || next == index) return;

        _items[next].Focus();
        e.Handled = true;
    }

    private void SyncWidths()
    {
        var width = _list.ClientSize.Width;
        if (width <= 0) return;

        _list.SuspendLayout();
        foreach (Control child in _list.Controls)
            child.Width = width;
        _list.ResumeLayout();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _toolTip.Dispose();

        base.Dispose(disposing);
    }
}

/// <summary>Tiêu đề nhóm trong sidebar — chữ hoa nhỏ, màu nhạt. Thu gọn thành đường kẻ.</summary>
public sealed class NavGroupLabel : Control
{
    private bool _collapsed;

    public NavGroupLabel(string caption)
    {
        Text = caption;
        Height = 40;
        Margin = new Padding(0, 6, 0, 0);
        BackColor = AgrisTheme.NavBack;
        Font = AgrisTheme.Ui(8.5F, FontStyle.Bold);
        AccessibleRole = AccessibleRole.StaticText;
        AccessibleName = caption;

        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public bool Collapsed
    {
        get => _collapsed;
        set { _collapsed = value; Height = value ? 18 : 40; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);

        if (_collapsed)
        {
            using var pen = new Pen(AgrisTheme.NavDivider);
            g.DrawLine(pen, 16, Height / 2, Width - 16, Height / 2);
            return;
        }

        TextRenderer.DrawText(g, Text.ToUpper(System.Globalization.CultureInfo.CurrentCulture), Font,
            new Rectangle(22, 12, Width - 30, Height - 12), AgrisTheme.NavTextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Một mục điều hướng: icon + nhãn, trạng thái hover / đang mở / focus.</summary>
public sealed class NavItem : Control
{
    private bool _hovered;
    private bool _active;
    private bool _collapsed;

    public NavItem(string text, string glyph)
    {
        Text = text;
        Glyph = glyph;
        Height = 46;
        Margin = new Padding(0, 1, 0, 1);
        BackColor = AgrisTheme.NavBack;
        Cursor = Cursors.Hand;
        TabStop = true;
        Font = AgrisTheme.Ui(10.5F);
        AccessibleRole = AccessibleRole.MenuItem;
        AccessibleName = text;

        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
            | ControlStyles.Selectable, true);
    }

    public string Glyph { get; }
    public object? Key { get; init; }

    public bool Active
    {
        get => _active;
        set
        {
            if (_active == value) return;
            _active = value;
            AccessibleDescription = value ? "active" : null;
            Invalidate();
        }
    }

    public bool Collapsed
    {
        get => _collapsed;
        set { _collapsed = value; Invalidate(); }
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override bool IsInputKey(Keys keyData)
        => keyData is Keys.Up or Keys.Down or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(AgrisTheme.NavBack);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Nền "viên thuốc" thụt vào hai bên — nhìn hiện đại hơn dải full-width.
        var pill = new Rectangle(8, 2, Width - 17, Height - 5);
        if (_active || _hovered)
        {
            using var path = Shapes.RoundedRect(pill, AgrisTheme.RadiusMd);
            using var brush = new SolidBrush(_active ? AgrisTheme.NavBackActive : AgrisTheme.NavBackHover);
            g.FillPath(brush, path);
        }

        if (_active)
        {
            using var accent = new SolidBrush(AgrisTheme.NavAccent);
            using var bar = Shapes.RoundedRect(new Rectangle(8, pill.Top + 10, 4, pill.Height - 20), 2);
            g.FillPath(accent, bar);
        }

        if (Focused && ShowFocusCues)
        {
            using var focusPath = Shapes.RoundedRect(pill, AgrisTheme.RadiusMd);
            using var focusPen = new Pen(AgrisTheme.NavAccent, 1.5f);
            g.DrawPath(focusPen, focusPath);
        }

        var color = _active ? AgrisTheme.NavTextActive : _hovered ? Color.White : AgrisTheme.NavText;
        var iconFont = AgrisTheme.Icon(13F);

        if (_collapsed)
        {
            // Không có font icon thì hiện chữ cái đầu để mục vẫn nhận biết được.
            var glyph = iconFont is not null ? Glyph : Text[..1];
            TextRenderer.DrawText(g, glyph, iconFont ?? AgrisTheme.Ui(11F, FontStyle.Bold),
                pill, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            return;
        }

        var textLeft = 22;
        if (iconFont is not null)
        {
            TextRenderer.DrawText(g, Glyph, iconFont, new Rectangle(20, 0, 26, Height), color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            textLeft = 58;
        }

        var font = _active ? AgrisTheme.Ui(10.5F, FontStyle.Bold) : Font;
        TextRenderer.DrawText(g, Text, font, new Rectangle(textLeft, 0, Width - textLeft - 14, Height), color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
