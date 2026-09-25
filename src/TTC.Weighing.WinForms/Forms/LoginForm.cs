using System.Drawing.Drawing2D;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Services;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Forms;

/// <summary>Mục ngôn ngữ trong combo box, hiển thị tên bản địa của ngôn ngữ.</summary>
internal sealed record LanguageItem(AppLanguage Language)
{
    public override string ToString() => Language.DisplayName();
}

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

    private static readonly Color Teal = Color.FromArgb(22, 111, 163);
    private static readonly Color Green = Color.FromArgb(0, 148, 68);

    private readonly TextBox _txtUser;
    private readonly TextBox _txtPass;
    private readonly Button _btnLogin;
    private readonly Label _lblError;
    private readonly Label _lblTitle;
    private readonly Label _lblSub;
    private readonly Label _lblU;
    private readonly Label _lblP;
    private readonly string? _apiBaseUrl;
    private readonly string? _requiredTenantId;

    // Card + close button — lưu để DoLayout dùng.
    private readonly Panel _card;
    private readonly Button _btnClose;

    // Cờ chặn vẽ + layout khi form đang maximize / chưa sẵn sàng.
    private bool _ready;

    public string? AuthenticatedUser { get; private set; }

    /// <summary>Kết quả đăng nhập API (token, permissions, ...). Null nếu chưa login hoặc lỗi.</summary>
    public LoginResult? LoginResult { get; private set; }

    public LoginForm(string? apiBaseUrl = null, string? requiredTenantId = null)
    {
        _apiBaseUrl = apiBaseUrl;
        _requiredTenantId = requiredTenantId;
        Text = "Đăng nhập / Sign In";
        FormBorderStyle = FormBorderStyle.None;
        DoubleBuffered = true;
        KeyPreview = true;

        // Không dùng WindowState = Maximized (gây chớp nháy do Windows resize 2 lần).
        // Thay vào đó set kích thước = toàn màn hình ngay từ constructor.
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        StartPosition = FormStartPosition.Manual;
        Location = screen.Location;
        ClientSize = screen.Size;
        BackColor = Color.FromArgb(42, 90, 100);

        Paint += PaintBg;

        SuspendLayout();

        // ── Card giữa màn hình ──
        _card = new Panel { Size = new Size(440, 520), BackColor = Color.Transparent, Visible = false };
        _card.Paint += PaintCard;

        // Logo area
        var pnlLogo = new Panel { Dock = DockStyle.Top, Height = 160, BackColor = Color.Transparent };
        pnlLogo.Paint += PaintLogo;

        // Form area
        var formArea = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(48, 8, 48, 16)
        };

        _lblTitle = new Label
        {
            Text = "Đăng nhập hệ thống / System Login",
            Dock = DockStyle.Top, Height = 38,
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 60, 70),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent
        };

        _lblSub = new Label
        {
            Text = "Nhập thông tin tài khoản / Enter your credentials",
            Dock = DockStyle.Top, Height = 22,
            Font = new Font("Segoe UI", 9.5F),
            ForeColor = Color.FromArgb(120, 135, 140),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent
        };

        var sp1 = Spacer(18);
        _lblU = FieldLabel("TÊN ĐĂNG NHẬP / USERNAME");
        _txtUser = Input("Nhập tên đăng nhập / Enter username");
        var sp2 = Spacer(12);
        _lblP = FieldLabel("MẬT KHẨU / PASSWORD");
        _txtPass = Input("••••••••");
        _txtPass.UseSystemPasswordChar = true;
        var sp3 = Spacer(20);

        _btnLogin = new Button
        {
            Text = "ĐĂNG NHẬP / SIGN IN",
            Dock = DockStyle.Top, Height = 44,
            FlatStyle = FlatStyle.Flat,
            BackColor = Teal, ForeColor = Color.White,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        _btnLogin.FlatAppearance.BorderSize = 0;
        _btnLogin.FlatAppearance.MouseOverBackColor = Green;
        _btnLogin.Paint += PaintBtn;
        _btnLogin.Click += OnLogin;

        _lblError = new Label
        {
            Text = string.Empty,
            Dock = DockStyle.Top, Height = 26,
            Font = new Font("Segoe UI", 9.5F),
            ForeColor = AgrisTheme.Danger,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent
        };

        formArea.Controls.Add(_lblError);
        formArea.Controls.Add(Spacer(6));
        formArea.Controls.Add(_btnLogin);
        formArea.Controls.Add(sp3);
        formArea.Controls.Add(_txtPass);
        formArea.Controls.Add(_lblP);
        formArea.Controls.Add(sp2);
        formArea.Controls.Add(_txtUser);
        formArea.Controls.Add(_lblU);
        formArea.Controls.Add(sp1);
        formArea.Controls.Add(_lblSub);
        formArea.Controls.Add(_lblTitle);

        _card.Controls.Add(formArea);
        _card.Controls.Add(pnlLogo);

        // Close
        _btnClose = new Button
        {
            Text = "✕", Size = new Size(40, 30), FlatStyle = FlatStyle.Flat,
            BackColor = Color.Transparent, ForeColor = Color.FromArgb(200, 255, 255, 255),
            Font = new Font("Segoe UI", 14F), Cursor = Cursors.Hand,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Visible = false
        };
        _btnClose.FlatAppearance.BorderSize = 0;
        _btnClose.FlatAppearance.MouseOverBackColor = AgrisTheme.Danger;
        _btnClose.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        // Footer
        var lblFoot = new Label
        {
            Text = "© 2026 AgriS · Weigh Smarter, Drive Further",
            Dock = DockStyle.Bottom, Height = 28,
            Font = new Font(AgrisTheme.FontFamily, 8.5F),
            ForeColor = Color.FromArgb(140, 255, 255, 255),
            TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent
        };

        Controls.Add(_card);
        Controls.Add(_btnClose);
        Controls.Add(lblFoot);

        ResumeLayout(false);

        // DoLayout chỉ chạy khi _ready = true, tránh repaint loạn khi maximize.
        Resize += (_, _) => { if (_ready) DoLayout(); };

        // Shown fire SAU khi form đã maximize + hiển thị xong.
        // Lúc này mới layout card, hiện controls, vẽ nền — một lần duy nhất.
        Shown += (_, _) =>
        {
            DoLayout();
            _card.Visible = true;
            _btnClose.Visible = true;
            _ready = true;
            Invalidate(); // Vẽ gradient background lần đầu
            _txtUser.Focus();
        };

        AcceptButton = _btnLogin;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
    }

    private void DoLayout()
    {
        _card.Location = new Point(
            (ClientSize.Width - _card.Width) / 2,
            Math.Max(30, (ClientSize.Height - _card.Height) / 2));
        _btnClose.Location = new Point(ClientSize.Width - 48, 8);
    }

    /// <summary>Song ngữ cố định — không cần cập nhật khi đổi ngôn ngữ.</summary>

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
            _btnLogin.Enabled = false;
            _btnLogin.Text = "Đang đăng nhập... / Signing in...";
            _txtUser.Enabled = false;
            _txtPass.Enabled = false;
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
                _btnLogin.Enabled = true;
                _btnLogin.Text = "ĐĂNG NHẬP / SIGN IN";
                _txtUser.Enabled = true;
                _txtPass.Enabled = true;
            }
            return;
        }

        // Fallback: đăng nhập cục bộ khi chưa cấu hình API (dev mode)
        if (u.Equals("Admin", StringComparison.OrdinalIgnoreCase) && p == "Admin@123")
        { AuthenticatedUser = u; DialogResult = DialogResult.OK; Close(); return; }
        _lblError.Text = "❌ Sai tên đăng nhập hoặc mật khẩu / Incorrect credentials";
        _txtPass.SelectAll(); _txtPass.Focus();
    }

    // ═══ Drawing ═══

    private void PaintBg(object? sender, PaintEventArgs e)
    {
        if (!_ready) return; // Chưa sẵn sàng → không vẽ, tránh flicker khi maximize.

        var g = e.Graphics;
        // Nền gradient sáng hơn: teal sáng → xanh lá sáng (chéo)
        using var bg = new LinearGradientBrush(ClientRectangle,
            Color.FromArgb(36, 88, 110),
            Color.FromArgb(32, 82, 68),
            LinearGradientMode.ForwardDiagonal);
        g.FillRectangle(bg, ClientRectangle);

        // Glow mềm
        using var glow1 = new SolidBrush(Color.FromArgb(10, 255, 255, 255));
        g.FillEllipse(glow1, -150, ClientSize.Height - 350, 500, 500);
        using var glow2 = new SolidBrush(Color.FromArgb(8, 255, 255, 255));
        g.FillEllipse(glow2, ClientSize.Width - 250, -100, 450, 450);
    }

    private static void PaintCard(object? sender, PaintEventArgs e)
    {
        var panel = (Panel)sender!;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, panel.Width - 1, panel.Height - 1);
        using var path = RoundedRect(rect, 16);

        // Card nền trắng bán trong suốt (glassmorphism nhẹ)
        using var fill = new SolidBrush(Color.FromArgb(240, 255, 255, 255));
        g.FillPath(fill, path);

        // Viền mờ
        using var border = new Pen(Color.FromArgb(60, 255, 255, 255), 1.5f);
        g.DrawPath(border, path);

        panel.Region = new Region(RoundedRect(new Rectangle(0, 0, panel.Width, panel.Height), 16));
    }

    private static void PaintLogo(object? sender, PaintEventArgs e)
    {
        var p = (Panel)sender!;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var cx = p.Width / 2;

        var logo = LogoHelper.GetLogo();
        if (logo is not null)
        {
            var maxH = p.Height - 12;
            var ratio = Math.Min((double)(p.Width - 60) / logo.Width, (double)maxH / logo.Height);
            var w = (int)(logo.Width * ratio);
            var h = (int)(logo.Height * ratio);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(logo, cx - w / 2, (p.Height - h) / 2, w, h);
            return;
        }

        // Fallback — chỉ hiện chữ viết tắt, không có tên thương hiệu
        using var f1 = new Font("Segoe UI", 48F, FontStyle.Bold);
        using var b1 = new LinearGradientBrush(new Rectangle(cx - 35, 12, 70, 65), Teal, Green, LinearGradientMode.Vertical);
        var s1 = g.MeasureString("W", f1);
        g.DrawString("W", f1, b1, cx - s1.Width / 2, 18);

        using var f3 = new Font("Segoe UI", 9F, FontStyle.Bold);
        using var b3 = new SolidBrush(Color.FromArgb(100, 115, 120));
        var t = "Hệ thống cân hợp nhất / Unified Weighing";
        var s3 = g.MeasureString(t, f3);
        g.DrawString(t, f3, b3, cx - s3.Width / 2, 88);

        using var f4 = new Font("Segoe UI", 7.5F);
        using var b4 = new SolidBrush(Color.FromArgb(130, 145, 150));
        var t2 = "WEIGH SMARTER   DRIVE FURTHER";
        var s4 = g.MeasureString(t2, f4);
        g.DrawString(t2, f4, b4, cx - s4.Width / 2, 112);
    }

    private static void PaintBtn(object? sender, PaintEventArgs e)
    {
        var btn = (Button)sender!;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Bo tròn nút
        var rect = new Rectangle(0, 0, btn.Width - 1, btn.Height - 1);
        using var path = RoundedRect(rect, 8);
        using var brush = new LinearGradientBrush(rect, Teal, Green, LinearGradientMode.Horizontal);
        g.FillPath(brush, path);
        btn.Region = new Region(RoundedRect(new Rectangle(0, 0, btn.Width, btn.Height), 8));

        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(btn.Text, btn.Font, Brushes.White, btn.ClientRectangle, sf);
    }

    private static GraphicsPath RoundedRect(Rectangle r, int rad)
    {
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, rad, rad, 180, 90);
        p.AddArc(r.Right - rad, r.Y, rad, rad, 270, 90);
        p.AddArc(r.Right - rad, r.Bottom - rad, rad, rad, 0, 90);
        p.AddArc(r.X, r.Bottom - rad, rad, rad, 90, 90);
        p.CloseFigure();
        return p;
    }

    private static Label FieldLabel(string t) => new()
    {
        Text = t, Dock = DockStyle.Top, Height = 22,
        Font = new Font(AgrisTheme.FontFamily, 8.5F, FontStyle.Bold),
        ForeColor = Color.FromArgb(100, 115, 120),
        Padding = new Padding(0, 4, 0, 0),
        BackColor = Color.Transparent
    };

    private static TextBox Input(string ph) => new()
    {
        Dock = DockStyle.Top, Font = new Font(AgrisTheme.FontFamily, 12F), Height = 38,
        PlaceholderText = ph, BorderStyle = BorderStyle.FixedSingle,
        BackColor = Color.FromArgb(245, 248, 247)
    };

    private static Panel Spacer(int h) => new() { Dock = DockStyle.Top, Height = h, BackColor = Color.Transparent };
}
