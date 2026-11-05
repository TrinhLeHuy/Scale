using QRCoder;

namespace TTC.Weighing.WinForms.Printing;

/// <summary>
/// Vẽ mã QR lên phiếu in (thay mã vạch Code 128). Nội dung QR:
/// phiếu đăng tài / phiếu cân / tạp chất / CCS = SỐ ĐĂNG TÀI — quét ở các màn Cân, Đo tạp chất, Đo CCS để mở chuyến.
/// Mức sửa lỗi M (chịu được ~15% hỏng / bẩn); có viền trắng 2 ô để máy quét đọc chắc.
/// </summary>
public static class QrCodeRenderer
{
    private const int QUIET_ZONE_MODULES = 2;

    /// <summary>Vẽ QR vuông cạnh <paramref name="size"/> pixel tại (x, y). Trả false nếu nội dung rỗng / lỗi.</summary>
    public static bool Draw(Graphics g, string? content, int x, int y, int size)
    {
        if (string.IsNullOrWhiteSpace(content) || size <= 0) return false;
        try
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(content.Trim(), QRCodeGenerator.ECCLevel.M);
            var matrix = data.ModuleMatrix; // đã gồm 4 ô viền mặc định của QRCoder
            var count = matrix.Count;
            if (count == 0) return false;

            // Bỏ bớt viền mặc định (4 ô) còn 2 ô để QR to hơn trong cùng khung.
            var trim = Math.Max(0, 4 - QUIET_ZONE_MODULES);
            var modules = count - trim * 2;
            var cell = size / (float)modules;

            g.FillRectangle(Brushes.White, x, y, size, size);
            for (var r = trim; r < count - trim; r++)
            {
                var row = matrix[r];
                for (var c = trim; c < count - trim; c++)
                {
                    if (!row[c]) continue;
                    // +0.5 tránh khe trắng giữa các ô khi in / phóng to.
                    g.FillRectangle(Brushes.Black,
                        x + (c - trim) * cell, y + (r - trim) * cell, cell + 0.5f, cell + 0.5f);
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }
}
