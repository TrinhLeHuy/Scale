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
/// Đo chữ đường (CCS). Bố cục từ trên xuống theo đúng thứ tự thao tác:
/// ① Tìm chuyến → ② Nhập Bx, Pol → ③ CCS + tạp chất THỰC TẾ (tự tính) → ④ CCS + tạp chất THANH TOÁN (FRM).
/// Không hiển thị thông tin khách hàng / phiếu cân. Hệ số xơ (Fibre) ẩn — lấy từ Portal,
/// chỉ tài khoản có quyền mới cài được (nút "Hệ số xơ").
/// </summary>
public sealed class CcsForm : Form
{
    // Glyph font icon Windows (Segoe Fluent Icons / MDL2 Assets).
    private const string GLYPH_SEARCH = "\uE721";
    private const string GLYPH_LAB = "\uE9D9";
    private const string GLYPH_CALC = "\uE8EF";
    private const string GLYPH_PAYMENT = "\uE8C7";
    private const string GLYPH_HISTORY = "\uE81C";
    private const string GLYPH_CAR = "\uE804";

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
    private bool _paymentBusy;

    // Số liệu đang hiển thị — dùng để in phiếu (không đọc lại chữ trên ô).
    private decimal? _actualImpurity;
    private decimal? _paidCcs;
    private decimal? _paidImpurity;
    private string? _paidPolicy;

    // ── Tìm chuyến ──
    private readonly TextBox _txtBarcode = new()
    {
        Dock = DockStyle.Fill,
        Font = AgrisTheme.Mono(12F),
        BackColor = Color.FromArgb(255, 255, 220),
        BorderStyle = BorderStyle.FixedSingle,
        PlaceholderText = Loc.T("ccs.ux_search_placeholder"),
        AccessibleName = Loc.T("ccs.ux_search_placeholder"),
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

    /// <summary>Chip nhận diện chuyến đang đo (số đăng tài · biển số) — thay cho khối thông tin chuyến.</summary>
    private readonly ChipLabel _chipTrip = new()
    {
        IconGlyph = GLYPH_CAR,
        ForeColor = AgrisTheme.BrandTealDark,
        ChipBackColor = AgrisTheme.InfoSoft,
        ChipBorderColor = Color.FromArgb(190, 215, 235),
        Visible = false,
        Margin = new Padding(0, 6, 0, 0)
    };

    // Giữ số đăng tài / biển số / khách hàng cho in phiếu, đẩy DigiScale — KHÔNG hiển thị trên màn hình.
    private readonly TextBox _txtTripNo = new();
    private readonly TextBox _txtPlate = new();
    private readonly TextBox _txtOwner = new();

    // ── Bx, Pol: đọc từ máy đo, hoặc nhập tay khi quản lý duyệt ──
    private readonly LockableNumericUpDown _numBrix = new() { DecimalPlaces = 2, Maximum = 40m, Minimum = 0m, Increment = 0.01m };
    private readonly LockableNumericUpDown _numPol = new() { DecimalPlaces = 2, Maximum = 100m, Minimum = 0m, Increment = 0.01m };
    private FieldTile? _tileBrix;
    private FieldTile? _tilePol;

    /// <summary>Nguồn của từng số đo — lưu kèm kết quả CCS để truy vết.</summary>
    private enum ReadingSource { None, Device, Manual }
    private ReadingSource _brixSource;
    private ReadingSource _polSource;
    /// <summary>Quản lý đã duyệt nhập tay cho chuyến đang đo (null = chưa duyệt, ô bị khoá).</summary>
    private ApprovalResult? _manualApproval;
    /// <summary>Đang gán số bằng code (máy đo / reset) — không tính là nhập tay.</summary>
    private bool _settingReading;
    /// <summary>Đang xem lại dữ liệu đã ghi — khoá mọi thao tác nhập.</summary>
    private bool _fieldsReadOnly;

    private readonly Button _btnReadBrix = GridBuilder.SecondaryButton("⇩  " + Loc.T("ccs.ux_btn_read_brix"), new Size(170, 40));
    private readonly Button _btnReadPol = GridBuilder.SecondaryButton("⇩  " + Loc.T("ccs.ux_btn_read_pol"), new Size(170, 40));
    private readonly Button _btnManual = GridBuilder.SecondaryButton("🔒  " + Loc.T("ccs.ux_btn_manual"), new Size(220, 40));

    // ── Giá trị tự tính / ẩn (không nằm trên màn hình) ──
    private readonly NumericUpDown _numPolDoc = new() { DecimalPlaces = 4, Maximum = 100m, Minimum = 0m };
    private readonly NumericUpDown _numCcs = new() { DecimalPlaces = 2, Maximum = 30m, Minimum = 0m };
    /// <summary>Hệ số xơ — từ Portal; người có quyền cài qua nút "Hệ số xơ". Không hiển thị.</summary>
    private readonly NumericUpDown _numFibre = new() { DecimalPlaces = 2, Maximum = 20m, Minimum = 0m, Value = 0m };

    /// <summary>Lý do cụ thể chưa tính được CCS (null = đã tính được).</summary>
    private string? _ccsIssue;

    private readonly CheckBox _chkCcsPending = new()
    {
        Text = Loc.T("ccs.ui_pending"),
        AutoSize = true,
        Cursor = Cursors.Hand
    };

    // ── Kết quả thực tế / thanh toán ──
    private readonly MetricTile _tileCcsActual = new(Loc.T("ccs.ux_tile_ccs_actual"), AgrisTheme.BrandGreen, Loc.T("ccs.ux_note_enter_bx_pol"));
    private readonly MetricTile _tileTcActual = new(Loc.T("ccs.ux_tile_tc_actual"), AgrisTheme.BrandGreen, Loc.T("ccs.ux_note_tc_none"));
    private readonly MetricTile _tileCcsPaid = new(Loc.T("ccs.ux_tile_ccs_paid"), AgrisTheme.BrandTeal, Loc.T("ccs.ux_note_paid_waiting"));
    private readonly MetricTile _tileTcPaid = new(Loc.T("ccs.ux_tile_tc_paid"), AgrisTheme.BrandTeal, Loc.T("ccs.ux_note_paid_waiting"));

    private readonly IconSectionHeader _hdrInput = new(GLYPH_LAB, Loc.T("ccs.ux_input_title"), AgrisTheme.BrandGreen, Loc.T("ccs.ux_input_sub"));
    private readonly IconSectionHeader _hdrPayment = new(GLYPH_PAYMENT, Loc.T("ccs.ux_payment_title"), AgrisTheme.BrandTeal, Loc.T("ccs.ux_payment_sub"));
    private readonly Button _btnFibre = GridBuilder.SecondaryButton("⚙  " + Loc.T("ccs.ux_btn_fibre"), new Size(130, 32));
    private readonly Button _btnPayment = GridBuilder.SecondaryButton("↻  " + Loc.T("ccs.ux_btn_payment"), new Size(140, 32));

    private readonly Button _btnRecord = GridBuilder.PrimaryButton("✓  " + Loc.T("ccs.ux_btn_record"), new Size(200, 44));
    private readonly Button _btnPrint = GridBuilder.SecondaryButton("🖨  " + Loc.T("ccs.ux_btn_print"), new Size(120, 44));
    private readonly Button _btnRetryDigi = GridBuilder.SecondaryButton("⟳  " + Loc.T("ccs.ux_btn_retry"), new Size(150, 44));
    private readonly Button _btnReset = GridBuilder.SecondaryButton("↺  " + Loc.T("ccs.ux_btn_reset"), new Size(150, 44));

    // ── Ngày nhập CCS (KSK nhập CCS theo ngày): chọn ngày → danh sách xe + nhập CCS cho ngày đó ──
    private readonly DateTimePicker _dtpCcsDate = new()
    {
        Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 124,
        Font = AgrisTheme.Body(10.5F, FontStyle.Bold)
    };
    private readonly Button _btnToday = GridBuilder.SecondaryButton(Loc.T("ccs.ux_today"), new Size(96, 32));
    /// <summary>Đang đổi ngày bằng code (quét xe khác ngày) — không reset lượt đang nhập.</summary>
    private bool _suppressDateChange;

    // ── Danh sách xe trong ngày + CCS ──
    private readonly DataGridView _grid = GridBuilder.ReadOnlyGrid("CCS theo ngày");
    private readonly IconSectionHeader _hdrDay = new(GLYPH_HISTORY, Loc.T("ccs.ux_day_title"), AgrisTheme.BrandTeal, " ");
    private readonly CheckBox _chkOnlyMissing = new()
    {
        Text = Loc.T("ccs.ux_only_missing"),
        AutoSize = true,
        Cursor = Cursors.Hand,
        Margin = new Padding(0, 6, 0, 0)
    };

    /// <summary>Dải trạng thái — nền tự đổi theo màu chữ (xanh = sẵn sàng, đỏ = lỗi, xanh biển = đang xử lý).</summary>
    private readonly Label _lblStatus = new NoticeLabel
    {
        Text = Loc.T("ccs.ux_start"),
        Font = AgrisTheme.Body(9.5F, FontStyle.Bold),
        ForeColor = AgrisTheme.BrandGreen,
        BackColor = AgrisTheme.SuccessSoft,
        Dock = DockStyle.Top,
        Height = 42,
        Padding = new Padding(14, 0, 8, 0),
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true,
        AccessibleRole = AccessibleRole.StatusBar
    };

    public CcsForm(QualityPresenter presenter, ISiteContext siteContext, ITripRegistrationQueries tripRegistrationQueries, TTC.Weighing.Infrastructure.Persistence.WeighingDbContext db, TripEventBus eventBus)
    {
        _presenter = presenter;
        _siteContext = siteContext;
        _tripRegistrationQueries = tripRegistrationQueries;
        _db = db;
        _eventBus = eventBus;

        Text = Loc.T("ccs.ux_form_title");
        MinimumSize = new Size(900, 600);
        BackColor = AgrisTheme.Canvas;
        DoubleBuffered = true;

        BuildLayout();

        _txtBarcode.KeyDown += OnBarcodeKeyDown;
        _btnRecord.Click += async (_, _) => await RecordCcsAsync();
        _btnPrint.Click += (_, _) => PrintCcsTicket();
        _btnRetryDigi.Click += async (_, _) => await RetryPushDigiAsync();
        _btnReset.Click += (_, _) => ResetForm();
        _btnFibre.Click += (_, _) => EditFibre();
        _btnPayment.Click += async (_, _) => await FetchPaymentFiguresAsync(showDialogOnError: true);
        _chkCcsPending.CheckedChanged += (_, _) => AutoCalcCcs();

        _lblStatus.ForeColorChanged += (_, _) => _lblStatus.BackColor = SoftBackground(_lblStatus.ForeColor);
        Load += async (_, _) =>
        {
            await ApplyPortalRulesAsync();
            await LoadAllCcsAsync();
        };

        SetTripLoaded(false);
    }

    private void BuildLayout()
    {
        // 2 cột: trái = luồng nhập (cuộn được) + thanh nút ghim đáy; phải = lịch sử.
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(10)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var left = new Panel { Dock = DockStyle.Fill, BackColor = AgrisTheme.Canvas, Margin = new Padding(0, 0, 10, 0) };
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = AgrisTheme.Canvas };

        var actionCard = new Card { Dock = DockStyle.Bottom, Height = 72, Padding = new Padding(1) };
        actionCard.Controls.Add(BuildActionBar());

        // Thứ tự Add ngược thứ tự hiển thị (Dock = Top).
        scroll.Controls.Add(BuildPaymentCard());
        scroll.Controls.Add(Gap());
        scroll.Controls.Add(BuildActualCard());
        scroll.Controls.Add(Gap());
        scroll.Controls.Add(BuildInputCard());
        scroll.Controls.Add(Gap());
        scroll.Controls.Add(BuildSearchCard());

        left.Controls.Add(scroll);
        left.Controls.Add(Gap(DockStyle.Bottom));
        left.Controls.Add(actionCard);
        root.Controls.Add(left, 0, 0);
        root.Controls.Add(BuildHistoryCard(), 1, 0);

        Controls.Add(root);
    }

    private static Panel Gap(DockStyle dock = DockStyle.Top)
        => new() { Dock = dock, Height = 10, BackColor = AgrisTheme.Canvas };

    /// <summary>
    /// Thanh nút ghim đáy: nút chính rộng hơn, 3 nút phụ chia đều — co giãn theo bề ngang,
    /// luôn nằm một hàng (không xuống dòng bị cắt ở màn hình nhỏ).
    /// </summary>
    private TableLayoutPanel BuildActionBar()
    {
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Padding = new Padding(12, 13, 12, 13),
            BackColor = AgrisTheme.Surface
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 23));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 23));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 23));
        bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Button[] buttons = [_btnRecord, _btnPrint, _btnRetryDigi, _btnReset];
        for (var i = 0; i < buttons.Length; i++)
        {
            buttons[i].Dock = DockStyle.Fill;
            buttons[i].MinimumSize = new Size(0, 0);
            buttons[i].Margin = new Padding(i == 0 ? 0 : 8, 0, 0, 0);
            bar.Controls.Add(buttons[i], i, 0);
        }
        return bar;
    }

    /// <summary>① Tìm chuyến — ô quét lớn, khung ngày danh sách xe, chip chuyến đang đo, dải trạng thái.</summary>
    private Card BuildSearchCard()
    {
        var card = new Card { Dock = DockStyle.Top, Padding = new Padding(1) };
        var header = new IconSectionHeader(GLYPH_SEARCH, Loc.T("ccs.ux_search_title"), AgrisTheme.BrandTeal, Loc.T("ccs.ux_search_sub"));

        var body = new Panel { Dock = DockStyle.Top, Padding = new Padding(14, 12, 14, 12), BackColor = AgrisTheme.Surface };

        var barcode = BuildBarcodeWithDropdown();
        barcode.Dock = DockStyle.Top;
        _btnTripDropdown.Width = 48;
        _btnTripDropdown.AccessibleName = Loc.T("ccs.ui_trip_dates");
        barcode.Height = Math.Max(44, _txtBarcode.PreferredHeight);

        // Một ngày nhập CCS (không phải khoảng ngày): danh sách xe, ô chọn xe và lượt nhập đều theo ngày này.
        var tripBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 50,
            FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            Padding = new Padding(0, 8, 0, 0), BackColor = AgrisTheme.Surface
        };
        _dtpCcsDate.Value = DateTime.Today;
        _dtpCcsDate.MaxDate = DateTime.Today.AddDays(1);
        _dtpCcsDate.Margin = new Padding(0, 2, 8, 0);
        _btnToday.Margin = new Padding(0, 0, 0, 0);
        _btnToday.MaximumSize = new Size(100, 36);
        tripBar.Controls.AddRange([
            new Label { Text = "📅 " + Loc.T("ccs.ux_ccs_date"), AutoSize = true, Margin = new Padding(0, 8, 8, 0),
                Font = AgrisTheme.Body(9F, FontStyle.Bold), ForeColor = AgrisTheme.TextSecondary },
            _dtpCcsDate,
            _btnToday
        ]);
        _dtpCcsDate.ValueChanged += async (_, _) => await OnCcsDateChangedAsync();
        _btnToday.Click += (_, _) => _dtpCcsDate.Value = DateTime.Today;

        // Chip chuyến đang đo nằm bên phải tiêu đề "Tìm chuyến".
        _chipTrip.Margin = new Padding(0, 0, 0, 0);
        header.Actions.Padding = new Padding(0, 14, 12, 0);
        header.Actions.Controls.Add(_chipTrip);

        var statusGap = new Panel { Dock = DockStyle.Top, Height = 8, BackColor = AgrisTheme.Surface };

        body.Controls.Add(_lblStatus);
        body.Controls.Add(statusGap);
        body.Controls.Add(tripBar);
        body.Controls.Add(barcode);
        body.Height = barcode.Height + tripBar.Height + statusGap.Height + _lblStatus.Height + body.Padding.Vertical;

        card.Controls.Add(body);
        card.Controls.Add(header);
        card.Height = header.Height + body.Height + card.Padding.Vertical;
        return card;
    }

    /// <summary>② Kết quả phân tích — chỉ nhập tay Bx và Pol. Enter: Bx → Pol → Ghi nhận.</summary>
    private Card BuildInputCard()
    {
        var card = new Card { Dock = DockStyle.Top, Padding = new Padding(1) };

        // Nút "Hệ số xơ" chỉ hiện với tài khoản được phép (hoặc cần quản lý xác nhận khi chưa cấu hình quyền).
        _btnFibre.Visible = SugarcaneFlowSettings.Current.FibreAccess(Program.LoggedInRole, Program.LoggedInPermissions)
                            != FibreEditAccess.Denied;
        _btnFibre.Margin = new Padding(0);
        _hdrInput.Actions.Controls.Add(_btnFibre);

        _tileBrix = new FieldTile(Loc.T("ccs.print_label_brix"), _numBrix, Loc.T("ccs.ux_hint_bx"));
        _tilePol = new FieldTile(Loc.T("ccs.print_label_pol"), _numPol, Loc.T("ccs.ux_hint_pol"));
        var inputRow = TileRow([_tileBrix, _tilePol], [50, 50]);

        FieldTile.ConfigureNumeric(_numBrix, () => _numPol);
        FieldTile.ConfigureNumeric(_numPol, () => _btnRecord);
        _numBrix.ValueChanged += (_, _) => OnReadingEdited(CcsInstrument.Brix);
        _numPol.ValueChanged += (_, _) => OnReadingEdited(CcsInstrument.Pol);
        _numFibre.ValueChanged += (_, _) => AutoCalcCcs();

        // Nguồn số đo: máy Brix / máy Pol, hoặc nhập tay (quản lý duyệt bằng tài khoản + mật khẩu).
        var sourceRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 58,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(14, 6, 14, 6),
            BackColor = AgrisTheme.Surface
        };
        foreach (var b in new[] { _btnReadBrix, _btnReadPol, _btnManual })
        {
            b.Margin = new Padding(0, 0, 10, 0);
            sourceRow.Controls.Add(b);
        }
        _btnManual.ForeColor = AgrisTheme.Warning;
        _btnManual.FlatAppearance.BorderColor = AgrisTheme.Warning;
        _btnReadBrix.Click += async (_, _) => await ReadFromDeviceAsync(CcsInstrument.Brix);
        _btnReadPol.Click += async (_, _) => await ReadFromDeviceAsync(CcsInstrument.Pol);
        _btnManual.Click += (_, _) => RequestManualEntry();
        ResetReadings();

        var pendingRow = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(14, 4, 14, 0), BackColor = AgrisTheme.Surface };
        _chkCcsPending.Location = new Point(16, 8);
        _chkCcsPending.ForeColor = AgrisTheme.TextSecondary;
        pendingRow.Controls.Add(_chkCcsPending);

        card.Controls.Add(pendingRow);
        card.Controls.Add(sourceRow);
        card.Controls.Add(inputRow);
        card.Controls.Add(_hdrInput);
        card.Height = _hdrInput.Height + inputRow.Height + sourceRow.Height + pendingRow.Height + card.Padding.Vertical + 4;
        return card;
    }

    /// <summary>Về trạng thái chưa đo: số = 0, khoá ô, bỏ phê duyệt nhập tay của chuyến trước.</summary>
    private void ResetReadings()
    {
        _settingReading = true;
        try
        {
            _numBrix.Value = 0;
            _numPol.Value = 0;
        }
        finally { _settingReading = false; }

        _brixSource = ReadingSource.None;
        _polSource = ReadingSource.None;
        _manualApproval = null;
        _numBrix.Locked = true;
        _numPol.Locked = true;
        RefreshReadingUi();
        AutoCalcCcs();
    }

    private void OnReadingEdited(CcsInstrument instrument)
    {
        if (!_settingReading)
        {
            var num = instrument == CcsInstrument.Brix ? _numBrix : _numPol;
            if (!num.Locked && _manualApproval is not null)
            {
                if (instrument == CcsInstrument.Brix) _brixSource = ReadingSource.Manual;
                else _polSource = ReadingSource.Manual;
                RefreshReadingUi();
            }
        }

        AutoCalcCcs();
    }

    /// <summary>Đọc nguồn số đo từ Reason đã lưu ("Brix=Manual; Pol=Device"). Không rõ → coi là thiết bị.</summary>
    private static ReadingSource SourceFromReason(string? reason, string name)
    {
        var part = (reason ?? "").Split(';', StringSplitOptions.TrimEntries)
            .FirstOrDefault(p => p.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase));
        return part is not null && part.EndsWith("Manual", StringComparison.OrdinalIgnoreCase)
            ? ReadingSource.Manual
            : ReadingSource.Device;
    }

    /// <summary>Badge nguồn trên từng ô + trạng thái nút theo chuyến / phê duyệt.</summary>
    private void RefreshReadingUi()
    {
        void Badge(FieldTile? tile, ReadingSource source)
        {
            if (tile is null) return;
            switch (source)
            {
                case ReadingSource.Device:
                    tile.SetBadge(Loc.T("ccs.ux_badge_device"), AgrisTheme.SuccessSoft, AgrisTheme.Success);
                    break;
                case ReadingSource.Manual:
                    tile.SetBadge(Loc.T("ccs.ux_badge_manual"), AgrisTheme.WarningSoft, AgrisTheme.Warning);
                    break;
                default:
                    tile.SetBadge(Loc.T("ccs.ux_badge_none"), AgrisTheme.SurfaceMuted, AgrisTheme.TextMuted);
                    break;
            }
        }

        Badge(_tileBrix, _brixSource);
        Badge(_tilePol, _polSource);

        var canMeasure = _currentTripId is not null && !_fieldsReadOnly;
        _btnReadBrix.Enabled = canMeasure;
        _btnReadPol.Enabled = canMeasure;
        _btnManual.Enabled = canMeasure && _manualApproval is null;
        _btnManual.Text = _manualApproval is null
            ? "🔒  " + Loc.T("ccs.ux_btn_manual")
            : "🔓  " + Loc.T("ccs.ux_btn_manual_approved", ApproverName(_manualApproval));
    }

    /// <summary>Đọc một lần số Brix hoặc Pol từ máy đo. Không đọc được → báo rõ, không tự điền số.</summary>
    private async Task ReadFromDeviceAsync(CcsInstrument instrument)
    {
        if (_currentTripId is null)
        {
            MessageBox.Show(Loc.T("ccs.ux_need_trip"), Loc.T("common.missing_info"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var name = instrument == CcsInstrument.Brix ? "Brix" : "Pol";
        var button = instrument == CcsInstrument.Brix ? _btnReadBrix : _btnReadPol;
        var num = instrument == CcsInstrument.Brix ? _numBrix : _numPol;

        button.Enabled = false;
        _lblStatus.Text = Loc.T("ccs.ux_device_reading", name);
        _lblStatus.ForeColor = AgrisTheme.Info;
        try
        {
            var reading = await _presenter.ReadCcsInstrumentAsync(instrument, _cts.Token);
            if (reading.Value is not { } value || value < num.Minimum || value > num.Maximum)
            {
                _lblStatus.Text = Loc.T("ccs.ux_device_unavailable", name);
                _lblStatus.ForeColor = AgrisTheme.Danger;
                return;
            }

            _settingReading = true;
            try { num.Value = value; }
            finally { _settingReading = false; }

            if (instrument == CcsInstrument.Brix) _brixSource = ReadingSource.Device;
            else _polSource = ReadingSource.Device;

            AutoCalcCcs();
            _lblStatus.Text = Loc.T("ccs.ux_device_read_ok", name, value.ToString("N2"));
            _lblStatus.ForeColor = AgrisTheme.Success;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _lblStatus.Text = Loc.T("ccs.ux_device_error", name, ex.Message);
            _lblStatus.ForeColor = AgrisTheme.Danger;
        }
        finally
        {
            RefreshReadingUi();
        }
    }

    private static string ApproverName(ApprovalResult a)
        => string.IsNullOrWhiteSpace(a.DisplayName) ? a.UserName : a.DisplayName;

    /// <summary>Nhập tay Brix/Pol: quản lý xác thực tài khoản + mật khẩu, duyệt cho chuyến đang đo.</summary>
    private void RequestManualEntry()
    {
        if (_currentTripId is null || _fieldsReadOnly) return;

        var approval = ApprovalDialog.Request(this, ApprovalLevel.Manager,
            Loc.T("ccs.ux_manual_approval", _txtTripNo.Text));
        if (approval is null) return;

        _manualApproval = approval;
        _numBrix.Locked = false;
        _numPol.Locked = false;
        RefreshReadingUi();

        _lblStatus.Text = Loc.T("ccs.ux_manual_approved_status", ApproverName(approval));
        _lblStatus.ForeColor = AgrisTheme.Warning;
        _numBrix.Focus();
    }

    /// <summary>③ CCS + tạp chất thực tế — tự tính từ Bx, Pol (CCS) và lấy từ trạm đo tạp chất. Không sửa tay.</summary>
    private Card BuildActualCard()
    {
        var card = new Card { Dock = DockStyle.Top, Padding = new Padding(1) };
        var header = new IconSectionHeader(GLYPH_CALC, Loc.T("ccs.ux_actual_title"), AgrisTheme.BrandGreen, Loc.T("ccs.ux_actual_sub"));
        var row = MetricRow(_tileCcsActual, _tileTcActual);
        card.Controls.Add(row);
        card.Controls.Add(header);
        card.Height = header.Height + row.Height + card.Padding.Vertical;
        return card;
    }

    /// <summary>④ CCS + tạp chất thanh toán — theo chính sách thu mua FRM (Action gọi qua DigiScale).</summary>
    private Card BuildPaymentCard()
    {
        var card = new Card { Dock = DockStyle.Top, Padding = new Padding(1) };
        _btnPayment.Margin = new Padding(0);
        _hdrPayment.Actions.Controls.Add(_btnPayment);
        var row = MetricRow(_tileCcsPaid, _tileTcPaid);
        card.Controls.Add(row);
        card.Controls.Add(_hdrPayment);
        card.Height = _hdrPayment.Height + row.Height + card.Padding.Vertical;
        return card;
    }

    /// <summary>Hai ô số liệu cạnh nhau, chia đều.</summary>
    private static TableLayoutPanel MetricRow(MetricTile a, MetricTile b)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = AgrisTheme.Surface,
            Padding = new Padding(14, 12, 14, 14)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        a.Dock = DockStyle.Fill;
        b.Dock = DockStyle.Fill;
        a.Margin = new Padding(0, 0, 6, 0);
        b.Margin = new Padding(6, 0, 0, 0);
        row.Controls.Add(a, 0, 0);
        row.Controls.Add(b, 1, 0);
        var h = MetricTile.PreferredTileHeight;
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
        row.Height = h + row.Padding.Vertical;
        return row;
    }

    /// <summary>
    /// Xe mía đăng tài trong NGÀY NHẬP CCS + trạng thái CCS từng xe. Xe chưa có CCS xếp trên, chữ cam;
    /// bấm xe chưa có → nhập CCS; bấm xe đã có → xem / in lại.
    /// </summary>
    private Card BuildHistoryCard()
    {
        var rightCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(1) };

        var filter = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 40,
            FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(14, 6, 8, 4),
            WrapContents = false, BackColor = AgrisTheme.Surface
        };
        _chkOnlyMissing.ForeColor = AgrisTheme.TextSecondary;
        filter.Controls.Add(_chkOnlyMissing);
        _chkOnlyMissing.CheckedChanged += async (_, _) => await LoadAllCcsAsync();

        var hint = new Label
        {
            Text = Loc.T("ccs.ux_day_hint"), Dock = DockStyle.Top, Height = 24,
            Font = AgrisTheme.Caption(), ForeColor = AgrisTheme.TextMuted,
            Padding = new Padding(14, 0, 8, 4), BackColor = AgrisTheme.Surface
        };

        _grid.Dock = DockStyle.Fill;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = Loc.T("ccs.print_label_reg_no"), MinimumWidth = 90, FillWeight = 24, Name = "TripNo" },
            new DataGridViewTextBoxColumn { HeaderText = Loc.T("ccs.print_label_plate"), MinimumWidth = 70, FillWeight = 16, Name = "Plate" },
            new DataGridViewTextBoxColumn { HeaderText = "CCS", MinimumWidth = 50, FillWeight = 12, Name = "Ccs" },
            new DataGridViewTextBoxColumn { HeaderText = Loc.T("ccs.ux_col_status"), MinimumWidth = 80, FillWeight = 20, Name = "Status" },
            new DataGridViewTextBoxColumn { HeaderText = Loc.T("ccs.ux_col_received"), MinimumWidth = 60, FillWeight = 12, Name = "Received" });
        foreach (DataGridViewColumn col in _grid.Columns)
            col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _grid.Columns["TripNo"]!.DefaultCellStyle.ForeColor = AgrisTheme.BrandTeal;
        _grid.Columns["TripNo"]!.DefaultCellStyle.Font = AgrisTheme.Body(9.5F, FontStyle.Underline | FontStyle.Bold);
        _grid.Columns["Ccs"]!.DefaultCellStyle.Font = AgrisTheme.Mono(10F, FontStyle.Bold);
        _grid.CellMouseEnter += (_, ev) => { if (ev.RowIndex >= 0) _grid.Cursor = Cursors.Hand; };
        _grid.CellMouseLeave += (_, _) => _grid.Cursor = Cursors.Default;
        _grid.CellClick += OnCcsGridCellClick;
        GridBuilder.AttachEmptyState(_grid, Loc.T("ccs.ux_day_empty"));

        rightCard.Controls.Add(_grid);
        rightCard.Controls.Add(hint);
        rightCard.Controls.Add(filter);
        rightCard.Controls.Add(_hdrDay);
        GridScrollBar.Attach(_grid);
        return rightCard;
    }

    /// <summary>Đổi ngày nhập CCS: bỏ lượt đang nhập (của ngày cũ) và tải danh sách xe của ngày mới.</summary>
    private async Task OnCcsDateChangedAsync()
    {
        if (_suppressDateChange) return;
        ResetForm();
        await LoadAllCcsAsync();
    }

    /// <summary>[Đầu ngày, đầu ngày hôm sau) theo giờ máy của ngày nhập CCS.</summary>
    private (DateTimeOffset From, DateTimeOffset To) SelectedDayRange()
    {
        var day = _dtpCcsDate.Value.Date;
        var from = new DateTimeOffset(day, TimeZoneInfo.Local.GetUtcOffset(day));
        var next = day.AddDays(1);
        // Npgsql chỉ nhận DateTimeOffset offset 0 cho cột timestamptz.
        return (from.ToUniversalTime(), new DateTimeOffset(next, TimeZoneInfo.Local.GetUtcOffset(next)).ToUniversalTime());
    }

    /// <summary>Một hàng ô cỡ lớn chia cột theo tỉ lệ phần trăm.</summary>
    private static TableLayoutPanel TileRow(FieldTile[] tiles, int[] percents)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = tiles.Length,
            RowCount = 1,
            BackColor = AgrisTheme.Surface,
            Padding = new Padding(14, 12, 4, 0)
        };

        for (var i = 0; i < tiles.Length; i++)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, percents[i]));
            row.Controls.Add(tiles[i], i, 0);
        }

        var height = tiles.Max(t => t.PreferredTileHeight) + tiles[0].Margin.Vertical;
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        row.Height = height + row.Padding.Vertical;
        return row;
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
        _lastQrContent = qr;
        await ScanAndLoadTripAsync(qr);
    }

    /// <summary>Bật/tắt các thao tác cần có chuyến; chip chuyến chỉ hiện khi đã chọn.</summary>
    private void SetTripLoaded(bool loaded)
    {
        _chipTrip.Visible = loaded;
        _btnPayment.Enabled = loaded && !_paymentBusy;
        _btnPrint.Enabled = loaded;
        _btnRetryDigi.Enabled = loaded;
        RefreshReadingUi();
    }

    /// <summary>Hiển thị chuyến vừa quét: chip chuyến, tạp chất thực tế; số thanh toán chờ ghi CCS.</summary>
    private async Task ShowLoadedTripAsync()
    {
        _chipTrip.Text = Loc.T("ccs.ux_trip_chip", _txtTripNo.Text, _txtPlate.Text);
        // Chuyến mới: đo lại từ đầu, phê duyệt nhập tay của chuyến trước không dùng lại.
        ResetReadings();
        SetTripLoaded(true);
        ResetPaymentTiles(Loc.T("ccs.ux_note_paid_waiting"));
        await LoadActualImpurityAsync();
        AutoCalcCcs();
    }

    private async Task LoadActualImpurityAsync()
    {
        if (_currentTripId is not { } tripId) return;
        try
        {
            var pct = await _presenter.GetActualImpurityAsync(tripId, _cts.Token);
            _actualImpurity = pct;
            _tileTcActual.SetValue(pct is { } p ? $"{p:N2} %" : "—");
            _tileTcActual.SetNote(pct is null ? Loc.T("ccs.ux_note_tc_none") : Loc.T("ccs.ux_note_tc_from_station"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _tileTcActual.SetValue("—");
            _tileTcActual.SetNote(ex.Message);
        }
    }

    private void ResetPaymentTiles(string note)
    {
        _paidCcs = null;
        _paidImpurity = null;
        _paidPolicy = null;
        _tileCcsPaid.SetValue("—");
        _tileTcPaid.SetValue("—");
        _tileCcsPaid.SetNote(note);
        _tileTcPaid.SetNote(note);
        _hdrPayment.SetSubtitle(Loc.T("ccs.ux_payment_sub"));
    }

    /// <summary>
    /// Lấy CCS + tạp chất thanh toán từ chính sách FRM. Chỉ có khi chuyến đã cân lần 2; gửi kèm số thanh toán
    /// lên DigiScale như màn Cân. Tự gọi sau khi ghi CCS; người dùng có thể bấm "Lấy từ FRM" để lấy lại.
    /// </summary>
    private async Task FetchPaymentFiguresAsync(bool showDialogOnError)
    {
        if (_currentTripId is not { } tripId || _paymentBusy) return;

        _paymentBusy = true;
        _btnPayment.Enabled = false;
        ResetPaymentTiles(Loc.T("ccs.ux_note_paid_loading"));
        try
        {
            var r = await _presenter.FetchPaymentFiguresAsync(tripId, _cts.Token);
            if (!r.HasPolicy)
            {
                var note = r.Failure switch
                {
                    PaymentFiguresFailure.NotWeighedOut => Loc.T("ccs.ux_note_paid_not_weighed_out"),
                    PaymentFiguresFailure.NoLenhDon => Loc.T("ccs.ux_note_paid_no_lenhdon"),
                    _ => Loc.T("ccs.ux_note_paid_no_policy")
                };
                ResetPaymentTiles(note);
                if (r.Failure == PaymentFiguresFailure.NoPolicy)
                {
                    var friendly = Helpers.DigiScaleErrors.Describe(r.Error);
                    _lblStatus.Text = Loc.T("ccs.ux_status_paid_failed", friendly.Reason);
                    _lblStatus.ForeColor = AgrisTheme.Warning;
                    if (showDialogOnError)
                        Helpers.DigiScalePopup.ShowAlert(this, Helpers.AlertKind.Warning, Loc.T("ccs.ux_payment_title"),
                            friendly.Reason, friendly.Action,
                            [(Loc.T("alert.fact_trip"), _txtTripNo.Text), (Loc.T("alert.fact_plate"), _txtPlate.Text)]);
                }
                return;
            }

            _paidCcs = r.CcsThanhToan;
            _paidImpurity = r.TapChatThanhToan;
            _paidPolicy = r.ChinhSach;
            _tileCcsPaid.SetValue(r.CcsThanhToan is { } ccs ? $"{ccs:N2}" : "—");
            _tileCcsPaid.SetNote(r.CcsThanhToan is null ? Loc.T("ccs.ux_note_paid_ccs_none") : Loc.T("ccs.ux_note_paid_from_frm"));
            _tileTcPaid.SetValue(r.TapChatThanhToan is { } tc ? $"{tc:N2} %" : "—");
            _tileTcPaid.SetNote(Loc.T("ccs.ux_note_paid_from_frm"));
            if (!string.IsNullOrWhiteSpace(r.ChinhSach))
                _hdrPayment.SetSubtitle(Loc.T("ccs.ux_note_paid_policy", r.ChinhSach));

            if (r.Error is not null)
            {
                _lblStatus.Text = Loc.T("ccs.ux_status_paid_push_failed", Helpers.DigiScaleErrors.Describe(r.Error).Reason);
                _lblStatus.ForeColor = AgrisTheme.Warning;
            }
            else
            {
                _lblStatus.Text = Loc.T("ccs.ux_status_paid_ok", _txtTripNo.Text);
                _lblStatus.ForeColor = AgrisTheme.Success;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ResetPaymentTiles(Loc.T("ccs.ux_note_paid_no_policy"));
            _lblStatus.Text = Loc.T("ccs.ux_status_paid_failed", ex.Message);
            _lblStatus.ForeColor = AgrisTheme.Danger;
        }
        finally
        {
            _paymentBusy = false;
            _btnPayment.Enabled = _currentTripId is not null;
        }
    }

    /// <summary>
    /// Cài hệ số xơ cho máy này. Giá trị chính thức nằm trên Portal (Quy tắc cân) — ở đây chỉ áp dụng
    /// tới khi đóng màn hình. Chưa cấu hình vai trò được cài → quản lý phải xác nhận.
    /// </summary>
    private void EditFibre()
    {
        var access = SugarcaneFlowSettings.Current.FibreAccess(Program.LoggedInRole, Program.LoggedInPermissions);
        if (access == FibreEditAccess.Denied) return;

        string by = Program.LoggedInUserAccount;
        if (access == FibreEditAccess.NeedsApproval)
        {
            var approval = ApprovalDialog.Request(this, ApprovalLevel.Manager, Loc.T("ccs.ux_fibre_approval"));
            if (approval is null) return;
            by = approval.UserName;
        }

        using var dlg = new Form
        {
            Text = Loc.T("ccs.ux_fibre_title"),
            Size = new Size(520, 300),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = AgrisTheme.Surface
        };
        var table = FormLayout.FieldTable();
        FormLayout.AddFullWidth(table, GridBuilder.Notice(Loc.T("ccs.ux_fibre_notice"), AgrisTheme.Info), 56);
        var portal = SugarcaneFlowSettings.Current.CcsFibre;
        FormLayout.AddFullWidth(table, GridBuilder.Caption(Loc.T("ccs.ux_fibre_portal", portal?.ToString("N2") ?? "—")), 28);
        var num = new NumericUpDown
        {
            DecimalPlaces = 2, Minimum = 0m, Maximum = 20m, Increment = 0.1m,
            Value = Math.Clamp(_numFibre.Value, 0m, 20m),
            Dock = DockStyle.Fill, Font = AgrisTheme.Body(14F, FontStyle.Bold)
        };
        FormLayout.AddField(table, Loc.T("ccs.ux_fibre_field"), num);
        var ok = GridBuilder.PrimaryButton("✓  " + Loc.T("common.save"), new Size(130, 38));
        ok.DialogResult = DialogResult.OK;
        var cancel = GridBuilder.SecondaryButton(Loc.T("common.cancel"), new Size(100, 38));
        cancel.DialogResult = DialogResult.Cancel;
        FormLayout.AddFullWidth(table, FormLayout.ActionBar(ok, cancel), FormLayout.ActionRowHeight);
        dlg.Controls.Add(table);
        dlg.AcceptButton = ok;
        dlg.CancelButton = cancel;
        dlg.Shown += (_, _) => { num.Select(0, num.Text.Length); num.Focus(); };

        if (dlg.ShowDialog(this) != DialogResult.OK || num.Value <= 0) return;

        _numFibre.Value = num.Value;
        _lblStatus.Text = Loc.T("ccs.ux_fibre_saved", by);
        _lblStatus.ForeColor = AgrisTheme.Success;
    }

    private async Task RecordCcsAsync()
    {
        if (_currentTripId is null)
        {
            MessageBox.Show(Loc.T("ccs.ux_need_trip"), Loc.T("common.missing_info"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!_chkCcsPending.Checked && _numCcs.Value <= 0)
        {
            MessageBox.Show(_ccsIssue ?? Loc.T("ccs.ux_need_ccs"), Loc.T("ccs.ux_ccs_fail_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _numPol.Focus();
            return;
        }

        // Mỗi số Brix/Pol phải có nguồn: đọc từ máy, hoặc nhập tay đã được quản lý duyệt.
        if (!_chkCcsPending.Checked && (_brixSource == ReadingSource.None || _polSource == ReadingSource.None))
        {
            MessageBox.Show(Loc.T("ccs.ux_need_reading"), Loc.T("common.missing_info"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!_chkCcsPending.Checked && !ConfirmCcsSettings(_numCcs.Value)) return;

        var anyManual = _brixSource == ReadingSource.Manual || _polSource == ReadingSource.Manual;
        var source = _chkCcsPending.Checked ? "FRM" : anyManual ? "MANUAL" : "DEVICE";

        _btnRecord.Enabled = false;
        try
        {
            var result = await _presenter.RecordCcsAsync(
                new ReceiveCcsResultCommand(
                    TripId: _currentTripId.Value,
                    CcsValue: _chkCcsPending.Checked ? null : _numCcs.Value,
                    Source: source,
                    SourceReportsPending: _chkCcsPending.Checked,
                    CorrelationId: Guid.NewGuid().ToString("N"),
                    ApprovalRef: anyManual ? _manualApproval?.ApprovalRef : null,
                    Reason: _chkCcsPending.Checked ? null : $"Brix={_brixSource}; Pol={_polSource}",
                    Brix: _chkCcsPending.Checked ? null : _numBrix.Value,
                    Pol: _chkCcsPending.Checked ? null : _numPol.Value,
                    PolCorrected: _chkCcsPending.Checked ? null : _numPolDoc.Value),
                _cts.Token);

            if (!result.IsSuccess)
            {
                MessageBox.Show(result.FailureReason, Loc.T("quality.ccs_failed_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Push CCS lên DigiScale + popup
            var digiOk = false;
            // Chưa có kết quả (chờ nguồn báo) → không đẩy phiếu số 0 lên DigiScale.
            if (!_chkCcsPending.Checked && _numBrix.Value > 0 && _numPol.Value > 0 && _numCcs.Value > 0)
            try
            {
                var ld = _presenter.FindLenhDonForTrip(_currentFrmLenhDonId, _txtPlate.Text);
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                var pushResult = await _presenter.PushCcsAsync(
                    _currentFrmLenhDonId ?? ld?.SourceKey ?? _txtTripNo.Text,
                    _currentFrmMaLenhDon ?? ld?.MaLD ?? _txtTripNo.Text,
                    "0", "1", DateTime.Now,
                    _numBrix.Value.ToString("F2", inv), _numCcs.Value.ToString("F2", inv),
                    _numPol.Value.ToString("F2", inv), _numPolDoc.Value.ToString("F2", inv), "1",
                    Program.LoggedInUserName, _currentFrmLenhDonBu ?? ld?.BusinessUnit, _cts.Token);

                digiOk = pushResult.IsSuccess;
                Helpers.DigiScalePopup.Show(this,
                    Loc.T("ccs.ux_record_ok_title"),
                    Loc.T("ccs.ux_record_ok_body", _txtTripNo.Text, _txtPlate.Text,
                        $"{_numBrix.Value:#,##0.##}", $"{_numPol.Value:#,##0.##}", $"{_numCcs.Value:N2}"),
                    digiOk, pushResult.ErrorMessage ?? (digiOk ? "OK" : Loc.T("common.error")));
            }
            catch (Exception pushEx) when (pushEx is not OperationCanceledException)
            {
                Helpers.DigiScalePopup.Show(this,
                    Loc.T("ccs.ux_record_ok_title"),
                    Loc.T("ccs.ux_record_ok_body", _txtTripNo.Text, _txtPlate.Text,
                        $"{_numBrix.Value:#,##0.##}", $"{_numPol.Value:#,##0.##}", $"{_numCcs.Value:N2}"),
                    false, pushEx.Message);
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

            // CCS đã lên DigiScale → lấy CCS + tạp chất thanh toán theo chính sách FRM.
            if (digiOk && !_chkCcsPending.Checked)
                await FetchPaymentFiguresAsync(showDialogOnError: false);

            await LoadAllCcsAsync();

            // Đã ghi xong → khoá về chế độ xem, không cho đo lại.
            await ShowRecordedCcsAsync(_currentTripId.Value, _txtTripNo.Text);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Loc.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnRecord.Enabled = _numBrix.Enabled;
        }
    }

    /// <summary>
    /// Kéo Quy tắc cân mới nhất từ Portal (Fibre, CCS Min/Max). Có Fibre trên Portal thì dùng làm hệ số xơ.
    /// Chưa lấy được / chưa cài → hiện cảnh báo trên dải trạng thái.
    /// </summary>
    private async Task ApplyPortalRulesAsync()
    {
        try { await PortalScaleRules.RefreshAsync(); }
        catch (OperationCanceledException) { }

        var settings = SugarcaneFlowSettings.Current;
        if (settings.CcsFibre is { } fibre)
            _numFibre.Value = Math.Clamp(fibre, _numFibre.Minimum, _numFibre.Maximum);

        if (!settings.PortalRulesLoaded)
        {
            _lblStatus.Text = Loc.T("scale_rules.not_loaded_status");
            _lblStatus.ForeColor = AgrisTheme.Warning;
        }
        else if (settings.CcsFibre is null)
        {
            _lblStatus.Text = Loc.T("ccs.fibre_not_configured_status");
            _lblStatus.ForeColor = AgrisTheme.Warning;
        }
    }

    /// <summary>
    /// Kiểm tra trước khi ghi CCS:
    /// - Chưa có hệ số xơ → chặn (CCS tính sai).
    /// - Chưa cấu hình ngưỡng CCS → cảnh báo, hỏi có lưu không.
    /// - CCS ngoài ngưỡng [min, max] → cảnh báo, hỏi có lưu không.
    /// </summary>
    private bool ConfirmCcsSettings(decimal ccs)
    {
        var settings = SugarcaneFlowSettings.Current;

        if (_numFibre.Value <= 0)
        {
            MessageBox.Show(this, Loc.T("ccs.fibre_not_configured_msg"), Loc.T("ccs.fibre_not_configured_title"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (settings.CcsMin is null && settings.CcsMax is null)
        {
            return MessageBox.Show(this, Loc.T("ccs.threshold_not_configured_msg", ccs.ToString("N2")),
                Loc.T("ccs.threshold_not_configured_title"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
                == DialogResult.Yes;
        }

        if ((settings.CcsMin is { } min && ccs < min) || (settings.CcsMax is { } max && ccs > max))
        {
            return MessageBox.Show(this,
                Loc.T("ccs.out_of_threshold_msg", ccs.ToString("N2"),
                    settings.CcsMin?.ToString("N2") ?? "—", settings.CcsMax?.ToString("N2") ?? "—"),
                Loc.T("ccs.out_of_threshold_title"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        return true;
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
        ResetReadings();
        _numPolDoc.Value = 0;
        // Hệ số xơ giữ nguyên (xác định theo tuần, không reset mỗi lượt)
        _chkCcsPending.Checked = false;
        _lblStatus.Text = Loc.T("ccs.ux_start");
        _lblStatus.ForeColor = AgrisTheme.BrandGreen;
        _actualImpurity = null;
        _tileTcActual.SetValue("—");
        _tileTcActual.SetNote(Loc.T("ccs.ux_note_tc_none"));
        ResetPaymentTiles(Loc.T("ccs.ux_note_paid_waiting"));
        SetFieldsReadOnly(false);
        SetTripLoaded(false);
        AutoCalcCcs();
        _txtBarcode.Focus();
    }

    /// <summary>
    /// CCS thực tế tự tính từ Bx + Pol + hệ số xơ (QCVN 01-98:2012/BNNPTNT). Không cho sửa tay.
    /// </summary>
    private void AutoCalcCcs()
    {
        _ccsIssue = null;

        void Clear(string note, string? issue = null)
        {
            _ccsIssue = issue ?? note;
            _numCcs.Value = 0;
            _numPolDoc.Value = 0;
            _tileCcsActual.SetValue("—");
            _tileCcsActual.SetNote(note);
        }

        if (_chkCcsPending.Checked) { Clear(Loc.T("ccs.ui_pending")); return; }
        if (_numBrix.Value <= 0 || _numPol.Value <= 0) { Clear(Loc.T("ccs.ux_note_enter_bx_pol")); return; }
        if (_numFibre.Value <= 0) { Clear(Loc.T("ccs.ux_note_fibre_missing")); return; }

        var result = CcsCalculator.Calculate(_numPol.Value, _numBrix.Value, _numFibre.Value);
        if (!result.IsValid) { Clear("❌ " + result.InvalidReason, result.InvalidReason); return; }

        // CCS ≤ 0: không ép về 0 im lặng — báo rõ Pol quá thấp so với Bx.
        if (result.CcsValue <= 0)
        {
            var ccsText = result.CcsValue.ToString("N2");
            Clear("❌ " + Loc.T("ccs.ux_note_ccs_not_positive", ccsText),
                Loc.T("ccs.ux_ccs_not_positive", ccsText, _numPol.Value.ToString("N2"), _numBrix.Value.ToString("N2")));
            return;
        }

        _numCcs.Value = Math.Clamp(result.CcsValue, _numCcs.Minimum, _numCcs.Maximum);
        _numPolDoc.Value = Math.Clamp(Math.Round(result.PolCaiChinh, 4), _numPolDoc.Minimum, _numPolDoc.Maximum);
        _tileCcsActual.SetValue($"{_numCcs.Value:N2}");
        _tileCcsActual.SetNote(Loc.T("ccs.ux_note_ccs_calc", result.PolCaiChinh.ToString("N2")));
    }

    private static Color SoftBackground(Color fore)
        => fore == AgrisTheme.Danger ? AgrisTheme.DangerSoft
         : fore == AgrisTheme.Info ? AgrisTheme.InfoSoft
         : fore == AgrisTheme.Warning ? AgrisTheme.WarningSoft
         : fore == AgrisTheme.Success || fore == AgrisTheme.BrandGreen ? AgrisTheme.SuccessSoft
         : AgrisTheme.SurfaceMuted;

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
            // Xe đăng tài trong ngày nhập CCS — cả xe còn trong nhà máy lẫn đã ra (CCS có thể về sau khi xe ra).
            var (from, to) = SelectedDayRange();
            var trips = new List<TripRegistrationRow>();
            foreach (var tab in new[] { TripGridTab.InFactory, TripGridTab.Exited })
                trips.AddRange(await _tripRegistrationQueries.ListAsync(_siteContext.CurrentSiteId,
                    new TripRegistrationFilter
                    {
                        Tab = tab, FromUtc = from, ToUtc = to,
                        Category = TTC.Weighing.Application.Queries.TripCategory.Sugarcane // CCS chỉ cho xe mía
                    }, _cts.Token));
            _popupTrips = trips.DistinctBy(t => t.TripId).OrderBy(t => t.TripNo).ToArray();

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
        try
        {
            _lblStatus.Text = Loc.T("ccs.ux_searching");
            _lblStatus.ForeColor = AgrisTheme.Info;

            var result = await _presenter.ScanQrAsync(content, _siteContext.CurrentSiteId, _cts.Token);
            if (!result.IsSuccess)
            {
                _lblStatus.Text = $"❌ {result.ErrorMessage}";
                _lblStatus.ForeColor = AgrisTheme.Danger;
                SetTripLoaded(false);
                return;
            }

            // Chữ đường chỉ cho xe mía — chuyến vật tư (ERP) không đo CCS.
            var scannedId = result.TripId!.Value;
            var sourceSystem = await _db.Trips.AsNoTracking()
                .Where(t => t.Id == scannedId).Select(t => t.SourceSystem).FirstOrDefaultAsync(_cts.Token);
            if (sourceSystem == "ERP")
            {
                _lblStatus.Text = "❌ " + Loc.T("ccs.ux_material_trip", result.TripNo ?? content);
                _lblStatus.ForeColor = AgrisTheme.Danger;
                SetTripLoaded(false);
                return;
            }

            // CCS nhập theo ngày: xe đăng tài ngày khác → hỏi chuyển sang ngày của xe, không thì bỏ.
            if (!await EnsureTripOnSelectedDayAsync(result.TripId!.Value, result.TripNo ?? content))
            {
                _lblStatus.Text = Loc.T("ccs.ux_start");
                _lblStatus.ForeColor = AgrisTheme.BrandGreen;
                SetTripLoaded(false);
                return;
            }

            _currentTripId = result.TripId;
            _currentFrmLenhDonId = result.FrmLenhDonId;
            _currentFrmMaLenhDon = result.FrmMaLenhDon;
            _currentFrmLenhDonBu = result.FrmLenhDonBu;
            _txtTripNo.Text = result.TripNo ?? "—";
            _txtPlate.Text = result.Plate ?? "—";
            _txtOwner.Text = result.CustomerOrOwner ?? "—";
            _lblStatus.Text = Loc.T("ccs.ui_ready", result.TripNo ?? string.Empty);
            _lblStatus.ForeColor = AgrisTheme.Success;
            SetFieldsReadOnly(false);
            await ShowLoadedTripAsync();

            // Đã đo rồi (dù chọn từ ô tìm, quét QR hay lưới) → chỉ xem, không đo lại.
            if (await ShowRecordedCcsAsync(result.TripId!.Value, result.TripNo ?? content)) return;

            if (_numBrix.Enabled) _numBrix.Focus();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _lblStatus.Text = $"❌ {ex.Message}";
            _lblStatus.ForeColor = AgrisTheme.Danger;
        }
    }

    /// <summary>
    /// Xe quét được phải thuộc ngày nhập CCS. Khác ngày → hỏi chuyển sang ngày của xe (giữ lượt đang quét).
    /// Trả false nếu người dùng không chuyển.
    /// </summary>
    private async Task<bool> EnsureTripOnSelectedDayAsync(Guid tripId, string tripNo)
    {
        var createdAt = await _db.Trips.AsNoTracking()
            .Where(t => t.Id == tripId)
            .Select(t => (DateTimeOffset?)t.CreatedAt)
            .FirstOrDefaultAsync(_cts.Token);
        if (createdAt is not { } created) return true;

        var tripDay = created.ToLocalTime().Date;
        if (tripDay == _dtpCcsDate.Value.Date) return true;

        var answer = MessageBox.Show(this,
            Loc.T("ccs.ux_other_day_msg", tripNo, tripDay.ToString("dd/MM/yyyy"), _dtpCcsDate.Value.ToString("dd/MM/yyyy")),
            Loc.T("ccs.ux_other_day_title"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return false;

        _suppressDateChange = true;
        try { _dtpCcsDate.Value = tripDay; }
        finally { _suppressDateChange = false; }
        await LoadAllCcsAsync();
        return true;
    }

    /// <summary>
    /// Danh sách xe mía đăng tài trong ngày nhập CCS (không tính phiếu hủy / đã xoá) kèm CCS hiệu lực của từng xe.
    /// Xe chưa có CCS xếp trên. Tiêu đề hiện tổng số xe / đã có / chưa có.
    /// </summary>
    private async Task LoadAllCcsAsync()
    {
        try
        {
            var (from, to) = SelectedDayRange();
            var siteId = _siteContext.CurrentSiteId;
            var ccsType = TTC.Weighing.Domain.Enums.QualitySampleType.Ccs;

            var rows = await _db.Trips.AsNoTracking()
                .Where(t => t.SiteId == siteId && t.DeletedAt == null
                            && t.Lifecycle != TTC.Weighing.Domain.Enums.TripLifecycle.Cancelled
                            && t.SourceSystem != "ERP"
                            && t.CreatedAt >= from && t.CreatedAt < to)
                .Select(t => new
                {
                    t.TripNo,
                    t.TractorPlate,
                    Sample = _db.QualitySamples
                        .Where(s => s.TripId == t.Id && s.Type == ccsType && s.IsEffective)
                        .OrderByDescending(s => s.Revision)
                        .Select(s => new { s.CcsValue, s.Status, s.ReceivedAt })
                        .FirstOrDefault()
                })
                .ToListAsync(_cts.Token);

            var done = rows.Count(r => r.Sample?.CcsValue is not null);
            var missing = rows.Count - done;
            _hdrDay.SetSubtitle(Loc.T("ccs.ux_day_sub", _dtpCcsDate.Value.ToString("dd/MM/yyyy"), rows.Count, done, missing));

            var visible = rows
                .Where(r => !_chkOnlyMissing.Checked || r.Sample?.CcsValue is null)
                .OrderBy(r => r.Sample?.CcsValue is null ? 0 : 1)
                .ThenBy(r => r.TripNo);

            _grid.SuspendLayout();
            _grid.Rows.Clear();
            foreach (var r in visible)
            {
                var hasCcs = r.Sample?.CcsValue is not null;
                var pending = r.Sample is not null && r.Sample.CcsValue is null
                              && r.Sample.Status.ToString() == "Pending";
                var status = hasCcs ? "✓ " + Loc.T("ccs.ux_status_done")
                    : pending ? "⏳ " + Loc.T("ccs.ux_status_pending")
                    : "● " + Loc.T("ccs.ux_status_missing");

                var idx = _grid.Rows.Add(
                    r.TripNo,
                    r.TractorPlate,
                    r.Sample?.CcsValue is { } v ? v.ToString("N2") : "—",
                    status,
                    r.Sample?.ReceivedAt?.ToLocalTime().ToString("HH:mm") ?? "");
                var row = _grid.Rows[idx];
                row.Tag = hasCcs;
                row.Cells["Status"].Style.ForeColor = hasCcs ? AgrisTheme.Success
                    : pending ? AgrisTheme.Info : AgrisTheme.Warning;
                row.Cells["Status"].Style.Font = AgrisTheme.Body(9F, FontStyle.Bold);
            }
            _grid.ResumeLayout();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _hdrDay.SetSubtitle(Loc.T("ccs.ux_day_load_error", ex.Message));
        }
    }

    /// <summary>Click dòng grid CCS → load thông tin chuyến + số liệu CCS lên form để xem/in lại.</summary>
    private async void OnCcsGridCellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        var row = _grid.Rows[e.RowIndex];
        var tripNo = row.Cells["TripNo"].Value as string;
        if (string.IsNullOrWhiteSpace(tripNo)) return;

        // Load chuyến — đã có CCS thì ScanAndLoadTripAsync tự chuyển sang chế độ xem.
        _txtBarcode.Text = tripNo;
        _lastQrContent = tripNo;
        await ScanAndLoadTripAsync(tripNo);
    }

    /// <summary>
    /// Chuyến đã có CCS hợp lệ → điền đúng số đã ghi (Bx, Pol, Pol cải chính, CCS) và khoá ô nhập:
    /// chỉ xem / in / đẩy lại, không đo lại. Trả false nếu chuyến chưa có CCS.
    /// </summary>
    private async Task<bool> ShowRecordedCcsAsync(Guid tripId, string tripNo)
    {
        var ccsType = TTC.Weighing.Domain.Enums.QualitySampleType.Ccs;
        var sample = await _db.QualitySamples.AsNoTracking()
            .Where(s => s.TripId == tripId && s.Type == ccsType && s.IsEffective)
            .OrderByDescending(s => s.Revision)
            .Select(s => new { s.CcsValue, s.BrixValue, s.PolValue, s.PolCorrected, s.Reason })
            .FirstOrDefaultAsync(_cts.Token);

        if (sample?.CcsValue is { } ccs)
        {
            _chkCcsPending.Checked = false;

            _settingReading = true;
            try
            {
                _numBrix.Value = Math.Clamp(sample.BrixValue ?? 0m, _numBrix.Minimum, _numBrix.Maximum);
                _numPol.Value = Math.Clamp(sample.PolValue ?? 0m, _numPol.Minimum, _numPol.Maximum);
            }
            finally { _settingReading = false; }

            // Nguồn số đo lưu dạng "Brix=Manual; Pol=Device".
            _brixSource = sample.BrixValue is > 0 ? SourceFromReason(sample.Reason, "Brix") : ReadingSource.None;
            _polSource = sample.PolValue is > 0 ? SourceFromReason(sample.Reason, "Pol") : ReadingSource.None;

            // Giữ đúng số đã ghi — không tính lại theo hệ số xơ hiện tại.
            _ccsIssue = null;
            _numCcs.Value = Math.Clamp(ccs, _numCcs.Minimum, _numCcs.Maximum);
            _numPolDoc.Value = Math.Clamp(sample.PolCorrected ?? 0m, _numPolDoc.Minimum, _numPolDoc.Maximum);
            _tileCcsActual.SetValue($"{_numCcs.Value:N2}");
            _tileCcsActual.SetNote(Loc.T("ccs.ux_note_recorded"));
        }
        else
        {
            return false;
        }

        // Lock: đây là dữ liệu đã ghi — chỉ xem/in, không cho sửa
        SetFieldsReadOnly(true);
        _lblStatus.Text = Loc.T("ccs.ux_review", tripNo);
        _lblStatus.ForeColor = AgrisTheme.Info;
        return true;
    }

    /// <summary>Lock/unlock các ô nhập khi xem lại từ lịch sử vs nhập mới.</summary>
    private void SetFieldsReadOnly(bool readOnly)
    {
        _fieldsReadOnly = readOnly;
        _numBrix.Enabled = !readOnly;
        _numPol.Enabled = !readOnly;
        _chkCcsPending.Enabled = !readOnly;
        _btnRecord.Enabled = !readOnly;
        RefreshReadingUi();
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

        // Không bao giờ đẩy phiếu CCS toàn số 0 — DigiScale sẽ ghi đè kết quả đúng đã đẩy trước đó.
        if (brix <= 0 || pol <= 0 || ccs <= 0)
        {
            MessageBox.Show(Loc.T("ccs.ux_retry_no_values", tripNo), Loc.T("ccs.ux_retry_blocked_title"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _btnRetryDigi.Enabled = false;
        _lblStatus.Text = "⏳ Đang đẩy lại CCS lên DigiScale...";
        _lblStatus.ForeColor = AgrisTheme.Info;

        try
        {
            var ld = _presenter.FindLenhDonForTrip(_currentFrmLenhDonId, plate);
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
                    $"Chuyến: {tripNo}\nBiển số: {plate}\nCCS: {ccs:#,##0.##}",
                    true, "OK");
            }
            else
            {
                _lblStatus.Text = $"❌ Đẩy lại thất bại: {pushResult.ErrorMessage}";
                _lblStatus.ForeColor = AgrisTheme.Danger;
                Helpers.DigiScalePopup.Show(this,
                    "ĐẨY LẠI THẤT BẠI",
                    $"Chuyến: {tripNo}\nBiển số: {plate}\nCCS: {ccs:#,##0.##}",
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

    /// <summary>Số liệu in trên phiếu CCS — chụp tại lúc bấm In, không đọc lại control khi vẽ.</summary>
    private sealed record CcsTicketData(
        string TripNo, string Plate,
        decimal Brix, decimal Pol, decimal PolDoc, decimal? Ccs, bool Pending,
        decimal? ActualImpurity, decimal? PaidCcs, decimal? PaidImpurity, string? Policy,
        DateTime PrintedAt,
        string? Factory = null, string? Owner = null, string? CaneForm = null);

    private void PrintCcsTicket()
    {
        if (_currentTripId is null)
        {
            MessageBox.Show(Loc.T("ccs.print_no_trip"),
                Loc.T("quality.no_trip_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var ldPrint = _presenter.FindLenhDonForTrip(_currentFrmLenhDonId, _txtPlate.Text);
        var data = new CcsTicketData(
            _txtTripNo.Text, _txtPlate.Text,
            _numBrix.Value, _numPol.Value, _numPolDoc.Value,
            _chkCcsPending.Checked ? null : _numCcs.Value, _chkCcsPending.Checked,
            _actualImpurity, _paidCcs, _paidImpurity, _paidPolicy,
            DateTime.Now,
            Factory: ldPrint?.TenNhaMay ?? _siteContext.CurrentSiteName,
            Owner: string.IsNullOrWhiteSpace(_txtOwner.Text) ? ldPrint?.KHName : _txtOwner.Text.Trim(),
            CaneForm: Printing.PrintText.CaneForm(ldPrint));

        using var previewForm = new Form
        {
            Text = Loc.T("ccs.print_form_title", data.TripNo),
            ClientSize = new Size(720, 960),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false,
            BackColor = AgrisTheme.Canvas,
            Font = AgrisTheme.Body()
        };

        var pnlPreview = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(230, 230, 230), AutoScroll = true };
        var receiptPanel = new Panel { Size = new Size(660, 880), BackColor = Color.White, Location = new Point(20, 14) };
        receiptPanel.GetType().GetProperty("DoubleBuffered",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(receiptPanel, true);
        receiptPanel.Paint += (_, pe) => DrawCcsTicket(pe.Graphics, new Rectangle(0, 0, receiptPanel.Width, receiptPanel.Height), data);
        pnlPreview.Controls.Add(receiptPanel);

        var btnPrint = GridBuilder.PrimaryButton("🖨  " + Loc.T("common.print"), new Size(130, 36));
        var btnClose = GridBuilder.SecondaryButton(Loc.T("common.close"), new Size(100, 36));
        btnPrint.Click += (_, _) =>
        {
            try
            {
                using var printDoc = new System.Drawing.Printing.PrintDocument();
                printDoc.DocumentName = Loc.T("ccs.print_form_title", data.TripNo);
                // In đúng mẫu đang xem trước (cùng hàm vẽ), co vào lề trang.
                printDoc.PrintPage += (_, pe) => DrawCcsTicket(pe.Graphics!, pe.MarginBounds, data);
                using var dialog = new PrintDialog { Document = printDoc, UseEXDialog = true };
                if (dialog.ShowDialog(previewForm) == DialogResult.OK) printDoc.Print();
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.T("ccs.print_error", ex.Message), Loc.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        btnClose.Click += (_, _) => previewForm.Close();

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 52,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8), BackColor = AgrisTheme.SurfaceMuted
        };
        toolbar.Controls.Add(btnClose);
        toolbar.Controls.Add(btnPrint);

        previewForm.Controls.Add(pnlPreview);
        previewForm.Controls.Add(toolbar);
        previewForm.ShowDialog(this);
    }

    /// <summary>
    /// Vẽ phiếu CCS: thông tin chuyến → kết quả phân tích (Bx, Pol, Pol cải chính) →
    /// THỰC TẾ (CCS + tạp chất) → THANH TOÁN theo chính sách FRM (CCS + tạp chất) → thời gian, mã vạch.
    /// Dùng chung cho xem trước và in thật.
    /// </summary>
    private static void DrawCcsTicket(Graphics g, Rectangle area, CcsTicketData d)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        const int m = 28;
        var left = area.Left + m;
        var right = area.Right - m;
        var width = right - left;
        var y = area.Top + m;

        using var borderPen = new Pen(AgrisTheme.BrandGreenDark, 2f);
        g.DrawRectangle(borderPen, area.Left + 2, area.Top + 2, area.Width - 4, area.Height - 4);

        // Font Latin cố định; chữ Khmer/Lào được PrintText đổi font theo đoạn. Nhãn song ngữ theo BU.
        const string family = "Segoe UI";
        using var companyFont = new Font(family, 12F, FontStyle.Bold);
        using var titleFont = new Font(family, 18F, FontStyle.Bold);
        using var titleBrush = new SolidBrush(AgrisTheme.BrandGreenDark);
        y += (int)Printing.PrintText.DrawCentered(g, Printing.PrintText.V(d.Factory), companyFont, Brushes.Black, left, width, y) + 6;
        y += (int)Printing.PrintText.DrawCentered(g, Printing.PrintText.L("ccs.print_title"), titleFont, titleBrush, left, width, y) + 12;

        using var lbl = new Font(family, 11F);
        using var val = new Font(family, 11F, FontStyle.Bold);
        using var big = new Font(family, 14F, FontStyle.Bold);
        using var section = new Font(family, 10.5F, FontStyle.Bold);
        using var text = new SolidBrush(AgrisTheme.TextPrimary);
        using var actualBrush = new SolidBrush(AgrisTheme.BrandGreenDark);
        using var paidBrush = new SolidBrush(AgrisTheme.BrandTealDark);
        using var sep = new Pen(AgrisTheme.Border, 1f);

        string? Num(decimal? v) => v is { } x ? x.ToString("N2") : null;
        string? Pct(decimal? v) => v is { } x ? $"{x:N2} %" : null;

        var labels = new[]
        {
            Printing.PrintText.L("ccs.print_label_reg_no"), Printing.PrintText.L("ccs.print_label_plate"),
            Printing.PrintText.L("ccs.print_label_customer"), Printing.PrintText.L("print.field_loai_mia"),
            Printing.PrintText.L("ccs.print_label_brix"), Printing.PrintText.L("ccs.print_label_pol"),
            Printing.PrintText.L("ccs.print_label_pol_doc"), Printing.PrintText.L("ccs.print_ccs_actual"),
            Printing.PrintText.L("ccs.print_tc_actual"), Printing.PrintText.L("ccs.print_ccs_paid"),
            Printing.PrintText.L("ccs.print_tc_paid"), Printing.PrintText.L("ccs.print_policy"),
            Printing.PrintText.L("ccs.print_label_time")
        };
        var vx = left + Printing.PrintText.LabelWidth(g, lbl, 210, labels);
        var lh = Math.Max(28, (int)Math.Ceiling(Printing.PrintText.Measure(g, "Ág ភ្ជ", val).Height) + 6);

        void Row(string label, string? value, Font font, Brush brush, int height)
        {
            Printing.PrintText.Field(g, label, value, lbl, font, brush, left, vx, y);
            y += Math.Max(height, lh);
        }

        void Line() { y += 4; g.DrawLine(sep, left, y, right, y); y += 10; }

        void Section(string caption, Brush brush)
        {
            Printing.PrintText.Draw(g, caption.ToUpperInvariant(), section, brush, left, y);
            y += Math.Max(26, lh - 2);
        }

        Row(labels[0], d.TripNo, val, text, 28);
        Row(labels[1], d.Plate, val, text, 28);
        Row(labels[2], d.Owner, val, text, 28);
        Row(labels[3], d.CaneForm, val, text, 28);
        Line();

        Section(Printing.PrintText.L("ccs.ux_input_title"), text);
        Row(labels[4], $"{d.Brix:N2}", val, text, 28);
        Row(labels[5], $"{d.Pol:N2}", val, text, 28);
        Row(labels[6], $"{d.PolDoc:N2}", val, text, 28);
        Line();

        Section(Printing.PrintText.L("ccs.ux_actual_title"), actualBrush);
        Row(labels[7], d.Pending ? Printing.PrintText.L("ccs.pending_value") : Num(d.Ccs), big, actualBrush, 32);
        Row(labels[8], Pct(d.ActualImpurity), big, actualBrush, 32);
        Line();

        Section(Printing.PrintText.L("ccs.print_payment_section"), paidBrush);
        Row(labels[9], Num(d.PaidCcs), big, paidBrush, 32);
        Row(labels[10], Pct(d.PaidImpurity), big, paidBrush, 32);
        Row(labels[11], d.Policy, val, text, 28);
        Line();

        Row(labels[12], d.PrintedAt.ToString("dd/MM/yyyy HH:mm:ss"), val, text, 28);

        if (!string.IsNullOrWhiteSpace(d.TripNo))
        {
            y += 8;
            Printing.Code128Renderer.Draw(g, d.TripNo, left, y, 320, 50);
            y += 54;
            using var bcFont = new Font(AgrisTheme.MonoFamily, 10F);
            g.DrawString(d.TripNo, bcFont, Brushes.Black, left, y);
        }

        var printLabel = Printing.PrintText.L("print.field_printed_at") + ": " + (d.PrintedAt.ToString("dd/MM/yyyy HH:mm"));
        using var tsFont = new Font(family, 9.5F, FontStyle.Italic);
        using var tsBrush = new SolidBrush(AgrisTheme.TextMuted);
        var tsSz = Printing.PrintText.Measure(g, printLabel, tsFont);
        Printing.PrintText.DrawRight(g, printLabel, tsFont, tsBrush, right, area.Bottom - m - tsSz.Height);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _cts.Dispose();
        base.Dispose(disposing);
    }
}
