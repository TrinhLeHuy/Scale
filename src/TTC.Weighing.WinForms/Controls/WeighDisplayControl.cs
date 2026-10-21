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
        Padding = new Padding(18, 12, 18, 12);
        MinimumSize = new Size(420, 190);

        _lblWeight = new Label
        {
            Text = "---",
            Font = new Font(AgrisTheme.MonoFamily, 46F, FontStyle.Bold),
            ForeColor = AgrisTheme.ScaleDisplayIdle,
            TextAlign = ContentAlignment.MiddleRight,
            Dock = DockStyle.Top,
            Height = 82,
            AccessibleName = Loc.T("control.scale_accessible_weight")
        };

        _lblUom = new Label
        {
            Text = "tấn",
            Font = AgrisTheme.Body(11F),
            ForeColor = Color.FromArgb(150, 160, 166),
            TextAlign = ContentAlignment.MiddleRight,
            Dock = DockStyle.Top,
            Height = 24
        };

        _lblSignalState = new Label
        {
            Text = Loc.T("control.scale_signal_lost"),
            Font = AgrisTheme.Body(11F, FontStyle.Bold),
            ForeColor = AgrisTheme.Danger,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Top,
            Height = 30,
            AccessibleName = Loc.T("control.scale_accessible_signal")
        };

        _lblStability = new Label
        {
            Text = Loc.T("control.scale_stability_na"),
            Font = AgrisTheme.Body(9.5F),
            ForeColor = Color.FromArgb(150, 160, 166),
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Top,
            Height = 24
        };

        _stabilityBar = new StabilityBar
        {
            Dock = DockStyle.Top,
            Height = 12,
            AccessibleName = Loc.T("control.scale_accessible_stability")
        };

        Controls.Add(_stabilityBar);
        Controls.Add(_lblStability);
        Controls.Add(_lblSignalState);
        Controls.Add(_lblUom);
        Controls.Add(_lblWeight);
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

            _lblSignalState.Text = deviceStatus == ScaleDeviceStatus.Ok
                ? Loc.T("control.scale_signal_stale")
                : Loc.T("control.scale_fault", deviceStatus);
            _lblSignalState.ForeColor = AgrisTheme.Danger;

            _stabilityBar.SetProgress(0);
            _lblStability.Text = Loc.T("control.scale_stability_na");

            AccessibleDescription = _lblSignalState.Text;
            return;
        }

        var displayValue = DisplayAsTon ? normalizedKg.Value / 1000m : normalizedKg.Value;
        var displayUom = DisplayAsTon ? "tấn" : "kg";

        _lblWeight.Text = $"{displayValue:N0}";
        _lblUom.Text = displayUom;

        var window = requiredWindow ?? TimeSpan.Zero;
        var isStable = window > TimeSpan.Zero && stableDuration >= window;

        _lblWeight.ForeColor = isStable ? Color.FromArgb(120, 226, 160) : Color.FromArgb(238, 213, 130);
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
            using var track = new SolidBrush(Color.FromArgb(38, 48, 54));
            e.Graphics.FillRectangle(track, ClientRectangle);

            if (_progress <= 0) return;

            var width = (int)(Width * _progress);
            var color = _progress >= 1f ? AgrisTheme.BrandGreen : Color.FromArgb(196, 158, 40);

            using var fill = new SolidBrush(color);
            e.Graphics.FillRectangle(fill, 0, 0, width, Height);
        }
    }
}
