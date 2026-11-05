using System.Drawing;
using System.Drawing.Imaging;
using TTC.Weighing.WinForms.Theme;

// So sánh chữ vẽ thẳng lên màn hình vs vẽ qua ảnh 32bppRgb rồi dán.
ApplicationConfiguration.Initialize();
var form = new Form { Width = 700, Height = 200, BackColor = Color.White, StartPosition = FormStartPosition.Manual, Location = new Point(50, 50) };
var font = AgrisTheme.Ui(10F, FontStyle.Bold);
const string s = "20261029-005  LÊ THANH DUY TUẤN · LD2526";
form.Paint += (_, e) =>
{
    TextRenderer.DrawText(e.Graphics, s, font, new Rectangle(10, 10, 600, 30), Color.FromArgb(0, 133, 62), Color.White,
        TextFormatFlags.Left | TextFormatFlags.NoPadding);
    foreach (var (fmt, y) in new[] { (PixelFormat.Format32bppRgb, 60), (PixelFormat.Format32bppArgb, 110) })
    {
        using var bmp = new Bitmap(600, 30, fmt);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            TextRenderer.DrawText(g, s, font, new Rectangle(0, 0, 600, 30), Color.FromArgb(0, 133, 62), Color.White,
                TextFormatFlags.Left | TextFormatFlags.NoPadding);
        }
        e.Graphics.DrawImageUnscaled(bmp, 10, y);
    }
};
form.Shown += async (_, _) =>
{
    await Task.Delay(600);
    var p = form.PointToScreen(Point.Empty);
    using var shot = new Bitmap(620, 150);
    using (var g = Graphics.FromImage(shot)) g.CopyFromScreen(p, Point.Empty, shot.Size);
    long Diff(int y2) { long d = 0; for (var y = 0; y < 30; y++) for (var x = 0; x < 600; x++) { var a = shot.GetPixel(10 + x, 10 + y); var b = shot.GetPixel(10 + x, y2 + y); d += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B); } return d; }
    File.WriteAllText("result.txt", $"diff32Rgb={Diff(60)} diff32Argb={Diff(110)}");
    form.Close();
};
Application.Run(form);

