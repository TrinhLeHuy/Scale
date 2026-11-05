using Microsoft.Extensions.Configuration;
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
    private bool? _shownOnline;

    // ── Xem trực tiếp (chụp ảnh định kỳ theo cấu hình màn Kết nối camera) ──
    private ICameraConnectionService? _liveService;
    private string? _liveStation;
    private CancellationTokenSource? _liveCts;

    /// <summary>Id cổng mà camera thuộc về.</summary>
    public Guid GateId { get; set; }

    /// <summary>Vị trí camera: "In" (Camera Vào) hoặc "Out" (Camera Ra).</summary>
    public string CameraPosition { get; set; } = "In";

    /// <summary>
    /// true = ảnh phủ kín khung (cắt bớt mép theo tỉ lệ khung), không có dải đen hai bên.
    /// false (mặc định) = hiện trọn ảnh, có thể có dải đen.
    /// </summary>
    public bool FillFrame { get; set; }

    public CameraViewControl()
    {
        BackColor = AgrisTheme.Surface;
        BorderStyle = BorderStyle.FixedSingle;
        // Ngưỡng tối thiểu vừa phải: khung hình vẫn co giãn đầy ô chứa (Dock.Fill),
        // nhưng không ép chiều cao lớn tới mức tràn khi panel camera bị chia hẹp.
        // Bề rộng tối thiểu 96: ô camera ở màn Cân là cột 10% — màn hình 1366/1280 chỉ còn ~110px.
        MinimumSize = new Size(96, 60);

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
        // Xem trực tiếp gọi mỗi giây: trạng thái không đổi thì không vẽ lại.
        if (_shownOnline == status.IsOnline) return;
        _shownOnline = status.IsOnline;

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

        SetLiveImage(image);
    }

    /// <summary>Trạng thái kết nối camera hiện tại.</summary>
    public bool IsOnline => _isOnline;

    /// <summary>
    /// Hiển thị trực tiếp camera của màn hình <paramref name="station"/> (Weigh / Registration) tại vị trí
    /// <see cref="CameraPosition"/>, đúng cấu hình đã lưu ở màn Kết nối camera.
    /// Chỉ lấy ảnh khi khung đang hiện (tab đang mở, cửa sổ không thu nhỏ). Dừng khi control bị hủy.
    /// </summary>
    public void StartLive(ICameraConnectionService service, string station)
    {
        _liveService = service;
        _liveStation = station;
        if (IsHandleCreated) RestartLive();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (_liveService is not null) RestartLive();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        StopLive();
        base.OnHandleDestroyed(e);
    }

    private void RestartLive()
    {
        StopLive();
        _liveCts = new CancellationTokenSource();
        _ = LiveLoopAsync(_liveService!, _liveStation!, _liveCts.Token);
    }

    private void StopLive()
    {
        _liveCts?.Cancel();
        _liveCts?.Dispose();
        _liveCts = null;
    }

    internal static int LiveIntervalMs =>
        Math.Clamp(Program.AppConfiguration?.GetValue<int?>("Cameras:LiveIntervalMs") ?? 250, 100, 10000);

    /// <summary>Chạy trên luồng giao diện; chỉ phần giải mã / thu nhỏ ảnh chạy nền.</summary>
    private async Task LiveLoopAsync(ICameraConnectionService service, string station, CancellationToken ct)
    {
        var interval = LiveIntervalMs;
        while (!ct.IsCancellationRequested)
        {
            var delay = interval;
            var started = Environment.TickCount64;
            try
            {
                // Luôn lấy trực tiếp, kể cả khi tab đang ẩn / cửa sổ thu nhỏ: khi quay lại là ảnh mới ngay,
                // trạng thái Online/Offline luôn đúng.
                {
                    if (service.FindEnabled(station, CameraPosition) is null)
                    {
                        ShowNotConfigured();
                        delay = Math.Max(interval, 3000);
                    }
                    else
                    {
                        var bytes = await service.CaptureAsync(station, CameraPosition, ct);
                        if (ct.IsCancellationRequested) break;
                        if (bytes is null)
                        {
                            UpdateStatus(new CameraStatus(false, service.GetLive(station, CameraPosition).Error));
                            delay = Math.Max(interval, 3000); // mất kết nối: thử lại thưa hơn
                        }
                        else
                        {
                            var target = _picCamera.ClientSize;
                            var fill = FillFrame;
                            var image = await Task.Run(() => DecodeToFit(bytes, target, fill), ct);
                            if (ct.IsCancellationRequested) { image?.Dispose(); break; }
                            SetLiveImage(image);
                            UpdateStatus(new CameraStatus(image is not null, null));
                            // Camera mất 0,3–1 s mới trả ảnh: trừ thời gian đó để giữ đúng nhịp khung hình.
                            delay = (int)Math.Max(100, interval - (Environment.TickCount64 - started));
                        }
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception)
            {
                if (IsDisposed) break;
                UpdateStatus(new CameraStatus(false, Loc.T("control.camera_check_error")));
            }

            try { await Task.Delay(delay, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Giải mã JPEG và thu nhỏ vừa khung (ảnh camera 2–4 MP, vẽ ảnh gốc mỗi giây tốn CPU).</summary>
    private static Image? DecodeToFit(byte[] bytes, Size target, bool fill)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            using var src = Image.FromStream(ms);

            // Vùng ảnh gốc được dùng: cả ảnh, hoặc (fill) phần giữa cắt theo tỉ lệ khung.
            var srcRect = new Rectangle(0, 0, src.Width, src.Height);
            if (fill && target.Width > 0 && target.Height > 0)
            {
                var targetRatio = (double)target.Width / target.Height;
                var srcRatio = (double)src.Width / src.Height;
                if (srcRatio > targetRatio)
                {
                    var cw = Math.Max(1, (int)Math.Round(src.Height * targetRatio));
                    srcRect = new Rectangle((src.Width - cw) / 2, 0, cw, src.Height);
                }
                else
                {
                    var ch = Math.Max(1, (int)Math.Round(src.Width / targetRatio));
                    srcRect = new Rectangle(0, (src.Height - ch) / 2, src.Width, ch);
                }
            }

            var scale = target.Width <= 0 || target.Height <= 0
                ? 1d
                : Math.Min(1d, Math.Min((double)target.Width / srcRect.Width, (double)target.Height / srcRect.Height));
            var w = Math.Max(1, (int)Math.Round(srcRect.Width * scale));
            var h = Math.Max(1, (int)Math.Round(srcRect.Height * scale));
            var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            g.DrawImage(src, new Rectangle(0, 0, w, h), srcRect, GraphicsUnit.Pixel);
            return bmp;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private void SetLiveImage(Image? image)
    {
        var old = _picCamera.Image;
        _picCamera.Image = image;
        if (!ReferenceEquals(old, image)) old?.Dispose();
        _lblOfflineMessage.Visible = image is null;
    }

    private void ShowNotConfigured()
    {
        SetLiveImage(null);
        _isOnline = false;
        _shownOnline = null;
        _pnlStatusDot.BackColor = AgrisTheme.TextMuted;
        _lblStatus.Text = Loc.T("control.camera_offline");
        _lblStatus.ForeColor = AgrisTheme.TextMuted;
        _lblOfflineMessage.Text = Loc.T("control.camera_not_configured");
        _lblOfflineMessage.Visible = true;
    }

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
        _lblOfflineMessage.Text = Loc.T("control.camera_disconnected");
        // Không giữ ảnh cũ như đang trực tuyến.
        SetLiveImage(null);

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
            StopLive();
            _picCamera.Image?.Dispose();
        }

        base.Dispose(disposing);
    }
}
