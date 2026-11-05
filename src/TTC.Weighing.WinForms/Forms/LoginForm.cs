using System.Drawing.Drawing2D;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Services;
using TTC.Weighing.WinForms.Theme;
using TTC.Weighing.Domain.Common;

namespace TTC.Weighing.WinForms.Forms;

/// <summary>Mục ngôn ngữ trong combo box, hiển thị tên bản địa của ngôn ngữ.</summary>
internal sealed record LanguageItem(AppLanguage Language)
{
    public override string ToString() => Language.DisplayName();
}

/// <summary>
/// Màn hình đăng nhập toàn màn hình, bố cục chia đôi:
/// trái là vùng thương hiệu (nền tối, logo, thông điệp), phải là biểu mẫu đăng nhập nền trắng.
/// Văn bản song ngữ Việt/Anh cố định vì lúc này chưa biết BU → chưa biết ngôn ngữ được phép.
/// </summary>
public sealed class LoginForm : Form
{
    /// <summary>Giảm chớp nháy khi khởi động.</summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style |= 0x02000000; // WS_CLIPCHILDREN
            return cp;
        }
    }

    private const string SignInText = "ĐĂNG NHẬP / SIGN IN";

    private readonly TextBox _txtUser;
    private readonly TextBox _txtPass;
    private readonly Button _btnLogin;
    private readonly Label _lblError;
    private readonly string? _apiBaseUrl;
    private readonly string? _requiredTenantId;

    private readonly BrandPanel _brand;
    private readonly Panel _formHost;
    private readonly Panel _formCard;
    private readonly Button _btnClose;

    private bool _ready;

    public string? AuthenticatedUser { get; private set; }

    /// <summary>Kết quả đăng nhập API (token, permissions, ...). Null nếu chưa login hoặc lỗi.</summary>
    public LoginResult? LoginResult { get; private set; }

    public LoginForm(string? apiBaseUrl = null, string? requiredTenantId = null)
    {
        _apiBaseUrl = apiBaseUrl;
        _requiredTenantId = requiredTenantId;
        Text = "Đăng nhập / Sign In";
        if (LogoHelper.GetAppIcon() is { } appIcon) Icon = appIcon;
        FormBorderStyle = FormBorderStyle.None;
        DoubleBuffered = true;
        KeyPreview = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        // Kích thước = toàn màn hình ngay từ constructor (tránh chớp do maximize hai lần).
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        StartPosition = FormStartPosition.Manual;
        Location = screen.Location;
        ClientSize = screen.Size;
        BackColor = AgrisTheme.Surface;
        Opacity = 0; // Ẩn cho đến khi layout xong — tránh chớp trắng

        SuspendLayout();

        // ── Trái: vùng thương hiệu ──
        _brand = new BrandPanel { Dock = DockStyle.Left, Width = (int)(ClientSize.Width * 0.46) };

        // ── Phải: biểu mẫu ──
        _formHost = new Panel { Dock = DockStyle.Fill, BackColor = AgrisTheme.Surface };

        _formCard = new Panel { Size = new Size(440, 560), BackColor = AgrisTheme.Surface };

        var lblTitle = new Label
        {
            Text = "Đăng nhập",
            Dock = DockStyle.Top, Height = 52,
            Font = new Font(AgrisTheme.FontFamily, 22F, FontStyle.Bold),
            ForeColor = AgrisTheme.TextPrimary,
            TextAlign = ContentAlignment.BottomLeft
        };

        var lblTitleEn = new Label
        {
            Text = "Sign in to continue",
            Dock = DockStyle.Top, Height = 28,
            Font = new Font(AgrisTheme.FontFamily, 11F),
            ForeColor = AgrisTheme.TextMuted,
            TextAlign = ContentAlignment.TopLeft
        };

        var lblSub = new Label
        {
            Text = "Nhập thông tin tài khoản DigiScale / Enter your DigiScale credentials",
            Dock = DockStyle.Top, Height = 44,
            Font = new Font(AgrisTheme.FontFamily, 9.5F),
            ForeColor = AgrisTheme.TextMuted,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var userBox = new InputBox("\uE77B", "Nhập tên đăng nhập / Enter username");
        _txtUser = userBox.TextBox;
        _txtUser.TabIndex = 0;
        _txtUser.AccessibleName = "Tên đăng nhập / Username";

        var passBox = new InputBox("\uE72E", "••••••••");
        _txtPass = passBox.TextBox;
        _txtPass.UseSystemPasswordChar = true;
        _txtPass.TabIndex = 1;
        _txtPass.AccessibleName = "Mật khẩu / Password";

        _btnLogin = new ModernButton
        {
            Text = SignInText,
            Dock = DockStyle.Top, Height = 50,
            BackColor = AgrisTheme.Primary,
            ForeColor = AgrisTheme.TextOnBrand,
            Font = new Font(AgrisTheme.FontFamily, 11F, FontStyle.Bold),
            TabIndex = 2,
            CornerRadius = AgrisTheme.RadiusMd,
            AccessibleName = SignInText
        };
        _btnLogin.FlatAppearance.MouseOverBackColor = AgrisTheme.PrimaryHover;
        _btnLogin.Click += OnLogin;

        _lblError = new Label
        {
            Text = string.Empty,
            Dock = DockStyle.Top, Height = 48,
            Font = new Font(AgrisTheme.FontFamily, 9.5F),
            ForeColor = AgrisTheme.Danger,
            TextAlign = ContentAlignment.MiddleLeft,
            TabStop = false,
            AccessibleRole = AccessibleRole.Alert
        };

        var lblHelp = new Label
        {
            Text = "Quên mật khẩu? Liên hệ quản trị hệ thống / Forgot password? Contact your administrator",
            Dock = DockStyle.Top, Height = 40,
            Font = new Font(AgrisTheme.FontFamily, 8.5F),
            ForeColor = AgrisTheme.TextMuted,
            TextAlign = ContentAlignment.MiddleLeft
        };

        // Thứ tự Add ngược với thứ tự hiển thị (Dock = Top).
        _formCard.Controls.Add(lblHelp);
        _formCard.Controls.Add(_lblError);
        _formCard.Controls.Add(_btnLogin);
        _formCard.Controls.Add(Spacer(24));
        _formCard.Controls.Add(passBox);
        _formCard.Controls.Add(FieldLabel("MẬT KHẨU / PASSWORD"));
        _formCard.Controls.Add(Spacer(16));
        _formCard.Controls.Add(userBox);
        _formCard.Controls.Add(FieldLabel("TÊN ĐĂNG NHẬP / USERNAME"));
        _formCard.Controls.Add(Spacer(20));
        _formCard.Controls.Add(lblSub);
        _formCard.Controls.Add(lblTitleEn);
        _formCard.Controls.Add(lblTitle);

        _formHost.Controls.Add(_formCard);

        var lblFoot = new Label
        {
            Text = $"© {VnTime.Now.Year} AgriS · Weigh Smarter, Drive Further",
            Dock = DockStyle.Bottom, Height = 40,
            Font = new Font(AgrisTheme.FontFamily, 8.5F),
            ForeColor = AgrisTheme.TextMuted,
            TextAlign = ContentAlignment.MiddleCenter
        };
        _formHost.Controls.Add(lblFoot);

        _btnClose = new ModernButton
        {
            Text = AgrisTheme.Icon(11F) is not null ? "\uE8BB" : "✕",
            Font = AgrisTheme.Icon(11F) ?? new Font(AgrisTheme.FontFamily, 12F),
            Size = new Size(44, 36),
            BackColor = AgrisTheme.Surface,
            ForeColor = AgrisTheme.TextMuted,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            TabStop = false,
            AccessibleName = "Đóng / Close"
        };
        _btnClose.FlatAppearance.MouseOverBackColor = AgrisTheme.DangerSoft;
        _btnClose.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        _formHost.Controls.Add(_btnClose);

        Controls.Add(_formHost);
        Controls.Add(_brand);

        ResumeLayout(false);

        Resize += (_, _) => { if (_ready) DoLayout(); };
        _formHost.Resize += (_, _) => { if (_ready) DoLayout(); };

        // Shown fire SAU khi form đã hiển thị: layout một lần rồi mới hiện (Opacity = 1).
        Shown += (_, _) =>
        {
            DoLayout();
            _ready = true;
            Invalidate(true);
            Update();
            Opacity = 1;
            _txtUser.Focus();
        };

        AcceptButton = _btnLogin;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
    }

    private void DoLayout()
    {
        // Màn hình hẹp: thu vùng thương hiệu để biểu mẫu luôn đủ chỗ.
        _brand.Width = ClientSize.Width < UiScale.Px(1100) ? 0 : (int)(ClientSize.Width * 0.46);
        _brand.Visible = _brand.Width > 0;

        _formCard.Location = new Point(
            Math.Max(24, (_formHost.ClientSize.Width - _formCard.Width) / 2),
            Math.Max(40, (_formHost.ClientSize.Height - _formCard.Height) / 2 - 20));

        _btnClose.Location = new Point(_formHost.ClientSize.Width - _btnClose.Width - 12, 12);
    }

    private async void OnLogin(object? sender, EventArgs e)
    {
        _lblError.Text = string.Empty;
        var u = _txtUser.Text.Trim();
        var p = _txtPass.Text;
        if (string.IsNullOrWhiteSpace(u)) { _lblError.Text = "Vui lòng nhập tên đăng nhập / Please enter username"; _txtUser.Focus(); return; }
        if (string.IsNullOrWhiteSpace(p)) { _lblError.Text = "Vui lòng nhập mật khẩu / Please enter password"; _txtPass.Focus(); return; }

        // Gọi API DigiScale nếu có cấu hình base URL
        if (!string.IsNullOrWhiteSpace(_apiBaseUrl))
        {
            SetBusy(true);
            try
            {
                using var authService = new DigiScaleAuthService(_apiBaseUrl);
                var result = await authService.LoginAsync(u, p);

                if (result is null)
                {
                    _lblError.Text = "❌ Sai tên đăng nhập hoặc mật khẩu / Incorrect credentials";
                    _txtPass.SelectAll();
                    _txtPass.Focus();
                    return;
                }

                // Kiểm tra user có thuộc đúng tenant được cấu hình không
                if (!string.IsNullOrWhiteSpace(_requiredTenantId)
                    && !string.Equals(result.TenantId, _requiredTenantId, StringComparison.OrdinalIgnoreCase))
                {
                    _lblError.Text = "❌ Tài khoản không thuộc nhà máy / Account not in this plant";
                    _txtUser.Focus();
                    return;
                }

                AuthenticatedUser = result.DisplayName;
                LoginResult = result;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (HttpRequestException ex)
            {
                _lblError.Text = $"❌ Lỗi kết nối / Connection error: {ex.Message}";
            }
            catch (TaskCanceledException)
            {
                _lblError.Text = "❌ Hết thời gian chờ / Connection timed out";
            }
            catch (Exception ex)
            {
                _lblError.Text = $"❌ Lỗi / Error: {ex.Message}";
            }
            finally
            {
                if (!IsDisposed)
                    SetBusy(false);
            }
            return;
        }

        // Fallback: đăng nhập cục bộ khi chưa cấu hình API (dev mode)
        if (u.Equals("Admin", StringComparison.OrdinalIgnoreCase) && p == "Admin@123")
        { AuthenticatedUser = u; DialogResult = DialogResult.OK; Close(); return; }
        _lblError.Text = "❌ Sai tên đăng nhập hoặc mật khẩu / Incorrect credentials";
        _txtPass.SelectAll(); _txtPass.Focus();
    }

    private void SetBusy(bool busy)
    {
        _btnLogin.Enabled = !busy;
        _btnLogin.Text = busy ? "Đang đăng nhập... / Signing in..." : SignInText;
        _txtUser.Enabled = !busy;
        _txtPass.Enabled = !busy;
        UseWaitCursor = busy;
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Top, Height = 26,
        Font = new Font(AgrisTheme.FontFamily, 8.5F, FontStyle.Bold),
        ForeColor = AgrisTheme.TextSecondary,
        TextAlign = ContentAlignment.MiddleLeft,
        TabStop = false
    };

    private static Panel Spacer(int h) => new() { Dock = DockStyle.Top, Height = h, TabStop = false };

    /// <summary>
    /// Ô nhập hiện đại: viền bo góc, icon bên trái, viền chuyển màu thương hiệu khi focus.
    /// </summary>
    private sealed class InputBox : Panel
    {
        private readonly string _glyph;

        public InputBox(string glyph, string placeholder)
        {
            _glyph = glyph;
            Dock = DockStyle.Top;
            Height = 50;
            BackColor = AgrisTheme.Surface;

            var hasIcon = AgrisTheme.Icon(12F) is not null;
            Padding = new Padding(hasIcon ? 46 : 14, 13, 14, 8);

            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);

            TextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                Font = new Font(AgrisTheme.FontFamily, 12F),
                BackColor = AgrisTheme.Surface,
                ForeColor = AgrisTheme.TextPrimary,
                PlaceholderText = placeholder
            };
            TextBox.GotFocus += (_, _) => Invalidate();
            TextBox.LostFocus += (_, _) => Invalidate();
            TextBox.EnabledChanged += (_, _) =>
            {
                TextBox.BackColor = TextBox.Enabled ? AgrisTheme.Surface : AgrisTheme.SurfaceMuted;
                BackColor = TextBox.BackColor;
                Invalidate();
            };

            Controls.Add(TextBox);
            Cursor = Cursors.IBeam;
            Click += (_, _) => TextBox.Focus();
        }

        public TextBox TextBox { get; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Shapes.EffectiveBackColor(Parent));
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var focused = TextBox.Focused;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);

            using var path = Shapes.RoundedRect(rect, AgrisTheme.RadiusMd);
            using var fill = new SolidBrush(BackColor);
            g.FillPath(fill, path);

            using var pen = new Pen(focused ? AgrisTheme.Primary : AgrisTheme.BorderStrong, focused ? 2f : 1f);
            g.DrawPath(pen, path);

            if (AgrisTheme.Icon(12F) is { } iconFont)
            {
                TextRenderer.DrawText(g, _glyph, iconFont, new Rectangle(14, 0, 22, Height),
                    focused ? AgrisTheme.Primary : AgrisTheme.TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    /// <summary>Vùng thương hiệu: nền gradient tối, hoạ tiết vòng tròn mờ, logo trên thẻ trắng, thông điệp.</summary>
    private sealed class BrandPanel : Panel
    {
        private Bitmap? _cache;

        public BrandPanel()
        {
            BackColor = AgrisTheme.NavBack;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        protected override void OnResize(EventArgs e)
        {
            _cache?.Dispose();
            _cache = null;
            base.OnResize(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width <= 0 || Height <= 0) return;

            _cache ??= Render();
            e.Graphics.DrawImageUnscaled(_cache, 0, 0);
        }

        private Bitmap Render()
        {
            var bmp = new Bitmap(Width, Height);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var rect = new Rectangle(0, 0, Width, Height);
            using (var bg = new LinearGradientBrush(rect, AgrisTheme.NavBack, AgrisTheme.BrandTealDark, 60f))
            {
                var blend = new ColorBlend
                {
                    Colors = [AgrisTheme.NavBack, AgrisTheme.BrandTealDark, Color.FromArgb(0, 92, 58)],
                    Positions = [0f, 0.6f, 1f]
                };
                bg.InterpolationColors = blend;
                g.FillRectangle(bg, rect);
            }

            // Hoạ tiết vòng tròn mờ — chiều sâu mà không rối mắt.
            using (var ring = new Pen(Color.FromArgb(22, 255, 255, 255), 2f))
            {
                g.DrawEllipse(ring, Width - 360, -180, 560, 560);
                g.DrawEllipse(ring, Width - 260, -80, 360, 360);
                g.DrawEllipse(ring, -200, Height - 300, 520, 520);
            }
            using (var glow = new SolidBrush(Color.FromArgb(14, 255, 255, 255)))
                g.FillEllipse(glow, -140, Height - 240, 400, 400);

            var left = Math.Max(48, Width / 9);
            var top = Math.Max(60, Height / 2 - 220);

            // Logo đặt trên thẻ trắng bo góc — logo gốc nền trắng nên không đặt trực tiếp lên nền tối.
            var logo = LogoHelper.GetLogo();
            var tile = new Rectangle(left, top, 220, 110);
            using (var tilePath = Shapes.RoundedRect(tile, AgrisTheme.RadiusLg))
            using (var tileBrush = new SolidBrush(Color.White))
                g.FillPath(tileBrush, tilePath);

            if (logo is not null)
            {
                var ratio = Math.Min((tile.Width - 32) / (double)logo.Width, (tile.Height - 24) / (double)logo.Height);
                var w = (int)(logo.Width * ratio);
                var h = (int)(logo.Height * ratio);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(logo, tile.Left + (tile.Width - w) / 2, tile.Top + (tile.Height - h) / 2, w, h);
            }
            else
            {
                using var f = new Font(AgrisTheme.FontFamily, 30F, FontStyle.Bold);
                TextRenderer.DrawText(g, "AgriS", f, tile, AgrisTheme.BrandGreen,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            var y = tile.Bottom + 48;
            var textWidth = Width - left * 2;

            using (var fTitle = new Font(AgrisTheme.FontFamily, 26F, FontStyle.Bold))
            {
                var r = new Rectangle(left, y, textWidth, 96);
                TextRenderer.DrawText(g, "Hệ thống cân hợp nhất\nmía và vật tư", fTitle, r, Color.White,
                    TextFormatFlags.Left | TextFormatFlags.WordBreak);
                y += 104;
            }

            using (var fSub = new Font(AgrisTheme.FontFamily, 12F))
            {
                TextRenderer.DrawText(g, "Unified Sugarcane & Material Weighing System", fSub,
                    new Rectangle(left, y, textWidth, 28), Color.FromArgb(190, 215, 225),
                    TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                y += 56;
            }

            using (var accent = new SolidBrush(AgrisTheme.NavAccent))
                g.FillRectangle(accent, left, y, 56, 4);

            using var fTag = new Font(AgrisTheme.FontFamily, 9F, FontStyle.Bold);
            TextRenderer.DrawText(g, "WEIGH SMARTER  ·  DRIVE FURTHER", fTag,
                new Rectangle(left, Height - 64, textWidth, 24), Color.FromArgb(130, 160, 175),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            return bmp;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _cache?.Dispose();
            base.Dispose(disposing);
        }
    }
}
