using TTC.Weighing.Domain.Common;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Domain.Entities;

/// <summary>
/// Một lượt vận chuyển (R-003). TripId nội bộ là khóa duy nhất — biển số KHÔNG phải khóa chuyến.
/// Một biển số có nhiều chuyến theo thời gian.
/// </summary>
public class Trip : AuditableEntity
{
    public string TripNo { get; set; } = string.Empty;
    public Guid SiteId { get; set; }

    /// <summary>Mã BU (công ty) tạo chuyến — dùng để phân tách dữ liệu giữa các nhà máy.</summary>
    public string? OrganizationUnitCode { get; set; }

    public Guid WorkflowProfileId { get; set; }
    public int WorkflowProfileVersion { get; set; }

    /// <summary>FRM hoặc ERP.</summary>
    public string SourceSystem { get; set; } = string.Empty;

    /// <summary>Mã chuyến/chứng từ bên nguồn. Lưu dạng chuỗi để giữ tiền tố và số 0 đầu.</summary>
    public string? ReferenceTripNo { get; set; }

    public Guid? VehicleId { get; set; }
    public string? TractorPlate { get; set; }
    public string? TrailerPlate { get; set; }
    public string? DriverRef { get; set; }

    public TripLifecycle Lifecycle { get; set; } = TripLifecycle.TempRegistered;

    /// <summary>Độc lập với Lifecycle. PAUSED giữ nguyên bước cuối (02_Quy_tac 2.2).</summary>
    public ExecutionStatus ExecutionStatus { get; set; } = ExecutionStatus.Active;

    public string? PauseIssueCode { get; set; }
    public string? PauseScope { get; set; }
    public DateTimeOffset? PausedAt { get; set; }

    // --- Trường mới cho trip-registration-redesign ---

    /// <summary>Loại xe: Container, Truck, Trailer. Null khi hồ sơ không bắt buộc phân loại.</summary>
    public VehicleType? VehicleType { get; set; }

    /// <summary>Số thứ tự trong hàng chờ theo loại xe, riêng biệt với TripNo.</summary>
    public int? QueueSequenceNumber { get; set; }

    /// <summary>Bàn cân được gán khi xếp hàng.</summary>
    public Guid? AssignedWeighbridgeId { get; set; }

    /// <summary>Kho đích được gán từ kế hoạch nguồn.</summary>
    public Guid? AssignedWarehouseId { get; set; }

    /// <summary>Cổng đăng tài.</summary>
    public Guid? GateId { get; set; }

    // --- Thông tin mía mở rộng (từ FRM) ---

    /// <summary>Loại mía.</summary>
    public string? SugarcaneType { get; set; }

    /// <summary>Giống mía.</summary>
    public string? SugarcaneVariety { get; set; }

    /// <summary>Chủ mía.</summary>
    public string? SugarcaneOwner { get; set; }

    /// <summary>Hình thức thu hoạch.</summary>
    public string? HarvestMethod { get; set; }

    /// <summary>Trạm.</summary>
    public string? Station { get; set; }

    /// <summary>Vụ mùa.</summary>
    public string? CropSeason { get; set; }

    // --- Thông tin lệnh đốn FRM (lưu khi đăng tài để push DigiScale sau) ---

    /// <summary>GUID lệnh đốn từ FRM (sourceKey).</summary>
    public string? FrmLenhDonId { get; set; }

    /// <summary>Mã lệnh đốn (VD: LD2425-00000003).</summary>
    public string? FrmMaLenhDon { get; set; }

    /// <summary>BU của lệnh đốn (VD: KSK, TTCS).</summary>
    public string? FrmLenhDonBu { get; set; }

    /// <summary>Mã loại xe từ FRM (100000000/100000001/100000002).</summary>
    public string? FrmLoaiXeCode { get; set; }

    /// <summary>Nhà vận chuyển (ERP transporter) lưu lúc đăng tài vật tư — đẩy lên DigiScale khi cân xong.</summary>
    public string? Transporter { get; set; }

    /// <summary>Kho — tên tổ chức kho ERP (ví dụ "126501 - Thanh pham - PROC") lưu lúc đăng tài vật tư.</summary>
    public string? WarehouseName { get; set; }

    // --- Phiếu cân ---

    /// <summary>Số phiếu cân sau khi cân lần 2 hoàn thành.</summary>
    public string? TicketNumber { get; set; }

    // --- Collections ---

    public List<CheckpointEvent> Checkpoints { get; set; } = [];
    public List<WeighSession> WeighSessions { get; set; } = [];
    public List<QrToken> QrTokens { get; set; } = [];

    public bool IsOperable =>
        ExecutionStatus == ExecutionStatus.Active &&
        Lifecycle is not (TripLifecycle.Closed or TripLifecycle.Cancelled);
}

/// <summary>Một lần đi qua checkpoint. Một chuyến có thể ghé kho nhiều lần (R-012).</summary>
public class CheckpointEvent : AuditableEntity
{
    public Guid TripId { get; set; }
    public CheckpointType Type { get; set; }
    public CheckpointStatus Status { get; set; } = CheckpointStatus.Pending;

    public Guid? WarehouseId { get; set; }
    public Guid? LaneId { get; set; }

    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public string? ConfirmedBy { get; set; }

    /// <summary>Bắt buộc khi Status = Waived. Không miễn để bỏ qua thiết bị đang lỗi.</summary>
    public string? WaiverApprovalRef { get; set; }
    public string? WaiverReason { get; set; }
}
