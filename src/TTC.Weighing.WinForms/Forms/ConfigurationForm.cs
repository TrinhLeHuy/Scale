using TTC.Weighing.WinForms.Controls;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Services;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Forms;

/// <summary>
/// UI-09 — Thiết bị đang kết nối trên máy này.
/// <para>
/// Không có danh sách thiết bị viết sẵn: mọi thiết bị USB đang cắm, cổng COM, máy in thật (USB / mạng / LPT)
/// được Windows báo thì hiện ra, gom theo nhóm suy từ lớp thiết bị. Vai trò trong phần mềm lấy từ thực tế:
/// máy in đang dùng in phiếu (theo cấu hình in hiện tại), máy quét (thiết bị đã thật sự gửi mã quét).
/// Tự cập nhật khi cắm / rút thiết bị.
/// </para>
/// Bố cục: tiêu đề + dải tổng quan → lưới thẻ thiết bị theo nhóm (trái) → khung kiểm tra nhanh: quét thử, in thử (phải).
/// </summary>
public sealed class ConfigurationForm : Form
{
    // Glyph Segoe Fluent Icons / MDL2 Assets
    private const string GLYPH_DEVICES = "\uE772";
    private const string GLYPH_PRINTER = "\uE749";
    private const string GLYPH_SCAN = "\uEE6F";
    private const string GLYPH_KEYBOARD = "\uE765";
    private const string GLYPH_CAMERA = "\uE722";
    private const string GLYPH_PORT = "\uE83E";
    private const string GLYPH_STORAGE = "\uEDA2";
    private const string GLYPH_AUDIO = "\uE767";
    private const string GLYPH_NETWORK = "\uE968";
    private const string GLYPH_BLUETOOTH = "\uE702";
    private const string GLYPH_USB = "\uE88E";
    private const string GLYPH_REFRESH = "\uE72C";

    private readonly string _machine = Environment.MachineName;
    private readonly CancellationTokenSource _cts = new();
    private readonly ToolTip _tips = new() { AutoPopDelay = 15000 };
    private bool _refreshing;
    private bool _refreshAgain;

    // ── Tiêu đề + tổng quan ──
    private readonly IconSectionHeader _header;
    private readonly FlowLayoutPanel _summary = new()
    {
        Dock = DockStyle.Top, Height = 48, Padding = new Padding(14, 8, 14, 6), WrapContents = false,
        BackColor = AgrisTheme.Canvas, AutoScroll = false
    };

    // ── Lưới thẻ ──
    private readonly FlowLayoutPanel _devices = new()
    {
        Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(14, 6, 6, 14),
        BackColor = AgrisTheme.Canvas, FlowDirection = FlowDirection.LeftToRight, WrapContents = true
    };

    // ── Kiểm tra nhanh ──
    private readonly Label _lblScanCode = new()
    {
        Dock = DockStyle.Top, Height = 54, TextAlign = ContentAlignment.MiddleLeft,
        Font = AgrisTheme.Mono(16F, FontStyle.Bold), ForeColor = AgrisTheme.TextMuted, AutoEllipsis = true
    };
    private readonly Label _lblScanMeta = new()
    {
        Dock = DockStyle.Top, Height = 46, Font = AgrisTheme.Body(9F), ForeColor = AgrisTheme.TextSecondary
    };
    private readonly Label _lblPrintTarget = new()
    {
        Dock = DockStyle.Top, Height = 46, Font = AgrisTheme.Body(9F), ForeColor = AgrisTheme.TextSecondary
    };
    private readonly Button _btnPrintTest = GridBuilder.PrimaryButton(Loc.T("dev.btn_print_test"), new Size(180, 38));
    private readonly Label _lblFooter = new()
    {
        Dock = DockStyle.Bottom, Height = 54, Font = AgrisTheme.Caption(), ForeColor = AgrisTheme.TextMuted,
        Padding = new Padding(4, 6, 4, 0)
    };

    public ConfigurationForm()
    {
        Text = Loc.T("dev.title");
        MinimumSize = new Size(1060, 680);
        BackColor = AgrisTheme.Canvas;
        DoubleBuffered = true;

        _header = new IconSectionHeader(GLYPH_DEVICES, Loc.T("dev.title"), AgrisTheme.BrandTeal, " ");
        var btnRefresh = GridBuilder.SecondaryButton(Loc.T("dev.btn_refresh"), new Size(120, 34));
        btnRefresh.Click += (_, _) => RequestRefresh();
        _header.Actions.Controls.Add(btnRefresh);

        BuildLayout();

        Load += (_, _) => RequestRefresh();
        VisibleChanged += (_, _) => { if (Visible && IsHandleCreated) RequestRefresh(); };
        _devices.Resize += (_, _) => LayoutSectionHeaders();
        _btnPrintTest.Click += (_, _) => PrintTestPage();

        // Quét thử ngay trên màn này: mã quét được hiện ở khung Quét thử (không tra chuyến).
        QrScanner.Register(this, () => null, ShowScanTest);
        DeviceWatcher.EnsureStarted();
        DeviceWatcher.Changed += OnDevicesChanged;
        DeviceWatcher.ScannerSeen += OnScannerSeen;
        Disposed += (_, _) =>
        {
            DeviceWatcher.Changed -= OnDevicesChanged;
            DeviceWatcher.ScannerSeen -= OnScannerSeen;
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  Bố cục
    // ═══════════════════════════════════════════════════════════

    private void BuildLayout()
    {
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = AgrisTheme.Canvas,
            Padding = new Padding(0, 0, 14, 14)
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.Controls.Add(_devices, 0, 0);
        body.Controls.Add(BuildQuickChecks(), 1, 0);

        Controls.Add(body);
        Controls.Add(_summary);
        Controls.Add(_header);
    }

    /// <summary>Khung bên phải: Quét thử + In thử + thông tin nhật ký.</summary>
    private Control BuildQuickChecks()
    {
        var side = new Panel { Dock = DockStyle.Fill, BackColor = AgrisTheme.Canvas, Padding = new Padding(0, 6, 0, 0) };

        // Quét thử
        var scanCard = new Card { Dock = DockStyle.Top, Height = 222, Padding = new Padding(14, 0, 14, 10) };
        var scanHint = new Label
        {
            Dock = DockStyle.Top, Height = 56, Text = Loc.T("dev.scan_test_hint"),
            Font = AgrisTheme.Body(9F), ForeColor = AgrisTheme.TextMuted
        };
        scanCard.Controls.Add(_lblScanMeta);
        scanCard.Controls.Add(_lblScanCode);
        scanCard.Controls.Add(scanHint);
        scanCard.Controls.Add(new IconSectionHeader(GLYPH_SCAN, Loc.T("dev.scan_test_title"), AgrisTheme.BrandGreen) { Margin = Padding.Empty });

        var gap = new Panel { Dock = DockStyle.Top, Height = 12, BackColor = AgrisTheme.Canvas };

        // In thử
        var printCard = new Card { Dock = DockStyle.Top, Height = 170, Padding = new Padding(14, 0, 14, 12) };
        var printBar = new Panel { Dock = DockStyle.Top, Height = 44 };
        _btnPrintTest.Dock = DockStyle.Left;
        printBar.Controls.Add(_btnPrintTest);
        printCard.Controls.Add(printBar);
        printCard.Controls.Add(_lblPrintTarget);
        printCard.Controls.Add(new IconSectionHeader(GLYPH_PRINTER, Loc.T("dev.print_test_title"), AgrisTheme.BrandTeal));

        side.Controls.Add(_lblFooter);
        side.Controls.Add(printCard);
        side.Controls.Add(gap);
        side.Controls.Add(scanCard);

        _lblScanCode.Text = Loc.T("dev.scan_test_wait");
        _lblFooter.Text = Loc.T("dev.footer_log", QrScanLog.LogFile);
        return side;
    }

    // ═══════════════════════════════════════════════════════════
    //  Dò thiết bị (luồng nền) → vẽ lại
    // ═══════════════════════════════════════════════════════════

    private void OnDevicesChanged()
    {
        if (!IsDisposed && IsHandleCreated && Visible) RequestRefresh();
    }

    private void OnScannerSeen(DeviceWatcher.ScannerInfo info)
    {
        if (IsDisposed || !IsHandleCreated) return;
        RequestRefresh();
    }

    /// <summary>Dò lại thiết bị. Đang dò thì hẹn dò thêm một lần khi xong (không chạy chồng).</summary>
    private async void RequestRefresh()
    {
        if (_refreshing) { _refreshAgain = true; return; }
        _refreshing = true;
        try
        {
            do
            {
                _refreshAgain = false;
                var scanners = DeviceWatcher.SnapshotScanners();
                var labels = BuildLabels();
                var result = await Task.Run(() => DeviceDiscovery.Discover(labels, scanners), _cts.Token);
                if (IsDisposed) return;
                Render(result);
            } while (_refreshAgain && !IsDisposed);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            _header.SetSubtitle(Loc.T("dev.discover_error", ex.Message));
        }
        finally { _refreshing = false; }
    }

    private static DeviceDiscovery.Labels BuildLabels() => new(
        RoleTicketThermal: () => Loc.T("dev.role_ticket_thermal"),
        RoleTicket: () => Loc.T("dev.role_ticket"),
        RoleTicketFallback: () => Loc.T("dev.role_ticket_fallback"),
        RoleScanner: (at, _) => Loc.T("dev.role_scanner", at.ToString("dd/MM HH:mm")),
        DriverProblem: code => Loc.T("dev.problem_driver", code),
        Disabled: () => Loc.T("dev.problem_disabled"),
        PrinterStatus: PrinterStatusText);

    private static string PrinterStatusText(uint s)
    {
        var parts = new List<string>();
        void Add(uint flag, string key) { if ((s & flag) != 0) parts.Add(Loc.T(key)); }
        Add(DeviceDiscovery.STATUS_OFFLINE | DeviceDiscovery.STATUS_NOT_AVAILABLE, "dev.pst_offline");
        Add(DeviceDiscovery.STATUS_PAUSED, "dev.pst_paused");
        Add(DeviceDiscovery.STATUS_PAPER_OUT, "dev.pst_paper_out");
        Add(DeviceDiscovery.STATUS_PAPER_JAM, "dev.pst_paper_jam");
        Add(DeviceDiscovery.STATUS_PAPER_PROBLEM, "dev.pst_paper_problem");
        Add(DeviceDiscovery.STATUS_NO_TONER, "dev.pst_no_toner");
        Add(DeviceDiscovery.STATUS_DOOR_OPEN, "dev.pst_door_open");
        if (parts.Count == 0 && (s & DeviceDiscovery.STATUS_ERROR) != 0) parts.Add(Loc.T("dev.pst_error"));
        return parts.Count == 0 ? Loc.T("dev.health_ok") : string.Join(" · ", parts);
    }

    // ── Thứ tự nhóm: thiết bị phần mềm cân dùng trước ──
    private static readonly DeviceCategory[] CategoryOrder =
    [
        DeviceCategory.Printer, DeviceCategory.Scanner, DeviceCategory.SerialPort, DeviceCategory.Camera,
        DeviceCategory.InputDevice, DeviceCategory.Storage, DeviceCategory.Audio, DeviceCategory.Network,
        DeviceCategory.Bluetooth, DeviceCategory.Other
    ];

    private void Render(DiscoveryResult r)
    {
        var attention = r.Devices.Count(d => d.Health != DeviceHealth.Ok);
        _header.SetSubtitle(Loc.T("dev.subtitle", _machine, r.At.ToString("HH:mm:ss")));

        // ── Dải tổng quan: tình trạng chung + số thiết bị theo nhóm ──
        _summary.SuspendLayout();
        DisposeChildren(_summary);
        _summary.Controls.Add(Chip(
            attention == 0 ? Loc.T("dev.summary_ok") : Loc.T("dev.summary_attention", attention),
            attention == 0 ? AgrisTheme.Success : AgrisTheme.Warning,
            attention == 0 ? AgrisTheme.SuccessSoft : AgrisTheme.WarningSoft));
        foreach (var cat in CategoryOrder)
        {
            var n = r.Devices.Count(d => d.Category == cat);
            if (n > 0) _summary.Controls.Add(Chip($"{CategoryName(cat)}  {n}", AgrisTheme.TextSecondary, AgrisTheme.Surface));
        }
        _summary.ResumeLayout();

        // ── Lưới thẻ theo nhóm ──
        _devices.SuspendLayout();
        _tips.RemoveAll();
        DisposeChildren(_devices);
        if (r.Devices.Count == 0)
        {
            _devices.Controls.Add(new Label
            {
                Text = Loc.T("dev.empty"), AutoSize = false, Height = 80, Width = 600,
                Font = AgrisTheme.Body(11F), ForeColor = AgrisTheme.TextMuted, TextAlign = ContentAlignment.MiddleLeft
            });
        }
        foreach (var cat in CategoryOrder)
        {
            var items = r.Devices.Where(d => d.Category == cat)
                .OrderByDescending(d => d.Roles.Count).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            if (items.Count == 0) continue;

            var header = new Label
            {
                Text = $"{CategoryName(cat)}  ·  {items.Count}",
                Font = AgrisTheme.Body(10F, FontStyle.Bold), ForeColor = AgrisTheme.TextSecondary,
                AutoSize = false, Height = 30, TextAlign = ContentAlignment.BottomLeft,
                Margin = new Padding(0, 4, 0, 6), Tag = "section"
            };
            _devices.Controls.Add(header);
            _devices.SetFlowBreak(header, true);

            DeviceCard? lastCard = null;
            foreach (var d in items)
            {
                lastCard = BuildCard(d);
                _devices.Controls.Add(lastCard);
            }
            if (lastCard is not null) _devices.SetFlowBreak(lastCard, true);
        }
        if (r.HiddenVirtualPrinters > 0)
        {
            var note = new Label
            {
                Text = Loc.T("dev.hidden_virtual", r.HiddenVirtualPrinters), AutoSize = true,
                Font = AgrisTheme.Caption(), ForeColor = AgrisTheme.TextMuted, Margin = new Padding(0, 4, 0, 0)
            };
            _devices.Controls.Add(note);
        }
        LayoutSectionHeaders();
        _devices.ResumeLayout();

        RenderPrintTarget(r);
    }

    private DeviceCard BuildCard(DeviceEntry d)
    {
        var (healthFore, healthBack, healthText) = d.Health switch
        {
            DeviceHealth.Error => (AgrisTheme.Danger, AgrisTheme.DangerSoft, d.HealthText ?? Loc.T("dev.health_error")),
            DeviceHealth.Warning => (AgrisTheme.Warning, AgrisTheme.WarningSoft, d.HealthText ?? Loc.T("dev.health_warning")),
            _ => (AgrisTheme.Success, AgrisTheme.SuccessSoft, Loc.T("dev.health_ok"))
        };

        var pills = new List<DeviceCard.Pill> { new(healthText, healthFore, healthBack) };
        pills.AddRange(d.Roles.Select(role => new DeviceCard.Pill(role, AgrisTheme.TextOnBrand, AgrisTheme.BrandTeal)));

        var subtitle = string.Join("  ·  ", new[] { d.Manufacturer, ConnectionName(d.Connection) }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        var card = new DeviceCard(CategoryGlyph(d.Category), CategoryAccent(d.Category), healthFore,
            d.Name, subtitle, d.Detail, pills);
        _tips.SetToolTip(card,
            string.Join(Environment.NewLine, new[] { d.Name, subtitle, d.Detail, healthText }
                .Concat(d.Roles).Append(d.Key).Where(s => !string.IsNullOrWhiteSpace(s))));
        return card;
    }

    /// <summary>Tiêu đề nhóm trải hết bề ngang lưới (để thẻ của nhóm xuống dòng dưới tiêu đề).</summary>
    private void LayoutSectionHeaders()
    {
        var width = Math.Max(200, _devices.ClientSize.Width - _devices.Padding.Horizontal - 8);
        foreach (Control c in _devices.Controls)
            if (Equals(c.Tag, "section")) c.Width = width;
    }

    private void RenderPrintTarget(DiscoveryResult r)
    {
        var ticket = r.Devices.FirstOrDefault(d => d.Roles.Contains(Loc.T("dev.role_ticket_thermal")))
                     ?? r.Devices.FirstOrDefault(d => d.Roles.Contains(Loc.T("dev.role_ticket")));
        if (ticket is not null)
        {
            _lblPrintTarget.Text = Loc.T("dev.print_test_target", ticket.Name);
            _lblPrintTarget.ForeColor = AgrisTheme.TextSecondary;
            _btnPrintTest.Enabled = true;
        }
        else if (Printing.ThermalPrint.Enabled && r.Devices.Any(d => d.Roles.Contains(Loc.T("dev.role_ticket_fallback"))))
        {
            _lblPrintTarget.Text = Loc.T("dev.print_test_thermal_missing");
            _lblPrintTarget.ForeColor = AgrisTheme.Warning;
            _btnPrintTest.Enabled = true;
        }
        else
        {
            _lblPrintTarget.Text = Loc.T("dev.print_test_none");
            _lblPrintTarget.ForeColor = AgrisTheme.Danger;
            _btnPrintTest.Enabled = false;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Quét thử / In thử
    // ═══════════════════════════════════════════════════════════

    private void ShowScanTest(string code)
    {
        _lblScanCode.Text = code;
        _lblScanCode.ForeColor = AgrisTheme.Success;
        var scan = QrScanner.LastScan;
        _lblScanMeta.Text = Loc.T("dev.scan_test_result", code.Length, (scan?.At ?? DateTime.Now).ToString("HH:mm:ss"),
            Loc.T(scan?.SentEnter == true ? "common.yes" : "common.no"));
        // Thiết bị gửi mã được DeviceWatcher ghi nhận → thẻ của nó hiện nhãn "Máy quét" sau lần dò kế tiếp.
    }

    /// <summary>In một trang thử ra máy in phiếu (in nhiệt USB nếu đang bật và có cắm, không thì máy in Windows).</summary>
    private void PrintTestPage()
    {
        var at = DateTime.Now;
        var printerName = _lblPrintTarget.Text;

        void Render(Graphics g, Rectangle b)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var title = new Font("Segoe UI", 16F, FontStyle.Bold);
            using var body = new Font("Segoe UI", 10F);
            var y = b.Top + 4f;
            y += Printing.PrintText.DrawCentered(g, Loc.T("dev.print_test_slip_title"), title, Brushes.Black, b.Left, b.Width, y) + 8;
            foreach (var line in new[]
                     {
                         Loc.T("dev.print_test_slip_machine", _machine),
                         Loc.T("dev.print_test_slip_time", at.ToString("dd/MM/yyyy HH:mm:ss")),
                         printerName
                     })
                y += Printing.PrintText.DrawCentered(g, line, body, Brushes.Black, b.Left, b.Width, y) + 4;
            y += 8;
            Printing.QrCodeRenderer.Draw(g, $"TEST-{_machine}", b.Left + (b.Width - 96) / 2, (int)y, 96);
        }

        try
        {
            if (Printing.ThermalPrint.TryPrint(this, Render, Printing.ThermalPrint.TICKET_DESIGN_WIDTH)) return;
            using var doc = new System.Drawing.Printing.PrintDocument { DocumentName = Loc.T("dev.print_test_slip_title") };
            doc.PrintPage += (_, pe) => Render(pe.Graphics!, pe.MarginBounds);
            Printing.PrintPrinter.ShowDialogAndPrint(doc, this);
        }
        catch (Exception ex)
        {
            Helpers.DigiScalePopup.ShowAlert(this, Helpers.AlertKind.Error, Loc.T("dev.print_test_failed_title"),
                ex.Message, Loc.T("dev.print_test_failed_action"));
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Nhãn / biểu tượng theo nhóm
    // ═══════════════════════════════════════════════════════════

    private static string CategoryName(DeviceCategory c) => Loc.T(c switch
    {
        DeviceCategory.Printer => "dev.cat_printer",
        DeviceCategory.Scanner => "dev.cat_scanner",
        DeviceCategory.InputDevice => "dev.cat_input",
        DeviceCategory.Camera => "dev.cat_camera",
        DeviceCategory.SerialPort => "dev.cat_serial",
        DeviceCategory.Storage => "dev.cat_storage",
        DeviceCategory.Audio => "dev.cat_audio",
        DeviceCategory.Network => "dev.cat_network",
        DeviceCategory.Bluetooth => "dev.cat_bluetooth",
        _ => "dev.cat_other"
    });

    private static string CategoryGlyph(DeviceCategory c) => c switch
    {
        DeviceCategory.Printer => GLYPH_PRINTER,
        DeviceCategory.Scanner => GLYPH_SCAN,
        DeviceCategory.InputDevice => GLYPH_KEYBOARD,
        DeviceCategory.Camera => GLYPH_CAMERA,
        DeviceCategory.SerialPort => GLYPH_PORT,
        DeviceCategory.Storage => GLYPH_STORAGE,
        DeviceCategory.Audio => GLYPH_AUDIO,
        DeviceCategory.Network => GLYPH_NETWORK,
        DeviceCategory.Bluetooth => GLYPH_BLUETOOTH,
        _ => GLYPH_USB
    };

    private static Color CategoryAccent(DeviceCategory c) => c switch
    {
        DeviceCategory.Printer => AgrisTheme.BrandTeal,
        DeviceCategory.Scanner => AgrisTheme.BrandGreen,
        DeviceCategory.SerialPort => AgrisTheme.Warning,
        DeviceCategory.Camera => AgrisTheme.Info,
        _ => AgrisTheme.Neutral
    };

    private static string? ConnectionName(DeviceConnection c) => Loc.T(c switch
    {
        DeviceConnection.Usb => "dev.conn_usb",
        DeviceConnection.Bluetooth => "dev.conn_bluetooth",
        DeviceConnection.Network => "dev.conn_network",
        DeviceConnection.Parallel => "dev.conn_parallel",
        DeviceConnection.Serial => "dev.conn_serial",
        _ => "dev.conn_internal"
    });

    private static Label Chip(string text, Color fore, Color back) => new()
    {
        Text = text, AutoSize = true, Padding = new Padding(10, 6, 10, 6), Margin = new Padding(0, 0, 8, 0),
        Font = AgrisTheme.Body(9F, FontStyle.Bold), ForeColor = fore, BackColor = back
    };

    private static void DisposeChildren(Control parent)
    {
        var old = parent.Controls.Cast<Control>().ToList();
        parent.Controls.Clear();
        foreach (var c in old) c.Dispose();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _cts.Cancel();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cts.Dispose();
            _tips.Dispose();
        }
        base.Dispose(disposing);
    }
}
