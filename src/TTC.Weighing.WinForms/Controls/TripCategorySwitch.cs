using TTC.Weighing.Application.Queries;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Controls;

/// <summary>
/// Công tắc phân đoạn "Mía | Vật tư" đặt đầu thanh công cụ của mọi lưới chuyến.
/// Hai nghiệp vụ luôn hiển thị TÁCH RIÊNG — không có lựa chọn gộp chung.
/// Mía tô xanh lá thương hiệu, Vật tư tô xanh biển để người vận hành nhận ra ngay đang xem lưới nào.
/// Lựa chọn được nhớ theo từng lưới trong phiên làm việc.
/// </summary>
public sealed class TripCategorySwitch : FlowLayoutPanel
{
    private static readonly Dictionary<string, TripCategory> _remembered = [];

    private readonly string _preferenceKey;
    private readonly ModernButton _btnSugarcane;
    private readonly ModernButton _btnMaterial;

    /// <summary>Phát khi người dùng (hoặc code với raiseEvent=true) đổi nhóm.</summary>
    public event EventHandler? CategoryChanged;

    public TripCategorySwitch(string preferenceKey, TripCategory defaultCategory = TripCategory.Sugarcane)
    {
        _preferenceKey = preferenceKey;
        Category = _remembered.TryGetValue(preferenceKey, out var remembered) ? remembered : defaultCategory;
        if (Category == TripCategory.All) Category = TripCategory.Sugarcane;

        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FlowDirection = FlowDirection.LeftToRight;
        WrapContents = false;
        BackColor = Color.Transparent;
        Margin = new Padding(0, 0, 16, 0);
        Padding = Padding.Empty;
        AccessibleRole = AccessibleRole.Grouping;
        AccessibleName = Loc.T("category.accessible");

        _btnSugarcane = Segment(Loc.T("category.sugarcane_plain"));
        _btnMaterial = Segment(Loc.T("category.material_plain"));
        _btnSugarcane.Margin = new Padding(0, 0, 2, 0);

        _btnSugarcane.Click += (_, _) => SetCategory(TripCategory.Sugarcane);
        _btnMaterial.Click += (_, _) => SetCategory(TripCategory.Material);

        Controls.AddRange([_btnSugarcane, _btnMaterial]);
        ApplyStyles();

        Loc.Changed += OnLanguageChanged;
    }

    /// <summary>Nhóm đang chọn — luôn là Sugarcane hoặc Material.</summary>
    public TripCategory Category { get; private set; }

    /// <summary>Tên nhóm theo ngôn ngữ hiện hành (dùng cho tiêu đề file xuất).</summary>
    public string CategoryLabel => LabelFor(Category);

    /// <summary>Hậu tố ASCII cho tên file xuất.</summary>
    public string FileSuffix => Category == TripCategory.Material ? "vat-tu" : "mia";

    /// <summary>Màu nhận diện nhóm (vạch nhấn tiêu đề lưới, tab đang chọn).</summary>
    public Color AccentColor => AccentFor(Category);

    public static Color AccentFor(TripCategory category)
        => category == TripCategory.Material ? AgrisTheme.BrandTeal : AgrisTheme.BrandGreen;

    public static string LabelFor(TripCategory category)
        => category == TripCategory.Material ? Loc.T("category.material_plain") : Loc.T("category.sugarcane_plain");

    public void SetCategory(TripCategory category, bool raiseEvent = true)
    {
        if (category == TripCategory.All) category = TripCategory.Sugarcane;
        if (category == Category) return;

        Category = category;
        _remembered[_preferenceKey] = category;
        ApplyStyles();

        if (raiseEvent)
            CategoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyStyles()
    {
        Style(_btnSugarcane, Category == TripCategory.Sugarcane, AgrisTheme.BrandGreen);
        Style(_btnMaterial, Category == TripCategory.Material, AgrisTheme.BrandTeal);
    }

    private static void Style(ModernButton button, bool active, Color accent)
    {
        button.BackColor = active ? accent : AgrisTheme.Surface;
        button.ForeColor = active ? AgrisTheme.TextOnBrand : AgrisTheme.TextSecondary;
        button.Font = AgrisTheme.Body(9.5F, active ? FontStyle.Bold : FontStyle.Regular);
        button.FlatAppearance.MouseOverBackColor = active
            ? AgrisTheme.Blend(accent, Color.Black, 0.12f)
            : AgrisTheme.SurfaceHover;
        button.AccessibleDescription = active ? Loc.T("category.selected") : null;
        button.Invalidate();
    }

    private static ModernButton Segment(string text)
    {
        var button = new ModernButton
        {
            Text = text,
            Size = new Size(130, 40),
            AccessibleName = text,
            AccessibleRole = AccessibleRole.RadioButton,
            CornerRadius = AgrisTheme.RadiusMd
        };
        button.FlatAppearance.BorderColor = AgrisTheme.BorderStrong;
        return button;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => OnLanguageChanged(sender, e)); return; }

        _btnSugarcane.Text = _btnSugarcane.AccessibleName = Loc.T("category.sugarcane_plain");
        _btnMaterial.Text = _btnMaterial.AccessibleName = Loc.T("category.material_plain");
        AccessibleName = Loc.T("category.accessible");
        ApplyStyles();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Loc.Changed -= OnLanguageChanged;
        base.Dispose(disposing);
    }
}
