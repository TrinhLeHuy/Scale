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
            ColumnHeadersHeight = 58,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            RowTemplate = { Height = 42 },
            AccessibleName = accessibleName,
            AutoGenerateColumns = false
        };

        ApplyStyle(grid);

        // Cỡ chữ chỉ đặt cho lưới chuẩn — lưới tạo ngoài giữ font riêng vì chiều cao dòng/header
        // của chúng được tính theo font đó.
        grid.Font = AgrisTheme.Body();
        grid.ColumnHeadersDefaultCellStyle.Font = AgrisTheme.Body(9F, FontStyle.Bold);

        return grid;
    }

    /// <summary>Lưới đã được style — tránh gắn event handler hai lần.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DataGridView, object> _styled = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DataGridView, object> _tooltipCleaned = new();

    /// <summary>
    /// Phong cách bảng dữ liệu chuẩn: header sáng, chữ đậm màu phụ, đường kẻ ngang mảnh,
    /// dòng chọn tô xanh thương hiệu nhạt. Dùng cho cả lưới tạo ngoài GridBuilder.
    /// </summary>
    public static void ApplyStyle(DataGridView grid)
    {
        if (_styled.TryGetValue(grid, out _)) return;
        _styled.Add(grid, new object());

        grid.BackgroundColor = AgrisTheme.Surface;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.EnableHeadersVisualStyles = false;
        grid.GridColor = AgrisTheme.Border;

        // Tooltip tự động của DataGridView (hiện giá trị ô bị cắt chữ) bị "kẹt" trên màn hình
        // khi lưới được bind lại theo timer trong lúc chuột đang đứng trên ô — lỗi đã biết của WinForms.
        // Tắt mặc định; form nào cần tooltip riêng (ToolTipText) tự bật lại, và vẫn được dọn bên dưới.
        grid.ShowCellToolTips = false;
        AttachTooltipCleanup(grid);

        // Giá trị enum / bool hiện theo ngôn ngữ hiện hành thay vì tên kỹ thuật ("Registered", "True").
        // Gắn khi handle được tạo để chạy SAU các CellFormatting riêng của form (form có thể đã
        // tự đổi giá trị sang chuỗi — khi đó bỏ qua).
        var localizedFormattingAttached = false;
        grid.HandleCreated += (_, _) =>
        {
            if (localizedFormattingAttached) return;
            localizedFormattingAttached = true;
            grid.CellFormatting += (_, e) =>
            {
                if (e.RowIndex < 0 || grid.Columns[e.ColumnIndex] is not DataGridViewTextBoxColumn) return;
                switch (e.Value)
                {
                    case Enum en:
                        e.Value = Localization.Loc.Enum(en);
                        e.FormattingApplied = true;
                        break;
                    case bool b:
                        e.Value = Localization.Loc.YesNo(b);
                        e.FormattingApplied = true;
                        break;
                }
            };
        };

        var header = grid.ColumnHeadersDefaultCellStyle;
        header.BackColor = AgrisTheme.GridHeader;
        header.ForeColor = AgrisTheme.TextSecondary;
        header.Alignment = DataGridViewContentAlignment.MiddleLeft;
        header.Padding = new Padding(8, 0, 4, 0);
        header.SelectionBackColor = AgrisTheme.GridHeader;
        header.SelectionForeColor = AgrisTheme.TextSecondary;
        header.WrapMode = DataGridViewTriState.True;

        var cell = grid.DefaultCellStyle;
        cell.ForeColor = AgrisTheme.TextPrimary;
        cell.BackColor = AgrisTheme.Surface;
        cell.SelectionBackColor = AgrisTheme.PrimarySoft;
        cell.SelectionForeColor = AgrisTheme.TextPrimary;
        cell.Padding = new Padding(8, 0, 6, 0);

        grid.AlternatingRowsDefaultCellStyle.BackColor = AgrisTheme.SurfaceMuted;

        grid.RowHeadersDefaultCellStyle.BackColor = AgrisTheme.SurfaceMuted;
        grid.RowHeadersDefaultCellStyle.SelectionBackColor = AgrisTheme.PrimarySoft;

        // Header không có đường kẻ dọc — chỉ một đường đậm phân cách với dữ liệu.
        grid.CellPainting += (_, e) =>
        {
            if (e.RowIndex != -1 || e.Graphics is null) return;

            e.Paint(e.CellBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.Border);

            using var pen = new Pen(AgrisTheme.BorderStrong);
            e.Graphics.DrawLine(pen, e.CellBounds.Left, e.CellBounds.Bottom - 1,
                e.CellBounds.Right, e.CellBounds.Bottom - 1);

            e.Handled = true;
        };

        // Dòng đang chọn có vạch nhấn trái để nhận diện nhanh khi bảng dài.
        grid.RowPostPaint += (_, e) =>
        {
            if (!grid.Rows[e.RowIndex].Selected) return;

            using var accent = new SolidBrush(AgrisTheme.Primary);
            e.Graphics.FillRectangle(accent, e.RowBounds.Left, e.RowBounds.Top, 3, e.RowBounds.Height);
        };
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
    public static Label SectionHeader(string text) => new SectionHeaderLabel
    {
        Text = text,
        Dock = DockStyle.Top,
        Height = 44,
        Font = AgrisTheme.Body(10.5F, FontStyle.Bold),
        ForeColor = AgrisTheme.TextPrimary,
        Padding = new Padding(14, 0, 8, 0),
        TextAlign = ContentAlignment.MiddleLeft,
        BackColor = AgrisTheme.CardHeader,
        AutoEllipsis = true
    };

    /// <summary>Ghi chú cho nội dung chưa duyệt hoặc đang giả lập.</summary>
    public static Label Notice(string text, Color? color = null)
    {
        var fore = color ?? AgrisTheme.Warning;
        var back = fore == AgrisTheme.Danger ? AgrisTheme.DangerSoft
            : fore == AgrisTheme.Info ? AgrisTheme.InfoSoft
            : fore == AgrisTheme.Success ? AgrisTheme.SuccessSoft
            : AgrisTheme.WarningSoft;

        return new NoticeLabel
        {
            Text = text,
            Dock = DockStyle.Top,
            Height = 36,
            ForeColor = fore,
            BackColor = back,
            Font = AgrisTheme.Body(9F),
            Padding = new Padding(14, 0, 8, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
    }

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
        var button = new ModernButton
        {
            Text = text,
            Size = new Size(Math.Max(size.Width, 150), Math.Max(size.Height, 40)),
            BackColor = AgrisTheme.Primary,
            ForeColor = AgrisTheme.TextOnBrand,
            Font = AgrisTheme.Body(10F, FontStyle.Bold),
            AccessibleName = text
        };

        button.FlatAppearance.MouseOverBackColor = AgrisTheme.PrimaryHover;
        return button;
    }

    /// <summary>Nút hành động phụ: nền trắng, viền mảnh.</summary>
    public static Button SecondaryButton(string text, Size size)
    {
        var button = new ModernButton
        {
            Text = text,
            Size = new Size(Math.Max(size.Width, 130), Math.Max(size.Height, 40)),
            BackColor = AgrisTheme.Surface,
            ForeColor = AgrisTheme.TextSecondary,
            Font = AgrisTheme.Body(10F),
            AccessibleName = text
        };

        button.FlatAppearance.BorderColor = AgrisTheme.BorderStrong;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = AgrisTheme.SurfaceHover;
        return button;
    }

    /// <summary>
    /// Gắn nhóm Mía/Vật tư vào tiêu đề lưới ("Danh sách chuyến · Mía") và tô vạch nhấn thẻ theo màu nhóm,
    /// để người vận hành không nhầm đang xem lưới nào.
    /// </summary>
    public static void ApplyCategoryHeader(
        Card card, Label header, string baseTitle, TTC.Weighing.Application.Queries.TripCategory category)
    {
        card.AccentColor = TripCategorySwitch.AccentFor(category);
        header.Text = $"{baseTitle}  ·  {TripCategorySwitch.LabelFor(category)}";
        card.Invalidate();
    }

    /// <summary>Màu nhận diện Excel cho nút xuất file.</summary>
    public static readonly Color ExcelGreen = Color.FromArgb(16, 124, 65);

    /// <summary>
    /// Nút "Xuất Excel" chuẩn trên thanh công cụ lưới: nền trắng, chữ + viền xanh Excel,
    /// icon tải xuống (nếu máy có font icon Windows).
    /// </summary>
    public static Button ExportExcelButton()
    {
        var text = Localization.Loc.T("grid.export_excel");
        var button = new ModernButton
        {
            Text = text,
            Size = new Size(160, 40),
            BackColor = AgrisTheme.Surface,
            ForeColor = ExcelGreen,
            Font = AgrisTheme.Body(9.5F, FontStyle.Bold),
            AccessibleName = text,
            AccessibleDescription = Localization.Loc.T("grid.export_excel_hint"),
            IconGlyph = "\uE896" // Download (Segoe MDL2 / Fluent)
        };

        button.FlatAppearance.BorderColor = ExcelGreen;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 245, 237);
        return button;
    }

    /// <summary>Nút hành động có hậu quả khó hoàn tác (hủy phiếu, xóa...).</summary>
    public static Button DangerButton(string text, Size size)
    {
        var button = new ModernButton
        {
            Text = text,
            Size = new Size(Math.Max(size.Width, 130), Math.Max(size.Height, 40)),
            BackColor = AgrisTheme.Danger,
            ForeColor = AgrisTheme.TextOnBrand,
            Font = AgrisTheme.Body(10F, FontStyle.Bold),
            AccessibleName = text
        };

        button.FlatAppearance.MouseOverBackColor = AgrisTheme.Blend(AgrisTheme.Danger, Color.Black, 0.15f);
        return button;
    }

    /// <summary>
    /// Ẩn tooltip ô đang hiện khi dữ liệu đổi / cuộn / chuột rời lưới.
    /// Bật-tắt ShowCellToolTips là cách duy nhất buộc DataGridView đóng tooltip nội bộ của nó.
    /// </summary>
    public static void AttachTooltipCleanup(DataGridView grid)
    {
        if (_tooltipCleaned.TryGetValue(grid, out _)) return;
        _tooltipCleaned.Add(grid, new object());

        void Dismiss(object? sender, EventArgs e)
        {
            if (!grid.ShowCellToolTips || grid.IsDisposed) return;
            grid.ShowCellToolTips = false;
            grid.ShowCellToolTips = true;
        }

        grid.DataSourceChanged += Dismiss;
        grid.DataBindingComplete += (s, e) => Dismiss(s, e);
        grid.Scroll += (s, e) => Dismiss(s, e);
        grid.MouseLeave += Dismiss;
        grid.VisibleChanged += Dismiss;
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
