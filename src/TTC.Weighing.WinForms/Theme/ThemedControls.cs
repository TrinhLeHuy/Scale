using System.Drawing.Drawing2D;

namespace TTC.Weighing.WinForms.Theme;

/// <summary>Dải màu chuyển xanh lá sang xanh biển, dùng cho thanh thương hiệu và chân trang.</summary>
public sealed class GradientBar : Panel
{
    public GradientBar()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.ResizeRedraw, true);
    }

    /// <summary>Đảo chiều dải màu: mặc định xanh biển bên trái, xanh lá bên phải.</summary>
    public bool GreenFirst { get; init; }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width <= 0 || Height <= 0) return;

        // Nền sáng: xám xanh → xám xanh lá (sáng hơn logo để logo nổi)
        var from = GreenFirst
            ? Color.FromArgb(45, 115, 90)
            : Color.FromArgb(50, 105, 125);
        var to = GreenFirst
            ? Color.FromArgb(50, 105, 125)
            : Color.FromArgb(45, 115, 90);

        using var brush = new LinearGradientBrush(ClientRectangle, from, to, 0f);
        e.Graphics.FillRectangle(brush, ClientRectangle);

        base.OnPaint(e);
    }
}

/// <summary>Thẻ nội dung nền trắng, viền mảnh, dùng làm khối bố cục chuẩn.</summary>
public class Card : Panel
{
    public Card()
    {
        BackColor = AgrisTheme.Surface;
        Padding = new Padding(12);

        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.ResizeRedraw, true);
    }

    /// <summary>Vạch nhấn màu bên trái, dùng để phân loại thẻ theo mức độ.</summary>
    public Color? AccentColor { get; set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        using var pen = new Pen(AgrisTheme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

        if (AccentColor is null) return;

        using var accent = new SolidBrush(AccentColor.Value);
        e.Graphics.FillRectangle(accent, 1, 1, 4, Height - 2);
    }
}

/// <summary>
/// Ô số liệu trên màn hình tổng quan: nhãn, giá trị lớn và ghi chú.
/// </summary>
public sealed class MetricTile : Card
{
    private readonly Label _caption = new();
    private readonly Label _value = new();
    private readonly Label _note = new();

    public MetricTile(string caption, Color accent, string? note = null)
    {
        AccentColor = accent;
        Size = new Size(190, 108);
        Margin = new Padding(0, 0, 10, 10);
        Padding = new Padding(14, 10, 10, 8);

        _caption.Text = caption;
        _caption.Dock = DockStyle.Top;
        _caption.Height = 24;
        _caption.Font = AgrisTheme.Caption();
        _caption.ForeColor = AgrisTheme.TextMuted;

        _value.Text = "—";
        _value.Dock = DockStyle.Fill;
        _value.Font = AgrisTheme.Display(24F);
        _value.ForeColor = accent;
        _value.TextAlign = ContentAlignment.MiddleLeft;

        _note.Text = note ?? string.Empty;
        _note.Dock = DockStyle.Bottom;
        _note.Height = 18;
        _note.Font = AgrisTheme.Caption();
        _note.ForeColor = AgrisTheme.TextMuted;

        Controls.Add(_value);
        Controls.Add(_note);
        Controls.Add(_caption);

        AccessibleName = caption;
    }

    public void SetValue(int value) => SetValue(value.ToString());

    public void SetValue(string value)
    {
        if (InvokeRequired)
        {
            Invoke(() => SetValue(value));
            return;
        }

        _value.Text = value;
        AccessibleDescription = $"{_caption.Text}: {value}";
    }

    public void SetNote(string note)
    {
        if (InvokeRequired)
        {
            Invoke(() => SetNote(note));
            return;
        }

        _note.Text = note;
    }

    /// <summary>Cập nhật nhãn tiêu đề. Dùng khi đổi ngôn ngữ runtime.</summary>
    public void SetCaption(string caption)
    {
        if (InvokeRequired)
        {
            Invoke(() => SetCaption(caption));
            return;
        }

        _caption.Text = caption;
        AccessibleName = caption;
    }
}

/// <summary>
/// Một chặng trong luồng nghiệp vụ, vẽ dạng con trỏ nối tiếp nhau.
/// Nguồn: vòng đời chuyến trong 02_Quy_tac mục 2.1.
/// </summary>
public sealed class PipelineStageBox : Control
{
    private const int ArrowWidth = 14;

    public PipelineStageBox(string title, string subtitle)
    {
        Title = title;
        Subtitle = subtitle;
        Size = new Size(150, 74);
        Margin = new Padding(0, 0, 2, 0);

        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);

        AccessibleName = title;
    }

    public string Title { get; set; }
    public string Subtitle { get; set; }

    private int _count;
    public int Count
    {
        get => _count;
        set
        {
            _count = value;
            AccessibleDescription = $"{Title}: {value} chuyến";
            Invalidate();
        }
    }

    /// <summary>Chặng cuối không vẽ mũi nhọn để khép luồng.</summary>
    public bool IsLast { get; init; }

    /// <summary>Chặng cần chú ý được tô màu cảnh báo thay vì màu thương hiệu.</summary>
    public Color? AttentionColor { get; init; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var hasWork = Count > 0;

        var fill = AttentionColor is not null && hasWork
            ? AttentionColor.Value
            : hasWork ? AgrisTheme.BrandGreen : AgrisTheme.SurfaceMuted;

        var textColor = hasWork ? AgrisTheme.TextOnBrand : AgrisTheme.TextMuted;

        using var path = BuildShape();
        using var brush = new SolidBrush(fill);
        g.FillPath(brush, path);

        using var pen = new Pen(hasWork ? fill : AgrisTheme.Border);
        g.DrawPath(pen, path);

        var textArea = new Rectangle(12, 8, Width - ArrowWidth - 18, 20);
        TextRenderer.DrawText(g, Title, AgrisTheme.Caption(), textArea, textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        var countArea = new Rectangle(12, 24, Width - ArrowWidth - 18, 30);
        TextRenderer.DrawText(g, Count.ToString(),
            AgrisTheme.Display(18F), countArea, textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        var subtitleArea = new Rectangle(12, 52, Width - ArrowWidth - 18, 18);
        TextRenderer.DrawText(g, Subtitle, AgrisTheme.Caption(), subtitleArea,
            hasWork ? Color.FromArgb(220, 235, 226) : AgrisTheme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private GraphicsPath BuildShape()
    {
        var path = new GraphicsPath();
        var right = Width - 1;
        var bottom = Height - 1;

        if (IsLast)
        {
            path.AddRectangle(new Rectangle(0, 0, right, bottom));
            return path;
        }

        path.AddPolygon(new[]
        {
            new Point(0, 0),
            new Point(right - ArrowWidth, 0),
            new Point(right, bottom / 2),
            new Point(right - ArrowWidth, bottom),
            new Point(0, bottom)
        });

        return path;
    }
}

/// <summary>
/// Thanh thương hiệu dải gradient tối mượt — nền dark teal sang dark green,
/// gọn hơn GradientBar cũ và tạo chiều sâu cho logo/text trắng.
/// </summary>
public sealed class BrandBar : Panel
{
    public BrandBar()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width <= 0 || Height <= 0) return;

        var g = e.Graphics;

        // Nền trắng sạch
        g.Clear(Color.White);

        // Viền dưới mỏng xám — phân cách với content area
        using var borderPen = new Pen(Color.FromArgb(220, 228, 232));
        g.DrawLine(borderPen, 0, Height - 1, Width, Height - 1);

        base.OnPaint(e);
    }
}

/// <summary>
/// Nút trên thanh brand bar — viền trắng mờ, hover đổi màu đỏ nhẹ (đăng xuất)
/// hoặc xanh nhẹ (hành động khác). Bo tròn nhẹ qua owner-draw.
/// </summary>
public sealed class BrandBarButton : Control
{
    private bool _hovered;
    private bool _pressed;

    public BrandBarButton(string text)
    {
        Text = text;
        Size = new Size(118, 34);
        Cursor = Cursors.Hand;
        Font = new Font(AgrisTheme.FontFamily, 9.5F, FontStyle.Bold);

        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.ResizeRedraw | ControlStyles.UserPaint
            | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);

        Color bgColor;
        Color borderColor;
        Color textColor;

        if (_pressed)
        {
            bgColor = Color.FromArgb(190, 58, 58);
            borderColor = Color.FromArgb(160, 40, 40);
            textColor = Color.White;
        }
        else if (_hovered)
        {
            bgColor = Color.FromArgb(220, 60, 60);
            borderColor = Color.FromArgb(190, 50, 50);
            textColor = Color.White;
        }
        else
        {
            bgColor = Color.FromArgb(253, 236, 236);
            borderColor = Color.FromArgb(210, 130, 130);
            textColor = Color.FromArgb(160, 50, 50);
        }

        using var path = RoundedRect(rect, 6);
        using var fillBrush = new SolidBrush(bgColor);
        g.FillPath(fillBrush, path);

        using var pen = new Pen(borderColor, 1.2f);
        g.DrawPath(pen, path);

        TextRenderer.DrawText(g, Text, Font, rect, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var d = radius * 2;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>
/// Đường kẻ dọc mỏng dùng làm viền phân tách giữa các phần tử trên thanh header.
/// </summary>
public sealed class VerticalSeparator : Control
{
    public VerticalSeparator()
    {
        Size = new Size(1, 32);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.ResizeRedraw | ControlStyles.UserPaint
            | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var pen = new Pen(Color.FromArgb(200, 210, 215));
        e.Graphics.DrawLine(pen, 0, 4, 0, Height - 4);
    }
}
