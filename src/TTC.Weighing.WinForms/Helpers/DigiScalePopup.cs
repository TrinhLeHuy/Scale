using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Helpers;

/// <summary>
/// Popup thông báo kết quả push DigiScale — dùng chung cho tất cả form.
/// </summary>
public static class DigiScalePopup
{
    public static void Show(IWin32Window owner, string title, string details, bool digiOk, string digiStatus)
    {
        using var popup = new Form
        {
            Text = digiOk ? $"✅ {title}" : $"⚠ {title} — CHƯA ĐẨY DIGI",
            Size = new Size(520, 380),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = digiOk ? Color.FromArgb(232, 255, 232) : Color.FromArgb(255, 255, 220)
        };

        var lblIcon = new Label
        {
            Text = digiOk ? "✅" : "⚠",
            Font = new Font(AgrisTheme.FontFamily, 42F),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 60,
            ForeColor = digiOk ? AgrisTheme.Success : AgrisTheme.Warning
        };

        var lblTitle = new Label
        {
            Text = digiOk ? title.ToUpperInvariant() : $"{title} — CHƯA ĐẨY DIGI",
            Font = new Font(AgrisTheme.FontFamily, 16F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 36,
            ForeColor = digiOk ? AgrisTheme.BrandGreen : AgrisTheme.Warning
        };

        var lblDetails = new Label
        {
            Text = details + $"\n\nDigiScale: {digiStatus}",
            Font = new Font(AgrisTheme.FontFamily, 11F),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill,
            ForeColor = AgrisTheme.TextPrimary,
            Padding = new Padding(16)
        };

        var btnOk = new Button
        {
            Text = "OK",
            Size = new Size(140, 40),
            Font = new Font(AgrisTheme.FontFamily, 11F, FontStyle.Bold),
            BackColor = digiOk ? AgrisTheme.BrandGreen : AgrisTheme.Warning,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.OK
        };
        btnOk.FlatAppearance.BorderSize = 0;

        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 54,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(16, 6, 16, 6)
        };
        btnPanel.Controls.Add(btnOk);

        popup.AcceptButton = btnOk;
        popup.Controls.Add(lblDetails);
        popup.Controls.Add(lblTitle);
        popup.Controls.Add(lblIcon);
        popup.Controls.Add(btnPanel);
        popup.ShowDialog(owner);
    }

    /// <summary>
    /// Popup cảnh báo/lỗi validation — font lớn, rõ ràng, không kèm DigiScale status.
    /// Icon: ⛔ (error) hoặc ⚠ (warning).
    /// </summary>
    public static void ShowWarning(IWin32Window owner, string title, string details)
    {
        using var popup = new Form
        {
            Text = $"⚠ {title}",
            Size = new Size(520, 340),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Color.FromArgb(255, 248, 230)
        };

        var lblIcon = new Label
        {
            Text = "⚠",
            Font = new Font(AgrisTheme.FontFamily, 42F),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 60,
            ForeColor = AgrisTheme.Warning
        };

        var lblTitle = new Label
        {
            Text = title,
            Font = new Font(AgrisTheme.FontFamily, 15F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 36,
            ForeColor = Color.FromArgb(180, 80, 0)
        };

        var lblDetails = new Label
        {
            Text = details,
            Font = new Font(AgrisTheme.FontFamily, 11F),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill,
            ForeColor = AgrisTheme.TextPrimary,
            Padding = new Padding(20, 8, 20, 8)
        };

        var btnOk = new Button
        {
            Text = "OK",
            Size = new Size(140, 40),
            Font = new Font(AgrisTheme.FontFamily, 11F, FontStyle.Bold),
            BackColor = AgrisTheme.Warning,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.OK
        };
        btnOk.FlatAppearance.BorderSize = 0;

        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 54,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(16, 6, 16, 6)
        };
        btnPanel.Controls.Add(btnOk);

        popup.AcceptButton = btnOk;
        popup.Controls.Add(lblDetails);
        popup.Controls.Add(lblTitle);
        popup.Controls.Add(lblIcon);
        popup.Controls.Add(btnPanel);
        popup.ShowDialog(owner);
    }
}
