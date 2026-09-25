using TTC.Weighing.WinForms.Controls;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Forms;

/// <summary>Hộp thoại xem nội dung dạng văn bản: audit trail, phiếu, payload đã che dữ liệu.</summary>
public sealed class TextPreviewForm : Form
{
    public TextPreviewForm(string content, string title, string? notice = null)
    {
        Text = title;
        Size = new Size(760, 600);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = AgrisTheme.Canvas;
        MinimizeBox = false;

        var textBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            Font = AgrisTheme.Mono(9.5F),
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            BackColor = AgrisTheme.Surface,
            ForeColor = AgrisTheme.TextPrimary,
            BorderStyle = BorderStyle.None,
            Text = content,
            AccessibleName = title
        };

        var body = new Card { Dock = DockStyle.Fill, Padding = new Padding(10) };
        body.Controls.Add(textBox);

        Controls.Add(body);

        if (notice is not null)
            Controls.Add(GridBuilder.Notice(notice));
    }
}
