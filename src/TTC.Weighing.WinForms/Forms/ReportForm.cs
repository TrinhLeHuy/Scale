using System.Globalization;
using System.Text;
using TTC.Weighing.Application.Queries;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.WinForms.Controls;
using TTC.Weighing.WinForms.Helpers;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Services;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Forms;

/// <summary>
/// UI-10 — Báo cáo và phiếu. Lọc theo ngày/hồ sơ/xe, xuất dữ liệu và xem phiếu cân.
/// Bản in lại được đánh dấu; in lại KHÔNG sinh số cân mới và không gửi lại chứng từ (R-027).
/// </summary>
public sealed class ReportForm : Form
{
    private readonly IReportQueries _reports;
    private readonly TripEventBus _eventBus;
    private readonly Guid _siteId;
    private readonly CancellationTokenSource _cts = new();

    private readonly DateTimePicker _dtpFrom = new() { Format = DateTimePickerFormat.Short, Width = 150 };
    private readonly DateTimePicker _dtpTo = new() { Format = DateTimePickerFormat.Short, Width = 150 };
    private readonly TextBox _txtProfileCode = new();
    private readonly TextBox _txtPlate = new();

    private readonly Button _btnSearch = GridBuilder.PrimaryButton(Loc.T("report.btn_search"), new Size(120, 34));
    private readonly Button _btnExport = GridBuilder.SecondaryButton(Loc.T("report.btn_export"), new Size(130, 34));
    private readonly Button _btnPreviewTicket =
        GridBuilder.SecondaryButton(Loc.T("report.btn_preview"), new Size(160, 34));

    private readonly DataGridView _grid = GridBuilder.ReadOnlyGrid(Loc.T("report.section_report"));

    // ── Tách Mía / Vật tư + xuất Excel ──
    private readonly TripCategorySwitch _category = new("report.weigh");
    private readonly Button _btnExportExcel = GridBuilder.ExportExcelButton();
    private readonly Label _lblRowCount = new();

    public ReportForm(IReportQueries reports, ISiteContext siteContext, TripEventBus eventBus)
    {
        _reports = reports;
        _eventBus = eventBus;
        _siteId = siteContext.CurrentSiteId;

        Text = Loc.T("report.title");
        MinimumSize = new Size(1120, 720);
        BackColor = AgrisTheme.Canvas;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        BuildLayout();
        Load += async (_, _) => await SearchAsync();

        _eventBus.TripChanged += OnTripChangedFromOtherForm;
    }

    private void BuildLayout()
    {
        ConfigureGrid();

        Controls.Add(FormLayout.VerticalStack(
            (BuildFilterCard(), SizeType.AutoSize, 0),
            (BuildReportCard(), SizeType.Percent, 100)));
    }

    private void ConfigureGrid()
    {
        _dtpFrom.Value = DateTime.Today.AddDays(-7);
        _dtpTo.Value = DateTime.Today.AddDays(1);

        _grid.Columns.AddRange(
            GridBuilder.Column(Loc.T("col.trip_no"), nameof(WeighReportRow.TripNo), 120),
            GridBuilder.Column(Loc.T("col.reference_no"), nameof(WeighReportRow.ReferenceNo), 115),
            GridBuilder.Column(Loc.T("col.profile"), nameof(WeighReportRow.ProfileCode), 140),
            GridBuilder.Column(Loc.T("col.plate"), nameof(WeighReportRow.Plate), 90),
            GridBuilder.Column(Loc.T("col.first_weight"), nameof(WeighReportRow.GrossKg), 100, "N3"),
            GridBuilder.Column(Loc.T("col.second_weight"), nameof(WeighReportRow.TareKg), 100, "N3"),
            GridBuilder.Column(Loc.T("col.net_weight"), nameof(WeighReportRow.NetKg), 100, "N3"),
            GridBuilder.Column(Loc.T("col.impurity_pct"), nameof(WeighReportRow.ImpurityPercent), 90, "N4"),
            GridBuilder.Column(Loc.T("col.ccs"), nameof(WeighReportRow.CcsValue), 80, "N2"),
            GridBuilder.Column(Loc.T("col.reconciliation"), nameof(WeighReportRow.OverallVerdict), 90),
            GridBuilder.Column(Loc.T("col.manual_count"), nameof(WeighReportRow.ManualInterventionCount), 90),
            GridBuilder.Column(Loc.T("col.completed_at"), nameof(WeighReportRow.CompletedAt), 125));

        GridBuilder.AttachEmptyState(_grid, Loc.T("report.no_data"));
    }

    private Card BuildFilterCard()
    {
        var table = FormLayout.FieldTable();

        FormLayout.AddField(table, Loc.T("report.filter_from"), _dtpFrom);
        FormLayout.AddField(table, Loc.T("report.filter_to"), _dtpTo);
        FormLayout.AddField(table, Loc.T("report.filter_profile"), _txtProfileCode);
        FormLayout.AddField(table, Loc.T("report.filter_plate"), _txtPlate);

        _btnSearch.Click += async (_, _) => await SearchAsync();
        _btnExport.Click += (_, _) => ExportCsv();
        _btnPreviewTicket.Click += (_, _) => PreviewTicket();

        FormLayout.AddFullWidth(table,
            FormLayout.ActionBar(_btnSearch, _btnExport, _btnPreviewTicket), 54);

        return FormLayout.TitledCard(Loc.T("report.section_filter"), table);
    }

    /// <summary>Thẻ báo cáo: tiêu đề theo nhóm, ghi chú, thanh công cụ (Mía/Vật tư | Xuất Excel), lưới.</summary>
    private Card BuildReportCard()
    {
        var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(1) };
        var header = GridBuilder.SectionHeader(Loc.T("report.section_report"));
        var notice = GridBuilder.Notice(Loc.T("report.report_notice"));

        _lblRowCount.AutoSize = true;
        _lblRowCount.Font = AgrisTheme.Body(9F, FontStyle.Bold);
        _lblRowCount.ForeColor = AgrisTheme.TextSecondary;
        _lblRowCount.Margin = new Padding(0, 10, 8, 0);

        _btnExportExcel.Click += async (_, _) => await ExportExcelAsync();

        _category.CategoryChanged += async (_, _) =>
        {
            GridBuilder.ApplyCategoryHeader(card, header, Loc.T("report.section_report"), _category.Category);
            ApplyCategoryColumns();
            await SearchAsync();
        };

        GridBuilder.ApplyCategoryHeader(card, header, Loc.T("report.section_report"), _category.Category);
        ApplyCategoryColumns();

        var toolbar = new GridToolbar([_category], [_lblRowCount, _btnExportExcel]);

        _grid.Dock = DockStyle.Fill;
        card.Controls.Add(_grid);
        card.Controls.Add(toolbar);
        card.Controls.Add(notice);
        card.Controls.Add(header);
        return card;
    }

    /// <summary>Tạp chất / chữ đường chỉ có nghĩa với mía — ẩn khi xem vật tư (và không xuất ra Excel).</summary>
    private void ApplyCategoryColumns()
    {
        var isSugarcane = _category.Category == TripCategory.Sugarcane;
        _grid.Columns[nameof(WeighReportRow.ImpurityPercent)]!.Visible = isSugarcane;
        _grid.Columns[nameof(WeighReportRow.CcsValue)]!.Visible = isSugarcane;
    }

    private async Task ExportExcelAsync()
    {
        var rows = _grid.DataSource as IReadOnlyList<WeighReportRow> ?? [];
        var filters = new List<string>
        {
            Loc.T("export.filter_category", _category.CategoryLabel),
            Loc.T("export.filter_range", _dtpFrom.Value.ToString("dd/MM/yyyy"), _dtpTo.Value.ToString("dd/MM/yyyy"))
        };
        if (!string.IsNullOrWhiteSpace(_txtProfileCode.Text))
            filters.Add($"{Loc.T("report.filter_profile")}: {_txtProfileCode.Text.Trim()}");
        if (!string.IsNullOrWhiteSpace(_txtPlate.Text))
            filters.Add($"{Loc.T("report.filter_plate")}: {_txtPlate.Text.Trim()}");

        try
        {
            await ExportHelper.ExportGridAsync(
                this, _grid, rows,
                new ExportHelper.ExportRequest(
                    $"{Loc.T("report.section_report")} - {_category.CategoryLabel}",
                    $"bao-cao-luot-can_{_category.FileSuffix}",
                    filters,
                    _category.CategoryLabel),
                busyControl: _btnExportExcel);
        }
        catch (Exception ex)
        {
            ShowError(Loc.T("export.error"), ex);
        }
    }

    private async Task SearchAsync()
    {
        _btnSearch.Enabled = false;
        try
        {
            var filter = new TripBoardFilter
            {
                FromUtc = ToUtcStartOfDay(_dtpFrom.Value),
                ToUtc = ToUtcStartOfDay(_dtpTo.Value),
                ProfileCode = string.IsNullOrWhiteSpace(_txtProfileCode.Text)
                    ? null : _txtProfileCode.Text.Trim(),
                PlateContains = string.IsNullOrWhiteSpace(_txtPlate.Text)
                    ? null : _txtPlate.Text.Trim(),
                Category = _category.Category
            };

            var rows = await _reports.ListWeighReportAsync(_siteId, filter, _cts.Token);

            GridBuilder.BindAndColorize(_grid, rows, r => r.OverallVerdict?.ToString());
            _lblRowCount.Text = Loc.T("grid.row_count", rows.Count.ToString("N0"));
        }
        catch (OperationCanceledException)
        {
            // Form đang đóng.
        }
        catch (Exception ex)
        {
            ShowError(Loc.T("report.load_error"), ex);
        }
        finally
        {
            _btnSearch.Enabled = true;
        }
    }

    private void ExportCsv()
    {
        if (_grid.DataSource is not List<WeighReportRow> rows || rows.Count == 0)
        {
            MessageBox.Show(Loc.T("report.export_no_data"), Loc.T("common.export_csv"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"bao-cao-luot-can-{DateTime.Now:yyyyMMdd-HHmm}.csv"
        };

        if (dialog.ShowDialog() != DialogResult.OK) return;

        try
        {
            var csv = new StringBuilder();
            csv.AppendLine("TripNo;ReferenceNo;ProfileCode;Plate;GrossKg;TareKg;NetKg;" +
                           "ImpurityPercent;Ccs;Verdict;ManualCount;CompletedAt");

            foreach (var r in rows)
            {
                csv.AppendLine(string.Join(';',
                    r.TripNo, r.ReferenceNo, r.ProfileCode, r.Plate,
                    Num(r.GrossKg), Num(r.TareKg), Num(r.NetKg),
                    Num(r.ImpurityPercent), Num(r.CcsValue),
                    r.OverallVerdict?.ToString(), r.ManualInterventionCount,
                    r.CompletedAt?.ToString("O")));
            }

            // UTF-8 có BOM để Excel đọc đúng tiếng Việt.
            File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(true));

            MessageBox.Show(Loc.T("report.export_ok", rows.Count), Loc.T("common.export_csv"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            ShowError(Loc.T("report.export_error"), ex);
        }
    }

    private void PreviewTicket()
    {
        if (_grid.CurrentRow?.DataBoundItem is not WeighReportRow row)
        {
            MessageBox.Show(Loc.T("report.preview_no_selection"), Loc.T("report.preview_title"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var preview = new TextPreviewForm(
            BuildTicketText(row),
            Loc.T("report.preview_title"),
            Loc.T("report.preview_notice"));

        preview.ShowDialog(this);
    }

    /// <summary>
    /// Bố cục phiếu theo S02 tr.23 và S03 tr.17-19. Mẫu và số bản in cần D-11 duyệt.
    /// Kết quả đang chờ để trống, không in số 0.
    /// </summary>
    private static string BuildTicketText(WeighReportRow row)
    {
        var lines = new List<string>
        {
            Loc.T("report.ticket_title"),
            Loc.T("report.ticket_draft"),
            new('-', 60),
            $"Số chuyến        : {row.TripNo}",
            $"Chứng từ nguồn   : {row.ReferenceNo ?? "—"}",
            $"Hồ sơ nghiệp vụ  : {row.ProfileCode}",
            $"Biển số          : {row.Plate ?? "—"}",
            new('-', 60),
            $"Cân lần 1        : {Fmt(row.GrossKg)} kg",
            $"Cân lần 2        : {Fmt(row.TareKg)} kg",
            $"Net cân xe       : {Fmt(row.NetKg)} kg",
            "                   (chênh lệch cân xe, không mặc định là khối lượng sản phẩm tinh)",
            new('-', 60),
            $"Tạp chất         : {Fmt(row.ImpurityPercent, "N4")} %",
            $"Chữ đường        : {Fmt(row.CcsValue, "N2")}",
            $"Kết luận đối soát: {row.OverallVerdict?.ToString() ?? Loc.T("report.ticket_not_assessed")}",
            $"Can thiệp tay    : {row.ManualInterventionCount} lần",
            new('-', 60)
        };

        if (row.OverallVerdict != ReconciliationVerdict.Pass)
            lines.Add(Loc.T("report.ticket_pending"));

        lines.Add($"In lúc: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Người dùng chọn ngày theo múi giờ nhà máy; bộ lọc dùng UTC.
    /// DateTimePicker trả DateTime Kind = Local nên phải quy đổi bằng offset của chính ngày đó,
    /// không được ghép thẳng với offset zero.
    /// </summary>
    private static DateTimeOffset ToUtcStartOfDay(DateTime localDate)
    {
        var startOfDay = DateTime.SpecifyKind(localDate.Date, DateTimeKind.Unspecified);
        var offset = TimeZoneInfo.Local.GetUtcOffset(startOfDay);

        return new DateTimeOffset(startOfDay, offset).ToUniversalTime();
    }

    private static string Fmt(decimal? value, string format = "N3")
        => value?.ToString(format, CultureInfo.InvariantCulture) ?? "—";

    private static string Num(decimal? value)
        => value?.ToString("F3", CultureInfo.InvariantCulture) ?? string.Empty;

    private static void ShowError(string title, Exception ex)
        => MessageBox.Show($"{title}.{Environment.NewLine}{ex.Message}",
            Loc.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _eventBus.TripChanged -= OnTripChangedFromOtherForm;
        _cts.Cancel();
        base.OnFormClosing(e);
    }

    private async void OnTripChangedFromOtherForm(object? sender, TripChangedEventArgs e)
    {
        if (InvokeRequired) { Invoke(() => OnTripChangedFromOtherForm(sender, e)); return; }
        await SearchAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _cts.Dispose();
        base.Dispose(disposing);
    }
}
