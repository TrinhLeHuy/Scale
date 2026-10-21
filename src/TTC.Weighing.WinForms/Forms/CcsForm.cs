using Microsoft.EntityFrameworkCore;
using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.Queries;
using TTC.Weighing.Application.UseCases.Quality;
using TTC.Weighing.Domain.BusinessRules;
using TTC.Weighing.WinForms.Controls;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Presenters;
using TTC.Weighing.WinForms.Services;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Forms;

/// <summary>
/// Form đo chữ đường (CCS) — tách riêng khỏi QualityForm.
/// Quét QR → nhập giá trị CCS → ghi nhận → push DigiScale.
/// </summary>
public sealed class CcsForm : Form
{
    private readonly QualityPresenter _presenter;
    private readonly ISiteContext _siteContext;
    private readonly ITripRegistrationQueries _tripRegistrationQueries;
    private readonly TTC.Weighing.Infrastructure.Persistence.WeighingDbContext _db;
    private readonly TripEventBus _eventBus;
    private readonly CancellationTokenSource _cts = new();

    private Guid? _currentTripId;
    private string? _currentFrmLenhDonId;
    private string? _currentFrmMaLenhDon;
    private string? _currentFrmLenhDonBu;
    private string? _lastQrContent;

    // ── Tìm chuyến ──
    private readonly TextBox _txtBarcode = new()
    {
        Dock = DockStyle.Fill,
        Font = AgrisTheme.Mono(12F),
        BackColor = Color.FromArgb(255, 255, 220),
        BorderStyle = BorderStyle.FixedSingle,
        PlaceholderText = "Nhập mã đăng tài hoặc biển số xe...",
        AccessibleName = "Nhập số đăng tài hoặc biển số xe",
        AutoCompleteMode = AutoCompleteMode.None,
        AutoCompleteSource = AutoCompleteSource.None
    };
    private readonly Button _btnTripDropdown = new()
    {
        Text = "▼",
        Width = 30,
        Dock = DockStyle.Right,
        FlatStyle = FlatStyle.Flat,
        Cursor = Cursors.Hand,
        TabStop = false
    };
    private readonly ListBox _lstTripPopup = new()
    {
        Visible = false,
        BorderStyle = BorderStyle.FixedSingle,
        IntegralHeight = false,
        Font = AgrisTheme.Mono(9F)
    };
    private TripRegistrationRow[] _popupTrips = [];

    // ── Thông tin chuyến ──
    private readonly TextBox _txtTripNo = ReadOnlyField("Số đăng tài");
    private readonly TextBox _txtPlate = ReadOnlyField("Biển số");
    private readonly TextBox _txtOwner = ReadOnlyField("Khách hàng");

    // ── CCS + Brix + Pol ──
    private readonly NumericUpDown _numBrix = new()
    {
        DecimalPlaces = 2, Maximum = 40m, Minimum = 0m, Increment = 0.01m,
        Dock = DockStyle.Fill, Font = new Font(AgrisTheme.MonoFamily, 16F, FontStyle.Bold),
        BackColor = Color.FromArgb(255, 255, 220), ForeColor = Color.FromArgb(0, 80, 40),
        BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Center
    };
    private readonly NumericUpDown _numPol = new()
    {
        DecimalPlaces = 2, Maximum = 100m, Minimum = 0m, Increment = 0.01m,
        Dock = DockStyle.Fill, Font = new Font(AgrisTheme.MonoFamily, 16F, FontStyle.Bold),
        BackColor = Color.FromArgb(255, 255, 220), ForeColor = Color.FromArgb(0, 80, 40),
        BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Center
    };
    private readonly NumericUpDown _numPolDoc = new()
    {
        DecimalPlaces = 2, Maximum = 100m, Minimum = 0m, Increment = 0.01m,
        Dock = DockStyle.Fill, Font = new Font(AgrisTheme.MonoFamily, 16F, FontStyle.Bold),
        BackColor = Color.FromArgb(255, 255, 220), ForeColor = Color.FromArgb(0, 80, 40),
        BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Center
    };
    private readonly NumericUpDown _numCcs = new()
    {
        DecimalPlaces = 2, Maximum = 30m, Minimum = 0m, Increment = 0.1m,
        Dock = DockStyle.Fill, Font = new Font(AgrisTheme.MonoFamily, 18F, FontStyle.Bold),
        BackColor = Color.FromArgb(255, 250, 200), ForeColor = Color.FromArgb(180, 80, 0),
        BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Center
    };
    // ── Fibre (tỉ lệ xơ) — xác định hàng tuần, dùng để tính CCS tự động ──
    private readonly NumericUpDown _numFibre = new()
    {
        DecimalPlaces = 2, Maximum = 20m, Minimum = 0m, Increment = 0.1m, Value = 13.0m,
        Dock = DockStyle.Fill, Font = new Font(AgrisTheme.MonoFamily, 14F, FontStyle.Bold),
        BackColor = Color.FromArgb(230, 245, 255), ForeColor = Color.FromArgb(0, 60, 120),
        BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Center
    };
    private readonly Label _lblCcsCalcResult = new()
    {
        Text = "CCS tự động: —",
        Font = AgrisTheme.Body(10F, FontStyle.Bold),
        ForeColor = AgrisTheme.BrandGreen,
        AutoSize = true,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly CheckBox _chkCcsPending = new()
    {
        Text = "Nguồn báo đang chờ kết quả",
        AutoSize = true
    };
    private readonly Button _btnRecord = GridBuilder.PrimaryButton("Ghi nhận CCS", new Size(200, 36));
    private readonly Button _btnPrint = GridBuilder.SecondaryButton("In phiếu", new Size(100, 36));
    private readonly Button _btnRetryDigi = GridBuilder.SecondaryButton("🔄 Đẩy lại Digi", new Size(140, 36));
    private readonly Button _btnReset = GridBuilder.SecondaryButton("Lượt mới (Esc)", new Size(130, 36));

    // ── Khung ngày lọc xe bên trái ──
    private readonly DateTimePicker _dtpTripFrom = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 150 };
    private readonly DateTimePicker _dtpTripTo = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 150 };

    // ── Lịch sử ──
    private readonly DataGridView _grid = GridBuilder.ReadOnlyGrid("Lịch sử CCS");
    private readonly DateTimePicker _dtpHistFrom = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 150 };
    private readonly DateTimePicker _dtpHistTo = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 150 };

    // ── Status ──
    private readonly Label _lblStatus = new()
    {
        Text = "Nhập mã đăng tài hoặc biển số để bắt đầu",
        Font = AgrisTheme.Body(10F, FontStyle.Italic),
        ForeColor = AgrisTheme.BrandGreen,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft
    };

    public CcsForm(QualityPresenter presenter, ISiteContext siteContext, ITripRegistrationQueries tripRegistrationQueries, TTC.Weighing.Infrastructure.Persistence.WeighingDbContext db, TripEventBus eventBus)
    {
        _presenter = presenter;
        _siteContext = siteContext;
        _tripRegistrationQueries = tripRegistrationQueries;
        _db = db;
        _eventBus = eventBus;

        Text = "Đo chữ đường (CCS)";
        MinimumSize = new Size(900, 600);
        BackColor = AgrisTheme.Canvas;
        DoubleBuffered = true;

        BuildLayout();

        _txtBarcode.KeyDown += OnBarcodeKeyDown;
        _btnRecord.Click += async (_, _) => await RecordCcsAsync();
        _btnPrint.Click += (_, _) => PrintCcsTicket();
        _btnRetryDigi.Click += async (_, _) => await RetryPushDigiAsync();
        _btnReset.Click += (_, _) => ResetForm();
        _chkCcsPending.CheckedChanged += (_, _) => _numCcs.Enabled = !_chkCcsPending.Checked;
        Load += async (_, _) =>
        {
            await LoadAllCcsAsync();
        };
    }

    private void BuildLayout()
    {
        // 2 cột: trái = thông tin + CCS, phải = lịch sử
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(8)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // ── Cột trái ──
        var leftCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(1), AutoScroll = true };
        var header = GridBuilder.SectionHeader("Đo chữ đường (CCS)");
        var table = FormLayout.FieldTable();

        FormLayout.AddFullWidth(table, BuildBarcodeWithDropdown());

        // Khung ngày lọc xe
        var tripDateBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, Height = 38,
            FlowDirection = FlowDirection.LeftToRight, WrapContents = false
        };
        _dtpTripFrom.Value = DateTime.Today;
        _dtpTripTo.Value = DateTime.Today;
        tripDateBar.Controls.AddRange([
            new Label { Text = "Xem theo thời gian", AutoSize = true, Margin = new Padding(0, 6, 4, 0),
                Font = AgrisTheme.Body(8.5F), ForeColor = AgrisTheme.TextMuted },
            _dtpTripFrom,
            new Label { Text = "→", AutoSize = true, Margin = new Padding(4, 6, 4, 0) },
            _dtpTripTo
        ]);
        FormLayout.AddFullWidth(table, tripDateBar, 42);

        FormLayout.AddFullWidth(table, _lblStatus);
        FormLayout.AddField(table, "Số đăng tài", _txtTripNo);
        FormLayout.AddField(table, "Biển số", _txtPlate);
        FormLayout.AddField(table, "Khách hàng", _txtOwner);

        var ccsLabel = new Label { Text = "KẾT QUẢ PHÂN TÍCH", Font = AgrisTheme.Body(11F, FontStyle.Bold), AutoSize = true };
        FormLayout.AddFullWidth(table, ccsLabel, 42);
        FormLayout.AddField(table, "Brix", _numBrix);
        FormLayout.AddField(table, "Pol", _numPol);
        FormLayout.AddField(table, "Pol cải chính", _numPolDoc);
        FormLayout.AddField(table, "CCS", _numCcs);

        // Override font + màu sau AddField (vì AddField set font mặc định)
        _numBrix.Font = new Font(AgrisTheme.MonoFamily, 16F, FontStyle.Bold);
        _numBrix.BackColor = Color.FromArgb(255, 255, 220);
        _numBrix.ForeColor = Color.FromArgb(0, 80, 40);
        _numBrix.TextAlign = HorizontalAlignment.Center;
        _numPol.Font = new Font(AgrisTheme.MonoFamily, 16F, FontStyle.Bold);
        _numPol.BackColor = Color.FromArgb(255, 255, 220);
        _numPol.ForeColor = Color.FromArgb(0, 80, 40);
        _numPol.TextAlign = HorizontalAlignment.Center;
        _numPolDoc.Font = new Font(AgrisTheme.MonoFamily, 16F, FontStyle.Bold);
        _numPolDoc.BackColor = Color.FromArgb(255, 255, 220);
        _numPolDoc.ForeColor = Color.FromArgb(0, 80, 40);
        _numPolDoc.TextAlign = HorizontalAlignment.Center;
        _numCcs.Font = new Font(AgrisTheme.MonoFamily, 18F, FontStyle.Bold);
        _numCcs.BackColor = Color.FromArgb(255, 250, 200);
        _numCcs.ForeColor = Color.FromArgb(180, 80, 0);
        _numCcs.TextAlign = HorizontalAlignment.Center;

        // Fibre (tỉ lệ xơ) + label CCS tự tính
        FormLayout.AddField(table, "Fibre (xơ %)", _numFibre);
        _numFibre.Font = new Font(AgrisTheme.MonoFamily, 14F, FontStyle.Bold);
        _numFibre.BackColor = Color.FromArgb(230, 245, 255);
        _numFibre.ForeColor = Color.FromArgb(0, 60, 120);
        _numFibre.TextAlign = HorizontalAlignment.Center;
        FormLayout.AddFullWidth(table, _lblCcsCalcResult, 38);

        // Auto-calc CCS khi Brix/Pol/Fibre thay đổi
        _numBrix.ValueChanged += (_, _) => AutoCalcCcs();
        _numPol.ValueChanged += (_, _) => AutoCalcCcs();
        _numFibre.ValueChanged += (_, _) => AutoCalcCcs();

        FormLayout.AddFullWidth(table, _chkCcsPending, 38);
        FormLayout.AddFullWidth(table, FormLayout.ActionBar(_btnRecord, _btnPrint, _btnRetryDigi, _btnReset), 58);

        leftCard.Controls.Add(table);
        leftCard.Controls.Add(header);
        root.Controls.Add(leftCard, 0, 0);

        // ── Cột phải: lịch sử CCS + bộ lọc ngày ──
        var rightCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(1) };
        var histHeader = GridBuilder.SectionHeader("Lịch sử CCS");

        var histFilter = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 42,
            FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(4, 2, 4, 2),
            WrapContents = false
        };
        var lblHistFrom = new Label { Text = "Từ:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) };
        _dtpHistFrom.Value = DateTime.Today.AddDays(-7);
        var lblHistTo = new Label { Text = "→", AutoSize = true, Margin = new Padding(4, 6, 4, 0) };
        _dtpHistTo.Value = DateTime.Today;
        var lblHistNote = new Label
        {
            Text = "Bấm vào dòng để xem thông tin chuyến và in lại phiếu",
            AutoSize = true, Margin = new Padding(12, 6, 0, 0),
            Font = AgrisTheme.Body(8F), ForeColor = AgrisTheme.TextMuted
        };
        histFilter.Controls.AddRange([lblHistFrom, _dtpHistFrom, lblHistTo, _dtpHistTo, lblHistNote]);
        _dtpHistFrom.ValueChanged += async (_, _) => await LoadAllCcsAsync();
        _dtpHistTo.ValueChanged += async (_, _) => await LoadAllCcsAsync();

        _grid.Dock = DockStyle.Fill;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "Số đăng tài", MinimumWidth = 80, FillWeight = 15, Name = "TripNo" },
            new DataGridViewTextBoxColumn { HeaderText = "Biển số", MinimumWidth = 60, FillWeight = 10, Name = "Plate" },
            new DataGridViewTextBoxColumn { HeaderText = "Brix", MinimumWidth = 45, FillWeight = 8 },
            new DataGridViewTextBoxColumn { HeaderText = "Pol", MinimumWidth = 45, FillWeight = 8 },
            new DataGridViewTextBoxColumn { HeaderText = "Pol CC", MinimumWidth = 45, FillWeight = 8 },
            new DataGridViewTextBoxColumn { HeaderText = "CCS", MinimumWidth = 45, FillWeight = 8 },
            new DataGridViewTextBoxColumn { HeaderText = "Trạng thái", MinimumWidth = 60, FillWeight = 10 },
            new DataGridViewTextBoxColumn { HeaderText = "Thời gian", MinimumWidth = 80, FillWeight = 15 });
        foreach (DataGridViewColumn col in _grid.Columns)
            col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _grid.Columns["TripNo"]!.DefaultCellStyle.ForeColor = AgrisTheme.BrandTeal;
        _grid.Columns["TripNo"]!.DefaultCellStyle.Font = AgrisTheme.Body(9.5F, FontStyle.Underline | FontStyle.Bold);
        _grid.CellMouseEnter += (_, ev) => { if (ev.RowIndex >= 0) _grid.Cursor = Cursors.Hand; };
        _grid.CellMouseLeave += (_, _) => _grid.Cursor = Cursors.Default;
        _grid.CellClick += OnCcsGridCellClick;

        var hintLabel = new Label
        {
            Text = Loc.T("quality.grid_click_hint"),
            Font = AgrisTheme.Caption(),
            ForeColor = AgrisTheme.TextMuted,
            AutoSize = true,
            Dock = DockStyle.Top,
            Padding = new Padding(8, 0, 0, 2)
        };

        rightCard.Controls.Add(_grid);
        rightCard.Controls.Add(hintLabel);
        rightCard.Controls.Add(histFilter);
        rightCard.Controls.Add(histHeader);
        root.Controls.Add(rightCard, 1, 0);

        Controls.Add(root);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { ResetForm(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private async void OnBarcodeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter) return;
        e.Handled = true;
        e.SuppressKeyPress = true;

        var qr = _txtBarcode.Text.Trim();
        if (string.IsNullOrWhiteSpace(qr)) return;

        try
        {
            _lblStatus.Text = "Đang tra cứu...";
            _lblStatus.ForeColor = AgrisTheme.Info;

            var result = await _presenter.ScanQrAsync(qr, _siteContext.CurrentSiteId, _cts.Token);

            if (!result.IsSuccess)
            {
                _lblStatus.Text = $"❌ {result.ErrorMessage}";
                _lblStatus.ForeColor = AgrisTheme.Danger;
                return;
            }

            _currentTripId = result.TripId;
            _currentFrmLenhDonId = result.FrmLenhDonId;
            _currentFrmMaLenhDon = result.FrmMaLenhDon;
            _currentFrmLenhDonBu = result.FrmLenhDonBu;
            _lastQrContent = qr;
            _txtTripNo.Text = result.TripNo ?? "";
            _txtPlate.Text = result.Plate ?? "";
            _txtOwner.Text = result.CustomerOrOwner ?? "";

            _lblStatus.Text = $"✅ Chuyến {result.TripNo} — Nhập giá trị CCS rồi bấm Ghi nhận";
            _lblStatus.ForeColor = AgrisTheme.BrandGreen;
            _numCcs.Focus();
        }
        catch (Exception ex)
        {
            _lblStatus.Text = $"❌ Lỗi: {ex.Message}";
            _lblStatus.ForeColor = AgrisTheme.Danger;
        }
    }

    private async Task RecordCcsAsync()
    {
        if (_currentTripId is null)
        {
            MessageBox.Show("Nhập mã đăng tài hoặc biển số trước.", "Thiếu thông tin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _btnRecord.Enabled = false;
        try
        {
            var result = await _presenter.RecordCcsAsync(
                new ReceiveCcsResultCommand(
                    TripId: _currentTripId.Value,
                    CcsValue: _chkCcsPending.Checked ? null : _numCcs.Value,
                    Source: "FRM",
                    SourceReportsPending: _chkCcsPending.Checked,
                    CorrelationId: Guid.NewGuid().ToString("N")),
                _cts.Token);

            if (!result.IsSuccess)
            {
                MessageBox.Show(result.FailureReason, "Lỗi ghi CCS", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            MessageBox.Show($"Đã ghi nhận CCS. Trạng thái: {result.Status}", "Thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);

            // Thêm vào lịch sử grid
            _grid.Rows.Insert(0, _txtTripNo.Text, _txtPlate.Text,
                $"{_numBrix.Value:N2}",
                $"{_numPol.Value:N2}",
                $"{_numPolDoc.Value:N2}",
                _chkCcsPending.Checked ? "Đang chờ" : $"{_numCcs.Value:N2}",
                result.Status.ToString(), DateTime.Now.ToString("dd/MM/yyyy HH:mm"));

            // Push CCS lên DigiScale + popup
            try
            {
                // Lấy LenhDon từ scan result — fallback cache
                var ld = _presenter.FindLenhDonByPlate(_txtPlate.Text);
                var pushResult = await _presenter.PushCcsAsync(
                    _currentFrmLenhDonId ?? ld?.SourceKey ?? _txtTripNo.Text,
                    _currentFrmMaLenhDon ?? ld?.MaLD ?? _txtTripNo.Text,
                    "0", "1", DateTime.Now,
                    _numBrix.Value.ToString("F2"), _numCcs.Value.ToString("F2"),
                    _numPol.Value.ToString("F2"), _numPolDoc.Value.ToString("F2"), "1",
                    Program.LoggedInUserName, _currentFrmLenhDonBu ?? ld?.BusinessUnit, _cts.Token);

                var digiOk = pushResult.IsSuccess;
                Helpers.DigiScalePopup.Show(this,
                    "GHI CHỮ ĐƯỜNG THÀNH CÔNG",
                    $"Chuyến: {_txtTripNo.Text}\nBiển số: {_txtPlate.Text}\nBrix: {_numBrix.Value:N2} | Pol: {_numPol.Value:N2} | CCS: {_numCcs.Value:N2}",
                    digiOk, pushResult.ErrorMessage ?? (digiOk ? "OK" : "Lỗi"));
            }
            catch (Exception pushEx)
            {
                Helpers.DigiScalePopup.Show(this,
                    "GHI CHỮ ĐƯỜNG THÀNH CÔNG",
                    $"Chuyến: {_txtTripNo.Text}\nBiển số: {_txtPlate.Text}\nCCS: {_numCcs.Value:N2}",
                    false, $"Lỗi push: {pushEx.Message}");
            }

            _eventBus.Publish(new TripChangedEventArgs
            {
                TripId = _currentTripId.Value,
                TripNo = _txtTripNo.Text,
                Plate = _txtPlate.Text,
                QrContent = _lastQrContent,
                ChangeType = TripChangeType.CcsReceived,
                SourceForm = "CcsForm"
            });

            // Refresh lịch sử CCS
            await LoadAllCcsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi: {ex.Message}", "Lỗi hệ thống", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnRecord.Enabled = true;
        }
    }

    private void ResetForm()
    {
        _currentTripId = null;
        _currentFrmLenhDonId = null;
        _currentFrmMaLenhDon = null;
        _currentFrmLenhDonBu = null;
        _txtBarcode.Clear();
        _txtTripNo.Clear();
        _txtPlate.Clear();
        _txtOwner.Clear();
        _numCcs.Value = 0;
        _numBrix.Value = 0;
        _numPol.Value = 0;
        _numPolDoc.Value = 0;
        // Fibre giữ nguyên giá trị trước đó (xác định theo tuần, không reset mỗi lần)
        _chkCcsPending.Checked = false;
        _lblStatus.Text = "Nhập mã đăng tài hoặc biển số để bắt đầu";
        _lblStatus.ForeColor = AgrisTheme.BrandGreen;
        _lblCcsCalcResult.Text = "CCS tự động: —";
        SetFieldsReadOnly(false);
        _txtBarcode.Focus();
    }

    /// <summary>
    /// Tự tính CCS từ Brix + Pol + Fibre theo QCVN 01-98:2012/BNNPTNT.
    /// Kết quả hiện ở label + tự điền vào ô CCS.
    /// </summary>
    private void AutoCalcCcs()
    {
        if (_numBrix.Value <= 0 || _numPol.Value <= 0)
        {
            _lblCcsCalcResult.Text = "CCS tự động: — (nhập Brix + Pol)";
            _lblCcsCalcResult.ForeColor = AgrisTheme.TextMuted;
            return;
        }

        var result = CcsCalculator.Calculate(_numPol.Value, _numBrix.Value, _numFibre.Value);
        if (!result.IsValid)
        {
            _lblCcsCalcResult.Text = $"CCS tự động: ❌ {result.InvalidReason}";
            _lblCcsCalcResult.ForeColor = AgrisTheme.Danger;
            return;
        }

        _lblCcsCalcResult.Text = $"CCS tự động: {result.CcsValue:N2} (Pol cc: {result.PolCaiChinh:N4}, g: {result.ApparentDensity:N5})";
        _lblCcsCalcResult.ForeColor = AgrisTheme.BrandGreen;

        // Tự điền CCS vào ô nhập (user vẫn có thể sửa tay)
        _numCcs.Value = Math.Max(0, Math.Min(_numCcs.Maximum, result.CcsValue));

        // Tính Pol cải chính và điền
        _numPolDoc.Value = Math.Max(0, Math.Min(_numPolDoc.Maximum, result.PolCaiChinh));
    }

    private static TextBox ReadOnlyField(string name) => new()
    {
        ReadOnly = true,
        BackColor = AgrisTheme.ReadOnlyBg,
        ForeColor = AgrisTheme.ReadOnlyText,
        BorderStyle = BorderStyle.FixedSingle,
        AccessibleName = name
    };

    // ═══════════════════════════════════════════════════════════
    //  Trip dropdown — chọn xe đang trong nhà máy
    // ═══════════════════════════════════════════════════════════

    private Panel BuildBarcodeWithDropdown()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Height = 36 };
        _txtBarcode.Dock = DockStyle.Fill;
        _btnTripDropdown.FlatAppearance.BorderSize = 1;
        panel.Controls.Add(_txtBarcode);
        panel.Controls.Add(_btnTripDropdown);

        _btnTripDropdown.Click += async (_, _) => await ShowTripDropdownAsync();
        _lstTripPopup.Click += (_, _) => SelectTripFromPopup();
        _lstTripPopup.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.Handled = true; SelectTripFromPopup(); }
            else if (e.KeyCode == Keys.Escape) { _lstTripPopup.Visible = false; _txtBarcode.Focus(); }
        };

        return panel;
    }

    private async Task ShowTripDropdownAsync()
    {
        try
        {
            // Lọc theo khung ngày bên trái — hiện tất cả chuyến (không chỉ InFactory)
            var filter = new TripRegistrationFilter
            {
                Tab = TripGridTab.InFactory,
                FromUtc = new DateTimeOffset(_dtpTripFrom.Value.Date, TimeZoneInfo.Local.GetUtcOffset(_dtpTripFrom.Value)).UtcDateTime,
                ToUtc = new DateTimeOffset(_dtpTripTo.Value.Date.AddDays(1), TimeZoneInfo.Local.GetUtcOffset(_dtpTripTo.Value)).UtcDateTime
            };
            var trips = await _tripRegistrationQueries.ListAsync(_siteContext.CurrentSiteId, filter, _cts.Token);
            _popupTrips = trips.ToArray();

            if (_popupTrips.Length == 0) { _lstTripPopup.Visible = false; return; }

            _lstTripPopup.Items.Clear();
            foreach (var t in _popupTrips)
                _lstTripPopup.Items.Add($"{t.Plate ?? "?",-15} | {t.TripNo,-25} | {t.CustomerOrSupplier ?? ""}");

            var screenPos = _txtBarcode.PointToScreen(new Point(0, _txtBarcode.Height));
            var formPos = FindForm()!.PointToClient(screenPos);
            _lstTripPopup.Location = formPos;
            _lstTripPopup.Width = Math.Max(_txtBarcode.Width + _btnTripDropdown.Width, 500);
            _lstTripPopup.Height = Math.Min(_popupTrips.Length * 20 + 4, 300);

            if (_lstTripPopup.Parent != FindForm())
            {
                FindForm()!.Controls.Add(_lstTripPopup);
                _lstTripPopup.BringToFront();
            }
            _lstTripPopup.Visible = true;
            _lstTripPopup.Focus();
        }
        catch { _lstTripPopup.Visible = false; }
    }

    private void SelectTripFromPopup()
    {
        var idx = _lstTripPopup.SelectedIndex;
        _lstTripPopup.Visible = false;
        if (idx < 0 || idx >= _popupTrips.Length) return;

        var trip = _popupTrips[idx];
        _txtBarcode.Text = trip.TripNo;
        _lastQrContent = trip.TripNo;
        _ = ScanAndLoadTripAsync(trip.TripNo);
    }

    private async Task ScanAndLoadTripAsync(string content)
    {
        // Reuse existing OnBarcodeKeyDown logic
        var e = new KeyEventArgs(Keys.Enter);
        // Directly call scan
        try
        {
            var result = await _presenter.ScanQrAsync(content, _siteContext.CurrentSiteId, _cts.Token);
            if (!result.IsSuccess)
            {
                _lblStatus.Text = $"❌ {result.ErrorCode}: {result.ErrorMessage}";
                _lblStatus.ForeColor = AgrisTheme.Danger;
                return;
            }

            _currentTripId = result.TripId;
            _currentFrmLenhDonId = result.FrmLenhDonId;
            _currentFrmMaLenhDon = result.FrmMaLenhDon;
            _currentFrmLenhDonBu = result.FrmLenhDonBu;
            _txtTripNo.Text = result.TripNo ?? "—";
            _txtPlate.Text = result.Plate ?? "—";
            _txtOwner.Text = result.CustomerOrOwner ?? "—";
            _lblStatus.Text = $"✓ Chuyến {result.TripNo} — {result.Plate}";
            _lblStatus.ForeColor = AgrisTheme.Success;
        }
        catch (Exception ex)
        {
            _lblStatus.Text = $"Lỗi: {ex.Message}";
            _lblStatus.ForeColor = AgrisTheme.Danger;
        }
    }

    /// <summary>Load tất cả CCS samples trong khung ngày.</summary>
    private async Task LoadAllCcsAsync()
    {
        try
        {
            var fromUtc = _dtpHistFrom.Value.Date.ToUniversalTime();
            var toUtc = _dtpHistTo.Value.Date.AddDays(1).ToUniversalTime();
            var fromDto = new DateTimeOffset(fromUtc, TimeSpan.Zero);
            var toDto = new DateTimeOffset(toUtc, TimeSpan.Zero);

            var samples = await _db.QualitySamples
                .Where(s => s.Type == TTC.Weighing.Domain.Enums.QualitySampleType.Ccs)
                .Where(s => s.ReceivedAt >= fromDto && s.ReceivedAt < toDto)
                .Where(s => _db.Trips.Any(t => t.Id == s.TripId && t.SiteId == _siteContext.CurrentSiteId && t.DeletedAt == null))
                .OrderByDescending(s => s.ReceivedAt)
                .Join(_db.Trips, s => s.TripId, t => t.Id, (s, t) => new { s, t })
                .ToListAsync(_cts.Token);

            _grid.SuspendLayout();
            _grid.Rows.Clear();
            foreach (var row in samples)
            {
                _grid.Rows.Add(
                    row.t.TripNo,
                    row.t.TractorPlate,
                    "", "", "", // Brix, Pol, Pol CC — chưa lưu riêng trong QualitySample
                    row.s.CcsValue?.ToString("N2") ?? (row.s.Status.ToString() == "Pending" ? "Đang chờ" : "—"),
                    row.s.Status.ToString(),
                    row.s.ReceivedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "");
            }
            _grid.ResumeLayout();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi tải lịch sử CCS: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>Click dòng grid CCS → load thông tin chuyến + số liệu CCS lên form để xem/in lại.</summary>
    private async void OnCcsGridCellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        var row = _grid.Rows[e.RowIndex];
        var tripNo = row.Cells["TripNo"].Value as string;
        if (string.IsNullOrWhiteSpace(tripNo)) return;

        // Load trip info
        _txtBarcode.Text = tripNo;
        _lastQrContent = tripNo;
        await ScanAndLoadTripAsync(tripNo);

        // Điền CCS từ grid (cột index 5 = CCS)
        var ccsText = row.Cells[5].Value?.ToString();
        if (decimal.TryParse(ccsText, out var ccs))
        {
            _numCcs.Value = Math.Min(_numCcs.Maximum, Math.Max(_numCcs.Minimum, ccs));
            _chkCcsPending.Checked = false;
        }
        else
        {
            _chkCcsPending.Checked = true;
        }

        // Lock: đây là dữ liệu đã ghi — chỉ xem/in, không cho sửa
        SetFieldsReadOnly(true);
        _lblStatus.Text = $"📋 Xem lại chuyến {tripNo} — bấm In phiếu hoặc Lượt mới (Esc) để nhập mới";
        _lblStatus.ForeColor = AgrisTheme.Info;
    }

    /// <summary>Lock/unlock các ô nhập khi xem lại từ lịch sử vs nhập mới.</summary>
    private void SetFieldsReadOnly(bool readOnly)
    {
        _numBrix.Enabled = !readOnly;
        _numPol.Enabled = !readOnly;
        _numPolDoc.Enabled = !readOnly;
        _numCcs.Enabled = !readOnly && !_chkCcsPending.Checked;
        _numFibre.Enabled = !readOnly;
        _chkCcsPending.Enabled = !readOnly;
        _btnRecord.Enabled = !readOnly;
    }

    /// <summary>Đẩy lại CCS lên DigiScale cho chuyến đang chọn.</summary>
    private async Task RetryPushDigiAsync()
    {
        if (_currentTripId is null)
        {
            MessageBox.Show("Chọn chuyến trước khi đẩy lại.", "Thiếu thông tin",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var tripNo = _txtTripNo.Text;
        var plate = _txtPlate.Text;
        var brix = _numBrix.Value;
        var pol = _numPol.Value;
        var polDoc = _numPolDoc.Value;
        var ccs = _numCcs.Value;

        _btnRetryDigi.Enabled = false;
        _lblStatus.Text = "⏳ Đang đẩy lại CCS lên DigiScale...";
        _lblStatus.ForeColor = AgrisTheme.Info;

        try
        {
            var ld = _presenter.FindLenhDonByPlate(plate);
            var pushResult = await _presenter.PushCcsAsync(
                _currentFrmLenhDonId ?? ld?.SourceKey ?? tripNo,
                _currentFrmMaLenhDon ?? ld?.MaLD ?? tripNo,
                "0", "1", DateTime.Now,
                brix.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                ccs.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                pol.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                polDoc.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                "1",
                Program.LoggedInUserName,
                _currentFrmLenhDonBu ?? ld?.BusinessUnit,
                _cts.Token);

            if (pushResult.IsSuccess)
            {
                _lblStatus.Text = $"✅ Đẩy DigiScale thành công cho {tripNo}";
                _lblStatus.ForeColor = AgrisTheme.Success;
                Helpers.DigiScalePopup.Show(this,
                    "ĐẨY LẠI THÀNH CÔNG",
                    $"Chuyến: {tripNo}\nBiển số: {plate}\nCCS: {ccs:N2}",
                    true, "OK");
            }
            else
            {
                _lblStatus.Text = $"❌ Đẩy lại thất bại: {pushResult.ErrorMessage}";
                _lblStatus.ForeColor = AgrisTheme.Danger;
                Helpers.DigiScalePopup.Show(this,
                    "ĐẨY LẠI THẤT BẠI",
                    $"Chuyến: {tripNo}\nBiển số: {plate}\nCCS: {ccs:N2}",
                    false, pushResult.ErrorMessage ?? "Lỗi không xác định");
            }
        }
        catch (Exception ex)
        {
            _lblStatus.Text = $"❌ Lỗi: {ex.Message}";
            _lblStatus.ForeColor = AgrisTheme.Danger;
            MessageBox.Show($"Lỗi đẩy lại DigiScale:\n{ex.Message}", "Lỗi",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnRetryDigi.Enabled = true;
        }
    }

    private void PrintCcsTicket()
    {
        if (_currentTripId is null)
        {
            MessageBox.Show(Loc.T("ccs.print_no_trip"),
                Loc.T("quality.no_trip_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var tripNo = _txtTripNo.Text;
        var plate = _txtPlate.Text;
        var owner = _txtOwner.Text;
        var brix = _numBrix.Value;
        var pol = _numPol.Value;
        var polDoc = _numPolDoc.Value;
        var ccsVal = _chkCcsPending.Checked ? (decimal?)null : _numCcs.Value;
        var isPending = _chkCcsPending.Checked;

        // GDI+ preview
        using var previewForm = new Form
        {
            Text = Loc.T("ccs.print_form_title", tripNo),
            ClientSize = new Size(720, 720),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false,
            BackColor = AgrisTheme.Canvas,
            Font = AgrisTheme.Body()
        };

        var pnlPreview = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(230, 230, 230),
            Padding = new Padding(15)
        };

        var receiptPanel = new Panel
        {
            Size = new Size(660, 560),
            BackColor = Color.White,
            Location = new Point(10, 10)
        };
        receiptPanel.GetType().GetProperty("DoubleBuffered",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(receiptPanel, true);

        receiptPanel.Paint += (_, pe) =>
        {
            var g = pe.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var pw = receiptPanel.Width;
            var ph = receiptPanel.Height;
            var m = 28;
            var cw = pw - m * 2;
            var y = m;

            // Viền
            using var borderPen = new Pen(AgrisTheme.BrandGreenDark, 2f);
            g.DrawRectangle(borderPen, 2, 2, pw - 4, ph - 4);

            // Tiêu đề
            using var titleFont = new Font(AgrisTheme.FontFamily, 18F, FontStyle.Bold);
            using var titleBrush = new SolidBrush(AgrisTheme.BrandGreenDark);
            var titleText = Loc.T("ccs.print_title");
            var titleSz = g.MeasureString(titleText, titleFont);
            g.DrawString(titleText, titleFont, titleBrush, m + (cw - titleSz.Width) / 2, y);
            y += (int)titleSz.Height + 14;

            // Fields
            using var lbl = new Font(AgrisTheme.FontFamily, 11F);
            using var val = new Font(AgrisTheme.FontFamily, 11F, FontStyle.Bold);
            using var tb = new SolidBrush(AgrisTheme.TextPrimary);
            var lx = m;
            var vx = m + 160;
            var lh = 28;

            DrawF(g, Loc.T("ccs.print_label_reg_no"), tripNo, lbl, val, tb, lx, vx, ref y, lh);
            DrawF(g, Loc.T("ccs.print_label_plate"), plate, lbl, val, tb, lx, vx, ref y, lh);
            DrawF(g, Loc.T("ccs.print_label_customer"), owner, lbl, val, tb, lx, vx, ref y, lh);

            // Separator
            y += 6;
            using var sep = new Pen(AgrisTheme.Border, 1f);
            g.DrawLine(sep, m, y, pw - m, y);
            y += 10;

            // CCS values — big
            using var big = new Font(AgrisTheme.FontFamily, 14F, FontStyle.Bold);
            DrawF(g, Loc.T("ccs.print_label_brix"), $"{brix:N2}", lbl, big, tb, lx, vx, ref y, 32);
            DrawF(g, Loc.T("ccs.print_label_pol"), $"{pol:N2}", lbl, big, tb, lx, vx, ref y, 32);
            DrawF(g, Loc.T("ccs.print_label_pol_doc"), $"{polDoc:N2}", lbl, big, tb, lx, vx, ref y, 32);

            // CCS — extra large, orange
            using var ccsFont = new Font(AgrisTheme.FontFamily, 18F, FontStyle.Bold);
            using var ccsBrush = new SolidBrush(Color.FromArgb(180, 80, 0));
            var ccsText = isPending ? Loc.T("ccs.pending_value") : $"{ccsVal:N2}";
            DrawF(g, Loc.T("ccs.print_label_ccs"), ccsText, lbl, ccsFont, ccsBrush, lx, vx, ref y, 36);

            // Separator
            y += 6;
            g.DrawLine(sep, m, y, pw - m, y);
            y += 10;

            // Time
            DrawF(g, Loc.T("ccs.print_label_time"), DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"), lbl, val, tb, lx, vx, ref y, lh);

            // Barcode
            if (!string.IsNullOrWhiteSpace(tripNo))
            {
                y += 10;
                Printing.Code128Renderer.Draw(g, tripNo, lx, y, 320, 50);
                y += 54;
                using var bcFont = new Font(AgrisTheme.MonoFamily, 10F);
                g.DrawString(tripNo, bcFont, Brushes.Black, lx, y);
            }

            // Ngày in
            var printLabel = Loc.T("print.dangtai_ngay_in", DateTime.Now.ToString("dd/MM/yyyy HH:mm"));
            using var tsFont = new Font(AgrisTheme.FontFamily, 9.5F, FontStyle.Italic);
            using var tsBrush = new SolidBrush(AgrisTheme.TextMuted);
            var tsSz = g.MeasureString(printLabel, tsFont);
            g.DrawString(printLabel, tsFont, tsBrush, pw - m - tsSz.Width, ph - m - tsSz.Height - 4);
        };

        pnlPreview.Controls.Add(receiptPanel);

        var btnPrint = GridBuilder.PrimaryButton(Loc.T("common.print"), new Size(120, 36));
        var btnClose = GridBuilder.SecondaryButton(Loc.T("common.close"), new Size(100, 36));
        btnPrint.Click += (_, _) =>
        {
            try
            {
                var printDoc = new System.Drawing.Printing.PrintDocument();
                var capturedContent = $"{Loc.T("ccs.print_title")}\n{tripNo} | {plate} | CCS: {(isPending ? "Pending" : $"{ccsVal:N2}")}";
                printDoc.PrintPage += (s, pe) =>
                    pe.Graphics!.DrawString(capturedContent, AgrisTheme.Mono(10F), Brushes.Black,
                        new RectangleF(40, 40, pe.PageBounds.Width - 80, pe.PageBounds.Height - 80));
                var dialog = new PrintDialog { Document = printDoc };
                if (dialog.ShowDialog() == DialogResult.OK) printDoc.Print();
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.T("ccs.print_error", ex.Message), Loc.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        btnClose.Click += (_, _) => previewForm.Close();

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 50,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8), BackColor = AgrisTheme.SurfaceMuted
        };
        toolbar.Controls.Add(btnClose);
        toolbar.Controls.Add(btnPrint);

        previewForm.Controls.Add(pnlPreview);
        previewForm.Controls.Add(toolbar);
        previewForm.ShowDialog(this);
    }

    private static void DrawF(Graphics g, string label, string? value,
        Font lf, Font vf, Brush b, int lx, int vx, ref int y, int lh)
    {
        g.DrawString(label, lf, b, lx, y);
        g.DrawString(": " + (string.IsNullOrWhiteSpace(value) ? "—" : value), vf, b, vx, y);
        y += lh;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _cts.Dispose();
        base.Dispose(disposing);
    }
}
