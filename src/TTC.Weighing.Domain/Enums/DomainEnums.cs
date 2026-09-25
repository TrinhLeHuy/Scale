namespace TTC.Weighing.Domain.Enums;

/// <summary>Vòng đời nghiệp vụ của chuyến. Nguồn: 02_Quy_tac mục 2.1.</summary>
public enum TripLifecycle
{
    TempRegistered,
    Registered,
    InProgress,
    WeighingCompleted,
    Reconciled,
    Posted,
    Closed,
    Cancelled
}

/// <summary>Trạng thái thực thi, độc lập với vòng đời chuyến. Nguồn: 02_Quy_tac mục 2.2 (U-13).</summary>
public enum ExecutionStatus
{
    Active,
    Paused
}

/// <summary>Loại xe: Container, Xe tải, Xe kéo. Dùng để xếp hàng riêng biệt và cấp số thứ tự.</summary>
public enum VehicleType
{
    Container,
    Truck,
    Trailer
}

/// <summary>Loại QR. QR tạm KHÔNG đủ điều kiện cân chính thức (R-004).</summary>
public enum QrTokenType
{
    Temp,
    Trip,
    InternalVehicle
}

public enum QrTokenStatus
{
    Issued,
    Revoked,
    Expired
}

/// <summary>Vai trò đo của một lần cân. Không suy ra cố định từ thứ tự lần cân (02_Quy_tac 4.1).</summary>
public enum WeightRole
{
    Gross,
    Tare
}

/// <summary>Nguồn của số cân. MANUAL/IMPORT/TEST không được đi vào luồng production như AUTO.</summary>
public enum WeighSource
{
    Auto,
    Manual,
    Import,
    Test
}

public enum WeighSessionStatus
{
    Open,
    Committed,
    Abandoned
}

/// <summary>Hướng nghiệp vụ, quyết định công thức net (02_Quy_tac 4.1).</summary>
public enum ProfileDirection
{
    Inbound,
    Outbound,
    InternalTransfer
}

public enum CheckpointStatus
{
    Pending,
    Active,
    Done,
    Rejected,
    /// <summary>Chỉ dùng khi quy tắc cho phép miễn có phê duyệt. Không dùng để bỏ qua thiết bị đang lỗi.</summary>
    Waived
}

public enum CheckpointType
{
    Registration,
    GateIn,
    FirstWeight,
    SecondWeight,
    ImpuritySample,
    CcsSample,
    WarehouseIn,
    WarehouseOut,
    Reconciliation,
    Sync,
    GateOut
}

/// <summary>Kết luận từng phép đối soát (R-020).</summary>
public enum ReconciliationVerdict
{
    Pending,
    Pass,
    Mismatch,
    MissingData,
    NotApplicable
}

/// <summary>Trạng thái thông điệp tích hợp. UNKNOWN khác FAILED (R-024).</summary>
public enum IntegrationMessageStatus
{
    New,
    Held,
    Sending,
    Acknowledged,
    Failed,
    Unknown,
    Superseded
}

/// <summary>Kết luận AI vị trí. Không chuyển null confidence thành 1 (03_Du_lieu mục 4).</summary>
public enum PositionVerdict
{
    Valid,
    Invalid,
    Unknown
}

public enum ScaleDeviceStatus
{
    Ok,
    Fault,
    Overload,
    Underload,
    Unknown
}

/// <summary>Cơ sở khối lượng — phải khai báo rõ trước khi đối soát (02_Quy_tac mục 6).</summary>
public enum MassBasis
{
    ProductNet,
    PackagedGoods,
    IncludingPallets,
    Unknown
}

public enum MassOrigin
{
    Measured,
    DerivedFromCount
}

public enum IotReadingMode
{
    Delta,
    Cumulative
}

public enum GateCommandState
{
    Requested,
    Acknowledged,
    Executed,
    Failed,
    Unknown,
    Expired
}

/// <summary>Loại mẫu chất lượng.</summary>
public enum QualitySampleType
{
    /// <summary>Cân tạp chất tại nhà máy.</summary>
    Impurity,

    /// <summary>Chữ đường — kết quả có thể đến sau cân lần 2 (R-014).</summary>
    Ccs
}

public enum QualityResultStatus
{
    /// <summary>Đã gắn mẫu, chưa có số đo.</summary>
    Collected,

    /// <summary>Đang chờ kết quả từ nguồn có thẩm quyền. Với CCS đây KHÔNG phải lỗi.</summary>
    Pending,

    Valid,

    /// <summary>Kết quả không hợp lệ — giữ cờ không đồng bộ, không gửi đi.</summary>
    Invalid,

    /// <summary>Đã lấy lại mẫu, bản này hết hiệu lực.</summary>
    Superseded
}

/// <summary>Trạng thái phiên đo IoT/đếm bao (B-010).</summary>
public enum MeasurementSessionStatus
{
    Open,

    /// <summary>Tạm giữ để xác minh: reset bộ đếm, thiếu sự kiện, sai gán chuyến.</summary>
    Held,

    Closed,

    /// <summary>Đóng nhưng dữ liệu chưa đủ để dùng cho đối soát.</summary>
    ClosedIncomplete
}

/// <summary>Trạng thái xử lý một sự kiện thiết bị (R-024).</summary>
public enum DeviceEventProcessingStatus
{
    Received,
    Applied,

    /// <summary>Trùng ID cùng nội dung — đã xử lý trước đó.</summary>
    DuplicateIgnored,

    /// <summary>Trùng ID nhưng nội dung khác — cách ly và báo lỗi.</summary>
    Quarantined,

    /// <summary>Chưa gán được chuyến/phiên, chờ xác minh.</summary>
    Unassigned
}

/// <summary>Loại phép đối soát (02_Quy_tac mục 6).</summary>
public enum ReconciliationCheckType
{
    VehicleNetVsIotMass,
    AiBagCountVsWarehouseCount,
    VehicleNetVsNominalRange,
    TripActualVsSourcePlan
}

/// <summary>Phương pháp phân bổ khi một chuyến có nhiều chủ mía/Shipment (02_Quy_tac 4.3).</summary>
public enum AllocationMethod
{
    /// <summary>Theo tỷ lệ đã duyệt.</summary>
    ApprovedRatio,

    /// <summary>Theo số lượng xác định, phần còn lại về dòng chỉ định.</summary>
    FixedQuantityWithRemainder,

    /// <summary>Theo tỷ lệ trọng lượng đơn (S03 tr.16).</summary>
    OrderWeightRatio
}

public enum GateDirection
{
    In,
    Out
}
