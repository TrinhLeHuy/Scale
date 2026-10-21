using TTC.Weighing.Infrastructure.Adapters;
using TTC.Weighing.WinForms.Controls;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Forms;

/// <summary>
/// Form kỹ thuật — quản lý dữ liệu ERP API (order-lines).
/// Giống FrmSyncForm: config BU/khung ngày, nút kéo + tự động,
/// grid data với phân trang, tìm kiếm nhanh, column filter popup kiểu Excel.
/// Auto-load từ DB khi mở form.
/// </summary>
public sealed class ErpSyncForm : Form
{
    private readonly ErpOrderLineAdapter _adapter;

    private readonly Label _lblStatus = new()
    {
        Dock = DockStyle.Top, Height = 60,
        Font = AgrisTheme.Body(11F), ForeColor = AgrisTheme.TextPrimary,
        TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12)
    };

    private readonly Button _btnRefresh = GridBuilder.PrimaryButton("🔄 Kéo dữ liệu ERP mới nhất", new Size(280, 40));
    private readonly Button _btnAutoSync = GridBuilder.SecondaryButton("▶ Bật tự động", new Size(160, 40));
    private readonly NumericUpDown _numInterval = new()
    {
        Minimum = 1, Maximum = 120, Value = 10, Width = 60,
        Font = AgrisTheme.Body(10F), AccessibleName = "Chu kỳ (phút)"
    };
    private readonly System.Windows.Forms.Timer _autoTimer = new();
    private bool _autoEnabled;

    private readonly TextBox _txtBU = new()
    {
        Font = AgrisTheme.Mono(9F),
        Text = Program.ActiveOrganizationUnitCode ?? "TTCS",
        ReadOnly = true,
        BackColor = AgrisTheme.ReadOnlyBg,
        ForeColor = AgrisTheme.ReadOnlyText,
        AccessibleName = "Business Unit"
    };
    private readonly ComboBox _cboTxnType = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList, Width = 320, DropDownWidth = 380,
        Font = AgrisTheme.Body(9F), AccessibleName = "Loại GD"
    };
    private readonly DateTimePicker _dtpFrom = new()
    {
        Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 130,
        AccessibleName = "Từ ngày"
    };
    private readonly DateTimePicker _dtpTo = new()
    {
        Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 130,
        AccessibleName = "Đến ngày"
    };

    private readonly RichTextBox _rtbLog = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true,
        Font = AgrisTheme.Mono(9F), BackColor = AgrisTheme.SurfaceMuted,
        BorderStyle = BorderStyle.None
    };

    // ── Grid dữ liệu ERP (mới nhất trước, phân trang, tìm kiếm) ──
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

    private List<ErpOrderLine> _allData = [];
    private List<ErpOrderLine> _filteredData = [];
    private int _currentPage = 1;
    private int _pageSize = 10;
    private readonly Dictionary<int, HashSet<string>> _columnFilters = new();
    private readonly string _activeBu = Program.ActiveOrganizationUnitCode ?? "TTCS";

    /// <summary>Chỉ giữ lại đơn hàng thuộc BU của tài khoản đang đăng nhập.</summary>
    private List<ErpOrderLine> FilterByActiveBu(List<ErpOrderLine> items)
    {
        // API trả BU dạng "KSK BU", login trả "KSK" — normalize: bỏ suffix " BU", bỏ spaces, so sánh case-insensitive
        static string NormalizeBu(string? bu)
        {
            if (string.IsNullOrWhiteSpace(bu)) return "";
            var s = bu.Trim();
            if (s.EndsWith(" BU", StringComparison.OrdinalIgnoreCase))
                s = s[..^3].Trim();
            return s.Replace(" ", "").ToUpperInvariant();
        }

        var normalizedActive = NormalizeBu(_activeBu);
        return items
            .Where(i => NormalizeBu(i.BusinessUnit) == normalizedActive)
            .ToList();
    }

    public ErpSyncForm(ErpOrderLineAdapter adapter)
    {
        _adapter = adapter;
        Text = "Quản lý dữ liệu ERP API";
        MinimumSize = new Size(900, 550);
        BackColor = AgrisTheme.Canvas;
        DoubleBuffered = true;

        _dtpFrom.Value = DateTime.Today.AddDays(-7);
        _dtpTo.Value = DateTime.Today;

        // Loại GD filter
        _cboTxnType.Items.AddRange(new object[]
        {
            "(Tất cả)",
            "01 - Purchasing Order for Receiving",
            "02 - InterOrg Intransit Shipped",
            "03 - InterOrg Intransit for Receiving",
            "04 - Transfer Order for Ship Confirm",
            "05 - Transfer Order for Receiving",
            "06 - Sales Order for Ship Confirm"
        });
        _cboTxnType.SelectedIndex = 0;

        BuildLayout();

        _btnRefresh.Click += async (_, _) => await RefreshAsync();
        _autoTimer.Tick += async (_, _) => await RefreshAsync();

        // Auto-load từ DB khi mở form
        Load += async (_, _) => await LoadFromDbAsync();
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
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));   // Status
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));   // Config (BU + khung ngày)
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));   // Buttons
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 65));    // Data grid
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 35));    // Log

        root.Controls.Add(_lblStatus, 0, 0);

        // Config: BU + khung ngày
        var configPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 4, 0, 0)
        };
        configPanel.Controls.Add(new Label { Text = "BU:", AutoSize = true, Margin = new Padding(0, 8, 4, 0), Font = AgrisTheme.Body(9F) });
        _txtBU.Width = 100;
        configPanel.Controls.Add(_txtBU);
        configPanel.Controls.Add(new Label { Text = "Từ ngày:", AutoSize = true, Margin = new Padding(12, 8, 4, 0), Font = AgrisTheme.Body(9F) });
        configPanel.Controls.Add(_dtpFrom);
        configPanel.Controls.Add(new Label { Text = "→", AutoSize = true, Margin = new Padding(4, 8, 4, 0), Font = AgrisTheme.Body(9F) });
        configPanel.Controls.Add(_dtpTo);
        configPanel.Controls.Add(new Label { Text = "Loại GD:", AutoSize = true, Margin = new Padding(12, 8, 4, 0), Font = AgrisTheme.Body(9F) });
        configPanel.Controls.Add(_cboTxnType);
        root.Controls.Add(configPanel, 0, 1);

        // Buttons
        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        btnPanel.Controls.Add(_btnRefresh);
        btnPanel.Controls.Add(new Label { Text = "  ", AutoSize = true, Margin = new Padding(4, 12, 0, 0) });
        btnPanel.Controls.Add(_btnAutoSync);
        btnPanel.Controls.Add(new Label { Text = "mỗi", AutoSize = true, Margin = new Padding(8, 12, 4, 0), Font = AgrisTheme.Body(9F) });
        btnPanel.Controls.Add(_numInterval);
        btnPanel.Controls.Add(new Label { Text = "phút", AutoSize = true, Margin = new Padding(4, 12, 0, 0), Font = AgrisTheme.Body(9F) });
        _btnAutoSync.Click += (_, _) => ToggleAutoSync();
        root.Controls.Add(btnPanel, 0, 2);

        // Data grid card
        var dataCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(1) };
        var dataHeader = GridBuilder.SectionHeader("Dữ liệu đơn hàng ERP (Order Lines)");

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
        _btnNextPage.Click += (_, _) =>
        {
            var maxPage = Math.Max(1, (_filteredData.Count + _pageSize - 1) / _pageSize);
            if (_currentPage < maxPage) { _currentPage++; RefreshDataGrid(); }
        };
        searchBar.Controls.Add(_btnPrevPage);
        searchBar.Controls.Add(_btnNextPage);

        _dgvData.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "Shipment ▼", SortMode = DataGridViewColumnSortMode.Programmatic },
            new DataGridViewTextBoxColumn { HeaderText = "Số tham chiếu ▼", SortMode = DataGridViewColumnSortMode.Programmatic },
            new DataGridViewTextBoxColumn { HeaderText = "Trip ▼", SortMode = DataGridViewColumnSortMode.Programmatic },
            new DataGridViewTextBoxColumn { HeaderText = "Biển số ▼", SortMode = DataGridViewColumnSortMode.Programmatic },
            new DataGridViewTextBoxColumn { HeaderText = "Loại GD ▼", SortMode = DataGridViewColumnSortMode.Programmatic },
            new DataGridViewTextBoxColumn { HeaderText = "Action ▼", SortMode = DataGridViewColumnSortMode.Programmatic },
            new DataGridViewTextBoxColumn { HeaderText = "Mã hàng ▼", SortMode = DataGridViewColumnSortMode.Programmatic },
            new DataGridViewTextBoxColumn { HeaderText = "Tên hàng ▼", SortMode = DataGridViewColumnSortMode.Programmatic },
            new DataGridViewTextBoxColumn { HeaderText = "Org Code ▼", SortMode = DataGridViewColumnSortMode.Programmatic },
            new DataGridViewTextBoxColumn { HeaderText = "NCC / KH ▼", SortMode = DataGridViewColumnSortMode.Programmatic },
            new DataGridViewTextBoxColumn { HeaderText = "Vận chuyển ▼", SortMode = DataGridViewColumnSortMode.Programmatic },
            new DataGridViewTextBoxColumn { HeaderText = "SL tổng ▼", SortMode = DataGridViewColumnSortMode.Programmatic },
            new DataGridViewTextBoxColumn { HeaderText = "SL còn lại ▼", SortMode = DataGridViewColumnSortMode.Programmatic },
            new DataGridViewTextBoxColumn { HeaderText = "Ngày GD ▼", SortMode = DataGridViewColumnSortMode.Programmatic },
            new DataGridViewTextBoxColumn { HeaderText = "BU ▼", SortMode = DataGridViewColumnSortMode.Programmatic },
            new DataGridViewTextBoxColumn { HeaderText = "Nguồn ▼", SortMode = DataGridViewColumnSortMode.Programmatic });

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

        Controls.Add(root);
        _lblStatus.Text = "Đang tải dữ liệu từ DB...";
    }

    // ═══════════════════════════════════════════════════════════
    //  Data load + refresh
    // ═══════════════════════════════════════════════════════════

    /// <summary>Load dữ liệu ERP đã lưu trong DB local khi mở form.</summary>
    private async Task LoadFromDbAsync()
    {
        try
        {
            var items = await _adapter.LoadFromDbAsync();
            _allData = FilterByActiveBu(items);
            ApplyFilter();

            if (_allData.Count > 0)
            {
                _lblStatus.Text = $"✅ Sẵn sàng  |  Đơn hàng: {_allData.Count}  |  BU: {_activeBu}  |  Tải từ DB local";
                AppendLog($"✅ Tải {_allData.Count} đơn hàng từ DB local (BU: {_activeBu}).");
            }
            else
            {
                _lblStatus.Text = $"Chưa có dữ liệu ERP cho BU {_activeBu}. Bấm 'Kéo dữ liệu ERP mới nhất' để kéo từ API.";
            }
        }
        catch (Exception ex)
        {
            AppendLog($"⚠ Lỗi tải từ DB: {ex.Message}");
            _lblStatus.Text = "⚠ Lỗi tải từ DB.";
        }
    }

    private static readonly string[] ALL_TXN_TYPES = ["01", "02", "03", "04", "05", "06"];

    private async Task RefreshAsync()
    {
        _btnRefresh.Enabled = false;
        _btnRefresh.Text = "⏳ Đang kéo dữ liệu...";
        var inputBu = _txtBU.Text.Trim();
        DateTime? fromDate = _dtpFrom.Value.Date;
        DateTime? toDate = _dtpTo.Value.Date.AddDays(1);

        // Parse transaction type code từ combo: "01 - ..." → "01", "(Tất cả)" → null → kéo tất cả types
        string? txnTypeCode = null;
        var txnSel = _cboTxnType.SelectedItem?.ToString();
        if (txnSel is not null && txnSel.Length >= 2 && char.IsDigit(txnSel[0]))
            txnTypeCode = txnSel[..2];

        AppendLog($"Bắt đầu kéo ERP order-lines: BU={inputBu}, {_dtpFrom.Value:dd/MM/yyyy} → {_dtpTo.Value:dd/MM/yyyy}" +
            (txnTypeCode is not null ? $", Loại GD={txnTypeCode}" : ", Tất cả loại GD"));

        try
        {
            var allItems = new List<ErpOrderLine>();
            var seen = new HashSet<string>(); // track shipment+bu để tránh trùng
            var typesToFetch = txnTypeCode is not null ? new[] { txnTypeCode } : ALL_TXN_TYPES;

            // Lần 1: kéo theo khung ngày
            foreach (var typeCode in typesToFetch)
            {
                try
                {
                    var items = await _adapter.GetAllOrderLinesAsync(inputBu, fromDate, toDate, typeCode);
                    if (items.Count > 0)
                    {
                        AppendLog($"  Type {typeCode} (theo ngày): {items.Count} đơn hàng");
                        foreach (var item in items)
                        {
                            var key = $"{item.ShipmentNumber}|{item.BusinessUnit}";
                            if (seen.Add(key)) allItems.Add(item);
                        }
                    }
                }
                catch (Exception ex) { AppendLog($"  ⚠ Type {typeCode}: {ex.Message}"); }
            }

            // Lần 2: kéo KHÔNG filter ngày (để lấy PO/data chưa có transactionDate)
            foreach (var typeCode in typesToFetch)
            {
                try
                {
                    var items = await _adapter.GetAllOrderLinesAsync(inputBu, null, null, typeCode);
                    var newCount = 0;
                    foreach (var item in items)
                    {
                        var key = $"{item.ShipmentNumber}|{item.BusinessUnit}";
                        if (seen.Add(key)) { allItems.Add(item); newCount++; }
                    }
                    if (newCount > 0)
                        AppendLog($"  Type {typeCode} (không ngày): +{newCount} đơn hàng mới");
                }
                catch (Exception ex) { AppendLog($"  ⚠ Type {typeCode} (no-date): {ex.Message}"); }
            }

            AppendLog($"  Tổng: {allItems.Count} đơn hàng");

            // Lưu DB 1 lần sau khi gom tất cả BU
            if (allItems.Count > 0)
            {
                await _adapter.SaveToDbAsync(allItems);
                AppendLog($"💾 Đã lưu {allItems.Count} đơn hàng vào DB (UPSERT).");
            }
            else
            {
                AppendLog("⚠ API trả rỗng — giữ nguyên dữ liệu DB cũ.");
            }

            // Reload toàn bộ từ DB (mới nhất trước, kèm cả data cũ chưa bị đè)
            var dbItems = await _adapter.LoadFromDbAsync();
            _allData = FilterByActiveBu(dbItems);
            _currentPage = 1;
            ApplyFilter();

            _lblStatus.Text = $"✅ Sẵn sàng  |  Đơn hàng: {_allData.Count}  |  BU: {_activeBu}  |  {_dtpFrom.Value:dd/MM} → {_dtpTo.Value:dd/MM}  |  Lần cuối: {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            AppendLog($"❌ Lỗi: {ex.Message}");
        }
        finally
        {
            _btnRefresh.Text = "🔄 Kéo dữ liệu ERP mới nhất";
            _btnRefresh.Enabled = true;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Column filter + search + pagination (giống FrmSyncForm)
    // ═══════════════════════════════════════════════════════════

    private void OnDataGridHeaderClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.ColumnIndex < 0) return;

        var colIdx = e.ColumnIndex;
        var values = _allData
            .Select(item => GetFieldByIndex(item, colIdx))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct()
            .OrderBy(v => v)
            .ToArray()!;

        _columnFilters.TryGetValue(colIdx, out var currentFilter);

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

        if (popup.SortAscending.HasValue)
        {
            _allData = popup.SortAscending.Value
                ? _allData.OrderBy(item => GetFieldByIndex(item, colIdx)).ToList()
                : _allData.OrderByDescending(item => GetFieldByIndex(item, colIdx)).ToList();
        }

        if (popup.SelectedValues is null)
            _columnFilters.Remove(colIdx);
        else
            _columnFilters[colIdx] = popup.SelectedValues;

        _currentPage = 1;
        ApplyFilter();
    }

    private static string? GetFieldByIndex(ErpOrderLine item, int idx) => idx switch
    {
        0 => item.ShipmentNumber,
        1 => item.ReferenceNumber,
        2 => item.TripNumber,
        3 => item.PlateNumber,
        4 => item.TransactionTypeName,
        5 => item.ActionCodeRequest,
        6 => item.ItemNumber ?? item.ItemCode,
        7 => item.ItemDescription ?? item.ItemName,
        8 => item.OrganizationCode,
        9 => item.VendorName ?? item.CustomerName,
        10 => item.Transporter ?? item.DriverName,
        11 => item.TotalQuantity?.ToString("N0"),
        12 => item.RemainingQuantity?.ToString("N0"),
        13 => item.TransactionDate,
        14 => item.BusinessUnit,
        15 => item.Source,
        _ => null
    };

    private void ApplyFilter()
    {
        var search = _txtSearch.Text.Trim().ToUpperInvariant();

        _filteredData = string.IsNullOrWhiteSpace(search)
            ? _allData.ToList()
            : _allData.Where(item =>
                (item.ShipmentNumber?.ToUpperInvariant().Contains(search) == true) ||
                (item.ReferenceNumber?.ToUpperInvariant().Contains(search) == true) ||
                (item.TripNumber?.ToUpperInvariant().Contains(search) == true) ||
                (item.PlateNumber?.ToUpperInvariant().Contains(search) == true) ||
                (item.TransactionTypeName?.ToUpperInvariant().Contains(search) == true) ||
                (item.ActionCodeRequest?.ToUpperInvariant().Contains(search) == true) ||
                (item.ItemName?.ToUpperInvariant().Contains(search) == true) ||
                (item.ItemDescription?.ToUpperInvariant().Contains(search) == true) ||
                (item.CustomerName?.ToUpperInvariant().Contains(search) == true) ||
                (item.VendorName?.ToUpperInvariant().Contains(search) == true) ||
                (item.Transporter?.ToUpperInvariant().Contains(search) == true) ||
                (item.Source?.ToUpperInvariant().Contains(search) == true) ||
                (item.BusinessUnit?.ToUpperInvariant().Contains(search) == true))
            .ToList();

        foreach (var (colIdx, allowedValues) in _columnFilters)
        {
            _filteredData = _filteredData
                .Where(item => allowedValues.Contains(GetFieldByIndex(item, colIdx) ?? ""))
                .ToList();
        }

        UpdateHeaderFilterIndicators();
        RefreshDataGrid();
    }

    private static readonly string[] BaseHeaders =
    [
        "Shipment", "Số tham chiếu", "Trip", "Biển số", "Loại GD", "Action",
        "Mã hàng", "Tên hàng", "Org Code", "NCC / KH", "Vận chuyển",
        "SL tổng", "SL còn lại", "Ngày GD", "BU", "Nguồn"
    ];

    private void UpdateHeaderFilterIndicators()
    {
        for (var i = 0; i < _dgvData.Columns.Count && i < BaseHeaders.Length; i++)
        {
            var hasFilter = _columnFilters.ContainsKey(i);
            _dgvData.Columns[i].HeaderText = hasFilter
                ? $"{BaseHeaders[i]} 🔽"
                : $"{BaseHeaders[i]} ▼";

            if (hasFilter)
            {
                _dgvData.Columns[i].HeaderCell.Style.BackColor = Color.FromArgb(255, 235, 156);
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
        foreach (var item in pageData)
        {
            _dgvData.Rows.Add(
                item.ShipmentNumber, item.ReferenceNumber, item.TripNumber, item.PlateNumber,
                item.TransactionTypeName, item.ActionCodeRequest,
                item.ItemNumber ?? item.ItemCode,
                item.ItemDescription ?? item.ItemName,
                item.OrganizationCode,
                item.VendorName ?? item.CustomerName ?? "",
                item.Transporter ?? item.DriverName ?? "",
                item.TotalQuantity?.ToString("N0"),
                item.RemainingQuantity?.ToString("N0"),
                item.TransactionDate,
                item.BusinessUnit,
                item.Source);
        }
        _dgvData.ResumeLayout();

        _lblPageInfo.Text = _filteredData.Count > 0
            ? $"{(_currentPage - 1) * _pageSize + 1}-{Math.Min(_currentPage * _pageSize, _filteredData.Count)} / {_filteredData.Count} (Trang {_currentPage}/{totalPages})"
            : "0 / 0 (Trang 0/0)";
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
