using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Forms;

/// <summary>
/// Tab hướng dẫn sử dụng. Nội dung đọc từ docs/HUONG_DAN_SU_DUNG.md nếu có,
/// hoặc hiện hướng dẫn nhúng sẵn nếu tệp không tìm thấy.
/// </summary>
public sealed class GuideForm : Form
{
    public GuideForm()
    {
        Text = Loc.T("guide.title");
        BackColor = AgrisTheme.Canvas;

        var content = LoadContent();

        var textBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = AgrisTheme.Surface,
            ForeColor = AgrisTheme.TextPrimary,
            Font = AgrisTheme.Body(10.5F),
            Padding = new Padding(24, 16, 24, 16),
            ScrollBars = RichTextBoxScrollBars.Vertical,
            WordWrap = true,
            DetectUrls = false,
            AccessibleName = "Hướng dẫn sử dụng"
        };

        // RichTextBox không render markdown, nhưng nội dung vẫn dễ đọc dạng text thuần.
        textBox.Text = content;

        // Tô đậm các dòng tiêu đề bắt đầu bằng #.
        HighlightHeadings(textBox);

        var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(1) };
        card.Controls.Add(textBox);

        Controls.Add(card);
    }

    private static string LoadContent()
    {
        // Tìm file docs/HUONG_DAN_SU_DUNG.md theo đường dẫn tương đối từ thư mục chạy.
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "docs", "HUONG_DAN_SU_DUNG.md"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "HUONG_DAN_SU_DUNG.md"),
            Path.Combine(Directory.GetCurrentDirectory(), "docs", "HUONG_DAN_SU_DUNG.md")
        };

        foreach (var path in candidates)
        {
            if (!File.Exists(path)) continue;

            try
            {
                return File.ReadAllText(path);
            }
            catch
            {
                // Tiếp tục thử đường dẫn khác.
            }
        }

        return EmbeddedGuide;
    }

    private static void HighlightHeadings(RichTextBox rtb)
    {
        var headingFont = AgrisTheme.Body(12F, FontStyle.Bold);
        var headingColor = AgrisTheme.BrandGreenDark;

        for (var i = 0; i < rtb.Lines.Length; i++)
        {
            var line = rtb.Lines[i];

            if (!line.StartsWith('#'))
                continue;

            var start = rtb.GetFirstCharIndexFromLine(i);
            rtb.Select(start, line.Length);
            rtb.SelectionFont = headingFont;
            rtb.SelectionColor = headingColor;
        }

        rtb.Select(0, 0);
    }

    private const string EmbeddedGuide = """
        Hướng dẫn sử dụng — TTC Hệ thống cân hợp nhất mía và vật tư

        LUỒNG VẬN HÀNH MỘT CHUYẾN NHẬP MÍA

        1. Đăng tài và phát QR
           Menu Vận hành → Đăng tài. Chọn hồ sơ "Nhập mía", nhập chứng từ nguồn,
           biển số, bấm Đăng tài. Thiếu chứng từ → chỉ QR tạm, không cân chính thức được.

        2. Cổng vào
           Menu Vận hành → Cổng và barie. Chọn chuyến, hướng Vào, bấm Đánh giá.
           Đạt → phát lệnh mở. Xe qua → bấm Xác nhận xe đã qua.

        3. Cân cả xe có hàng
           Menu Vận hành → Cân hợp nhất. Số cân hiện lớn, checklist 8 điều kiện
           cập nhật liên tục. Đủ 8 nhóm → bấm Chốt cân.

        4. Tạp chất
           Menu Vận hành → Tạp chất và chữ đường. Nhập mẫu trước và sau,
           bấm Ghi kết quả. Chưa ghi → không cân lần 2 được.

        5. Chữ đường
           Cùng màn hình. Đang chờ kết quả thì tick Pending → chỉ cảnh báo,
           vẫn cân lần 2 được. Kết quả có thể đến sau khi xe đã ra.

        6. Cân bì xe
           Quay lại Cân hợp nhất, bấm Mở lại phiên → vai trò Tare.
           Chốt cân khi ổn định.

        7. Đối soát
           Menu Vận hành → Phiên kho và đối soát. Bấm Chạy đối soát.
           4 phép so sánh hiện kết quả riêng.

        8. Ra cổng
           Quay lại Cổng và barie, hướng Ra. Đã đạt đối soát → cho ra.

        PHÍM TẮT

        Ctrl+W        Đóng tab hiện tại
        Chuột giữa    Đóng tab (bấm lên dải tab)
        Ctrl+Tab      Chuyển tab kế tiếp

        NGUYÊN TẮC QUAN TRỌNG

        • Số cân do server quyết định, giao diện chỉ hiển thị
        • Net cân xe là chênh lệch, không mặc định là sản phẩm tinh
        • QR tạm không cân chính thức được
        • Cân lại tạo bản mới, bản cũ không bao giờ bị xóa
        • Nhập tay bắt buộc có phê duyệt và lý do
        • Ngưỡng chưa duyệt thì chặn, không đặt giá trị mặc định
        """;
}
