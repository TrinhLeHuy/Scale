using System.Drawing.Drawing2D;
using System.Globalization;
using System.Reflection;
using TTC.Weighing.Domain.Common;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Controls;

/// <summary>
/// Bộ lọc giống bảng Dữ liệu FRM / ERP (<see cref="DataExplorerGrid{T}"/>) gắn vào lưới chuyến đã có sẵn
/// của các màn vận hành (Đăng tài, Cân hợp nhất, Đo tạp chất):
/// <list type="bullet">
/// <item>Ô tìm kiếm: mọi từ khóa phải có trong ít nhất một cột đang hiện ("59A mía" → xe 59A chở mía).</item>
/// <item>Bấm tiêu đề cột → popup kiểu Excel: sắp xếp A→Z / Z→A, tick chọn giá trị. Cột đang lọc tô màu + phễu đặc.</item>
/// <item>Nút "Xóa lọc (n)" hiện khi đang lọc.</item>
/// </list>
/// Lọc trên TOÀN BỘ danh sách trước khi phân trang (form gọi <see cref="Apply"/> rồi mới cắt trang).
/// Lưới vẫn bind / tô màu / phân trang như cũ — bộ lọc không thay đổi cách form nạp dữ liệu.
/// </summary>
public sealed class GridListFilter<T>
{
    private static int HeaderGlyphWidth => Theme.UiScale.Px(22);

    private readonly DataGridView _grid;
    private readonly Action _changed;
    private readonly Func<string, T, string?>? _textOverride;
    private readonly Dictionary<string, HashSet<string>> _columnFilters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PropertyInfo?> _props = new(StringComparer.Ordinal);
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 250 };
    private IReadOnlyList<T> _lastAll = [];
    private (string Property, bool Ascending)? _sort;

    /// <summary>Ô tìm kiếm — form đặt vào thanh công cụ của lưới.</summary>
    public TextBox SearchBox { get; }

    /// <summary>Nút xóa mọi bộ lọc — chỉ hiện khi đang lọc.</summary>
    public Button ClearButton { get; }

    /// <summary>Nhãn "Khớp x / y" — chỉ hiện khi đang lọc.</summary>
    public Label CountLabel { get; }

    /// <param name="grid">Lưới chuyến (cột có DataPropertyName).</param>
    /// <param name="changed">Bộ lọc đổi → form bind lại trang hiện tại (thường: pager.Reset() + BindCurrentPage()).</param>
    /// <param name="textOverride">Chữ hiển thị riêng của một cột (vd trạng thái đồng bộ) — trả null để dùng mặc định.</param>
    public GridListFilter(DataGridView grid, Action changed, Func<string, T, string?>? textOverride = null,
        string? searchPlaceholder = null)
    {
        _grid = grid;
        _changed = changed;
        _textOverride = textOverride;

        SearchBox = new TextBox
        {
            Width = 260,
            Font = AgrisTheme.Body(10F),
            PlaceholderText = "🔍  " + (searchPlaceholder ?? Loc.T("explorer.search_placeholder")),
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 6, 0, 0),
            AccessibleName = searchPlaceholder ?? Loc.T("explorer.search_placeholder")
        };
        Helpers.SearchText.Prepare(SearchBox); // tắt bộ gõ → lọc ngay khi gõ, không đợi Space
        SearchBox.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };
        SearchBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Escape || SearchBox.TextLength == 0) return;
            SearchBox.Clear();
            e.SuppressKeyPress = true;
        };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Changed(); };

        ClearButton = GridBuilder.SecondaryButton(Loc.T("explorer.clear_filters", 0), new Size(150, 36));
        ClearButton.Visible = false;
        ClearButton.Margin = new Padding(8, 0, 0, 0);
        ClearButton.Click += (_, _) => Clear();

        CountLabel = new Label
        {
            AutoSize = true,
            Visible = false,
            Font = AgrisTheme.Body(9F, FontStyle.Bold),
            ForeColor = AgrisTheme.TextSecondary,
            Margin = new Padding(8, 10, 0, 0)
        };

        _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(
            _grid.ColumnHeadersDefaultCellStyle.Padding.Left, 0, HeaderGlyphWidth, 0);
        _grid.ColumnHeaderMouseClick += OnHeaderClick;
        _grid.CellPainting += OnCellPainting;
        _grid.DataBindingComplete += (_, _) => UpdateHeaderState();
        _grid.CellMouseEnter += (_, e) => { if (e.RowIndex == -1 && e.ColumnIndex >= 0) _grid.Cursor = Cursors.Hand; };
        _grid.CellMouseLeave += (_, e) => { if (e.RowIndex == -1) _grid.Cursor = Cursors.Default; };
        _grid.Disposed += (_, _) => _debounce.Dispose();
    }

    /// <summary>Đang có bộ lọc / tìm kiếm.</summary>
    public bool IsActive => _columnFilters.Count > 0 || SearchBox.TextLength > 0;

    /// <summary>Lọc + sắp xếp toàn bộ danh sách. Gọi trước khi phân trang / bind.</summary>
    public IReadOnlyList<T> Apply(IReadOnlyList<T> all)
    {
        _lastAll = all;
        IEnumerable<T> query = all;

        var terms = SearchBox.Text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length > 0)
        {
            var visible = VisibleProperties().ToArray();
            // Không dấu, bỏ qua "-" "." trong mã / biển số (xem SearchText).
            query = query.Where(item =>
                new Helpers.SearchText.Row(string.Join('\u001F', visible.Select(p => Text(p, item)))).ContainsAll(terms));
        }

        foreach (var (prop, allowed) in _columnFilters)
            query = query.Where(item => allowed.Contains(Text(prop, item)));

        if (_sort is { } s)
            query = s.Ascending
                ? query.OrderBy(item => SortKey(s.Property, item))
                : query.OrderByDescending(item => SortKey(s.Property, item));

        var result = query.ToList();
        var filtered = IsActive;
        CountLabel.Visible = filtered;
        CountLabel.Text = Loc.T("explorer.count_filtered", result.Count.ToString("N0"), all.Count.ToString("N0"));
        return result;
    }

    private void Changed()
    {
        UpdateHeaderState();
        _changed();
    }

    private void Clear()
    {
        _columnFilters.Clear();
        SearchBox.Clear();
        _debounce.Stop();
        Changed();
    }

    // ═══════════════════════════════════════════════════════════
    //  Giá trị hiển thị của một ô (khớp với chữ người dùng thấy trên lưới)
    // ═══════════════════════════════════════════════════════════

    private IEnumerable<string> VisibleProperties() => _grid.Columns.Cast<DataGridViewColumn>()
        .Where(c => c.Visible && !string.IsNullOrEmpty(c.DataPropertyName))
        .Select(c => c.DataPropertyName);

    private PropertyInfo? Prop(string name)
    {
        if (!_props.TryGetValue(name, out var p))
            _props[name] = p = typeof(T).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        return p;
    }

    private DataGridViewColumn? ColumnOf(string property) => _grid.Columns.Cast<DataGridViewColumn>()
        .FirstOrDefault(c => c.DataPropertyName == property);

    private string Text(string property, T item)
    {
        if (_textOverride?.Invoke(property, item) is { } custom) return custom;
        var value = Prop(property)?.GetValue(item);
        var format = ColumnOf(property)?.DefaultCellStyle.Format;
        return value switch
        {
            null => "",
            Enum en => Loc.Enum(en),
            bool b => Loc.YesNo(b),
            DateTimeOffset dto => dto.ToVnTime().ToString(string.IsNullOrEmpty(format) ? "dd/MM/yyyy HH:mm" : format),
            DateTime dt => dt.ToString(string.IsNullOrEmpty(format) ? "dd/MM/yyyy HH:mm" : format),
            IFormattable f when !string.IsNullOrEmpty(format) => f.ToString(format, CultureInfo.CurrentCulture),
            _ => value.ToString() ?? ""
        };
    }

    /// <summary>Số theo giá trị, ngày theo thời gian, còn lại theo chữ hiển thị.</summary>
    private IComparable SortKey(string property, T item)
    {
        var value = Prop(property)?.GetValue(item);
        return value switch
        {
            DateTimeOffset dto => dto.UtcDateTime,
            DateTime dt => dt,
            decimal or double or float or int or long => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
            _ => Text(property, item)
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  Popup lọc cột + biểu tượng phễu trên tiêu đề
    // ═══════════════════════════════════════════════════════════

    private void OnHeaderClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.ColumnIndex < 0 || e.Button != MouseButtons.Left) return;
        var column = _grid.Columns[e.ColumnIndex];
        var prop = column.DataPropertyName;
        if (string.IsNullOrEmpty(prop) || Prop(prop) is null) return;

        var values = _lastAll
            .Select(item => Text(prop, item))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct()
            .OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        _columnFilters.TryGetValue(prop, out var current);
        using var popup = new ColumnFilterPopup(values, current);

        // Ngay dưới tiêu đề cột, không vượt khỏi màn hình.
        var headerRect = _grid.GetCellDisplayRectangle(e.ColumnIndex, -1, true);
        var screenPos = _grid.PointToScreen(new Point(headerRect.Left, headerRect.Bottom));
        var screen = Screen.FromPoint(screenPos).WorkingArea;
        var x = Math.Clamp(screenPos.X, screen.Left, Math.Max(screen.Left, screen.Right - popup.Width));
        var y = screenPos.Y + popup.Height > screen.Bottom ? screenPos.Y - popup.Height - headerRect.Height : screenPos.Y;
        popup.Location = new Point(x, Math.Max(screen.Top, y));

        if (popup.ShowDialog(_grid.FindForm()) != DialogResult.OK) return;

        if (popup.SortAscending is { } ascending) _sort = (prop, ascending);

        if (popup.SelectedValues is null) _columnFilters.Remove(prop);
        else _columnFilters[prop] = popup.SelectedValues;

        Changed();
    }

    private void UpdateHeaderState()
    {
        foreach (DataGridViewColumn c in _grid.Columns)
        {
            var active = !string.IsNullOrEmpty(c.DataPropertyName) && _columnFilters.ContainsKey(c.DataPropertyName);
            var style = c.HeaderCell.Style;
            style.BackColor = active ? AgrisTheme.PrimarySoft : AgrisTheme.GridHeader;
            style.SelectionBackColor = style.BackColor;
            style.ForeColor = active ? AgrisTheme.Primary : AgrisTheme.TextSecondary;
            c.HeaderCell.SortGlyphDirection = _sort is { } s && s.Property == c.DataPropertyName
                ? (s.Ascending ? SortOrder.Ascending : SortOrder.Descending)
                : SortOrder.None;
        }

        var count = _columnFilters.Count + (SearchBox.TextLength > 0 ? 1 : 0);
        ClearButton.Visible = count > 0;
        ClearButton.Text = Loc.T("explorer.clear_filters", count);
        _grid.Invalidate();
    }

    private void OnCellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.Graphics is null || e.RowIndex != -1 || e.ColumnIndex < 0) return;
        var prop = _grid.Columns[e.ColumnIndex].DataPropertyName;
        if (string.IsNullOrEmpty(prop) || Prop(prop) is null) return;

        // Vẽ tiêu đề mặc định trước (nếu chưa có handler khác vẽ), rồi thêm phễu ở mép phải.
        if (!e.Handled)
        {
            e.Paint(e.CellBounds, DataGridViewPaintParts.All);
            e.Handled = true;
        }

        var active = _columnFilters.ContainsKey(prop);
        var area = new Rectangle(e.CellBounds.Right - HeaderGlyphWidth, e.CellBounds.Top, HeaderGlyphWidth - 4, e.CellBounds.Height);
        var cx = area.Left + area.Width / 2f;
        var cy = area.Top + area.Height / 2f;
        PointF[] funnel =
        [
            new(cx - 5, cy - 4), new(cx + 5, cy - 4), new(cx + 1.2f, cy + 0.5f),
            new(cx + 1.2f, cy + 4.5f), new(cx - 1.2f, cy + 3.2f), new(cx - 1.2f, cy + 0.5f)
        ];
        // Khử răng cưa CHỈ cho phễu — trả lại ngay, nếu không các đường kẻ ô vẽ sau bị nhòe / nhạt màu.
        var oldMode = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (active)
        {
            using var brush = new SolidBrush(AgrisTheme.Primary);
            e.Graphics.FillPolygon(brush, funnel);
        }
        else
        {
            using var pen = new Pen(AgrisTheme.TextMuted, 1.2f);
            e.Graphics.DrawPolygon(pen, funnel);
        }
        e.Graphics.SmoothingMode = oldMode;
    }
}
