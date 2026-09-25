using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Controls;

/// <summary>
/// Hiển thị khung hình camera tại cổng (Camera Vào hoặc Camera Ra).
/// Khi camera offline, hiển thị placeholder "Mất kết nối camera" và chấm đỏ.
/// Không chặn đăng tài khi camera không khả dụng (Req 10.3).
/// </summary>
public sealed class CameraViewControl : UserControl
{
    private readonly PictureBox _picCamera;
    private readonly Label _lblTitle;
    private readonly Label _lblStatus;
    private readonly Panel _pnlStatusDot;
    private readonly Label _lblOfflineMessage;

    private bool _isOnline;

    /// <summary>Id cổng mà camera thuộc về.</summary>
    public Guid GateId { get; set; }

    /// <summary>Vị trí camera: "In" (Camera Vào) hoặc "Out" (Camera Ra).</summary>
    public string CameraPosition { get; set; } = "In";

    public CameraViewControl()
    {
        BackColor = AgrisTheme.Surface;
        BorderStyle = BorderStyle.FixedSingle;
        // Ngưỡng tối thiểu vừa phải: khung hình vẫn co giãn đầy ô chứa (Dock.Fill),
        // nhưng không ép chiều cao lớn tới mức tràn khi panel camera bị chia hẹp.
        MinimumSize = new Size(140, 60);

        // --- Header: title + status dot ---
        var pnlHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 28,
            BackColor = AgrisTheme.SurfaceMuted,
            Padding = new Padding(6, 0, 6, 0)
        };

        _pnlStatusDot = new Panel
        {
            Size = new Size(10, 10),
            BackColor = AgrisTheme.Danger,
            Anchor = AnchorStyles.None
        };
        MakeCircular(_pnlStatusDot);

        _lblStatus = new Label
        {
            Text = Loc.T("control.camera_offline"),
            Font = AgrisTheme.Body(8.5F),
            ForeColor = AgrisTheme.Danger,
            AutoSize = true,
            Anchor = AnchorStyles.None
        };

        // FlowLayoutPanel for right-aligned status area
        var flowStatus = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 5, 0, 0)
        };
        flowStatus.Controls.Add(_pnlStatusDot);
        flowStatus.Controls.Add(_lblStatus);

        _lblTitle = new Label
        {
            Text = Loc.T("control.camera_in"),
            Font = AgrisTheme.Body(9F, FontStyle.Bold),
            ForeColor = AgrisTheme.TextPrimary,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AccessibleName = Loc.T("control.camera_accessible_title")
        };

        pnlHeader.Controls.Add(_lblTitle);
        pnlHeader.Controls.Add(flowStatus);

        // --- Camera image area ---
        _picCamera = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(38, 48, 54),
            AccessibleName = Loc.T("control.camera_accessible_frame")
        };

        _lblOfflineMessage = new Label
        {
            Text = Loc.T("control.camera_disconnected"),
            Font = AgrisTheme.Body(10F, FontStyle.Italic),
            ForeColor = AgrisTheme.TextMuted,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill,
            Visible = true,
            AccessibleName = Loc.T("control.camera_accessible_offline")
        };

        _picCamera.Controls.Add(_lblOfflineMessage);

        Controls.Add(_picCamera);
        Controls.Add(pnlHeader);

        // Accessibility
        AccessibleName = Loc.T("control.camera_in");
        AccessibleRole = AccessibleRole.Graphic;

        SetOfflineVisuals();
    }

    /// <summary>
    /// Cập nhật nhãn tiêu đề dựa trên <see cref="CameraPosition"/>.
    /// Gọi sau khi thiết lập CameraPosition.
    /// </summary>
    public void ApplyPositionLabel()
    {
        var title = CameraPosition == "Out" ? Loc.T("control.camera_out") : Loc.T("control.camera_in");

        if (InvokeRequired)
        {
            Invoke(() => SetTitle(title));
            return;
        }

        SetTitle(title);
    }

    /// <summary>
    /// Cập nhật trạng thái camera từ <see cref="CameraStatus"/>. Thread-safe.
    /// </summary>
    public void UpdateStatus(CameraStatus status)
    {
        if (InvokeRequired)
        {
            Invoke(() => UpdateStatus(status));
            return;
        }

        _isOnline = status.IsOnline;

        if (status.IsOnline)
        {
            SetOnlineVisuals();
        }
        else
        {
            SetOfflineVisuals();
        }
    }

    /// <summary>
    /// Gọi <see cref="ICameraService.GetStatusAsync"/> và cập nhật giao diện. Thread-safe.
    /// </summary>
    public async Task UpdateStatusAsync(ICameraService cameraService, CancellationToken ct = default)
    {
        try
        {
            var status = await cameraService.GetStatusAsync(GateId, CameraPosition, ct);
            UpdateStatus(status);
        }
        catch (OperationCanceledException)
        {
            // Polling bị hủy — không cần xử lý.
        }
        catch
        {
            // Lỗi bất kỳ khi kiểm tra camera → coi như offline.
            UpdateStatus(new CameraStatus(IsOnline: false, LastError: Loc.T("control.camera_check_error")));
        }
    }

    /// <summary>
    /// Hiển thị ảnh snapshot lên khung hình. Thread-safe.
    /// </summary>
    public void DisplaySnapshot(Image? image)
    {
        if (InvokeRequired)
        {
            Invoke(() => DisplaySnapshot(image));
            return;
        }

        if (image is not null)
        {
            _picCamera.Image = image;
            _lblOfflineMessage.Visible = false;
        }
        else
        {
            _picCamera.Image = null;
            _lblOfflineMessage.Visible = true;
        }
    }

    /// <summary>Trạng thái kết nối camera hiện tại.</summary>
    public bool IsOnline => _isOnline;

    private void SetTitle(string title)
    {
        _lblTitle.Text = title;
        AccessibleName = title;
    }

    private void SetOnlineVisuals()
    {
        _pnlStatusDot.BackColor = AgrisTheme.Success;
        _lblStatus.Text = Loc.T("control.camera_online");
        _lblStatus.ForeColor = AgrisTheme.Success;

        // Chỉ ẩn message nếu đã có ảnh; nếu chưa có snapshot thì vẫn hiển thị placeholder.
        if (_picCamera.Image is not null)
            _lblOfflineMessage.Visible = false;

        AccessibleDescription = $"{_lblTitle.Text} — {Loc.T("control.camera_online")}";
    }

    private void SetOfflineVisuals()
    {
        _pnlStatusDot.BackColor = AgrisTheme.Danger;
        _lblStatus.Text = Loc.T("control.camera_offline");
        _lblStatus.ForeColor = AgrisTheme.Danger;
        _lblOfflineMessage.Visible = true;
        _picCamera.Image = null;

        AccessibleDescription = $"{_lblTitle.Text} — {Loc.T("control.camera_offline")}: {Loc.T("control.camera_disconnected")}";
    }

    private static void MakeCircular(Panel panel)
    {
        panel.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(panel.BackColor);
            e.Graphics.Clear(panel.Parent?.BackColor ?? Color.Transparent);
            e.Graphics.FillEllipse(brush, 0, 0, panel.Width - 1, panel.Height - 1);
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _picCamera.Image?.Dispose();
        }

        base.Dispose(disposing);
    }
}
