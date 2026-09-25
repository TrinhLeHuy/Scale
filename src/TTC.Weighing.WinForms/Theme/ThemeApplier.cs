using TTC.Weighing.WinForms.Controls;

namespace TTC.Weighing.WinForms.Theme;

/// <summary>
/// Chuẩn hóa màu và font cho toàn bộ cây control của một màn hình.
/// Dùng một lần khi mở tab để mọi form nhất quán mà không phải đặt màu rải rác trong từng form.
/// </summary>
public static class ThemeApplier
{
    /// <summary>Các control tự vẽ, giữ nguyên màu riêng của chúng.</summary>
    private static bool IsSelfStyled(Control control) => control
        is GradientBar or MetricTile or PipelineStageBox
        or WeighDisplayControl or CommitChecklistControl
        or DocumentTabControl or DataGridView;

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

            if (control.HasChildren)
                Walk(control);
        }
    }

    private static void Style(Control control)
    {
        switch (control)
        {
            case Button button:
                StyleButton(button);
                break;

            case CheckBox checkBox:
                checkBox.Font = AgrisTheme.Body();
                checkBox.ForeColor = AgrisTheme.TextPrimary;
                break;

            case TextBox textBox:
                StyleInput(textBox);
                textBox.BorderStyle = BorderStyle.FixedSingle;
                break;

            case ComboBox comboBox:
                StyleInput(comboBox);
                comboBox.FlatStyle = FlatStyle.Flat;
                break;

            case NumericUpDown numeric:
                StyleInput(numeric);
                numeric.BorderStyle = BorderStyle.FixedSingle;
                numeric.TextAlign = HorizontalAlignment.Right;
                break;

            case DateTimePicker picker:
                picker.Font = AgrisTheme.Body(10F);
                picker.CalendarForeColor = AgrisTheme.TextPrimary;
                break;

            case ListBox listBox:
                listBox.Font = AgrisTheme.Body();
                listBox.BorderStyle = BorderStyle.None;
                listBox.BackColor = AgrisTheme.Surface;
                break;

            case TabControl tabControl:
                tabControl.Font = AgrisTheme.Body();
                break;

            case Label label:
                StyleLabel(label);
                break;

            case Panel panel:
                // Panel không đặt màu riêng thì lấy nền bề mặt.
                if (panel.BackColor == SystemColors.Control)
                    panel.BackColor = AgrisTheme.Surface;
                break;
        }
    }

    private static void StyleButton(Button button)
    {
        // Nút đã tạo qua GridBuilder thì giữ nguyên.
        if (button.FlatStyle == FlatStyle.Flat) return;

        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = AgrisTheme.Surface;
        button.ForeColor = AgrisTheme.TextPrimary;
        button.Font = AgrisTheme.Body(9.5F);
        button.UseVisualStyleBackColor = false;
        button.FlatAppearance.BorderColor = AgrisTheme.Border;
        button.FlatAppearance.MouseOverBackColor = AgrisTheme.SurfaceMuted;

        if (string.IsNullOrEmpty(button.AccessibleName))
            button.AccessibleName = button.Text;
    }

    private static void StyleInput(Control control)
    {
        control.Font = AgrisTheme.Body(10F);
        control.BackColor = AgrisTheme.Surface;
        control.ForeColor = AgrisTheme.TextPrimary;
    }

    private static void StyleLabel(Label label)
    {
        // Nhãn phụ dùng màu xám nhạt, nhãn giá trị giữ màu chữ chính.
        if (label.ForeColor == Color.DimGray || label.ForeColor == SystemColors.GrayText)
            label.ForeColor = AgrisTheme.TextMuted;
        else if (label.ForeColor == SystemColors.ControlText)
            label.ForeColor = AgrisTheme.TextPrimary;

        if (label.Font.FontFamily.Name == "Microsoft Sans Serif")
            label.Font = AgrisTheme.Body();
    }
}
