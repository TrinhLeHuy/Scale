using TTC.Weighing.WinForms.Controls;

namespace TTC.Weighing.WinForms.Theme;

/// <summary>
/// Chuẩn hóa màu và font cho toàn bộ cây control của một màn hình.
/// Dùng một lần khi mở tab để mọi form nhất quán mà không phải đặt màu rải rác trong từng form.
/// Chỉ ghi đè giá trị "mặc định hệ thống" — màu form tự đặt có chủ đích được giữ nguyên.
/// </summary>
public static class ThemeApplier
{
    /// <summary>Các control tự vẽ, giữ nguyên màu riêng của chúng.</summary>
    private static bool IsSelfStyled(Control control) => control
        is GradientBar or MetricTile or PipelineStageBox or ModernButton or ChipLabel
        or BrandBarButton or WeighDisplayControl or CommitChecklistControl or FieldTile
        or DocumentTabControl;

    /// <summary>Gán vào <c>TextBox.Tag</c> để theme không ép viền FixedSingle (ô đã có khung viền riêng).</summary>
    public const string KeepBorderTag = "theme:keep-border";

    public static void Apply(Control root)
    {
        if (root is Form form && form.BackColor != AgrisTheme.ScaleDisplayBack)
            form.BackColor = AgrisTheme.Canvas;

        root.SuspendLayout();
        try
        {
            Walk(root);
        }
        finally
        {
            root.ResumeLayout(true);
        }
    }

    private static void Walk(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            if (IsSelfStyled(control))
                continue;

            Style(control);

            // Lưới dữ liệu không có con cần style.
            if (control is DataGridView)
                continue;

            if (control.HasChildren)
                Walk(control);
        }
    }

    private static void Style(Control control)
    {
        switch (control)
        {
            case DataGridView grid:
                // Lưới tạo ngoài GridBuilder vẫn có header kiểu hệ thống → đưa về chuẩn chung.
                if (grid.EnableHeadersVisualStyles)
                    GridBuilder.ApplyStyle(grid);
                GridBuilder.AttachTooltipCleanup(grid);
                GridBuilder.EnsureSmoothAndReadable(grid);
                break;

            case Button button:
                StyleButton(button);
                break;

            case CheckBox checkBox:
                checkBox.Font = AgrisTheme.Body();
                checkBox.ForeColor = AgrisTheme.TextPrimary;
                checkBox.Cursor = Cursors.Hand;
                break;

            case RadioButton radio:
                radio.Font = AgrisTheme.Body();
                radio.ForeColor = AgrisTheme.TextPrimary;
                radio.Cursor = Cursors.Hand;
                break;

            case TextBox textBox:
                StyleInput(textBox);
                // Ô nằm trong khung viền tự vẽ (đánh dấu KeepBorderTag) giữ nguyên không viền.
                if (!Equals(textBox.Tag, KeepBorderTag))
                    textBox.BorderStyle = BorderStyle.FixedSingle;
                if (textBox.ReadOnly && textBox.BackColor == AgrisTheme.Surface)
                {
                    textBox.BackColor = AgrisTheme.ReadOnlyBg;
                    textBox.ForeColor = AgrisTheme.ReadOnlyText;
                }
                break;

            case ComboBox comboBox:
                StyleInput(comboBox);
                comboBox.FlatStyle = FlatStyle.Flat;
                break;

            case NumericUpDown numeric:
                StyleInput(numeric);
                numeric.BorderStyle = BorderStyle.FixedSingle;
                FieldTile.DisableIme(numeric); // ô số không cần bộ gõ tiếng Việt (VNI giữ chữ số trong khung soạn thảo)
                numeric.TextAlign = HorizontalAlignment.Right;
                break;

            case DateTimePicker picker:
                picker.Font = AgrisTheme.Body(10F);
                picker.CalendarForeColor = AgrisTheme.TextPrimary;
                picker.CalendarTitleBackColor = AgrisTheme.Primary;
                picker.CalendarTitleForeColor = AgrisTheme.TextOnBrand;
                break;

            case ListBox listBox:
                listBox.Font = AgrisTheme.Body();
                listBox.BorderStyle = BorderStyle.None;
                listBox.BackColor = AgrisTheme.Surface;
                listBox.ForeColor = AgrisTheme.TextPrimary;
                break;

            case TabControl tabControl:
                tabControl.Font = AgrisTheme.Body();
                break;

            case GroupBox groupBox:
                groupBox.ForeColor = AgrisTheme.TextSecondary;
                if (IsSystemFont(groupBox.Font))
                    groupBox.Font = AgrisTheme.Body(9.5F, FontStyle.Bold);
                if (groupBox.BackColor == SystemColors.Control)
                    groupBox.BackColor = AgrisTheme.Surface;
                break;

            case Label label:
                StyleLabel(label);
                break;

            case Panel panel:
                // Panel không đặt màu riêng thì lấy nền bề mặt.
                if (panel.BackColor == SystemColors.Control)
                    panel.BackColor = AgrisTheme.Surface;
                // Khung cuộn được: vẽ qua bộ đệm để kéo / lăn chuột không giật.
                if (panel.AutoScroll)
                    ScrollSmoothing.Apply(panel);
                break;
        }
    }

    /// <summary>
    /// Font cũ kiểu WinForms cổ điển. Không so với Control.DefaultFont (Segoe UI 9pt trên .NET 8)
    /// vì nhiều nhãn đặt toạ độ tuyệt đối dựa trên cỡ chữ mặc định đó.
    /// </summary>
    private static bool IsSystemFont(Font font)
        => font.FontFamily.Name is "Microsoft Sans Serif";

    private static void StyleButton(Button button)
    {
        // Nút form tự đặt FlatStyle.Flat (đã có màu riêng) thì giữ nguyên, chỉ thêm con trỏ tay.
        if (button.FlatStyle == FlatStyle.Flat)
        {
            button.Cursor = Cursors.Hand;
            return;
        }

        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = AgrisTheme.Surface;
        button.ForeColor = AgrisTheme.TextSecondary;
        button.Font = AgrisTheme.Body(9.5F);
        button.UseVisualStyleBackColor = false;
        button.Cursor = Cursors.Hand;
        button.FlatAppearance.BorderColor = AgrisTheme.BorderStrong;
        button.FlatAppearance.MouseOverBackColor = AgrisTheme.SurfaceHover;
        button.FlatAppearance.MouseDownBackColor = AgrisTheme.Border;

        if (string.IsNullOrEmpty(button.AccessibleName))
            button.AccessibleName = button.Text;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, object> FontKept = new();

    /// <summary>Ô / nút được form chủ động làm to (ô tìm lệnh đốn, biển số) — theme không ép về cỡ chữ chung.</summary>
    public static void KeepFont(Control control)
    {
        if (!FontKept.TryGetValue(control, out _)) FontKept.Add(control, new object());
    }

    private static void StyleInput(Control control)
    {
        // Giữ hành vi cũ: mọi ô nhập cùng một cỡ chữ để bố cục các form không lệch nhau.
        if (!FontKept.TryGetValue(control, out _))
            control.Font = AgrisTheme.Body(10F);

        if (control.BackColor == SystemColors.Window || control.BackColor == AgrisTheme.Surface)
            control.BackColor = AgrisTheme.InputBg;

        if (control.ForeColor == SystemColors.WindowText || control.ForeColor == SystemColors.ControlText)
            control.ForeColor = AgrisTheme.TextPrimary;
    }

    private static void StyleLabel(Label label)
    {
        // Nhãn phụ dùng màu xám nhạt, nhãn giá trị giữ màu chữ chính.
        if (label.ForeColor == Color.DimGray || label.ForeColor == Color.Gray
            || label.ForeColor == SystemColors.GrayText)
            label.ForeColor = AgrisTheme.TextMuted;
        else if (label.ForeColor == SystemColors.ControlText)
            label.ForeColor = AgrisTheme.TextPrimary;

        if (IsSystemFont(label.Font))
            label.Font = AgrisTheme.Body();
    }
}
