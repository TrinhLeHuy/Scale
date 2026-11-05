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
using TTC.Weighing.Domain.Common;

namespace TTC.Weighing.WinForms.Forms;

/// <summary>
/// Đo chữ đường (CCS). Bố cục từ trên xuống theo đúng thứ tự thao tác:
/// ① Tìm chuyến → ② Nhập Bx, Pol → ③ CCS + tạp chất THỰC TẾ (tự tính) → ④ CCS + tạp chất THANH TOÁN (FRM).
/// Không hiển thị thông tin khách hàng / phiếu cân. Hệ số xơ (Fibre) lấy từ DigiScale Portal, app cân không sửa;
/// chỉ quản lý mới xem được số (người khác thấy "•••", bấm nút Fibre phải nhờ quản lý xác thực).
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

    // ── Realtime: Fibre (DigiScale) + danh sách xe làm mới định kỳ ──
    private const int LIVE_REFRESH_MS = 30_000;   // danh sách xe ra trong ngày
    private const int FIBRE_REFRESH_MS = 3_000;   // Fibre từ DigiScale Portal — cập nhật gần như tức thì
    private readonly System.Windows.Forms.Timer _liveTimer = new() { Interval = LIVE_REFRESH_MS };
    private readonly System.Windows.Forms.Timer _fibreTimer = new() { Interval = FIBRE_REFRESH_MS };
    private bool _fibreBusy;
    private string? _lastRulesState;
    /// <summary>Lần lấy Quy tắc cân gần nhất thành công (Fibre đang dùng là mới nhất).</summary>
    private bool _rulesLive;
    private bool _liveBusy;
    /// <summary>Đang ghi CCS — không làm mới lưới / Fibre giữa chừng.</summary>
    private bool _recording;
    /// <summary>Cảnh báo cố định khi không lấy được Fibre mới nhất từ DigiScale.</summary>
    private readonly Label _lblRulesWarning = new NoticeLabel
    {
        Font = AgrisTheme.Body(9F, FontStyle.Bold),
        ForeColor = AgrisTheme.Danger,
        BackColor = AgrisTheme.DangerSoft,
        Dock = DockStyle.Top,
        Height = 48,
        Padding = new Padding(14, 0, 8, 0),
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true,
        Visible = false
    };

    // ── Chọn xe đo CCS chung (một mẫu cho cả ngày): mặc định chọn mọi xe chưa có CCS ──
    /// <summary>Xe chưa có CCS mà người dùng đã BỎ chọn trong ngày đang xem (mặc định là chọn).</summary>
    private readonly HashSet<Guid> _uncheckedTrips = [];
    /// <summary>Xe chưa có CCS đang hiện trên lưới (thứ tự lưới).</summary>
    private List<(Guid TripId, string TripNo, string? Plate)> _missingTrips = [];
    private readonly CheckBox _chkSelectAll = new()
    {
        Text = Loc.T("ccs.ux_select_all"),
        AutoSize = true,
        Checked = true,
        Cursor = Cursors.Hand,
        Margin = new Padding(16, 6, 0, 0)
    };
    private bool _settingSelectAll;

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

        _btnRecord.Click += async (_, _) => await RecordCcsAsync();
        // Xem / in lại phiếu cần quyền quản lý (phiếu tự hiện sau khi ghi + đẩy DigiScale thì không).
        _btnPrint.Click += (_, _) =>
        {
            if (_currentTripId is not null && !Helpers.ManagerAccess.RequireReprint(this, _txtTripNo.Text)) return;
            // CCS đã ghi và phiếu đã hiện trước đó → BẢN IN LẠI (như Đăng tài).
            // Vừa ghi mà phiếu chưa hiện lần nào (vd đẩy DigiScale lỗi) → bản in đầu.
            PrintCcsTicket(isReprint: _ccsRecorded
                && !(_currentTripId is { } id && _unprintedCcsTrips.Contains(id)));
        };
        _btnRetryDigi.Click += async (_, _) => await RetryPushDigiAsync();
        _btnReset.Click += (_, _) => ResetForm();
        _btnFibre.Click += async (_, _) => await ShowFibreInfoAsync(); // chỉ xem, không sửa
        _liveTimer.Tick += async (_, _) => await LiveRefreshAsync();
        _fibreTimer.Tick += async (_, _) => await RefreshFibreTickAsync();
        _eventBus.TripChanged += OnTripChangedFromOtherForm;
        // Máy quét QR (số đăng tài trên phiếu) — màn CCS không có ô tìm, quét ở đâu cũng mở đúng chuyến.
        QrScanner.Register(this, () => null, async content =>
        {
            if (_recording)
            {
                // Đang ghi CCS → không đổi chuyến giữa chừng; báo để quét lại khi ghi xong.
                _lblStatus.Text = "⚠ " + Loc.T("scan.busy_retry", content);
                _lblStatus.ForeColor = AgrisTheme.Warning;
                return;
            }
            _lastQrContent = content;
            await ScanAndLoadTripAsync(content);
        });
        _btnPayment.Click += async (_, _) => await FetchPaymentFiguresAsync(showDialogOnError: true);
        _chkCcsPending.CheckedChanged += (_, _) => AutoCalcCcs();

        _lblStatus.ForeColorChanged += (_, _) => _lblStatus.BackColor = SoftBackground(_lblStatus.ForeColor);
        Load += async (_, _) =>
        {
            await ApplyPortalRulesAsync();
            await LoadAllCcsAsync();
            _liveTimer.Start();
            _fibreTimer.Start();
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
        // Khung "Kết quả thanh toán" không hiển thị trên màn đo CCS (yêu cầu vận hành).
        // Số thanh toán FRM vẫn tự lấy sau khi ghi CCS (gửi DigiScale + in phiếu) — chỉ ẩn khỏi màn hình.
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

        // Một ngày xe ra (không phải khoảng ngày): danh sách xe và lượt đo CCS chung đều theo ngày này.
        var tripBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 42,
            FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            Padding = new Padding(0, 0, 0, 0), BackColor = AgrisTheme.Surface
        };
        _dtpCcsDate.Value = VnTime.Today;
        _dtpCcsDate.MaxDate = VnTime.Today.AddDays(1);
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
        _btnToday.Click += (_, _) => _dtpCcsDate.Value = VnTime.Today;

        // Chip chuyến đang đo nằm bên phải tiêu đề "Tìm chuyến".
        _chipTrip.Margin = new Padding(0, 0, 0, 0);
        header.Actions.Padding = new Padding(0, 14, 12, 0);
        header.Actions.Controls.Add(_chipTrip);

        var statusGap = new Panel { Dock = DockStyle.Top, Height = 8, BackColor = AgrisTheme.Surface };

        body.Controls.Add(_lblStatus);
        body.Controls.Add(statusGap);
        body.Controls.Add(_lblRulesWarning);
        body.Controls.Add(tripBar);
        // Chừa chỗ cho dải cảnh báo Fibre (ẩn khi Fibre là bản mới nhất).
        body.Height = tripBar.Height + _lblRulesWarning.Height + statusGap.Height + _lblStatus.Height + body.Padding.Vertical;

        card.Controls.Add(body);
        card.Controls.Add(header);
        card.Height = header.Height + body.Height + card.Padding.Vertical;
        return card;
    }

    /// <summary>② Kết quả phân tích — chỉ nhập tay Bx và Pol. Enter: Bx → Pol → Ghi nhận.</summary>
    private Card BuildInputCard()
    {
        var card = new Card { Dock = DockStyle.Top, Padding = new Padding(1) };

        // Hệ số xơ: lấy từ DigiScale Portal, không sửa được. Số chỉ hiện với quản lý (xem FibreDisplay).
        _btnFibre.Visible = true;
        _btnFibre.Size = new Size(150, 32);
        _btnFibre.Margin = new Padding(0);
        _hdrInput.Actions.Controls.Add(_btnFibre);

        _tileBrix = new FieldTile(Loc.T("ccs.print_label_brix"), _numBrix, Loc.T("ccs.ux_hint_bx"));
        _tilePol = new FieldTile(Loc.T("ccs.print_label_pol"), _numPol, Loc.T("ccs.ux_hint_pol"));
        var inputRow = TileRow([_tileBrix, _tilePol], [50, 50]);

        FieldTile.ConfigureNumeric(_numBrix, () => _numPol);
        FieldTile.ConfigureNumeric(_numPol, () => _btnRecord);
        _numBrix.ValueChanged += (_, _) => OnReadingEdited(CcsInstrument.Brix);
        _numPol.ValueChanged += (_, _) => OnReadingEdited(CcsInstrument.Pol);
        // Đang xem CCS đã ghi → giữ đúng số đã ghi, Fibre mới không tính lại.
        _numFibre.ValueChanged += (_, _) => { if (!_fieldsReadOnly) AutoCalcCcs(); };

        // Nguồn số đo: máy Brix / máy Pol, hoặc nhập tay (quản lý duyệt bằng tài khoản + mật khẩu).
        // Ba nút chia theo tỉ lệ bề rộng thẻ (không đặt cứng px): màn hình hẹp thì nút co lại, chữ thừa
        // hiện "…", thay vì nút "Nhập tay" bị đẩy ra ngoài thẻ.
        var sourceRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 58,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(14, 6, 14, 6),
            BackColor = AgrisTheme.Surface
        };
        sourceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
        sourceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
        sourceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
        sourceRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        var sourceButtons = new[] { _btnReadBrix, _btnReadPol, _btnManual };
        for (var i = 0; i < sourceButtons.Length; i++)
        {
            var b = sourceButtons[i];
            b.Dock = DockStyle.Fill;
            b.MinimumSize = Size.Empty;
            b.Margin = new Padding(0, 0, i < sourceButtons.Length - 1 ? 10 : 0, 0);
            sourceRow.Controls.Add(b, i, 0);
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

        var canMeasure = HasMeasureTarget && !_fieldsReadOnly;
        _btnReadBrix.Enabled = canMeasure;
        _btnReadPol.Enabled = canMeasure;
        _btnManual.Enabled = canMeasure && _manualApproval is null;
        _btnManual.Text = _manualApproval is null
            ? "🔒  " + Loc.T("ccs.ux_btn_manual")
            : "🔓  " + Loc.T("ccs.ux_btn_manual_approved", ApproverName(_manualApproval));
    }

    /// <summary>Đọc một lần số Brix hoặc Pol từ máy đo. Không đọc được → báo rõ, không tự điền số.</summary>
    /// <summary>Có đối tượng để đo: một chuyến đang mở, hoặc ít nhất một xe được chọn để đo chung.</summary>
    private bool HasMeasureTarget => _currentTripId is not null || SelectedTrips().Count > 0;

    private async Task ReadFromDeviceAsync(CcsInstrument instrument)
    {
        if (!HasMeasureTarget)
        {
            Helpers.AppMessageBox.Show(Loc.T("ccs.ux_need_trip"), Loc.T("common.missing_info"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        if (!HasMeasureTarget || _fieldsReadOnly) return;

        var approval = ApprovalDialog.Request(this, ApprovalLevel.Manager,
            _currentTripId is not null
                ? Loc.T("ccs.ux_manual_approval", _txtTripNo.Text)
                : Loc.T("ccs.ux_manual_approval_batch", SelectedTrips().Count));
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
        // Màn đo chữ đường chỉ hiện CCS thực tế; ô "Tạp chất thực tế" bỏ khỏi màn hình (yêu cầu vận hành).
        // Tạp chất vẫn nạp ngầm để in phiếu CCS như trước.
        var row = MetricRow(_tileCcsActual, null);
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
    /// <remarks><paramref name="b"/> = null → một ô chiếm cả hàng.</remarks>
    private static TableLayoutPanel MetricRow(MetricTile a, MetricTile? b)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = b is null ? 1 : 2,
            RowCount = 1,
            BackColor = AgrisTheme.Surface,
            Padding = new Padding(14, 12, 14, 14)
        };
        a.Dock = DockStyle.Fill;
        if (b is null)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            a.Margin = Padding.Empty;
            row.Controls.Add(a, 0, 0);
        }
        else
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            b.Dock = DockStyle.Fill;
            a.Margin = new Padding(0, 0, 6, 0);
            b.Margin = new Padding(6, 0, 0, 0);
            row.Controls.Add(a, 0, 0);
            row.Controls.Add(b, 1, 0);
        }
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
        _chkSelectAll.ForeColor = AgrisTheme.BrandTealDark;
        _chkSelectAll.Font = AgrisTheme.Body(9F, FontStyle.Bold);
        filter.Controls.Add(_chkSelectAll);
        filter.Controls.Add(_chkOnlyMissing);
        _chkOnlyMissing.Margin = new Padding(16, 6, 0, 0);
        _chkSelectAll.Margin = new Padding(0, 6, 0, 0);
        _chkOnlyMissing.CheckedChanged += async (_, _) => await LoadAllCcsAsync();
        _chkSelectAll.CheckedChanged += (_, _) => OnSelectAllChanged();

        var hint = new Label
        {
            Text = Loc.T("ccs.ux_day_hint"), Dock = DockStyle.Top, Height = 24,
            Font = AgrisTheme.Caption(), ForeColor = AgrisTheme.TextMuted,
            Padding = new Padding(14, 0, 8, 4), BackColor = AgrisTheme.Surface
        };

        _grid.Dock = DockStyle.Fill;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.AddRange(
            new DataGridViewCheckBoxColumn { HeaderText = Loc.T("ccs.ux_col_select"), MinimumWidth = 50, FillWeight = 9, Name = "Select" },
            new DataGridViewTextBoxColumn { HeaderText = Loc.T("ccs.print_label_reg_no"), MinimumWidth = 90, FillWeight = 22, Name = "TripNo" },
            new DataGridViewTextBoxColumn { HeaderText = Loc.T("col.lenhdon_code"), MinimumWidth = 110, FillWeight = 24, Name = "LenhDon" },
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
        _uncheckedTrips.Clear(); // ngày mới: mặc định chọn tất cả xe chưa có CCS
        ResetForm();
        await LoadAllCcsAsync();
    }

    /// <summary>[Đầu ngày, đầu ngày hôm sau) theo giờ máy của ngày nhập CCS.</summary>
    private (DateTimeOffset From, DateTimeOffset To) SelectedDayRange()
    {
        var day = _dtpCcsDate.Value.Date;
        var from = new DateTimeOffset(day, VnTime.OffsetAt(day));
        var next = day.AddDays(1);
        // Npgsql chỉ nhận DateTimeOffset offset 0 cho cột timestamptz.
        return (from.ToUniversalTime(), new DateTimeOffset(next, VnTime.OffsetAt(next)).ToUniversalTime());
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
    /// Kiểm tra chung trước khi ghi (một xe hay nhiều xe): có CCS / chờ kết quả, nguồn số đo, ngưỡng Portal.
    /// </summary>
    private bool ValidateReadingsBeforeRecord()
    {
        if (!_chkCcsPending.Checked && _numCcs.Value <= 0)
        {
            Helpers.AppMessageBox.Show(_ccsIssue ?? Loc.T("ccs.ux_need_ccs"), Loc.T("ccs.ux_ccs_fail_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _numPol.Focus();
            return false;
        }

        // Mỗi số Brix/Pol phải có nguồn: đọc từ máy, hoặc nhập tay đã được quản lý duyệt.
        if (!_chkCcsPending.Checked && (_brixSource == ReadingSource.None || _polSource == ReadingSource.None))
        {
            Helpers.AppMessageBox.Show(Loc.T("ccs.ux_need_reading"), Loc.T("common.missing_info"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        return _chkCcsPending.Checked || ConfirmCcsSettings(_numCcs.Value);
    }

    /// <summary>
    /// Đo CCS một lần cho mọi xe đã chọn của ngày xe ra: cùng Bx / Pol / CCS ghi cho từng chuyến
    /// (mỗi chuyến một bản ghi riêng, đẩy DigiScale theo lệnh đốn của chuyến đó, lấy số thanh toán FRM).
    /// Một xe lỗi không chặn các xe còn lại — cuối cùng báo tổng hợp.
    /// </summary>
    private async Task RecordBatchAsync()
    {
        var targets = SelectedTrips();
        if (targets.Count == 0)
        {
            Helpers.AppMessageBox.Show(Loc.T("ccs.ux_need_trip"), Loc.T("common.missing_info"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!await EnsureLatestFibreAsync()) return;
        if (!ValidateReadingsBeforeRecord()) return;

        var day = _dtpCcsDate.Value.ToString("dd/MM/yyyy");
        var confirm = _chkCcsPending.Checked
            ? Loc.T("ccs.ux_batch_confirm_pending_msg", targets.Count, day)
            : Loc.T("ccs.ux_batch_confirm_msg", $"{_numCcs.Value:N2}", $"{_numBrix.Value:N2}", $"{_numPol.Value:N2}", targets.Count, day);
        if (Helpers.AppMessageBox.Show(this, confirm, Loc.T("ccs.ux_batch_confirm_title"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        var pending = _chkCcsPending.Checked;
        var brix = _numBrix.Value;
        var pol = _numPol.Value;
        var polDoc = _numPolDoc.Value;
        var ccs = _numCcs.Value;
        var anyManual = _brixSource == ReadingSource.Manual || _polSource == ReadingSource.Manual;
        var source = pending ? "FRM" : anyManual ? "MANUAL" : "DEVICE";
        var reason = pending ? null : $"Brix={_brixSource}; Pol={_polSource}; Batch={targets.Count}";
        var approvalRef = anyManual ? _manualApproval?.ApprovalRef : null;
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        var recorded = 0;
        var pushed = 0;
        var failures = new List<string>();
        var pushedTrips = new List<(Guid TripId, string TripNo)>();
        var paidByTrip = new Dictionary<Guid, PaymentFiguresResult>();

        _btnRecord.Enabled = false;
        _recording = true;
        try
        {
            for (var i = 0; i < targets.Count; i++)
            {
                var (tripId, tripNo, plate) = targets[i];
                _lblStatus.Text = Loc.T("ccs.ux_batch_progress", i + 1, targets.Count, tripNo);
                _lblStatus.ForeColor = AgrisTheme.Info;

                try
                {
                    var result = await _presenter.RecordCcsAsync(
                        new ReceiveCcsResultCommand(
                            TripId: tripId,
                            CcsValue: pending ? null : ccs,
                            Source: source,
                            SourceReportsPending: pending,
                            CorrelationId: Guid.NewGuid().ToString("N"),
                            ApprovalRef: approvalRef,
                            Reason: reason,
                            Brix: pending ? null : brix,
                            Pol: pending ? null : pol,
                            PolCorrected: pending ? null : polDoc),
                        _cts.Token);
                    if (!result.IsSuccess)
                    {
                        failures.Add(Loc.T("ccs.ux_batch_failed_line", tripNo, result.FailureReason));
                        continue;
                    }
                    recorded++;
                    _unprintedCcsTrips.Add(tripId); // vừa ghi: lần in đầu không phải bản in lại

                    _eventBus.Publish(new TripChangedEventArgs
                    {
                        TripId = tripId, TripNo = tripNo, Plate = plate ?? "",
                        ChangeType = TripChangeType.CcsReceived, SourceForm = "CcsForm"
                    });

                    // Chờ kết quả → không đẩy phiếu số 0 lên DigiScale.
                    if (pending || brix <= 0 || pol <= 0 || ccs <= 0) continue;

                    // Lệnh đốn của TỪNG chuyến (mỗi xe một lệnh đốn / BU riêng).
                    var ctx = await _presenter.ScanQrAsync(tripNo, _siteContext.CurrentSiteId, _cts.Token);
                    var ld = _presenter.FindLenhDonForTrip(ctx.FrmLenhDonId, ctx.Plate ?? plate ?? "");
                    var push = await _presenter.PushCcsAsync(
                        ctx.FrmLenhDonId ?? ld?.SourceKey ?? tripNo,
                        ctx.FrmMaLenhDon ?? ld?.MaLD ?? tripNo,
                        "0", "1", VnTime.Now,
                        Num.S2(brix), Num.S2(ccs),
                        Num.S2(pol), Num.S2(polDoc), "1",
                        Program.LoggedInUserName, ctx.FrmLenhDonBu ?? ld?.BusinessUnit, _cts.Token);
                    if (!push.IsSuccess)
                    {
                        failures.Add(Loc.T("ccs.ux_batch_failed_line", tripNo,
                            Helpers.DigiScaleErrors.Describe(push.ErrorMessage).Reason));
                        continue;
                    }
                    pushed++;
                    pushedTrips.Add((tripId, tripNo));

                    // CCS đã lên DigiScale → lấy + gửi số thanh toán theo chính sách FRM (như ghi từng xe).
                    var paid = await _presenter.FetchPaymentFiguresAsync(tripId, _cts.Token);
                    paidByTrip[tripId] = paid;
                    if (paid.Error is not null)
                        failures.Add(Loc.T("ccs.ux_batch_failed_line", tripNo,
                            Helpers.DigiScaleErrors.Describe(paid.Error).Reason));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    failures.Add(Loc.T("ccs.ux_batch_failed_line", tripNo, ex.Message));
                }
            }
        }
        catch (OperationCanceledException) { return; }
        finally
        {
            _recording = false;
            _btnRecord.Enabled = true;
        }

        var summary = Loc.T("ccs.ux_batch_done_msg", recorded, targets.Count, pushed);
        if (failures.Count > 0)
            summary += Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, failures);
        Helpers.DigiScalePopup.Show(this, Loc.T("ccs.ux_batch_done_title"), summary,
            failures.Count == 0, failures.Count == 0 ? "OK" : Loc.T("common.error"));

        ResetForm();
        await LoadAllCcsAsync();

        // Hoàn thành + đã đẩy DigiScale → hiện phiếu CCS từng xe để in.
        await ShowBatchTicketsAsync(pushedTrips, paidByTrip);
    }

    /// <summary>Hiện phiếu CCS của chuyến đang mở; lỗi xem trước / máy in không ảnh hưởng CCS đã ghi.</summary>
    private void ShowTicketAfterCompletion()
    {
        try { PrintCcsTicket(isReprint: false); } // phiếu tự hiện ngay sau khi ghi = bản in đầu
        catch (Exception ex)
        {
            _lblStatus.Text = "⚠ " + Loc.T("weigh.auto_print_failed", ex.Message);
            _lblStatus.ForeColor = AgrisTheme.Warning;
        }
    }

    /// <summary>
    /// Đo chung xong: hiện lần lượt phiếu CCS của các xe đã đẩy DigiScale thành công (hỏi một lần trước khi mở).
    /// Số thanh toán lấy từ kết quả vừa nhận khi ghi, không gọi lại DigiScale.
    /// </summary>
    private async Task ShowBatchTicketsAsync(
        List<(Guid TripId, string TripNo)> trips, Dictionary<Guid, PaymentFiguresResult> paid)
    {
        if (trips.Count == 0) return;
        if (trips.Count > 1 && Helpers.AppMessageBox.Show(this, Loc.T("ccs.ux_batch_print_confirm", trips.Count),
                Loc.T("ccs.ux_batch_done_title"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        foreach (var (tripId, tripNo) in trips)
        {
            _lastQrContent = tripNo;
            await ScanAndLoadTripAsync(tripNo); // nạp chuyến ở chế độ xem (đúng số đã ghi)
            if (_currentTripId != tripId) continue;

            if (paid.TryGetValue(tripId, out var r) && r.HasPolicy)
            {
                _paidCcs = r.CcsThanhToan;
                _paidImpurity = r.TapChatThanhToan;
                _paidPolicy = r.ChinhSach;
                _tileCcsPaid.SetValue(r.CcsThanhToan is { } c ? $"{c:N2}" : "—");
                _tileTcPaid.SetValue(r.TapChatThanhToan is { } t ? $"{t:N2} %" : "—");
            }
            ShowTicketAfterCompletion();
        }
        ResetForm();
    }

    private async Task RecordCcsAsync()
    {
        // Không mở chuyến cụ thể → đo chung cho các xe đã chọn trong ngày.
        if (_currentTripId is null)
        {
            await RecordBatchAsync();
            return;
        }

        if (!_chkCcsPending.Checked && _numCcs.Value <= 0)
        {
            Helpers.AppMessageBox.Show(_ccsIssue ?? Loc.T("ccs.ux_need_ccs"), Loc.T("ccs.ux_ccs_fail_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _numPol.Focus();
            return;
        }

        // Mỗi số Brix/Pol phải có nguồn: đọc từ máy, hoặc nhập tay đã được quản lý duyệt.
        if (!_chkCcsPending.Checked && (_brixSource == ReadingSource.None || _polSource == ReadingSource.None))
        {
            Helpers.AppMessageBox.Show(Loc.T("ccs.ux_need_reading"), Loc.T("common.missing_info"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Fibre phải là bản mới nhất trên DigiScale (đổi → CCS tính lại, dừng để xem; mất kết nối → cảnh báo).
        if (!await EnsureLatestFibreAsync()) return;
        if (!_chkCcsPending.Checked && !ConfirmCcsSettings(_numCcs.Value)) return;

        var anyManual = _brixSource == ReadingSource.Manual || _polSource == ReadingSource.Manual;
        var source = _chkCcsPending.Checked ? "FRM" : anyManual ? "MANUAL" : "DEVICE";

        _btnRecord.Enabled = false;
        _recording = true;
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
                Helpers.AppMessageBox.Show(result.FailureReason, Loc.T("quality.ccs_failed_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _unprintedCcsTrips.Add(_currentTripId.Value); // vừa ghi: lần in đầu không phải bản in lại

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
                    "0", "1", VnTime.Now,
                    Num.S2(_numBrix.Value), Num.S2(_numCcs.Value),
                    Num.S2(_numPol.Value), Num.S2(_numPolDoc.Value), "1",
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

            // Hoàn thành + đã đẩy DigiScale → hiện phiếu CCS để in (như Đăng tài).
            if (digiOk) ShowTicketAfterCompletion();
        }
        catch (Exception ex)
        {
            Helpers.AppMessageBox.Show(ex.Message, Loc.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _recording = false;
            _btnRecord.Enabled = _numBrix.Enabled;
        }
    }

    /// <summary>
    /// Kéo Quy tắc cân mới nhất từ DigiScale Portal (Fibre, CCS Min/Max) — gọi khi mở màn, mỗi
    /// <see cref="LIVE_REFRESH_MS"/> và ngay trước khi ghi CCS. Hệ số xơ CHỈ lấy từ Portal (app cân không sửa).
    /// Trả số Fibre cũ nếu Portal vừa đổi (để báo người dùng), null nếu không đổi.
    /// Không lấy được bản mới → hiện cảnh báo cố định (đang dùng số lấy lúc nào, có thể đã đổi).
    /// </summary>
    private async Task<decimal?> ApplyPortalRulesAsync()
    {
        try { await PortalScaleRules.RefreshAsync(_cts.Token); }
        catch (OperationCanceledException) { return null; }

        var settings = SugarcaneFlowSettings.Current;
        var oldFibre = _numFibre.Value;
        var newFibre = settings.CcsFibre is { } f ? Math.Clamp(f, _numFibre.Minimum, _numFibre.Maximum) : 0m;
        if (newFibre != oldFibre) _numFibre.Value = newFibre; // Portal bỏ Fibre → 0 → chặn ghi CCS

        _rulesLive = PortalScaleRules.LastError is null && settings.PortalRulesLoaded;
        RefreshFibreUi();
        NotifyFibreTransition();

        // Làm mới mỗi 3s: chỉ ghi lên dải trạng thái khi tình trạng ĐỔI — không đè thông báo đang hiện mỗi lần.
        var rulesState = !settings.PortalRulesLoaded ? "not_loaded" : settings.CcsFibre is null ? "no_fibre" : "ok";
        var stateChanged = rulesState != _lastRulesState;
        _lastRulesState = rulesState;

        if (!settings.PortalRulesLoaded)
        {
            if (stateChanged)
            {
                _lblStatus.Text = Loc.T("scale_rules.not_loaded_status");
                _lblStatus.ForeColor = AgrisTheme.Warning;
            }
        }
        else if (settings.CcsFibre is null)
        {
            if (stateChanged)
            {
                _lblStatus.Text = Loc.T("ccs.fibre_not_configured_status");
                _lblStatus.ForeColor = AgrisTheme.Warning;
            }
        }
        else if (oldFibre > 0 && newFibre != oldFibre)
        {
            _lblStatus.Text = Loc.T("ccs.ux_fibre_changed", FibreDisplay(oldFibre), FibreDisplay(newFibre));
            _lblStatus.ForeColor = AgrisTheme.Warning;
        }

        return oldFibre > 0 && newFibre != oldFibre ? oldFibre : null;
    }

    /// <summary>
    /// Tài khoản đang đăng nhập có quyền quản lý (vai trò / quyền duyệt cấp Quản lý trong cấu hình SugarcaneFlow).
    /// Chưa cấu hình vai trò quản lý → coi là không (phải nhờ quản lý xác thực).
    /// </summary>
    private static bool IsLoggedInManager() => Helpers.ManagerAccess.IsLoggedInManager();

    private const string FIBRE_MASK = "***";

    /// <summary>
    /// Hệ số xơ trên màn hình: CÓ dữ liệu → "***" (không lộ số); KHÔNG có → "Trống".
    /// Số thật chỉ hiện trong hộp "Hệ số xơ" sau khi quản lý xác thực (<paramref name="reveal"/>).
    /// </summary>
    private string FibreDisplay(decimal value, bool reveal = false)
        => value <= 0 ? Loc.T("ccs.ux_fibre_empty") : reveal ? value.ToString("N2") : FIBRE_MASK;

    /// <summary>Lần trước lấy Fibre thành công — để báo lỗi NGAY khi vừa mất (một lần, không lặp mỗi 3s).</summary>
    private bool? _fibreWasLive;

    /// <summary>
    /// Nút Fibre (chỉ xem): "Fibre: *** · 09:41:05" — giờ lần lấy thành công gần nhất (mỗi 3s) để người dùng thấy
    /// app cân đang lấy được hệ số xơ mới nhất. Không lấy được → chữ cam + dải cảnh báo + popup báo lỗi ngay.
    /// </summary>
    private void RefreshFibreUi()
    {
        var fibreText = FibreDisplay(_numFibre.Value);
        var loadedClock = PortalScaleRules.LoadedAt?.ToString("HH:mm:ss");
        _btnFibre.Text = "ⓘ  " + (loadedClock is null
            ? Loc.T("ccs.ux_fibre_btn", fibreText)
            : Loc.T("ccs.ux_fibre_btn_at", fibreText, loadedClock));
        _btnFibre.AutoSize = true;
        _btnFibre.ForeColor = _rulesLive && _numFibre.Value > 0 ? AgrisTheme.Success : AgrisTheme.Warning;

        var loadedAt = PortalScaleRules.LoadedAt?.ToString("dd/MM HH:mm:ss") ?? "—";
        var error = PortalScaleRules.LastError ?? "—";
        _lblRulesWarning.Text = PortalScaleRules.IsLoaded
            ? Loc.T("ccs.ux_rules_stale", error, fibreText, loadedAt)
            : Loc.T("ccs.ux_rules_stale_never", error);
        _lblRulesWarning.Visible = !_rulesLive;
    }

    /// <summary>
    /// Vừa mất kết nối lấy hệ số xơ (lần trước lấy được, lần này không) → popup báo lỗi ngay một lần.
    /// Lấy lại được → ghi dòng trạng thái "đã lấy lại". Gọi sau mỗi lượt làm mới 3s.
    /// </summary>
    private void NotifyFibreTransition()
    {
        var live = _rulesLive;
        var was = _fibreWasLive;
        _fibreWasLive = live;
        if (was == live) return;

        if (!live && was is not false)
        {
            var friendly = Helpers.DigiScaleErrors.Describe(PortalScaleRules.LastError ?? Loc.T("ccs.ux_fibre_empty"));
            _lblStatus.Text = "⚠ " + Loc.T("ccs.ux_fibre_lost_title");
            _lblStatus.ForeColor = AgrisTheme.Danger;
            Helpers.DigiScalePopup.ShowAlert(this, Helpers.AlertKind.Error, Loc.T("ccs.ux_fibre_lost_title"),
                Loc.T("ccs.ux_fibre_lost_reason", friendly.Reason,
                    PortalScaleRules.LoadedAt?.ToString("dd/MM/yyyy HH:mm:ss") ?? "—"),
                Loc.T("ccs.ux_fibre_lost_action"));
        }
        else if (live && was == false)
        {
            _lblStatus.Text = "✓ " + Loc.T("ccs.ux_fibre_back");
            _lblStatus.ForeColor = AgrisTheme.Success;
        }
    }

    /// <summary>Xem hệ số xơ — chỉ đọc. Muốn đổi phải sửa trên DigiScale Portal.</summary>
    private async Task ShowFibreInfoAsync()
    {
        // Xem số Fibre cần quyền quản lý: tài khoản quản lý xem ngay, người khác phải nhờ quản lý xác thực.
        if (!IsLoggedInManager()
            && ApprovalDialog.Request(this, ApprovalLevel.Manager, Loc.T("ccs.ux_fibre_view_approval")) is null)
            return;

        await ApplyPortalRulesAsync();
        var fibreText = FibreDisplay(_numFibre.Value, reveal: true);
        Helpers.AppMessageBox.Show(this,
            Loc.T("ccs.ux_fibre_info_msg", fibreText, PortalScaleRules.LoadedAt?.ToString("dd/MM/yyyy HH:mm:ss") ?? "—"),
            Loc.T("ccs.ux_fibre_info_title"), MessageBoxButtons.OK,
            _rulesLive ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    /// <summary>
    /// Ngay trước khi ghi: lấy lại Fibre mới nhất. Portal vừa đổi → CCS đã tính lại, dừng để người dùng xem lại.
    /// Không lấy được → cảnh báo, chỉ ghi khi người dùng xác nhận dùng số cũ. Trả true nếu được ghi tiếp.
    /// </summary>
    private async Task<bool> EnsureLatestFibreAsync()
    {
        if (_chkCcsPending.Checked) return true; // chờ kết quả: không tính CCS

        var oldFibre = await ApplyPortalRulesAsync();
        if (oldFibre is { } before)
        {
            Helpers.AppMessageBox.Show(this,
                Loc.T("ccs.ux_fibre_changed_before_record", FibreDisplay(before), FibreDisplay(_numFibre.Value),
                    _numCcs.Value > 0 ? _numCcs.Value.ToString("N2") : "—"),
                Loc.T("ccs.ux_fibre_info_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (_rulesLive) return true;
        return Helpers.AppMessageBox.Show(this,
            Loc.T("ccs.ux_stale_confirm_msg", FibreDisplay(_numFibre.Value),
                PortalScaleRules.LoadedAt?.ToString("dd/MM/yyyy HH:mm") ?? "—"),
            Loc.T("ccs.ux_stale_confirm_title"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }

    /// <summary>Làm mới định kỳ: Fibre từ DigiScale + danh sách xe ra trong ngày (không chạy khi đang ghi).</summary>
    private async Task LiveRefreshAsync()
    {
        if (_recording || _liveBusy || IsDisposed) return;
        _liveBusy = true; // khoá chạy chồng
        try
        {
            // Fibre đã làm mới mỗi 3s ở _fibreTimer — ở đây chỉ tải lại danh sách xe.
            if (!_recording) await LoadAllCcsAsync();
        }
        catch (OperationCanceledException) { }
        finally { _liveBusy = false; }
    }

    /// <summary>
    /// Lấy Fibre từ DigiScale mỗi <see cref="FIBRE_REFRESH_MS"/>: Portal đổi là màn hình đổi theo (CCS tính lại).
    /// Bỏ qua khi lần trước chưa xong hoặc đang ghi CCS (lúc ghi đã tự lấy lại Fibre mới nhất).
    /// </summary>
    private async Task RefreshFibreTickAsync()
    {
        if (_fibreBusy || _recording || IsDisposed) return;
        _fibreBusy = true;
        try { await ApplyPortalRulesAsync(); }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        finally { _fibreBusy = false; }
    }

    private void OnTripChangedFromOtherForm(object? sender, TripChangedEventArgs e)
    {
        if (e.SourceForm == "CcsForm" || IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(async () => { if (!_recording) await LoadAllCcsAsync(); }); }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException) { }
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
            Helpers.AppMessageBox.Show(this, Loc.T("ccs.fibre_not_configured_msg"), Loc.T("ccs.fibre_not_configured_title"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (settings.CcsMin is null && settings.CcsMax is null)
        {
            return Helpers.AppMessageBox.Show(this, Loc.T("ccs.threshold_not_configured_msg", ccs.ToString("N2")),
                Loc.T("ccs.threshold_not_configured_title"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
                == DialogResult.Yes;
        }

        if ((settings.CcsMin is { } min && ccs < min) || (settings.CcsMax is { } max && ccs > max))
        {
            return Helpers.AppMessageBox.Show(this,
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
        _ccsRecorded = false;
        _currentFrmLenhDonId = null;
        _currentFrmMaLenhDon = null;
        _currentFrmLenhDonBu = null;
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
        ShowBatchSelection(); // về chế độ đo chung cho các xe đã chọn
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
    /// Danh sách xe mía RA trong ngày đã chọn (đã cân ra — giờ ra = lượt cân hiệu lực cuối cùng; không tính phiếu
    /// hủy / đã xoá) kèm CCS hiệu lực của từng xe. Xe chưa có CCS xếp trên, mặc định được chọn để đo CCS chung.
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
                            && t.Lifecycle == TTC.Weighing.Domain.Enums.TripLifecycle.Closed
                            && t.SourceSystem != "ERP")
                .Select(t => new
                {
                    t.Id,
                    t.TripNo,
                    t.TractorPlate,
                    LenhDon = t.ReferenceTripNo, // mã lệnh đốn của chuyến mía
                    ExitAt = _db.WeighEvents
                        .Where(w => w.TripId == t.Id && w.IsEffective)
                        .Max(w => (DateTimeOffset?)w.MeasuredAt),
                    Sample = _db.QualitySamples
                        .Where(s => s.TripId == t.Id && s.Type == ccsType && s.IsEffective)
                        .OrderByDescending(s => s.Revision)
                        .Select(s => new { s.CcsValue, s.Status, s.ReceivedAt })
                        .FirstOrDefault()
                })
                .Where(x => x.ExitAt >= from && x.ExitAt < to)
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
            _missingTrips = [];
            foreach (var r in visible)
            {
                var hasCcs = r.Sample?.CcsValue is not null;
                var pending = r.Sample is not null && r.Sample.CcsValue is null
                              && r.Sample.Status.ToString() == "Pending";
                var status = hasCcs ? "✓ " + Loc.T("ccs.ux_status_done")
                    : pending ? "⏳ " + Loc.T("ccs.ux_status_pending")
                    : "● " + Loc.T("ccs.ux_status_missing");

                if (!hasCcs) _missingTrips.Add((r.Id, r.TripNo, r.TractorPlate));
                var idx = _grid.Rows.Add(
                    !hasCcs && !_uncheckedTrips.Contains(r.Id),
                    r.TripNo,
                    r.LenhDon ?? "—",
                    r.TractorPlate,
                    r.Sample?.CcsValue is { } v ? v.ToString("N2") : "—",
                    status,
                    r.Sample?.ReceivedAt?.ToVnTime().ToString("HH:mm") ?? "");
                var row = _grid.Rows[idx];
                row.Tag = new CcsRowTag(r.Id, hasCcs);
                if (hasCcs) row.Cells["Select"].ReadOnly = true;
                row.Cells["Status"].Style.ForeColor = hasCcs ? AgrisTheme.Success
                    : pending ? AgrisTheme.Info : AgrisTheme.Warning;
                row.Cells["Status"].Style.Font = AgrisTheme.Body(9F, FontStyle.Bold);
            }
            _grid.ResumeLayout();
            SyncSelectAllBox();
            if (_currentTripId is null) ShowBatchSelection(onlyIfChanged: true);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _hdrDay.SetSubtitle(Loc.T("ccs.ux_day_load_error", ex.Message));
        }
    }

    private sealed record CcsRowTag(Guid TripId, bool HasCcs);

    /// <summary>Xe chưa có CCS đang được chọn để đo chung.</summary>
    private List<(Guid TripId, string TripNo, string? Plate)> SelectedTrips()
        => _missingTrips.Where(t => !_uncheckedTrips.Contains(t.TripId)).ToList();

    /// <summary>
    /// Bấm lưới: xe đã có CCS → xem / in lại; xe chưa có → bật/tắt chọn (đo CCS chung cho các xe đã chọn).
    /// </summary>
    private async void OnCcsGridCellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        var row = _grid.Rows[e.RowIndex];
        if (row.Tag is not CcsRowTag tag) return;
        var tripNo = row.Cells["TripNo"].Value as string;
        if (string.IsNullOrWhiteSpace(tripNo)) return;

        if (tag.HasCcs)
        {
            _lastQrContent = tripNo;
            await ScanAndLoadTripAsync(tripNo);
            return;
        }

        // Đang xem một xe đã ghi → quay về chế độ đo chung trước khi chọn.
        if (_currentTripId is not null) ResetForm();

        var check = !(row.Cells["Select"].Value is true);
        row.Cells["Select"].Value = check;
        if (check) _uncheckedTrips.Remove(tag.TripId); else _uncheckedTrips.Add(tag.TripId);
        SyncSelectAllBox();
        ShowBatchSelection();
    }

    /// <summary>"Chọn tất cả" phản ánh đúng trạng thái các dòng chưa có CCS.</summary>
    private void SyncSelectAllBox()
    {
        _settingSelectAll = true;
        try { _chkSelectAll.Checked = _missingTrips.Count > 0 && _missingTrips.All(t => !_uncheckedTrips.Contains(t.TripId)); }
        finally { _settingSelectAll = false; }
    }

    private void OnSelectAllChanged()
    {
        if (_settingSelectAll) return;
        if (_currentTripId is not null) ResetForm();
        foreach (var t in _missingTrips)
            if (_chkSelectAll.Checked) _uncheckedTrips.Remove(t.TripId); else _uncheckedTrips.Add(t.TripId);
        foreach (DataGridViewRow row in _grid.Rows)
            if (row.Tag is CcsRowTag { HasCcs: false }) row.Cells["Select"].Value = _chkSelectAll.Checked;
        ShowBatchSelection();
    }

    /// <summary>Chế độ đo chung: chip + dải trạng thái hiện số xe đã chọn; bật nút đo khi có ít nhất 1 xe.</summary>
    private int _lastBatchCount = -1;

    private void ShowBatchSelection(bool onlyIfChanged = false)
    {
        if (_currentTripId is not null) return;
        var count = SelectedTrips().Count;
        _chipTrip.Text = Loc.T("ccs.ux_batch_chip", count);
        _chipTrip.Visible = count > 0;
        // Làm mới định kỳ không đè thông báo đang hiện (đọc máy đo, Fibre đổi...) nếu số xe chọn không đổi.
        if (count != _lastBatchCount || !onlyIfChanged)
        {
            _lblStatus.Text = count > 0 ? Loc.T("ccs.ux_batch_ready", count) : Loc.T("ccs.ux_start");
            _lblStatus.ForeColor = count > 0 ? AgrisTheme.Success : AgrisTheme.BrandGreen;
        }
        _lastBatchCount = count;
        RefreshReadingUi();
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

        _ccsRecorded = sample?.CcsValue is not null;
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
            Helpers.AppMessageBox.Show("Chọn chuyến trước khi đẩy lại.", "Thiếu thông tin",
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
            Helpers.AppMessageBox.Show(Loc.T("ccs.ux_retry_no_values", tripNo), Loc.T("ccs.ux_retry_blocked_title"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _btnRetryDigi.Enabled = false;
        _lblStatus.Text = "⏳ Đang đẩy lại CCS lên FRM...";
        _lblStatus.ForeColor = AgrisTheme.Info;

        try
        {
            var ld = _presenter.FindLenhDonForTrip(_currentFrmLenhDonId, plate);
            var pushResult = await _presenter.PushCcsAsync(
                _currentFrmLenhDonId ?? ld?.SourceKey ?? tripNo,
                _currentFrmMaLenhDon ?? ld?.MaLD ?? tripNo,
                "0", "1", VnTime.Now,
                Num.S2(brix),
                Num.S2(ccs),
                Num.S2(pol),
                Num.S2(polDoc),
                "1",
                Program.LoggedInUserName,
                _currentFrmLenhDonBu ?? ld?.BusinessUnit,
                _cts.Token);

            if (pushResult.IsSuccess)
            {
                _lblStatus.Text = $"✅ Đã đẩy lên FRM cho {tripNo}";
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
            Helpers.AppMessageBox.Show($"Lỗi đẩy lại FRM:\n{ex.Message}", "Lỗi",
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
        string? Factory = null, string? Owner = null, string? CaneForm = null, bool IsReprint = false);

    /// <summary>CCS của chuyến đang mở đã ghi (có số hiệu lực trong DB).</summary>
    private bool _ccsRecorded;

    /// <summary>Chuyến vừa ghi CCS mà phiếu chưa hiện lần nào — lần in đầu không phải bản in lại.</summary>
    private readonly HashSet<Guid> _unprintedCcsTrips = [];

    /// <param name="isReprint">true = in nhãn "BẢN IN LẠI" trên phiếu và tiêu đề cửa sổ.</param>
    private void PrintCcsTicket(bool isReprint)
    {
        if (_currentTripId is null)
        {
            Helpers.AppMessageBox.Show(Loc.T("ccs.print_no_trip"),
                Loc.T("quality.no_trip_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _unprintedCcsTrips.Remove(_currentTripId.Value); // từ giờ in nữa là bản in lại

        var ldPrint = _presenter.FindLenhDonForTrip(_currentFrmLenhDonId, _txtPlate.Text);
        var data = new CcsTicketData(
            _txtTripNo.Text, _txtPlate.Text,
            _numBrix.Value, _numPol.Value, _numPolDoc.Value,
            _chkCcsPending.Checked ? null : _numCcs.Value, _chkCcsPending.Checked,
            _actualImpurity, _paidCcs, _paidImpurity, _paidPolicy,
            VnTime.Now,
            Factory: ldPrint?.TenNhaMay ?? _siteContext.CurrentSiteName,
            Owner: string.IsNullOrWhiteSpace(_txtOwner.Text) || _txtOwner.Text.Trim() == "—" ? ldPrint?.OwnerName : _txtOwner.Text.Trim(),
            CaneForm: Printing.PrintText.CaneForm(ldPrint),
            IsReprint: isReprint);

        using var previewForm = new Form
        {
            Text = WeighTicketPreviewForm.ReprintTitle(Loc.T("ccs.print_form_title", data.TripNo), isReprint),
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
                if (Printing.ThermalPrint.TryPrint(previewForm, (g, area) => DrawCcsTicket(g, area, data),
                        Printing.ThermalPrint.LIST_DESIGN_WIDTH)) return;
                printDoc.DocumentName = Loc.T("ccs.print_form_title", data.TripNo);
                // In đúng mẫu đang xem trước (cùng hàm vẽ), co vào lề trang.
                printDoc.PrintPage += (_, pe) => Printing.PrintPaper.RenderFitted(pe.Graphics!, pe.MarginBounds,
                    (g, area) => DrawCcsTicket(g, area, data));
                Printing.PrintPrinter.ShowDialogAndPrint(printDoc, previewForm);
            }
            catch (Exception ex)
            {
                Helpers.AppMessageBox.Show(Loc.T("ccs.print_error", ex.Message), Loc.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
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
        toolbar.Controls.Add(Printing.PrintPaper.CreatePicker());

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

        // Giấy cuộn máy in nhiệt: không viền khung, giá trị dài tự xuống dòng, ngày in ngay dưới nội dung.
        var thermal = Printing.ThermalPrint.Active;
        if (thermal) y = area.Top + 4;
        using var borderPen = new Pen(AgrisTheme.BrandGreenDark, 2f);
        if (!thermal) g.DrawRectangle(borderPen, area.Left + 2, area.Top + 2, area.Width - 4, area.Height - 4);

        // Font Latin cố định; chữ Khmer/Lào được PrintText đổi font theo đoạn. Nhãn song ngữ theo BU.
        const string family = "Segoe UI";
        using var companyFont = new Font(family, 12F, FontStyle.Bold);
        using var titleFont = new Font(family, 18F, FontStyle.Bold);
        using var titleBrush = new SolidBrush(AgrisTheme.BrandGreenDark);
        y += (int)Printing.PrintText.DrawCentered(g, Printing.PrintText.V(d.Factory), companyFont, Brushes.Black, left, width, y) + 6;
        y += (int)Printing.PrintText.DrawCentered(g, Printing.PrintText.L("ccs.print_title"), titleFont, titleBrush, left, width, y)
             + (d.IsReprint ? 4 : 12);
        if (d.IsReprint)
        {
            using var reprintFont = new Font(family, 12F, FontStyle.Bold);
            using var reprintBrush = new SolidBrush(AgrisTheme.Danger);
            y += (int)Printing.PrintText.DrawCentered(g, Printing.PrintText.L("receipt.reprint_label"), reprintFont, reprintBrush,
                left, width, y) + 10;
        }

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
            if (thermal)
            {
                var h = (int)Math.Ceiling(Printing.PrintText.FieldWrapped(g, label, value, lbl, font, brush, left, vx, right, y));
                y += Math.Max(height, h + 6);
                return;
            }
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
            // Mã QR số đăng tài (thay mã vạch) — quét ở màn Cân / Đo tạp chất để mở chuyến.
            y += 8;
            const int qrSize = 96;
            Printing.QrCodeRenderer.Draw(g, d.TripNo, left, y, qrSize);
            using var bcFont = new Font(AgrisTheme.MonoFamily, 10F, FontStyle.Bold);
            g.DrawString(d.TripNo, bcFont, Brushes.Black, left + qrSize + 10, y + qrSize / 2f - 8);
            y += qrSize;
        }

        var printLabel = Printing.PrintText.L("print.field_printed_at") + ": " + (d.PrintedAt.ToString("dd/MM/yyyy HH:mm"));
        using var tsFont = new Font(family, 9.5F, FontStyle.Italic);
        using var tsBrush = new SolidBrush(AgrisTheme.TextMuted);
        var tsSz = Printing.PrintText.Measure(g, printLabel, tsFont);
        Printing.PrintText.DrawRight(g, printLabel, tsFont, tsBrush, right, thermal ? y + 8 : area.Bottom - m - tsSz.Height);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _liveTimer.Stop();
            _liveTimer.Dispose();
            _fibreTimer.Stop();
            _fibreTimer.Dispose();
            _eventBus.TripChanged -= OnTripChangedFromOtherForm;
            _cts.Dispose();
        }
        base.Dispose(disposing);
    }
}
