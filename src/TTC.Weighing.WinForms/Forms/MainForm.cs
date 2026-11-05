using Microsoft.Extensions.DependencyInjection;
using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.WinForms.Controls;
using TTC.Weighing.WinForms.Development;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Services;
using TTC.Weighing.WinForms.Theme;
using TTC.Weighing.Domain.Common;

namespace TTC.Weighing.WinForms.Forms;

/// <summary>
/// Shell điều hướng theo 04_Man_hinh — bố cục "Modern Enterprise":
/// <list type="bullet">
/// <item>Header trắng: nút thu gọn menu, logo, tên hệ thống + màn hình hiện tại, ngôn ngữ, nhà máy, người dùng, đăng xuất.</item>
/// <item>Sidebar tối bên trái: chức năng nhóm theo luồng nghiệp vụ, mục đang mở được đánh dấu.</item>
/// <item>Vùng làm việc: mỗi màn hình là một tab có nút đóng; mở lại cùng màn hình thì chuyển sang tab đang có.</item>
/// <item>Status bar: chế độ vận hành, quyết định chưa duyệt, bản quyền.</item>
/// </list>
/// </summary>
public sealed class MainForm : Form
{
    // ── Icon glyph (Segoe Fluent Icons / Segoe MDL2 Assets) ──
    private const string IconHome = "\uE80F";
    private const string IconCar = "\uE804";
    private const string IconScale = "\uEC4A";
    private const string IconFilter = "\uE71C";
    private const string IconLab = "\uE9D9";
    private const string IconDocument = "\uE8A5";
    private const string IconReport = "\uE9F9";
    private const string IconSync = "\uE895";
    private const string IconApiLog = "\uE9D5";
    private const string IconSettings = "\uE713";
    private const string IconPlug = "\uE839"; // Ethernet / kết nối thiết bị
    private const string IconCamera = "\uE714"; // Video / camera IP
    private const string IconCloud = "\uE753";
    private const string IconLibrary = "\uE8F1";
    private const string IconHelp = "\uE897";
    private const string IconCloseAll = "\uE8BB";
    private const string IconMenu = "\uE700";
    private const string IconGlobe = "\uE774";
    private const string IconUser = "\uE77B";
    private const string IconSite = "\uE707";
    private const string IconPower = "\uE7E8";

    private readonly IServiceProvider _services;
    private readonly ICurrentUser _user;
    private readonly ISiteContext _site;
    private readonly PreviewDataStore? _previewStore = null; // Preview đã bỏ

    private string _loggedInUser = "Admin";
    private string? _orgUnitCode;
    private string? _orgUnitName;

    /// <summary>
    /// <c>true</c> khi người dùng chọn đăng xuất — Program.cs dùng để quay lại màn hình login
    /// thay vì thoát ứng dụng.
    /// </summary>
    public bool IsLogout { get; private set; }

    // Tham chiếu để dựng lại khi đổi ngôn ngữ lúc chạy.
    private Panel? _header;
    private NavigationSidebar? _sidebar;
    private Panel? _statusBar;
    private Label? _lblScreenTitle;
    private ChipLabel? _chipUser;
    private ChipLabel? _chipSite;
    private readonly ToolTip _headerTips = new() { AutoPopDelay = 8000 };
    private bool _sidebarCollapsed;

    /// <summary>
    /// Chống chớp: WS_CLIPCHILDREN ngăn vùng cha vẽ đè lên vùng con.
    /// KHÔNG dùng WS_EX_COMPOSITED vì xung đột với OwnerDraw TabControl gây flicker tab strip.
    /// </summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style |= 0x02000000; // WS_CLIPCHILDREN
            return cp;
        }
    }

    /// <summary>Sàn tuyệt đối cho mọi màn hình nghiệp vụ.</summary>
    private static readonly Size AbsoluteMinimumScreenSize = new(820, 480);

    /// <summary>
    /// Kích thước tối thiểu của một màn hình: lớn nhất giữa sàn tuyệt đối, mức form tự khai báo
    /// và mức mà khối bố cục bên trong đòi hỏi.
    /// </summary>
    internal static Size ResolveMinimumSize(Form form)
    {
        var required = AbsoluteMinimumScreenSize;

        required = Max(required, form.MinimumSize);

        foreach (Control child in form.Controls)
            required = Max(required, child.MinimumSize);

        return required;
    }

    private static Size Max(Size a, Size b)
        => new(Math.Max(a.Width, b.Width), Math.Max(a.Height, b.Height));

    private readonly DocumentTabControl _tabs = new();

    /// <summary>Tab đang mở theo kiểu form, để tránh mở trùng màn hình.</summary>
    private readonly Dictionary<Type, TabPage> _openTabs = [];

    /// <summary>DI scope cho mỗi tab — dispose khi tab đóng để tránh DbContext chia sẻ giữa tab.</summary>
    private readonly Dictionary<Type, IServiceScope> _tabScopes = [];

    public MainForm(IServiceProvider services, ICurrentUser user, ISiteContext site)
    {
        _services = services;
        _user = user;
        _site = site;

        Text = Loc.T("app.title");
        if (LogoHelper.GetAppIcon() is { } appIcon) Icon = appIcon;
        // Sàn nhỏ để cửa sổ phóng to luôn nằm gọn trong màn hình (1366x768, hay 1920x1080 đặt Scale 150%
        // = 1280x720). Trước đây 1380x920 làm cửa sổ to hơn màn hình nhỏ → mất thanh trạng thái và chân lưới.
        // Màn hình nghiệp vụ nào cần nhiều chỗ hơn thì tab tự cuộn (xem HostInTab).
        MinimumSize = new Size(1024, 600);
        BackColor = AgrisTheme.Canvas;
        KeyPreview = true;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        // Set kích thước = toàn màn hình ngay từ constructor → form hiện đúng kích thước ngay.
        FormBorderStyle = FormBorderStyle.Sizable;
        var screenArea = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        StartPosition = FormStartPosition.Manual;
        Location = screenArea.Location;
        ClientSize = screenArea.Size;
        WindowState = FormWindowState.Maximized;

        // Màn hình hẹp (dưới 1600 px, kể cả 1920 đặt Scale 125%+): thu gọn menu trái sẵn để vùng tab
        // rộng thêm ~200 px. Người dùng vẫn mở rộng lại được bằng nút thu/mở menu.
        // So theo bề rộng quy về màn hình thiết kế: giao diện đã thu phóng theo UiScale nên màn hình nhỏ
        // trông như 1920; chỉ thu menu khi kể cả sau thu phóng vẫn hẹp (hệ số chạm mức tối thiểu).
        _sidebarCollapsed = screenArea.Width / UiScale.Factor < 1600;

        // Thứ tự thêm quyết định thứ tự dock (control thêm sau được dock trước):
        // status bar (đáy, full width) → header (đỉnh, full width) → sidebar (trái) → vùng tab (fill).
        SuspendLayout();
        BuildWorkspace();
        BuildSidebar();
        BuildHeader();
        BuildStatusBar();
        ResumeLayout(true);

        // Auto-navigate: khi một form hoàn thành bước → tự nhảy sang tab tiếp theo
        var eventBus = services.GetRequiredService<TripEventBus>();
        eventBus.TripChanged += OnTripChangedAutoNavigate;

        // Đổi ngôn ngữ lúc chạy → dựng lại sidebar, header, status bar và các tab.
        Loc.Changed += OnLanguageChanged;
        FormClosed += (_, _) => Loc.Changed -= OnLanguageChanged;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.B))
        {
            ToggleSidebar();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ToggleSidebar()
    {
        if (_sidebar is null) return;
        _sidebar.ToggleCollapsed();
        _sidebarCollapsed = _sidebar.Collapsed;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (InvokeRequired) { Invoke(() => OnLanguageChanged(sender, e)); return; }

        Text = Loc.T("app.title");

        SuspendLayout();

        // Tạo lại tất cả tab đang mở với ngôn ngữ mới — giữ nguyên thứ tự và tab đang chọn.
        ReopenAllTabs();

        Rebuild(ref _sidebar, BuildSidebar);
        Rebuild(ref _header, BuildHeader);
        Rebuild(ref _statusBar, BuildStatusBar);

        ResumeLayout(true);
        SyncNavigationState();
    }

    /// <summary>Dựng lại một vùng chrome mà vẫn giữ đúng vị trí trong thứ tự dock.</summary>
    private void Rebuild<T>(ref T? current, Action build) where T : Control
    {
        var index = current is not null ? Controls.GetChildIndex(current) : -1;

        if (current is not null)
        {
            Controls.Remove(current);
            current.Dispose();
        }

        // build() gán lại chính field mà `current` đang tham chiếu tới.
        build();

        if (index >= 0 && current is not null)
            Controls.SetChildIndex(current, index);
    }

    /// <summary>
    /// Đóng rồi mở lại tất cả tab đang mở — form mới tạo với ngôn ngữ đúng.
    /// Giữ nguyên thứ tự tab và tab đang chọn.
    /// </summary>
    private void ReopenAllTabs()
    {
        var openTypes = _tabs.TabPages.Cast<TabPage>()
            .Select(page => _openTabs.FirstOrDefault(kv => kv.Value == page).Key)
            .Where(t => t is not null)
            .Cast<Type>()
            .ToList();

        var selectedType = _tabs.SelectedTab is not null
            ? _openTabs.FirstOrDefault(kv => kv.Value == _tabs.SelectedTab).Key
            : null;

        CloseAllTabs();

        foreach (var type in openTypes)
            ReopenTabByType(type);

        if (selectedType is not null && _openTabs.TryGetValue(selectedType, out var selectedPage))
            _tabs.SelectedTab = selectedPage;
    }

    /// <summary>Mở lại tab theo Type — dispatch sang OpenTab hoặc OpenDashboard tùy loại form.</summary>
    private void ReopenTabByType(Type formType)
    {
        // Dashboard cần cấu hình đặc biệt (TripSelected event) nên gọi riêng.
        if (formType == typeof(DashboardForm))
        {
            OpenDashboard();
            return;
        }

        CreateAndHost(formType, null, select: false);
    }

    /// <summary>
    /// Tự động mở tab tiếp theo trong luồng nghiệp vụ khi bước trước hoàn thành.
    /// Truyền QR để form đích tự xử lý mà không cần quét lại.
    /// </summary>
    private async void OnTripChangedAutoNavigate(object? sender, TripChangedEventArgs e)
    {
        if (InvokeRequired)
        {
            Invoke(() => OnTripChangedAutoNavigate(sender, e));
            return;
        }

        if (string.IsNullOrWhiteSpace(e.QrContent) && string.IsNullOrWhiteSpace(e.TripNo)) return;

        var qrForNextForm = !string.IsNullOrWhiteSpace(e.QrContent) ? e.QrContent : e.TripNo!;

        try
        {
            switch (e.ChangeType)
            {
                // Cổng vào xong → mở Cân (lần 1)
                case TripChangeType.GateEntryConfirmed:
                    var weigh1 = OpenTab<WeighOperationForm>(null);
                    if (weigh1 is not null)
                        await weigh1.AutoScanQrAsync(qrForNextForm);
                    break;

                // Cân lần 1 xong → mở Tạp chất
                case TripChangeType.FirstWeighCommitted:
                    var quality = OpenTab<QualityForm>(null);
                    if (quality is not null)
                        await quality.AutoScanQrAsync(qrForNextForm);
                    break;

                // Ra kho xong → mở Cân (lần 2)
                case TripChangeType.WarehouseExit:
                    var weigh2 = OpenTab<WeighOperationForm>(null);
                    if (weigh2 is not null)
                        await weigh2.AutoScanQrAsync(qrForNextForm);
                    break;

                // Đăng tài / tạp chất / cân lần 2: không tự chuyển (UI cổng và UI-04 đã bỏ).
                default:
                    break;
            }
        }
        catch
        {
            // Lỗi auto-navigate không crash app — user vẫn có thể thao tác thủ công
        }
    }

    // ═══════════════════════════ Sidebar ═══════════════════════════

    private void BuildSidebar()
    {
        var nav = new NavigationSidebar { AccessibleName = Loc.T("nav.accessible") };

        // Thứ tự theo luồng nghiệp vụ: Tổng quan → Đăng tài → Cân → Tạp chất → CCS.
        nav.AddGroup(Loc.T("menu.operations"));
        nav.AddItem(Loc.T("menu.dashboard"), IconHome, OpenDashboard, typeof(DashboardForm));
        nav.AddItem(Loc.T("menu.trip_registration"), IconCar, Open<TripRegistrationForm>, typeof(TripRegistrationForm));
        nav.AddItem(Loc.T("menu.weighing"), IconScale, Open<WeighOperationForm>, typeof(WeighOperationForm));
        nav.AddItem(Loc.T("menu.quality"), IconFilter, Open<QualityForm>, typeof(QualityForm));
        nav.AddItem(Loc.T("menu.ccs"), IconLab, Open<CcsForm>, typeof(CcsForm));

        nav.AddGroup(Loc.T("menu.lookup"));
        nav.AddItem(Loc.T("menu.trip_detail"), IconDocument, Open<TripDetailForm>, typeof(TripDetailForm));
        nav.AddItem(Loc.T("menu.report"), IconReport, Open<ReportForm>, typeof(ReportForm));

        nav.AddGroup(Loc.T("menu.technical"));
        nav.AddItem(Loc.T("menu.api_log"), IconApiLog, Open<ApiLogForm>, typeof(ApiLogForm));
        nav.AddItem(Loc.T("menu.configuration"), IconSettings, Open<ConfigurationForm>, typeof(ConfigurationForm));
        nav.AddItem(Loc.T("menu.scale_connection"), IconPlug, Open<ScaleConnectionForm>, typeof(ScaleConnectionForm));
        nav.AddItem(Loc.T("menu.camera_connection"), IconCamera, Open<CameraConnectionForm>, typeof(CameraConnectionForm));
        nav.AddItem(Loc.T("menu.frm_data"), IconCloud, Open<FrmSyncForm>, typeof(FrmSyncForm));
        nav.AddItem(Loc.T("menu.erp_data"), IconLibrary, Open<ErpSyncForm>, typeof(ErpSyncForm));

        nav.AddGroup(Loc.T("menu.help"));
        nav.AddItem(Loc.T("menu.close_all_tabs"), IconCloseAll, CloseAllTabs);

        nav.Collapsed = _sidebarCollapsed;

        _sidebar = nav;
        Controls.Add(nav);
    }

    // ═══════════════════════════ Header ═══════════════════════════

    private void BuildHeader()
    {
        var header = new BrandBar { Dock = DockStyle.Top, Height = 68, Padding = new Padding(8, 0, 16, 0) };

        // ── Trái: nút menu + logo + tên hệ thống / màn hình hiện tại ──
        var left = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = Padding.Empty
        };

        var btnToggle = new ModernButton
        {
            Text = AgrisTheme.Icon(14F) is not null ? IconMenu : "≡",
            Font = AgrisTheme.Icon(14F) ?? AgrisTheme.Ui(16F, FontStyle.Bold),
            Size = new Size(44, 44),
            Margin = new Padding(4, 12, 10, 0),
            BackColor = AgrisTheme.Surface,
            ForeColor = AgrisTheme.TextSecondary,
            AccessibleName = Loc.T("nav.toggle")
        };
        btnToggle.FlatAppearance.MouseOverBackColor = AgrisTheme.SurfaceHover;
        btnToggle.Click += (_, _) => ToggleSidebar();
        _headerTips.SetToolTip(btnToggle, Loc.T("nav.toggle"));
        left.Controls.Add(btnToggle);

        if (LogoHelper.GetLogoScaled(44) is { } logo)
        {
            left.Controls.Add(new PictureBox
            {
                Image = logo,
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(Math.Min(logo.Width, 140), 44),
                Margin = new Padding(0, 12, 14, 0),
                BackColor = Color.Transparent,
                AccessibleName = "AgriS"
            });
        }

        left.Controls.Add(new VerticalSeparator { Height = 36, Margin = new Padding(0, 16, 14, 0) });

        var titleBlock = new Panel { Size = new Size(520, 68), Margin = Padding.Empty, BackColor = Color.Transparent };
        var lblApp = new Label
        {
            Text = Loc.T("app.title"),
            Dock = DockStyle.Top,
            Height = 36,
            Font = AgrisTheme.Ui(12.5F, FontStyle.Bold),
            ForeColor = AgrisTheme.TextPrimary,
            TextAlign = ContentAlignment.BottomLeft,
            AutoEllipsis = true,
            BackColor = Color.Transparent
        };
        _lblScreenTitle = new Label
        {
            Text = Loc.T("header.empty_title"),
            Dock = DockStyle.Top,
            Height = 26,
            Font = AgrisTheme.Ui(9.5F),
            ForeColor = AgrisTheme.TextMuted,
            TextAlign = ContentAlignment.TopLeft,
            AutoEllipsis = true,
            BackColor = Color.Transparent
        };
        titleBlock.Controls.Add(_lblScreenTitle);
        titleBlock.Controls.Add(lblApp);
        left.Controls.Add(titleBlock);

        // ── Phải: ngôn ngữ · nhà máy · người dùng · đăng xuất ──
        var right = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent
        };

        var btnLogout = new BrandBarButton(Loc.T("button.logout").Replace("⏻", string.Empty).Trim())
        {
            IconGlyph = IconPower,
            Size = new Size(140, 38),
            Margin = new Padding(6, 15, 0, 0)
        };
        btnLogout.Click += (_, _) =>
        {
            if (Helpers.AppMessageBox.Show(Loc.T("logout.confirm"), Loc.T("button.logout_title"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                IsLogout = true;
                Close();
            }
        };
        right.Controls.Add(btnLogout);

        right.Controls.Add(new VerticalSeparator { Height = 36, Margin = new Padding(10, 16, 4, 0) });

        _chipUser = new ChipLabel
        {
            IconGlyph = IconUser,
            ForeColor = AgrisTheme.BrandGreenDark,
            ChipBackColor = AgrisTheme.BrandGreenSoft,
            ChipBorderColor = Color.FromArgb(190, 228, 205),
            Height = 34,
            Margin = new Padding(6, 17, 0, 0)
        };
        _chipUser.Text = _loggedInUser;
        right.Controls.Add(_chipUser);

        _chipSite = new ChipLabel
        {
            IconGlyph = IconSite,
            ForeColor = AgrisTheme.BrandTealDark,
            ChipBackColor = AgrisTheme.InfoSoft,
            ChipBorderColor = Color.FromArgb(190, 215, 235),
            Height = 34,
            Margin = new Padding(6, 17, 0, 0)
        };
        _chipSite.Text = string.IsNullOrWhiteSpace(_orgUnitCode)
            ? Loc.T("badge.site", _site.CurrentSiteCode)
            : _orgUnitCode;
        if (!string.IsNullOrWhiteSpace(_orgUnitName))
            _headerTips.SetToolTip(_chipSite, _orgUnitName);
        right.Controls.Add(_chipSite);

        right.Controls.Add(BuildLanguageButton());

        if (_previewStore is not null)
            right.Controls.Add(BuildPreviewButton(_previewStore));

        header.Controls.Add(left);
        header.Controls.Add(right);

        _header = header;
        Controls.Add(header);
    }

    /// <summary>Nút đổi ngôn ngữ. Chỉ hiển thị ngôn ngữ mà BU/tài khoản được phép dùng.</summary>
    private Control BuildLanguageButton()
    {
        var button = new ModernButton
        {
            Text = Loc.Language.DisplayName() + "  ▾",
            IconGlyph = IconGlobe,
            Font = new Font(LocalizedFont.Current, 9.5F),
            BackColor = AgrisTheme.Surface,
            ForeColor = AgrisTheme.TextSecondary,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 34),
            MaximumSize = new Size(0, 34),
            Padding = new Padding(8, 0, 8, 0),
            Margin = new Padding(6, 17, 0, 0),
            CornerRadius = 17,
            AccessibleName = Loc.T("menu.language")
        };
        button.FlatAppearance.MouseOverBackColor = AgrisTheme.SurfaceHover;

        _headerTips.SetToolTip(button, Loc.T("menu.language"));

        var menu = new ContextMenuStrip
        {
            Font = AgrisTheme.Ui(10F),
            Renderer = new ToolStripProfessionalRenderer(new AgrisMenuColors()),
            ShowImageMargin = false,
            ShowCheckMargin = true
        };

        var allowed = Loc.AllowedLanguages;
        foreach (AppLanguage lang in Enum.GetValues<AppLanguage>())
        {
            if (allowed is { Count: > 0 } && !allowed.Contains(lang))
                continue;

            var item = new ToolStripMenuItem(lang.DisplayName())
            {
                Checked = lang == Loc.Language,
                Font = new Font(LocalizedFont.FamilyFor(lang), 10F),
                Padding = new Padding(0, 4, 0, 4),
                Tag = lang
            };
            item.Click += (_, _) => Loc.SetLanguage((AppLanguage)item.Tag!);
            menu.Items.Add(item);
        }

        button.Click += (_, _) => menu.Show(button, new Point(0, button.Height + 2));
        button.Disposed += (_, _) => menu.Dispose();

        return button;
    }

    /// <summary>Gắn tên user đã đăng nhập lên header.</summary>
    internal void SetLoggedInUser(string username)
    {
        _loggedInUser = username;
        if (_chipUser is not null)
            _chipUser.Text = username;
    }

    /// <summary>Gắn mã BU (công ty) lên chip nhà máy; tên đầy đủ hiện khi hover.</summary>
    internal void SetOrganizationUnit(string? code, string? name)
    {
        if (string.IsNullOrWhiteSpace(code)) return;

        _orgUnitCode = code;
        _orgUnitName = name;

        if (_chipSite is null) return;

        _chipSite.Text = code;
        if (!string.IsNullOrWhiteSpace(name))
            _headerTips.SetToolTip(_chipSite, name);
    }

    // ═══════════════════════════ Status bar ═══════════════════════════

    private void BuildStatusBar()
    {
        var bar = new StatusBarPanel { Dock = DockStyle.Bottom, Height = 32, Padding = new Padding(16, 0, 16, 0) };

        var isPreview = _previewStore is not null;
        var modeColor = isPreview ? AgrisTheme.Warning : AgrisTheme.Success;

        var mode = new Label
        {
            Text = "●  " + (isPreview ? Loc.T("status.preview_mode") : Loc.T("status.operation_mode")),
            Dock = DockStyle.Left,
            AutoSize = true,
            Font = AgrisTheme.Ui(9F, FontStyle.Bold),
            ForeColor = modeColor,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(0, 8, 0, 0),
            BackColor = Color.Transparent
        };

        var right = new Label
        {
            Text = $"{Loc.T("status.pending_decisions")}     ·     {Loc.T("footer.copyright", VnTime.Now.Year)}",
            Dock = DockStyle.Right,
            AutoSize = true,
            Font = AgrisTheme.Ui(8.5F),
            ForeColor = AgrisTheme.TextMuted,
            TextAlign = ContentAlignment.MiddleRight,
            Padding = new Padding(0, 9, 0, 0),
            BackColor = Color.Transparent
        };

        bar.Controls.Add(mode);
        bar.Controls.Add(right);

        _statusBar = bar;
        Controls.Add(bar);
    }

    /// <summary>Thanh trạng thái: nền trắng, đường phân cách trên.</summary>
    private sealed class StatusBarPanel : Panel
    {
        public StatusBarPanel()
        {
            BackColor = AgrisTheme.Surface;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(AgrisTheme.Border);
            e.Graphics.DrawLine(pen, 0, 0, Width, 0);
        }
    }

    // ═══════════════════════════ Workspace / tabs ═══════════════════════════

    private void BuildWorkspace()
    {
        _tabs.Visible = true;
        _tabs.TabCloseRequested += CloseTab;
        _tabs.SelectedIndexChanged += (_, _) => SyncNavigationState();

        Controls.Add(_tabs);
    }

    /// <summary>Đồng bộ mục sidebar đang sáng và tiêu đề màn hình trên header với tab đang chọn.</summary>
    private void SyncNavigationState()
    {
        if (IsDisposed) return;

        var selected = _tabs.SelectedTab;
        var type = selected is not null
            ? _openTabs.FirstOrDefault(kv => kv.Value == selected).Key
            : null;

        _sidebar?.SetActive(type);

        if (_lblScreenTitle is not null)
            _lblScreenTitle.Text = selected?.Text ?? Loc.T("header.empty_title");
    }

    /// <summary>Mở Tổng quan tự động khi khởi động để người vận hành thấy trạng thái ngay.</summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // Mở Dashboard SAU khi MainForm đã paint xong — tránh giật UI khi khởi động.
        BeginInvoke(OpenDashboard);
    }

    /// <summary>Menu chỉ xuất hiện ở chế độ xem trước, để bật/tắt điều kiện và quan sát hành vi chặn.</summary>
    private Control BuildPreviewButton(PreviewDataStore store)
    {
        var button = new ModernButton
        {
            Text = "Xem trước (dev) ▾",
            Font = AgrisTheme.Ui(9.5F, FontStyle.Bold),
            BackColor = AgrisTheme.WarningSoft,
            ForeColor = AgrisTheme.Warning,
            AutoSize = true,
            MinimumSize = new Size(0, 34),
            Margin = new Padding(6, 17, 0, 0),
            CornerRadius = 17
        };

        var menu = new ContextMenuStrip { Renderer = new ToolStripProfessionalRenderer(new AgrisMenuColors()) };

        menu.Items.Add(Toggle("Đã đo tạp chất (bỏ tick để xem chặn cân lần 2)",
            store.ImpurityMeasured, v => store.ImpurityMeasured = v));
        menu.Items.Add(Toggle("Đã xác thực danh tính xe (bỏ tick để xem chặn theo R-039)",
            store.VehicleIdentityVerified, v => store.VehicleIdentityVerified = v));
        menu.Items.Add(Toggle("Chữ đường đang chờ (chỉ cảnh báo, không chặn cân)",
            store.CcsPending, v => store.CcsPending = v));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Toggle("Giả định hợp đồng API đã xác minh (thực tế D-02 chưa duyệt)",
            store.PretendContractVerified, v => store.PretendContractVerified = v));
        menu.Items.Add(Toggle("Giả định dung sai đã duyệt (thực tế D-06 chưa duyệt)",
            store.PretendToleranceApproved, v => store.PretendToleranceApproved = v));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Toggle("Bộ điều khiển barie sẵn sàng",
            store.BarrierControllerReady, v => store.BarrierControllerReady = v));
        menu.Items.Add(Toggle("Điều kiện an toàn tại chỗ đạt",
            store.BarrierSafetyOk, v => store.BarrierSafetyOk = v));
        menu.Items.Add(new ToolStripSeparator());

        var audit = new ToolStripMenuItem("Xem audit trail toàn hệ thống");
        audit.Click += (_, _) => ShowAuditTrail(store);
        menu.Items.Add(audit);

        button.Click += (_, _) => menu.Show(button, new Point(0, button.Height + 2));
        button.Disposed += (_, _) => menu.Dispose();
        return button;
    }

    private static void ShowAuditTrail(PreviewDataStore store)
    {
        var text = store.AuditLog.Count == 0
            ? "Chưa có bản ghi audit."
            : string.Join(Environment.NewLine, store.AuditLog
                .OrderByDescending(a => a.OccurredAt)
                .Select(a =>
                    $"{a.OccurredAt.ToVnTime():HH:mm:ss}  {a.ActionType,-30} actor={a.Actor}" +
                    $"  trip={a.TripId}  before={a.BeforeJson ?? "-"}  after={a.AfterJson ?? "-"}"));

        using var dialog = new TextPreviewForm(text, "Audit trail (xem trước)");
        dialog.ShowDialog();
    }

    /// <summary>Chỉ dùng cho chế độ chụp ảnh tài liệu (--capture-docs): mở màn hình theo kiểu form.</summary>
    internal void OpenScreenForDocs(Type formType)
    {
        if (formType == typeof(DashboardForm)) { OpenDashboard(); return; }
        if (_openTabs.TryGetValue(formType, out var existing)) { _tabs.SelectedTab = existing; return; }

        CreateAndHost(formType, null, select: true);
    }

    /// <summary>Chỉ dùng cho chế độ chụp ảnh tài liệu: đóng mọi tab giữa hai lần chụp.</summary>
    internal void CloseAllTabsForDocs() => CloseAllTabs();


    /// <summary>Mở màn hình thành tab. Nếu đã mở thì chuyển sang tab đó.</summary>
    private void Open<TForm>() where TForm : Form => OpenTab<TForm>(null);

    /// <summary>UI-01 mở kèm liên kết sang UI-06 khi bấm đôi một chuyến.</summary>
    private void OpenDashboard()
        => OpenTab<DashboardForm>(dashboard =>
        {
            dashboard.TripSelected += async tripId =>
            {
                var detail = OpenTab<TripDetailForm>(null);
                if (detail is not null)
                    await detail.ShowTripAsync(tripId);
            };
        });

    private TForm? OpenTab<TForm>(Action<TForm>? configure) where TForm : Form
    {
        if (_openTabs.TryGetValue(typeof(TForm), out var existing))
        {
            _tabs.SelectedTab = existing;
            return existing.Controls.OfType<TForm>().FirstOrDefault();
        }

        return CreateAndHost(typeof(TForm), f => configure?.Invoke((TForm)f), select: true) as TForm;
    }

    /// <summary>
    /// Tạo màn hình ở kích thước thiết kế, nhúng vào tab, rồi thu phóng cả màn hình theo màn hình máy
    /// (<see cref="UiScale"/>). Mỗi tab tạo DI scope riêng → DbContext riêng, tránh "second operation".
    /// </summary>
    private Form? CreateAndHost(Type formType, Action<Form>? configure, bool select)
    {
        var scope = _services.CreateScope();
        Form? form;

        using (UiScale.DesignScope())
        {
            form = scope.ServiceProvider.GetRequiredService(formType) as Form;
            if (form is null)
            {
                scope.Dispose();
                return null;
            }

            configure?.Invoke(form);
            HostInTab(formType, form, scope, select);
        }

        // Ngoài DesignScope mới co (nếu tab mở ngay trong lúc dựng MainForm thì MainForm co luôn cả tab).
        UiScale.Apply(form);
        return form;
    }

    /// <summary>Nhúng form vào một tab mới: bỏ viền cửa sổ, áp theme, fill toàn trang.</summary>
    private void HostInTab(Type formType, Form form, IServiceScope scope, bool select)
    {
        form.TopLevel = false;
        form.FormBorderStyle = FormBorderStyle.None;
        form.ControlBox = false;
        form.WindowState = FormWindowState.Normal;
        form.Dock = DockStyle.Fill;

        // Kích thước tối thiểu theo nội dung thật. Cửa sổ nhỏ hơn thì tab cuộn, không cắt nội dung.
        form.MinimumSize = ResolveMinimumSize(form);

        // Chống chớp nháy: ẩn form trong lúc apply theme + nhúng vào tab.
        form.Visible = false;

        _tabs.SuspendLayout();
        ThemeApplier.Apply(form);

        var page = new TabPage(form.Text)
        {
            BackColor = AgrisTheme.Canvas,
            AutoScroll = true
        };

        page.Controls.Add(form);

        _openTabs[formType] = page;
        _tabScopes[formType] = scope;
        _tabs.TabPages.Add(page);

        // Form tự đóng thì tab cũng phải biến mất.
        form.FormClosed += (_, _) => RemoveTab(formType, page);

        form.Show();
        _tabs.ResumeLayout(true);

        _tabs.Visible = true;
        if (select)
            _tabs.SelectedTab = page;

        SyncNavigationState();
    }

    private void CloseTab(TabPage page)
    {
        var hosted = page.Controls.OfType<Form>().FirstOrDefault();

        if (hosted is not null)
        {
            // Để form chạy OnFormClosing của nó: dừng timer, cancel token.
            hosted.Close();
            return;
        }

        RemoveTab(null, page);
    }

    private void CloseAllTabs()
    {
        foreach (var page in _tabs.TabPages.Cast<TabPage>().ToList())
            CloseTab(page);
    }

    private void RemoveTab(Type? formType, TabPage page)
    {
        formType ??= _openTabs.FirstOrDefault(kv => kv.Value == page).Key;

        if (formType is not null)
        {
            _openTabs.Remove(formType);

            // Dispose DI scope cho tab → giải phóng DbContext và các Scoped service.
            if (_tabScopes.Remove(formType, out var scope))
                scope.Dispose();
        }

        if (_tabs.TabPages.Contains(page))
            _tabs.TabPages.Remove(page);

        page.Dispose();
        SyncNavigationState();
    }

    private static ToolStripMenuItem Toggle(string text, bool initial, Action<bool> onChanged)
    {
        var item = new ToolStripMenuItem(text) { CheckOnClick = true, Checked = initial };
        item.CheckedChanged += (_, _) => onChanged(item.Checked);
        return item;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _headerTips.Dispose();

        base.Dispose(disposing);
    }
}

/// <summary>Bảng màu menu/popup theo design system.</summary>
internal sealed class AgrisMenuColors : ProfessionalColorTable
{
    public override Color MenuItemSelected => AgrisTheme.SurfaceHover;
    public override Color MenuItemSelectedGradientBegin => AgrisTheme.SurfaceHover;
    public override Color MenuItemSelectedGradientEnd => AgrisTheme.SurfaceHover;
    public override Color MenuItemBorder => AgrisTheme.Border;
    public override Color MenuBorder => AgrisTheme.BorderStrong;
    public override Color MenuItemPressedGradientBegin => AgrisTheme.Surface;
    public override Color MenuItemPressedGradientEnd => AgrisTheme.Surface;
    public override Color ToolStripDropDownBackground => AgrisTheme.Surface;
    public override Color ImageMarginGradientBegin => AgrisTheme.Surface;
    public override Color ImageMarginGradientMiddle => AgrisTheme.Surface;
    public override Color ImageMarginGradientEnd => AgrisTheme.Surface;
    public override Color CheckBackground => AgrisTheme.PrimarySoft;
    public override Color CheckSelectedBackground => AgrisTheme.PrimarySoft;
    public override Color SeparatorDark => AgrisTheme.Border;
}
