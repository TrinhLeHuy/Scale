using System.Globalization;
using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.UseCases.Weighing;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.WinForms.Controls;
using TTC.Weighing.WinForms.Helpers;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Forms;

/// <summary>
/// Kết nối cân — 2 kênh cân chạy cùng lúc, theo phần mềm cân CanDienTu. Không cố định tên cổng:
/// danh sách chỉ gồm cổng có thật (SerialPort.GetPortNames), tự cập nhật khi cắm / rút.
/// <list type="bullet">
/// <item>Mỗi kênh: chọn cổng ("(không)" + cổng thật), chọn baud (mặc định 9600; 8 bit, không parity, 1 stop),
///   màn hình số lớn (nền đen, chữ xanh lá) và dòng trạng thái riêng có chuỗi khung gốc.</item>
/// <item>Mở màn hình là tự kết nối mọi kênh. Đổi cổng / baud chỉ kết nối lại kênh đó.</item>
/// <item>Hai kênh không được trùng cổng: kênh trùng báo ở dòng trạng thái và không mở cổng.</item>
/// <item>Quá 3 giây không có khung hợp lệ → "---" màu xám, dòng trạng thái báo đỏ. Kênh kia vẫn chạy.</item>
/// </list>
/// Cổng COM do <see cref="IScaleConnectionService"/> giữ (đọc DataReceived → byte &amp; 0x7F → cắt CR / LF / ETX →
/// regex [-+]\d{1,8}(\.\d+)?), nên màn Cân vẫn có số khi đóng màn này. Cổng được đóng khi thoát ứng dụng.
/// Cân 1 / Cân 2 gắn với bàn cân thứ 1 / thứ 2 của nhà máy (theo mã); giới hạn Vào / Ra lưu theo bàn cân.
/// </summary>
public sealed class ScaleConnectionForm : Form
{
    private const int CHANNEL_COUNT = 2;
    private const string NO_PORT = "(khong)";
    private static readonly int[] BaudRates = [1200, 2400, 4800, 9600, 19200];

    private readonly IScaleConnectionService _connections;
    private readonly IWeighbridgeRepository _weighbridges;
    private readonly UpdateWeighbridgeLimitsHandler _limitsHandler;
    private readonly ISiteContext _site;
    private readonly CancellationTokenSource _cts = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
    private readonly List<Channel> _channels = [];
    private readonly string _baseTitle = Loc.T("scalecfg.title");
    private bool _loading = true;

    private readonly TableLayoutPanel _channelsRow = new()
    {
        Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 1, BackColor = AgrisTheme.Canvas, Padding = new Padding(0)
    };

    private readonly Button _btnSaveIndices = GridBuilder.PrimaryButton(Loc.T("scalech.btn_save_indices"), new Size(220, 44));
    private readonly Button _btnRefreshPorts = GridBuilder.SecondaryButton(Loc.T("scalecfg.btn_refresh_ports"), new Size(200, 44));
    private readonly Button _btnReconnect = GridBuilder.SecondaryButton(Loc.T("scalecfg.btn_reconnect"), new Size(180, 44));

    public ScaleConnectionForm(IScaleConnectionService connections, IWeighbridgeRepository weighbridges,
        UpdateWeighbridgeLimitsHandler limitsHandler, ISiteContext site)
    {
        _connections = connections;
        _weighbridges = weighbridges;
        _limitsHandler = limitsHandler;
        _site = site;

        Text = _baseTitle;
        MinimumSize = new Size(1024, 600);
        BackColor = AgrisTheme.Canvas;
        DoubleBuffered = true;

        BuildLayout();

        _btnSaveIndices.Click += async (_, _) => await SaveIndicesAsync();
        _btnRefreshPorts.Click += (_, _) => RefreshPorts();
        _btnReconnect.Click += (_, _) => { _connections.Reconnect(); foreach (var c in _channels) c.AppliedAt = DateTime.UtcNow; };
        _timer.Tick += (_, _) => { WatchPorts(); UpdateLive(); };

        // Mở màn hình là tự kết nối mọi kênh, không cần bấm nút.
        Load += async (_, _) => await StartAsync();
        FormClosing += (_, _) => { _cts.Cancel(); _timer.Stop(); };
        Disposed += (_, _) => { _timer.Dispose(); _cts.Dispose(); };
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Padding = new Padding(12, 10, 12, 8), BackColor = AgrisTheme.Canvas
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));

        var note = new Label
        {
            Dock = DockStyle.Fill, Text = Loc.T("scalech.note"), Font = AgrisTheme.Body(9.5F),
            ForeColor = AgrisTheme.TextSecondary, BackColor = AgrisTheme.InfoSoft,
            Padding = new Padding(12, 6, 12, 6), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 0, 0, 8)
        };

        _channelsRow.Controls.Add(new Label
        {
            Dock = DockStyle.Fill, Text = Loc.T("scalecfg.loading"), TextAlign = ContentAlignment.MiddleCenter,
            Font = AgrisTheme.Body(10F), ForeColor = AgrisTheme.TextMuted
        }, 0, 0);

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1,
            BackColor = AgrisTheme.Canvas, Padding = new Padding(0, 10, 0, 0)
        };
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 3; i++) actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        foreach (var b in new[] { _btnRefreshPorts, _btnReconnect, _btnSaveIndices }) b.Margin = new Padding(10, 0, 0, 0);
        actions.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = AgrisTheme.Canvas }, 0, 0);
        actions.Controls.Add(_btnRefreshPorts, 1, 0);
        actions.Controls.Add(_btnReconnect, 2, 0);
        actions.Controls.Add(_btnSaveIndices, 3, 0);

        root.Controls.Add(note, 0, 0);
        root.Controls.Add(_channelsRow, 0, 1);
        root.Controls.Add(actions, 0, 2);
        Controls.Add(root);
    }

    // ── Khởi động: dựng 2 kênh rồi tự kết nối ──

    private async Task StartAsync()
    {
        IReadOnlyList<Domain.Entities.Weighbridge> bridges;
        try
        {
            bridges = await _weighbridges.ListBySiteAsync(_site.CurrentSiteId, _cts.Token);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            bridges = [];
            DigiScalePopup.ShowAlert(this, AlertKind.Error, Loc.T("scalecfg.load_failed_title"),
                reason: ex.Message, action: Loc.T("scalecfg.load_failed_action"));
        }
        if (IsDisposed) return;

        var ordered = bridges.OrderBy(b => b.Code, StringComparer.OrdinalIgnoreCase).ToList();
        var saved = _connections.GetSettings();
        var ports = _connections.GetPortNames();

        _channelsRow.SuspendLayout();
        _channelsRow.Controls.Clear();
        _channelsRow.ColumnStyles.Clear();
        _channelsRow.ColumnCount = CHANNEL_COUNT;
        _channelsRow.RowStyles.Clear();
        _channelsRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        for (var i = 0; i < CHANNEL_COUNT; i++)
        {
            var number = i + 1;
            var bridge = i < ordered.Count ? ordered[i] : null;
            var key = bridge?.Code ?? $"CAN{number}";
            var settings = saved.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));

            // Không cố định COM1 / COM2: chỉ dùng cổng đang có thật trên máy.
            // Chưa từng cấu hình → lấy cổng có thật kế tiếp chưa kênh nào dùng (Cân 1 = cổng đầu, Cân 2 = cổng sau).
            // Không còn cổng → "(khong)".
            var used = _channels.Select(c => c.EffectivePort).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            var port = settings is null
                ? ports.FirstOrDefault(p => !used.Contains(p))
                : settings.Enabled ? settings.PortName : null;
            var baud = settings?.BaudRate is { } b && BaudRates.Contains(b) ? b : 9600;

            Channel? channel = null;
            var card = UiScale.Create(() =>
            {
                channel = new Channel(number, key, bridge, settings, ports, port, baud);
                return channel.Card;
            });
            card.Margin = new Padding(i == 0 ? 0 : UiScale.Px(10), 0, 0, 0);

            var ch = channel!;
            ch.PortBox.SelectedIndexChanged += (_, _) =>
            {
                if (_loading) return;
                ch.MissingPort = null; // người dùng chọn lại → bỏ cổng đã lưu nhưng đang không có trên máy
                ApplyChannels(ch);
            };
            ch.BaudBox.SelectedIndexChanged += (_, _) => { if (!_loading) ApplyChannels(ch); };
            _channels.Add(ch);
            _channelsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / CHANNEL_COUNT));
            _channelsRow.Controls.Add(card, i, 0);
        }

        _channelsRow.ResumeLayout(true);
        _lastPorts = ports;
        _loading = false;

        ApplyChannels(changed: null);
        UpdateLive();
        _timer.Start();
    }

    // ── Áp cấu hình kênh vào dịch vụ đọc cân ──

    /// <summary>
    /// Ghi cấu hình 2 kênh: 8N1, kiểu chuỗi phần mềm cân cũ, mất tín hiệu sau 3 giây.
    /// Trùng cổng → kênh vừa đổi (khởi động: kênh sau) không mở cổng. Thiết bị khác (cân mẫu...) giữ nguyên.
    /// Dịch vụ chỉ mở lại cổng có thay đổi nên kênh kia không bị ngắt.
    /// </summary>
    private void ApplyChannels(Channel? changed)
    {
        foreach (var c in _channels) c.Conflict = null;
        for (var i = 0; i < _channels.Count; i++)
        for (var j = i + 1; j < _channels.Count; j++)
        {
            var a = _channels[i];
            var b = _channels[j];
            if (a.EffectivePort is null || !string.Equals(a.EffectivePort, b.EffectivePort, StringComparison.OrdinalIgnoreCase))
                continue;
            var loser = changed == a ? a : b;
            loser.Conflict = (loser == a ? b : a).Name;
        }

        var keys = _channels.Select(c => c.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var settings = _connections.GetSettings().Where(s => !keys.Contains(s.Key)).ToList();
        settings.AddRange(_channels.Select(c => c.ToSettings()));

        try
        {
            _connections.Save(settings);
        }
        catch (Exception ex)
        {
            DigiScalePopup.ShowAlert(this, AlertKind.Error, Loc.T("scalecfg.save_failed_title"),
                reason: ex.Message, action: Loc.T("scalecfg.save_failed_action"));
            return;
        }

        foreach (var c in _channels.Where(c => changed is null || c == changed))
            c.AppliedAt = DateTime.UtcNow;
        UpdateLive();
    }

    private IReadOnlyList<string> _lastPorts = [];
    private int _portTicks;

    /// <summary>
    /// Nạp lại danh sách cổng thật. Cổng mới cắm thì hiện ngay; cổng đã rút thì biến khỏi danh sách
    /// (kênh đang dùng nó hiện "(khong)" và chờ cắm lại, không mất cấu hình).
    /// </summary>
    private void RefreshPorts()
    {
        var ports = _connections.GetPortNames();
        _lastPorts = ports;
        _loading = true;
        foreach (var c in _channels) c.SetPorts(ports);
        _loading = false;
    }

    /// <summary>Tự dò cổng cắm / rút mỗi 2 giây.</summary>
    private void WatchPorts()
    {
        if (++_portTicks % 4 != 0) return;
        var ports = _connections.GetPortNames();
        if (ports.SequenceEqual(_lastPorts, StringComparer.OrdinalIgnoreCase)) return;
        RefreshPorts();
    }

    // ── Số đang đọc (mỗi 0,5 giây) ──

    private void UpdateLive()
    {
        var open = new List<string>();
        foreach (var c in _channels)
        {
            var live = _connections.GetLive(c.Key);
            c.Show(live);
            if (live.PortOpen && c.EffectivePort is { } p) open.Add($"{c.Name}={p}");
        }

        // Thanh tiêu đề: các cổng đang mở, ví dụ "Cân 1=COM1, Cân 2=COM2".
        var title = open.Count > 0 ? $"{_baseTitle} — {string.Join(", ", open)}" : _baseTitle;
        if (Text != title) Text = title;
    }

    // ── Lưu chỉ số: hệ số K (theo máy) + giới hạn Vào / Ra (theo bàn cân) ──

    private async Task SaveIndicesAsync()
    {
        var facts = new List<(string, string?)>();
        foreach (var c in _channels)
        {
            facts.Add(($"{c.Name} · {Loc.T("scalecfg.factor")}", c.Factor.ToString("0.###", CultureInfo.InvariantCulture)));
            if (c.ReadLimits() is { } l)
                facts.Add(($"{c.Name} · {Loc.T("scalecfg.limits_short")}",
                    $"{Loc.T("scalecfg.limit_in")} {LimitText(l.FirstMin, l.FirstMax)} · {Loc.T("scalecfg.limit_out")} {LimitText(l.SecondMin, l.SecondMax)}"));
        }
        if (!DigiScalePopup.ShowConfirm(this, Loc.T("scalech.confirm_indices_title"), Loc.T("scalecfg.confirm_yes"),
                Loc.T("common.cancel"), facts, Loc.T("scalech.confirm_indices_msg"), defaultNo: true))
            return;

        _btnSaveIndices.Enabled = false;
        try
        {
            foreach (var c in _channels)
            {
                if (c.BridgeId is not { } bridgeId || c.ReadLimits() is not { } l) continue;
                var result = await _limitsHandler.HandleAsync(new UpdateWeighbridgeLimitsCommand(
                    bridgeId, l.FirstMin, l.FirstMax, l.SecondMin, l.SecondMax, Guid.NewGuid().ToString("N")), _cts.Token);
                if (!result.IsSuccess)
                {
                    DigiScalePopup.ShowAlert(this, AlertKind.Warning, Loc.T("scalecfg.limits_failed_title"),
                        reason: result.FailureReason, action: Loc.T("scalecfg.limits_failed_action"),
                        facts: [(Loc.T("scalecfg.limits_bridge"), c.Key)]);
                    return;
                }
            }
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            DigiScalePopup.ShowAlert(this, AlertKind.Error, Loc.T("scalecfg.limits_failed_title"),
                reason: ex.Message, action: Loc.T("scalecfg.load_failed_action"));
            return;
        }
        finally
        {
            if (!IsDisposed) _btnSaveIndices.Enabled = true;
        }

        // Hệ số K lưu cùng cấu hình kênh (không mở lại cổng vì thông số truyền không đổi).
        ApplyChannels(changed: null);
    }

    private static string LimitText(decimal? min, decimal? max)
        => $"{(min is { } a ? a.ToString("N0") : "—")} – {(max is { } b ? b.ToString("N0") : "—")} kg";

    /// <summary>Một kênh cân: chọn cổng / baud, màn hình số, dòng trạng thái, chỉ số.</summary>
    private sealed class Channel
    {
        private static readonly Color DisplayBack = Color.Black;
        private static readonly Color DisplayFore = Color.FromArgb(0, 230, 64);

        private readonly ScaleConnectionSettings? _original;
        private readonly Label _lblDisplay = new()
        {
            Dock = DockStyle.Top, Height = 92, Text = "---",
            Font = AgrisTheme.Mono(22F, FontStyle.Bold), BackColor = DisplayBack, ForeColor = Color.Gray,
            TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(12, 0, 16, 0)
        };
        private readonly Label _lblStatus = new()
        {
            Dock = DockStyle.Top, Height = 56, Font = AgrisTheme.Body(9.5F),
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Padding = new Padding(2, 0, 2, 0)
        };
        private readonly NumericUpDown _numFactor = new()
        {
            Dock = DockStyle.Fill, DecimalPlaces = 3, Minimum = 0.001m, Maximum = 1000m, Increment = 0.001m,
            Font = AgrisTheme.Mono(10F), TextAlign = HorizontalAlignment.Right
        };
        private readonly NumericUpDown _numInMin = LimitBox();
        private readonly NumericUpDown _numInMax = LimitBox();
        private readonly NumericUpDown _numOutMin = LimitBox();
        private readonly NumericUpDown _numOutMax = LimitBox();

        public int Number { get; }
        public string Key { get; }
        public string Name { get; }
        public Guid? BridgeId { get; }
        public ComboBox PortBox { get; } = Combo();
        public ComboBox BaudBox { get; } = Combo();
        public Card Card { get; }

        /// <summary>Tên kênh đang dùng cùng cổng (trùng cổng → không mở).</summary>
        public string? Conflict { get; set; }

        /// <summary>Lúc kết nối (lại) — chưa đủ 3 giây thì hiện "đang chờ dữ liệu" thay vì báo lỗi.</summary>
        public DateTime AppliedAt { get; set; } = DateTime.UtcNow;

        public decimal Factor => _numFactor.Value;

        /// <summary>Cổng đang chọn trong danh sách (chỉ gồm cổng có thật trên máy).</summary>
        public string? SelectedPort => PortBox.SelectedItem is string p && p != NO_PORT ? p : null;

        /// <summary>
        /// Cổng đã lưu nhưng hiện không có trên máy (rút cáp USB-COM...). Không hiện trong danh sách;
        /// cấu hình vẫn giữ để tự kết nối khi cắm lại. Người dùng chọn cổng khác thì bỏ.
        /// </summary>
        public string? MissingPort { get; set; }

        /// <summary>Cổng kênh đang dùng: cổng chọn, hoặc cổng đã lưu đang chờ cắm lại.</summary>
        public string? EffectivePort => SelectedPort ?? MissingPort;

        private int SelectedBaud => BaudBox.SelectedItem is string s && int.TryParse(s, out var b) ? b : 9600;

        public Channel(int number, string key, Domain.Entities.Weighbridge? bridge, ScaleConnectionSettings? settings,
            IReadOnlyList<string> ports, string? port, int baud)
        {
            Number = number;
            Key = key;
            Name = Loc.T("scalech.channel", number);
            BridgeId = bridge?.Id;
            _original = settings;

            SetPorts(ports, port);
            BaudBox.Items.AddRange(BaudRates.Select(b => (object)b.ToString()).ToArray());
            BaudBox.SelectedItem = baud.ToString();
            _numFactor.Value = Math.Clamp(settings?.Factor ?? 1m, _numFactor.Minimum, _numFactor.Maximum);

            Card = new Card { Dock = DockStyle.Fill, Padding = new Padding(1) };

            // Hàng chọn: [Cổng COM][combo] [Baud][combo]
            var pick = new TableLayoutPanel { Dock = DockStyle.Top, Height = 48, ColumnCount = 4, RowCount = 1, Padding = new Padding(0, 6, 0, 6) };
            pick.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            pick.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
            pick.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
            pick.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
            pick.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
            pick.Controls.Add(Caption(Loc.T("scalecfg.port")), 0, 0);
            pick.Controls.Add(PortBox, 1, 0);
            pick.Controls.Add(Caption(Loc.T("scalecfg.baud"), 12), 2, 0);
            pick.Controls.Add(BaudBox, 3, 0);

            var bridgeNote = new Label
            {
                Dock = DockStyle.Top, Height = 26, Font = AgrisTheme.Body(9F),
                ForeColor = bridge is null ? AgrisTheme.Warning : AgrisTheme.TextMuted, TextAlign = ContentAlignment.MiddleLeft,
                Text = bridge is null ? Loc.T("scalech.no_bridge") : Loc.T("scalech.bridge", bridge.Code)
                    + " · 8 bit · None · 1 stop"
            };

            // Chỉ số: hệ số K + giới hạn Vào / Ra (kg, theo bàn cân).
            var indices = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Padding = new Padding(0, 8, 0, 0) };
            indices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            indices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
            indices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 6));
            indices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
            AddIndexRow(indices, Loc.T("scalecfg.factor"), _numFactor, null);
            if (bridge is not null)
            {
                SetLimit(_numInMin, bridge.FirstWeightMinKg);
                SetLimit(_numInMax, bridge.FirstWeightMaxKg);
                SetLimit(_numOutMin, bridge.SecondWeightMinKg);
                SetLimit(_numOutMax, bridge.SecondWeightMaxKg);
                AddIndexRow(indices, Loc.T("scalech.limit_in"), _numInMin, _numInMax);
                AddIndexRow(indices, Loc.T("scalech.limit_out"), _numOutMin, _numOutMax);
            }
            var indicesTitle = new Label
            {
                Dock = DockStyle.Top, Height = 30, Text = Loc.T("scalecfg.section_indices"),
                Font = AgrisTheme.Body(10F, FontStyle.Bold), ForeColor = AgrisTheme.TextPrimary, TextAlign = ContentAlignment.BottomLeft
            };

            var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(14, 6, 14, 10), BackColor = AgrisTheme.Surface };
            // Dock Top xếp ngược thứ tự thêm.
            body.Controls.Add(indices);
            body.Controls.Add(indicesTitle);
            body.Controls.Add(_lblStatus);
            body.Controls.Add(_lblDisplay);
            body.Controls.Add(bridgeNote);
            body.Controls.Add(pick);

            Card.Controls.Add(body);
            Card.Controls.Add(GridBuilder.SectionHeader(Name));
        }

        /// <summary>
        /// Danh sách = "(khong)" + cổng có thật (SerialPort.GetPortNames). Không thêm cổng không tồn tại.
        /// Cổng đang dùng mà không còn trên máy → chọn "(khong)" và nhớ ở <see cref="MissingPort"/>;
        /// cắm lại thì tự chọn lại.
        /// </summary>
        public void SetPorts(IReadOnlyList<string> ports, string? select = null)
        {
            var current = select ?? EffectivePort;
            PortBox.BeginUpdate();
            PortBox.Items.Clear();
            PortBox.Items.Add(NO_PORT);
            foreach (var p in ports.OrderBy(p => p.Length).ThenBy(p => p, StringComparer.OrdinalIgnoreCase))
                PortBox.Items.Add(p);
            PortBox.EndUpdate();

            var exists = current is not null && ports.Contains(current, StringComparer.OrdinalIgnoreCase);
            PortBox.SelectedItem = exists ? ports.First(p => string.Equals(p, current, StringComparison.OrdinalIgnoreCase)) : NO_PORT;
            MissingPort = exists ? null : current;
        }

        public ScaleConnectionSettings ToSettings()
        {
            var baseline = _original ?? new ScaleConnectionSettings { Key = Key };
            return baseline with
            {
                Key = Key,
                WeighbridgeId = BridgeId,
                // Cổng đã lưu đang rút ra vẫn để bật: dịch vụ thử mở lại mỗi 5 giây, cắm lại là có số.
                Enabled = EffectivePort is not null && Conflict is null,
                PortName = EffectivePort,
                BaudRate = SelectedBaud,
                DataBits = 8,
                Parity = "None",
                StopBits = 1,
                Format = ScaleFrameFormat.SignedDigits,
                Unit = "kg",
                Factor = _numFactor.Value,
                StaleAfterMs = 3000
            };
        }

        public sealed record Limits(decimal? FirstMin, decimal? FirstMax, decimal? SecondMin, decimal? SecondMax);

        public Limits? ReadLimits() => BridgeId is null
            ? null
            : new Limits(Opt(_numInMin), Opt(_numInMax), Opt(_numOutMin), Opt(_numOutMax));

        /// <summary>Màn hình số + dòng trạng thái. Không giữ số cũ khi mất tín hiệu.</summary>
        public void Show(ScaleLiveReading live)
        {
            var port = EffectivePort ?? "";
            var baud = SelectedBaud;

            if (Conflict is not null)
            {
                SetDisplay(null);
                SetStatus(Loc.T("scalech.st_duplicate", port, Conflict), AgrisTheme.Danger);
                return;
            }

            if (SelectedPort is null && MissingPort is not null)
            {
                SetDisplay(null);
                SetStatus(Loc.T("scalech.st_port_missing", MissingPort), AgrisTheme.Warning);
                return;
            }

            if (EffectivePort is null || live.Error is "NOT_CONFIGURED" or "DISABLED")
            {
                SetDisplay(null);
                SetStatus(Loc.T("scalech.st_off"), AgrisTheme.TextMuted);
                return;
            }

            if (!live.PortOpen)
            {
                SetDisplay(null);
                var text = live.Error switch
                {
                    "PORT_NOT_FOUND" => Loc.T("scalech.st_port_missing", port),
                    "PORT_BUSY" => Loc.T("scalech.st_port_busy", port),
                    "PORT_CONFLICT" => Loc.T("scalecfg.st_conflict"),
                    _ when live.ErrorDetail is { Length: > 0 } d => Loc.T("scalech.st_port_error", port, d),
                    _ => Loc.T("scalech.st_connecting", port, baud)
                };
                SetStatus(text, live.Error is null ? AgrisTheme.Info : AgrisTheme.Danger);
                return;
            }

            var frame = live.LastLine is { Length: > 0 } l ? l : "—";
            if (!live.Fresh || live.Kg is not { } kg)
            {
                SetDisplay(null);
                if (live.ReceivedAt is null && DateTime.UtcNow - AppliedAt < TimeSpan.FromSeconds(3))
                    SetStatus(Loc.T("scalech.st_waiting", port, baud), AgrisTheme.Info);
                else if (live.Error == "PARSE_ERROR")
                    SetStatus(Loc.T("scalech.st_parse", frame), AgrisTheme.Danger);
                else
                    SetStatus(Loc.T("scalech.st_no_data", port, 3), AgrisTheme.Danger);
                return;
            }

            SetDisplay(kg);
            var state = live.Status == ScaleDeviceStatus.Overload ? Loc.T("scalecfg.st_overload")
                : live.StableFlag switch
                {
                    true => Loc.T("scalech.st_stable"),
                    false => Loc.T("scalech.st_moving"),
                    _ => live.IsStable ? Loc.T("scalech.st_stable") : Loc.T("scalech.st_moving")
                };
            var at = live.ReceivedAt is { } r ? Domain.Common.VnTime.ToVnTime(r).ToString("HH:mm:ss") : "";
            SetStatus(Loc.T("scalech.st_reading", port, baud, state, at, frame),
                live.Status == ScaleDeviceStatus.Overload ? AgrisTheme.Danger : AgrisTheme.Success);
        }

        private void SetDisplay(decimal? kg)
        {
            // Dạng "4450 kg" (số lẻ nếu đầu cân gửi số thập phân).
            var text = kg is { } v ? $"{v.ToString("0.###", CultureInfo.InvariantCulture)} kg" : "---";
            if (_lblDisplay.Text != text) _lblDisplay.Text = text;
            _lblDisplay.ForeColor = kg is null ? Color.Gray : DisplayFore;
        }

        private void SetStatus(string text, Color color)
        {
            if (_lblStatus.Text != text) _lblStatus.Text = text;
            _lblStatus.ForeColor = color;
        }

        private static void AddIndexRow(TableLayoutPanel table, string caption, Control first, Control? second)
        {
            var row = table.RowCount;
            table.RowCount = row + 1;
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            table.Controls.Add(Caption(caption), 0, row);
            first.Margin = new Padding(0, 5, 0, 5);
            table.Controls.Add(first, 1, row);
            if (second is null)
            {
                table.SetColumnSpan(first, 3);
                return;
            }
            table.Controls.Add(new Label
            {
                Dock = DockStyle.Fill, Text = "–", TextAlign = ContentAlignment.MiddleCenter,
                Font = AgrisTheme.Body(10F, FontStyle.Bold), ForeColor = AgrisTheme.TextSecondary
            }, 2, row);
            second.Margin = new Padding(0, 5, 0, 5);
            table.Controls.Add(second, 3, row);
        }

        private static Label Caption(string text, int left = 0) => new()
        {
            Dock = DockStyle.Fill, Text = text, Font = AgrisTheme.Body(9.5F), ForeColor = AgrisTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Margin = new Padding(left, 0, 4, 0)
        };

        private static ComboBox Combo() => new()
        {
            Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Font = AgrisTheme.Body(10F)
        };

        private static NumericUpDown LimitBox() => new()
        {
            Dock = DockStyle.Fill, DecimalPlaces = 0, Minimum = 0, Maximum = 1_000_000m, Increment = 10,
            ThousandsSeparator = true, Font = AgrisTheme.Mono(10F), TextAlign = HorizontalAlignment.Right
        };

        private static decimal? Opt(NumericUpDown n) => n.Value > 0 ? n.Value : null;

        private static void SetLimit(NumericUpDown n, decimal? kg) => n.Value = Math.Clamp(kg ?? 0m, n.Minimum, n.Maximum);
    }
}
