using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Controls;

/// <summary>
/// Tạo bố cục biểu mẫu và thanh hành động bằng TableLayoutPanel / FlowLayoutPanel.
/// Không dùng toạ độ tuyệt đối để tránh nút và ô nhập chồng nhau khi đổi font hoặc DPI.
/// </summary>
public static class FormLayout
{
    private const int LabelColumnWidth = 200;

    /// <summary>Chiều cao một dòng nhãn + ô nhập. Công khai để các form tính chiều cao panel con.</summary>
    public const int RowHeight = 58;

    /// <summary>
    /// Chiều cao chuẩn cho hàng chứa thanh nút hành động. Đủ chỗ cho nút cao + khoảng đệm
    /// của FlowLayoutPanel và Card bao ngoài, tránh nút bị cắt mép dưới.
    /// </summary>
    public const int ActionRowHeight = 70;

    /// <summary>Chiều cao tối thiểu để một bảng dữ liệu còn hiển thị được vài dòng.</summary>
    private const int MinimumFillHeight = 170;

    /// <summary>Chiều cao cần thiết cho một panel con chứa <paramref name="fieldCount"/> dòng nhập.</summary>
    public static int FieldPanelHeight(int fieldCount) => fieldCount * RowHeight + 12;

    /// <summary>Bảng hai cột: nhãn bên trái, ô nhập giãn theo chiều ngang.</summary>
    public static TableLayoutPanel FieldTable()
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            BackColor = AgrisTheme.Surface,
            Padding = new Padding(2, 2, 4, 2)
        };

        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LabelColumnWidth));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        return table;
    }

    /// <summary>Thêm một dòng nhãn kèm ô nhập.</summary>
    public static void AddField(TableLayoutPanel table, string caption, Control field)
    {
        var row = table.RowCount;
        table.RowCount = row + 1;
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, RowHeight));

        table.Controls.Add(FieldCaption(caption), 0, row);

        if (field is TextBox or ComboBox)
        {
            field.Dock = DockStyle.Fill;
            field.Margin = new Padding(0, 4, 0, 4);
        }
        else
        {
            field.Margin = new Padding(0, 5, 0, 4);
        }

        field.Font = AgrisTheme.Body(11F);

        // Nổi bật ô nhập liệu — viền + nền sáng cho editable, nền xám cho readonly
        if (field is TextBox txt)
        {
            if (!txt.ReadOnly)
            {
                txt.BorderStyle = BorderStyle.FixedSingle;
                txt.BackColor = Color.FromArgb(255, 255, 248); // Vàng nhạt — nổi bật "cần nhập"
            }
            else
            {
                txt.BackColor = AgrisTheme.ReadOnlyBg;
                txt.ForeColor = AgrisTheme.ReadOnlyText;
            }
        }
        else if (field is ComboBox cbo)
        {
            cbo.FlatStyle = FlatStyle.Flat;
        }
        else if (field is NumericUpDown num)
        {
            num.BorderStyle = BorderStyle.FixedSingle;
            if (!num.ReadOnly)
                num.BackColor = Color.FromArgb(255, 255, 248);
        }

        if (string.IsNullOrEmpty(field.AccessibleName))
            field.AccessibleName = caption;

        table.Controls.Add(field, 1, row);
    }

    /// <summary>Thêm một dòng chỉ có nội dung, chiếm cả hai cột.</summary>
    public static void AddFullWidth(TableLayoutPanel table, Control content, int height = RowHeight)
    {
        var row = table.RowCount;
        table.RowCount = row + 1;
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, height));

        content.Margin = new Padding(0, 4, 0, 4);

        table.Controls.Add(content, 0, row);
        table.SetColumnSpan(content, 2);
    }

    /// <summary>Nhãn của một ô nhập. Cho phép xuống dòng khi tên dài.</summary>
    public static Label FieldCaption(string caption) => new()
    {
        Text = caption,
        Dock = DockStyle.Fill,
        Font = AgrisTheme.Body(10F),
        ForeColor = AgrisTheme.TextMuted,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(4, 0, 6, 0),
        AutoEllipsis = true
    };

    /// <summary>
    /// Thanh nút xếp ngang tự giãn. Nút tự tính chiều rộng theo nội dung nên không bị chồng.
    /// </summary>
    public static FlowLayoutPanel ActionBar(params Button[] buttons)
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = false,
            BackColor = AgrisTheme.Surface,
            Padding = new Padding(8, 5, 8, 3)
        };

        foreach (var button in buttons)
        {
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.MinimumSize = new Size(90, 40);
            button.Padding = new Padding(14, 0, 14, 0);
            button.Margin = new Padding(0, 0, 6, 2);
            bar.Controls.Add(button);
        }

        return bar;
    }

    /// <summary>
    /// Bảng bố cục dọc, mỗi dòng một khối nội dung.
    /// SizeType.AutoSize được quy đổi thành chiều cao tuyệt đối theo chiều cao thật của thẻ,
    /// vì TableLayoutPanel không đo được Panel lồng nhiều lớp docked một cách tin cậy.
    /// </summary>
    public static TableLayoutPanel VerticalStack(params (Control Content, SizeType Type, float Size)[] rows)
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = rows.Length,
            BackColor = AgrisTheme.Canvas,
            Padding = new Padding(8, 6, 8, 6)
        };

        var requiredHeight = table.Padding.Vertical;

        for (var i = 0; i < rows.Length; i++)
        {
            var (content, type, size) = rows[i];

            content.Margin = new Padding(0, 0, 0, 10);

            if (type == SizeType.AutoSize)
            {
                type = SizeType.Absolute;
                size = content.Height + content.Margin.Vertical;
                content.Dock = DockStyle.Top;

                requiredHeight += (int)size;
            }
            else if (type == SizeType.Absolute)
            {
                content.Dock = DockStyle.Fill;
                requiredHeight += (int)size + content.Margin.Vertical;
            }
            else
            {
                content.Dock = DockStyle.Fill;

                // Khối co giãn cần một chiều cao tối thiểu để bảng dữ liệu còn dùng được.
                requiredHeight += MinimumFillHeight + content.Margin.Vertical;
            }

            table.RowStyles.Add(new RowStyle(type, size));
            table.Controls.Add(content, 0, i);
        }

        // Màn hình nhỏ hơn mức này thì vùng chứa phải cuộn, không được co nội dung tới mức bị cắt.
        table.MinimumSize = new Size(0, requiredHeight);

        return table;
    }

    /// <summary>
    /// Thẻ có tiêu đề khu vực. Nếu nội dung là biểu mẫu neo Top thì thẻ tự tính chiều cao,
    /// còn nếu là bảng dữ liệu thì thẻ giãn hết chỗ được cấp.
    /// </summary>
    public static Card TitledCard(string title, Control content, string? notice = null, bool autoScroll = false)
    {
        var card = new Card { Padding = new Padding(1), AutoScroll = autoScroll };

        var header = GridBuilder.SectionHeader(title);
        var noticeLabel = notice is null ? null : GridBuilder.Notice(notice);

        var isForm = content.Dock == DockStyle.Top;

        if (!isForm)
            content.Dock = DockStyle.Fill;

        card.Controls.Add(content);

        if (noticeLabel is not null)
            card.Controls.Add(noticeLabel);

        card.Controls.Add(header);

        if (isForm)
        {
            // Chiều cao xác định từ các phần đã biết, không phụ thuộc AutoSize của WinForms.
            card.Height = header.Height
                + (noticeLabel?.Height ?? 0)
                + MeasureContentHeight(content)
                + card.Padding.Vertical;

            card.Dock = DockStyle.Top;
        }
        else
        {
            card.Dock = DockStyle.Fill;
        }

        return card;
    }

    /// <summary>Chiều cao của một biểu mẫu dựng bằng FieldTable.</summary>
    private static int MeasureContentHeight(Control content)
    {
        if (content is not TableLayoutPanel table)
            return content.Height;

        var rowsHeight = table.RowStyles
            .Cast<RowStyle>()
            .Sum(style => style.SizeType == SizeType.Absolute ? style.Height : RowHeight);

        return (int)rowsHeight + table.Padding.Vertical;
    }
}
