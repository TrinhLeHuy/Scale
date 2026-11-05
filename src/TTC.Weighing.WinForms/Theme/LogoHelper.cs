namespace TTC.Weighing.WinForms.Theme;

/// <summary>
/// Load logo từ file Assets/logo.png.
/// Trả null nếu file không tồn tại — form tự vẽ text fallback.
/// </summary>
public static class LogoHelper
{
    private static Image? _cached;
    private static bool _loaded;
    private static Icon? _appIcon;
    private static bool _iconLoaded;

    /// <summary>Icon ứng dụng (Assets/logo.ico) cho thanh tiêu đề và taskbar. Null nếu thiếu tệp.</summary>
    public static Icon? GetAppIcon()
    {
        if (_iconLoaded) return _appIcon;
        _iconLoaded = true;

        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "logo.ico");
        try
        {
            _appIcon = File.Exists(path)
                ? new Icon(path)
                : Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? System.Windows.Forms.Application.ExecutablePath);
        }
        catch { /* Không có icon → dùng icon mặc định của WinForms */ }

        return _appIcon;
    }

    /// <summary>Logo đầy đủ (cho LoginForm). Trả null nếu chưa có file.</summary>
    public static Image? GetLogo()
    {
        if (_loaded) return _cached;
        _loaded = true;

        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "logo.png");
        if (!File.Exists(path))
        {
            // Thử tìm ở thư mục gốc project (khi debug từ IDE)
            path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Assets", "logo.png");
        }

        if (File.Exists(path))
        {
            try
            {
                // Load vào memory để không lock file
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
                _cached = Image.FromStream(fs);
            }
            catch { /* File lỗi → dùng fallback */ }
        }

        return _cached;
    }

    /// <summary>Logo thu nhỏ cho brand bar (height cố định, giữ tỷ lệ). Tự crop khoảng trắng.</summary>
    public static Image? GetLogoScaled(int targetHeight)
    {
        var logo = GetLogo();
        if (logo is null) return null;

        // Crop khoảng trắng xung quanh logo
        var bmp = new Bitmap(logo);
        var cropRect = GetContentBounds(bmp);

        if (cropRect.Width < 10 || cropRect.Height < 10)
        {
            // Logo quá nhỏ hoặc trắng hoàn toàn → dùng nguyên gốc
            var ratio0 = (double)targetHeight / logo.Height;
            var w0 = (int)(logo.Width * ratio0);
            var scaled0 = new Bitmap(w0, targetHeight);
            using var g0 = Graphics.FromImage(scaled0);
            g0.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g0.DrawImage(logo, 0, 0, w0, targetHeight);
            return scaled0;
        }

        var ratio = (double)targetHeight / cropRect.Height;
        var width = (int)(cropRect.Width * ratio);

        var scaled = new Bitmap(width, targetHeight);
        using var g = Graphics.FromImage(scaled);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(bmp, new Rectangle(0, 0, width, targetHeight), cropRect, GraphicsUnit.Pixel);

        return scaled;
    }

    /// <summary>Tìm vùng chứa nội dung thực (bỏ khoảng trắng/trong suốt xung quanh). Dùng LockBits cho tốc độ.</summary>
    private static Rectangle GetContentBounds(Bitmap bmp)
    {
        var data = bmp.LockBits(
            new Rectangle(0, 0, bmp.Width, bmp.Height),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        var stride = data.Stride;
        var bytes = new byte[Math.Abs(stride) * bmp.Height];
        System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
        bmp.UnlockBits(data);

        var top = bmp.Height;
        var bottom = 0;
        var left = bmp.Width;
        var right = 0;

        for (var y = 0; y < bmp.Height; y++)
        {
            for (var x = 0; x < bmp.Width; x++)
            {
                var i = y * Math.Abs(stride) + x * 4;
                var a = bytes[i + 3];
                var r = bytes[i + 2];
                var g = bytes[i + 1];
                var b = bytes[i];

                if (a > 20 && (r < 240 || g < 240 || b < 240))
                {
                    if (y < top) top = y;
                    if (y > bottom) bottom = y;
                    if (x < left) left = x;
                    if (x > right) right = x;
                }
            }
        }

        if (top > bottom || left > right)
            return new Rectangle(0, 0, bmp.Width, bmp.Height);

        return new Rectangle(left, top, right - left + 1, bottom - top + 1);
    }
}
