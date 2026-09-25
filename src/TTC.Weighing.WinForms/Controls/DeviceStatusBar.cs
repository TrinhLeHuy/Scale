using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Controls;

/// <summary>
/// Thanh trạng thái kết nối thiết bị trên form cân (UI-03).
/// Hiển thị: Barcode, Đồng hồ cân 1/2, Camera LPR 1/2, Camera toàn cảnh.
/// Mỗi thiết bị thể hiện bằng chấm màu + tên thiết bị.
/// Timer quản lý bên ngoài (Form gọi UpdateStatus mỗi 2 giây).
/// </summary>
public sealed class DeviceStatusBar : UserControl
{
    private readonly FlowLayoutPanel _flow;
    private readonly ToolTip _tooltip;
    private readonly Dictionary<string, DeviceIndicator> _indicators = new(StringComparer.OrdinalIgnoreCase);

    public DeviceStatusBar()
    {
        BackColor = AgrisTheme.SurfaceMuted;
        Height = 32;
        Dock = DockStyle.Top;
        Padding = new Padding(10, 0, 10, 0);
        AccessibleName = Loc.T("control.device_accessible");

        _tooltip = new ToolTip
        {
            AutoPopDelay = 8000,
            InitialDelay = 400,
            ReshowDelay = 200,
            ShowAlways = true
        };

        _flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = false,
            BackColor = AgrisTheme.SurfaceMuted,
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 0, 4)
        };

        Controls.Add(_flow);
    }

    /// <summary>
    /// Cập nhật trạng thái tất cả thiết bị. Gọi từ Form mỗi 2 giây.
    /// An toàn khi gọi từ thread khác (tự Invoke nếu cần).
    /// </summary>
    public void UpdateStatus(IReadOnlyList<DeviceStatus> devices)
    {
        if (InvokeRequired)
        {
            Invoke(() => UpdateStatus(devices));
            return;
        }

        if (devices.Count == 0)
            return;

        // Nếu số lượng thiết bị thay đổi, xây lại toàn bộ indicators.
        if (_indicators.Count != devices.Count || !AllDeviceCodesMatch(devices))
        {
            RebuildIndicators(devices);
            return;
        }

        // Cập nhật tại chỗ để tránh nháy khi poll liên tục.
        foreach (var device in devices)
        {
            if (_indicators.TryGetValue(device.DeviceCode, out var indicator))
                UpdateIndicator(indicator, device);
        }
    }

    /// <summary>Xóa tất cả indicators về trạng thái trống.</summary>
    public void Clear()
    {
        if (InvokeRequired)
        {
            Invoke(Clear);
            return;
        }

        _flow.SuspendLayout();
        _flow.Controls.Clear();
        _indicators.Clear();
        _flow.ResumeLayout();
    }

    private bool AllDeviceCodesMatch(IReadOnlyList<DeviceStatus> devices)
    {
        foreach (var device in devices)
        {
            if (!_indicators.ContainsKey(device.DeviceCode))
                return false;
        }

        return true;
    }

    private void RebuildIndicators(IReadOnlyList<DeviceStatus> devices)
    {
        _flow.SuspendLayout();
        _flow.Controls.Clear();
        _indicators.Clear();

        foreach (var device in devices)
        {
            var indicator = new DeviceIndicator(device.DeviceCode, DeviceClassLabel(device.DeviceClass));
            UpdateIndicator(indicator, device);
            _indicators[device.DeviceCode] = indicator;
            _flow.Controls.Add(indicator);
        }

        _flow.ResumeLayout();
    }

    private void UpdateIndicator(DeviceIndicator indicator, DeviceStatus device)
    {
        var dotColor = ResolveColor(device);
        var tooltipText = BuildTooltip(device);

        indicator.SetStatus(dotColor, tooltipText);
        _tooltip.SetToolTip(indicator, tooltipText);
        _tooltip.SetToolTip(indicator.DotPanel, tooltipText);
        _tooltip.SetToolTip(indicator.CodeLabel, tooltipText);
    }

    private static Color ResolveColor(DeviceStatus device)
    {
        if (!device.IsOnline)
            return AgrisTheme.Danger; // 🔴

        if (device.ScaleStatus is ScaleDeviceStatus.Fault
            or ScaleDeviceStatus.Overload
            or ScaleDeviceStatus.Underload)
            return AgrisTheme.Warning; // 🟡

        return AgrisTheme.Success; // 🟢
    }

    private static string BuildTooltip(DeviceStatus device)
    {
        var parts = new List<string>(4);

        parts.Add($"{DeviceClassLabel(device.DeviceClass)}: {device.DeviceCode}");
        parts.Add(device.IsOnline ? Loc.T("control.device_online") : Loc.T("control.device_offline"));

        if (!string.IsNullOrWhiteSpace(device.FaultDescription))
            parts.Add(Loc.T("control.device_fault", device.FaultDescription));

        if (device.ScaleStatus is ScaleDeviceStatus.Fault
            or ScaleDeviceStatus.Overload
            or ScaleDeviceStatus.Underload)
            parts.Add(Loc.T("control.device_status", device.ScaleStatus));

        if (!device.IsOnline && device.SignalAge is { } age)
            parts.Add(Loc.T("control.device_signal_age", FormatAge(age)));

        if (device.LastSignalAt is { } lastSignal)
            parts.Add(Loc.T("control.device_last_signal", lastSignal.ToString("HH:mm:ss dd/MM/yyyy")));

        return string.Join(Environment.NewLine, parts);
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalSeconds < 60)
            return $"{age.TotalSeconds:F0}s";
        if (age.TotalMinutes < 60)
            return $"{age.TotalMinutes:F0}p {age.Seconds}s";
        return $"{age.TotalHours:F0}h {age.Minutes}p";
    }

    private static string DeviceClassLabel(string deviceClass) => deviceClass switch
    {
        "Barcode" => Loc.T("control.device_barcode"),
        "Scale1" => Loc.T("control.device_scale1"),
        "Scale2" => Loc.T("control.device_scale2"),
        "LPR1" => Loc.T("control.device_lpr1"),
        "LPR2" => Loc.T("control.device_lpr2"),
        "Overview" => Loc.T("control.device_overview"),
        _ => deviceClass
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _tooltip.Dispose();

        base.Dispose(disposing);
    }

    /// <summary>Một chỉ thị thiết bị: chấm màu + label tên.</summary>
    internal sealed class DeviceIndicator : Panel
    {
        private readonly Panel _dot;
        private readonly Label _label;

        internal Panel DotPanel => _dot;
        internal Label CodeLabel => _label;

        public DeviceIndicator(string deviceCode, string displayName)
        {
            Height = 24;
            Width = 160;
            Margin = new Padding(0, 0, 12, 0);
            BackColor = Color.Transparent;

            _dot = new Panel
            {
                Size = new Size(12, 12),
                Location = new Point(0, 6),
                BackColor = AgrisTheme.Neutral
            };

            // Bo tròn chấm trạng thái.
            _dot.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var brush = new SolidBrush(_dot.BackColor);
                e.Graphics.Clear(BackColor);
                e.Graphics.FillEllipse(brush, 0, 0, _dot.Width - 1, _dot.Height - 1);
            };

            _label = new Label
            {
                Text = displayName,
                Location = new Point(16, 3),
                AutoSize = true,
                Font = AgrisTheme.Body(8.5F),
                ForeColor = AgrisTheme.TextPrimary,
                BackColor = Color.Transparent
            };

            Controls.Add(_label);
            Controls.Add(_dot);

            AccessibleName = $"{Loc.T("control.device_status_prefix")} {displayName}";
        }

        public void SetStatus(Color dotColor, string accessibleText)
        {
            if (_dot.BackColor != dotColor)
            {
                _dot.BackColor = dotColor;
                _dot.Invalidate();
            }

            AccessibleDescription = accessibleText;
        }
    }
}
