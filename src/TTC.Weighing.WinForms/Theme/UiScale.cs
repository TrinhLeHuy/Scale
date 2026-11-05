using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;

namespace TTC.Weighing.WinForms.Theme;

/// <summary>
/// Thu phóng TOÀN BỘ giao diện theo màn hình của máy đang chạy, như thu nhỏ một bức ảnh:
/// chữ, logo, kích thước, khoảng cách, lưới, menu... cùng một tỉ lệ. Màn hình nhỏ thấy đúng bố cục
/// của màn hình thiết kế 1920x1080, chỉ nhỏ hơn.
/// <para>Cách làm (giống WinForms tự scale theo DPI):</para>
/// <list type="number">
/// <item>Form được dựng ở kích thước thiết kế: trong <see cref="DesignScope"/>, <see cref="Current"/> = 1,
///   nên font của <see cref="AgrisTheme"/> và <see cref="Px"/> đều ra cỡ thiết kế, khớp các số px viết trong code.</item>
/// <item><see cref="Apply"/> co cả cây control một lần: vị trí, kích thước, Padding/Margin, MinimumSize,
///   cột TableLayoutPanel (Control.Scale), cộng font từng control, lưới, tab, splitter.</item>
/// <item>Ngoài DesignScope, <see cref="Current"/> = <see cref="Factor"/>: chữ vẽ lúc chạy (OnPaint) và
///   control tạo thêm sau đó cũng ra đúng tỉ lệ.</item>
/// </list>
/// Cấu hình: <c>App:UiScale</c> = <c>auto</c> (mặc định) hoặc số cố định 0.5..1.
/// </summary>
public static class UiScale
{
    /// <summary>Vùng làm việc mà bố cục được thiết kế.</summary>
    public static readonly Size DesignArea = new(1920, 1080);

    /// <summary>Co tối đa còn một nửa (1024x600 vẫn hiện đủ).</summary>
    public const float MinFactor = 0.5F;

    private static float? _factor;
    private static int _designDepth;
    private static readonly ConditionalWeakTable<Control, object> Applied = new();

    /// <summary>Hệ số thu phóng của máy này, trong khoảng [<see cref="MinFactor"/>, 1]. 1 = không đổi.</summary>
    public static float Factor => _factor ??= FromScreen();

    /// <summary>Hệ số đang áp dụng cho font/px: 1 khi đang dựng form (DesignScope), còn lại = <see cref="Factor"/>.</summary>
    public static float Current => _designDepth > 0 ? 1F : Factor;

    public static bool IsScaled => Factor < 0.999F;

    /// <summary>Gọi một lần khi khởi động, TRƯỚC khi tạo font / form nào.</summary>
    public static void Initialize(IConfiguration configuration)
    {
        var value = configuration.GetValue<string>("App:UiScale");
        _factor = float.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var fixedFactor) && fixedFactor > 0
            ? Math.Clamp(fixedFactor, MinFactor, 1F)
            : FromScreen();
    }

    /// <summary>Số px thiết kế → px thật (dùng cho số px đặt lúc chạy: OnPaint, Resize, đổi trạng thái).</summary>
    public static int Px(int designPx) => Current >= 0.999F ? designPx : (int)Math.Round(designPx * Current);

    /// <summary>Tạo control/form ở kích thước thiết kế rồi thu phóng ngay theo màn hình.</summary>
    public static T Create<T>(Func<T> create, bool scaleWindow = false) where T : Control
    {
        T control;
        using (DesignScope())
            control = create();
        Apply(control, scaleWindow);
        return control;
    }

    /// <summary>Dựng form ở kích thước thiết kế. Bọc quanh việc tạo + hiện form, rồi gọi <see cref="Apply"/>.</summary>
    public static IDisposable DesignScope()
    {
        _designDepth++;
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        private bool _done;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            _designDepth--;
        }
    }

    /// <summary>
    /// Co cả cây control về <see cref="Factor"/>. Mỗi control chỉ co một lần.
    /// Form cấp cao nhất: co các control con, giữ nguyên kích thước cửa sổ (cửa sổ tự phóng to/đặt theo màn hình).
    /// </summary>
    /// <param name="root">Control / form đã dựng xong ở kích thước thiết kế.</param>
    /// <param name="scaleWindow">Form cấp cao nhất cỡ cố định (splash, hộp thoại): co cả kích thước cửa sổ.</param>
    public static void Apply(Control root, bool scaleWindow = false)
    {
        // Đang trong DesignScope ngoài cùng (ví dụ tab mở ngay khi dựng MainForm): để Apply của form cha co một lần.
        if (!IsScaled || _designDepth > 0 || Applied.TryGetValue(root, out _)) return;
        Applied.Add(root, new object());

        var factor = Factor;
        var size = new SizeF(factor, factor);

        // Ghi lại font RIÊNG của từng control trước khi đổi gì (font thừa hưởng từ cha thì để cha lo).
        var ownFonts = new List<(Control Control, Font Font)>();
        CollectOwnFonts(root, null, ownFonts);

        // Control có chiều cao do font quyết định (ô nhập, combo, ô ngày, nhãn/nút AutoSize): viền cố định
        // của Windows không co theo, nên sau khi co có thể cao hơn ô chứa vài px. Ghi chiều cao thiết kế để chỉnh.
        var fontDriven = new List<(Control Control, int DesignHeight)>();
        CollectFontDriven(root, fontDriven);

        root.SuspendLayout();
        try
        {
            if (root is Form { TopLevel: true } form && !scaleWindow)
            {
                foreach (Control child in form.Controls)
                    child.Scale(size);
                form.MinimumSize = ScaleSize(form.MinimumSize, factor);
            }
            else
            {
                root.Scale(size);
            }

            foreach (var (control, font) in ownFonts)
                control.Font = ScaledFont(font, factor);

            Walk(root, factor);
        }
        finally
        {
            root.ResumeLayout(true);
        }

        FitFontDriven(fontDriven, factor);
    }

    private static bool IsFontDriven(Control c) => c switch
    {
        TextBoxBase { Multiline: false } or ComboBox or DateTimePicker or UpDownBase => true,
        Label { AutoSize: true } or ButtonBase { AutoSize: true } or CheckBox { AutoSize: true } => true,
        _ => false
    };

    private static void CollectFontDriven(Control control, List<(Control, int)> list)
    {
        if (IsFontDriven(control) && control.Height > 0)
            list.Add((control, control.Height));

        foreach (Control child in control.Controls)
            CollectFontDriven(child, list);
    }

    /// <summary>
    /// Đưa chiều cao các control "theo font" về đúng tỉ lệ: nút AutoSize thì chặn chiều cao (chữ vẽ giữa nút),
    /// còn lại thì giảm nhẹ cỡ chữ (tối đa 18%) cho tới khi vừa chiều cao thiết kế × hệ số.
    /// </summary>
    private static void FitFontDriven(List<(Control Control, int DesignHeight)> controls, float factor)
    {
        foreach (var (control, designHeight) in controls)
        {
            var target = (int)Math.Round(designHeight * factor);
            if (control.Height <= target + 1) continue;

            if (control is ButtonBase)
            {
                control.MaximumSize = new Size(control.MaximumSize.Width, target);
                continue;
            }

            var baseFont = control.Font;
            for (var step = 1; step <= 6 && control.Height > target + 1; step++)
                control.Font = ScaledFont(baseFont, 1F - 0.03F * step);
        }
    }

    private static void CollectOwnFonts(Control control, Control? parent, List<(Control, Font)> list)
    {
        var font = control.Font;
        if (parent is null || !ReferenceEquals(font, parent.Font))
            list.Add((control, font));

        foreach (Control child in control.Controls)
            CollectOwnFonts(child, control, list);
    }

    /// <summary>Những thứ Control.Scale không tự co.</summary>
    private static void Walk(Control control, float factor)
    {
        switch (control)
        {
            case DataGridView grid:
                ScaleGrid(grid, factor);
                break;
            case TabControl { SizeMode: TabSizeMode.Fixed } tabs:
                tabs.ItemSize = ScaleSize(tabs.ItemSize, factor);
                break;
            // Vạch kẻ 1px nhân hệ số nhỏ làm tròn về 0 → mất vạch. Giữ tối thiểu 1px.
            case Panel { Width: 0, Height: > 0, Dock: not (DockStyle.Fill or DockStyle.Top or DockStyle.Bottom) } line:
                line.Width = 1;
                break;
            case Panel { Height: 0, Width: > 0, Dock: not (DockStyle.Fill or DockStyle.Left or DockStyle.Right) } line:
                line.Height = 1;
                break;
        }

        foreach (Control child in control.Controls)
            Walk(child, factor);
    }

    private static void ScaleGrid(DataGridView grid, float factor)
    {
        static int S(int v, float f) => Math.Max(1, (int)Math.Round(v * f));

        grid.ColumnHeadersHeight = S(grid.ColumnHeadersHeight, factor);
        grid.RowTemplate.Height = S(grid.RowTemplate.Height, factor);
        if (grid.RowHeadersVisible)
            grid.RowHeadersWidth = Math.Max(4, S(grid.RowHeadersWidth, factor));

        foreach (DataGridViewColumn column in grid.Columns)
        {
            column.MinimumWidth = Math.Max(2, S(column.MinimumWidth, factor));
            if (column.AutoSizeMode != DataGridViewAutoSizeColumnMode.Fill)
                column.Width = Math.Max(column.MinimumWidth, S(column.Width, factor));
            if (column.DefaultCellStyle.Font is { } colFont)
                column.DefaultCellStyle.Font = ScaledFont(colFont, factor);
        }

        foreach (var style in new[] { grid.DefaultCellStyle, grid.ColumnHeadersDefaultCellStyle,
                     grid.RowsDefaultCellStyle, grid.AlternatingRowsDefaultCellStyle, grid.RowHeadersDefaultCellStyle })
        {
            if (style.Font is { } font)
                style.Font = ScaledFont(font, factor);
            style.Padding = ScalePadding(style.Padding, factor);
        }

        foreach (DataGridViewRow row in grid.Rows)
            row.Height = S(row.Height, factor);
    }

    private static readonly Dictionary<(string, float, FontStyle, GraphicsUnit), Font> FontCache = [];

    private static Font ScaledFont(Font font, float factor)
    {
        var key = (font.FontFamily.Name, (float)Math.Round(font.Size * factor, 2), font.Style, font.Unit);
        lock (FontCache)
        {
            if (!FontCache.TryGetValue(key, out var scaled))
                FontCache[key] = scaled = new Font(font.FontFamily, Math.Max(1F, key.Item2), font.Style, font.Unit);
            return scaled;
        }
    }

    private static Size ScaleSize(Size s, float f)
        => new((int)Math.Round(s.Width * f), (int)Math.Round(s.Height * f));

    private static Padding ScalePadding(Padding p, float f)
        => new((int)Math.Round(p.Left * f), (int)Math.Round(p.Top * f), (int)Math.Round(p.Right * f), (int)Math.Round(p.Bottom * f));

    private static float FromScreen()
    {
        var work = Screen.PrimaryScreen?.WorkingArea.Size ?? DesignArea;
        var factor = Math.Min((float)work.Width / DesignArea.Width, (float)work.Height / DesignArea.Height);
        return Math.Clamp(factor, MinFactor, 1F);
    }
}
