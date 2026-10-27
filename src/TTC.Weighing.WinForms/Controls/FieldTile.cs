using System.Drawing.Drawing2D;
using System.Globalization;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Controls;

/// <summary>Kiểu ô: nhập tay, giá trị tự tính (vẫn sửa được), hoặc chỉ hiển thị.</summary>
public enum FieldTileKind
{
    Input,
    Computed,
    ReadOnly
}

/// <summary>
/// Ô nhập cỡ lớn cho người vận hành lớn tuổi / màn hình cảm ứng:
/// nhãn rõ ở trên, số to ở giữa trong khung bo góc, gợi ý (khoảng giá trị, đơn vị) ở dưới.
/// Viền chuyển màu thương hiệu khi đang nhập để biết con trỏ đang ở ô nào.
/// Control tự vẽ — ThemeApplier bỏ qua để giữ cỡ chữ lớn.
/// </summary>
public sealed class FieldTile : Panel
{
    private readonly Control _input;
    private readonly Panel _host;
    private readonly Panel _clip;
    private readonly Label _caption;
    private readonly Label _hint;
    private readonly FieldTileKind _kind;
    private string? _badge;
    private Color _badgeBack = AgrisTheme.BrandGreenSoft;
    private Color _badgeFore = AgrisTheme.BrandGreenDark;

    /// <summary>Đổi nhãn phụ bên phải tiêu đề (ví dụ nguồn số liệu "MÁY ĐO" / "NHẬP TAY").</summary>
    public void SetBadge(string? badge, Color? back = null, Color? fore = null)
    {
        _badge = badge;
        _badgeBack = back ?? AgrisTheme.BrandGreenSoft;
        _badgeFore = fore ?? AgrisTheme.BrandGreenDark;
        Invalidate();
    }

    /// <summary>Đổi dòng gợi ý dưới ô.</summary>
    public void SetHint(string hint)
    {
        _hint.Text = hint;
        if (_hint.Height == 0) _hint.Height = 24;
    }

    public FieldTile(string caption, Control input, string? hint = null,
        FieldTileKind kind = FieldTileKind.Input, string? badge = null)
    {
        _input = input;
        _kind = kind;
        _badge = badge;

        Dock = DockStyle.Fill;
        Margin = new Padding(0, 0, 10, 10);
        Padding = new Padding(2, 4, 2, 2);
        BackColor = AgrisTheme.Surface;
        MinimumSize = new Size(150, 0);

        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);

        _caption = new Label
        {
            Text = caption,
            Dock = DockStyle.Top,
            Height = 30,
            Font = AgrisTheme.Ui(11F, FontStyle.Bold),
            ForeColor = AgrisTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            BackColor = Color.Transparent
        };

        _hint = new Label
        {
            Text = hint ?? string.Empty,
            Dock = DockStyle.Bottom,
            Height = hint is null ? 0 : 24,
            Font = AgrisTheme.Ui(9F),
            ForeColor = AgrisTheme.TextMuted,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            BackColor = Color.Transparent
        };

        _host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 12, 0) };
        _host.Resize += (_, _) => PositionInput();
        _host.Click += (_, _) => FocusInput();

        // Khung cắt: ô số được đặt rộng hơn khung đúng bằng bề rộng nút tăng/giảm,
        // nên nút nằm ngoài vùng nhìn thấy. Không ẩn nút bằng Visible = false vì NumericUpDown
        // không bố trí lại → chỗ trống vẽ ra ô trắng chứa chữ thừa.
        _clip = new Panel { Margin = Padding.Empty, Padding = Padding.Empty };
        _clip.Click += (_, _) => FocusInput();

        input.Font = input is NumericUpDown
            ? new Font(AgrisTheme.MonoFamily, kind == FieldTileKind.Computed ? 22F : 20F, FontStyle.Bold)
            : AgrisTheme.Ui(13F, FontStyle.Bold);
        input.ForeColor = kind == FieldTileKind.Computed ? AgrisTheme.BrandGreenDark : AgrisTheme.TextPrimary;
        input.Dock = DockStyle.None;
        input.Margin = Padding.Empty;

        switch (input)
        {
            case NumericUpDown num:
                num.BorderStyle = BorderStyle.None;
                num.TextAlign = HorizontalAlignment.Right;
                break;
            case TextBox txt:
                txt.BorderStyle = BorderStyle.None;
                txt.TabStop = !txt.ReadOnly;
                break;
        }

        if (string.IsNullOrEmpty(input.AccessibleName))
            input.AccessibleName = caption;
        if (hint is not null && string.IsNullOrEmpty(input.AccessibleDescription))
            input.AccessibleDescription = hint;

        input.Enter += (_, _) => Invalidate();
        input.Leave += (_, _) => Invalidate();
        input.EnabledChanged += (_, _) => ApplyFill();
        if (input is LockableNumericUpDown lockable)
            lockable.LockedChanged += (_, _) => ApplyFill();

        _clip.Controls.Add(input);
        _host.Controls.Add(_clip);
        Controls.Add(_host);
        Controls.Add(_hint);
        Controls.Add(_caption);

        ApplyFill();
    }

    /// <summary>Chiều cao gợi ý của ô: nhãn + khung nhập + gợi ý.</summary>
    public int PreferredTileHeight => _caption.Height + _hint.Height + Math.Max(56, _input.Height + 18) + Padding.Vertical + 6;

    private Color FillColor => !_input.Enabled ? AgrisTheme.ReadOnlyBg
        : _input is LockableNumericUpDown { Locked: true } ? AgrisTheme.SurfaceMuted
        : _kind switch
        {
            FieldTileKind.Computed => AgrisTheme.BrandGreenSoft,
            FieldTileKind.ReadOnly => AgrisTheme.SurfaceMuted,
            _ => AgrisTheme.InputBg
        };

    private void ApplyFill()
    {
        _host.BackColor = FillColor;
        _clip.BackColor = FillColor;
        _input.BackColor = FillColor;
        Invalidate();
    }

    private void FocusInput()
    {
        if (_input.Enabled && _input.CanFocus && _kind != FieldTileKind.ReadOnly)
            _input.Focus();
    }

    private void PositionInput()
    {
        var width = Math.Max(20, _host.ClientSize.Width - _host.Padding.Horizontal);
        var top = Math.Max(0, (_host.ClientSize.Height - _input.Height) / 2);

        _clip.Bounds = new Rectangle(_host.Padding.Left, top, width, _input.Height);

        // Phần nút tăng/giảm (Controls[0] của NumericUpDown) đẩy ra ngoài mép phải khung cắt.
        var hiddenSpin = _input is NumericUpDown { Controls.Count: > 0 } num ? num.Controls[0].Width + 2 : 0;
        _input.Bounds = new Rectangle(0, 0, width + hiddenSpin, _input.Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var focused = _input.ContainsFocus;
        var box = Rectangle.Inflate(_host.Bounds, 1, 1);
        box.Width -= 1;
        box.Height -= 1;

        var border = focused ? AgrisTheme.Primary
            : _kind == FieldTileKind.Computed ? Color.FromArgb(178, 222, 196)
            : _kind == FieldTileKind.ReadOnly ? AgrisTheme.Border
            : AgrisTheme.BorderStrong;

        using (var path = Shapes.RoundedRect(box, AgrisTheme.RadiusMd))
        {
            using var fill = new SolidBrush(FillColor);
            g.FillPath(fill, path);
            using var pen = new Pen(border, focused ? 2f : 1f);
            g.DrawPath(pen, path);
        }

        // Nhãn phụ bên phải tiêu đề (ví dụ "TỰ ĐỘNG").
        if (!string.IsNullOrEmpty(_badge))
        {
            var font = AgrisTheme.Ui(8F, FontStyle.Bold);
            var size = TextRenderer.MeasureText(_badge, font);
            var chip = new Rectangle(Width - size.Width - 18, _caption.Top + (_caption.Height - 22) / 2, size.Width + 14, 22);
            using var chipPath = Shapes.RoundedRect(chip, 11);
            using var chipBrush = new SolidBrush(_badgeBack);
            g.FillPath(chipBrush, chipPath);
            TextRenderer.DrawText(g, _badge, font, chip, _badgeFore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>
    /// Cấu hình ô số cho dễ nhập:
    /// <list type="bullet">
    /// <item>Nút tăng/giảm nhỏ (khó bấm) được FieldTile che đi — mũi tên lên/xuống trên bàn phím vẫn tăng/giảm được.</item>
    /// <item>Vào ô là bôi đen sẵn → gõ đè luôn, không phải xoá số cũ.</item>
    /// <item>Gõ dấu "." hay "," đều được hiểu là dấu thập phân.</item>
    /// <item>Enter → chốt giá trị và nhảy sang ô tiếp theo.</item>
    /// </list>
    /// </summary>
    public static void ConfigureNumeric(NumericUpDown num, Func<Control?> next)
    {
        // Nút tăng/giảm KHÔNG ẩn ở đây — FieldTile đẩy nó ra ngoài vùng nhìn thấy (xem PositionInput).
        var edit = num.Controls.OfType<TextBox>().FirstOrDefault();

        void SelectAll() => num.BeginInvoke(() => num.Select(0, num.Text.Length));

        num.Enter += (_, _) => SelectAll();

        // Bấm chuột vào ô lần đầu cũng bôi đen toàn bộ.
        var selectOnNextClick = true;
        num.Enter += (_, _) => selectOnNextClick = true;
        if (edit is not null)
        {
            edit.MouseUp += (_, _) =>
            {
                if (!selectOnNextClick) return;
                selectOnNextClick = false;
                SelectAll();
            };
        }

        num.KeyPress += (_, e) =>
        {
            if (e.KeyChar is not ('.' or ',') || edit is null) return;

            var separator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;

            // Chỉ cho một dấu thập phân.
            e.Handled = true;
            if (!edit.Text.Contains(separator) || edit.SelectedText.Contains(separator))
                edit.SelectedText = separator;
        };

        num.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;

            e.Handled = true;
            e.SuppressKeyPress = true;

            // Đọc Value buộc NumericUpDown phân tích chữ vừa gõ → ValueChanged chạy (tính CCS).
            _ = num.Value;

            var target = next();
            if (target is { Enabled: true, CanFocus: true })
                target.Focus();
            else
                SelectAll();
        };
    }
}
