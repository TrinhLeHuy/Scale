using TTC.Weighing.Infrastructure.Adapters;
using TTC.Weighing.WinForms.Controls;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Forms;

/// <summary>
/// Form kỹ thuật — quản lý dữ liệu FRM API cache.
/// Hiện trạng thái cache, nút refresh thủ công, thống kê entities đã kéo.
/// </summary>
public sealed class FrmSyncForm : Form
{
    private readonly FrmMasterDataCache _cache;

    private readonly Label _lblStatus = new()
    {
        Dock = DockStyle.Top,
        Height = 60,
        Font = AgrisTheme.Body(11F),
        ForeColor = AgrisTheme.TextPrimary,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(12)
    };

    private readonly Button _btnRefresh = GridBuilder.PrimaryButton("🔄 Kéo dữ liệu FRM mới nhất", new Size(280, 40));
    private readonly RichTextBox _rtbLog = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        Font = AgrisTheme.Mono(9F),
        BackColor = AgrisTheme.SurfaceMuted,
        BorderStyle = BorderStyle.None
    };

    private readonly DataGridView _dgvStats = GridBuilder.ReadOnlyGrid("Thống kê entities");

    public FrmSyncForm(FrmMasterDataCache cache)
    {
        _cache = cache;

        Text = "Quản lý dữ liệu FRM API";
        MinimumSize = new Size(700, 500);
        BackColor = AgrisTheme.Canvas;
        DoubleBuffered = true;

        BuildLayout();
        UpdateStatus();

        _btnRefresh.Click += async (_, _) => await RefreshAsync();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));   // Status
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));   // Button
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 40));    // Stats grid
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 60));    // Log

        root.Controls.Add(_lblStatus, 0, 0);

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        btnPanel.Controls.Add(_btnRefresh);
        root.Controls.Add(btnPanel, 0, 1);

        // Stats grid
        _dgvStats.Dock = DockStyle.Fill;
        _dgvStats.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "Thông tin", Name = "Key", Width = 250 },
            new DataGridViewTextBoxColumn { HeaderText = "Giá trị", Name = "Value", Width = 300 });
        root.Controls.Add(_dgvStats, 0, 2);

        // Log
        var logCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(1) };
        var logHeader = GridBuilder.SectionHeader("Nhật ký đồng bộ");
        logCard.Controls.Add(_rtbLog);
        logCard.Controls.Add(logHeader);
        root.Controls.Add(logCard, 0, 3);

        Controls.Add(root);
    }

    private void UpdateStatus()
    {
        var loading = _cache.IsLoading ? "⏳ Đang tải..." : "✅ Sẵn sàng";
        var refreshed = _cache.LastRefreshedAt == DateTimeOffset.MinValue
            ? "Chưa tải lần nào"
            : $"Lần cuối: {_cache.LastRefreshedAt.ToLocalTime():dd/MM/yyyy HH:mm:ss}";

        _lblStatus.Text = $"{loading}  |  Tổng xe: {_cache.VehicleCount}  |  {refreshed}";

        // Stats grid
        _dgvStats.Rows.Clear();
        var allVehicles = _cache.AllVehicles;
        var withCard = allVehicles.Count(v => v.MaThe is not null);
        var withHdvc = allVehicles.Count(v => v.DoiTacVanChuyenName is not null);
        var withTram = allVehicles.Count(v => v.TramName is not null);
        var withVuDt = allVehicles.Count(v => v.VuDauTuName is not null);
        var uniquePlates = allVehicles.Select(v => v.BienSo).Distinct().Count();

        _dgvStats.Rows.Add("Tổng records trong cache", _cache.VehicleCount.ToString("N0"));
        _dgvStats.Rows.Add("Biển số unique", uniquePlates.ToString("N0"));
        _dgvStats.Rows.Add("Có thẻ xe (the-xe)", withCard.ToString("N0"));
        _dgvStats.Rows.Add("Chỉ xe-van-chuyen (không thẻ)", (_cache.VehicleCount - withCard).ToString("N0"));
        _dgvStats.Rows.Add("Có đối tác vận chuyển (HDVC)", withHdvc.ToString("N0"));
        _dgvStats.Rows.Add("Có trạm", withTram.ToString("N0"));
        _dgvStats.Rows.Add("Có vụ đầu tư", withVuDt.ToString("N0"));
        _dgvStats.Rows.Add("API Base URL", "digiscale-uat.agris.com.vn");
        _dgvStats.Rows.Add("Entities kéo", "the-xe, xe-van-chuyen, hop-dong-van-chuyen");
        _dgvStats.Rows.Add("Đang tải?", _cache.IsLoading ? "Có" : "Không");
    }

    private async Task RefreshAsync()
    {
        _btnRefresh.Enabled = false;
        _btnRefresh.Text = "⏳ Đang kéo dữ liệu...";
        AppendLog("Bắt đầu kéo dữ liệu FRM API...");

        try
        {
            var before = _cache.VehicleCount;
            await _cache.RefreshAsync();
            var after = _cache.VehicleCount;

            AppendLog($"✅ Cache xe: Trước: {before}, Sau: {after} records.");

            // Kiểm tra từng entity + từng BU
            var entities = new[] { "brix", "tram", "thua-dat", "xe-van-chuyen", "vu-dau-tu", "giong-mia", "vung-dia-ly", "hop-dong-van-chuyen", "the-xe" };
            // TODO: Thêm "lenh-don" khi server DigiScale mirror entity LenhDon từ FRM.
            //       Hiện tại server trả 400 UNKNOWN_ENTITY cho cả "lenh-don" lẫn "LenhDon".
            var bus = new[] { "TTCS", "ATN", "KSK", "NHS", "PRS", "TTCA", "TTCBH" };

            foreach (var bu in bus)
            {
                AppendLog($"── BU: {bu} ──");
                foreach (var ent in entities)
                {
                    try
                    {
                        var (count, errorDetail) = await _cache.FetchEntityCountDetailAsync(ent, bu);
                        if (count >= 0)
                        {
                            var icon = count > 0 ? "✅" : "❌";
                            AppendLog($"  {icon} {ent}: {count}");
                        }
                        else
                        {
                            AppendLog($"  ❌ {ent}: FAILED — {errorDetail}");
                        }
                    }
                    catch (Exception ex) { AppendLog($"  ⚠ {ent}: lỗi — {ex.Message}"); }
                }
            }

            UpdateStatus();
        }
        catch (Exception ex)
        {
            AppendLog($"❌ Lỗi: {ex.Message}");
        }
        finally
        {
            _btnRefresh.Text = "🔄 Kéo dữ liệu FRM mới nhất";
            _btnRefresh.Enabled = true;
        }
    }

    private void AppendLog(string message)
    {
        if (_rtbLog.InvokeRequired)
        {
            _rtbLog.Invoke(() => AppendLog(message));
            return;
        }
        _rtbLog.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
        _rtbLog.ScrollToCaret();
    }
}
