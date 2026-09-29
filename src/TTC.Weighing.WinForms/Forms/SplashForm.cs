using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Forms;

/// <summary>
/// Splash screen — hiện logo DigiScale khi khởi động, tránh chớp giật.
/// </summary>
public sealed class SplashForm : Form
{
    private readonly Label _lblStatus;

    public SplashForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(480, 320);
        BackColor = Color.FromArgb(36, 88, 110);
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);

        var lblTitle = new Label
        {
            Text = "DigiScale",
            Font = new Font("Segoe UI", 28F, FontStyle.Bold),
            ForeColor = Color.White,
            Dock = DockStyle.Top,
            Height = 80,
            TextAlign = ContentAlignment.BottomCenter,
            BackColor = Color.Transparent
        };

        var lblSub = new Label
        {
            Text = "Weigh Smarter · Drive Further",
            Font = new Font("Segoe UI", 10F, FontStyle.Italic),
            ForeColor = Color.FromArgb(180, 220, 220),
            Dock = DockStyle.Top,
            Height = 30,
            TextAlign = ContentAlignment.TopCenter,
            BackColor = Color.Transparent
        };

        _lblStatus = new Label
        {
            Text = "Đang khởi động...",
            Font = new Font("Segoe UI", 9F),
            ForeColor = Color.FromArgb(150, 200, 200),
            Dock = DockStyle.Bottom,
            Height = 40,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent
        };

        var lblCopy = new Label
        {
            Text = "© 2026 AgriS",
            Font = new Font("Segoe UI", 8F),
            ForeColor = Color.FromArgb(100, 160, 160),
            Dock = DockStyle.Bottom,
            Height = 24,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent
        };

        Controls.Add(_lblStatus);
        Controls.Add(lblCopy);
        Controls.Add(lblSub);
        Controls.Add(lblTitle);
    }

    public void SetStatus(string text)
    {
        if (InvokeRequired)
            Invoke(() => _lblStatus.Text = text);
        else
            _lblStatus.Text = text;
    }
}
