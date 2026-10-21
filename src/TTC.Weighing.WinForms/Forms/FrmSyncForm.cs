using TTC.Weighing.Infrastructure.Adapters;
using TTC.Weighing.WinForms.Controls;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Forms;

/// <summary>
/// Form kỹ thuật — quản lý dữ liệu FRM API cache.
/// Hiện trạng thái cache, nút refresh thủ công, thống kê entities đã kéo.
/// </summary>
public sealed class FrmSyncForm : Form
{
    private readonly FrmMasterDataCache _cache;

    private readonly Label _lblStatus = new()
    {
        Dock = DockStyle.Top,
        Height = 60,
        Font = AgrisTheme.Body(11F),
        ForeColor = AgrisTheme.TextPrimary,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(12)
    };

    private readonly Button _btnRefresh = GridBuilder.PrimaryButton("🔄 Kéo dữ liệu FRM mới nhất", new Size(280, 40));
    private readonly Button _btnAutoSync = GridBuilder.SecondaryButton("▶ Bật tự động", new Size(160, 40));
    private readonly NumericUpDown _numInterval = new()
    {
        Minimum = 1, Maximum = 120, Value = 5, Width = 60,
        Font = AgrisTheme.Body(10F), AccessibleName = "Chu kỳ (phút)"
    };
    private readonly System.Windows.Forms.Timer _autoTimer = new();
    private bool _autoEnabled;
    private readonly TextBox _txtEntities = new()
    {
        Font = AgrisTheme.Mono(9F),
        Text = "brix,tram,thua-dat,xe-van-chuyen,vu-dau-tu,giong-mia,vung-dia-ly,hop-dong-van-chuyen,the-xe,LenhDon",
        AccessibleName = "Danh sách entities (phân cách bằng dấu phẩy)"
    };
    private readonly TextBox _txtBUs = new()
    {
        Font = AgrisTheme.Mono(9F),
        Text = Program.ActiveOrganizationUnitCode ?? "TTCS",
        ReadOnly = true,
        BackColor = AgrisTheme.ReadOnlyBg,
        ForeColor = AgrisTheme.ReadOnlyText,
        AccessibleName = "Danh sách BU (phân cách bằng dấu phẩy)"
    };
    private readonly RichTextBox _rtbLog = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        Font = AgrisTheme.Mono(9F),
        BackColor = AgrisTheme.SurfaceMuted,
        BorderStyle = BorderStyle.None
    };

    private readonly DataGridView _dgvStats = GridBuilder.ReadOnlyGrid("Thống kê entities");

    // ── Grid dữ liệu LenhDon (mới nhất trước, phân trang, tìm kiếm) ──
    private readonly DataGridView _dgvData = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        ReadOnly = true,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
        RowHeadersVisible = false,
        BackgroundColor = AgrisTheme.Surface,
        Font = AgrisTheme.Mono(8.5F)
    };
    private readonly TextBox _txtSearch = new() { PlaceholderText = "Tìm kiếm nhanh...", Width = 250, Font = AgrisTheme.Body(9F) };
    private readonly ComboBox _cboPageSize = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
    private readonly Label _lblPageInfo = new() { AutoSize = true, Font = AgrisTheme.Body(9F), Margin = new Padding(8, 6, 8, 0) };
    private readonly Button _btnPrevPage = new() { Text = "< Trước", Width = 70, Height = 26 };
    private readonly Button _btnNextPage = new() { Text = "Sau >", Width = 70, Height = 26 };
    private List<FrmLenhDonInfo> _allData = [];
    private List<FrmLenhDonInfo> _filteredData = [];
    private int _currentPage = 1;
    private int _pageSize = 10;
    private readonly Dictionary<int, HashSet<string>> _columnFilters = new();

    public FrmSyncForm(FrmMasterDataCache cache)
    {
        _cache = cache;

        Text = "Quản lý dữ liệu FRM API";
        MinimumSize = new Size(700, 500);
        BackColor = AgrisTheme.Canvas;
        DoubleBuffered = true;

        BuildLayout();
        UpdateStatus();

        // Load LenhDon từ cache (nếu đã có từ DB)
        _allData = _cache.AllLenhDon.OrderByDescending(ld => ld.NgayCap).ToList();

        // BU đã filter từ DB/API — không cần auto-filter cột BU trên grid

        ApplyFilter();

        _btnRefresh.Click += async (_, _) => await RefreshAsync();
        _autoTimer.Tick += async (_, _) => await RefreshAsync();

        // Reload data khi form load (có thể cache đã update từ background)
        Load += (_, _) => ReloadFromCache();
    }

    /// <summary>Reload dữ liệu từ cache vào grid (gọi khi form load hoặc cache đã refresh).</summary>
    private void ReloadFromCache()
    {
        _allData = _cache.AllLenhDon.OrderByDescending(ld => ld.NgayCap).ToList();
        ApplyFilter();
        UpdateStatus();
    }

    private void ToggleAutoSync()
    {
        _autoEnabled = !_autoEnabled;
        if (_autoEnabled)
        {
            _autoTimer.Interval = (int)_numInterval.Value * 60_000;
            _autoTimer.Start();
            _btnAutoSync.Text = "⏸ Tắt tự động";
            _btnAutoSync.BackColor = AgrisTheme.Warning;
            _numInterval.Enabled = false;
            AppendLog($"🔄 Tự động kéo dữ liệu mỗi {_numInterval.Value} phút.");
        }
        else
        {
            _autoTimer.Stop();
            _btnAutoSync.Text = "▶ Bật tự động";
            _btnAutoSync.BackColor = AgrisTheme.Surface;
            _numInterval.Enabled = true;
            AppendLog("⏹ Đã tắt tự động kéo dữ liệu.");
        }
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(12)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));   // Status
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));   // Entities config
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));   // Button
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 65));    // Data grid
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 35));    // Log

        root.Controls.Add(_lblStatus, 0, 0);

        // Config: Entities + BU (editable)
        var configPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = Padding.Empty
        };
        configPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        configPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        configPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        configPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        configPanel.Controls.Add(new Label { Text = "Entities:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = AgrisTheme.Body(9F) }, 0, 0);
        _txtEntities.Dock = DockStyle.Fill;
        configPanel.Controls.Add(_txtEntities, 1, 0);

        configPanel.Controls.Add(new Label { Text = "BU:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = AgrisTheme.Body(9F) }, 0, 1);
        _txtBUs.Dock = DockStyle.Fill;
        configPanel.Controls.Add(_txtBUs, 1, 1);

        root.Controls.Add(configPanel, 0, 1);

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        btnPanel.Controls.Add(_btnRefresh);
        btnPanel.Controls.Add(new Label { Text = "  ", AutoSize = true, Margin = new Padding(4, 12, 0, 0) });
        btnPanel.Controls.Add(_btnAutoSync);
        btnPanel.Controls.Add(new Label { Text = "mỗi", AutoSize = true, Margin = new Padding(8, 12, 4, 0), Font = AgrisTheme.Body(9F) });
        btnPanel.Controls.Add(_numInterval);
        btnPanel.Controls.Add(new Label { Text = "phút", AutoSize = true, Margin = new Padding(4, 12, 0, 0), Font = AgrisTheme.Body(9F) });
        root.Controls.Add(btnPanel, 0, 2);

        // Data grid: LenhDon
        var dataCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(1) };
        var buLabel = _cache.ActiveBusinessUnit ?? "FRM";
        var dataHeader = GridBuilder.SectionHeader($"Dữ liệu Lệnh đốn {buLabel}");

        var searchBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 32, FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(4, 2, 4, 2)
        };
        searchBar.Controls.Add(new Label { Text = "Hiển thị", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        _cboPageSize.Items.AddRange(new object[] { "10", "20", "50", "100" });
        _cboPageSize.SelectedIndex = 0;
        _cboPageSize.SelectedIndexChanged += (_, _) =>
        {
            _pageSize = int.Parse(_cboPageSize.Text);
            _currentPage = 1;
            RefreshDataGrid();
        };
        searchBar.Controls.Add(_cboPageSize);
        searchBar.Controls.Add(new Label { Text = "dòng/trang", AutoSize = true, Margin = new Padding(4, 6, 12, 0) });
        searchBar.Controls.Add(_txtSearch);
        _txtSearch.TextChanged += (_, _) => { _currentPage = 1; ApplyFilter(); };
        searchBar.Controls.Add(_lblPageInfo);
        _btnPrevPage.Click += (_, _) => { if (_currentPage > 1) { _currentPage--; RefreshDataGrid(); } };
        _btnNextPage.Click += (_, _) => { var maxPage = (_filteredData.Count + _pageSize - 1) / _pageSize; if (_currentPage < maxPage) { _currentPage++; RefreshDataGrid(); } };
        searchBar.Controls.Add(_btnPrevPage);
        searchBar.Controls.Add(_btnNextPage);

        _dgvData.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "Mã lệnh đốn ▼", Width = 150, SortMode = DataGridViewColumnSortMode.Programmatic },
            new DataGridViewTextBoxColumn { HeaderText = "BU ▼", Width = 70, SortMode = DataGridViewColumnSortMode.Programmatic },
            new DataGridViewTextBoxColumn { HeaderText = "Biển số xe ▼", Width = 110, SortMode = DataGridViewColumnSortMode.Programmatic },
            new DataGridViewTextBoxColumn { HeaderText = "Chủ mía ▼", Width = 180, SortMode = DataGridViewColumnSortMode.Programmatic },
            new DataGridViewTextBoxColumn { HeaderText = "Loại mía ▼", Width = 90, SortMode = DataGridViewColumnSortMode.Programmatic },
            new DataGridViewTextBoxColumn { HeaderText = "Giống mía ▼", Width = 90, SortMode = DataGridViewColumnSortMode.Programmatic },
            new DataGridViewTextBoxColumn { HeaderText = "Trạm ▼", Width = 130, SortMode = DataGridViewColumnSortMode.Programmatic },
            new DataGridViewTextBoxColumn { HeaderText = "Vụ đầu tư ▼", Width = 120, SortMode = DataGridViewColumnSortMode.Programmatic },
            new DataGridViewTextBoxColumn { HeaderText = "Loại xe ▼", Width = 80, SortMode = DataGridViewColumnSortMode.Programmatic },
            new DataGridViewTextBoxColumn { HeaderText = "Ưu tiên ▼", Width = 70, SortMode = DataGridViewColumnSortMode.Programmatic },
            new DataGridViewTextBoxColumn { HeaderText = "Trạng thái ▼", Width = 85, SortMode = DataGridViewColumnSortMode.Programmatic },
            new DataGridViewTextBoxColumn { HeaderText = "Ngày cấp ▼", Width = 140, SortMode = DataGridViewColumnSortMode.Programmatic });

        _dgvData.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _dgvData.ColumnHeaderMouseClick += OnDataGridHeaderClick;

        _dgvData.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(245, 250, 245);
        _dgvData.DefaultCellStyle.Padding = new Padding(4, 2, 4, 2);
        _dgvData.ColumnHeadersDefaultCellStyle.BackColor = AgrisTheme.BrandGreen;
        _dgvData.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        _dgvData.ColumnHeadersDefaultCellStyle.Font = new Font(AgrisTheme.FontFamily, 9F, FontStyle.Bold);
        _dgvData.EnableHeadersVisualStyles = false;
        _dgvData.ColumnHeadersHeight = 32;

        dataCard.Controls.Add(_dgvData);
        dataCard.Controls.Add(searchBar);
        dataCard.Controls.Add(dataHeader);
        root.Controls.Add(dataCard, 0, 3);

        // Log
        var logCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(1) };
        var logHeader = GridBuilder.SectionHeader("Nhật ký đồng bộ");
        logCard.Controls.Add(_rtbLog);
        logCard.Controls.Add(logHeader);
        root.Controls.Add(logCard, 0, 4);

        _btnAutoSync.Click += (_, _) => ToggleAutoSync();

        Controls.Add(root);
    }

    private void UpdateStatus()
    {
        var loading = _cache.IsLoading ? "⏳ Đang tải..." : "✅ Sẵn sàng";
        var refreshed = _cache.LastRefreshedAt == DateTimeOffset.MinValue
            ? "Chưa tải lần nào"
            : $"Lần cuối: {_cache.LastRefreshedAt.ToLocalTime():dd/MM/yyyy HH:mm:ss}";

        var allVehicles = _cache.AllVehicles;
        var withCard = allVehicles.Count(v => v.MaThe is not null);
        var uniquePlates = allVehicles.Select(v => v.BienSo).Distinct().Count();

        _lblStatus.Text = $"{loading}  |  Xe: {_cache.VehicleCount} ({uniquePlates} biển số, {withCard} có thẻ)  |  Lệnh đốn: {_cache.AllLenhDon.Count}  |  {refreshed}";
    }

    private async Task RefreshAsync()
    {
        _btnRefresh.Enabled = false;
        _btnRefresh.Text = "⏳ Đang kéo dữ liệu...";
        AppendLog("Bắt đầu kéo dữ liệu FRM API...");

        try
        {
            var before = _cache.VehicleCount;
            await _cache.RefreshAsync();
            var after = _cache.VehicleCount;

            // Load LenhDon data vào grid
            _allData = _cache.AllLenhDon.OrderByDescending(ld => ld.NgayCap).ToList();
            _currentPage = 1;
            ApplyFilter();

            AppendLog($"✅ Cache xe: Trước: {before}, Sau: {after} records. LenhDon: {_allData.Count}.");

            // Kiểm tra từng entity + từng BU (đọc từ textbox — có thể chỉnh sửa trên giao diện)
            var entities = _txtEntities.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var bus = _txtBUs.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var bu in bus)
            {
                AppendLog($"── BU: {bu} ──");
                foreach (var ent in entities)
                {
                    try
                    {
                        var (count, errorDetail) = await _cache.FetchEntityCountDetailAsync(ent, bu);
                        if (count >= 0)
                        {
                            var icon = count > 0 ? "✅" : "❌";
                            AppendLog($"  {icon} {ent}: {count}");
                        }
                        else
                        {
                            AppendLog($"  ❌ {ent}: FAILED — {errorDetail}");
                        }
                    }
                    catch (Exception ex) { AppendLog($"  ⚠ {ent}: lỗi — {ex.Message}"); }
                }
            }

            UpdateStatus();
        }
        catch (Exception ex)
        {
            AppendLog($"❌ Lỗi: {ex.Message}");
        }
        finally
        {
            _btnRefresh.Text = "🔄 Kéo dữ liệu FRM mới nhất";
            _btnRefresh.Enabled = true;
        }
    }

    private void OnDataGridHeaderClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.ColumnIndex < 0) return;

        // Collect unique values for this column from ALL data (not just filtered)
        var colIdx = e.ColumnIndex;
        var values = _allData
            .Select(ld => GetFieldByIndex(ld, colIdx))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct()
            .OrderBy(v => v)
            .ToArray()!;

        _columnFilters.TryGetValue(colIdx, out var currentFilter);

        // Position popup under the header
        var headerRect = _dgvData.GetCellDisplayRectangle(colIdx, -1, true);
        var screenPos = _dgvData.PointToScreen(new Point(headerRect.Left, headerRect.Bottom));

        using var popup = new ColumnFilterPopup(values, currentFilter);

        // Đảm bảo popup không vượt ra ngoài màn hình
        var screen = Screen.FromPoint(screenPos).WorkingArea;
        var popupX = screenPos.X;
        var popupY = screenPos.Y;
        if (popupX + popup.Width > screen.Right)
            popupX = screen.Right - popup.Width;
        if (popupY + popup.Height > screen.Bottom)
            popupY = screenPos.Y - popup.Height - headerRect.Height;
        if (popupX < screen.Left) popupX = screen.Left;
        if (popupY < screen.Top) popupY = screen.Top;

        popup.Location = new Point(popupX, popupY);

        if (popup.ShowDialog(this) != DialogResult.OK) return;

        // Apply sort
        if (popup.SortAscending.HasValue)
        {
            _allData = popup.SortAscending.Value
                ? _allData.OrderBy(ld => GetFieldByIndex(ld, colIdx)).ToList()
                : _allData.OrderByDescending(ld => GetFieldByIndex(ld, colIdx)).ToList();
        }

        // Apply column filter
        if (popup.SelectedValues is null)
            _columnFilters.Remove(colIdx);
        else
            _columnFilters[colIdx] = popup.SelectedValues;

        _currentPage = 1;
        ApplyFilter();
    }

    private static string? GetFieldByIndex(FrmLenhDonInfo ld, int idx) => idx switch
    {
        0 => ld.MaLD,
        1 => ld.BusinessUnit,
        2 => ld.BienSoXe,
        3 => ld.KHName,
        4 => ld.LoaiMiaName,
        5 => ld.GiongMiaName,
        6 => ld.TramName,
        7 => ld.VudautuName,
        8 => ld.LoaixeName,
        9 => ld.UuTien,
        10 => ld.StatusCodeName,
        11 => ld.NgayCap,
        _ => null
    };

    private void ApplyFilter()
    {
        var search = _txtSearch.Text.Trim().ToUpperInvariant();

        // Text search filter
        _filteredData = string.IsNullOrWhiteSpace(search)
            ? _allData.ToList()
            : _allData.Where(ld =>
                (ld.MaLD?.ToUpperInvariant().Contains(search) == true) ||
                (ld.BienSoXe?.ToUpperInvariant().Contains(search) == true) ||
                (ld.KHName?.ToUpperInvariant().Contains(search) == true) ||
                (ld.LoaiMiaName?.ToUpperInvariant().Contains(search) == true) ||
                (ld.TramName?.ToUpperInvariant().Contains(search) == true) ||
                (ld.GiongMiaName?.ToUpperInvariant().Contains(search) == true) ||
                (ld.BusinessUnit?.ToUpperInvariant().Contains(search) == true) ||
                (ld.StatusCodeName?.ToUpperInvariant().Contains(search) == true))
            .ToList();

        // Column filters (checkbox)
        foreach (var (colIdx, allowedValues) in _columnFilters)
        {
            _filteredData = _filteredData
                .Where(ld => allowedValues.Contains(GetFieldByIndex(ld, colIdx) ?? ""))
                .ToList();
        }

        // Visual: highlight headers that have active filters
        UpdateHeaderFilterIndicators();

        RefreshDataGrid();
    }

    /// <summary>
    /// Column header names — base names without indicators.
    /// </summary>
    private static readonly string[] BaseHeaders =
    [
        "Mã lệnh đốn", "BU", "Biển số xe", "Chủ mía", "Loại mía",
        "Giống mía", "Trạm", "Vụ đầu tư", "Loại xe", "Ưu tiên", "Trạng thái", "Ngày cấp"
    ];

    /// <summary>
    /// Cập nhật indicator trên header cột: ▼ mặc định, 🔽 khi có filter active.
    /// </summary>
    private void UpdateHeaderFilterIndicators()
    {
        for (var i = 0; i < _dgvData.Columns.Count && i < BaseHeaders.Length; i++)
        {
            var hasFilter = _columnFilters.ContainsKey(i);
            _dgvData.Columns[i].HeaderText = hasFilter
                ? $"{BaseHeaders[i]} 🔽"
                : $"{BaseHeaders[i]} ▼";

            // Highlight header background khi có filter
            if (hasFilter)
            {
                _dgvData.Columns[i].HeaderCell.Style.BackColor = Color.FromArgb(255, 235, 156); // vàng nhạt
                _dgvData.Columns[i].HeaderCell.Style.ForeColor = AgrisTheme.TextPrimary;
            }
            else
            {
                _dgvData.Columns[i].HeaderCell.Style.BackColor = AgrisTheme.BrandGreen;
                _dgvData.Columns[i].HeaderCell.Style.ForeColor = Color.White;
            }
        }
    }

    private void RefreshDataGrid()
    {
        var totalPages = Math.Max(1, (_filteredData.Count + _pageSize - 1) / _pageSize);
        if (_currentPage > totalPages) _currentPage = totalPages;

        var pageData = _filteredData
            .Skip((_currentPage - 1) * _pageSize)
            .Take(_pageSize)
            .ToList();

        _dgvData.SuspendLayout();
        _dgvData.Rows.Clear();
        foreach (var ld in pageData)
        {
            _dgvData.Rows.Add(
                ld.MaLD, ld.BusinessUnit, ld.BienSoXe, ld.KHName, ld.LoaiMiaName, ld.GiongMiaName,
                ld.TramName, ld.VudautuName, ld.LoaixeName, ld.UuTien, ld.StatusCodeName, ld.NgayCap);
        }
        _dgvData.ResumeLayout();

        _lblPageInfo.Text = $"{(_currentPage - 1) * _pageSize + 1}-{Math.Min(_currentPage * _pageSize, _filteredData.Count)} / {_filteredData.Count} (Trang {_currentPage}/{totalPages})";
        _btnPrevPage.Enabled = _currentPage > 1;
        _btnNextPage.Enabled = _currentPage < totalPages;
    }

    private void AppendLog(string message)
    {
        if (_rtbLog.InvokeRequired)
        {
            _rtbLog.Invoke(() => AppendLog(message));
            return;
        }
        _rtbLog.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
        _rtbLog.ScrollToCaret();
    }
}
