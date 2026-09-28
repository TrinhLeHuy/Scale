using Microsoft.Extensions.DependencyInjection;
using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.WinForms.Controls;
using TTC.Weighing.WinForms.Development;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Services;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Forms;

/// <summary>
/// Shell điều hướng theo 04_Man_hinh. Mỗi màn hình mở thành một tab có nút đóng;
/// mở lại cùng màn hình thì chuyển sang tab đang có thay vì tạo phiên trùng.
/// </summary>
public sealed class MainForm : Form
{
    private readonly IServiceProvider _services;
    private readonly ICurrentUser _user;
    private readonly ISiteContext _site;
    private readonly PreviewDataStore? _previewStore = null; // Preview đã bỏ

    private Label? _lblUserDisplay;
    private string _loggedInUser = "Admin";

    /// <summary>
    /// <c>true</c> khi người dùng chọn đăng xuất — Program.cs dùng để quay lại màn hình login
    /// thay vì thoát ứng dụng.
    /// </summary>
    public bool IsLogout { get; private set; }

    // Tham chiếu để dựng lại khi đổi ngôn ngữ lúc chạy.
    private MenuStrip? _menu;
    private Panel? _menuSeparator;
    private BrandBar? _brandBar;
    private BrandBar? _footer;
    private StatusStrip? _statusBar;
    private Label? _lblBrandDesc;
    private BrandBarButton? _btnLogout;

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
        MinimumSize = new Size(1200, 820);
        BackColor = AgrisTheme.Canvas;
        KeyPreview = true;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        // Không dùng WindowState = Maximized (gây chớp nháy do Windows resize 2 lần).
        // Set kích thước = toàn màn hình ngay từ constructor → form hiện đúng kích thước ngay.
        FormBorderStyle = FormBorderStyle.Sizable;
        var screenArea = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        StartPosition = FormStartPosition.Manual;
        Location = screenArea.Location;
        ClientSize = screenArea.Size;
        WindowState = FormWindowState.Maximized;

        // Thứ tự thêm control quyết định thứ tự dock: vùng Fill phải vào trước
        // các dải Top/Bottom, nếu không dải tab sẽ bị menu che.
        SuspendLayout();
        BuildWorkspace();
        BuildFooter();
        BuildStatusBar();
        BuildBrandBar();
        BuildMenu();
        ResumeLayout(true);

        // Auto-navigate: khi một form hoàn thành bước → tự nhảy sang tab tiếp theo
        var eventBus = services.GetRequiredService<TripEventBus>();
        eventBus.TripChanged += OnTripChangedAutoNavigate;

        // Đổi ngôn ngữ lúc chạy → dựng lại menu, brand bar, footer, status bar.
        Loc.Changed += OnLanguageChanged;
        FormClosed += (_, _) => Loc.Changed -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (InvokeRequired) { Invoke(() => OnLanguageChanged(sender, e)); return; }

        Text = Loc.T("app.title");

        SuspendLayout();

        // Tạo lại tất cả tab đang mở với ngôn ngữ mới — giữ nguyên thứ tự và tab đang chọn.
        ReopenAllTabs();

        RebuildBrandBar();
        RebuildFooter();
        RebuildStatusBar();
        RebuildMenu();
        ResumeLayout(true);
    }

    /// <summary>
    /// Đóng rồi mở lại tất cả tab đang mở — form mới tạo với ngôn ngữ đúng.
    /// Giữ nguyên thứ tự tab và tab đang chọn.
    /// </summary>
    private void ReopenAllTabs()
    {
        // Ghi nhớ danh sách tab đang mở theo thứ tự và tab đang chọn.
        var openTypes = _tabs.TabPages.Cast<TabPage>()
            .Select(page => _openTabs.FirstOrDefault(kv => kv.Value == page).Key)
            .Where(t => t is not null)
            .Cast<Type>()
            .ToList();

        var selectedType = _tabs.SelectedTab is not null
            ? _openTabs.FirstOrDefault(kv => kv.Value == _tabs.SelectedTab).Key
            : null;

        // Đóng tất cả tab (cleanup timers, tokens).
        CloseAllTabs();

        // Mở lại theo thứ tự cũ.
        foreach (var type in openTypes)
        {
            // Dùng reflection vì OpenTab<T> là generic — gọi qua method non-generic.
            ReopenTabByType(type);
        }

        // Khôi phục tab đang chọn.
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

        // Mỗi tab tạo scope riêng → DbContext riêng.
        var scope = _services.CreateScope();
        if (scope.ServiceProvider.GetRequiredService(formType) is not Form form)
        {
            scope.Dispose();
            return;
        }

        form.TopLevel = false;
        form.FormBorderStyle = FormBorderStyle.None;
        form.ControlBox = false;
        form.WindowState = FormWindowState.Normal;
        form.Dock = DockStyle.Fill;
        form.MinimumSize = ResolveMinimumSize(form);

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

        form.FormClosed += (_, _) => RemoveTab(formType, page);
        form.Show();
        _tabs.ResumeLayout(true);
    }

    private void RebuildMenu()
    {
        // Giữ đúng thứ tự dock: ghi lại child index rồi khôi phục sau khi dựng lại.
        var menuIndex = _menu is not null ? Controls.GetChildIndex(_menu) : -1;
        var sepIndex = _menuSeparator is not null ? Controls.GetChildIndex(_menuSeparator) : -1;

        if (_menu is not null) { Controls.Remove(_menu); _menu.Dispose(); }
        if (_menuSeparator is not null) { Controls.Remove(_menuSeparator); _menuSeparator.Dispose(); }

        BuildMenu();

        if (menuIndex >= 0 && _menu is not null) Controls.SetChildIndex(_menu, menuIndex);
        if (sepIndex >= 0 && _menuSeparator is not null) Controls.SetChildIndex(_menuSeparator, sepIndex);
    }

    private void RebuildBrandBar()
    {
        var index = _brandBar is not null ? Controls.GetChildIndex(_brandBar) : -1;
        if (_brandBar is not null) { Controls.Remove(_brandBar); _brandBar.Dispose(); }
        BuildBrandBar();
        if (index >= 0 && _brandBar is not null) Controls.SetChildIndex(_brandBar, index);
    }

    private void RebuildFooter()
    {
        var index = _footer is not null ? Controls.GetChildIndex(_footer) : -1;
        if (_footer is not null) { Controls.Remove(_footer); _footer.Dispose(); }
        BuildFooter();
        if (index >= 0 && _footer is not null) Controls.SetChildIndex(_footer, index);
    }

    private void RebuildStatusBar()
    {
        var index = _statusBar is not null ? Controls.GetChildIndex(_statusBar) : -1;
        if (_statusBar is not null) { Controls.Remove(_statusBar); _statusBar.Dispose(); }
        BuildStatusBar();
        if (index >= 0 && _statusBar is not null) Controls.SetChildIndex(_statusBar, index);
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
                // Đăng tài xong → mở Cổng (vào)
                case TripChangeType.Registered:
                    break;
                    break;

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

                // Tạp chất xong → mở Kho vận
                case TripChangeType.ImpurityRecorded:
                    break; // UI-04 đã bỏ
                    break;

                // Ra kho xong → mở Cân (lần 2)
                case TripChangeType.WarehouseExit:
                    var weigh2 = OpenTab<WeighOperationForm>(null);
                    if (weigh2 is not null)
                        await weigh2.AutoScanQrAsync(qrForNextForm);
                    break;

                // Cân lần 2 xong → mở Cổng (ra)
                case TripChangeType.SecondWeighCommitted:
                    break;
                    break;
            }
        }
        catch
        {
            // Lỗi auto-navigate không crash app — user vẫn có thể thao tác thủ công
        }
    }

    private void BuildMenu()
    {
        var menu = new MenuStrip
        {
            BackColor = AgrisTheme.Surface,
            Font = new Font(LocalizedFont.Current, 9.5F),
            Padding = new Padding(6, 2, 0, 2),
            Renderer = new ToolStripProfessionalRenderer(new AgrisMenuColors())
        };

        // Thứ tự theo luồng nghiệp vụ từ trên xuống dưới:
        // Tổng quan → Đăng tài → Cổng vào → Cân → Tạp chất → Phiên kho/đối soát → Cổng ra
        var opsMenu = new ToolStripMenuItem(Loc.T("menu.operations"));
        opsMenu.DropDownItems.Add(Screen(Loc.T("menu.dashboard"), OpenDashboard));
        opsMenu.DropDownItems.Add(new ToolStripSeparator());
        opsMenu.DropDownItems.Add(Screen(Loc.T("menu.trip_registration"), Open<TripRegistrationForm>));
        opsMenu.DropDownItems.Add(Screen(Loc.T("menu.weighing"), Open<WeighOperationForm>));
        opsMenu.DropDownItems.Add(Screen(Loc.T("menu.quality"), Open<QualityForm>));

        var mgmtMenu = new ToolStripMenuItem(Loc.T("menu.lookup"));
        mgmtMenu.DropDownItems.Add(Screen(Loc.T("menu.trip_detail"), Open<TripDetailForm>));
        mgmtMenu.DropDownItems.Add(Screen(Loc.T("menu.exception_queue"), Open<ExceptionQueueForm>));
        mgmtMenu.DropDownItems.Add(Screen(Loc.T("menu.report"), Open<ReportForm>));

        var itMenu = new ToolStripMenuItem(Loc.T("menu.technical"));
        itMenu.DropDownItems.Add(Screen(Loc.T("menu.integration_monitor"), Open<IntegrationMonitorForm>));
        itMenu.DropDownItems.Add(Screen(Loc.T("menu.configuration"), Open<ConfigurationForm>));
        itMenu.DropDownItems.Add(new ToolStripSeparator());
        itMenu.DropDownItems.Add(Screen("Quản lý dữ liệu FRM", Open<FrmSyncForm>));

        var tabMenu = new ToolStripMenuItem(Loc.T("menu.tab"));
        tabMenu.DropDownItems.Add(Screen(Loc.T("menu.guide"), OpenGuide));
        tabMenu.DropDownItems.Add(new ToolStripSeparator());
        tabMenu.DropDownItems.Add(Screen(Loc.T("menu.close_current_tab"), CloseCurrentTab));
        tabMenu.DropDownItems.Add(Screen(Loc.T("menu.close_all_tabs"), CloseAllTabs));

        menu.Items.AddRange([opsMenu, mgmtMenu, itMenu, tabMenu, BuildLanguageMenu()]);

        if (_previewStore is not null)
            menu.Items.Add(BuildPreviewMenu(_previewStore));

        MainMenuStrip = menu;
        _menu = menu;
        Controls.Add(menu);

        // Đường kẻ ngang phân tách menu với brand bar
        _menuSeparator = new Panel
        {
            Dock = DockStyle.Top,
            Height = 1,
            BackColor = AgrisTheme.Border
        };
        Controls.Add(_menuSeparator);
    }

    /// <summary>Menu đổi ngôn ngữ. Chỉ hiển thị ngôn ngữ mà BU/tài khoản được phép dùng.</summary>
    private ToolStripMenuItem BuildLanguageMenu()
    {
        var langMenu = new ToolStripMenuItem(Loc.T("menu.language"));
        var allowed = Loc.AllowedLanguages;

        foreach (AppLanguage lang in Enum.GetValues<AppLanguage>())
        {
            // Nếu có danh sách allowed (sau đăng nhập), chỉ hiện ngôn ngữ được phép
            if (allowed is { Count: > 0 } && !allowed.Contains(lang))
                continue;

            var item = new ToolStripMenuItem(lang.DisplayName())
            {
                Checked = lang == Loc.Language,
                CheckOnClick = false,
                Tag = lang
            };
            item.Click += (_, _) => Loc.SetLanguage((AppLanguage)item.Tag!);
            langMenu.DropDownItems.Add(item);
        }

        return langMenu;
    }

    /// <summary>Gắn tên user đã đăng nhập lên brand bar.</summary>
    internal void SetLoggedInUser(string username)
    {
        _loggedInUser = username;
        if (_lblUserDisplay is not null)
            _lblUserDisplay.Text = $"  {username}  ";
    }

    /// <summary>Gắn tên BU (công ty) lên badge nhà máy trên brand bar.</summary>
    internal void SetOrganizationUnit(string? code, string? name)
    {
        if (_lblSite is null || string.IsNullOrWhiteSpace(code)) return;

        // Hiện mã code ngắn (KSK, ATN...) trên badge — tên đầy đủ hiện khi hover tooltip.
        _lblSite.Text = $"  {code}  ";

        if (!string.IsNullOrWhiteSpace(name))
        {
            var tip = new ToolTip { AutoPopDelay = 8000 };
            tip.SetToolTip(_lblSite, name);
        }
    }

    private Label? _lblSite;

    private void BuildBrandBar()
    {
        var bar = new BrandBar { Dock = DockStyle.Top, Height = 72 };
        _brandBar = bar;

        // ── Trái: Logo + mô tả ──
        var leftPanel = new Panel { Dock = DockStyle.Left, Width = 480, BackColor = Color.Transparent, Padding = Padding.Empty };

        var logoImg = LogoHelper.GetLogoScaled(80);
        var textX = 10; // fallback khi không có logo

        if (logoImg is not null)
        {
            var logoPic = new PictureBox
            {
                Image = logoImg,
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(80, 80),
                Location = new Point(8, -4),
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            leftPanel.Controls.Add(logoPic);
            textX = 8 + 80 + 12;
        }

        var lblDesc = new Label
        {
            Text = Loc.T("app.title"),
            Location = new Point(textX, 0),
            Size = new Size(360, 72),
            Font = new Font(LocalizedFont.Current, 13F, FontStyle.Bold),
            ForeColor = AgrisTheme.BrandTealDark,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _lblBrandDesc = lblDesc;
        leftPanel.Controls.Add(lblDesc);

        // ── Phải: User + Nhà máy + Đăng xuất ──
        var rightPanel = new Panel { Dock = DockStyle.Right, Width = 420, BackColor = Color.Transparent };

        // Nút đăng xuất — tông đỏ cam nhẹ, nổi bật
        var btnLogout = new BrandBarButton(Loc.T("button.logout"))
        {
            Size = new Size(118, 34),
            Location = new Point(rightPanel.Width - 118 - 14, 19)
        };
        btnLogout.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnLogout.Click += (_, _) =>
        {
            if (MessageBox.Show(Loc.T("logout.confirm"), Loc.T("button.logout_title"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                IsLogout = true;
                Close();
            }
        };
        _btnLogout = btnLogout;
        rightPanel.Controls.Add(btnLogout);

        // Separator trước nút logout
        var sep2 = new VerticalSeparator { Anchor = AnchorStyles.Top | AnchorStyles.Right };
        sep2.Location = new Point(btnLogout.Left - sep2.Width - 6, 20);
        rightPanel.Controls.Add(sep2);

        // Badge nhà máy
        _lblSite = new Label
        {
            Text = $"  {Loc.T("badge.site", _site.CurrentSiteCode)}  ",
            AutoSize = true,
            Font = new Font(AgrisTheme.FontFamily, 9.5F, FontStyle.Bold),
            ForeColor = AgrisTheme.BrandTealDark,
            BackColor = Color.FromArgb(233, 242, 249),
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = new Padding(10, 5, 10, 5),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        _lblSite.Location = new Point(sep2.Left - _lblSite.PreferredWidth - 6, 21);
        rightPanel.Controls.Add(_lblSite);

        // Separator giữa nhà máy và user
        var sep1 = new VerticalSeparator { Anchor = AnchorStyles.Top | AnchorStyles.Right };
        sep1.Location = new Point(_lblSite.Left - sep1.Width - 6, 20);
        rightPanel.Controls.Add(sep1);

        // Badge user
        _lblUserDisplay = new Label
        {
            Text = $"  {_loggedInUser}  ",
            AutoSize = true,
            Font = new Font(AgrisTheme.FontFamily, 9.5F, FontStyle.Bold),
            ForeColor = AgrisTheme.BrandGreenDark,
            BackColor = Color.FromArgb(233, 246, 238),
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = new Padding(10, 5, 10, 5),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        _lblUserDisplay.Location = new Point(
            sep1.Left - _lblUserDisplay.PreferredWidth - 6, 21);
        rightPanel.Controls.Add(_lblUserDisplay);

        // Icon user trước tên
        var lblUserIcon = new Label
        {
            Text = "👤",
            AutoSize = true,
            Font = new Font(AgrisTheme.FontFamily, 11F),
            ForeColor = AgrisTheme.BrandGreenDark,
            BackColor = Color.Transparent,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        lblUserIcon.Location = new Point(_lblUserDisplay.Left - lblUserIcon.PreferredWidth - 4, 22);
        rightPanel.Controls.Add(lblUserIcon);

        bar.Controls.Add(leftPanel);
        bar.Controls.Add(rightPanel);

        Controls.Add(bar);
    }

    private void BuildWorkspace()
    {
        _tabs.Visible = true;
        _tabs.TabCloseRequested += CloseTab;

        Controls.Add(_tabs);
    }

    /// <summary>Mở Tổng quan tự động khi khởi động để người vận hành thấy trạng thái ngay.</summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // Dùng BeginInvoke để mở Dashboard SAU khi MainForm đã paint xong,
        // tránh giật UI khi khởi động do DI resolve + ThemeApplier + query DB
        // chạy đồng bộ trên UI thread trước khi form hiển thị.
        BeginInvoke(OpenDashboard);
    }

    /// <summary>Chân trang thương hiệu.</summary>
    private void BuildFooter()
    {
        var footer = new BrandBar { Dock = DockStyle.Bottom, Height = 26 };
        _footer = footer;

        footer.Controls.Add(new Label
        {
            Text = Loc.T("footer.copyright", DateTime.Now.Year),
            Dock = DockStyle.Fill,
            Font = new Font(LocalizedFont.Current, 8.5F),
            ForeColor = AgrisTheme.TextOnBrand,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent
        });

        Controls.Add(footer);
    }

    /// <summary>Menu chỉ xuất hiện ở chế độ xem trước, để bật/tắt điều kiện và quan sát hành vi chặn.</summary>
    private ToolStripMenuItem BuildPreviewMenu(PreviewDataStore store)
    {
        var previewMenu = new ToolStripMenuItem("&Xem trước (dev)")
        {
            ForeColor = AgrisTheme.Warning
        };

        previewMenu.DropDownItems.Add(Toggle(
            "Đã đo tạp chất (bỏ tick để xem chặn cân lần 2)",
            store.ImpurityMeasured, v => store.ImpurityMeasured = v));

        previewMenu.DropDownItems.Add(Toggle(
            "Đã xác thực danh tính xe (bỏ tick để xem chặn theo R-039)",
            store.VehicleIdentityVerified, v => store.VehicleIdentityVerified = v));

        previewMenu.DropDownItems.Add(Toggle(
            "Chữ đường đang chờ (chỉ cảnh báo, không chặn cân)",
            store.CcsPending, v => store.CcsPending = v));

        previewMenu.DropDownItems.Add(new ToolStripSeparator());

        previewMenu.DropDownItems.Add(Toggle(
            "Giả định hợp đồng API đã xác minh (thực tế D-02 chưa duyệt)",
            store.PretendContractVerified, v => store.PretendContractVerified = v));

        previewMenu.DropDownItems.Add(Toggle(
            "Giả định dung sai đã duyệt (thực tế D-06 chưa duyệt)",
            store.PretendToleranceApproved, v => store.PretendToleranceApproved = v));

        previewMenu.DropDownItems.Add(new ToolStripSeparator());

        previewMenu.DropDownItems.Add(Toggle(
            "Bộ điều khiển barie sẵn sàng",
            store.BarrierControllerReady, v => store.BarrierControllerReady = v));

        previewMenu.DropDownItems.Add(Toggle(
            "Điều kiện an toàn tại chỗ đạt",
            store.BarrierSafetyOk, v => store.BarrierSafetyOk = v));

        previewMenu.DropDownItems.Add(new ToolStripSeparator());

        var audit = new ToolStripMenuItem("Xem audit trail toàn hệ thống");
        audit.Click += (_, _) => ShowAuditTrail(store);
        previewMenu.DropDownItems.Add(audit);

        return previewMenu;
    }

    private static void ShowAuditTrail(PreviewDataStore store)
    {
        var text = store.AuditLog.Count == 0
            ? "Chưa có bản ghi audit."
            : string.Join(Environment.NewLine, store.AuditLog
                .OrderByDescending(a => a.OccurredAt)
                .Select(a =>
                    $"{a.OccurredAt.ToLocalTime():HH:mm:ss}  {a.ActionType,-30} actor={a.Actor}" +
                    $"  trip={a.TripId}  before={a.BeforeJson ?? "-"}  after={a.AfterJson ?? "-"}"));

        using var dialog = new TextPreviewForm(text, "Audit trail (xem trước)");
        dialog.ShowDialog();
    }

    private void BuildStatusBar()
    {
        var status = new StatusStrip
        {
            BackColor = AgrisTheme.Surface,
            Font = new Font(LocalizedFont.Current, 8.5F)
        };
        _statusBar = status;

        status.Items.Add(new ToolStripStatusLabel(_previewStore is not null
            ? Loc.T("status.preview_mode")
            : Loc.T("status.operation_mode"))
        {
            ForeColor = _previewStore is not null ? AgrisTheme.Warning : AgrisTheme.Success,
            Font = new Font(LocalizedFont.Current, 9F, FontStyle.Bold)
        });

        status.Items.Add(new ToolStripStatusLabel { Spring = true });
        status.Items.Add(new ToolStripStatusLabel(Loc.T("status.pending_decisions"))
        {
            ForeColor = AgrisTheme.TextMuted
        });

        Controls.Add(status);
    }

    /// <summary>Mở tab hướng dẫn sử dụng. Nội dung đọc từ docs/HUONG_DAN_SU_DUNG.md nếu có.</summary>
    private void OpenGuide() => OpenTab<GuideForm>(null);

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

        // Mỗi tab tạo scope riêng → DbContext riêng, tránh "second operation" khi async song song.
        var scope = _services.CreateScope();
        var form = scope.ServiceProvider.GetRequiredService<TForm>();
        configure?.Invoke(form);

        // Nhúng form vào tab: bỏ viền cửa sổ và cho fill toàn bộ trang.
        form.TopLevel = false;
        form.FormBorderStyle = FormBorderStyle.None;
        form.ControlBox = false;
        form.WindowState = FormWindowState.Normal;
        form.Dock = DockStyle.Fill;

        // Kích thước tối thiểu lấy theo nội dung thật của màn hình. Cửa sổ nhỏ hơn thì tab cuộn,
        // không co nội dung tới mức bị cắt.
        form.MinimumSize = ResolveMinimumSize(form);

        // Chống chớp nháy: ẩn form trong lúc apply theme + nhúng vào tab,
        // chỉ hiện khi mọi thứ layout xong.
        form.Visible = false;

        _tabs.SuspendLayout();
        ThemeApplier.Apply(form);

        var page = new TabPage(form.Text)
        {
            BackColor = AgrisTheme.Canvas,
            AutoScroll = true
        };

        page.Controls.Add(form);

        _openTabs[typeof(TForm)] = page;
        _tabScopes[typeof(TForm)] = scope;
        _tabs.TabPages.Add(page);

        // Form tự đóng thì tab cũng phải biến mất.
        form.FormClosed += (_, _) => RemoveTab(typeof(TForm), page);

        form.Show();
        _tabs.ResumeLayout(true);

        _tabs.Visible = true;
        _tabs.SelectedTab = page;

        return form;
    }

    private void CloseCurrentTab()
    {
        if (_tabs.SelectedTab is not null)
            CloseTab(_tabs.SelectedTab);
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
    }

    private static ToolStripMenuItem Screen(string text, Action onClick)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => onClick();
        return item;
    }

    private static ToolStripMenuItem Toggle(string text, bool initial, Action<bool> onChanged)
    {
        var item = new ToolStripMenuItem(text) { CheckOnClick = true, Checked = initial };
        item.CheckedChanged += (_, _) => onChanged(item.Checked);
        return item;
    }

    private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, radius, radius, 180, 90);
        path.AddArc(bounds.Right - radius, bounds.Y, radius, radius, 270, 90);
        path.AddArc(bounds.Right - radius, bounds.Bottom - radius, radius, radius, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - radius, radius, radius, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>Bảng màu menu theo nhận diện Agris.</summary>
internal sealed class AgrisMenuColors : ProfessionalColorTable
{
    public override Color MenuItemSelected => AgrisTheme.SuccessSoft;
    public override Color MenuItemSelectedGradientBegin => AgrisTheme.SuccessSoft;
    public override Color MenuItemSelectedGradientEnd => AgrisTheme.SuccessSoft;
    public override Color MenuItemBorder => AgrisTheme.BrandGreen;
    public override Color MenuBorder => AgrisTheme.Border;
    public override Color MenuItemPressedGradientBegin => AgrisTheme.Surface;
    public override Color MenuItemPressedGradientEnd => AgrisTheme.Surface;
    public override Color ToolStripDropDownBackground => AgrisTheme.Surface;
    public override Color ImageMarginGradientBegin => AgrisTheme.Surface;
    public override Color ImageMarginGradientMiddle => AgrisTheme.Surface;
    public override Color ImageMarginGradientEnd => AgrisTheme.Surface;
}
