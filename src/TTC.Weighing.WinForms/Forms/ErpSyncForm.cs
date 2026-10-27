using TTC.Weighing.Infrastructure.Adapters;
using TTC.Weighing.WinForms.Controls;
using TTC.Weighing.WinForms.Localization;
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

    private readonly Button _btnRefresh = GridBuilder.PrimaryButton(Loc.T("erp.btn_pull"), new Size(280, 40));
    private readonly Button _btnAutoSync = GridBuilder.SecondaryButton(Loc.T("erp.btn_auto_on"), new Size(160, 40));
    private readonly NumericUpDown _numInterval = new()
    {
        Minimum = 1, Maximum = 120, Value = 10, Width = 60,
        Font = AgrisTheme.Body(10F), AccessibleName = Loc.T("erp.interval_accessible")
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
        Font = AgrisTheme.Body(9F), AccessibleName = Loc.T("erp.txn_type")
    };
    private readonly DateTimePicker _dtpFrom = new()
    {
        Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 130,
        AccessibleName = Loc.T("erp.date_from")
    };
    private readonly DateTimePicker _dtpTo = new()
    {
        Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 130,
        AccessibleName = Loc.T("erp.date_to")
    };

    // Log hiển thị câu đã dịch (Lào/Khmer) → dùng font UI theo ngôn ngữ, không dùng Consolas (thiếu glyph).
    private readonly RichTextBox _rtbLog = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true,
        Font = AgrisTheme.Body(9F), BackColor = AgrisTheme.SurfaceMuted,
        BorderStyle = BorderStyle.None
    };

    // ── Bảng dữ liệu ERP (mới nhất trước): tìm kiếm, lọc theo cột, phân trang — control dùng chung ──
    private readonly DataExplorerGrid<ErpOrderLine> _explorer = new(
        Loc.T("erp.section_data"), "erp.order_lines", Columns, Loc.T("erp.search_placeholder"), Loc.T("erp.empty"));

    private List<ErpOrderLine> _allData = [];
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
        Text = Loc.T("erp.title");
        MinimumSize = new Size(900, 550);
        BackColor = AgrisTheme.Canvas;
        DoubleBuffered = true;

        _dtpFrom.Value = DateTime.Today.AddDays(-7);
        _dtpTo.Value = DateTime.Today;

        // Loại GD filter: không hardcode — "(Tất cả)" + các loại thực tế có trong dữ liệu đã kéo về.
        RefreshTxnTypeOptions();

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
            _btnAutoSync.Text = Loc.T("erp.btn_auto_off");
            _btnAutoSync.BackColor = AgrisTheme.Warning;
            _numInterval.Enabled = false;
            AppendLog(Loc.T("erp.auto_started", _numInterval.Value));
        }
        else
        {
            _autoTimer.Stop();
            _btnAutoSync.Text = Loc.T("erp.btn_auto_on");
            _btnAutoSync.BackColor = AgrisTheme.Surface;
            _numInterval.Enabled = true;
            AppendLog(Loc.T("erp.auto_stopped"));
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
        configPanel.Controls.Add(new Label { Text = Loc.T("reg.filter_from"), AutoSize = true, Margin = new Padding(12, 8, 4, 0), Font = AgrisTheme.Body(9F) });
        configPanel.Controls.Add(_dtpFrom);
        configPanel.Controls.Add(new Label { Text = "→", AutoSize = true, Margin = new Padding(4, 8, 4, 0), Font = AgrisTheme.Body(9F) });
        configPanel.Controls.Add(_dtpTo);
        configPanel.Controls.Add(new Label { Text = Loc.T("erp.label_txn_type"), AutoSize = true, Margin = new Padding(12, 8, 4, 0), Font = AgrisTheme.Body(9F) });
        configPanel.Controls.Add(_cboTxnType);
        root.Controls.Add(configPanel, 0, 1);

        // Buttons
        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        btnPanel.Controls.Add(_btnRefresh);
        btnPanel.Controls.Add(new Label { Text = "  ", AutoSize = true, Margin = new Padding(4, 12, 0, 0) });
        btnPanel.Controls.Add(_btnAutoSync);
        btnPanel.Controls.Add(new Label { Text = Loc.T("erp.every"), AutoSize = true, Margin = new Padding(8, 12, 4, 0), Font = AgrisTheme.Body(9F) });
        btnPanel.Controls.Add(_numInterval);
        btnPanel.Controls.Add(new Label { Text = Loc.T("erp.minutes"), AutoSize = true, Margin = new Padding(4, 12, 0, 0), Font = AgrisTheme.Body(9F) });
        _btnAutoSync.Click += (_, _) => ToggleAutoSync();
        root.Controls.Add(btnPanel, 0, 2);

        // Bảng dữ liệu (tiêu đề + tìm kiếm + lọc cột + phân trang)
        _explorer.Margin = new Padding(0, 0, 0, 10);
        root.Controls.Add(_explorer, 0, 3);

        // Log
        var logCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(1) };
        var logHeader = GridBuilder.SectionHeader(Loc.T("erp.section_log"));
        logCard.Controls.Add(_rtbLog);
        logCard.Controls.Add(logHeader);
        root.Controls.Add(logCard, 0, 4);

        Controls.Add(root);
        _lblStatus.Text = Loc.T("erp.status_loading_db");
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
            RefreshTxnTypeOptions();
            _explorer.SetData(_allData);

            if (_allData.Count > 0)
            {
                _lblStatus.Text = Loc.T("erp.status_ready_db", _allData.Count, _activeBu);
                AppendLog(Loc.T("erp.log_loaded_db", _allData.Count, _activeBu));
            }
            else
            {
                _lblStatus.Text = Loc.T("erp.status_no_data", _activeBu);
            }
        }
        catch (Exception ex)
        {
            AppendLog(Loc.T("erp.log_load_db_error", ex.Message));
            _lblStatus.Text = Loc.T("erp.status_load_db_error");
        }
    }

    /// <summary>
    /// Nạp lại combo Loại GD: "(Tất cả)" + các mã loại GD thực tế có trong dữ liệu (mọi nguồn Oracle / WMS...).
    /// Giữ lựa chọn hiện tại nếu còn.
    /// </summary>
    private void RefreshTxnTypeOptions()
    {
        var current = _cboTxnType.SelectedItem as TxnTypeOption;
        var options = _allData
            .Where(i => !string.IsNullOrWhiteSpace(i.TransactionTypeCode))
            .GroupBy(i => i.TransactionTypeCode!.Trim())
            .Select(g => new TxnTypeOption(g.Key,
                $"{g.Key} - {g.Select(i => i.TransactionTypeName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? ""}".TrimEnd(' ', '-')))
            .OrderBy(o => o.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _cboTxnType.BeginUpdate();
        _cboTxnType.Items.Clear();
        _cboTxnType.Items.Add(new TxnTypeOption(null, Loc.T("erp.txn_all")));
        foreach (var o in options) _cboTxnType.Items.Add(o);
        var keep = current?.Code is null ? null : options.FirstOrDefault(o => o.Code == current.Code);
        _cboTxnType.SelectedItem = keep ?? _cboTxnType.Items[0];
        _cboTxnType.EndUpdate();
    }

    private sealed record TxnTypeOption(string? Code, string Text)
    {
        public override string ToString() => Text;
    }

    private async Task RefreshAsync()
    {
        _btnRefresh.Enabled = false;
        _btnRefresh.Text = Loc.T("erp.btn_pulling");
        var inputBu = _txtBU.Text.Trim();
        DateTime? fromDate = _dtpFrom.Value.Date;
        DateTime? toDate = _dtpTo.Value.Date.AddDays(1);

        // "(Tất cả)" → không gửi transactionTypeCode → API trả mọi loại GD và mọi nguồn (Oracle, WMS...).
        var txnTypeCode = (_cboTxnType.SelectedItem as TxnTypeOption)?.Code;

        AppendLog(Loc.T("erp.log_pull_start", inputBu,
            _dtpFrom.Value.ToString("dd/MM/yyyy"), _dtpTo.Value.ToString("dd/MM/yyyy"),
            txnTypeCode is not null ? Loc.T("erp.log_txn_filter", txnTypeCode) : Loc.T("erp.log_txn_all")));

        try
        {
            var allItems = new List<ErpOrderLine>();
            var seen = new HashSet<string>(); // theo ID bản ghi DigiScale — không gộp dòng / nguồn khác nhau

            // Lần 1: kéo theo khung ngày
            try
            {
                var items = await _adapter.GetAllOrderLinesAsync(inputBu, fromDate, toDate, txnTypeCode);
                foreach (var item in items)
                    if (seen.Add(item.StorageKey)) allItems.Add(item);
                AppendLog(Loc.T("erp.log_type_by_date", txnTypeCode ?? Loc.T("erp.txn_all"), items.Count));
            }
            catch (Exception ex) { AppendLog($"  ⚠ {txnTypeCode ?? Loc.T("erp.txn_all")}: {ex.Message}"); }

            // Lần 2: kéo KHÔNG filter ngày (để lấy PO/data chưa có transactionDate)
            try
            {
                var items = await _adapter.GetAllOrderLinesAsync(inputBu, null, null, txnTypeCode);
                var newCount = 0;
                foreach (var item in items)
                    if (seen.Add(item.StorageKey)) { allItems.Add(item); newCount++; }
                if (newCount > 0)
                    AppendLog(Loc.T("erp.log_type_no_date", txnTypeCode ?? Loc.T("erp.txn_all"), newCount));
            }
            catch (Exception ex) { AppendLog($"  ⚠ {txnTypeCode ?? Loc.T("erp.txn_all")} (no-date): {ex.Message}"); }

            // Thống kê theo nguồn để thấy rõ đã có WMS / Oracle...
            foreach (var g in allItems.GroupBy(i => string.IsNullOrWhiteSpace(i.Type) ? "—" : i.Type!))
                AppendLog($"  • {g.Key}: {g.Count()}");

            AppendLog(Loc.T("erp.log_total", allItems.Count));

            // Lưu DB 1 lần sau khi gom tất cả BU
            if (allItems.Count > 0)
            {
                await _adapter.SaveToDbAsync(allItems);
                AppendLog(Loc.T("erp.log_saved", allItems.Count));
            }
            else
            {
                AppendLog(Loc.T("erp.log_api_empty"));
            }

            // Reload toàn bộ từ DB (mới nhất trước, kèm cả data cũ chưa bị đè)
            var dbItems = await _adapter.LoadFromDbAsync();
            _allData = FilterByActiveBu(dbItems);
            RefreshTxnTypeOptions();
            _explorer.SetData(_allData);

            _lblStatus.Text = Loc.T("erp.status_ready_pulled", _allData.Count, _activeBu,
                _dtpFrom.Value.ToString("dd/MM"), _dtpTo.Value.ToString("dd/MM"), DateTime.Now.ToString("HH:mm:ss"));
        }
        catch (Exception ex)
        {
            AppendLog(Loc.T("erp.log_error", ex.Message));
        }
        finally
        {
            _btnRefresh.Text = Loc.T("erp.btn_pull");
            _btnRefresh.Enabled = true;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Cột bảng ERP — một nguồn duy nhất cho tiêu đề, giá trị, lọc, sắp xếp
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Thứ tự theo cách tra cứu: định danh (Shipment, Trip, xe, nguồn) → nghiệp vụ (loại GD, hàng, KH/NCC)
    /// → vận chuyển → số lượng → thời gian → mã kỹ thuật. Shipment/Trip/Action/Org Code/BU là mã ERP, giữ nguyên.
    /// </summary>
    private static readonly ExplorerColumn<ErpOrderLine>[] Columns =
    [
        new(() => Loc.T("col.shipment"), i => i.ShipmentNumber, 130, ExplorerColumnKind.Code),
        new(() => "Trip", i => i.TripNumber, 130, ExplorerColumnKind.Code),
        new(() => Loc.T("col.plate"), i => i.PlateNumber, 120),
        new(() => Loc.T("col.source"), i => i.Type ?? i.Source, 100, ExplorerColumnKind.Badge, SourceTone),
        new(() => Loc.T("erp.col_txn_type"), i => i.TransactionTypeName, 210),
        new(() => "Action", i => i.ActionCodeRequest, 105, ExplorerColumnKind.Badge, ActionTone),
        new(() => Loc.T("erp.col_item_name"), i => i.ItemDescription ?? i.ItemName, 190),
        new(() => Loc.T("field.item_code"), i => i.ItemNumber ?? i.ItemCode, 110, ExplorerColumnKind.Code),
        new(() => Loc.T("erp.col_vendor_customer"), i => i.VendorName ?? i.CustomerName, 260),
        // Nhà vận chuyển = ERP transporter (không còn lẫn tên tài xế).
        new(() => Loc.T("erp.col_transporter"), i => i.EffectiveTransporter, 190),
        new(() => Loc.T("erp.col_driver"), i => i.DriverName, 150),
        // Kho = tên tổ chức kho ERP (organizationName) — lưu khi đăng tài, đẩy oracle-tickets khi cân xong.
        new(() => Loc.T("erp.col_warehouse"), i => i.EffectiveWarehouse, 230),
        new(() => Loc.T("erp.col_total_qty"), i => i.TotalQuantity, 105, ExplorerColumnKind.Number),
        new(() => Loc.T("erp.col_remaining_qty"), i => i.RemainingQuantity, 115, ExplorerColumnKind.Number),
        new(() => Loc.T("erp.col_txn_date"), i => i.TransactionDate, 145, ExplorerColumnKind.Date),
        new(() => Loc.T("erp.col_reference"), i => i.ReferenceNumber, 120, ExplorerColumnKind.Code),
        new(() => "Org Code", i => i.OrganizationCode, 100, ExplorerColumnKind.Code),
        new(() => "BU", i => i.BusinessUnit, 100)
    ];

    private static BadgeTone SourceTone(string source) =>
        source.Contains("WMS", StringComparison.OrdinalIgnoreCase) ? BadgeTone.Info
        : source.Contains("Oracle", StringComparison.OrdinalIgnoreCase) ? BadgeTone.Success
        : BadgeTone.Neutral;

    private static BadgeTone ActionTone(string action) => action.ToUpperInvariant() switch
    {
        "CONFIRM" => BadgeTone.Brand,
        "RECEIVE" => BadgeTone.Success,
        _ => BadgeTone.Neutral
    };

    private void AppendLog(string message)
    {
        // Kéo dữ liệu / timer tự động có thể xong sau khi tab đã đóng → log đã dispose, bỏ qua.
        if (IsDisposed || _rtbLog.IsDisposed) return;
        if (_rtbLog.InvokeRequired)
        {
            try { _rtbLog.BeginInvoke(() => AppendLog(message)); }
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException) { }
            return;
        }
        _rtbLog.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
        _rtbLog.ScrollToCaret();
    }
}
