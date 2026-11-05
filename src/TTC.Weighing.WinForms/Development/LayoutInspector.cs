using System.Text;

// Tránh nhập nhằng giữa namespace TTC.Weighing.Application và System.Windows.Forms.Application.
using WinFormsApp = System.Windows.Forms.Application;

namespace TTC.Weighing.WinForms.Development;

/// <summary>
/// Kiểm tra bố cục của một màn hình: control bị cắt khỏi vùng cha và control chồng nhau.
/// Dùng cho chẩn đoán khi phát triển, chạy bằng tham số dòng lệnh --layout.
/// </summary>
public static class LayoutInspector
{
    /// <summary>Bỏ qua chênh lệch nhỏ do bo viền và đường kẻ.</summary>
    private const int Tolerance = 2;

    /// <summary>
    /// Nhúng màn hình vào một tab giống hệt cách MainForm làm, rồi kiểm tra bố cục.
    /// Phải nhúng đúng như thật vì MinimumSize và AutoScroll quyết định việc nội dung có bị cắt.
    /// </summary>
    public static IReadOnlyList<string> Inspect(
        Form screen, Size viewport, Func<Form, Size> resolveMinimumSize)
    {
        var findings = new List<string>();

        var host = new Form
        {
            FormBorderStyle = FormBorderStyle.Sizable,
            StartPosition = FormStartPosition.Manual,
            ShowInTaskbar = false,
            Location = new Point(-6000, -6000),
            Size = viewport
        };

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var page = new TabPage("kiểm tra") { AutoScroll = true };

        screen.TopLevel = false;
        screen.FormBorderStyle = FormBorderStyle.None;
        screen.ControlBox = false;
        screen.WindowState = FormWindowState.Normal;
        screen.Dock = DockStyle.Fill;
        screen.MinimumSize = resolveMinimumSize(screen);

        page.Controls.Add(screen);
        tabs.TabPages.Add(page);
        host.Controls.Add(tabs);

        using (Theme.UiScale.DesignScope())
        {
            host.Show();
            screen.Show();
            host.PerformLayout();
        }

        // Giống MainForm.CreateAndHost: dựng + hiện ở kích thước thiết kế rồi thu phóng theo UiScale.
        Theme.UiScale.Apply(screen);
        host.PerformLayout();
        WinFormsApp.DoEvents();

        Walk(screen, screen.GetType().Name, findings);

        host.Hide();
        page.Controls.Remove(screen);
        host.Dispose();

        return findings;
    }

    /// <summary>Liệt kê kích thước các khối bố cục chính để đối chiếu khi chẩn đoán.</summary>
    public static IReadOnlyList<string> DescribeBlocks(Form form)
    {
        var lines = new List<string>();

        foreach (Control level1 in form.Controls)
        {
            lines.Add($"  {Describe(level1)}  client={level1.ClientSize.Width}x{level1.ClientSize.Height}");

            foreach (Control level2 in level1.Controls)
                lines.Add($"    {Describe(level2)}");
        }

        return lines;
    }

    private static void Walk(Control parent, string path, List<string> findings)
    {
        var clientHeight = parent.ClientSize.Height;
        var clientWidth = parent.ClientSize.Width;
        var scrolls = parent is ScrollableControl { AutoScroll: true };

        var visibleChildren = parent.Controls
            .Cast<Control>()
            .Where(c => c.Visible)
            .ToList();

        foreach (var child in visibleChildren)
        {
            var childPath = $"{path} > {Describe(child)}";

            // Cố ý, không phải lỗi: FieldTile đặt ô số rộng hơn khung để giấu nút tăng/giảm;
            // nhãn AutoSize chưa có chữ thì rộng 0.
            var intentional = (child is NumericUpDown && parent.Parent?.Parent is Controls.FieldTile)
                || (child is Label { AutoSize: true } lbl && string.IsNullOrEmpty(lbl.Text));

            if (!scrolls && !intentional)
            {
                if (child.Bottom > clientHeight + Tolerance)
                {
                    findings.Add(
                        $"CẮT DỌC   {childPath}: bottom={child.Bottom} vượt vùng cha {clientHeight}");
                }

                if (child.Right > clientWidth + Tolerance)
                {
                    findings.Add(
                        $"CẮT NGANG {childPath}: right={child.Right} vượt vùng cha {clientWidth}");
                }
            }

            if (!intentional && (child.Height <= 0 || child.Width <= 0))
                findings.Add($"KHÔNG CHỖ {childPath}: kích thước {child.Width}x{child.Height}");

            if (child.HasChildren)
                Walk(child, childPath, findings);
        }

        // Chỉ xét chồng lấn giữa các control không dùng dock, vì dock do layout engine quản lý.
        var freeChildren = visibleChildren
            .Where(c => c.Dock == DockStyle.None && parent is not TableLayoutPanel
                && parent is not FlowLayoutPanel)
            .ToList();

        for (var i = 0; i < freeChildren.Count; i++)
        {
            for (var j = i + 1; j < freeChildren.Count; j++)
            {
                var a = freeChildren[i];
                var b = freeChildren[j];

                var overlap = Rectangle.Intersect(a.Bounds, b.Bounds);

                if (overlap.Width > Tolerance && overlap.Height > Tolerance)
                {
                    findings.Add(
                        $"CHỒNG     {path}: {Describe(a)} và {Describe(b)} " +
                        $"chồng {overlap.Width}x{overlap.Height}px");
                }
            }
        }
    }

    private static string Describe(Control control)
    {
        var label = control switch
        {
            Label l => Shorten(l.Text),
            Button b => Shorten(b.Text),
            CheckBox c => Shorten(c.Text),
            TabPage p => Shorten(p.Text),
            _ => control.Name
        };

        var name = string.IsNullOrWhiteSpace(label) ? control.GetType().Name : label;

        return $"{control.GetType().Name}[{name}]@{control.Bounds.X},{control.Bounds.Y} " +
               $"{control.Width}x{control.Height}";
    }

    private static string Shorten(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var single = text.Replace(Environment.NewLine, " ").Trim();
        return single.Length <= 34 ? single : single[..34] + "…";
    }

    public static string Format(string formName, IReadOnlyList<string> findings)
    {
        if (findings.Count == 0)
            return $"OK    {formName}";

        var builder = new StringBuilder();
        builder.AppendLine($"FAIL  {formName}  ({findings.Count} vấn đề)");

        foreach (var finding in findings)
            builder.AppendLine($"        {finding}");

        return builder.ToString();
    }
}
