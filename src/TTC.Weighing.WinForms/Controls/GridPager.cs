using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Controls;

/// <summary>
/// Thanh phân trang đặt ở chân lưới: "Hiển thị 1–20 / 135" bên trái,
/// chọn số dòng/trang (10/20/50/100) và « ‹ Trang x/y › » bên phải.
/// Phân trang phía client: form giữ toàn bộ danh sách, gọi <see cref="Slice{T}"/> để lấy trang hiện tại.
/// Trang hiện tại được giữ khi dữ liệu nạp lại theo timer (tự kẹp nếu vượt số trang mới).
/// </summary>
public sealed class GridPager : Panel
{
    public static readonly int[] PageSizes = [10, 20, 50, 100];

    /// <summary>Số dòng/trang đã chọn, nhớ theo từng lưới trong phiên làm việc.</summary>
    private static readonly Dictionary<string, int> _rememberedSizes = [];

    private readonly string _preferenceKey;
    private readonly Label _lblInfo;
    private readonly ComboBox _cboSize;
    private readonly Label _lblPage;
    private readonly Button _btnFirst;
    private readonly Button _btnPrev;
    private readonly Button _btnNext;
    private readonly Button _btnLast;
    private bool _suppressEvents;

    /// <summary>Phát khi người dùng đổi trang hoặc đổi số dòng/trang — form bind lại lưới.</summary>
    public event EventHandler? PageChanged;

    public GridPager(string preferenceKey, int defaultPageSize = 20)
    {
        _preferenceKey = preferenceKey;
        PageSize = _rememberedSizes.TryGetValue(preferenceKey, out var remembered) ? remembered : defaultPageSize;

        Dock = DockStyle.Bottom;
        Height = 54;
        BackColor = AgrisTheme.SurfaceMuted;
        Padding = new Padding(14, 0, 10, 0);
        AccessibleName = Loc.T("grid.pager_accessible");

        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        _lblInfo = new Label
        {
            Dock = DockStyle.Left,
            AutoSize = false,
            Width = 360,
            Font = AgrisTheme.Body(9F),
            ForeColor = AgrisTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };

        var right = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 7, 0, 0)
        };

        var lblSize = new Label
        {
            Text = Loc.T("grid.rows_per_page"),
            AutoSize = true,
            Font = AgrisTheme.Body(9F),
            ForeColor = AgrisTheme.TextMuted,
            Margin = new Padding(0, 9, 6, 0)
        };

        _cboSize = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 86,
            Margin = new Padding(0, 3, 18, 0),
            AccessibleName = Loc.T("grid.rows_per_page")
        };
        foreach (var size in PageSizes)
            _cboSize.Items.Add(size);
        _cboSize.SelectedItem = PageSizes.Contains(PageSize) ? PageSize : 20;
        _cboSize.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressEvents || _cboSize.SelectedItem is not int size) return;

            // Giữ dòng đầu tiên đang xem trong tầm nhìn khi đổi cỡ trang.
            var firstIndex = (CurrentPage - 1) * PageSize;
            PageSize = size;
            _rememberedSizes[_preferenceKey] = size;
            CurrentPage = firstIndex / size + 1;
            RaisePageChanged();
        };

        _btnFirst = NavButton("«", Loc.T("grid.first_page"));
        _btnPrev = NavButton("‹", Loc.T("grid.prev_page"));
        _btnNext = NavButton("›", Loc.T("grid.next_page"));
        _btnLast = NavButton("»", Loc.T("grid.last_page"));

        _lblPage = new Label
        {
            AutoSize = false,
            Size = new Size(150, 40),
            Font = AgrisTheme.Body(9F, FontStyle.Bold),
            ForeColor = AgrisTheme.TextPrimary,
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(2, 0, 2, 0)
        };

        _btnFirst.Click += (_, _) => GoTo(1);
        _btnPrev.Click += (_, _) => GoTo(CurrentPage - 1);
        _btnNext.Click += (_, _) => GoTo(CurrentPage + 1);
        _btnLast.Click += (_, _) => GoTo(TotalPages);

        right.Controls.AddRange([lblSize, _cboSize, _btnFirst, _btnPrev, _lblPage, _btnNext, _btnLast]);

        Controls.Add(_lblInfo);
        Controls.Add(right);

        UpdateDisplay();
    }

    /// <summary>
    /// Luôn được xếp chỗ (dock) TRƯỚC tiêu đề / thanh lọc của thẻ: WinForms dock từ control cuối danh sách lên,
    /// nên thanh phân trang được đưa về cuối danh sách mỗi khi thẻ thêm control. Nhờ vậy khi lưới thấp
    /// (thanh lọc xuống 2 hàng với chữ Khmer/Lào, màn hình nhỏ) thanh phân trang vẫn nằm trong thẻ —
    /// phần bị thu hẹp là vùng dòng dữ liệu, không phải thanh phân trang.
    /// </summary>
    protected override void OnParentChanged(EventArgs e)
    {
        if (_dockParent is not null) _dockParent.ControlAdded -= OnSiblingAdded;
        base.OnParentChanged(e);
        _dockParent = Parent;
        if (_dockParent is null) return;

        _dockParent.ControlAdded += OnSiblingAdded;
        SendToBack();
    }

    private Control? _dockParent;

    private void OnSiblingAdded(object? sender, ControlEventArgs e)
    {
        if (e.Control != this && !IsDisposed) SendToBack();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _dockParent is not null) _dockParent.ControlAdded -= OnSiblingAdded;
        base.Dispose(disposing);
    }

    public int PageSize { get; private set; }
    public int CurrentPage { get; private set; } = 1;
    public int TotalCount { get; private set; }
    public int TotalPages => Math.Max(1, (TotalCount + PageSize - 1) / PageSize);

    /// <summary>
    /// Lấy các dòng của trang hiện tại từ toàn bộ danh sách và cập nhật thanh phân trang.
    /// </summary>
    public List<T> Slice<T>(IReadOnlyList<T> all)
    {
        TotalCount = all.Count;
        CurrentPage = Math.Clamp(CurrentPage, 1, TotalPages);
        UpdateDisplay();

        return all.Skip((CurrentPage - 1) * PageSize).Take(PageSize).ToList();
    }

    private bool _pagingEnabled = true;

    /// <summary>
    /// Bật/tắt phân trang. Tắt → thanh phân trang ẩn, <see cref="Page{T}"/> trả toàn bộ danh sách (lưới cuộn dọc).
    /// Dùng cho lưới chỉ phân trang khi "Phóng to lưới".
    /// </summary>
    public bool PagingEnabled
    {
        get => _pagingEnabled;
        set
        {
            _pagingEnabled = value;
            Visible = value;
            if (value) Reset();
        }
    }

    /// <summary>Trang hiện tại khi đang phân trang; toàn bộ danh sách khi đã tắt phân trang.</summary>
    public List<T> Page<T>(IReadOnlyList<T> all) => _pagingEnabled ? Slice(all) : all.ToList();

    /// <summary>Về trang đầu (dùng khi đổi bộ lọc/tab — danh sách là tập khác).</summary>
    public void Reset() => CurrentPage = 1;

    private void GoTo(int page)
    {
        var target = Math.Clamp(page, 1, TotalPages);
        if (target == CurrentPage) return;

        CurrentPage = target;
        RaisePageChanged();
    }

    private void RaisePageChanged()
    {
        UpdateDisplay();
        PageChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateDisplay()
    {
        if (InvokeRequired) { Invoke(UpdateDisplay); return; }

        var from = TotalCount == 0 ? 0 : (CurrentPage - 1) * PageSize + 1;
        var to = Math.Min(CurrentPage * PageSize, TotalCount);

        _lblInfo.Text = Loc.T("grid.page_info", from.ToString("N0"), to.ToString("N0"), TotalCount.ToString("N0"));
        _lblPage.Text = Loc.T("grid.page_of", CurrentPage.ToString("N0"), TotalPages.ToString("N0"));

        _btnFirst.Enabled = _btnPrev.Enabled = CurrentPage > 1;
        _btnNext.Enabled = _btnLast.Enabled = CurrentPage < TotalPages;

        _suppressEvents = true;
        if (!Equals(_cboSize.SelectedItem, PageSize))
            _cboSize.SelectedItem = PageSize;
        _suppressEvents = false;
    }

    private static Button NavButton(string glyph, string accessibleName)
    {
        var button = GridBuilder.SecondaryButton(glyph, new Size(44, 40));
        button.MinimumSize = Size.Empty;
        button.Size = new Size(44, 40);
        button.Font = AgrisTheme.Body(11F, FontStyle.Bold);
        button.Margin = new Padding(0, 0, 4, 0);
        button.AccessibleName = accessibleName;
        return button;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(AgrisTheme.Border);
        e.Graphics.DrawLine(pen, 0, 0, Width, 0);
    }
}
