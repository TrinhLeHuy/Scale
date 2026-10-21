using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Controls;

/// <summary>
/// Popup lọc cột kiểu Excel — sort A→Z/Z→A, tìm kiếm, checkbox danh sách giá trị.
/// </summary>
public sealed class ColumnFilterPopup : Form
{
    private readonly CheckedListBox _checklist = new()
    {
        Dock = DockStyle.Fill,
        CheckOnClick = true,
        BorderStyle = BorderStyle.None,
        Font = new Font("Segoe UI", 10F)
    };
    private readonly TextBox _txtSearch = new()
    {
        Dock = DockStyle.Top,
        PlaceholderText = "Tìm kiếm...",
        Font = new Font("Segoe UI", 10F),
        Height = 30
    };
    private readonly Button _btnSortAZ = new()
    {
        Text = "A→Z  Sắp xếp tăng dần",
        Dock = DockStyle.Top, Height = 32,
        FlatStyle = FlatStyle.Flat,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(8, 0, 0, 0),
        Font = new Font("Segoe UI", 10F)
    };
    private readonly Button _btnSortZA = new()
    {
        Text = "Z→A  Sắp xếp giảm dần",
        Dock = DockStyle.Top, Height = 32,
        FlatStyle = FlatStyle.Flat,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(8, 0, 0, 0),
        Font = new Font("Segoe UI", 10F)
    };
    private readonly CheckBox _chkAll = new()
    {
        Text = "Chọn Tất cả",
        Dock = DockStyle.Top, Height = 30,
        Checked = true,
        Font = new Font("Segoe UI", 10F, FontStyle.Bold),
        Padding = new Padding(4, 0, 0, 0)
    };
    private readonly Button _btnApply = new()
    {
        Text = "Áp dụng",
        Width = 100, Height = 34,
        BackColor = Color.FromArgb(0, 148, 68),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
        Font = new Font("Segoe UI", 10F, FontStyle.Bold)
    };
    private readonly Button _btnClear = new()
    {
        Text = "Xóa bộ lọc",
        Width = 100, Height = 34,
        FlatStyle = FlatStyle.Flat,
        Font = new Font("Segoe UI", 10F)
    };

    private string[] _allValues = [];
    private string[] _displayValues = [];

    /// <summary>Kết quả: giá trị được chọn (null = tất cả / xóa bộ lọc).</summary>
    public HashSet<string>? SelectedValues { get; private set; }

    /// <summary>Sort direction: null = không sort, true = A→Z, false = Z→A.</summary>
    public bool? SortAscending { get; private set; }

    public ColumnFilterPopup(string?[] uniqueValues, HashSet<string>? currentFilter)
    {
        _allValues = uniqueValues.Where(v => v is not null).Cast<string>().ToArray();
        _displayValues = _allValues;

        Text = "Lọc cột";
        Size = new Size(280, 420);
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        BackColor = Color.White;

        _btnSortAZ.FlatAppearance.BorderSize = 0;
        _btnSortZA.FlatAppearance.BorderSize = 0;
        _btnApply.FlatAppearance.BorderSize = 0;

        _btnSortAZ.Click += (_, _) => { SortAscending = true; DialogResult = DialogResult.OK; Close(); };
        _btnSortZA.Click += (_, _) => { SortAscending = false; DialogResult = DialogResult.OK; Close(); };
        _btnApply.Click += (_, _) => { ApplyFilter(); DialogResult = DialogResult.OK; Close(); };
        _btnClear.Click += (_, _) => { SelectedValues = null; SortAscending = null; DialogResult = DialogResult.OK; Close(); };

        _chkAll.CheckedChanged += (_, _) =>
        {
            for (var i = 0; i < _checklist.Items.Count; i++)
                _checklist.SetItemChecked(i, _chkAll.Checked);
        };

        _txtSearch.TextChanged += (_, _) => FilterList();

        // Layout
        var bottomBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 44,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(4, 4, 4, 4)
        };
        bottomBar.Controls.Add(_btnApply);
        bottomBar.Controls.Add(_btnClear);

        Controls.Add(_checklist);
        Controls.Add(_chkAll);
        Controls.Add(_txtSearch);
        Controls.Add(_btnSortZA);
        Controls.Add(_btnSortAZ);
        Controls.Add(bottomBar);

        // Populate
        PopulateList(currentFilter);
    }

    private void PopulateList(HashSet<string>? currentFilter)
    {
        _checklist.Items.Clear();
        foreach (var val in _displayValues)
        {
            var isChecked = currentFilter is null || currentFilter.Contains(val);
            _checklist.Items.Add(val, isChecked);
        }
    }

    private void FilterList()
    {
        var search = _txtSearch.Text.Trim().ToUpperInvariant();
        _displayValues = string.IsNullOrWhiteSpace(search)
            ? _allValues
            : _allValues.Where(v => v.ToUpperInvariant().Contains(search)).ToArray();

        _checklist.Items.Clear();
        foreach (var val in _displayValues)
            _checklist.Items.Add(val, true);
    }

    private void ApplyFilter()
    {
        var selected = new HashSet<string>();
        for (var i = 0; i < _checklist.Items.Count; i++)
        {
            if (_checklist.GetItemChecked(i))
                selected.Add(_checklist.Items[i].ToString()!);
        }

        // Nếu chọn tất cả → xóa filter
        SelectedValues = selected.Count == _allValues.Length ? null : selected;
    }
}
