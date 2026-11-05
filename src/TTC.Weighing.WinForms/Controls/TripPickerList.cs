using System.Drawing.Drawing2D;
using System.Drawing.Text;
using TTC.Weighing.Application.Queries;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Theme;
using TTC.Weighing.Domain.Common;

namespace TTC.Weighing.WinForms.Controls;

/// <summary>
/// Danh sách chọn xe trong nhà máy (nút ▼ cạnh ô "Nhập mã đăng tài hoặc biển số"), dùng chung cho
/// Cân hợp nhất và Đo tạp chất. Mỗi xe một thẻ 2 dòng tự vẽ:
/// <code>
/// ▌ 82A-01394                                   ( 20261029-005 )
/// ▌ LÊ THANH DUY  ·  LD2526-00014650  ·  Vào 29/10 09:41
/// </code>
/// Vạch màu trái theo <see cref="Accent"/> (Cân hợp nhất: xanh = cân vào, cam = cân ra).
/// </summary>
public sealed class TripPickerList
{
    public static int ITEM_HEIGHT => Theme.UiScale.Px(64);
    private const int MAX_VISIBLE = 6;
    private static int MIN_WIDTH => Theme.UiScale.Px(560);

    private readonly ListBox _list;
    private TripRegistrationRow[] _rows = [];
    private int _hover = -1;

    /// <summary>Màu nhấn của thẻ (vạch trái, chip số đăng tài, nền khi chọn).</summary>
    public Func<Color> Accent { get; set; } = () => AgrisTheme.BrandGreen;

    public TripPickerList(ListBox list)
    {
        _list = list;
        _list.DrawMode = DrawMode.OwnerDrawFixed;
        _list.ItemHeight = ITEM_HEIGHT;
        _list.IntegralHeight = false;
        _list.BorderStyle = BorderStyle.FixedSingle;
        _list.BackColor = AgrisTheme.Surface;
        _list.Cursor = Cursors.Hand;
        _list.DrawItem += DrawItem;
        // Lia chuột giật: trước đây mỗi lần đổi dòng hover vẽ lại CẢ danh sách và xóa nền trắng trước khi vẽ.
        // Nay: chỉ vẽ lại 2 thẻ (cũ + mới), không xóa nền, mỗi thẻ vẽ qua bộ đệm (xem DrawItem).
        _list.MouseMove += (_, e) =>
        {
            var i = _list.IndexFromPoint(e.Location);
            if (i == _hover) return;
            var old = _hover;
            _hover = i;
            InvalidateItem(old);
            InvalidateItem(i);
        };
        _list.MouseLeave += (_, _) =>
        {
            var old = _hover;
            _hover = -1;
            InvalidateItem(old);
        };
        // ListBox tự vẽ để lại vệt khi cuộn → vẽ lại cả danh sách.
        _list.MouseWheel += (_, _) => _list.Invalidate();
    }

    /// <summary>
    /// Một thẻ: <paramref name="Key"/> = giá trị trả về khi chọn (chính là <c>ListBox.Items[i]</c>),
    /// <paramref name="Title"/> = dòng to đậm, <paramref name="Chip"/> = viên bo tròn bên phải, <paramref name="Sub"/> = dòng phụ.
    /// </summary>
    public sealed record PickerItem(string Key, string Title, string? Chip, string? Sub);

    private PickerItem[] _items = [];

    /// <summary>Xe ở vị trí đang chọn, null nếu chưa chọn (chỉ khi mở bằng <see cref="Show"/>).</summary>
    public TripRegistrationRow? Selected =>
        _list.SelectedIndex >= 0 && _list.SelectedIndex < _rows.Length ? _rows[_list.SelectedIndex] : null;

    /// <summary>
    /// Nạp danh sách xe và mở ngay dưới <paramref name="anchor"/> (khung ô nhập), rộng bằng khung (tối thiểu 560px),
    /// cao tối đa 6 thẻ. Danh sách rỗng → ẩn, trả false.
    /// </summary>
    public bool Show(Control anchor, IReadOnlyList<TripRegistrationRow> rows, bool focus = true)
    {
        _rows = rows.ToArray();
        return ShowItems(anchor, _rows.Select(t => new PickerItem(t.TripNo, t.Plate ?? "—", t.TripNo, TripSub(t))).ToList(), focus);
    }

    /// <summary>
    /// Lọc xe theo chuỗi đang gõ: khớp một phần số đăng tài, biển số, khách hàng / chủ mía, lệnh đốn.
    /// Không phân biệt hoa thường; bỏ qua dấu cách, "-", "." (gõ "82A01" vẫn ra "82A-01394").
    /// </summary>
    public static TripRegistrationRow[] Filter(IEnumerable<TripRegistrationRow> rows, string? query)
    {
        var q = Normalize(query);
        if (q.Length == 0) return rows.ToArray();
        return rows.Where(t =>
                Normalize(t.TripNo).Contains(q, StringComparison.Ordinal)
                || Normalize(t.Plate).Contains(q, StringComparison.Ordinal)
                || Normalize(t.ShipmentNo).Contains(q, StringComparison.Ordinal)
                || Normalize(t.CustomerOrSupplier).Contains(q, StringComparison.Ordinal))
            .ToArray();
    }

    private static string Normalize(string? s) => NormalizeSearch(s);

    /// <summary>
    /// Chuẩn hoá để so khớp tìm kiếm: bỏ dấu tiếng Việt (đ → D), bỏ dấu cách / "-" / ".", viết hoa.
    /// Ô tìm đã tắt bộ gõ nên gõ "le thanh duy" phải khớp "LÊ THANH DUY".
    /// </summary>
    public static string NormalizeSearch(string? s) => Helpers.SearchText.Compact(s);

    /// <summary>
    /// Mở danh sách thẻ tuỳ ý (đăng tài: lệnh đốn / biển số). <paramref name="focus"/> = false khi đang gõ để
    /// gợi ý — con trỏ ở lại ô nhập, chưa chọn sẵn dòng nào.
    /// </summary>
    public bool ShowItems(Control anchor, IReadOnlyList<PickerItem> items, bool focus = true)
    {
        _items = items.ToArray();
        if (_items.Length == 0 || anchor.FindForm() is not { } form) { _list.Visible = false; return false; }

        // Theme có thể đã đổi font / viền ListBox sau khi tạo → đặt lại cho thẻ tự vẽ.
        _list.ItemHeight = ITEM_HEIGHT;
        _list.BorderStyle = BorderStyle.FixedSingle;
        _list.BackColor = AgrisTheme.Surface;

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var it in _items) _list.Items.Add(it.Key); // giá trị chọn + chữ cho đọc màn hình — nội dung tự vẽ
        _list.EndUpdate();
        _hover = -1;
        if (focus) _list.SelectedIndex = 0;

        _list.Location = form.PointToClient(anchor.PointToScreen(new Point(0, anchor.Height + 2)));
        _list.Width = Math.Max(anchor.Width, MIN_WIDTH);
        _list.Height = Math.Min(_items.Length, MAX_VISIBLE) * ITEM_HEIGHT + 2;

        if (_list.Parent != form) form.Controls.Add(_list);
        _list.BringToFront();
        _list.Visible = true;
        if (focus) _list.Focus();
        return true;
    }

    private static string TripSub(TripRegistrationRow t)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(t.CustomerOrSupplier)) parts.Add(t.CustomerOrSupplier!.Trim());
        if (!string.IsNullOrWhiteSpace(t.ShipmentNo)) parts.Add(t.ShipmentNo!.Trim());
        if (t.EntryTime is { } entered)
            parts.Add(Loc.T("weigh.popup_entry_time", entered.ToVnTime().ToString("dd/MM HH:mm")));
        return parts.Count > 0 ? string.Join("  ·  ", parts) : "—";
    }

    private void InvalidateItem(int index)
    {
        if (index < 0 || index >= _list.Items.Count) return;
        _list.Invalidate(_list.GetItemRectangle(index), false);
    }

    private void DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _items.Length) return;
        // Vẽ thẻ vào ảnh đệm ở toạ độ (0,0) rồi dán ra màn hình một lần — không nháy nền trắng.
        // (Không dùng BufferedGraphics: nó dịch toạ độ bằng transform mà TextRenderer/GDI bỏ qua
        //  → chữ của thẻ thứ 2 trở đi vẽ lệch ra ngoài, thẻ trắng trơn.)
        var b = e.Bounds;
        if (b.Width <= 0 || b.Height <= 0) return;
        // Ảnh KHÔNG có kênh alpha: chữ ClearType (TextRenderer/GDI) vẽ lên ảnh ARGB bị viền đen / nhòe.
        using var bmp = new Bitmap(b.Width, b.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        using (var g = Graphics.FromImage(bmp))
            DrawCard(g, new Rectangle(0, 0, b.Width, b.Height), e.Index, (e.State & DrawItemState.Selected) != 0);
        e.Graphics.DrawImageUnscaled(bmp, b.Location);
    }

    private void DrawCard(Graphics g, Rectangle r, int index, bool selected)
    {
        var t = _items[index];
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(AgrisTheme.Surface); // nền đặc trước khi vẽ chữ (ClearType cần nền đặc)

        var hover = index == _hover;
        var accent = Accent();
        var soft = Blend(accent, AgrisTheme.Surface, 0.12f);

        var cardBack = selected ? soft : hover ? AgrisTheme.SurfaceHover : AgrisTheme.Surface;
        var chipBack = selected ? AgrisTheme.Surface : soft;
        using (var bg = new SolidBrush(cardBack))
            g.FillRectangle(bg, r);

        // Vạch màu trái: đậm khi chọn / rê chuột.
        using (var bar = new SolidBrush(selected || hover ? accent : Color.FromArgb(70, accent)))
            g.FillRectangle(bar, r.Left, r.Top + 6, 4, r.Height - 12);

        using (var line = new Pen(AgrisTheme.Border))
            g.DrawLine(line, r.Left + 12, r.Bottom - 1, r.Right - 12, r.Bottom - 1);

        const int padLeft = 18, padRight = 14;
        var plateFont = AgrisTheme.Ui(14F, FontStyle.Bold);
        var chipFont = AgrisTheme.Ui(10F, FontStyle.Bold);
        var subFont = AgrisTheme.Ui(10F);

        // Chip (phải, dòng trên) — không có thì dòng to chiếm hết bề ngang.
        var chipText = t.Chip ?? "";
        var chip = new Rectangle(r.Right - padRight, r.Top + 9, 0, 26);
        if (chipText.Length > 0)
        {
            var chipSize = TextRenderer.MeasureText(g, chipText, chipFont, Size.Empty, TextFormatFlags.NoPadding);
            chip = new Rectangle(r.Right - padRight - chipSize.Width - 18, r.Top + 9, chipSize.Width + 18, 26);
            using (var chipPath = Shapes.RoundedRect(chip, 13))
            {
                using var chipBg = new SolidBrush(chipBack);
                g.FillPath(chipBg, chipPath);
                using var chipPen = new Pen(Color.FromArgb(120, accent));
                g.DrawPath(chipPen, chipPath);
            }
            // Có màu nền thì TextRenderer tô cả khung chữ → thu vào trong chip để không đè viền bo tròn.
            TextRenderer.DrawText(g, chipText, chipFont, Rectangle.Inflate(chip, -8, -5), accent, chipBack,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        // Dòng to (trái, dòng trên).
        var plateRect = new Rectangle(r.Left + padLeft, r.Top + 6, Math.Max(40, chip.Left - r.Left - padLeft - 10), 32);
        TextRenderer.DrawText(g, t.Title, plateFont, plateRect, AgrisTheme.TextPrimary, cardBack,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        // Dòng phụ.
        var sub = string.IsNullOrWhiteSpace(t.Sub) ? "—" : t.Sub;
        var subRect = new Rectangle(r.Left + padLeft, r.Top + 38, r.Width - padLeft - padRight, 20);
        TextRenderer.DrawText(g, sub, subFont, subRect, AgrisTheme.TextSecondary, cardBack,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    /// <summary>Trộn màu nhấn với nền theo tỉ lệ (0 = nền, 1 = màu nhấn).</summary>
    private static Color Blend(Color a, Color b, float amount) => Color.FromArgb(
        (int)(a.R * amount + b.R * (1 - amount)),
        (int)(a.G * amount + b.G * (1 - amount)),
        (int)(a.B * amount + b.B * (1 - amount)));
}
