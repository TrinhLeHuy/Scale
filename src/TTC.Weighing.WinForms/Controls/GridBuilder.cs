using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Controls;

/// <summary>Tạo DataGridView và các phần tử bố cục chuẩn theo theme Agris.</summary>
public static class GridBuilder
{
    public static DataGridView ReadOnlyGrid(string accessibleName)
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AllowUserToOrderColumns = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            BackgroundColor = AgrisTheme.Surface,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            EnableHeadersVisualStyles = false,
            ColumnHeadersHeight = 58,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            RowTemplate = { Height = 42 },
            Font = AgrisTheme.Body(),
            AccessibleName = accessibleName,
            AutoGenerateColumns = false
        };

        grid.ColumnHeadersDefaultCellStyle.BackColor = AgrisTheme.BrandGreenDark;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = AgrisTheme.TextOnBrand;
        grid.ColumnHeadersDefaultCellStyle.Font = AgrisTheme.Body(9F, FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(4, 0, 0, 0);
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = AgrisTheme.BrandGreenDark;
        grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;

        grid.DefaultCellStyle.ForeColor = AgrisTheme.TextPrimary;
        grid.DefaultCellStyle.BackColor = AgrisTheme.Surface;
        grid.DefaultCellStyle.SelectionBackColor = AgrisTheme.InfoSoft;
        grid.DefaultCellStyle.SelectionForeColor = AgrisTheme.TextPrimary;
        grid.DefaultCellStyle.Padding = new Padding(6, 0, 4, 0);

        grid.AlternatingRowsDefaultCellStyle.BackColor = AgrisTheme.SurfaceMuted;
        grid.GridColor = AgrisTheme.Border;

        return grid;
    }

    public static DataGridViewTextBoxColumn Column(
        string header, string propertyName, int fillWeight = 100, string? format = null)
    {
        var column = new DataGridViewTextBoxColumn
        {
            HeaderText = header,
            DataPropertyName = propertyName,
            Name = propertyName,
            FillWeight = fillWeight,
            SortMode = DataGridViewColumnSortMode.Automatic
        };

        if (format is not null)
        {
            column.DefaultCellStyle.Format = format;

            if (format.StartsWith('N'))
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        }

        return column;
    }

    /// <summary>Tiêu đề khu vực trong một màn hình.</summary>
    public static Label SectionHeader(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Top,
        Height = 42,
        Font = AgrisTheme.Body(11F, FontStyle.Bold),
        ForeColor = AgrisTheme.BrandGreenDark,
        Padding = new Padding(10, 9, 8, 0),
        BackColor = AgrisTheme.SurfaceMuted
    };

    /// <summary>Ghi chú cho nội dung chưa duyệt hoặc đang giả lập.</summary>
    public static Label Notice(string text, Color? color = null) => new()
    {
        Text = text,
        Dock = DockStyle.Top,
        Height = 36,
        ForeColor = color ?? AgrisTheme.Warning,
        BackColor = AgrisTheme.WarningSoft,
        Font = AgrisTheme.Body(9F),
        Padding = new Padding(10, 8, 8, 0)
    };

    public static Label Caption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = AgrisTheme.Body(9F),
        ForeColor = AgrisTheme.TextMuted
    };

    public static Label Caption(string text, int x, int y)
    {
        var label = Caption(text);
        label.Location = new Point(x, y);
        return label;
    }

    /// <summary>Nút hành động chính, dùng màu thương hiệu.</summary>
    public static Button PrimaryButton(string text, Size size)
    {
        var button = new Button
        {
            Text = text,
            Size = new Size(Math.Max(size.Width, 150), Math.Max(size.Height, 40)),
            FlatStyle = FlatStyle.Flat,
            BackColor = AgrisTheme.BrandGreen,
            ForeColor = AgrisTheme.TextOnBrand,
            Font = AgrisTheme.Body(10.5F, FontStyle.Bold),
            UseVisualStyleBackColor = false,
            AccessibleName = text
        };

        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = AgrisTheme.BrandGreenDark;

        // Nút bị vô hiệu phải nhìn rõ là không bấm được.
        button.EnabledChanged += (_, _) =>
        {
            button.BackColor = button.Enabled ? AgrisTheme.BrandGreen : AgrisTheme.Border;
            button.ForeColor = button.Enabled ? AgrisTheme.TextOnBrand : AgrisTheme.TextMuted;
        };

        return button;
    }

    /// <summary>Nút hành động phụ, viền mảnh trên nền trắng.</summary>
    public static Button SecondaryButton(string text, Size size)
    {
        var button = new Button
        {
            Text = text,
            Size = new Size(Math.Max(size.Width, 130), Math.Max(size.Height, 38)),
            FlatStyle = FlatStyle.Flat,
            BackColor = AgrisTheme.Surface,
            ForeColor = AgrisTheme.TextPrimary,
            Font = AgrisTheme.Body(10F),
            UseVisualStyleBackColor = false,
            AccessibleName = text
        };

        button.FlatAppearance.BorderColor = AgrisTheme.Border;
        button.FlatAppearance.MouseOverBackColor = AgrisTheme.SurfaceMuted;

        return button;
    }

    /// <summary>Tô màu dòng theo trạng thái để người vận hành nhận diện nhanh.</summary>
    public static void ApplyStatusColor(DataGridViewRow row, string? status)
    {
        var background = AgrisTheme.StatusBackground(status);

        if (background == AgrisTheme.Surface) return;

        var foreground = AgrisTheme.StatusForeground(status);
        var style = row.DefaultCellStyle;

        // Chỉ gán khi giá trị thực sự thay đổi — tránh trigger repaint thừa
        if (style.BackColor != background)
            style.BackColor = background;
        if (style.ForeColor != foreground)
            style.ForeColor = foreground;
    }

    /// <summary>
    /// Gán DataSource cho DataGridView và tô màu hàng loạt mà không gây flicker.
    /// Dùng thay cho pattern: grid.DataSource = list; foreach row ApplyStatusColor.
    /// </summary>
    public static void BindAndColorize<T>(
        DataGridView grid,
        IReadOnlyList<T> data,
        Func<T, string?> statusSelector)
    {
        try
        {
            grid.SuspendLayout();

            grid.DataSource = data is List<T> list ? list : data.ToList();

            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.DataBoundItem is T item)
                    ApplyStatusColor(row, statusSelector(item));
            }
        }
        finally
        {
            grid.ResumeLayout();
        }
    }

    /// <summary>Hiển thị dòng chữ khi bảng không có dữ liệu, thay vì để trống trơn.</summary>
    public static void AttachEmptyState(DataGridView grid, string message)
    {
        grid.Paint += (_, e) =>
        {
            if (grid.Rows.Count > 0) return;

            var area = new Rectangle(0, grid.ColumnHeadersHeight + 12, grid.Width, 40);

            TextRenderer.DrawText(e.Graphics, message, AgrisTheme.Body(9.5F, FontStyle.Italic),
                area, AgrisTheme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
    }
}
