using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.UseCases.Quality;
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
    private readonly TripEventBus _eventBus;
    private readonly CancellationTokenSource _cts = new();

    private Guid? _currentTripId;
    private string? _lastQrContent;

    // ── QR Scan ──
    private readonly TextBox _txtBarcode = new()
    {
        Dock = DockStyle.Fill,
        Font = AgrisTheme.Mono(12F),
        PlaceholderText = "Nhập mã đăng tài hoặc biển số xe...",
        AccessibleName = "Barcode scan"
    };

    // ── Thông tin chuyến ──
    private readonly TextBox _txtTripNo = ReadOnlyField("Số đăng tài");
    private readonly TextBox _txtPlate = ReadOnlyField("Biển số");
    private readonly TextBox _txtOwner = ReadOnlyField("Khách hàng");

    // ── CCS ──
    private readonly NumericUpDown _numCcs = new()
    {
        DecimalPlaces = 2, Maximum = 30m, Minimum = 0m, Increment = 0.1m, Width = 160,
        Font = AgrisTheme.Body(14F)
    };
    private readonly CheckBox _chkCcsPending = new()
    {
        Text = "Nguồn báo đang chờ kết quả",
        AutoSize = true
    };
    private readonly Button _btnRecord = GridBuilder.PrimaryButton("Ghi nhận CCS", new Size(200, 36));
    private readonly Button _btnReset = GridBuilder.SecondaryButton("Lượt mới (Esc)", new Size(130, 36));

    // ── Lịch sử ──
    private readonly DataGridView _grid = GridBuilder.ReadOnlyGrid("Lịch sử CCS");

    // ── Status ──
    private readonly Label _lblStatus = new()
    {
        Text = "Nhập mã đăng tài hoặc biển số để bắt đầu",
        Font = AgrisTheme.Body(10F, FontStyle.Italic),
        ForeColor = AgrisTheme.BrandGreen,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft
    };

    public CcsForm(QualityPresenter presenter, ISiteContext siteContext, TripEventBus eventBus)
    {
        _presenter = presenter;
        _siteContext = siteContext;
        _eventBus = eventBus;

        Text = "Đo chữ đường (CCS)";
        MinimumSize = new Size(800, 500);
        BackColor = AgrisTheme.Canvas;
        DoubleBuffered = true;

        BuildLayout();

        _txtBarcode.KeyDown += OnBarcodeKeyDown;
        _btnRecord.Click += async (_, _) => await RecordCcsAsync();
        _btnReset.Click += (_, _) => ResetForm();
        _chkCcsPending.CheckedChanged += (_, _) => _numCcs.Enabled = !_chkCcsPending.Checked;
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
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // ── Cột trái ──
        var leftCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(1), AutoScroll = true };
        var header = GridBuilder.SectionHeader("Đo chữ đường (CCS)");
        var table = FormLayout.FieldTable();

        FormLayout.AddFullWidth(table, _txtBarcode);
        FormLayout.AddFullWidth(table, _lblStatus);
        FormLayout.AddField(table, "Số đăng tài", _txtTripNo);
        FormLayout.AddField(table, "Biển số", _txtPlate);
        FormLayout.AddField(table, "Khách hàng", _txtOwner);

        var ccsLabel = new Label { Text = "GIÁ TRỊ CCS", Font = AgrisTheme.Body(11F, FontStyle.Bold), AutoSize = true };
        FormLayout.AddFullWidth(table, ccsLabel, 30);
        FormLayout.AddField(table, "Chữ đường", _numCcs);
        FormLayout.AddFullWidth(table, _chkCcsPending, 30);
        FormLayout.AddFullWidth(table, FormLayout.ActionBar(_btnRecord, _btnReset), 52);

        leftCard.Controls.Add(table);
        leftCard.Controls.Add(header);
        root.Controls.Add(leftCard, 0, 0);

        // ── Cột phải: lịch sử ──
        var rightCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(1) };
        var histHeader = GridBuilder.SectionHeader("Lịch sử CCS");
        _grid.Dock = DockStyle.Fill;
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "Số đăng tài", Width = 120 },
            new DataGridViewTextBoxColumn { HeaderText = "Biển số", Width = 90 },
            new DataGridViewTextBoxColumn { HeaderText = "CCS", Width = 70 },
            new DataGridViewTextBoxColumn { HeaderText = "Trạng thái", Width = 80 },
            new DataGridViewTextBoxColumn { HeaderText = "Thời gian", Width = 140 });
        rightCard.Controls.Add(_grid);
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
                _chkCcsPending.Checked ? "Đang chờ" : $"{_numCcs.Value:N2}",
                result.Status.ToString(), DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"));

            // Push CCS lên DigiScale
            try
            {
                await _presenter.PushCcsAsync(
                    _txtTripNo.Text, "0", "1", DateTime.Now,
                    "0", _numCcs.Value.ToString("F2"), "0", "0", "1",
                    _cts.Token);
            }
            catch { /* Push thất bại không chặn flow */ }

            _eventBus.Publish(new TripChangedEventArgs
            {
                TripId = _currentTripId.Value,
                TripNo = _txtTripNo.Text,
                Plate = _txtPlate.Text,
                QrContent = _lastQrContent,
                ChangeType = TripChangeType.CcsReceived,
                SourceForm = "CcsForm"
            });
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
        _txtBarcode.Clear();
        _txtTripNo.Clear();
        _txtPlate.Clear();
        _txtOwner.Clear();
        _numCcs.Value = 0;
        _chkCcsPending.Checked = false;
        _lblStatus.Text = "Nhập mã đăng tài hoặc biển số để bắt đầu";
        _lblStatus.ForeColor = AgrisTheme.BrandGreen;
        _txtBarcode.Focus();
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
