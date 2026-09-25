namespace TTC.Weighing.WinForms.Services;

/// <summary>
/// Event bus in-memory cho các form WinForms. Khi một form thay đổi trạng thái chuyến,
/// phát event → các form khác đang mở nhận được và tự cập nhật (refresh lưới, load dữ liệu).
///
/// Đăng ký Singleton trong DI. Form subscribe khi mở, unsubscribe khi đóng.
/// Gọi Publish từ UI thread — handler cũng chạy trên UI thread.
/// </summary>
public sealed class TripEventBus
{
    /// <summary>Sự kiện khi trạng thái chuyến thay đổi.</summary>
    public event EventHandler<TripChangedEventArgs>? TripChanged;

    /// <summary>
    /// Phát sự kiện thay đổi chuyến. Gọi từ form sau khi thao tác thành công.
    /// </summary>
    public void Publish(TripChangedEventArgs args)
    {
        TripChanged?.Invoke(this, args);
    }
}

/// <summary>
/// Thông tin sự kiện thay đổi chuyến. Các form dùng ChangeType để quyết định cần refresh gì.
/// </summary>
public sealed class TripChangedEventArgs : EventArgs
{
    /// <summary>ID chuyến bị thay đổi.</summary>
    public required Guid TripId { get; init; }

    /// <summary>Số đăng tài để hiển thị log.</summary>
    public string? TripNo { get; init; }

    /// <summary>Biển số xe.</summary>
    public string? Plate { get; init; }

    /// <summary>Nội dung QR để form tiếp theo tự xử lý mà không cần quét lại.</summary>
    public string? QrContent { get; init; }

    /// <summary>Loại thay đổi.</summary>
    public required TripChangeType ChangeType { get; init; }

    /// <summary>Form phát sự kiện (để form không tự xử lý event của chính mình nếu cần).</summary>
    public string? SourceForm { get; init; }
}

/// <summary>Các loại thay đổi trạng thái chuyến.</summary>
public enum TripChangeType
{
    /// <summary>Xe mới đăng tài (UI-02).</summary>
    Registered,

    /// <summary>Xe đã hủy đăng tài (UI-02).</summary>
    Cancelled,

    /// <summary>Xe đã qua cổng vào (UI-11).</summary>
    GateEntryConfirmed,

    /// <summary>Cân lần 1 đã chốt (UI-03).</summary>
    FirstWeighCommitted,

    /// <summary>Tạp chất đã đo xong (UI-05).</summary>
    ImpurityRecorded,

    /// <summary>Chữ đường đã nhận (UI-05).</summary>
    CcsReceived,

    /// <summary>Xe đã vào kho (UI-04).</summary>
    WarehouseEntry,

    /// <summary>Xe đã ra kho (UI-04).</summary>
    WarehouseExit,

    /// <summary>Cân lần 2 đã chốt (UI-03).</summary>
    SecondWeighCommitted,

    /// <summary>Đối soát xong (UI-04).</summary>
    Reconciled,

    /// <summary>Xe đã qua cổng ra (UI-11).</summary>
    GateExitConfirmed,

    /// <summary>Đồng bộ ERP/FRM xong (UI-03/UI-08).</summary>
    IntegrationSynced
}
