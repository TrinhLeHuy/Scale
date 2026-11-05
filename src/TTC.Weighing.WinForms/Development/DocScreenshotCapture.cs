using System.Drawing.Imaging;
using TTC.Weighing.WinForms.Forms;

namespace TTC.Weighing.WinForms.Development;

/// <summary>
/// Chế độ chụp ảnh màn hình cho tài liệu hướng dẫn (cờ <c>--capture-docs &lt;thư mục&gt;</c>).
/// Mở lần lượt từng màn hình trong MainForm, chờ dữ liệu tải xong rồi chụp vùng cửa sổ.
/// Chỉ đọc: không bấm nút nghiệp vụ, không đẩy FRM/ERP/DigiScale, không mở barie.
/// </summary>
internal static class DocScreenshotCapture
{
    /// <summary>Tên file ảnh ↔ màn hình. Thứ tự theo sidebar.</summary>
    internal static readonly (string File, Type Form)[] Screens =
    [
        ("02-tong-quan.png", typeof(DashboardForm)),
        ("03-dang-tai.png", typeof(TripRegistrationForm)),
        ("04-can-hop-nhat.png", typeof(WeighOperationForm)),
        ("05-do-tap-chat.png", typeof(QualityForm)),
        ("06-do-ccs.png", typeof(CcsForm)),
        ("07-chi-tiet-chuyen.png", typeof(TripDetailForm)),
        ("08-bao-cao.png", typeof(ReportForm)),
        ("10-nhat-ky-api.png", typeof(ApiLogForm)),
        ("11-cau-hinh.png", typeof(ConfigurationForm)),
        ("12-du-lieu-frm.png", typeof(FrmSyncForm)),
        ("13-du-lieu-erp.png", typeof(ErpSyncForm)),
    ];

    /// <summary>Chụp màn hình đăng nhập (trước khi có DI).</summary>
    internal static void CaptureLogin(string outputDir, string? apiBaseUrl, string? tenantId)
    {
        using var login = new LoginForm(apiBaseUrl, tenantId) { StartPosition = FormStartPosition.CenterScreen };
        login.Show();
        Pump(1500);
        Save(login, Path.Combine(outputDir, "01-dang-nhap.png"));
        login.Hide();
    }

    /// <summary>Chạy MainForm và chụp lần lượt các màn hình. Trả về danh sách file + lỗi.</summary>
    internal static void CaptureScreens(MainForm mainForm, string outputDir, int waitMs, List<string> log)
    {
        // Giữ phóng to toàn màn hình như khi vận hành thật; chụp vùng client để không dính viền.
        mainForm.TopMost = true;
        var logPath = Path.Combine(outputDir, "capture-log.txt");
        var current = "";
        var popupCount = 0;

        // Popup modal mở đồng bộ sẽ chặn vòng chụp. Timer vẫn chạy trong vòng lặp modal,
        // nên dùng nó để chụp popup (đặt tên theo màn hình) rồi đóng lại.
        var watchdog = new System.Windows.Forms.Timer { Interval = 1500 };
        watchdog.Tick += (_, _) =>
        {
            foreach (var f in System.Windows.Forms.Application.OpenForms.Cast<Form>().ToList())
            {
                if (f == mainForm || !f.TopLevel || !f.Visible || f is LoginForm || f is SplashForm) continue;
                var name = $"{Path.GetFileNameWithoutExtension(current)}-popup{++popupCount}.png";
                try { Save(f, Path.Combine(outputDir, name)); } catch { /* bỏ qua */ }
                var msg = $"POPUP {name}  {f.GetType().Name}:'{f.Text}'";
                log.Add(msg);
                File.AppendAllLines(logPath, [msg]);
                if (DumpDir is { } d) File.WriteAllText(Path.Combine(d, Path.ChangeExtension(name, ".txt")), DescribeUi(f));
                try { f.DialogResult = DialogResult.Cancel; f.Close(); } catch { /* bỏ qua */ }
            }
        };
        watchdog.Start();
        File.WriteAllText(logPath, "");

        mainForm.Shown += async (_, _) =>
        {
            await Task.Delay(waitMs);

            foreach (var (file, formType) in Screens)
            {
                current = file;
                File.AppendAllLines(logPath, [$"START {file}"]);
                try
                {
                    CloseStrayDialogs(mainForm);
                    mainForm.CloseAllTabsForDocs();
                    mainForm.OpenScreenForDocs(formType);
                    await Task.Delay(waitMs);

                    // Nếu màn hình tự bật popup khi mở, chụp cả popup để biết, rồi đóng.
                    Save(mainForm, Path.Combine(outputDir, file));
                    if (DumpDir is { } dumpDir)
                        File.WriteAllText(Path.Combine(dumpDir, Path.ChangeExtension(file, ".txt")), DescribeUi(mainForm));
                    var ok = $"OK    {file}  ({formType.Name})";
                    log.Add(ok);
                    File.AppendAllLines(logPath, [ok]);
                }
                catch (Exception ex)
                {
                    var err = $"ERROR {file}  ({formType.Name}): {ex.Message}";
                    log.Add(err);
                    File.AppendAllLines(logPath, [err]);
                }
            }

            watchdog.Stop();
            CloseStrayDialogs(mainForm);
            mainForm.Close();
        };
    }

    /// <summary>Thư mục ghi mô tả chữ trên giao diện (để đối chiếu khi viết tài liệu). Null = không ghi.</summary>
    internal static string? DumpDir { get; set; }

    /// <summary>Liệt kê control đang hiện: loại, chữ, số dòng lưới. Dùng để viết tài liệu khớp giao diện.</summary>
    private static string DescribeUi(Form root)
    {
        var sb = new System.Text.StringBuilder();

        void Walk(Control c, int depth)
        {
            if (!c.Visible) return;
            var text = c.Text?.Replace("\r", " ").Replace("\n", " ").Trim();
            var extra = c switch
            {
                DataGridView g => $" rows={g.Rows.Count} cols=[{string.Join(" | ", g.Columns.Cast<DataGridViewColumn>().Where(x => x.Visible).Select(x => x.HeaderText))}]",
                ComboBox cb => $" items={cb.Items.Count}",
                _ => ""
            };
            if (!string.IsNullOrEmpty(text) || extra.Length > 0)
            {
                var shown = text is { Length: > 160 } ? text[..160] + "…" : text;
                sb.AppendLine($"{new string(' ', depth * 2)}{c.GetType().Name} '{shown}'{extra}");
            }
            foreach (Control child in c.Controls) Walk(child, depth + 1);
        }

        Walk(root, 0);
        return sb.ToString();
    }

    private static void CloseStrayDialogs(Form mainForm)
    {
        foreach (var f in System.Windows.Forms.Application.OpenForms.Cast<Form>().ToList())
        {
            if (f == mainForm || !f.TopLevel || !f.Visible || f is LoginForm || f is SplashForm) continue;
            try { f.Close(); } catch { /* bỏ qua */ }
        }
    }

    private static void Pump(int ms)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until)
        {
            System.Windows.Forms.Application.DoEvents();
            Thread.Sleep(20);
        }
    }

    /// <summary>
    /// Chụp từ màn hình thật (giữ đúng font/owner-draw). Nếu không có desktop tương tác
    /// thì dùng DrawToBitmap.
    /// </summary>
    private static void Save(Form form, string path)
    {
        form.Activate();
        form.Refresh();
        System.Windows.Forms.Application.DoEvents();

        if (form is MainForm && form.WindowState != FormWindowState.Maximized)
        {
            form.WindowState = FormWindowState.Maximized;
            Pump(500);
        }

        var bounds = form.RectangleToScreen(form.ClientRectangle);

        using var bmp = new Bitmap(bounds.Width, bounds.Height);
        try
        {
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        }
        catch
        {
            form.DrawToBitmap(bmp, new Rectangle(Point.Empty, bounds.Size));
        }

        bmp.Save(path, ImageFormat.Png);
    }
}
