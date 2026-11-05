using TTC.Weighing.Domain.Enums;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Controls;

/// <summary>
/// Hiển thị số cân theo UI-03: số lớn, đơn vị, trạng thái tín hiệu và tiến trình ổn định.
/// Không giữ số cuối như đang trực tuyến khi mất kết nối (04_Man_hinh mục 1).
/// </summary>
public sealed class WeighDisplayControl : UserControl
{
    private readonly Label _lblWeight;
    private readonly Label _lblUom;
    private readonly Label _lblSignalState;
    private readonly Label _lblStability;
    private readonly StabilityBar _stabilityBar;

    /// <summary>
    /// true = hiển thị tấn (cân xe: ÷1000), false = hiển thị kg nguyên gốc (cân mẫu tạp chất).
    /// Mặc định true (cân xe).
    /// </summary>
    public bool DisplayAsTon { get; set; } = true;

    public WeighDisplayControl()
    {
        BackColor = AgrisTheme.ScaleDisplayBack;
        // Bố cục lưới, không xếp chồng nhãn cao cố định (dễ mất chữ khi khung thấp / thu phóng):
        //   [ trạng thái tín hiệu ][            số cân ][ tấn ]   ← dòng chính, co giãn
        //   [ ổn định: x.x s / y.y s                           ]   ← dòng phụ
        //   [ ======== thanh ổn định ========================= ]
        // Số cân vẫn ≥ 36pt (UI-03). Đơn vị đứng cạnh số nên không bị cắt.
        Padding = new Padding(16, 6, 16, 8);
        MinimumSize = new Size(420, 120);

        _lblWeight = new Label
        {
            Text = "---",
            Font = AgrisTheme.Mono(40F, FontStyle.Bold),
            ForeColor = AgrisTheme.ScaleDisplayIdle,
            BackColor = AgrisTheme.ScaleDisplayBack,
            TextAlign = ContentAlignment.MiddleRight,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            AccessibleName = Loc.T("control.scale_accessible_weight")
        };

        _lblUom = new Label
        {
            Text = Loc.T("weigh.unit_ton"),
            Font = AgrisTheme.Body(14F, FontStyle.Bold),
            ForeColor = AgrisTheme.ScaleDisplayCaption,
            BackColor = AgrisTheme.ScaleDisplayBack,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(6, 0, 0, 0)
        };

        _lblSignalState = new Label
        {
            Text = Loc.T("control.scale_no_data"),
            Font = AgrisTheme.Body(11F, FontStyle.Bold),
            ForeColor = AgrisTheme.ScaleDisplayCaption,
            BackColor = AgrisTheme.ScaleDisplayBack,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            AutoEllipsis = true,
            AccessibleName = Loc.T("control.scale_accessible_signal")
        };

        _lblStability = new Label
        {
            Text = Loc.T("control.scale_stability_na"),
            Font = AgrisTheme.Body(9.5F),
            ForeColor = AgrisTheme.ScaleDisplayCaption,
            BackColor = AgrisTheme.ScaleDisplayBack,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            AutoEllipsis = true
        };

        _stabilityBar = new StabilityBar
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 2, 0, 0),
            AccessibleName = Loc.T("control.scale_accessible_stability")
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = AgrisTheme.ScaleDisplayBack
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));  // trạng thái tín hiệu
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));  // số cân
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // đơn vị
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));       // dòng chính
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));       // ổn định
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 10));       // thanh ổn định

        layout.Controls.Add(_lblSignalState, 0, 0);
        layout.Controls.Add(_lblWeight, 1, 0);
        layout.Controls.Add(_lblUom, 2, 0);
        layout.Controls.Add(_lblStability, 0, 1);
        layout.SetColumnSpan(_lblStability, 3);
        layout.Controls.Add(_stabilityBar, 0, 2);
        layout.SetColumnSpan(_stabilityBar, 3);

        Controls.Add(layout);
    }

    /// <summary>Cập nhật hiển thị. An toàn khi gọi từ thread khác.</summary>
    public void Update(
        decimal? normalizedKg,
        string uom,
        ScaleDeviceStatus deviceStatus,
        bool signalFresh,
        TimeSpan stableDuration,
        TimeSpan? requiredWindow)
    {
        if (InvokeRequired)
        {
            Invoke(() => Update(normalizedKg, uom, deviceStatus, signalFresh, stableDuration, requiredWindow));
            return;
        }

        // Tín hiệu cũ hoặc lỗi: không hiển thị số như đang trực tuyến.
        if (!signalFresh || normalizedKg is null)
        {
            _lblWeight.Text = "---";
            _lblWeight.ForeColor = AgrisTheme.ScaleDisplayIdle;

            // Chưa có số cân (chưa có xe / chưa nhập tay) → trạng thái trung tính, không báo lỗi.
            var noData = normalizedKg is null && deviceStatus == ScaleDeviceStatus.Unknown;
            _lblSignalState.Text = noData
                ? Loc.T("control.scale_no_data")
                : deviceStatus == ScaleDeviceStatus.Ok
                    ? Loc.T("control.scale_signal_stale")
                    : Loc.T("control.scale_fault", deviceStatus);
            _lblSignalState.ForeColor = noData ? AgrisTheme.ScaleDisplayCaption : AgrisTheme.Danger;

            _stabilityBar.SetProgress(0);
            _lblStability.Text = Loc.T("control.scale_stability_na");

            AccessibleDescription = _lblSignalState.Text;
            return;
        }

        var displayValue = DisplayAsTon ? normalizedKg.Value / 1000m : normalizedKg.Value;
        var displayUom = DisplayAsTon ? Loc.T("weigh.unit_ton") : "kg";

        // Số cân luôn N3, không theo culture riêng — tránh mất phần lẻ (30,050 kg → 30.050 tấn).
        _lblWeight.Text = $"{displayValue:N3}";
        _lblUom.Text = displayUom;

        var window = requiredWindow ?? TimeSpan.Zero;
        var isStable = window > TimeSpan.Zero && stableDuration >= window;

        _lblWeight.ForeColor = isStable ? AgrisTheme.ScaleStable : AgrisTheme.ScaleMeasuring;
        _lblSignalState.Text = isStable ? Loc.T("control.scale_stable") : Loc.T("control.scale_measuring");
        _lblSignalState.ForeColor = _lblWeight.ForeColor;

        if (window > TimeSpan.Zero)
        {
            var ratio = (float)Math.Clamp(
                stableDuration.TotalMilliseconds / window.TotalMilliseconds, 0d, 1d);

            _stabilityBar.SetProgress(ratio);
            _lblStability.Text =
                Loc.T("control.scale_stability_progress", stableDuration.TotalSeconds.ToString("F1"), window.TotalSeconds.ToString("F1"));
        }
        else
        {
            // Cửa sổ ổn định chưa cấu hình (D-04) — không tự giả định 5 giây.
            _stabilityBar.SetProgress(0);
            _lblStability.Text = Loc.T("control.scale_stability_pending");
        }

        AccessibleDescription = $"{displayValue:N3} {displayUom}, {_lblSignalState.Text}";
    }

    /// <summary>Thanh tiến trình phẳng, màu theo trạng thái ổn định.</summary>
    private sealed class StabilityBar : Control
    {
        private float _progress;

        public StabilityBar()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        public void SetProgress(float ratio)
        {
            _progress = Math.Clamp(ratio, 0f, 1f);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(AgrisTheme.ScaleDisplayBack);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            var radius = Math.Max(1, Height / 2);
            using (var trackPath = Shapes.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), radius))
            using (var track = new SolidBrush(AgrisTheme.ScaleDisplayTrack))
                g.FillPath(track, trackPath);

            if (_progress <= 0) return;

            var width = Math.Max(Height, (int)(Width * _progress));
            var color = _progress >= 1f ? AgrisTheme.ScaleStable : AgrisTheme.ScaleMeasuring;

            using var fillPath = Shapes.RoundedRect(new Rectangle(0, 0, width - 1, Height - 1), radius);
            using var fill = new SolidBrush(color);
            g.FillPath(fill, fillPath);
        }
    }
}
