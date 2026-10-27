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

    private readonly Button _btnRefresh = GridBuilder.PrimaryButton(Loc.T("frm.btn_pull"), new Size(280, 40));
    private readonly Button _btnAutoSync = GridBuilder.SecondaryButton(Loc.T("frm.btn_auto_on"), new Size(160, 40));
    private readonly NumericUpDown _numInterval = new()
    {
        Minimum = 1, Maximum = 120, Value = 5, Width = 60,
        Font = AgrisTheme.Body(10F), AccessibleName = Loc.T("frm.interval_accessible")
    };
    private readonly System.Windows.Forms.Timer _autoTimer = new();
    private bool _autoEnabled;
    private readonly TextBox _txtEntities = new()
    {
        Font = AgrisTheme.Mono(9F),
        Text = "brix,tram,thua-dat,xe-van-chuyen,vu-dau-tu,giong-mia,vung-dia-ly,hop-dong-van-chuyen,the-xe,LenhDon",
        AccessibleName = Loc.T("frm.entities_accessible")
    };
    private readonly TextBox _txtBUs = new()
    {
        Font = AgrisTheme.Mono(9F),
        Text = Program.ActiveOrganizationUnitCode ?? "TTCS",
        ReadOnly = true,
        BackColor = AgrisTheme.ReadOnlyBg,
        ForeColor = AgrisTheme.ReadOnlyText,
        AccessibleName = Loc.T("frm.bu_list_accessible")
    };
    // Log hiển thị câu đã dịch (Lào/Khmer) → dùng font UI theo ngôn ngữ, không dùng Consolas (thiếu glyph).
    private readonly RichTextBox _rtbLog = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        Font = AgrisTheme.Body(9F),
        BackColor = AgrisTheme.SurfaceMuted,
        BorderStyle = BorderStyle.None
    };

    private readonly DataGridView _dgvStats = GridBuilder.ReadOnlyGrid(Loc.T("frm.stats_grid"));

    // ── Bảng Lệnh đốn (mới nhất trước): tìm kiếm, lọc theo cột, phân trang — control dùng chung ──
    private readonly DataExplorerGrid<FrmLenhDonInfo> _explorer;
    private List<FrmLenhDonInfo> _allData = [];

    public FrmSyncForm(FrmMasterDataCache cache)
    {
        _cache = cache;
        RebuildCardIndex();
        _explorer = new DataExplorerGrid<FrmLenhDonInfo>(
            Loc.T("frm.section_data", _cache.ActiveBusinessUnit ?? "FRM"), "frm.lenh_don", BuildColumns(),
            Loc.T("frm.search_placeholder"), Loc.T("frm.empty"));

        Text = Loc.T("frm.title");
        MinimumSize = new Size(700, 500);
        BackColor = AgrisTheme.Canvas;
        DoubleBuffered = true;

        BuildLayout();
        UpdateStatus();

        // Load LenhDon từ cache (nếu đã có từ DB)
        _allData = _cache.AllLenhDon.OrderByDescending(ld => ld.NgayCap).ToList();

        // BU đã filter từ DB/API — không cần auto-filter cột BU trên grid
        _explorer.SetData(_allData);

        _btnRefresh.Click += async (_, _) => await RefreshAsync();
        _autoTimer.Tick += async (_, _) => await RefreshAsync();

        // Reload data khi form load (có thể cache đã update từ background)
        Load += (_, _) => ReloadFromCache();
    }

    /// <summary>Reload dữ liệu từ cache vào grid (gọi khi form load hoặc cache đã refresh).</summary>
    private void ReloadFromCache()
    {
        _allData = _cache.AllLenhDon.OrderByDescending(ld => ld.NgayCap).ToList();
        RebuildCardIndex();
        _explorer.SetData(_allData, keepPage: true);
        UpdateStatus();
    }

    private void ToggleAutoSync()
    {
        _autoEnabled = !_autoEnabled;
        if (_autoEnabled)
        {
            _autoTimer.Interval = (int)_numInterval.Value * 60_000;
            _autoTimer.Start();
            _btnAutoSync.Text = Loc.T("frm.btn_auto_off");
            _btnAutoSync.BackColor = AgrisTheme.Warning;
            _numInterval.Enabled = false;
            AppendLog(Loc.T("frm.auto_started", _numInterval.Value));
        }
        else
        {
            _autoTimer.Stop();
            _btnAutoSync.Text = Loc.T("frm.btn_auto_on");
            _btnAutoSync.BackColor = AgrisTheme.Surface;
            _numInterval.Enabled = true;
            AppendLog(Loc.T("frm.auto_stopped"));
        }
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(12)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));   // Status
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));   // Entities config
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));   // Button
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 65));    // Data grid
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 35));    // Log

        root.Controls.Add(_lblStatus, 0, 0);

        // Config: Entities + BU (editable)
        var configPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = Padding.Empty
        };
        configPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        configPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        configPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        configPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        configPanel.Controls.Add(new Label { Text = "Entities:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = AgrisTheme.Body(9F) }, 0, 0);
        _txtEntities.Dock = DockStyle.Fill;
        configPanel.Controls.Add(_txtEntities, 1, 0);

        configPanel.Controls.Add(new Label { Text = "BU:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = AgrisTheme.Body(9F) }, 0, 1);
        _txtBUs.Dock = DockStyle.Fill;
        configPanel.Controls.Add(_txtBUs, 1, 1);

        root.Controls.Add(configPanel, 0, 1);

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        btnPanel.Controls.Add(_btnRefresh);
        btnPanel.Controls.Add(new Label { Text = "  ", AutoSize = true, Margin = new Padding(4, 12, 0, 0) });
        btnPanel.Controls.Add(_btnAutoSync);
        btnPanel.Controls.Add(new Label { Text = Loc.T("frm.every"), AutoSize = true, Margin = new Padding(8, 12, 4, 0), Font = AgrisTheme.Body(9F) });
        btnPanel.Controls.Add(_numInterval);
        btnPanel.Controls.Add(new Label { Text = Loc.T("frm.minutes"), AutoSize = true, Margin = new Padding(4, 12, 0, 0), Font = AgrisTheme.Body(9F) });
        root.Controls.Add(btnPanel, 0, 2);

        // Bảng Lệnh đốn (tiêu đề + tìm kiếm + lọc cột + phân trang)
        _explorer.Margin = new Padding(0, 0, 0, 10);
        root.Controls.Add(_explorer, 0, 3);

        // Log
        var logCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(1) };
        var logHeader = GridBuilder.SectionHeader(Loc.T("frm.section_log"));
        logCard.Controls.Add(_rtbLog);
        logCard.Controls.Add(logHeader);
        root.Controls.Add(logCard, 0, 4);

        _btnAutoSync.Click += (_, _) => ToggleAutoSync();

        Controls.Add(root);
    }

    private void UpdateStatus()
    {
        var loading = _cache.IsLoading ? "⏳ Đang tải..." : "✅ Sẵn sàng";
        var refreshed = _cache.LastRefreshedAt == DateTimeOffset.MinValue
            ? "Chưa tải lần nào"
            : $"Lần cuối: {_cache.LastRefreshedAt.ToLocalTime():dd/MM/yyyy HH:mm:ss}";

        var allVehicles = _cache.AllVehicles;
        var withCard = allVehicles.Count(v => v.MaThe is not null);
        var uniquePlates = allVehicles.Select(v => v.BienSo).Distinct().Count();

        _lblStatus.Text = $"{loading}  |  Xe: {_cache.VehicleCount} ({uniquePlates} biển số, {withCard} có thẻ)  |  Lệnh đốn: {_cache.AllLenhDon.Count}  |  {refreshed}";
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

            // Load LenhDon data vào grid
            _allData = _cache.AllLenhDon.OrderByDescending(ld => ld.NgayCap).ToList();
            RebuildCardIndex();
            _explorer.SetData(_allData);

            AppendLog($"✅ Cache xe: Trước: {before}, Sau: {after} records. LenhDon: {_allData.Count}.");

            // Kiểm tra từng entity + từng BU (đọc từ textbox — có thể chỉnh sửa trên giao diện)
            var entities = _txtEntities.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var bus = _txtBUs.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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
    // ═══════════════════════════════════════════════════════════
    //  Cột bảng Lệnh đốn — một nguồn duy nhất cho tiêu đề, giá trị, lọc, sắp xếp
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Thứ tự theo cách tra cứu: định danh + trạng thái + <b>tình trạng đăng tài</b> + ngày cấp
    /// → thẻ xe / hợp đồng vận chuyển (điều kiện DigiScale DT-03, DT-04) → xe, chủ mía → mía → trạm/vụ
    /// → xe/ưu tiên → cờ thẻ tạm / mía cháy / thu hoạch máy → hợp đồng đầu tư → BU.
    /// </summary>
    private ExplorerColumn<FrmLenhDonInfo>[] BuildColumns() =>
    [
        new(() => Loc.T("frm.col_ma_ld"), ld => ld.MaLD, 150, ExplorerColumnKind.Code),
        new(() => Loc.T("frm.col_status"), ld => ld.StatusCodeName, 130, ExplorerColumnKind.Badge, StatusTone),
        new(() => Loc.T("frm.col_ready"), Readiness, 200, ExplorerColumnKind.Badge, ReadinessTone),
        new(() => Loc.T("frm.col_ngay_cap"), ld => ld.NgayCap, 145, ExplorerColumnKind.Date),
        // ── Thẻ xe + hợp đồng vận chuyển: lệnh đốn → Thẻ xe → Hợp đồng vận chuyển ──
        new(() => Loc.T("frm.col_the_xe"), ld => ld.Thexename, 230, ExplorerColumnKind.Code),
        new(() => Loc.T("frm.col_ma_the"), ld => CardOf(ld)?.MaThe, 120, ExplorerColumnKind.Code),
        new(() => Loc.T("frm.col_card_status"), CardStatus, 150, ExplorerColumnKind.Badge, CardStatusTone),
        new(() => Loc.T("frm.col_transport_contract"), TransportContract, 170, ExplorerColumnKind.Code),
        new(() => Loc.T("frm.col_transporter"), ld => CardOf(ld)?.DoiTacVanChuyenName, 200),
        new(() => Loc.T("field.plate"), ld => ld.BienSoXe, 120),
        new(() => Loc.T("field.sugarcane_owner"), ld => ld.KHName, 230),
        new(() => Loc.T("field.sugarcane_type"), ld => ld.LoaiMiaName, 115),
        new(() => Loc.T("field.sugarcane_variety"), ld => ld.GiongMiaName, 115),
        new(() => Loc.T("frm.col_tram"), ld => ld.TramName, 150),
        new(() => Loc.T("frm.col_vu_dau_tu"), ld => ld.VudautuName, 130),
        new(() => Loc.T("frm.col_loai_xe"), ld => ld.LoaixeName, 105),
        new(() => Loc.T("frm.col_uu_tien"), ld => ld.UuTien, 95),
        new(() => Loc.T("frm.col_so_xe_tam"), ld => ld.SoXeTam, 115),
        new(() => Loc.T("frm.col_the_xe_tam"), ld => YesNo(ld.TheXeTam), 110, ExplorerColumnKind.Badge, FlagTone(BadgeTone.Info)),
        new(() => Loc.T("frm.col_mia_chay"), ld => YesNo(ld.MiaChay), 105, ExplorerColumnKind.Badge, FlagTone(BadgeTone.Warning)),
        new(() => Loc.T("frm.col_loai_mia_chay"), ld => ld.LoaiMiaChayName, 130),
        new(() => Loc.T("frm.col_thu_hoach_may"), ld => YesNo(ld.ThuHoachMay), 125, ExplorerColumnKind.Badge, FlagTone(BadgeTone.Brand)),
        new(() => Loc.T("frm.col_loai_hop_dong"), ld => ld.LoaiHopDongName, 180),
        new(() => "BU", ld => ld.BusinessUnit, 90)
    ];

    // ═══════════════════════════════════════════════════════════
    //  Thẻ xe của lệnh đốn (tra danh mục thẻ FRM đã kéo về)
    // ═══════════════════════════════════════════════════════════

    /// <summary>Thẻ xe FRM theo mã thẻ — dựng lại mỗi lần nạp dữ liệu.</summary>
    private Dictionary<string, FrmVehicleInfo> _cardsByCode = new(StringComparer.OrdinalIgnoreCase);

    private void RebuildCardIndex()
    {
        var index = new Dictionary<string, FrmVehicleInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in _cache.AllVehicles)
            if (!string.IsNullOrWhiteSpace(v.MaThe))
                index.TryAdd(v.MaThe.Trim(), v);
        _cardsByCode = index;
    }

    /// <summary>
    /// Thẻ xe của lệnh đốn. Tên thẻ trên lệnh đốn dạng "{HĐ vận chuyển}-{xe}-{mã thẻ}"
    /// (ví dụ "2026VC0006-70R-020.10-T260000047") → khớp theo mã thẻ ở cuối. Null nếu chưa có trên FRM.
    /// </summary>
    private FrmVehicleInfo? CardOf(FrmLenhDonInfo ld)
    {
        var name = ld.Thexename?.Trim();
        if (string.IsNullOrEmpty(name)) return null;
        if (_cardsByCode.TryGetValue(name, out var exact)) return exact;
        var last = name[(name.LastIndexOf('-') + 1)..];
        return _cardsByCode.TryGetValue(last, out var byCode) ? byCode : null;
    }

    /// <summary>Hợp đồng vận chuyển: theo thẻ FRM; chưa có thẻ thì lấy mã HĐ ở đầu tên thẻ lệnh đốn.</summary>
    private string? TransportContract(FrmLenhDonInfo ld)
    {
        var card = CardOf(ld);
        return Blank(card?.HopDongVanChuyenName) ?? Blank(card?.MaHopDong)
            ?? Presenters.TripRegistrationPresenter.TransportContractOf(ld.Thexename);
    }

    /// <summary>Trạng thái thẻ xe: thẻ tạm / trạng thái FRM / chưa có trên FRM (chưa phát hành, còn Nháp).</summary>
    private string? CardStatus(FrmLenhDonInfo ld)
    {
        if (ld.IsTemporaryCard) return Loc.T("frm.card_temp");
        if (string.IsNullOrWhiteSpace(ld.Thexename)) return null;
        return Blank(CardOf(ld)?.StatusName) ?? Loc.T("frm.card_missing");
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static BadgeTone CardStatusTone(string status)
    {
        if (status == Loc.T("frm.card_missing")) return BadgeTone.Warning;
        if (status == Loc.T("frm.card_temp")) return BadgeTone.Info;
        var s = status.ToUpperInvariant();
        if (s.Contains("HỦY") || s.Contains("HUỶ") || s.Contains("CANCEL") || s.Contains("HẾT HẠN") || s.Contains("NHÁP"))
            return BadgeTone.Danger;
        return BadgeTone.Success;
    }

    /// <summary>
    /// Tình trạng đăng tài dự kiến — cùng điều kiện app + DigiScale kiểm tra khi bấm Đăng tài:
    /// lệnh đốn Đã duyệt; thẻ xe tạm (DigiScale chưa nhận); thẻ chưa có trên FRM (DT-03/DT-04); thẻ đã hủy.
    /// </summary>
    private string Readiness(FrmLenhDonInfo ld)
    {
        if (!Presenters.TripRegistrationPresenter.IsApproved(ld)) return Loc.T("frm.ready_not_approved");
        if (ld.IsTemporaryCard) return Loc.T("frm.ready_temp");
        if (string.IsNullOrWhiteSpace(ld.Thexename)) return Loc.T("frm.ready_ok");

        var card = CardOf(ld);
        if (card is null) return Loc.T("frm.ready_card_missing");
        return CardStatusTone(card.StatusName ?? "") == BadgeTone.Danger
            ? Loc.T("frm.ready_card_cancelled")
            : Loc.T("frm.ready_ok");
    }

    private static BadgeTone ReadinessTone(string value)
        => value == Loc.T("frm.ready_ok") ? BadgeTone.Success
            : value == Loc.T("frm.ready_not_approved") ? BadgeTone.Neutral
            : value == Loc.T("frm.ready_temp") ? BadgeTone.Info
            : BadgeTone.Danger;

    /// <summary>Trạng thái lệnh đốn: Đã duyệt xanh, Nháp vàng, Hủy đỏ, đã tính tiền / khác xanh dương.</summary>
    private static BadgeTone StatusTone(string status)
    {
        var s = status.ToUpperInvariant();
        if (s.Contains("HỦY") || s.Contains("HUỶ") || s.Contains("CANCEL")) return BadgeTone.Danger;
        if (s.Contains("NHÁP") || s.Contains("DRAFT")) return BadgeTone.Warning;
        if (s.Contains("ĐÃ DUYỆT") || s.Contains("APPROVED")) return BadgeTone.Success;
        return BadgeTone.Info;
    }

    /// <summary>Cờ Có/Không: "Có" tô màu nhấn, "Không" trung tính.</summary>
    private static Func<string, BadgeTone> FlagTone(BadgeTone yesTone)
        => value => value == Loc.T("frm.yes") ? yesTone : BadgeTone.Neutral;

    /// <summary>Có / Không cho cờ FRM; null (dữ liệu cũ chưa làm mới FRM) → để trống.</summary>
    private static string? YesNo(bool? value) => value switch
    {
        true => Loc.T("frm.yes"),
        false => Loc.T("frm.no"),
        null => null
    };

    private void AppendLog(string message)
    {
        // Kéo dữ liệu / timer tự động có thể xong sau khi tab đã đóng → log đã dispose, bỏ qua.
        if (IsDisposed || _rtbLog.IsDisposed) return;
        if (_rtbLog.InvokeRequired)
        {
            try { _rtbLog.BeginInvoke(() => AppendLog(message)); }
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException) { }
            return;
        }
        _rtbLog.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
        _rtbLog.ScrollToCaret();
    }
}
