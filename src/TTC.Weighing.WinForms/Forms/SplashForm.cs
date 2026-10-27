using System.Drawing.Drawing2D;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Forms;

/// <summary>
/// Splash screen khi khởi động: nền tối thương hiệu, logo trên thẻ trắng, thanh tiến trình chạy.
/// </summary>
public sealed class SplashForm : Form
{
    private readonly Label _lblStatus;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 30 };
    private float _phase;

    public SplashForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(520, 320);
        BackColor = AgrisTheme.NavBack;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw, true);

        _lblStatus = new Label
        {
            Text = "Đang khởi động... / Starting...",
            Font = new Font(AgrisTheme.FontFamily, 9F),
            ForeColor = Color.FromArgb(170, 195, 205),
            Location = new Point(40, 236),
            Size = new Size(440, 24),
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent
        };
        Controls.Add(_lblStatus);

        // Thanh tiến trình chạy — cho biết ứng dụng không bị treo trong lúc khởi động.
        _timer.Tick += (_, _) =>
        {
            _phase = (_phase + 0.018f) % 1.4f;
            Invalidate(new Rectangle(40, 268, 440, 6));
        };
        Shown += (_, _) => _timer.Start();
        FormClosed += (_, _) => _timer.Stop();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var bg = new LinearGradientBrush(ClientRectangle, AgrisTheme.NavBack, AgrisTheme.BrandTealDark, 35f))
            g.FillRectangle(bg, ClientRectangle);

        using (var ring = new Pen(Color.FromArgb(22, 255, 255, 255), 2f))
            g.DrawEllipse(ring, Width - 200, -120, 340, 340);

        // Logo trên thẻ trắng.
        var tile = new Rectangle(40, 40, 160, 80);
        using (var tilePath = Shapes.RoundedRect(tile, AgrisTheme.RadiusLg))
        using (var white = new SolidBrush(Color.White))
            g.FillPath(white, tilePath);

        if (LogoHelper.GetLogo() is { } logo)
        {
            var ratio = Math.Min((tile.Width - 24) / (double)logo.Width, (tile.Height - 16) / (double)logo.Height);
            var w = (int)(logo.Width * ratio);
            var h = (int)(logo.Height * ratio);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(logo, tile.Left + (tile.Width - w) / 2, tile.Top + (tile.Height - h) / 2, w, h);
        }

        using (var fTitle = new Font(AgrisTheme.FontFamily, 22F, FontStyle.Bold))
            TextRenderer.DrawText(g, "DigiScale", fTitle, new Rectangle(40, 140, 440, 44), Color.White,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        using (var fSub = new Font(AgrisTheme.FontFamily, 10F))
            TextRenderer.DrawText(g, "Weigh Smarter · Drive Further", fSub, new Rectangle(40, 184, 440, 24),
                Color.FromArgb(180, 210, 220), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        // Thanh tiến trình không xác định.
        var track = new Rectangle(40, 268, 440, 4);
        using (var trackBrush = new SolidBrush(Color.FromArgb(40, 255, 255, 255)))
        using (var trackPath = Shapes.RoundedRect(track, 2))
            g.FillPath(trackBrush, trackPath);

        var start = (int)(track.Left + (_phase - 0.4f) * track.Width);
        var seg = Rectangle.Intersect(track, new Rectangle(start, track.Top, (int)(track.Width * 0.4f), track.Height));
        if (seg.Width > 0)
        {
            using var accent = new SolidBrush(AgrisTheme.NavAccent);
            using var segPath = Shapes.RoundedRect(seg, 2);
            g.FillPath(accent, segPath);
        }

        using var fCopy = new Font(AgrisTheme.FontFamily, 8F);
        TextRenderer.DrawText(g, $"© {DateTime.Now.Year} AgriS", fCopy, new Rectangle(40, 284, 440, 24),
            Color.FromArgb(120, 150, 165), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }

    public void SetStatus(string text)
    {
        if (InvokeRequired)
            Invoke(() => _lblStatus.Text = text);
        else
            _lblStatus.Text = text;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _timer.Dispose();
        base.Dispose(disposing);
    }
}
