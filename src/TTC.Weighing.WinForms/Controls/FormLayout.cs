using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Controls;

/// <summary>
/// Tạo bố cục biểu mẫu và thanh hành động bằng TableLayoutPanel / FlowLayoutPanel.
/// Không dùng toạ độ tuyệt đối để tránh nút và ô nhập chồng nhau khi đổi font hoặc DPI.
/// </summary>
public static class FormLayout
{
    private static int LabelColumnWidth => Theme.UiScale.Px(200);

    /// <summary>Chiều cao một dòng nhãn + ô nhập. Công khai để các form tính chiều cao panel con.</summary>
    public static int RowHeight => Theme.UiScale.Px(58);

    /// <summary>
    /// Chiều cao chuẩn cho hàng chứa thanh nút hành động. Đủ chỗ cho nút cao + khoảng đệm
    /// của FlowLayoutPanel và Card bao ngoài, tránh nút bị cắt mép dưới.
    /// </summary>
    public static int ActionRowHeight => Theme.UiScale.Px(70);

    /// <summary>Chiều cao tối thiểu để một bảng dữ liệu còn hiển thị được vài dòng.</summary>
    private static int MinimumFillHeight => Theme.UiScale.Px(170);

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

        // Ô được nhập: nền trắng, viền rõ. Ô chỉ đọc: nền xám, chữ phụ — phân biệt ngay bằng mắt.
        if (field is TextBox txt)
        {
            txt.BorderStyle = BorderStyle.FixedSingle;
            if (!txt.ReadOnly)
            {
                txt.BackColor = AgrisTheme.InputBg;
                txt.ForeColor = AgrisTheme.TextPrimary;
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
            cbo.BackColor = AgrisTheme.InputBg;
        }
        else if (field is NumericUpDown num)
        {
            num.BorderStyle = BorderStyle.FixedSingle;
            num.BackColor = num.ReadOnly ? AgrisTheme.ReadOnlyBg : AgrisTheme.InputBg;
        }

        if (string.IsNullOrEmpty(field.AccessibleName))
            field.AccessibleName = caption;

        table.Controls.Add(field, 1, row);
    }

    /// <summary>
    /// Ô nhập + nút ▼ trong CÙNG một khung viền, cao bằng một ô nhập thường (kiểu combobox):
    /// ô nhập bỏ viền riêng, căn giữa theo chiều dọc; vạch ngăn mảnh giữa ô và nút.
    /// Gọi SAU khi đã đặt font cho <paramref name="input"/> (chiều cao khung tính theo font).
    /// Thêm khung trả về vào bảng bằng <see cref="AddField"/> (giữ nguyên khung, không giãn cao cả hàng).
    /// </summary>
    public static Panel InputWithDropdown(TextBox input, Button dropdown, int buttonWidth = 36)
    {
        // Cao bằng ô nhập có viền thường cùng font → thẳng hàng với các ô khác trên form.
        input.BorderStyle = BorderStyle.FixedSingle;
        var height = input.PreferredHeight;

        var host = new Panel
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            Dock = DockStyle.None,
            Height = height,
            Padding = new Padding(1),
            BackColor = AgrisTheme.BorderStrong // màu viền chung
        };

        var inputArea = new Panel { Dock = DockStyle.Fill, BackColor = input.BackColor, Cursor = Cursors.IBeam };
        input.BorderStyle = BorderStyle.None;
        input.Tag = Theme.ThemeApplier.KeepBorderTag;
        input.Dock = DockStyle.Top;
        inputArea.Controls.Add(input);
        void Center() => inputArea.Padding = new Padding(6, Math.Max(0, (inputArea.Height - input.Height) / 2), 2, 0);
        inputArea.Resize += (_, _) => Center();
        input.SizeChanged += (_, _) => Center();
        input.BackColorChanged += (_, _) => inputArea.BackColor = input.BackColor;
        inputArea.Click += (_, _) => input.Focus();
        // Theme / đổi ngôn ngữ đổi font ô nhập → tính lại chiều cao khung cho khớp ô thường.
        input.FontChanged += (_, _) =>
        {
            using var probe = new TextBox { Font = input.Font, BorderStyle = BorderStyle.FixedSingle };
            host.Height = probe.PreferredHeight;
        };

        dropdown.Dock = DockStyle.Right;
        dropdown.Width = buttonWidth;
        dropdown.Margin = Padding.Empty;
        dropdown.FlatStyle = FlatStyle.Flat;
        dropdown.FlatAppearance.BorderSize = 0;
        dropdown.BackColor = AgrisTheme.Surface;
        dropdown.Cursor = Cursors.Hand;
        dropdown.TabStop = false;

        var separator = new Panel { Dock = DockStyle.Right, Width = 1, BackColor = AgrisTheme.BorderStrong };

        host.Controls.Add(inputArea);
        host.Controls.Add(separator);
        host.Controls.Add(dropdown);
        return host;
    }

    /// <summary>Thêm một dòng chỉ có nội dung, chiếm cả hai cột.</summary>
    public static void AddFullWidth(TableLayoutPanel table, Control content, int? height = null)
    {
        height ??= RowHeight;
        var row = table.RowCount;
        table.RowCount = row + 1;
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, height.Value));

        content.Margin = new Padding(0, 4, 0, 4);

        table.Controls.Add(content, 0, row);
        table.SetColumnSpan(content, 2);
    }

    /// <summary>Nhãn của một ô nhập. Cho phép xuống dòng khi tên dài.</summary>
    public static Label FieldCaption(string caption) => new()
    {
        Text = caption,
        Dock = DockStyle.Fill,
        Font = AgrisTheme.Body(9.5F),
        ForeColor = AgrisTheme.TextSecondary,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(10, 0, 8, 0),
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
            Padding = new Padding(10, 6, 10, 3)
        };

        foreach (var button in buttons)
        {
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.MinimumSize = new Size(90, 40);
            button.Padding = new Padding(16, 0, 16, 0);
            button.Margin = new Padding(0, 0, 8, 2);
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
            Padding = new Padding(12, 10, 12, 8)
        };

        var requiredHeight = table.Padding.Vertical;

        for (var i = 0; i < rows.Length; i++)
        {
            var (content, type, size) = rows[i];

            content.Margin = new Padding(0, 0, 0, 12);

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
