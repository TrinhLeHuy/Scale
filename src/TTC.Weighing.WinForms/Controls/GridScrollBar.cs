using System.Drawing.Drawing2D;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Controls;

/// <summary>
/// Thanh cuộn dọc cỡ lớn cho DataGridView — dễ bấm/kéo trên màn hình cảm ứng và với găng tay
/// ở trạm cân. Thay thanh cuộn hệ thống (không chỉnh được độ rộng) bằng thanh tự vẽ:
/// kéo con trượt, bấm vào rãnh để sang trang, nút ▲▼ hai đầu, lăn chuột trên lưới.
/// Tự ẩn khi mọi dòng đã nằm vừa trong lưới.
/// </summary>
public sealed class GridScrollBar : Control
{
    public static int BarWidth => Theme.UiScale.Px(22);
    private static int ArrowHeight => Theme.UiScale.Px(26);
    private static int MinThumbHeight => Theme.UiScale.Px(48);

    private readonly DataGridView _grid;
    private readonly System.Windows.Forms.Timer _repeat = new() { Interval = 60 };
    private int _repeatStep;

    private bool _dragging;
    private int _dragOffsetY;
    private bool _thumbHovered;
    private Rectangle _thumb;

    /// <summary>Gắn thanh cuộn lớn vào lưới. Thanh được đặt cùng cha, dock phải cạnh lưới.</summary>
    public static GridScrollBar Attach(DataGridView grid)
    {
        var bar = new GridScrollBar(grid);

        // Bọc lưới + thanh cuộn trong một panel để thanh luôn sát mép phải lưới.
        var parent = grid.Parent ?? throw new InvalidOperationException("Lưới phải có cha trước khi gắn thanh cuộn.");
        var index = parent.Controls.GetChildIndex(grid);
        var host = new Panel { Dock = grid.Dock, BackColor = AgrisTheme.Surface, Margin = grid.Margin };

        parent.Controls.Remove(grid);
        grid.Dock = DockStyle.Fill;
        host.Controls.Add(grid);
        host.Controls.Add(bar);
        parent.Controls.Add(host);
        parent.Controls.SetChildIndex(host, index);

        return bar;
    }

    private GridScrollBar(DataGridView grid)
    {
        _grid = grid;
        Dock = DockStyle.Right;
        Width = BarWidth;
        BackColor = AgrisTheme.Surface;
        Cursor = Cursors.Default;
        AccessibleRole = AccessibleRole.ScrollBar;
        AccessibleName = grid.AccessibleName;

        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        // Ẩn thanh dọc hệ thống, giữ thanh ngang.
        _grid.ScrollBars = ScrollBars.Horizontal;

        _grid.Scroll += (_, _) => Sync();
        _grid.RowsAdded += (_, _) => Sync();
        _grid.RowsRemoved += (_, _) => Sync();
        _grid.DataBindingComplete += (_, _) => Sync();
        _grid.Resize += (_, _) => Sync();
        _grid.CurrentCellChanged += (_, _) => Sync();
        _grid.MouseWheel += OnGridMouseWheel;

        _repeat.Tick += (_, _) => ScrollBy(_repeatStep);

        Sync();
    }

    private int RowCount => _grid.Rows.Cast<DataGridViewRow>().Count(r => r.Visible);
    private int VisibleRows => Math.Max(1, _grid.DisplayedRowCount(false));
    private int MaxFirst => Math.Max(0, RowCount - VisibleRows);

    private int FirstRow
    {
        get => Math.Max(0, _grid.FirstDisplayedScrollingRowIndex);
        set
        {
            if (_grid.Rows.Count == 0) return;
            var target = Math.Clamp(value, 0, MaxFirst);

            // Chỉ số hiển thị phải trỏ tới dòng đang Visible.
            var visibleRows = _grid.Rows.Cast<DataGridViewRow>().Where(r => r.Visible).ToList();
            if (target >= visibleRows.Count) return;

            try { _grid.FirstDisplayedScrollingRowIndex = visibleRows[target].Index; }
            catch (InvalidOperationException) { /* Lưới đang layout — bỏ qua lần này. */ }

            Sync();
        }
    }

    private int CurrentPosition
    {
        get
        {
            var first = _grid.FirstDisplayedScrollingRowIndex;
            if (first < 0) return 0;
            return _grid.Rows.Cast<DataGridViewRow>().Take(first).Count(r => r.Visible);
        }
    }

    /// <summary>Cập nhật vị trí con trượt theo trạng thái cuộn của lưới.</summary>
    public void Sync()
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(Sync); return; }

        var needed = RowCount > VisibleRows;
        if (Visible != needed)
            Visible = needed;

        Invalidate();
    }

    private Rectangle Track => new(0, ArrowHeight, Width, Math.Max(0, Height - ArrowHeight * 2));

    private Rectangle ComputeThumb()
    {
        var track = Track;
        var total = Math.Max(1, RowCount);
        var height = Math.Max(MinThumbHeight, (int)(track.Height * (VisibleRows / (double)total)));
        height = Math.Min(height, track.Height);

        var range = Math.Max(1, track.Height - height);
        var ratio = MaxFirst == 0 ? 0 : CurrentPosition / (double)MaxFirst;
        var top = track.Top + (int)(range * Math.Clamp(ratio, 0, 1));

        return new Rectangle(3, top, Width - 6, height);
    }

    private void ScrollBy(int rows) => FirstRow = CurrentPosition + rows;

    private void OnGridMouseWheel(object? sender, MouseEventArgs e)
    {
        // ScrollBars = Horizontal làm lưới không tự cuộn dọc bằng chuột — tự xử lý, 3 dòng/nấc.
        var notches = e.Delta / SystemInformation.MouseWheelScrollDelta;
        if (notches == 0) return;

        ScrollBy(-notches * Math.Max(1, SystemInformation.MouseWheelScrollLines));
        if (e is HandledMouseEventArgs handled)
            handled.Handled = true;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        OnGridMouseWheel(this, e);
        base.OnMouseWheel(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;

        if (e.Y < ArrowHeight) StartRepeat(-1);
        else if (e.Y > Height - ArrowHeight) StartRepeat(1);
        else if (_thumb.Contains(e.Location))
        {
            _dragging = true;
            _dragOffsetY = e.Y - _thumb.Top;
            Capture = true;
        }
        else
        {
            // Bấm vào rãnh: sang trang theo hướng bấm, lặp khi giữ chuột.
            StartRepeat(e.Y < _thumb.Top ? -VisibleRows : VisibleRows);
        }

        Invalidate();
    }

    private void StartRepeat(int step)
    {
        _repeatStep = step;
        ScrollBy(step);
        _repeat.Interval = 350; // trễ trước khi lặp
        _repeat.Start();
        _repeat.Tick -= SpeedUp;
        _repeat.Tick += SpeedUp;
    }

    private void SpeedUp(object? sender, EventArgs e)
    {
        _repeat.Interval = 60;
        _repeat.Tick -= SpeedUp;

        // Bấm rãnh: dừng khi con trượt đã tới chỗ chuột.
        if (Math.Abs(_repeatStep) > 1)
        {
            var mouse = PointToClient(MousePosition);
            if (_thumb.Contains(mouse)) _repeat.Stop();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var hovered = _thumb.Contains(e.Location);
        if (hovered != _thumbHovered) { _thumbHovered = hovered; Invalidate(); }

        if (!_dragging) return;

        var track = Track;
        var range = Math.Max(1, track.Height - _thumb.Height);
        var ratio = (e.Y - _dragOffsetY - track.Top) / (double)range;
        FirstRow = (int)Math.Round(Math.Clamp(ratio, 0, 1) * MaxFirst);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
        Capture = false;
        _repeat.Stop();
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _thumbHovered = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(AgrisTheme.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Rãnh.
        var track = Track;
        using (var trackBrush = new SolidBrush(AgrisTheme.SurfaceMuted))
        using (var trackPath = Shapes.RoundedRect(new Rectangle(3, track.Top, Width - 7, Math.Max(1, track.Height - 1)), (Width - 6) / 2))
            g.FillPath(trackBrush, trackPath);

        using (var pen = new Pen(AgrisTheme.Border))
            g.DrawLine(pen, 0, 0, 0, Height);

        // Con trượt.
        _thumb = ComputeThumb();
        var thumbColor = _dragging ? AgrisTheme.Primary
            : _thumbHovered ? AgrisTheme.TextMuted
            : AgrisTheme.BorderStrong;
        using (var thumbBrush = new SolidBrush(thumbColor))
        using (var thumbPath = Shapes.RoundedRect(new Rectangle(_thumb.X, _thumb.Y, _thumb.Width - 1, _thumb.Height - 1), (_thumb.Width - 1) / 2))
            g.FillPath(thumbBrush, thumbPath);

        // Nút mũi tên.
        DrawArrow(g, new Rectangle(0, 0, Width, ArrowHeight), up: true, enabled: CurrentPosition > 0);
        DrawArrow(g, new Rectangle(0, Height - ArrowHeight, Width, ArrowHeight), up: false, enabled: CurrentPosition < MaxFirst);
    }

    private static void DrawArrow(Graphics g, Rectangle area, bool up, bool enabled)
    {
        var cx = area.Left + area.Width / 2f;
        var cy = area.Top + area.Height / 2f;
        const float w = 6f, h = 4f;

        PointF[] points = up
            ? [new(cx - w, cy + h / 2), new(cx + w, cy + h / 2), new(cx, cy - h)]
            : [new(cx - w, cy - h / 2), new(cx + w, cy - h / 2), new(cx, cy + h)];

        using var brush = new SolidBrush(enabled ? AgrisTheme.TextSecondary : AgrisTheme.BorderStrong);
        g.FillPolygon(brush, points);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _repeat.Dispose();
            _grid.MouseWheel -= OnGridMouseWheel;
        }

        base.Dispose(disposing);
    }
}
