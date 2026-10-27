using System.Drawing.Drawing2D;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Controls;

/// <summary>Trạng thái một ô lần cân mẫu tạp chất.</summary>
public enum WeighingSlotState
{
    /// <summary>Chưa tới lượt (vd. lần 2 khi chưa có lần 1) — mờ, không bấm được.</summary>
    Locked,
    /// <summary>Đang chờ cân — viền đậm, nhấp nháy chấm "đang cân".</summary>
    Active,
    /// <summary>Đã có số — bấm để điều chỉnh (lần cân thứ 3) nếu còn lượt.</summary>
    Done,
    /// <summary>Đã có số, hết lượt điều chỉnh / đang xem lại — chỉ hiển thị.</summary>
    Final
}

/// <summary>
/// Ô "Lần cân" lớn, bấm được: số thứ tự trong vòng tròn, tên lần cân, số kg cỡ lớn, dòng trạng thái và nhãn nguồn
/// (Cân / Nhập tay / Đã điều chỉnh). Ô đang cân có viền màu thương hiệu dày + chấm báo, đúng một ô tại một thời điểm.
/// Tự vẽ để chữ số không bị cắt và đổi màu theo trạng thái rõ ràng ở trạm cân.
/// </summary>
public sealed class WeighingSlotCard : Control
{
    private readonly int _number;
    private string _title;
    private string _subtitle;
    private string _value = "—";
    private string _status = string.Empty;
    private string? _tag;
    private Color _tagColor = AgrisTheme.Info;
    private WeighingSlotState _state = WeighingSlotState.Locked;
    private bool _hovered;

    public WeighingSlotCard(int number, string title, string subtitle)
    {
        _number = number;
        _title = title;
        _subtitle = subtitle;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Height = PreferredCardHeight;
        MinimumSize = new Size(180, PreferredCardHeight);
        Margin = new Padding(0);
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
        UpdateAccessibility();
    }

    public static int PreferredCardHeight => 132;

    public WeighingSlotState State => _state;

    /// <summary>Cập nhật toàn bộ nội dung ô một lần (tránh vẽ lại nhiều lần).</summary>
    public void SetContent(WeighingSlotState state, string value, string status, string? tag = null, Color? tagColor = null)
    {
        _state = state;
        _value = value;
        _status = status;
        _tag = tag;
        _tagColor = tagColor ?? AgrisTheme.Info;
        Cursor = state == WeighingSlotState.Done || state == WeighingSlotState.Active ? Cursors.Hand : Cursors.Default;
        UpdateAccessibility();
        Invalidate();
    }

    private void UpdateAccessibility()
    {
        AccessibleName = $"{_number}. {_title}";
        AccessibleDescription = $"{_value} — {_status}";
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Shapes.EffectiveBackColor(Parent));
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var (accent, fill, border, borderWidth) = _state switch
        {
            WeighingSlotState.Active => (AgrisTheme.BrandTeal, AgrisTheme.InfoSoft, AgrisTheme.BrandTeal, 3f),
            WeighingSlotState.Done => (AgrisTheme.BrandGreen, AgrisTheme.SuccessSoft,
                _hovered ? AgrisTheme.BrandGreen : Color.FromArgb(178, 222, 196), _hovered ? 2f : 1.5f),
            WeighingSlotState.Final => (AgrisTheme.BrandGreen, AgrisTheme.SurfaceMuted, AgrisTheme.Border, 1f),
            _ => (AgrisTheme.TextMuted, AgrisTheme.SurfaceMuted, AgrisTheme.Border, 1f)
        };
        if (Focused) border = AgrisTheme.FocusRing;

        var rect = new Rectangle(1, 1, Width - 3, Height - 3);
        using (var path = Shapes.RoundedRect(rect, AgrisTheme.RadiusMd + 2))
        {
            using var b = new SolidBrush(fill);
            g.FillPath(b, path);
            using var p = new Pen(border, borderWidth);
            g.DrawPath(p, path);
        }

        // Vòng tròn số thứ tự
        var circle = new Rectangle(16, 16, 34, 34);
        using (var cb = new SolidBrush(_state == WeighingSlotState.Locked ? AgrisTheme.BorderStrong : accent))
            g.FillEllipse(cb, circle);
        TextRenderer.DrawText(g, _number.ToString(), AgrisTheme.Ui(13F, FontStyle.Bold), circle, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        var textLeft = circle.Right + 12;
        var textWidth = Width - textLeft - 16;
        var muted = _state == WeighingSlotState.Locked;
        TextRenderer.DrawText(g, _title, AgrisTheme.Ui(11F, FontStyle.Bold),
            new Rectangle(textLeft, 12, textWidth, 22), muted ? AgrisTheme.TextMuted : AgrisTheme.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, _subtitle, AgrisTheme.Ui(8.5F),
            new Rectangle(textLeft, 34, textWidth, 18), AgrisTheme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        // Nhãn nguồn (Cân / Nhập tay / Điều chỉnh) góc phải trên
        if (!string.IsNullOrEmpty(_tag))
        {
            var tf = AgrisTheme.Ui(8F, FontStyle.Bold);
            var sz = TextRenderer.MeasureText(_tag, tf);
            var chip = new Rectangle(Width - sz.Width - 30, 14, sz.Width + 14, 22);
            using var chipPath = Shapes.RoundedRect(chip, 11);
            using var chipBrush = new SolidBrush(AgrisTheme.Blend(AgrisTheme.Surface, _tagColor, 0.15f));
            g.FillPath(chipBrush, chipPath);
            TextRenderer.DrawText(g, _tag, tf, chip, _tagColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        // Số kg cỡ lớn — tự thu nhỏ khi ô hẹp
        var valueRect = new Rectangle(16, 56, Width - 32, 44);
        var font = new[] { 24F, 21F, 18F, 15F }
            .Select(s => AgrisTheme.Mono(s, FontStyle.Bold))
            .FirstOrDefault(f => TextRenderer.MeasureText(_value, f).Width <= valueRect.Width)
            ?? AgrisTheme.Mono(13F, FontStyle.Bold);
        TextRenderer.DrawText(g, _value, font, valueRect,
            muted ? AgrisTheme.TextMuted : _state == WeighingSlotState.Active ? AgrisTheme.BrandTealDark : AgrisTheme.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

        // Dòng trạng thái / gợi ý thao tác
        TextRenderer.DrawText(g, _status, AgrisTheme.Ui(9F, _state == WeighingSlotState.Active ? FontStyle.Bold : FontStyle.Regular),
            new Rectangle(16, Height - 30, Width - 32, 20),
            _state == WeighingSlotState.Active ? AgrisTheme.BrandTeal : AgrisTheme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
    }
}
