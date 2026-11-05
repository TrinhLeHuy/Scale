namespace TTC.Weighing.WinForms.Controls;

/// <summary>
/// Khung cuộn (Panel / Card AutoScroll) kéo thanh cuộn hoặc lăn chuột bị giật / để lại vệt:
/// <list type="bullet">
/// <item>Bật double-buffer cho chính khung (nền vẽ qua bộ đệm, không nháy trắng).</item>
/// <item>Sau mỗi bước cuộn vẽ lại ngay khung + con một lần (<c>Update</c>) thay vì để từng ô tự vẽ trễ
///   → không còn vệt / xé hình khi kéo nhanh.</item>
/// </list>
/// Không dùng WS_EX_COMPOSITED: làm con trỏ nhập của TextBox nhấp nháy / mất và tốn CPU khi có camera.
/// </summary>
public static class ScrollSmoothing
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, object> Applied = new();

    public static void Apply(ScrollableControl panel)
    {
        if (panel is Form || Applied.TryGetValue(panel, out _)) return;
        Applied.Add(panel, new object());

        typeof(Control).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(panel, true);

        void Repaint()
        {
            if (panel.IsDisposed) return;
            panel.Invalidate(true);
            panel.Update();
        }

        panel.Scroll += (_, e) =>
        {
            // ThumbTrack = đang kéo thanh cuộn; EndScroll = thả chuột. Cả hai đều vẽ lại gọn một lần.
            if (e.Type is ScrollEventType.ThumbTrack or ScrollEventType.EndScroll
                or ScrollEventType.SmallIncrement or ScrollEventType.SmallDecrement
                or ScrollEventType.LargeIncrement or ScrollEventType.LargeDecrement)
                Repaint();
        };
        panel.MouseWheel += (_, _) => Repaint();
    }
}
