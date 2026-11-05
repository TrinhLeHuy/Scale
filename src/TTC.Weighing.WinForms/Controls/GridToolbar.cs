using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Controls;

/// <summary>
/// Thanh công cụ chuẩn đặt trên mọi lưới chuyến:
/// trái = nhóm Mía/Vật tư + tab lọc + khoảng ngày; phải = Xuất Excel + Phóng to.
/// Một mẫu chung để các màn hình nhìn và thao tác giống nhau.
/// </summary>
public sealed class GridToolbar : Panel
{
    public FlowLayoutPanel LeftItems { get; }
    public FlowLayoutPanel RightItems { get; }

    public GridToolbar(Control[] left, Control[] right)
    {
        Dock = DockStyle.Top;
        Height = 56;
        BackColor = AgrisTheme.SurfaceMuted;
        Padding = new Padding(10, 7, 10, 7);

        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        RightItems = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };

        // Cho xuống dòng: ngôn ngữ có chữ dài (Khmer/Lào) hoặc màn hình hẹp thì bộ lọc sang hàng thứ hai,
        // không bị đẩy ra ngoài khung (mất ô "Đến ngày"). Chiều cao thanh tự tăng theo số hàng — xem OnLayout.
        LeftItems = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.Transparent,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };

        foreach (var control in right)
        {
            if (control.Margin == new Padding(3))
                control.Margin = new Padding(8, 0, 0, 0);
            RightItems.Controls.Add(control);
        }

        LeftItems.Controls.AddRange(left);

        // Thứ tự dock: control add sau được dock trước → RightItems chiếm mép phải, LeftItems lấp phần còn lại.
        Controls.Add(LeftItems);
        Controls.Add(RightItems);
    }

    private static int SingleRowHeight => Theme.UiScale.Px(56);
    private bool _adjustingHeight;

    /// <summary>Chiều cao theo số hàng thực tế của dãy bên trái (tối thiểu một hàng).</summary>
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_adjustingHeight || LeftItems is null || RightItems is null) return;

        var available = ClientSize.Width - Padding.Horizontal - RightItems.Width;
        if (available <= 0) return;

        var needed = LeftItems.GetPreferredSize(new Size(available, 0)).Height + Padding.Vertical;
        var target = Math.Max(SingleRowHeight, needed);
        if (target == Height) return;

        try
        {
            _adjustingHeight = true;
            Height = target;
        }
        finally
        {
            _adjustingHeight = false;
        }
    }

    /// <summary>
    /// Nhóm các control luôn đi cùng nhau khi thanh xuống dòng (ví dụ "Từ ngày [..] → [..]"),
    /// để không bị tách nhãn khỏi ô.
    /// </summary>
    public static FlowLayoutPanel Group(params Control[] controls)
    {
        var group = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = Padding.Empty,
            Margin = new Padding(0, 0, 8, 0)
        };
        group.Controls.AddRange(controls);
        return group;
    }

    /// <summary>Nhãn nhỏ đặt cạnh ô lọc ("Từ ngày", "→").</summary>
    public static Label FilterLabel(string text, Padding? margin = null) => new()
    {
        Text = text,
        AutoSize = true,
        Font = AgrisTheme.Body(9F),
        ForeColor = AgrisTheme.TextMuted,
        Margin = margin ?? new Padding(0, 9, 6, 0)
    };

    /// <summary>Vạch dọc ngăn nhóm lọc.</summary>
    public static Control Divider() => new Panel
    {
        Width = 1,
        Height = 32,
        BackColor = AgrisTheme.BorderStrong,
        Margin = new Padding(4, 4, 14, 0)
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(AgrisTheme.Border);
        e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }
}
