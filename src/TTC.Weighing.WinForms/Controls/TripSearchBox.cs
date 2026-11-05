using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Controls;

/// <summary>
/// Ô tìm kiếm chuyến — nhập mã đăng tài hoặc biển số xe, có popup đề xuất.
/// Dùng chung cho CcsForm, QualityForm, WeighOperationForm.
/// </summary>
public sealed class TripSearchBox : UserControl
{
    public event EventHandler<TripSearchSelectedEventArgs>? TripSelected;

    private readonly TextBox _txtSearch = new()
    {
        Dock = DockStyle.Fill,
        Font = AgrisTheme.Mono(11F),
        PlaceholderText = "Nhập mã đăng tài hoặc biển số xe...",
        AccessibleName = "Tìm chuyến"
    };

    private readonly ListBox _lstPopup = new()
    {
        Visible = false,
        BorderStyle = BorderStyle.FixedSingle,
        IntegralHeight = false,
        Font = AgrisTheme.Body(9.5F)
    };

    /// <summary>Danh sách chuyến đã đăng tài — load từ DB.</summary>
    private List<TripSearchItem> _trips = [];

    public TripSearchBox()
    {
        Height = 30;
        Dock = DockStyle.Fill;

        Helpers.SearchText.Prepare(_txtSearch); // tắt bộ gõ → gợi ý ngay khi gõ, không đợi Space
        _txtSearch.TextChanged += OnTextChanged;
        _txtSearch.KeyDown += OnKeyDown;
        _txtSearch.Leave += OnLeave;
        _lstPopup.Click += OnPopupClick;
        _lstPopup.KeyDown += OnPopupKeyDown;

        Controls.Add(_txtSearch);
    }

    /// <summary>Text hiện tại trong ô tìm kiếm.</summary>
    public string SearchText => _txtSearch.Text.Trim();

    /// <summary>Nạp danh sách chuyến đã đăng tài.</summary>
    public void LoadTrips(IEnumerable<TripSearchItem> trips)
    {
        _trips = trips.ToList();
    }

    /// <summary>Thêm popup vào form cha — gọi sau khi control được add vào form.</summary>
    public void AttachPopupToForm(Form form)
    {
        form.Controls.Add(_lstPopup);
        _lstPopup.BringToFront();
    }

    public void Clear()
    {
        _txtSearch.Clear();
        _lstPopup.Visible = false;
    }

    public new void Focus() => _txtSearch.Focus();

    private void OnTextChanged(object? sender, EventArgs e)
    {
        var text = _txtSearch.Text.Trim();
        if (text.Length == 0 || _trips.Count == 0)
        {
            _lstPopup.Visible = false;
            return;
        }

        var matched = _trips
            .Where(t => Helpers.SearchText.Matches(t.TripNo, text) || Helpers.SearchText.Matches(t.Plate, text))
            .Take(20)
            .Select(t => $"{t.TripNo}  |  {t.Plate}")
            .ToArray();

        if (matched.Length == 0)
        {
            _lstPopup.Visible = false;
            return;
        }

        _lstPopup.Items.Clear();
        _lstPopup.Items.AddRange(matched);

        // Vị trí popup ngay dưới TextBox
        var screenPos = _txtSearch.PointToScreen(new Point(0, _txtSearch.Height));
        if (_lstPopup.Parent is not null)
        {
            var formPos = _lstPopup.Parent.PointToClient(screenPos);
            _lstPopup.Location = formPos;
        }
        _lstPopup.Width = _txtSearch.Width;
        _lstPopup.Height = Math.Min(matched.Length * _lstPopup.ItemHeight + 4, 200);
        _lstPopup.BringToFront();
        _lstPopup.Visible = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Down && _lstPopup.Visible && _lstPopup.Items.Count > 0)
        {
            _lstPopup.Focus();
            _lstPopup.SelectedIndex = 0;
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Enter)
        {
            if (_lstPopup.Visible && _lstPopup.Items.Count > 0)
                SelectFromPopup(_lstPopup.Items[0]?.ToString());
            else
                RaiseSelected(_txtSearch.Text.Trim());
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            _lstPopup.Visible = false;
            e.Handled = true;
        }
    }

    private async void OnLeave(object? sender, EventArgs e)
    {
        await Task.Delay(150);
        if (!_lstPopup.Focused)
            _lstPopup.Visible = false;
    }

    private void OnPopupClick(object? sender, EventArgs e)
    {
        if (_lstPopup.SelectedItem is string item)
            SelectFromPopup(item);
    }

    private void OnPopupKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && _lstPopup.SelectedItem is string item)
        {
            SelectFromPopup(item);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            _lstPopup.Visible = false;
            _txtSearch.Focus();
            e.Handled = true;
        }
    }

    private void SelectFromPopup(string? displayText)
    {
        if (string.IsNullOrWhiteSpace(displayText)) return;
        _lstPopup.Visible = false;

        // Parse "TripNo  |  Plate"
        var parts = displayText.Split('|', 2);
        var tripNo = parts[0].Trim();
        var plate = parts.Length > 1 ? parts[1].Trim() : "";

        _txtSearch.Text = tripNo;
        _txtSearch.SelectionStart = tripNo.Length;

        var trip = _trips.FirstOrDefault(t => t.TripNo == tripNo);
        if (trip is not null)
            TripSelected?.Invoke(this, new TripSearchSelectedEventArgs(trip));
        else
            RaiseSelected(tripNo);
    }

    private void RaiseSelected(string searchText)
    {
        // Tìm exact match theo TripNo hoặc Plate
        var trip = _trips.FirstOrDefault(t =>
            string.Equals(t.TripNo, searchText, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.Plate?.Replace(" ", "").Replace("-", "").Replace(".", ""),
                searchText.Replace(" ", "").Replace("-", "").Replace(".", ""),
                StringComparison.OrdinalIgnoreCase));

        if (trip is not null)
            TripSelected?.Invoke(this, new TripSearchSelectedEventArgs(trip));
    }
}

/// <summary>Item trong danh sách chuyến cho autocomplete.</summary>
public sealed record TripSearchItem(
    Guid TripId,
    string TripNo,
    string? Plate,
    string? CustomerOrOwner,
    string? Commodity);

/// <summary>Event khi user chọn chuyến.</summary>
public sealed class TripSearchSelectedEventArgs(TripSearchItem trip) : EventArgs
{
    public TripSearchItem Trip { get; } = trip;
}
