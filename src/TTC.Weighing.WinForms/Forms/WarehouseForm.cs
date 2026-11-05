using Microsoft.EntityFrameworkCore;
using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.Queries;
using TTC.Weighing.WinForms.Controls;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Presenters;
using TTC.Weighing.WinForms.Services;
using TTC.Weighing.WinForms.Theme;
using TTC.Weighing.Domain.Common;

namespace TTC.Weighing.WinForms.Forms;

/// <summary>
/// Form Kho vận — xác nhận xe vào kho / ra kho cho nghiệp vụ vật tư.
/// Luồng: Cân vào → Vào kho (xác nhận hàng, xuất theo Shipment) → Cân ra.
/// </summary>
public sealed class WarehouseForm : Form
{
    private readonly QualityPresenter _presenter;
    private readonly ISiteContext _siteContext;
    private readonly ITripRegistrationQueries _tripQueries;
    private readonly TripEventBus _eventBus;
    private readonly TTC.Weighing.Infrastructure.Persistence.WeighingDbContext _db;
    private readonly CancellationTokenSource _cts = new();

    private Guid? _currentTripId;

    // ── Tìm chuyến ──
    private readonly TextBox _txtBarcode = new()
    {
        Dock = DockStyle.Fill,
        Font = AgrisTheme.Mono(12F),
        BackColor = Color.FromArgb(255, 255, 220),
        BorderStyle = BorderStyle.FixedSingle,
        PlaceholderText = "Nhập số đăng tài hoặc biển số xe...",
        AccessibleName = "Tìm chuyến"
    };
    private readonly Button _btnTripDropdown = new()
    {
        Text = "▼", Width = 30, Dock = DockStyle.Right,
        FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, TabStop = false
    };
    private readonly ListBox _lstTripPopup = new()
    {
        Visible = false, BorderStyle = BorderStyle.FixedSingle,
        IntegralHeight = false, Font = AgrisTheme.Mono(9F)
    };
    private TripRegistrationRow[] _popupTrips = [];
    /// <summary>Gõ vào ô nhập → chờ ngắn rồi lọc danh sách xe (không tra DB theo từng phím).</summary>
    private readonly System.Windows.Forms.Timer _suggestTimer = new() { Interval = 250 };
    /// <summary>Tăng mỗi lần tra gợi ý — kết quả về trễ của lần tra cũ bị bỏ.</summary>
    private int _suggestVersion;

    // ── Chi tiết chuyến (read-only, tự điền khi chọn) ──
    private readonly TextBox _txtTripNo = ReadOnlyField("Số đăng tài");
    private readonly TextBox _txtPlate = ReadOnlyField("Biển số xe");
    private readonly TextBox _txtTrailer = new() { AccessibleName = "Rơ moóc" };
    private readonly TextBox _txtCustomer = ReadOnlyField("KH/NCC");
    private readonly TextBox _txtTransporter = ReadOnlyField("Nhà vận tải");
    private readonly TextBox _txtDriver = new() { AccessibleName = "Lái xe" };
    private readonly TextBox _txtTripNumber = ReadOnlyField("Trip Number");
    private readonly TextBox _txtTimeIn = ReadOnlyField("TG vào kho");
    private readonly TextBox _txtTimeOut = ReadOnlyField("TG ra kho");
    private readonly NumericUpDown _numActualQty = new()
    {
        DecimalPlaces = 3, Maximum = 999999m, Width = 160,
        Font = AgrisTheme.Body(11F), AccessibleName = "SL thực tế"
    };

    // ── Thông báo ──
    private readonly RichTextBox _rtbNotification = new()
    {
        ReadOnly = true, BackColor = Color.FromArgb(255, 255, 230),
        BorderStyle = BorderStyle.FixedSingle, Font = AgrisTheme.Mono(9F),
        ScrollBars = RichTextBoxScrollBars.Vertical, Height = 140
    };

    // ── Danh sách hàng ──
    private readonly DataGridView _dgvItems = GridBuilder.ReadOnlyGrid("Danh sách hàng");

    // ── Lưới xe trong kho ──
    private readonly DataGridView _dgvVehicles = GridBuilder.ReadOnlyGrid("Danh sách xe trong kho");
    private readonly RadioButton _rbInWarehouse = new() { Text = "Trong kho", Checked = true, AutoSize = true };
    private readonly RadioButton _rbExited = new() { Text = "Đã ra", AutoSize = true };
    private readonly DateTimePicker _dtpFrom = new() { Format = DateTimePickerFormat.Short, Width = 120 };
    private readonly DateTimePicker _dtpTo = new() { Format = DateTimePickerFormat.Short, Width = 120 };

    // ── Nút hành động ──
    private readonly Button _btnConfirm = GridBuilder.PrimaryButton("Xác nhận vào kho", new Size(180, 36));
    private readonly Button _btnExit = GridBuilder.SecondaryButton("Xác nhận ra kho", new Size(160, 36));
    private readonly Button _btnReset = GridBuilder.SecondaryButton("Làm mới", new Size(100, 36));

    public WarehouseForm(
        QualityPresenter presenter,
        ISiteContext siteContext,
        ITripRegistrationQueries tripQueries,
        TripEventBus eventBus,
        TTC.Weighing.Infrastructure.Persistence.WeighingDbContext db)
    {
        _presenter = presenter;
        _siteContext = siteContext;
        _tripQueries = tripQueries;
        _eventBus = eventBus;
        _db = db;

        Text = "Kho vận — Xác nhận vào/ra kho";
        MinimumSize = new Size(1000, 600);
        BackColor = AgrisTheme.Canvas;
        DoubleBuffered = true;

        BuildLayout();
        WireEvents();
        Load += async (_, _) => await LoadVehiclesAsync();
    }

    private void WireEvents()
    {
        _txtBarcode.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = true; e.SuppressKeyPress = true;
            HideTripSuggestions();
            await LookupTripAsync(_txtBarcode.Text.Trim());
        };
        // Máy quét QR (số đăng tài) — quét ở đâu trên màn cũng được, quét xong tra chuyến luôn.
        QrScanner.Register(this, () => _txtBarcode, async content =>
        {
            _txtBarcode.Text = content;
            HideTripSuggestions(); // sau khi gán chữ: gán chữ có thể bật lại hẹn giờ gợi ý
            await LookupTripAsync(content);
        });
        _btnTripDropdown.Click += async (_, _) => await ShowTripDropdownAsync();
        _btnTripDropdown.FlatAppearance.BorderSize = 1;

        // Gõ số đăng tài / biển số → hiện ngay danh sách xe khớp (con trỏ vẫn ở ô nhập).
        // Tắt bộ gõ tiếng Việt: bộ gõ giữ chữ tới khi bấm Space → gợi ý không hiện ngay khi gõ.
        Helpers.SearchText.Prepare(_txtBarcode);
        _txtBarcode.TextChanged += (_, _) =>
        {
            _suggestTimer.Stop();
            if (!_txtBarcode.Focused) return; // gán chữ bằng code (chọn xe, làm mới) → không gợi ý
            if (QrScanner.ScanInProgress) return; // máy quét đang gõ → quét xong tự tra, không gợi ý
            if (string.IsNullOrWhiteSpace(_txtBarcode.Text)) { _suggestVersion++; _lstTripPopup.Visible = false; return; }
            _suggestTimer.Start();
        };
        _suggestTimer.Tick += async (_, _) =>
        {
            _suggestTimer.Stop();
            if (!_txtBarcode.Focused || QrScanner.ScanInProgress) return;
            await ShowTripDropdownAsync(_txtBarcode.Text.Trim(), focus: false);
        };
        _txtBarcode.KeyDown += (_, e) =>
        {
            if (!_lstTripPopup.Visible) return;
            if (e.KeyCode == Keys.Down && _lstTripPopup.Items.Count > 0)
            {
                e.Handled = true; e.SuppressKeyPress = true;
                _lstTripPopup.Focus();
                _lstTripPopup.SelectedIndex = 0;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true; e.SuppressKeyPress = true;
                HideTripSuggestions();
            }
        };
        _txtBarcode.Leave += (_, _) =>
        {
            if (!IsHandleCreated || IsDisposed) return;
            BeginInvoke(() => { if (!_lstTripPopup.Focused) HideTripSuggestions(); });
        };
        Disposed += (_, _) => _suggestTimer.Dispose();
        _lstTripPopup.Click += (_, _) => SelectTripFromPopup();
        _lstTripPopup.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.Handled = true; SelectTripFromPopup(); }
            else if (e.KeyCode == Keys.Escape) { _lstTripPopup.Visible = false; _txtBarcode.Focus(); }
        };
        _btnConfirm.Click += async (_, _) => await ConfirmEntryAsync();
        _btnExit.Click += async (_, _) => await ConfirmExitAsync();
        _btnReset.Click += (_, _) => ResetForm();
        _rbInWarehouse.CheckedChanged += async (_, _) => await LoadVehiclesAsync();
    }

    private void BuildLayout()
    {
        // Layout chính: trên (chi tiết + thông báo), dưới (lưới xe)
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(8)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));

        // ── Phần trên: 2 cột ──
        var topPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1
        };
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        topPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // Cột trái: chi tiết chuyến
        var leftCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(1), AutoScroll = true };
        var header = GridBuilder.SectionHeader("Chi tiết chuyến xe");
        var detailTable = FormLayout.FieldTable();

        // Tìm chuyến
        var barcodePanel = new Panel { Dock = DockStyle.Fill, Height = 36 };
        _txtBarcode.Dock = DockStyle.Fill;
        barcodePanel.Controls.Add(_txtBarcode);
        barcodePanel.Controls.Add(_btnTripDropdown);
        FormLayout.AddFullWidth(detailTable, barcodePanel);

        FormLayout.AddField(detailTable, "Số đăng tài", _txtTripNo);
        FormLayout.AddField(detailTable, "Biển số xe", _txtPlate);
        FormLayout.AddField(detailTable, "Rơ moóc", _txtTrailer);
        FormLayout.AddField(detailTable, "KH/NCC", _txtCustomer);
        FormLayout.AddField(detailTable, "Lái xe", _txtDriver);
        FormLayout.AddField(detailTable, "Trip Number", _txtTripNumber);
        FormLayout.AddField(detailTable, "TG vào kho", _txtTimeIn);
        FormLayout.AddField(detailTable, "TG ra kho", _txtTimeOut);
        FormLayout.AddField(detailTable, "SL thực tế", _numActualQty);

        // Nút hành động
        FormLayout.AddFullWidth(detailTable, FormLayout.ActionBar(_btnConfirm, _btnExit, _btnReset), 48);

        leftCard.Controls.Add(detailTable);
        leftCard.Controls.Add(header);
        topPanel.Controls.Add(leftCard, 0, 0);

        // Cột phải: thông báo + danh sách hàng
        var rightPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2
        };
        rightPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var notifCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(1) };
        _rtbNotification.Dock = DockStyle.Fill;
        notifCard.Controls.Add(_rtbNotification);
        notifCard.Controls.Add(GridBuilder.SectionHeader("Thông báo"));
        rightPanel.Controls.Add(notifCard, 0, 0);

        var itemsCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(1) };
        _dgvItems.Dock = DockStyle.Fill;
        _dgvItems.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "Mã hàng", Width = 100 },
            new DataGridViewTextBoxColumn { HeaderText = "Tên hàng", Width = 200 },
            new DataGridViewTextBoxColumn { HeaderText = "Kho", Width = 120 },
            new DataGridViewTextBoxColumn { HeaderText = "GrossWeight", Width = 100 },
            new DataGridViewTextBoxColumn { HeaderText = "TransactionQom", Width = 120 },
            new DataGridViewTextBoxColumn { HeaderText = "TotalQuantity", Width = 100 });
        itemsCard.Controls.Add(_dgvItems);
        itemsCard.Controls.Add(GridBuilder.SectionHeader("Danh sách hàng"));
        rightPanel.Controls.Add(itemsCard, 0, 1);

        topPanel.Controls.Add(rightPanel, 1, 0);
        root.Controls.Add(topPanel, 0, 0);

        // ── Phần dưới: lưới xe trong kho ──
        var bottomCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(1) };
        var filterPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 54,
            FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(8, 6, 8, 6)
        };
        filterPanel.Controls.Add(_rbInWarehouse);
        filterPanel.Controls.Add(_rbExited);
        var lblFrom = new Label { Text = "Từ ngày:", AutoSize = true, Margin = new Padding(12, 6, 4, 0) };
        _dtpFrom.Value = VnTime.Today.AddDays(-7);
        var lblTo2 = new Label { Text = "→", AutoSize = true, Margin = new Padding(4, 6, 4, 0) };
        _dtpTo.Value = VnTime.Today;
        filterPanel.Controls.Add(lblFrom);
        filterPanel.Controls.Add(_dtpFrom);
        filterPanel.Controls.Add(lblTo2);
        filterPanel.Controls.Add(_dtpTo);

        _dtpFrom.ValueChanged += async (_, _) => await LoadVehiclesAsync();
        _dtpTo.ValueChanged += async (_, _) => await LoadVehiclesAsync();

        _dgvVehicles.Dock = DockStyle.Fill;
        _dgvVehicles.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "Số đăng tài", Width = 120 },
            new DataGridViewTextBoxColumn { HeaderText = "Shipment Number", Width = 130 },
            new DataGridViewTextBoxColumn { HeaderText = "Biển số", Width = 100 },
            new DataGridViewTextBoxColumn { HeaderText = "Lái xe", Width = 100 },
            new DataGridViewTextBoxColumn { HeaderText = "Nhà vận tải", Width = 140 },
            new DataGridViewTextBoxColumn { HeaderText = "Chủ hàng", Width = 140 },
            new DataGridViewTextBoxColumn { HeaderText = "SL thực tế", Width = 90 },
            new DataGridViewTextBoxColumn { HeaderText = "Tình trạng", Width = 90 });

        bottomCard.Controls.Add(_dgvVehicles);
        bottomCard.Controls.Add(filterPanel);
        root.Controls.Add(bottomCard, 0, 1);

        Controls.Add(root);
    }

    private async Task LookupTripAsync(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return;

        AppendNotification($"Đang tìm chuyến: {input}...");
        try
        {
            var result = await _presenter.ScanQrAsync(input, _siteContext.CurrentSiteId, _cts.Token);
            if (!result.IsSuccess)
            {
                AppendNotification($"❌ {result.ErrorMessage}");
                return;
            }

            _currentTripId = result.TripId;
            _txtTripNo.Text = result.TripNo ?? "";
            _txtPlate.Text = result.Plate ?? "";
            _txtCustomer.Text = result.CustomerOrOwner ?? "";
            _txtTripNumber.Text = result.ShipmentNo ?? "";
            _txtTransporter.Text = "";
            AppendNotification($"✅ Chuyến {result.TripNo} — {result.Plate}");
        }
        catch (Exception ex)
        {
            AppendNotification($"⚠ Lỗi: {ex.Message}");
        }
    }

    private async Task ConfirmEntryAsync()
    {
        if (_currentTripId is null)
        {
            Helpers.AppMessageBox.Show("Chưa chọn chuyến.", "Thiếu thông tin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _txtTimeIn.Text = VnTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
        AppendNotification($"✅ Xe {_txtPlate.Text} đã vào kho lúc {_txtTimeIn.Text}");

        // Ghi CheckpointEvent WarehouseIn vào DB — để precondition check cho cân lần 2
        try
        {
            var now = DateTimeOffset.UtcNow;

            _db.CheckpointEvents.Add(new TTC.Weighing.Domain.Entities.CheckpointEvent
            {
                TripId = _currentTripId.Value,
                Type = TTC.Weighing.Domain.Enums.CheckpointType.WarehouseIn,
                Status = TTC.Weighing.Domain.Enums.CheckpointStatus.Done,
                StartedAt = now,
                CompletedAt = now,
                ConfirmedBy = "warehouse.operator",
                CreatedAt = now,
                CreatedBy = "warehouse.operator"
            });

            await _db.SaveChangesAsync(_cts.Token);
            AppendNotification("💾 Đã ghi nhận vào kho (CheckpointEvent).");
        }
        catch (Exception ex)
        {
            AppendNotification($"⚠ Lỗi ghi DB: {ex.Message}");
        }

        // Thêm vào lưới
        _dgvVehicles.Rows.Insert(0,
            _txtTripNo.Text, _txtTripNumber.Text, _txtPlate.Text,
            _txtDriver.Text, _txtTransporter.Text, _txtCustomer.Text,
            _numActualQty.Value.ToString("N3"), "Trong kho");

        _eventBus.Publish(new TripChangedEventArgs
        {
            TripId = _currentTripId.Value,
            TripNo = _txtTripNo.Text,
            Plate = _txtPlate.Text,
            ChangeType = TripChangeType.WarehouseEntry,
            SourceForm = "WarehouseForm"
        });

        Helpers.AppMessageBox.Show($"Xe {_txtPlate.Text} đã vào kho thành công.",
            "Xác nhận", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task ConfirmExitAsync()
    {
        if (_currentTripId is null)
        {
            Helpers.AppMessageBox.Show("Chưa chọn chuyến.", "Thiếu thông tin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _txtTimeOut.Text = VnTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
        AppendNotification($"✅ Xe {_txtPlate.Text} đã ra kho lúc {_txtTimeOut.Text}");

        _eventBus.Publish(new TripChangedEventArgs
        {
            TripId = _currentTripId.Value,
            TripNo = _txtTripNo.Text,
            Plate = _txtPlate.Text,
            ChangeType = TripChangeType.WarehouseExit,
            SourceForm = "WarehouseForm"
        });

        Helpers.AppMessageBox.Show($"Xe {_txtPlate.Text} đã ra kho thành công.",
            "Xác nhận", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ResetForm()
    {
        _currentTripId = null;
        _txtBarcode.Clear();
        _txtTripNo.Clear();
        _txtPlate.Clear();
        _txtTrailer.Clear();
        _txtCustomer.Clear();
        _txtTransporter.Clear();
        _txtDriver.Clear();
        _txtTripNumber.Clear();
        _txtTimeIn.Clear();
        _txtTimeOut.Clear();
        _numActualQty.Value = 0;
        _dgvItems.Rows.Clear();
        _txtBarcode.Focus();
    }

    private void HideTripSuggestions()
    {
        _suggestTimer.Stop();
        _suggestVersion++;
        _lstTripPopup.Visible = false;
    }

    /// <param name="query">Chuỗi đang gõ để lọc (null = nút ▼, hiện tất cả).</param>
    /// <param name="focus">false = gợi ý khi gõ, con trỏ ở lại ô nhập.</param>
    private async Task ShowTripDropdownAsync(string? query = null, bool focus = true)
    {
        var version = ++_suggestVersion;
        try
        {
            var filter = new TripRegistrationFilter { Tab = TripGridTab.InFactory };
            var trips = await _tripQueries.ListAsync(_siteContext.CurrentSiteId, filter, _cts.Token);
            if (version != _suggestVersion) return; // đã gõ tiếp / đã chọn xe trong lúc chờ
            if (!focus && !_txtBarcode.Focused) return;
            _popupTrips = string.IsNullOrWhiteSpace(query) ? trips.ToArray() : TripPickerList.Filter(trips, query);
            if (_popupTrips.Length == 0) { _lstTripPopup.Visible = false; return; }

            _lstTripPopup.Items.Clear();
            foreach (var t in _popupTrips)
                _lstTripPopup.Items.Add($"{t.Plate ?? "?",-15} | {t.TripNo,-25} | {t.CustomerOrSupplier ?? ""}");

            var screenPos = _txtBarcode.PointToScreen(new Point(0, _txtBarcode.Height));
            var formPos = FindForm()!.PointToClient(screenPos);
            _lstTripPopup.Location = formPos;
            _lstTripPopup.Width = Math.Max(_txtBarcode.Width + 30, 500);
            _lstTripPopup.Height = Math.Min(_popupTrips.Length * 20 + 4, 300);
            if (_lstTripPopup.Parent != FindForm())
            { FindForm()!.Controls.Add(_lstTripPopup); _lstTripPopup.BringToFront(); }
            _lstTripPopup.Visible = true;
            if (focus) _lstTripPopup.Focus();
        }
        catch { _lstTripPopup.Visible = false; }
    }

    private void SelectTripFromPopup()
    {
        var idx = _lstTripPopup.SelectedIndex;
        _lstTripPopup.Visible = false;
        if (idx < 0 || idx >= _popupTrips.Length) return;
        _txtBarcode.Text = _popupTrips[idx].TripNo;
        _ = LookupTripAsync(_popupTrips[idx].TripNo);
    }

    private async Task LoadVehiclesAsync()
    {
        try
        {
            var siteId = _siteContext.CurrentSiteId;
            var showInWarehouse = _rbInWarehouse.Checked;

            var fromUtc = VnTime.ToUtc(_dtpFrom.Value.Date);
            var toUtc = VnTime.ToUtc(_dtpTo.Value.Date.AddDays(1));

            var fromDto = new DateTimeOffset(fromUtc, TimeSpan.Zero);
            var toDto = new DateTimeOffset(toUtc, TimeSpan.Zero);

            // Load trips có WarehouseIn checkpoint trong khung ngày
            var trips = await _db.Trips
                .Where(t => t.SiteId == siteId && t.DeletedAt == null)
                .Where(t => t.CreatedAt >= fromDto && t.CreatedAt < toDto)
                .Where(t => t.Lifecycle != TTC.Weighing.Domain.Enums.TripLifecycle.Cancelled)
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new
                {
                    t.TripNo,
                    t.ReferenceTripNo,
                    t.TractorPlate,
                    t.SugarcaneOwner,
                    t.Lifecycle,
                    HasWarehouseIn = _db.CheckpointEvents.Any(c =>
                        c.TripId == t.Id && c.Type == TTC.Weighing.Domain.Enums.CheckpointType.WarehouseIn),
                    IsClosed = t.Lifecycle == TTC.Weighing.Domain.Enums.TripLifecycle.Closed
                })
                .ToListAsync(_cts.Token);

            // Filter: Trong kho = có WarehouseIn nhưng chưa Closed; Đã ra = Closed
            var filtered = showInWarehouse
                ? trips.Where(t => t.HasWarehouseIn && !t.IsClosed).ToList()
                : trips.Where(t => t.IsClosed).ToList();

            _dgvVehicles.SuspendLayout();
            _dgvVehicles.Rows.Clear();
            foreach (var t in filtered)
            {
                var status = t.IsClosed ? "Đã ra" : "Trong kho";
                _dgvVehicles.Rows.Add(
                    t.TripNo, t.ReferenceTripNo, t.TractorPlate,
                    "", "", t.SugarcaneOwner ?? "", "", status);
            }
            _dgvVehicles.ResumeLayout();
        }
        catch (Exception ex)
        {
            AppendNotification($"⚠ Lỗi tải danh sách xe: {ex.Message}");
        }
    }

    private void AppendNotification(string message)
    {
        // Tác vụ nền có thể xong sau khi tab đã đóng → khung thông báo đã dispose, bỏ qua.
        if (IsDisposed || _rtbNotification.IsDisposed) return;
        if (_rtbNotification.InvokeRequired)
        {
            try { _rtbNotification.BeginInvoke(() => AppendNotification(message)); }
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException) { }
            return;
        }
        _rtbNotification.AppendText($"{VnTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
        _rtbNotification.ScrollToCaret();
    }

    private static TextBox ReadOnlyField(string name) => new()
    {
        ReadOnly = true,
        BackColor = AgrisTheme.ReadOnlyBg,
        ForeColor = AgrisTheme.ReadOnlyText,
        BorderStyle = BorderStyle.FixedSingle,
        AccessibleName = name
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing) _cts.Dispose();
        base.Dispose(disposing);
    }
}
