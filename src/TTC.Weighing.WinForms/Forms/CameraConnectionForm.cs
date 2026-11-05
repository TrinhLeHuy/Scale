using System.Net;
using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.WinForms.Controls;
using TTC.Weighing.WinForms.Helpers;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Forms;

/// <summary>
/// Kết nối camera IP qua TCP/IP (HTTP chụp ảnh, RTSP xem luồng).
/// Thanh chuyển trên cùng chọn màn hình dùng camera: Bàn cân (màn Cân) / Đăng tài — mỗi màn 2 camera Vào / Ra,
/// màn tương ứng hiển thị trực tiếp đúng camera cấu hình ở đây.
/// Mỗi thẻ là một camera: IP, cổng, tài khoản, hãng. "Kiểm tra kết nối" thử TCP rồi chụp một ảnh;
/// "Xem trực tiếp" chụp liên tục mỗi giây để người cài đặt chỉnh góc máy.
/// Cấu hình lưu theo máy trạm; mật khẩu mã hóa theo tài khoản Windows.
/// </summary>
public sealed class CameraConnectionForm : Form
{
    private readonly ICameraConnectionService _connections;
    private readonly CancellationTokenSource _cts = new();
    private readonly System.Windows.Forms.Timer _liveTimer = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 2000 };
    private readonly List<CameraEditor> _editors = [];

    // Thanh chuyển màn hình: mỗi màn (Bàn cân / Đăng tài) có 2 camera Vào / Ra riêng.
    private readonly Dictionary<string, Button> _stationButtons = [];
    private readonly Dictionary<string, Control> _stationPanels = [];
    private string _station = CameraConnectionSettings.StationWeigh;

    private readonly Button _btnSave = GridBuilder.PrimaryButton(Loc.T("camcfg.btn_save"), new Size(220, 44));
    private readonly Button _btnLive = GridBuilder.SecondaryButton(Loc.T("camcfg.btn_live_start"), new Size(220, 44));
    private bool _liveOn;

    public CameraConnectionForm(ICameraConnectionService connections)
    {
        _connections = connections;

        Text = Loc.T("camcfg.title");
        MinimumSize = new Size(1024, 600);
        BackColor = AgrisTheme.Canvas;
        DoubleBuffered = true;

        var saved = _connections.GetSettings();
        foreach (var station in CameraConnectionSettings.Stations)
        foreach (var position in CameraConnectionSettings.Positions)
        {
            var settings = saved.FirstOrDefault(s => s.Is(station, position))
                ?? new CameraConnectionSettings { Station = station, Position = position };
            var editor = new CameraEditor(settings);
            editor.TestButton.Click += async (_, _) => await TestAsync(editor);
            _editors.Add(editor);
        }

        BuildLayout();
        SelectStation(CameraConnectionSettings.StationWeigh);

        _btnSave.Click += (_, _) => Save();
        _btnLive.Click += (_, _) => ToggleLive();
        _liveTimer.Tick += async (_, _) => await LiveTickAsync();
        _statusTimer.Tick += (_, _) => UpdateSystemStatus();

        // Mở màn là xem trực tiếp luôn (cùng nhịp với màn Cân). Nút vẫn cho dừng / bật lại.
        _liveTimer.Interval = TTC.Weighing.WinForms.Controls.CameraViewControl.LiveIntervalMs;
        Load += (_, _) =>
        {
            UpdateSystemStatus();
            _statusTimer.Start();
            if (!_liveOn) ToggleLive();
        };
        FormClosing += (_, _) => { _cts.Cancel(); _liveTimer.Stop(); _statusTimer.Stop(); };
        Disposed += (_, _) => { _liveTimer.Dispose(); _statusTimer.Dispose(); _cts.Dispose(); };
    }

    private IEnumerable<CameraEditor> CurrentEditors => _editors.Where(e => e.Station == _station);

    private static string StationText(string station) => Loc.T(station == CameraConnectionSettings.StationRegistration
        ? "camcfg.station_registration" : "camcfg.station_weigh");

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
            Padding = new Padding(12, 10, 12, 8), BackColor = AgrisTheme.Canvas
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));

        var note = new Label
        {
            Dock = DockStyle.Fill, Text = Loc.T("camcfg.note"), Font = AgrisTheme.Body(9.5F),
            ForeColor = AgrisTheme.TextSecondary, BackColor = AgrisTheme.InfoSoft,
            Padding = new Padding(12, 6, 12, 6), TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 0, 8)
        };

        // ── Thanh chuyển: [Camera cho màn hình:] [Bàn cân] [Đăng tài] ──
        var switcher = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1,
            BackColor = AgrisTheme.Canvas, Margin = new Padding(0, 0, 0, 8)
        };
        switcher.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        switcher.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        switcher.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        switcher.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        switcher.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
        switcher.Controls.Add(new Label
        {
            AutoSize = true, Dock = DockStyle.Fill, Text = Loc.T("camcfg.station_caption"),
            Font = AgrisTheme.Body(10F, FontStyle.Bold), ForeColor = AgrisTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 0, 12, 0)
        }, 0, 0);

        var host = new Panel { Dock = DockStyle.Fill, BackColor = AgrisTheme.Canvas, Margin = Padding.Empty };
        var col = 1;
        foreach (var station in CameraConnectionSettings.Stations)
        {
            var button = new Button
            {
                Dock = DockStyle.Fill, Text = StationText(station), FlatStyle = FlatStyle.Flat,
                Font = AgrisTheme.Body(10.5F, FontStyle.Bold), Cursor = Cursors.Hand,
                Margin = new Padding(col == 1 ? 0 : 6, 0, 0, 0), TextAlign = ContentAlignment.MiddleCenter
            };
            button.FlatAppearance.BorderColor = AgrisTheme.BrandGreen;
            button.FlatAppearance.BorderSize = 1;
            var target = station;
            button.Click += (_, _) => SelectStation(target);
            _stationButtons[station] = button;
            switcher.Controls.Add(button, col++, 0);

            // Hai thẻ camera Vào / Ra của màn hình này.
            var editors = _editors.Where(e => e.Station == station).ToList();
            var cards = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, RowCount = 1, ColumnCount = editors.Count,
                BackColor = AgrisTheme.Canvas, Padding = new Padding(0), Visible = false
            };
            cards.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            for (var i = 0; i < editors.Count; i++)
            {
                cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / editors.Count));
                editors[i].Card.Margin = new Padding(i == 0 ? 0 : 10, 0, 0, 0);
                cards.Controls.Add(editors[i].Card, i, 0);
            }
            _stationPanels[station] = cards;
            host.Controls.Add(cards);
        }
        switcher.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = AgrisTheme.Canvas }, col, 0);

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
            BackColor = AgrisTheme.Canvas, Padding = new Padding(0, 10, 0, 0)
        };
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _btnLive.Margin = new Padding(10, 0, 0, 0);
        _btnSave.Margin = new Padding(10, 0, 0, 0);
        actions.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = AgrisTheme.Canvas }, 0, 0);
        actions.Controls.Add(_btnLive, 1, 0);
        actions.Controls.Add(_btnSave, 2, 0);

        root.Controls.Add(note, 0, 0);
        root.Controls.Add(switcher, 0, 1);
        root.Controls.Add(host, 0, 2);
        root.Controls.Add(actions, 0, 3);
        Controls.Add(root);
    }

    /// <summary>Hiện 2 camera của màn hình được chọn. Cấu hình màn kia vẫn giữ, Lưu ghi cả 4 camera.</summary>
    private void SelectStation(string station)
    {
        _station = station;
        foreach (var (key, button) in _stationButtons)
        {
            var selected = key == station;
            button.BackColor = selected ? AgrisTheme.BrandGreen : AgrisTheme.Surface;
            button.ForeColor = selected ? Color.White : AgrisTheme.BrandGreen;
            button.FlatAppearance.MouseOverBackColor = selected ? AgrisTheme.BrandGreen : AgrisTheme.SurfaceMuted;
        }
        foreach (var (key, panel) in _stationPanels)
            panel.Visible = key == station;
        UpdateSystemStatus();
    }

    // ── Kiểm tra kết nối ──

    private async Task TestAsync(CameraEditor editor)
    {
        var settings = editor.Read();
        if (ValidateHost(editor, settings) is false) return;

        editor.TestButton.Enabled = false;
        editor.ShowTesting();
        CameraTestResult result;
        try
        {
            result = await _connections.TestAsync(settings, _cts.Token);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            result = new CameraTestResult { Error = "HTTP_ERROR", ErrorDetail = ex.Message };
        }
        finally
        {
            if (!IsDisposed) editor.TestButton.Enabled = true;
        }
        if (IsDisposed) return;

        editor.ShowResult(result);

        var facts = new List<(string, string?)>
        {
            (Loc.T("camcfg.camera"), editor.FullTitle),
            (Loc.T("camcfg.host"), $"{settings.Host}:{settings.HttpPort}")
        };
        if (result.Success)
        {
            if (result.DetectedBrand is { } brand) facts.Add((Loc.T("camcfg.brand"), BrandText(brand)));
            if (result.LatencyMs is { } ms) facts.Add((Loc.T("camcfg.latency_label"), $"{ms} ms"));
            if (result.RtspUrl is { } rtsp) facts.Add((Loc.T("camcfg.rtsp_url"), rtsp));
            DigiScalePopup.ShowAlert(this, AlertKind.Success, Loc.T("camcfg.test_ok_title"),
                facts: facts, action: Loc.T("camcfg.test_ok_action"));
        }
        else
        {
            var code = (result.Error ?? "HTTP_ERROR").ToLowerInvariant();
            DigiScalePopup.ShowAlert(this, AlertKind.Error, Loc.T("camcfg.test_failed_title"),
                reason: Loc.T("camcfg.reason_" + code, $"{settings.Host}:{settings.HttpPort}"),
                action: Loc.T("camcfg.action_" + code), facts: facts);
        }
    }

    // ── Xem trực tiếp (chụp ảnh mỗi giây) ──

    private void ToggleLive()
    {
        _liveOn = !_liveOn;
        _btnLive.Text = Loc.T(_liveOn ? "camcfg.btn_live_stop" : "camcfg.btn_live_start");
        if (_liveOn) { _liveTimer.Start(); _ = LiveTickAsync(); }
        else _liveTimer.Stop();
    }

    private bool _liveBusy;

    private async Task LiveTickAsync()
    {
        if (_liveBusy || !_liveOn || IsDisposed) return;
        _liveBusy = true;
        try
        {
            // Chỉ camera của màn hình đang chọn.
            var tasks = CurrentEditors
                .Select(e => (Editor: e, Settings: e.Read()))
                .Where(x => x.Settings.Enabled && !string.IsNullOrWhiteSpace(x.Settings.Host))
                .Select(async x =>
                {
                    var r = await _connections.TestAsync(x.Settings, _cts.Token);
                    if (!IsDisposed) x.Editor.ShowResult(r);
                });
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            // Lỗi từng camera đã hiện trên thẻ; không bật popup mỗi giây.
        }
        finally
        {
            _liveBusy = false;
        }
    }

    // ── Trạng thái hệ thống (cấu hình đã lưu) ──

    private void UpdateSystemStatus()
    {
        foreach (var editor in CurrentEditors)
            editor.ShowSystemStatus(_connections.GetLive(editor.Station, editor.Position));
    }

    // ── Lưu ──

    private void Save()
    {
        var settings = _editors.Select(e => e.Read()).ToList();
        foreach (var editor in _editors)
            if (ValidateHost(editor, editor.Read()) is false)
            {
                SelectStation(editor.Station); // đưa người dùng tới đúng màn hình có camera lỗi
                return;
            }

        var facts = _editors.Select(e =>
        {
            var s = e.Read();
            return (e.FullTitle, (string?)(s.Enabled
                ? $"{s.Host}:{s.HttpPort} · {BrandText(s.Brand)} · {s.Username}"
                : Loc.T("camcfg.off")));
        }).ToList();
        if (!DigiScalePopup.ShowConfirm(this, Loc.T("camcfg.confirm_title"), Loc.T("camcfg.confirm_yes"),
                Loc.T("common.cancel"), facts, Loc.T("camcfg.confirm_msg"), defaultNo: true))
            return;

        try
        {
            _connections.Save(settings);
            foreach (var e in _editors) e.ShowSystemStatus(new CameraLiveState());
        }
        catch (Exception ex)
        {
            DigiScalePopup.ShowAlert(this, AlertKind.Error, Loc.T("camcfg.save_failed_title"),
                reason: ex.Message, action: Loc.T("camcfg.save_failed_action"));
        }
    }

    /// <summary>Camera bật thì phải có địa chỉ hợp lệ. Trả false (và báo) khi không hợp lệ.</summary>
    private bool ValidateHost(CameraEditor editor, CameraConnectionSettings s)
    {
        if (!s.Enabled && string.IsNullOrWhiteSpace(s.Host)) return true;
        if (string.IsNullOrWhiteSpace(s.Host))
        {
            DigiScalePopup.ShowAlert(this, AlertKind.Warning, Loc.T("camcfg.missing_host_title"),
                reason: Loc.T("camcfg.missing_host_reason", editor.FullTitle), action: Loc.T("camcfg.missing_host_action"));
            return false;
        }
        if (!IPAddress.TryParse(s.Host, out _) && Uri.CheckHostName(s.Host) == UriHostNameType.Unknown)
        {
            DigiScalePopup.ShowAlert(this, AlertKind.Warning, Loc.T("camcfg.invalid_host_title"),
                reason: Loc.T("camcfg.invalid_host_reason", s.Host, editor.FullTitle), action: Loc.T("camcfg.missing_host_action"));
            return false;
        }
        return true;
    }

    private static string BrandText(CameraBrand brand) => Loc.T("camcfg.brand_" + brand.ToString().ToLowerInvariant());

    /// <summary>Một thẻ camera: ô thông số + khung ảnh xem thử.</summary>
    private sealed class CameraEditor
    {
        private readonly CameraConnectionSettings _original;
        private readonly CheckBox _chkEnabled = new() { AutoSize = true, Text = Loc.T("camcfg.enabled"), Font = AgrisTheme.Body(10F, FontStyle.Bold) };
        private readonly TextBox _txtHost = Text();
        private readonly NumericUpDown _numHttpPort = Port();
        private readonly NumericUpDown _numRtspPort = Port();
        private readonly TextBox _txtUser = Text();
        private readonly TextBox _txtPassword = Text();
        private readonly CheckBox _chkShowPassword = new() { AutoSize = true, Text = Loc.T("camcfg.show_password"), Font = AgrisTheme.Body(9F) };
        private readonly ComboBox _cboBrand = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Font = AgrisTheme.Body(10F) };
        private readonly NumericUpDown _numChannel = new()
        {
            Dock = DockStyle.Fill, Minimum = 1, Maximum = 64, Font = AgrisTheme.Mono(10F), TextAlign = HorizontalAlignment.Right
        };
        private readonly TextBox _txtSnapshotPath = Text();

        private readonly PictureBox _picPreview = new()
        {
            Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(38, 48, 54)
        };
        private readonly Label _lblPreviewNone = new()
        {
            Dock = DockStyle.Fill, Text = Loc.T("camcfg.preview_none"), Font = AgrisTheme.Body(10F, FontStyle.Italic),
            ForeColor = Color.Gainsboro, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleCenter
        };
        private readonly Label _lblStatus = new()
        {
            Dock = DockStyle.Top, Height = 30, Font = AgrisTheme.Body(10F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true
        };
        private readonly Label _lblDetail = new()
        {
            Dock = DockStyle.Top, Height = 48, Font = AgrisTheme.Body(9F), ForeColor = AgrisTheme.TextSecondary,
            TextAlign = ContentAlignment.TopLeft, AutoEllipsis = true
        };
        private readonly Label _lblSystem = new()
        {
            Dock = DockStyle.Top, Height = 26, Font = AgrisTheme.Body(9F), ForeColor = AgrisTheme.TextMuted,
            BackColor = AgrisTheme.SurfaceMuted, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
            Padding = new Padding(6, 0, 6, 0)
        };

        public string Station { get; }
        public string Position { get; }
        public string Title { get; }

        /// <summary>"Bàn cân · Camera Vào" — dùng trong popup / xác nhận lưu.</summary>
        public string FullTitle => $"{StationText(Station)} · {Title}";
        public Card Card { get; }
        public Button TestButton { get; } = GridBuilder.SecondaryButton(Loc.T("camcfg.btn_test"), new Size(150, 34));

        public CameraEditor(CameraConnectionSettings settings)
        {
            _original = settings;
            Station = settings.Station;
            Position = settings.Position;
            Title = Loc.T(Position == CameraConnectionSettings.OutPosition ? "camcfg.card_out" : "camcfg.card_in");

            foreach (var b in Enum.GetValues<CameraBrand>())
                _cboBrand.Items.Add(new Choice(b, BrandText(b)));
            _cboBrand.SelectedItem = _cboBrand.Items.Cast<Choice>().First(c => c.Value == settings.Brand);
            _cboBrand.SelectedIndexChanged += (_, _) => _txtSnapshotPath.Enabled = SelectedBrand == CameraBrand.Custom;

            _chkEnabled.Checked = settings.Enabled;
            _txtHost.Text = settings.Host ?? "";
            _numHttpPort.Value = Math.Clamp(settings.HttpPort, 1, 65535);
            _numRtspPort.Value = Math.Clamp(settings.RtspPort, 1, 65535);
            _txtUser.Text = settings.Username ?? "";
            _txtPassword.Text = settings.Password ?? "";
            _txtPassword.UseSystemPasswordChar = true;
            _chkShowPassword.CheckedChanged += (_, _) => _txtPassword.UseSystemPasswordChar = !_chkShowPassword.Checked;
            _numChannel.Value = Math.Clamp(settings.Channel, 1, 64);
            _txtSnapshotPath.Text = settings.SnapshotPath ?? "";
            _txtSnapshotPath.Enabled = settings.Brand == CameraBrand.Custom;

            Card = new Card { Dock = DockStyle.Fill, Padding = new Padding(1) };

            var fields = new TableLayoutPanel
            {
                Dock = DockStyle.Top, ColumnCount = 4, AutoSize = true,
                Padding = new Padding(14, 8, 14, 4), BackColor = AgrisTheme.Surface
            };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22F));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22F));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));

            TestButton.Dock = DockStyle.Fill;
            TestButton.MinimumSize = Size.Empty;

            AddRow(fields, (null, _chkEnabled), (null, TestButton));
            AddRow(fields, (Loc.T("camcfg.host"), _txtHost), (Loc.T("camcfg.http_port"), _numHttpPort));
            AddRow(fields, (Loc.T("camcfg.username"), _txtUser), (Loc.T("camcfg.rtsp_port"), _numRtspPort));
            AddRow(fields, (Loc.T("camcfg.password"), _txtPassword), (null, _chkShowPassword));
            AddRow(fields, (Loc.T("camcfg.brand"), _cboBrand), (Loc.T("camcfg.channel"), _numChannel));
            AddRow(fields, (Loc.T("camcfg.snapshot_path"), _txtSnapshotPath), (null, new Label()));

            var status = new Panel { Dock = DockStyle.Top, Height = 110, Padding = new Padding(14, 4, 14, 4), BackColor = AgrisTheme.Surface };
            status.Controls.Add(_lblDetail);
            status.Controls.Add(_lblStatus);
            status.Controls.Add(_lblSystem);

            var preview = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 0, 14, 12), BackColor = AgrisTheme.Surface };
            _picPreview.Controls.Add(_lblPreviewNone);
            preview.Controls.Add(_picPreview);

            Card.Controls.Add(preview);
            Card.Controls.Add(status);
            Card.Controls.Add(fields);
            Card.Controls.Add(GridBuilder.SectionHeader(Title));

            SetStatus(Loc.T("camcfg.st_not_tested"), AgrisTheme.TextMuted, null);
            Card.Disposed += (_, _) => _picPreview.Image?.Dispose();
        }

        private CameraBrand SelectedBrand => (_cboBrand.SelectedItem as Choice)?.Value ?? CameraBrand.Auto;

        public CameraConnectionSettings Read() => _original with
        {
            Enabled = _chkEnabled.Checked,
            Host = string.IsNullOrWhiteSpace(_txtHost.Text) ? null : _txtHost.Text.Trim(),
            HttpPort = (int)_numHttpPort.Value,
            RtspPort = (int)_numRtspPort.Value,
            Username = string.IsNullOrWhiteSpace(_txtUser.Text) ? null : _txtUser.Text.Trim(),
            Password = string.IsNullOrEmpty(_txtPassword.Text) ? null : _txtPassword.Text,
            Brand = SelectedBrand,
            Channel = (int)_numChannel.Value,
            SnapshotPath = string.IsNullOrWhiteSpace(_txtSnapshotPath.Text) ? null : _txtSnapshotPath.Text.Trim()
        };

        public void ShowTesting() => SetStatus(Loc.T("camcfg.st_testing"), AgrisTheme.Info, null);

        /// <summary>Hiện kết quả kiểm tra / ảnh xem thử. Thread-safe.</summary>
        public void ShowResult(CameraTestResult r)
        {
            if (Card.IsDisposed) return;
            if (Card.InvokeRequired) { Card.BeginInvoke(() => ShowResult(r)); return; }

            if (r.Success)
            {
                SetImage(r.Image!);
                // Auto đã nhận ra hãng → chọn luôn để lần sau không phải dò.
                if (SelectedBrand == CameraBrand.Auto && r.DetectedBrand is { } b and not CameraBrand.Auto)
                    _cboBrand.SelectedItem = _cboBrand.Items.Cast<Choice>().First(c => c.Value == b);

                var lines = new List<string> { Loc.T("camcfg.latency", r.LatencyMs ?? 0, DateTime.Now.ToString("HH:mm:ss")) };
                if (r.RtspUrl is { } rtsp)
                    lines.Add($"{Loc.T("camcfg.rtsp_url")}: {rtsp}" + (r.RtspPortOpen == false ? " " + Loc.T("camcfg.rtsp_closed") : ""));
                SetStatus(Loc.T("camcfg.st_online"), AgrisTheme.Success, string.Join(Environment.NewLine, lines));
            }
            else
            {
                // Không giữ ảnh cũ như đang trực tuyến.
                SetImage(null);
                var code = (r.Error ?? "HTTP_ERROR").ToLowerInvariant();
                var color = r.Error == "NOT_CONFIGURED" ? AgrisTheme.TextMuted : AgrisTheme.Danger;
                SetStatus(Loc.T("camcfg.st_" + code), color, r.ErrorDetail);
            }
        }

        /// <summary>Trạng thái theo cấu hình đã lưu (kiểm tra nền).</summary>
        public void ShowSystemStatus(CameraLiveState live)
        {
            var text = live switch
            {
                { Error: "NOT_CONFIGURED" } => Loc.T("camcfg.sys_not_configured"),
                { Error: "DISABLED" } => Loc.T("camcfg.sys_disabled"),
                { CheckedAt: null } => Loc.T("camcfg.sys_checking"),
                { Online: true } => Loc.T("camcfg.sys_online", live.LatencyMs ?? 0,
                    Domain.Common.VnTime.ToVnTime(live.CheckedAt.Value).ToString("HH:mm:ss")),
                _ => Loc.T("camcfg.sys_offline", Domain.Common.VnTime.ToVnTime(live.CheckedAt!.Value).ToString("HH:mm:ss"))
            };
            _lblSystem.Text = "● " + text;
            _lblSystem.ForeColor = live switch
            {
                { Online: true } => AgrisTheme.Success,
                { Error: "HOST_UNREACHABLE" } => AgrisTheme.Danger,
                _ => AgrisTheme.TextMuted
            };
        }

        private void SetStatus(string text, Color color, string? detail)
        {
            _lblStatus.Text = "● " + text;
            _lblStatus.ForeColor = color;
            _lblDetail.Text = detail ?? "";
        }

        private void SetImage(byte[]? bytes)
        {
            Image? image = null;
            if (bytes is not null)
            {
                try
                {
                    using var ms = new MemoryStream(bytes);
                    using var decoded = Image.FromStream(ms);
                    image = new Bitmap(decoded); // tách khỏi stream để giải phóng stream ngay
                }
                catch (ArgumentException)
                {
                    image = null;
                }
            }

            var old = _picPreview.Image;
            _picPreview.Image = image;
            old?.Dispose();
            _lblPreviewNone.Visible = image is null;
        }

        private static void AddRow(TableLayoutPanel table,
            (string? Caption, Control Input) left, (string? Caption, Control Input) right)
        {
            var row = table.RowCount;
            table.RowCount = row + 1;
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            Place(table, left, 0, row);
            Place(table, right, 2, row);
        }

        private static void Place(TableLayoutPanel table, (string? Caption, Control Input) cell, int col, int row)
        {
            if (cell.Caption is not null)
            {
                table.Controls.Add(new Label
                {
                    Dock = DockStyle.Fill, Text = cell.Caption, Font = AgrisTheme.Body(9.5F),
                    ForeColor = AgrisTheme.TextSecondary, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
                    Margin = new Padding(col == 0 ? 0 : 10, 0, 4, 0)
                }, col, row);
                cell.Input.Margin = new Padding(0, 6, 0, 6);
                table.Controls.Add(cell.Input, col + 1, row);
            }
            else
            {
                cell.Input.Margin = new Padding(col == 0 ? 0 : 10, 8, 0, 4);
                table.Controls.Add(cell.Input, col, row);
                table.SetColumnSpan(cell.Input, 2);
            }
        }

        private static TextBox Text() => new() { Dock = DockStyle.Fill, Font = AgrisTheme.Body(10F) };

        private static NumericUpDown Port() => new()
        {
            Dock = DockStyle.Fill, Minimum = 1, Maximum = 65535, Font = AgrisTheme.Mono(10F),
            TextAlign = HorizontalAlignment.Right
        };

        private sealed record Choice(CameraBrand Value, string Text)
        {
            public override string ToString() => Text;
        }
    }
}
