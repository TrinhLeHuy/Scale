using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace TTC.Weighing.WinForms.Theme;

/// <summary>Tiện ích hình học dùng chung cho các control tự vẽ.</summary>
public static class Shapes
{
    /// <summary>Hình chữ nhật bo góc. <paramref name="radius"/> là bán kính, không phải đường kính.</summary>
    public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();

        if (radius <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Màu nền thực của cha gần nhất (bỏ qua cha trong suốt) — để vẽ góc bo không lộ viền.</summary>
    public static Color EffectiveBackColor(Control? control)
    {
        while (control is not null)
        {
            if (control.BackColor.A == 255)
                return control.BackColor;
            control = control.Parent;
        }

        return AgrisTheme.Canvas;
    }

    /// <summary>Độ sáng cảm nhận (0..1), dùng để chọn màu hover sáng/tối.</summary>
    public static float Luminance(Color c) => (0.299f * c.R + 0.587f * c.G + 0.114f * c.B) / 255f;
}

/// <summary>Dải màu thương hiệu xanh biển → xanh lá. Giữ để tương thích các màn hình cũ.</summary>
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

        var from = GreenFirst ? AgrisTheme.BrandGreenDark : AgrisTheme.BrandTealDark;
        var to = GreenFirst ? AgrisTheme.BrandTealDark : AgrisTheme.BrandGreenDark;

        using var brush = new LinearGradientBrush(ClientRectangle, from, to, 0f);
        e.Graphics.FillRectangle(brush, ClientRectangle);

        base.OnPaint(e);
    }
}

/// <summary>
/// Thẻ nội dung nền trắng, viền mảnh. Khối bố cục chuẩn của mọi màn hình.
/// Góc vuông có chủ đích: control con docked phủ kín mép thẻ nên bo góc sẽ bị cắt lởm chởm.
/// </summary>
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

        using (var pen = new Pen(AgrisTheme.Border))
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

        // Mép dưới đậm hơn: cảm giác thẻ nổi trên canvas.
        using (var shadow = new Pen(AgrisTheme.CardShadow))
            e.Graphics.DrawLine(shadow, 0, Height - 1, Width - 1, Height - 1);

        if (AccentColor is null) return;

        using var accent = new SolidBrush(AccentColor.Value);
        e.Graphics.FillRectangle(accent, 0, 0, 3, Height);
    }
}

/// <summary>
/// Ô KPI trên màn hình tổng quan: nhãn, số lớn và ghi chú — tự vẽ, tự đo theo cỡ chữ.
/// Chiều cao lấy từ <see cref="PreferredTileHeight"/>, số lớn tự thu nhỏ khi không đủ rộng
/// (ví dụ "-14.110.000") nên không bao giờ bị cắt nửa chữ.
/// </summary>
public sealed class MetricTile : Control
{
    private static readonly float[] ValueSizes = [20F, 18F, 16F, 14F, 12F, 10F];

    private readonly Color _accent;
    private string _caption;
    private string _value = "—";
    private string _note;
    private readonly ToolTip _tip = new() { InitialDelay = 300 };

    public MetricTile(string caption, Color accent, string? note = null)
    {
        _caption = caption;
        _accent = accent;
        _note = note ?? string.Empty;

        Margin = new Padding(0, 0, 12, 0);
        Size = new Size(190, PreferredTileHeight);
        MinimumSize = new Size(140, 0);
        BackColor = AgrisTheme.Surface;
        AccessibleName = caption;
        AccessibleRole = AccessibleRole.StaticText;

        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        UpdateTooltip();
    }

    private static Font CaptionFont => AgrisTheme.Body(8.5F, FontStyle.Bold);
    private static Font NoteFont => AgrisTheme.Caption();
    private static int PadX => Theme.UiScale.Px(16);
    private static int PadTop => Theme.UiScale.Px(12);
    private static int PadBottom => Theme.UiScale.Px(10);

    private static int LineHeight(Font font) => TextRenderer.MeasureText("Ág", font, Size.Empty, TextFormatFlags.NoPadding).Height;

    /// <summary>Chiều cao đủ cho nhãn + số lớn nhất + ghi chú ở cỡ chữ hiện hành.</summary>
    public static int PreferredTileHeight
        => PadTop + LineHeight(CaptionFont) + 4
           + LineHeight(AgrisTheme.Display(ValueSizes[0])) + 4
           + LineHeight(NoteFont) + PadBottom;

    public void SetValue(int value) => SetValue(value.ToString("N0"));

    public void SetValue(string value)
    {
        if (InvokeRequired) { Invoke(() => SetValue(value)); return; }
        _value = value;
        AccessibleDescription = $"{_caption}: {value}";
        UpdateTooltip();
        Invalidate();
    }

    public void SetNote(string note)
    {
        if (InvokeRequired) { Invoke(() => SetNote(note)); return; }
        _note = note;
        UpdateTooltip();
        Invalidate();
    }

    /// <summary>Cập nhật nhãn tiêu đề. Dùng khi đổi ngôn ngữ runtime.</summary>
    public void SetCaption(string caption)
    {
        if (InvokeRequired) { Invoke(() => SetCaption(caption)); return; }
        _caption = caption;
        AccessibleName = caption;
        UpdateTooltip();
        Invalidate();
    }

    /// <summary>Di chuột vào thẻ hiện đầy đủ nội dung (khi nhãn/ghi chú bị rút gọn "…").</summary>
    private void UpdateTooltip()
        => _tip.SetToolTip(this, string.IsNullOrEmpty(_note) ? $"{_caption}: {_value}" : $"{_caption}: {_value}\n{_note}");

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);

        using (var pen = new Pen(AgrisTheme.Border))
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        using (var shadow = new Pen(AgrisTheme.CardShadow))
            g.DrawLine(shadow, 0, Height - 1, Width - 1, Height - 1);
        using (var accent = new SolidBrush(_accent))
            g.FillRectangle(accent, 0, 0, Width, 4);

        var width = Width - PadX * 2;
        var y = PadTop;

        var captionH = LineHeight(CaptionFont);
        TextRenderer.DrawText(g, _caption, CaptionFont, new Rectangle(PadX, y, width, captionH), AgrisTheme.TextSecondary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        y += captionH + 4;

        // Số lớn: chọn cỡ lớn nhất vừa bề ngang, căn đáy theo dòng cỡ lớn nhất để các thẻ thẳng hàng.
        var valueLine = LineHeight(AgrisTheme.Display(ValueSizes[0]));
        var font = ValueSizes.Select(s => AgrisTheme.Display(s))
            .FirstOrDefault(f => TextRenderer.MeasureText(_value, f, Size.Empty, TextFormatFlags.NoPadding).Width <= width)
            ?? AgrisTheme.Display(ValueSizes[^1]);
        TextRenderer.DrawText(g, _value, font, new Rectangle(PadX, y, width, valueLine), _accent,
            TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        y += valueLine + 4;

        TextRenderer.DrawText(g, _note, NoteFont, new Rectangle(PadX, y, width, LineHeight(NoteFont)), AgrisTheme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>
/// Một chặng trong luồng nghiệp vụ, vẽ dạng chevron nối tiếp nhau.
/// Nguồn: vòng đời chuyến trong 02_Quy_tac mục 2.1.
/// Chiều cao tự tính theo cỡ chữ (<see cref="PreferredBoxHeight"/>) — đặt trong bảng chia đều cột
/// để cả luồng nằm vừa một hàng, không cần thanh cuộn ngang.
/// </summary>
public sealed class PipelineStageBox : Control
{
    private static int ArrowWidth => Theme.UiScale.Px(16);
    private readonly ToolTip _tip = new() { InitialDelay = 300 };

    public PipelineStageBox(string title, string subtitle)
    {
        _title = title;
        _subtitle = subtitle;
        Size = new Size(170, PreferredBoxHeight);
        Margin = new Padding(0, 0, 2, 0);

        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);

        AccessibleName = title;
        UpdateTooltip();
    }

    private static Font TitleFont => AgrisTheme.Body(8.5F, FontStyle.Bold);
    private static Font CountFont => AgrisTheme.Display(16F);
    private static Font SubtitleFont => AgrisTheme.Caption();

    private static int LineHeight(Font font) => TextRenderer.MeasureText("Ág", font, Size.Empty, TextFormatFlags.NoPadding).Height;

    public static int PreferredBoxHeight
        => 8 + LineHeight(TitleFont) + 2 + LineHeight(CountFont) + 2 + LineHeight(SubtitleFont) + 8;

    private string _title;
    public string Title
    {
        get => _title;
        set { _title = value; AccessibleName = value; UpdateTooltip(); Invalidate(); }
    }

    private string _subtitle;
    public string Subtitle
    {
        get => _subtitle;
        set { _subtitle = value; UpdateTooltip(); Invalidate(); }
    }

    private int _count;
    public int Count
    {
        get => _count;
        set
        {
            _count = value;
            AccessibleDescription = $"{Title}: {value}";
            Invalidate();
        }
    }

    /// <summary>Chặng cuối không vẽ mũi nhọn để khép luồng.</summary>
    public bool IsLast { get; init; }

    /// <summary>Chặng cần chú ý được tô màu cảnh báo thay vì màu thương hiệu.</summary>
    public Color? AttentionColor { get; init; }

    private void UpdateTooltip() => _tip.SetToolTip(this, $"{_title}\n{_subtitle}");

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Shapes.EffectiveBackColor(Parent));
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var hasWork = Count > 0;

        var fill = AttentionColor is not null && hasWork
            ? AttentionColor.Value
            : hasWork ? AgrisTheme.Primary : AgrisTheme.SurfaceMuted;

        using (var path = BuildShape())
        {
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, path);
            using var pen = new Pen(hasWork ? fill : AgrisTheme.Border);
            g.DrawPath(pen, path);
        }

        var left = 18;
        var width = Width - ArrowWidth - left - 4;
        var y = 8;

        var titleColor = hasWork ? AgrisTheme.TextOnBrand : AgrisTheme.TextSecondary;
        var subColor = hasWork ? Color.FromArgb(225, 240, 232) : AgrisTheme.TextMuted;
        var countColor = hasWork ? AgrisTheme.TextOnBrand : AgrisTheme.TextPrimary;

        var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                    | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;

        var th = LineHeight(TitleFont);
        TextRenderer.DrawText(g, Title, TitleFont, new Rectangle(left, y, width, th), titleColor, flags);
        y += th + 2;

        var ch = LineHeight(CountFont);
        TextRenderer.DrawText(g, Count.ToString("N0"), CountFont, new Rectangle(left, y, width, ch), countColor, flags);
        y += ch + 2;

        TextRenderer.DrawText(g, Subtitle, SubtitleFont, new Rectangle(left, y, width, LineHeight(SubtitleFont)), subColor, flags);
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

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>
/// Nút bo góc tự vẽ. Là <see cref="Button"/> nên dùng thay thế trực tiếp được.
/// Tôn trọng <c>BackColor</c>, <c>ForeColor</c>, <c>FlatAppearance.BorderColor/BorderSize</c>
/// và <c>FlatAppearance.MouseOverBackColor</c> — form đổi màu nút theo trạng thái vẫn hoạt động.
/// </summary>
public class ModernButton : Button
{
    private bool _hovered;
    private bool _pressed;

    public ModernButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
    }

    /// <summary>Bán kính bo góc.</summary>
    public int CornerRadius { get; set; } = AgrisTheme.RadiusMd;

    private static int IconSlot => Theme.UiScale.Px(26);

    /// <summary>Ký tự icon (Segoe Fluent/MDL2) vẽ trước chữ. Bỏ qua nếu máy không có font icon.</summary>
    public string? IconGlyph { get; set; }

    private Font? IconFont => IconGlyph is null ? null : AgrisTheme.Icon(Font.SizeInPoints * 1.1f);

    public override Size GetPreferredSize(Size proposedSize)
    {
        var size = base.GetPreferredSize(proposedSize);
        if (IconFont is not null)
            size.Width += IconSlot;
        if (MaximumSize.Height > 0)
            size.Height = Math.Min(size.Height, MaximumSize.Height);
        return size;
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        base.OnEnabledChanged(e);
    }

    // ── Tự khớp kích thước ──
    // Form cũ đặt Size cố định cho nút rồi bỏ vào thanh/panel cao cố định. Nút bo góc bị cắt
    // mép thì lộ rõ, chữ đậm (tab đang chọn) thì tràn xuống dòng. Hai cơ chế dưới đây sửa chung.

    /// <summary>Chiều cao mà form yêu cầu — dùng để khôi phục khi vùng chứa cao lên lại.</summary>
    private int _desiredHeight;
    private bool _fitting;
    private Control? _observedParent;

    protected override void OnSizeChanged(EventArgs e)
    {
        if (!_fitting)
            _desiredHeight = Height;
        base.OnSizeChanged(e);
    }

    protected override void OnParentChanged(EventArgs e)
    {
        if (_observedParent is not null)
            _observedParent.Resize -= OnParentResized;

        _observedParent = Parent;

        if (_observedParent is not null)
            _observedParent.Resize += OnParentResized;

        base.OnParentChanged(e);
        FitToText();
        FitToParentHeight();
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        FitToText();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        FitToText();
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        FitToParentHeight();
    }

    private void OnParentResized(object? sender, EventArgs e) => FitToParentHeight();

    /// <summary>
    /// Trong FlowLayoutPanel (thanh nút/thanh lọc): nới rộng nút để chữ nằm trên một dòng,
    /// tính theo bản đậm của font để bề rộng không nhảy khi form đổi font active/inactive.
    /// Bố cục toạ độ tuyệt đối thì không tự nới — tránh đè lên control bên cạnh.
    /// </summary>
    private void FitToText()
    {
        if (AutoSize || Dock != DockStyle.None || Parent is not FlowLayoutPanel || string.IsNullOrEmpty(Text))
            return;

        using var bold = new Font(Font, FontStyle.Bold);
        var textWidth = Math.Max(
            TextRenderer.MeasureText(Text, Font).Width,
            TextRenderer.MeasureText(Text, bold).Width);

        var iconWidth = IconFont is not null ? IconSlot : 0;
        var required = textWidth + iconWidth + Padding.Horizontal + 28;

        if (MaximumSize.Width > 0)
            required = Math.Min(required, MaximumSize.Width);

        if (Width < required)
            Width = required;
    }

    /// <summary>
    /// Thu chiều cao nút cho vừa vùng chứa cao cố định để viền bo góc không bị cắt.
    /// Khi vùng chứa cao lên thì trả về chiều cao gốc.
    /// </summary>
    private void FitToParentHeight()
    {
        var parent = Parent;
        if (parent is null || _desiredHeight <= 0 || Dock != DockStyle.None
            || (Anchor & AnchorStyles.Bottom) != 0
            || parent is TableLayoutPanel
            || parent.AutoSize
            || (parent is ScrollableControl { AutoScroll: true })
            || (parent is FlowLayoutPanel { WrapContents: true }))
            return;

        var available = parent is FlowLayoutPanel flow
            ? flow.ClientSize.Height - flow.Padding.Bottom - Top - Margin.Bottom
            : parent.ClientSize.Height - Top - 1;

        // Vùng chứa chưa được đặt kích thước thật (còn mặc định) — chưa co.
        if (available < 24) return;

        var target = Math.Min(_desiredHeight, available);
        if (target == Height) return;

        _fitting = true;
        try { Height = target; }
        finally { _fitting = false; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _observedParent is not null)
        {
            _observedParent.Resize -= OnParentResized;
            _observedParent = null;
        }

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Shapes.EffectiveBackColor(Parent));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var isLight = Shapes.Luminance(BackColor) > 0.85f;

        Color fill;
        if (!Enabled)
            fill = AgrisTheme.ReadOnlyBg;
        else if (_pressed)
            fill = isLight ? AgrisTheme.Border : AgrisTheme.Blend(BackColor, Color.Black, 0.25f);
        else if (_hovered)
            // Chỉ dùng MouseOverBackColor khi cùng "tông" với nền hiện tại — form có thể đổi
            // BackColor nút phụ thành màu đậm (tab lọc đang chọn) mà không đổi màu hover.
            fill = FlatAppearance.MouseOverBackColor is { IsEmpty: false } hover && hover != BackColor
                && (Shapes.Luminance(hover) > 0.85f) == isLight
                ? hover
                : isLight ? AgrisTheme.SurfaceHover : AgrisTheme.Blend(BackColor, Color.Black, 0.14f);
        else
            fill = BackColor;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Shapes.RoundedRect(rect, CornerRadius))
        {
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, path);

            // Nút nền sáng luôn có viền để không "chìm" vào thẻ trắng.
            if (isLight && Enabled)
            {
                var borderColor = FlatAppearance.BorderColor is { IsEmpty: false } bc
                    && bc != AgrisTheme.Border ? bc : AgrisTheme.BorderStrong;
                using var pen = new Pen(borderColor);
                g.DrawPath(pen, path);
            }
            else if (!Enabled)
            {
                using var pen = new Pen(AgrisTheme.Border);
                g.DrawPath(pen, path);
            }
        }

        // Vòng focus bàn phím (accessibility).
        if (Focused && ShowFocusCues)
        {
            var focusRect = new Rectangle(2, 2, Width - 5, Height - 5);
            using var focusPath = Shapes.RoundedRect(focusRect, Math.Max(0, CornerRadius - 2));
            using var focusPen = new Pen(isLight ? AgrisTheme.FocusRing : Color.FromArgb(220, 255, 255, 255), 1.6f);
            g.DrawPath(focusPen, focusPath);
        }

        var textColor = Enabled ? ForeColor : AgrisTheme.TextMuted;
        var content = new Rectangle(Padding.Left + 4, 0, Width - Padding.Horizontal - 8, Height);

        if (IconFont is { } iconFont)
        {
            var textWidth = string.IsNullOrEmpty(Text) ? 0 : TextRenderer.MeasureText(g, Text, Font).Width;
            var total = IconSlot + textWidth;
            var x = content.Left + Math.Max(0, (content.Width - total) / 2);

            TextRenderer.DrawText(g, IconGlyph, iconFont, new Rectangle(x, 0, IconSlot - 6, Height), textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            TextRenderer.DrawText(g, Text, Font, new Rectangle(x + IconSlot, 0, Math.Max(0, content.Right - x - IconSlot), Height),
                textColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            return;
        }

        if (Image is not null)
        {
            var imgSize = Image.Size;
            var textSize = TextRenderer.MeasureText(g, Text, Font);
            var total = imgSize.Width + (string.IsNullOrEmpty(Text) ? 0 : 6 + textSize.Width);
            var x = content.Left + Math.Max(0, (content.Width - total) / 2);
            g.DrawImage(Image, x, (Height - imgSize.Height) / 2, imgSize.Width, imgSize.Height);
            content = new Rectangle(x + imgSize.Width + 6, 0, Math.Max(0, content.Right - x - imgSize.Width - 6), Height);

            TextRenderer.DrawText(g, Text, Font, content, textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            return;
        }

        // Chỉ cho xuống dòng khi nút đủ cao cho 2 dòng; nút một dòng thì cắt "…" thay vì
        // để dòng thứ hai bị xén nửa chừng.
        var lineHeight = TextRenderer.MeasureText(g, "Ag", Font).Height;
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        if (Height >= lineHeight * 2 + 8)
            flags |= TextFormatFlags.WordBreak;
        else
            flags |= TextFormatFlags.SingleLine;

        TextRenderer.DrawText(g, Text, Font, content, textColor, flags);
    }
}

/// <summary>Tiêu đề khu vực trong thẻ: chữ đậm, vạch nhấn trái, đường phân cách dưới.</summary>
public sealed class SectionHeaderLabel : Label
{
    public SectionHeaderLabel()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        using var accent = new SolidBrush(AgrisTheme.Primary);
        e.Graphics.FillRectangle(accent, 0, (Height - 20) / 2, 4, 20);

        using var pen = new Pen(AgrisTheme.BorderStrong);
        e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }
}

/// <summary>Dải thông báo: nền nhạt theo mức độ, vạch màu bên trái.</summary>
public sealed class NoticeLabel : Label
{
    public NoticeLabel()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        using var accent = new SolidBrush(ForeColor);
        e.Graphics.FillRectangle(accent, 0, 0, 3, Height);
    }
}

/// <summary>Nhãn dạng "chip" bo tròn — dùng cho badge người dùng / nhà máy / trạng thái.</summary>
public sealed class ChipLabel : Control
{
    public ChipLabel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Font = AgrisTheme.Ui(9.5F, FontStyle.Bold);
        Height = 32;
    }

    /// <summary>Ký tự icon (Segoe Fluent/MDL2) hiển thị trước chữ. Bỏ qua nếu máy không có font icon.</summary>
    public string? IconGlyph { get; set; }

    public Color ChipBackColor { get; set; } = AgrisTheme.SurfaceMuted;
    public Color ChipBorderColor { get; set; } = AgrisTheme.Border;

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        AccessibleName = Text;
        Width = PreferredWidth;
        Invalidate();
    }

    public int PreferredWidth
    {
        get
        {
            var text = TextRenderer.MeasureText(Text, Font).Width;
            var icon = IconGlyph is not null && AgrisTheme.Icon(11F) is not null ? 22 : 0;
            return text + icon + 24;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Shapes.EffectiveBackColor(Parent));
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Shapes.RoundedRect(rect, Height / 2);
        using var brush = new SolidBrush(ChipBackColor);
        g.FillPath(brush, path);
        using var pen = new Pen(ChipBorderColor);
        g.DrawPath(pen, path);

        var x = 12;
        if (IconGlyph is not null && AgrisTheme.Icon(11F) is { } iconFont)
        {
            TextRenderer.DrawText(g, IconGlyph, iconFont, new Rectangle(x, 0, 18, Height), ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            x += 22;
        }

        TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, Width - x - 10, Height), ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Thanh trắng có viền dưới mảnh. Giữ để tương thích.</summary>
public sealed class BrandBar : Panel
{
    public BrandBar()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.ResizeRedraw, true);
        BackColor = AgrisTheme.Surface;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width <= 0 || Height <= 0) return;

        e.Graphics.Clear(BackColor);
        using var borderPen = new Pen(AgrisTheme.Border);
        e.Graphics.DrawLine(borderPen, 0, Height - 1, Width, Height - 1);

        base.OnPaint(e);
    }
}

/// <summary>
/// Nút "nguy hiểm" dạng viền (ví dụ Đăng xuất): viền đỏ nhạt, hover tô đỏ. Owner-draw bo góc.
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
        TabStop = true;
        Font = AgrisTheme.Ui(9.5F, FontStyle.Bold);
        AccessibleName = text;
        AccessibleRole = AccessibleRole.PushButton;

        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
    }

    /// <summary>Ký tự icon hiển thị trước chữ (tùy chọn).</summary>
    public string? IconGlyph { get; set; }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Focus(); Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Shapes.EffectiveBackColor(Parent));
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);

        var (bg, border, text) = _pressed
            ? (AgrisTheme.Blend(AgrisTheme.Danger, Color.Black, 0.2f), AgrisTheme.Danger, Color.White)
            : _hovered
                ? (AgrisTheme.Danger, AgrisTheme.Danger, Color.White)
                : (AgrisTheme.Surface, Color.FromArgb(240, 180, 184), AgrisTheme.Danger);

        using var path = Shapes.RoundedRect(rect, AgrisTheme.RadiusMd);
        using var fill = new SolidBrush(bg);
        g.FillPath(fill, path);
        using var pen = new Pen(border);
        g.DrawPath(pen, path);

        if (Focused)
        {
            using var focusPath = Shapes.RoundedRect(new Rectangle(2, 2, Width - 5, Height - 5), AgrisTheme.RadiusSm);
            using var focusPen = new Pen(AgrisTheme.FocusRing, 1.5f);
            g.DrawPath(focusPen, focusPath);
        }

        var x = 0;
        var w = Width;
        if (IconGlyph is not null && AgrisTheme.Icon(11F) is { } iconFont)
        {
            var textW = TextRenderer.MeasureText(Text, Font).Width;
            var total = 18 + 6 + textW;
            x = Math.Max(4, (Width - total) / 2);
            TextRenderer.DrawText(g, IconGlyph, iconFont, new Rectangle(x, 0, 18, Height), text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            x += 24;
            w = Width - x;
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, w, Height), text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            return;
        }

        TextRenderer.DrawText(g, Text, Font, rect, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>Đường kẻ dọc mỏng phân tách các phần tử trên thanh header.</summary>
public sealed class VerticalSeparator : Control
{
    public VerticalSeparator()
    {
        Size = new Size(1, 32);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Shapes.EffectiveBackColor(Parent));
        using var pen = new Pen(AgrisTheme.Border);
        e.Graphics.DrawLine(pen, 0, 4, 0, Height - 4);
    }
}
