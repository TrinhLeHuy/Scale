using System.Text.Encodings.Web;
using System.Text.Json;
using TTC.Weighing.Application.Queries;
using TTC.Weighing.WinForms.Controls;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Theme;
using TTC.Weighing.Domain.Common;

namespace TTC.Weighing.WinForms.Forms;

/// <summary>
/// Nhật ký đẩy / kéo API: mọi lượt app gọi DigiScale, FRM (kể cả bắt chính sách), ERP, Portal.
/// Chọn một ngày → lọc theo hệ thống / hướng / kết quả / bắt chính sách / mã chứng từ → bấm dòng
/// xem nguyên văn nội dung gửi và nhận (đã che mật khẩu, token).
/// </summary>
public sealed class ApiLogForm : Form
{
    private const string GLYPH_LOG = "\uE9D5";
    private const string GLYPH_DETAIL = "\uE8A5";

    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // giữ nguyên tiếng Việt, không \uXXXX
    };

    private readonly IApiCallLogQueries _logs;
    private readonly CancellationTokenSource _cts = new();
    private int _loadId;

    /// <summary>Đang xem theo chuyến (số đăng tài, lệnh đốn) — bỏ qua ngày, lấy cả lịch sử của chuyến.</summary>
    private IReadOnlyList<string>? _tripTerms;
    private string? _tripLabel;

    /// <summary>Mở nhật ký của MỘT chuyến: mọi lượt đẩy / kéo có số đăng tài hoặc lệnh đốn này, mọi ngày.</summary>
    public void FocusTrip(string tripNo, string? maLenhDon)
    {
        _tripTerms = [tripNo, maLenhDon ?? string.Empty];
        _tripLabel = string.IsNullOrWhiteSpace(maLenhDon) ? tripNo : $"{tripNo} · {maLenhDon}";
        Text = Loc.T("apilog.trip_title", _tripLabel);
        _dtpDay.Enabled = false;
        _btnToday.Enabled = false;
    }

    // ── Bộ lọc ──
    private readonly DateTimePicker _dtpDay = new()
    {
        Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 124,
        Font = AgrisTheme.Body(10.5F, FontStyle.Bold)
    };
    private readonly Button _btnToday = GridBuilder.SecondaryButton(Loc.T("ccs.ux_today"), new Size(96, 32));
    private readonly ComboBox _cboSystem = Combo(140);
    private readonly ComboBox _cboDirection = Combo(130);
    private readonly ComboBox _cboResult = Combo(130);
    private readonly CheckBox _chkPolicy = new() { Text = "★ " + Loc.T("apilog.filter_policy"), AutoSize = true, Cursor = Cursors.Hand, Margin = new Padding(8, 8, 8, 0) };
    private readonly TextBox _txtSearch = new()
    {
        Width = 220, Font = AgrisTheme.Body(10F),
        PlaceholderText = Loc.T("apilog.search_placeholder"), Margin = new Padding(8, 4, 8, 0)
    };
    private readonly Button _btnReload = GridBuilder.PrimaryButton("↻  " + Loc.T("apilog.btn_reload"), new Size(120, 32));
    private readonly System.Windows.Forms.Timer _searchDebounce = new() { Interval = 400 };

    // ── Tóm tắt ──
    private readonly MetricTile _tileTotal = new(Loc.T("apilog.tile_total"), AgrisTheme.BrandTeal);
    private readonly MetricTile _tileOk = new(Loc.T("apilog.tile_ok"), AgrisTheme.Success);
    private readonly MetricTile _tileFail = new(Loc.T("apilog.tile_fail"), AgrisTheme.Danger);
    private readonly MetricTile _tilePolicy = new(Loc.T("apilog.tile_policy"), AgrisTheme.Warning);

    // ── Lưới + chi tiết ──
    private readonly DataGridView _grid = GridBuilder.ReadOnlyGrid("API log");
    private readonly IconSectionHeader _hdrDetail = new(GLYPH_DETAIL, Loc.T("apilog.detail_title"), AgrisTheme.BrandTeal, Loc.T("apilog.detail_hint"));
    private readonly Label _lblDetail = new()
    {
        Dock = DockStyle.Top, Height = 92, Padding = new Padding(14, 6, 10, 6),
        Font = AgrisTheme.Body(9.5F), ForeColor = AgrisTheme.TextSecondary, BackColor = AgrisTheme.Surface
    };
    private readonly TextBox _txtRequest = JsonBox();
    private readonly TextBox _txtResponse = JsonBox();

    public ApiLogForm(IApiCallLogQueries logs)
    {
        _logs = logs;
        Text = Loc.T("apilog.title");
        MinimumSize = new Size(1000, 600);
        BackColor = AgrisTheme.Canvas;
        DoubleBuffered = true;

        BuildLayout();

        Load += async (_, _) => await LoadAsync();
    }

    private static ComboBox Combo(int width) => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList, Width = width, Font = AgrisTheme.Body(10F), Margin = new Padding(0, 4, 8, 0)
    };

    private static TextBox JsonBox() => new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false,
        Dock = DockStyle.Fill, Font = AgrisTheme.Mono(9.5F), BackColor = AgrisTheme.SurfaceMuted,
        ForeColor = AgrisTheme.TextPrimary, BorderStyle = BorderStyle.None
    };

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, MetricTile.PreferredTileHeight + 12));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(BuildFilterCard(), 0, 0);
        root.Controls.Add(BuildSummaryRow(), 0, 1);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 8,
            BackColor = AgrisTheme.Canvas, Margin = new Padding(0, 6, 0, 0)
        };
        split.Panel1.Controls.Add(BuildGridCard());
        split.Panel2.Controls.Add(BuildDetailCard());
        split.SizeChanged += (_, _) =>
        {
            if (split.Width > 400) split.SplitterDistance = (int)(split.Width * 0.56);
        };
        root.Controls.Add(split, 0, 2);

        Controls.Add(root);
    }

    private Card BuildFilterCard()
    {
        var card = new Card { Dock = DockStyle.Top, Padding = new Padding(1), Margin = new Padding(0, 0, 0, 6) };
        var header = new IconSectionHeader(GLYPH_LOG, Loc.T("apilog.title"), AgrisTheme.BrandTeal, Loc.T("apilog.subtitle"));

        _cboSystem.Items.AddRange([Loc.T("apilog.all"), "DigiScale", "FRM", "ERP", "Portal"]);
        _cboDirection.Items.AddRange([Loc.T("apilog.all"), "⬆ " + Loc.T("apilog.push"), "⬇ " + Loc.T("apilog.pull")]);
        _cboResult.Items.AddRange([Loc.T("apilog.all"), "✓ " + Loc.T("apilog.ok"), "✗ " + Loc.T("apilog.fail")]);
        _cboSystem.SelectedIndex = 0;
        _cboDirection.SelectedIndex = 0;
        _cboResult.SelectedIndex = 0;
        _dtpDay.Value = VnTime.Today;
        _dtpDay.MaxDate = VnTime.Today.AddDays(1);
        _dtpDay.Margin = new Padding(0, 4, 8, 0);
        _btnToday.MaximumSize = new Size(100, 36);

        // Hai hàng để vừa cả màn hình nhỏ: (ngày, hệ thống, hướng, kết quả) / (bắt chính sách, tìm, tải lại).
        FlowLayoutPanel Row(int top) => new()
        {
            Dock = DockStyle.Top, Height = 48, WrapContents = false, AutoScroll = false,
            Padding = new Padding(14, top, 8, 4), BackColor = AgrisTheme.Surface
        };
        var row1 = Row(8);
        var row2 = Row(2);
        Label Cap(string text) => new()
        {
            Text = text, AutoSize = true, Margin = new Padding(0, 9, 6, 0),
            Font = AgrisTheme.Body(9F, FontStyle.Bold), ForeColor = AgrisTheme.TextSecondary
        };
        row1.Controls.AddRange([
            Cap("📅 " + Loc.T("apilog.day")), _dtpDay, _btnToday,
            Cap(Loc.T("apilog.system")), _cboSystem,
            Cap(Loc.T("apilog.direction")), _cboDirection
        ]);
        _chkPolicy.Margin = new Padding(12, 8, 12, 0);
        row2.Controls.AddRange([Cap(Loc.T("apilog.result")), _cboResult, _chkPolicy, _txtSearch, _btnReload]);
        _txtSearch.Width = 280;
        var bar = new Panel { Dock = DockStyle.Top, Height = row1.Height + row2.Height, BackColor = AgrisTheme.Surface };
        bar.Controls.Add(row2);
        bar.Controls.Add(row1);

        _dtpDay.ValueChanged += async (_, _) => await LoadAsync();
        _btnToday.Click += (_, _) => _dtpDay.Value = VnTime.Today;
        _cboSystem.SelectedIndexChanged += async (_, _) => await LoadAsync();
        _cboDirection.SelectedIndexChanged += async (_, _) => await LoadAsync();
        _cboResult.SelectedIndexChanged += async (_, _) => await LoadAsync();
        _chkPolicy.CheckedChanged += async (_, _) => await LoadAsync();
        _btnReload.Click += async (_, _) => await LoadAsync();
        _txtSearch.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            _searchDebounce.Stop();
            await LoadAsync();
        };
        // Gõ là tìm luôn (chờ ngắn để không tra DB theo từng phím); tắt bộ gõ để chữ vào ô ngay, không đợi Space.
        Helpers.SearchText.Prepare(_txtSearch);
        _txtSearch.TextChanged += (_, _) => { _searchDebounce.Stop(); _searchDebounce.Start(); };
        _searchDebounce.Tick += async (_, _) => { _searchDebounce.Stop(); await LoadAsync(); };
        Disposed += (_, _) => _searchDebounce.Dispose();

        card.Controls.Add(bar);
        card.Controls.Add(header);
        card.Height = header.Height + bar.Height + card.Padding.Vertical;
        return card;
    }

    private TableLayoutPanel BuildSummaryRow()
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = new Padding(0, 0, 0, 6) };
        MetricTile[] tiles = [_tileTotal, _tileOk, _tileFail, _tilePolicy];
        for (var i = 0; i < tiles.Length; i++)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            tiles[i].Dock = DockStyle.Fill;
            tiles[i].Margin = new Padding(i == 0 ? 0 : 6, 0, i == tiles.Length - 1 ? 0 : 6, 0);
            row.Controls.Add(tiles[i], i, 0);
        }
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return row;
    }

    private Card BuildGridCard()
    {
        var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(1) };
        _grid.Dock = DockStyle.Fill;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.MultiSelect = false;
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "Time", HeaderText = Loc.T("apilog.col_time"), FillWeight = 11, MinimumWidth = 70 },
            new DataGridViewTextBoxColumn { Name = "Dir", HeaderText = Loc.T("apilog.direction"), FillWeight = 10, MinimumWidth = 70 },
            new DataGridViewTextBoxColumn { Name = "Sys", HeaderText = Loc.T("apilog.system"), FillWeight = 10, MinimumWidth = 70 },
            new DataGridViewTextBoxColumn { Name = "Op", HeaderText = Loc.T("apilog.col_operation"), FillWeight = 22, MinimumWidth = 120 },
            new DataGridViewTextBoxColumn { Name = "Key", HeaderText = Loc.T("apilog.col_key"), FillWeight = 18, MinimumWidth = 100 },
            new DataGridViewTextBoxColumn { Name = "Http", HeaderText = "HTTP", FillWeight = 7, MinimumWidth = 50 },
            new DataGridViewTextBoxColumn { Name = "Result", HeaderText = Loc.T("apilog.result"), FillWeight = 12, MinimumWidth = 80 },
            new DataGridViewTextBoxColumn { Name = "Ms", HeaderText = "ms", FillWeight = 8, MinimumWidth = 50 });
        foreach (var name in new[] { "Time", "Dir", "Sys", "Http", "Result", "Ms" })
            _grid.Columns[name]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _grid.Columns["Key"]!.DefaultCellStyle.Font = AgrisTheme.Mono(9.5F, FontStyle.Bold);
        _grid.SelectionChanged += async (_, _) => await ShowSelectedAsync();
        GridBuilder.AttachEmptyState(_grid, Loc.T("apilog.empty"));

        card.Controls.Add(_grid);
        GridScrollBar.Attach(_grid);
        return card;
    }

    private Card BuildDetailCard()
    {
        var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(1) };

        var tabs = new TabControl { Dock = DockStyle.Fill, Font = AgrisTheme.Body(9.5F, FontStyle.Bold) };
        tabs.TabPages.Add(JsonPage("⬆ " + Loc.T("apilog.tab_request"), _txtRequest));
        tabs.TabPages.Add(JsonPage("⬇ " + Loc.T("apilog.tab_response"), _txtResponse));

        var copyReq = GridBuilder.SecondaryButton("⧉  " + Loc.T("apilog.copy_request"), new Size(150, 32));
        var copyRes = GridBuilder.SecondaryButton("⧉  " + Loc.T("apilog.copy_response"), new Size(150, 32));
        copyReq.Click += (_, _) => CopyText(_txtRequest.Text);
        copyRes.Click += (_, _) => CopyText(_txtResponse.Text);
        _hdrDetail.Actions.Controls.Add(copyRes);
        _hdrDetail.Actions.Controls.Add(copyReq);

        card.Controls.Add(tabs);
        card.Controls.Add(_lblDetail);
        card.Controls.Add(_hdrDetail);
        return card;
    }

    private static TabPage JsonPage(string title, TextBox box)
    {
        var page = new TabPage(title) { BackColor = AgrisTheme.SurfaceMuted, Padding = new Padding(6) };
        page.Controls.Add(box);
        return page;
    }

    private static void CopyText(string text)
    {
        if (!string.IsNullOrEmpty(text)) Clipboard.SetText(text);
    }

    private ApiCallLogFilter BuildFilter()
    {
        var day = _dtpDay.Value.Date;
        var next = day.AddDays(1);
        string? system = _cboSystem.SelectedIndex switch
        {
            1 => "DIGISCALE", 2 => "FRM", 3 => "ERP", 4 => "PORTAL", _ => null
        };
        string? direction = _cboDirection.SelectedIndex switch { 1 => "PUSH", 2 => "PULL", _ => null };
        bool? success = _cboResult.SelectedIndex switch { 1 => true, 2 => false, _ => null };
        if (_tripTerms is not null)
            return new ApiCallLogFilter(DateTimeOffset.UtcNow.AddDays(-365), DateTimeOffset.UtcNow.AddDays(1),
                system, direction, success, _chkPolicy.Checked,
                string.IsNullOrWhiteSpace(_txtSearch.Text) ? null : _txtSearch.Text.Trim(), AnyOf: _tripTerms);
        return new ApiCallLogFilter(
            new DateTimeOffset(day, VnTime.OffsetAt(day)).ToUniversalTime(),
            new DateTimeOffset(next, VnTime.OffsetAt(next)).ToUniversalTime(),
            system, direction, success, _chkPolicy.Checked,
            string.IsNullOrWhiteSpace(_txtSearch.Text) ? null : _txtSearch.Text.Trim());
    }

    private async Task LoadAsync()
    {
        var id = ++_loadId;
        try
        {
            var rows = await _logs.ListAsync(BuildFilter(), _cts.Token);
            if (id != _loadId) return; // đã có lần tải mới hơn

            _tileTotal.SetValue(rows.Count);
            _tileOk.SetValue(rows.Count(r => r.Success));
            _tileFail.SetValue(rows.Count(r => !r.Success));
            _tilePolicy.SetValue(rows.Count(r => r.IsPolicy));
            _tileTotal.SetNote(_tripLabel ?? _dtpDay.Value.ToString("dd/MM/yyyy"));

            _grid.SuspendLayout();
            _grid.Rows.Clear();
            foreach (var r in rows)
            {
                var idx = _grid.Rows.Add(
                    r.OccurredAt.ToVnTime().ToString(_tripTerms is null ? "HH:mm:ss" : "dd/MM HH:mm:ss"),
                    r.Direction == "PUSH" ? "⬆ " + Loc.T("apilog.push") : "⬇ " + Loc.T("apilog.pull"),
                    SystemLabel(r.System),
                    (r.IsPolicy ? "★ " : "") + OperationLabel(r.Operation),
                    r.BusinessKey ?? "—",
                    r.HttpStatus?.ToString() ?? "—",
                    r.Success ? "✓ " + Loc.T("apilog.ok") : "✗ " + Loc.T("apilog.fail"),
                    r.DurationMs.ToString("N0"));
                var row = _grid.Rows[idx];
                row.Tag = r.Id;
                row.Cells["Result"].Style.ForeColor = r.Success ? AgrisTheme.Success : AgrisTheme.Danger;
                row.Cells["Result"].Style.Font = AgrisTheme.Body(9F, FontStyle.Bold);
                row.Cells["Dir"].Style.ForeColor = r.Direction == "PUSH" ? AgrisTheme.BrandTeal : AgrisTheme.BrandGreen;
                if (r.IsPolicy) row.Cells["Op"].Style.ForeColor = AgrisTheme.Warning;
            }
            _grid.ResumeLayout();
            if (_grid.Rows.Count == 0) ClearDetail();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _lblDetail.Text = Loc.T("apilog.load_error", ex.Message);
            _lblDetail.ForeColor = AgrisTheme.Danger;
        }
    }

    private void ClearDetail()
    {
        _lblDetail.Text = string.Empty;
        _txtRequest.Clear();
        _txtResponse.Clear();
        _hdrDetail.SetSubtitle(Loc.T("apilog.detail_hint"));
    }

    private async Task ShowSelectedAsync()
    {
        if (_grid.CurrentRow?.Tag is not Guid id) return;
        try
        {
            var d = await _logs.GetAsync(id, _cts.Token);
            if (d is null || _grid.CurrentRow?.Tag is not Guid current || current != id) return;

            var r = d.Row;
            _hdrDetail.SetSubtitle($"{SystemLabel(r.System)} · {OperationLabel(r.Operation)}{(r.IsPolicy ? " · ★ " + Loc.T("apilog.filter_policy") : "")}");
            _lblDetail.ForeColor = r.Success ? AgrisTheme.TextSecondary : AgrisTheme.Danger;
            _lblDetail.Text =
                $"{d.Method}  {d.Url}\n" +
                Loc.T("apilog.detail_line", r.OccurredAt.ToVnTime().ToString("dd/MM/yyyy HH:mm:ss"),
                    r.HttpStatus?.ToString() ?? "—", r.DurationMs.ToString("N0"), d.Actor ?? "—", d.ClientName ?? "—") +
                (r.Success || string.IsNullOrWhiteSpace(r.Error) ? "" : "\n✗ " + OneLine(r.Error));
            _txtRequest.Text = Pretty(d.RequestBody);
            _txtResponse.Text = Pretty(d.ResponseBody);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _lblDetail.Text = Loc.T("apilog.load_error", ex.Message);
            _lblDetail.ForeColor = AgrisTheme.Danger;
        }
    }

    private static string OneLine(string s)
    {
        var t = s.Replace("\r", " ").Replace("\n", " ");
        return t.Length > 220 ? t[..220] + "…" : t;
    }

    /// <summary>JSON thụt lề cho dễ đọc; không phải JSON thì hiện nguyên văn.</summary>
    private static string Pretty(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return Loc.T("apilog.no_body");
        try
        {
            using var doc = JsonDocument.Parse(body);
            return JsonSerializer.Serialize(doc.RootElement, PrettyJson).Replace("\n", Environment.NewLine);
        }
        catch (JsonException)
        {
            return body.Replace("\n", Environment.NewLine);
        }
    }

    private static string SystemLabel(string system) => system switch
    {
        "DIGISCALE" => "DigiScale",
        "PORTAL" => "Portal",
        _ => system
    };

    /// <summary>Tên nghiệp vụ dễ hiểu theo logicalName / endpoint.</summary>
    private static string OperationLabel(string operation)
    {
        var op = operation.ToLowerInvariant();
        var key = op switch
        {
            "new_phieudangtai" => "apilog.op_dangtai",
            "new_phieucan" => "apilog.op_phieucan",
            "new_phieudotapchat" => "apilog.op_tapchat",
            "new_phieudoccs" => "apilog.op_ccs",
            "oracle-tickets" => "apilog.op_erp_ticket",
            _ when op.Contains("integration/frm") => "apilog.op_frm_data",
            _ when op.Contains("order-lines") => "apilog.op_erp_orders",
            _ when op.Contains("scale-rules") => "apilog.op_scale_rules",
            _ when op.Contains("sugarcane-info") => "apilog.op_frm_trip",
            _ => null
        };
        return key is null ? operation : $"{Loc.T(key)} ({operation})";
    }

    private bool _disposed;

    protected override void Dispose(bool disposing)
    {
        // Form có thể bị Dispose hai lần (đóng tab + scope DI) — chỉ hủy token một lần.
        if (disposing && !_disposed)
        {
            _disposed = true;
            _cts.Cancel();
            _cts.Dispose();
        }
        base.Dispose(disposing);
    }
}
