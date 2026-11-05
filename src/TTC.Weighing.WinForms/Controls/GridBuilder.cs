using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Controls;

/// <summary>Tạo DataGridView và các phần tử bố cục chuẩn theo theme Agris.</summary>
public static class GridBuilder
{
    /// <summary>Màu đường kẻ ô (ngang + dọc) của mọi bảng.</summary>
    public static readonly Color GridLineColor = Color.FromArgb(120, 128, 140);

    /// <summary>Đường đậm ngăn tiêu đề với dữ liệu.</summary>
    public static readonly Color GridHeaderLineColor = Color.FromArgb(70, 78, 90);

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
        // Kẻ khung đầy đủ (ngang + dọc) cho MỌI ô, mọi cột — cuộn ngang sang phần nào cũng có khung như nhau.
        grid.CellBorderStyle = DataGridViewCellBorderStyle.Single;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.EnableHeadersVisualStyles = false;
        // Đường kẻ khung bảng màu xám đậm (gần đen) — nhìn rõ trên màn hình trạm cân, không bị mờ như viền thẻ.
        grid.GridColor = GridLineColor;

        // Tooltip tự động của DataGridView (hiện giá trị ô bị cắt chữ) bị "kẹt" trên màn hình
        // khi lưới được bind lại theo timer trong lúc chuột đang đứng trên ô — lỗi đã biết của WinForms.
        // Tắt mặc định; form nào cần tooltip riêng (ToolTipText) tự bật lại, và vẫn được dọn bên dưới.
        grid.ShowCellToolTips = false;
        AttachTooltipCleanup(grid);
        EnsureSmoothAndReadable(grid);

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
                    // Mốc thời gian chưa có định dạng riêng: giờ Việt Nam, bỏ phần "+07:00" bị cắt thành "+…".
                    case DateTimeOffset dto when string.IsNullOrEmpty(e.CellStyle?.Format):
                        e.Value = Domain.Common.VnTime.ToVnTime(dto).ToString("dd/MM/yyyy HH:mm");
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

        // Tiêu đề: kẻ dọc phân cách từng cột (cùng màu đường kẻ dữ liệu, thẳng hàng với khung ô bên dưới)
        // + một đường đậm phân cách với dữ liệu. Vẽ cho MỌI ô tiêu đề mỗi lần vẽ → không có cột thiếu khung.
        grid.CellPainting += (_, e) =>
        {
            if (e.Graphics is null) return;
            // Graphics dùng chung cho cả lượt vẽ: handler khác (phễu lọc, pill trạng thái) bật khử răng cưa
            // mà quên trả lại thì mọi đường kẻ ô vẽ sau bị nhòe sang 2 điểm ảnh → nhạt màu. Mỗi ô bắt đầu nét sắc.
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Default;
            if (e.RowIndex != -1) return;

            e.Paint(e.CellBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.Border);

            using (var grid1 = new Pen(grid.GridColor))
                e.Graphics.DrawLine(grid1, e.CellBounds.Right - 1, e.CellBounds.Top,
                    e.CellBounds.Right - 1, e.CellBounds.Bottom - 1);
            using (var strong = new Pen(GridHeaderLineColor))
                e.Graphics.DrawLine(strong, e.CellBounds.Left, e.CellBounds.Bottom - 1,
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

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DataGridView, object> _smoothed = new();

    /// <summary>Khoảng chừa cho padding ô + mũi tên lọc / sắp xếp trên tiêu đề cột.</summary>
    private static int HEADER_EXTRA_PX => Theme.UiScale.Px(34);

    /// <summary>
    /// Sửa chung cho MỌI lưới (gọi một lần / lưới):
    /// <list type="bullet">
    /// <item><b>Cuộn / kéo không giật:</b> bật double-buffer cho DataGridView (thuộc tính protected → reflection),
    ///   lưới vẽ vào bộ đệm rồi mới ra màn hình, không nháy khi kéo thanh cuộn / bánh xe chuột.</item>
    /// <item><b>Không mất chữ tiêu đề:</b> mỗi cột có độ rộng tối thiểu đủ cho từ dài nhất của tiêu đề
    ///   và tiêu đề gói tối đa 2 dòng (header cao 58px). Cột Fill không bị bóp còn vài pixel —
    ///   bảng nhiều cột thì hiện thanh cuộn ngang thay vì cắt chữ.</item>
    /// <item>Giá trị dài trong ô hiện "…" (WrapMode False) thay vì bị cắt cụt giữa chữ.</item>
    /// </list>
    /// </summary>
    public static void EnsureSmoothAndReadable(DataGridView grid)
    {
        if (_smoothed.TryGetValue(grid, out _)) return;
        _smoothed.Add(grid, new object());

        typeof(DataGridView).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(grid, true);

        // Ô dữ liệu một dòng: chữ dài → "…" ở cuối, không vỡ dòng đè lên dòng dưới.
        if (grid.DefaultCellStyle.WrapMode == DataGridViewTriState.NotSet)
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;

        void FitAll()
        {
            if (grid.IsDisposed) return;
            foreach (DataGridViewColumn col in grid.Columns) FitHeader(grid, col);
        }

        grid.ColumnAdded += (_, e) => FitHeader(grid, e.Column);
        grid.ColumnHeaderCellChanged += (_, e) => FitHeader(grid, e.Column);
        grid.HandleCreated += (_, _) => FitAll();
        grid.FontChanged += (_, _) => FitAll();
        // Tiêu đề đổi theo ngôn ngữ / luồng (vd "Shipment" ↔ "Mã lệnh đốn") + dữ liệu mới → đo lại sau khi bind.
        // Đo theo nội dung sau MỌI cách nạp dữ liệu: bind DataSource, Rows.Add (lưới CCS), lưới nằm trong tab
        // ẩn chỉ tạo dòng khi hiện ra, sắp xếp lại. Gộp nhiều sự kiện liền nhau thành một lần đo.
        var fitPending = false;
        void ScheduleFit()
        {
            if (fitPending || grid.IsDisposed) return;
            if (!grid.IsHandleCreated) return; // HandleCreated sẽ gọi lại
            fitPending = true;
            grid.BeginInvoke(() =>
            {
                fitPending = false;
                FitAll();
                FitContent(grid);
            });
        }
        grid.DataBindingComplete += (_, _) => ScheduleFit();
        grid.RowsAdded += (_, _) => ScheduleFit();
        grid.Sorted += (_, _) => ScheduleFit();
        grid.VisibleChanged += (_, _) => { if (grid.Visible) ScheduleFit(); };
        grid.HandleCreated += (_, _) => ScheduleFit();
        FitAll();

        // Sọc / vệt / thiếu đường kẻ khi cuộn: DataGridView cuộn bằng cách chép điểm ảnh cũ (ScrollWindow),
        // phần tự vẽ (vạch dòng chọn, đường kẻ dưới tiêu đề) bị nhân bản hoặc mất đoạn.
        // Vẽ lại toàn lưới SAU khi bước cuộn hoàn tất (BeginInvoke) — lưới đã double-buffer nên không nháy.
        void RepaintAfterScroll()
        {
            if (grid.IsDisposed || !grid.IsHandleCreated) return;
            grid.BeginInvoke(() => { if (!grid.IsDisposed) grid.Invalidate(true); });
        }
        grid.Scroll += (_, _) => RepaintAfterScroll();
        grid.MouseWheel += (_, _) => RepaintAfterScroll();
        grid.ColumnWidthChanged += (_, _) => RepaintAfterScroll();
        grid.SizeChanged += (_, _) => RepaintAfterScroll();

        // Đường kẻ dưới tiêu đề vẽ liền một nét trên toàn bề ngang (không phụ thuộc từng ô tiêu đề được vẽ lại).
        grid.Paint += (_, e) =>
        {
            if (!grid.ColumnHeadersVisible) return;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            using var pen = new Pen(GridHeaderLineColor);
            var y = grid.ColumnHeadersHeight - 1;
            e.Graphics.DrawLine(pen, 0, y, grid.ClientSize.Width, y);
        };
    }

    /// <summary>Số dòng tối đa đem đo khi tính độ rộng theo nội dung (đủ cho một trang lưới, không chậm).</summary>
    private const int CONTENT_SAMPLE_ROWS = 120;

    /// <summary>Độ rộng tối đa một cột được giãn theo nội dung (chữ dài hơn nữa thì "…").</summary>
    private static int CONTENT_MAX_PX => Theme.UiScale.Px(380);

    /// <summary>
    /// Không mất chữ trong ô: cột tối thiểu rộng bằng giá trị dài nhất đang hiện (tối đa 380px).
    /// Chỉ TĂNG MinimumWidth (không co lại mỗi lần bind) để lưới tự làm mới theo timer không bị nhảy cột.
    /// Bảng rộng hơn khung → thanh cuộn ngang thay vì cắt "LD2526-0…".
    /// </summary>
    private static void FitContent(DataGridView grid)
    {
        if (grid.IsDisposed || grid.Rows.Count == 0) return;
        var font = grid.DefaultCellStyle.Font ?? grid.Font;
        var rows = Math.Min(grid.Rows.Count, CONTENT_SAMPLE_ROWS);

        foreach (DataGridViewColumn col in grid.Columns)
        {
            if (!col.Visible || col is not DataGridViewTextBoxColumn) continue;
            var colFont = col.DefaultCellStyle.Font ?? font;
            var widest = 0;
            for (var r = 0; r < rows; r++)
            {
                if (grid.Rows[r].Cells[col.Index].FormattedValue is not string s || s.Length == 0) continue;
                widest = Math.Max(widest, TextRenderer.MeasureText(s, colFont).Width);
                if (widest >= CONTENT_MAX_PX) break;
            }
            var cellPad = (col.DefaultCellStyle.Padding != Padding.Empty ? col.DefaultCellStyle.Padding : grid.DefaultCellStyle.Padding).Horizontal;
            // +8%: ô có thể tô đậm (dòng chọn, trạng thái) — chữ đậm rộng hơn font đo.
            var needed = Math.Min((int)(widest * 1.08) + cellPad + 10, CONTENT_MAX_PX);
            if (widest > 0 && col.MinimumWidth < needed)
                col.MinimumWidth = needed;
        }
    }

    /// <summary>
    /// Độ rộng tối thiểu của cột = max(từ dài nhất, nửa tiêu đề) + chừa mũi tên lọc —
    /// tiêu đề luôn đọc được trọn vẹn trong 2 dòng. Không giảm MinimumWidth form đã đặt lớn hơn.
    /// </summary>
    private static void FitHeader(DataGridView grid, DataGridViewColumn col)
    {
        if (!col.Visible || string.IsNullOrWhiteSpace(col.HeaderText)) return;
        if (col is DataGridViewCheckBoxColumn or DataGridViewImageColumn) return;

        var font = col.HeaderCell.Style.Font ?? grid.ColumnHeadersDefaultCellStyle.Font ?? grid.Font;
        var words = col.HeaderText.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int W(IEnumerable<string> ws) => TextRenderer.MeasureText(string.Join(' ', ws), font).Width;

        // Bề rộng nhỏ nhất để tiêu đề nằm gọn trong 2 dòng, ngắt đúng chỗ khoảng trắng
        // ("Số đăng tài" → "Số đăng / tài", không thành 3 dòng bị cắt mất dòng cuối).
        var best = W(words);
        // Tiêu đề một dòng (lưới Dữ liệu FRM / ERP tắt gói dòng) → cần đủ cả câu, không chia đôi.
        var wrap = (col.HeaderCell.Style.WrapMode is var m && m != DataGridViewTriState.NotSet
            ? m : grid.ColumnHeadersDefaultCellStyle.WrapMode) != DataGridViewTriState.False;
        if (wrap)
            for (var i = 1; i < words.Length; i++)
                best = Math.Min(best, Math.Max(W(words.Take(i)), W(words.Skip(i))));
        var pad = grid.ColumnHeadersDefaultCellStyle.Padding.Horizontal;
        var needed = best + Math.Max(HEADER_EXTRA_PX, pad + 18);

        if (col.MinimumWidth < needed)
            col.MinimumWidth = Math.Min(needed, 420);
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
        // Lưới nạp lại bằng Rows.Clear()/Rows.Add (vd nạp lại mỗi 3s theo dữ liệu kéo nền) không qua DataSource →
        // ô đang hiện tooltip bị xóa, tooltip mồ côi đứng yên trên màn hình. Xóa dòng là đóng tooltip.
        grid.RowsRemoved += (s, e) => Dismiss(s, e);
        grid.Scroll += (s, e) => Dismiss(s, e);
        grid.MouseLeave += Dismiss;
        grid.Leave += Dismiss;
        grid.VisibleChanged += Dismiss;
        grid.Disposed += Dismiss;

        // Chuyển sang cửa sổ khác / popup mở ra khi tooltip đang hiện → đóng luôn.
        grid.HandleCreated += (_, _) =>
        {
            if (grid.FindForm() is { } form) form.Deactivate += Dismiss;
        };
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
