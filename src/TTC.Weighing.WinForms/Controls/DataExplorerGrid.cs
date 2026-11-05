using System.Drawing.Drawing2D;
using System.Globalization;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Theme;
using TTC.Weighing.Domain.Common;

namespace TTC.Weighing.WinForms.Controls;

/// <summary>Cách hiển thị một cột trong <see cref="DataExplorerGrid{T}"/>.</summary>
public enum ExplorerColumnKind
{
    /// <summary>Chữ thường, căn trái.</summary>
    Text,
    /// <summary>Mã kỹ thuật (Shipment, mã hàng, mã lệnh đốn) — font đơn cách, dễ đọc từng ký tự.</summary>
    Code,
    /// <summary>Số lượng — căn phải, N0; số 0 làm mờ.</summary>
    Number,
    /// <summary>Ngày giờ — dd/MM/yyyy HH:mm theo giờ máy; sắp xếp theo thời gian thật.</summary>
    Date,
    /// <summary>Nhãn trạng thái dạng pill màu.</summary>
    Badge
}

/// <summary>Tông màu cho cột <see cref="ExplorerColumnKind.Badge"/>.</summary>
public enum BadgeTone { Neutral, Success, Warning, Danger, Info, Brand }

/// <summary>Định nghĩa cột: tiêu đề (theo ngôn ngữ hiện hành), giá trị, độ rộng, kiểu hiển thị.</summary>
public sealed record ExplorerColumn<T>(
    Func<string> Header,
    Func<T, object?> Value,
    int Width = 140,
    ExplorerColumnKind Kind = ExplorerColumnKind.Text,
    Func<string, BadgeTone>? Tone = null);

/// <summary>
/// Bảng dữ liệu tra cứu chuẩn (Modern Enterprise): thẻ có tiêu đề + số dòng, thanh tìm kiếm,
/// lọc theo cột kiểu Excel (bấm tiêu đề), nhãn trạng thái dạng pill, phân trang ở chân bảng.
/// Phân trang/lọc phía client. Dùng cho các màn kỹ thuật ERP / FRM.
/// </summary>
public sealed class DataExplorerGrid<T> : Card
{
    private static int HeaderGlyphWidth => Theme.UiScale.Px(22);

    private readonly ExplorerColumn<T>[] _columns;
    private readonly Label _header;
    private readonly Label _lblCount;
    private readonly TextBox _txtSearch;
    private readonly Button _btnClearFilters;
    private readonly DataGridView _grid;
    private readonly GridPager _pager;
    private readonly Dictionary<int, HashSet<string>> _columnFilters = [];
    private readonly System.Windows.Forms.Timer _searchDebounce = new() { Interval = 250 };

    private List<T> _all = [];
    private List<T> _filtered = [];
    private string _title;

    public DataExplorerGrid(string title, string preferenceKey, IEnumerable<ExplorerColumn<T>> columns,
        string? searchPlaceholder = null, string? emptyMessage = null)
    {
        _columns = columns.ToArray();
        _title = title;

        Dock = DockStyle.Fill;
        Padding = new Padding(1);

        _header = GridBuilder.SectionHeader(title);

        // ── Thanh công cụ: tìm kiếm (trái) · số dòng + xóa bộ lọc (phải) ──
        _txtSearch = new TextBox
        {
            Width = 340,
            Font = AgrisTheme.Body(10F),
            PlaceholderText = "🔍  " + (searchPlaceholder ?? Loc.T("explorer.search_placeholder")),
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 6, 0, 0),
            AccessibleName = searchPlaceholder ?? Loc.T("explorer.search_placeholder")
        };
        Helpers.SearchText.Prepare(_txtSearch); // tắt bộ gõ → lọc ngay khi gõ, không đợi Space
        _txtSearch.TextChanged += (_, _) => { _searchDebounce.Stop(); _searchDebounce.Start(); };
        _txtSearch.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Escape || _txtSearch.TextLength == 0) return;
            _txtSearch.Clear();
            e.SuppressKeyPress = true;
        };
        _searchDebounce.Tick += (_, _) => { _searchDebounce.Stop(); _pager!.Reset(); ApplyFilter(); };

        _btnClearFilters = GridBuilder.SecondaryButton(Loc.T("explorer.clear_filters", 0), new Size(150, 36));
        _btnClearFilters.Visible = false;
        _btnClearFilters.Margin = new Padding(8, 0, 0, 0);
        _btnClearFilters.Click += (_, _) => ClearFilters();

        _lblCount = new Label
        {
            AutoSize = true,
            Font = AgrisTheme.Body(9F, FontStyle.Bold),
            ForeColor = AgrisTheme.TextSecondary,
            Margin = new Padding(8, 10, 0, 0)
        };

        var toolbar = new GridToolbar([_txtSearch], [_btnClearFilters, _lblCount]);

        // ── Lưới ──
        _grid = GridBuilder.ReadOnlyGrid(title);
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grid.ScrollBars = ScrollBars.Both;
        _grid.ColumnHeadersHeight = 44;
        _grid.RowTemplate.Height = 38;
        _grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
        _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(10, 0, HeaderGlyphWidth, 0);
        _grid.DefaultCellStyle.Padding = new Padding(10, 0, 8, 0);
        _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
        _grid.ShowCellToolTips = true; // xem đủ nội dung ô bị cắt khi rê chuột

        for (var i = 0; i < _columns.Length; i++)
        {
            var def = _columns[i];
            var column = new DataGridViewTextBoxColumn
            {
                HeaderText = def.Header(),
                Width = def.Width,
                MinimumWidth = 60,
                SortMode = DataGridViewColumnSortMode.Programmatic,
                Resizable = DataGridViewTriState.True
            };
            if (def.Kind == ExplorerColumnKind.Number)
            {
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
            }
            if (def.Kind == ExplorerColumnKind.Code)
                column.DefaultCellStyle.Font = AgrisTheme.Mono(9.5F);
            _grid.Columns.Add(column);
        }
        if (_grid.Columns.Count > 0) _grid.Columns[0].Frozen = true; // cột khóa chính luôn nhìn thấy khi cuộn ngang

        _grid.ColumnHeaderMouseClick += OnHeaderClick;
        _grid.CellFormatting += OnCellFormatting;
        _grid.CellPainting += OnCellPainting;
        _grid.CellMouseEnter += (_, e) =>
        {
            if (e.RowIndex == -1 && e.ColumnIndex >= 0) _grid.Cursor = Cursors.Hand;
        };
        _grid.CellMouseLeave += (_, _) => _grid.Cursor = Cursors.Default;
        GridBuilder.AttachEmptyState(_grid, emptyMessage ?? Loc.T("explorer.empty"));

        _pager = new GridPager(preferenceKey);
        _pager.PageChanged += (_, _) => Bind();

        // Dock: add Fill trước, các thanh Top/Bottom sau.
        Controls.Add(_grid);
        Controls.Add(_pager);
        Controls.Add(toolbar);
        Controls.Add(_header);

        Disposed += (_, _) => _searchDebounce.Dispose();
        UpdateCount();
    }

    /// <summary>Tiêu đề thẻ (đổi được khi BU / bối cảnh thay đổi).</summary>
    public string Title
    {
        get => _title;
        set { _title = value; UpdateCount(); }
    }

    /// <summary>Lưới bên trong — để form gắn thêm hành vi riêng (double-click, menu chuột phải…).</summary>
    public DataGridView Grid => _grid;

    /// <summary>Nạp dữ liệu. <paramref name="keepPage"/> = true khi nạp lại theo timer (giữ trang đang xem).</summary>
    public void SetData(IEnumerable<T> items, bool keepPage = false)
    {
        _all = items.ToList();
        if (!keepPage) _pager.Reset();
        ApplyFilter();
    }

    // ═══════════════════════════════════════════════════════════
    //  Lọc / tìm kiếm / phân trang
    // ═══════════════════════════════════════════════════════════

    private string DisplayText(T item, int col) => FormatValue(_columns[col], _columns[col].Value(item));

    private static string FormatValue(ExplorerColumn<T> def, object? value)
    {
        switch (value)
        {
            case null: return "";
            case string s when def.Kind == ExplorerColumnKind.Date:
                return TryParseDate(s) is { } d ? d.ToString("dd/MM/yyyy HH:mm") : s;
            case DateTimeOffset dto: return dto.ToVnTime().ToString("dd/MM/yyyy HH:mm");
            case DateTime dt: return dt.ToString("dd/MM/yyyy HH:mm");
            case decimal or double or float or int or long when def.Kind == ExplorerColumnKind.Number:
                return Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString("N0");
            case bool b: return Loc.YesNo(b);
            default: return value.ToString() ?? "";
        }
    }

    /// <summary>Chuỗi ngày từ DigiScale (FRM / ERP) — giữ nguyên giờ API trả, không cộng múi giờ.</summary>
    private static DateTime? TryParseDate(string s)
        => TTC.Weighing.Infrastructure.Adapters.DigiScaleDate.Parse(s);

    /// <summary>Khóa sắp xếp: số theo giá trị, ngày theo thời gian, còn lại theo chữ.</summary>
    private IComparable SortKey(T item, int col)
    {
        var def = _columns[col];
        var value = def.Value(item);
        return def.Kind switch
        {
            ExplorerColumnKind.Number when value is not null =>
                Convert.ToDecimal(value, CultureInfo.InvariantCulture),
            ExplorerColumnKind.Date => value switch
            {
                DateTimeOffset dto => dto.UtcDateTime,
                DateTime dt => dt,
                string s when TryParseDate(s) is { } d => d,
                _ => DateTime.MinValue
            },
            ExplorerColumnKind.Number => decimal.MinValue,
            _ => DisplayText(item, col)
        };
    }

    private void ApplyFilter()
    {
        var terms = _txtSearch.Text.Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        IEnumerable<T> query = _all;
        if (terms.Length > 0)
        {
            // Mọi từ khóa phải xuất hiện ở ít nhất một cột (tìm "59A mật" ra xe 59A chở mật rỉ).
            // Không dấu, bỏ qua "-" "." trong mã / biển số (xem SearchText).
            query = query.Where(item =>
                new Helpers.SearchText.Row(string.Join('\u001F',
                    Enumerable.Range(0, _columns.Length).Select(c => DisplayText(item, c)))).ContainsAll(terms));
        }

        foreach (var (col, allowed) in _columnFilters)
            query = query.Where(item => allowed.Contains(DisplayText(item, col)));

        _filtered = query.ToList();
        UpdateHeaderState();
        Bind();
    }

    private void Bind()
    {
        var page = _pager.Slice(_filtered);

        _grid.SuspendLayout();
        _grid.Rows.Clear();
        foreach (var item in page)
        {
            var values = new object[_columns.Length];
            for (var c = 0; c < _columns.Length; c++) values[c] = DisplayText(item, c);
            var index = _grid.Rows.Add(values);
            _grid.Rows[index].Tag = item;
        }
        _grid.ResumeLayout();
        _grid.ClearSelection();

        UpdateCount();
    }

    private void UpdateCount()
    {
        // Tiêu đề luôn có tổng số; thanh công cụ chỉ báo số khớp khi đang lọc (tránh lặp lại số với chân bảng).
        var filtered = _filtered.Count != _all.Count;
        _lblCount.Visible = filtered;
        _lblCount.Text = Loc.T("explorer.count_filtered", _filtered.Count.ToString("N0"), _all.Count.ToString("N0"));
        _header.Text = $"{_title}  ·  {Loc.T("explorer.count", _all.Count.ToString("N0"))}";
    }

    private void UpdateHeaderState()
    {
        for (var i = 0; i < _grid.Columns.Count; i++)
        {
            var active = _columnFilters.ContainsKey(i);
            var style = _grid.Columns[i].HeaderCell.Style;
            style.BackColor = active ? AgrisTheme.PrimarySoft : AgrisTheme.GridHeader;
            style.SelectionBackColor = style.BackColor;
            style.ForeColor = active ? AgrisTheme.Primary : AgrisTheme.TextSecondary;
            _grid.Columns[i].HeaderText = _columns[i].Header();
        }

        var count = _columnFilters.Count + (_txtSearch.TextLength > 0 ? 1 : 0);
        _btnClearFilters.Visible = count > 0;
        _btnClearFilters.Text = Loc.T("explorer.clear_filters", count);
        _grid.Invalidate();
    }

    private void ClearFilters()
    {
        _columnFilters.Clear();
        _txtSearch.Clear();
        _searchDebounce.Stop(); // Clear() vừa kích hoạt debounce — lọc ngay bên dưới, không lọc lần hai
        _pager.Reset();
        ApplyFilter();
    }

    private void OnHeaderClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.ColumnIndex < 0 || e.Button != MouseButtons.Left) return;
        var col = e.ColumnIndex;

        var values = _all
            .Select(item => DisplayText(item, col))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct()
            .OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        _columnFilters.TryGetValue(col, out var current);
        using var popup = new ColumnFilterPopup(values, current);

        // Đặt popup ngay dưới tiêu đề cột, không vượt khỏi màn hình.
        var headerRect = _grid.GetCellDisplayRectangle(col, -1, true);
        var screenPos = _grid.PointToScreen(new Point(headerRect.Left, headerRect.Bottom));
        var screen = Screen.FromPoint(screenPos).WorkingArea;
        var x = Math.Clamp(screenPos.X, screen.Left, Math.Max(screen.Left, screen.Right - popup.Width));
        var y = screenPos.Y + popup.Height > screen.Bottom ? screenPos.Y - popup.Height - headerRect.Height : screenPos.Y;
        popup.Location = new Point(x, Math.Max(screen.Top, y));

        if (popup.ShowDialog(FindForm()) != DialogResult.OK) return;

        if (popup.SortAscending is { } ascending)
        {
            _all = ascending
                ? _all.OrderBy(item => SortKey(item, col)).ToList()
                : _all.OrderByDescending(item => SortKey(item, col)).ToList();
            for (var i = 0; i < _grid.Columns.Count; i++)
                _grid.Columns[i].HeaderCell.SortGlyphDirection = SortOrder.None;
            _grid.Columns[col].HeaderCell.SortGlyphDirection = ascending ? SortOrder.Ascending : SortOrder.Descending;
        }

        if (popup.SelectedValues is null) _columnFilters.Remove(col);
        else _columnFilters[col] = popup.SelectedValues;

        _pager.Reset();
        ApplyFilter();
    }

    // ═══════════════════════════════════════════════════════════
    //  Vẽ: số 0 mờ, tooltip, pill trạng thái, biểu tượng lọc trên tiêu đề
    // ═══════════════════════════════════════════════════════════

    private void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.ColumnIndex >= _columns.Length) return;
        var text = e.Value as string ?? "";
        var cell = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex];

        // Tooltip chỉ khi chữ dài hơn ô.
        cell.ToolTipText = text.Length * 7 > _grid.Columns[e.ColumnIndex].Width ? text : "";

        if (_columns[e.ColumnIndex].Kind == ExplorerColumnKind.Number && text is "0" or "")
            e.CellStyle!.ForeColor = AgrisTheme.TextMuted;
    }

    private void OnCellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.Graphics is null || e.ColumnIndex < 0 || e.ColumnIndex >= _columns.Length) return;

        // Tiêu đề: GridBuilder đã vẽ nền + chữ; thêm biểu tượng lọc ở mép phải.
        if (e.RowIndex == -1)
        {
            var active = _columnFilters.ContainsKey(e.ColumnIndex);
            var glyphRect = new Rectangle(e.CellBounds.Right - HeaderGlyphWidth, e.CellBounds.Top,
                HeaderGlyphWidth - 4, e.CellBounds.Height);
            DrawFilterGlyph(e.Graphics, glyphRect, active);
            return;
        }

        var def = _columns[e.ColumnIndex];
        if (def.Kind != ExplorerColumnKind.Badge || e.Value is not string text || text.Length == 0) return;

        e.Paint(e.CellBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.Border
            | DataGridViewPaintParts.SelectionBackground);

        var (fore, back) = ToneColors(def.Tone?.Invoke(text) ?? BadgeTone.Neutral);
        var font = AgrisTheme.Body(8.5F, FontStyle.Bold);
        var size = TextRenderer.MeasureText(e.Graphics, text, font, Size.Empty, TextFormatFlags.NoPadding);
        var maxWidth = e.CellBounds.Width - 16;
        var pill = new Rectangle(e.CellBounds.Left + 8, e.CellBounds.Top + (e.CellBounds.Height - 22) / 2,
            Math.Min(size.Width + 18, maxWidth), 22);

        var oldMode = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Shapes.RoundedRect(pill, 11))
        using (var brush = new SolidBrush(back))
            e.Graphics.FillPath(brush, path);
        e.Graphics.SmoothingMode = oldMode; // không để đường kẻ ô vẽ sau bị nhòe

        TextRenderer.DrawText(e.Graphics, text, font, pill, fore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        e.Handled = true;
    }

    private static void DrawFilterGlyph(Graphics g, Rectangle area, bool active)
    {
        // Phễu nhỏ: đặc + màu thương hiệu khi đang lọc, viền mờ khi chưa lọc.
        var cx = area.Left + area.Width / 2f;
        var cy = area.Top + area.Height / 2f;
        PointF[] funnel =
        [
            new(cx - 5, cy - 4), new(cx + 5, cy - 4), new(cx + 1.2f, cy + 0.5f),
            new(cx + 1.2f, cy + 4.5f), new(cx - 1.2f, cy + 3.2f), new(cx - 1.2f, cy + 0.5f)
        ];

        var oldMode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (active)
        {
            using var brush = new SolidBrush(AgrisTheme.Primary);
            g.FillPolygon(brush, funnel);
        }
        else
        {
            using var pen = new Pen(AgrisTheme.TextMuted, 1.2f);
            g.DrawPolygon(pen, funnel);
        }
        g.SmoothingMode = oldMode;
    }

    private static (Color Fore, Color Back) ToneColors(BadgeTone tone) => tone switch
    {
        BadgeTone.Success => (AgrisTheme.Success, AgrisTheme.SuccessSoft),
        BadgeTone.Warning => (AgrisTheme.Warning, AgrisTheme.WarningSoft),
        BadgeTone.Danger => (AgrisTheme.Danger, AgrisTheme.DangerSoft),
        BadgeTone.Info => (AgrisTheme.Info, AgrisTheme.InfoSoft),
        BadgeTone.Brand => (AgrisTheme.BrandTeal, AgrisTheme.InfoSoft),
        _ => (AgrisTheme.TextSecondary, AgrisTheme.GridHeader)
    };
}
