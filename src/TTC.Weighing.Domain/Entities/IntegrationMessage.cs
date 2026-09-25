using TTC.Weighing.Domain.Common;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Domain.Entities;

/// <summary>
/// Sổ thông điệp bền vững, lưu cùng transaction với kết quả nghiệp vụ (03_Du_lieu mục 7).
/// UNKNOWN khác FAILED; không đổi BusinessKey chỉ để gửi lại cho qua lỗi.
/// </summary>
public class IntegrationMessage : AuditableEntity
{
    public Guid TripId { get; set; }
    public int TripRevision { get; set; }

    /// <summary>FRM hoặc ERP.</summary>
    public string Destination { get; set; } = string.Empty;

    /// <summary>Tên operation theo hợp đồng đã xác minh.</summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>Khóa nghiệp vụ chống lặp: đích + trip + loại kết quả + revision + chứng từ/dòng.</summary>
    public string BusinessKey { get; set; } = string.Empty;

    /// <summary>Gửi nhiều Shipment thì mỗi Shipment một thông điệp và trạng thái riêng.</summary>
    public string? ShipmentNo { get; set; }

    public string PayloadRef { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;

    /// <summary>Tham chiếu do hợp đồng cũ dùng, nếu có. Chưa xác nhận API hỗ trợ idempotency (D-02).</summary>
    public string? InterfaceReference { get; set; }

    public IntegrationMessageStatus Status { get; set; } = IntegrationMessageStatus.New;

    public int Attempts { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }

    /// <summary>Phản hồi gốc đã che dữ liệu nhạy cảm.</summary>
    public string? MaskedResponse { get; set; }

    public string? BusinessErrorCode { get; set; }
    public string? BusinessErrorMessage { get; set; }

    /// <summary>Chứng từ do hệ thống đích tạo ra, nếu hợp đồng trả về.</summary>
    public string? ExternalDocumentRef { get; set; }

    /// <summary>Thông điệp bị thay bởi bản điều chỉnh sau cân lại (R-037).</summary>
    public Guid? SupersededById { get; set; }

    /// <summary>Chỉ được gửi lại tự động khi hợp đồng đã chứng minh cơ chế chống trùng.</summary>
    public bool AutoRetryAllowed { get; set; }

    public bool RequiresManualReconciliation =>
        Status is IntegrationMessageStatus.Unknown ||
        (Status == IntegrationMessageStatus.Failed && !AutoRetryAllowed);
}

/// <summary>Quyết định cho xe qua cổng (R-006). Đã gửi lệnh KHÔNG nghĩa là xe đã đi qua.</summary>
public class GateDecision : AuditableEntity
{
    public Guid TripId { get; set; }
    public Guid LaneId { get; set; }
    public Guid GateId { get; set; }
    public GateDirection Direction { get; set; }

    /// <summary>Bằng chứng xác thực danh tính tại làn (D-03).</summary>
    public string? IdentityEvidence { get; set; }
    public string? IdentityVerifiedBy { get; set; }

    /// <summary>Ảnh chụp từng điều kiện tại thời điểm quyết định.</summary>
    public string ConditionsJson { get; set; } = "{}";

    public bool Allowed { get; set; }
    public string? DenyReason { get; set; }

    public DateTimeOffset DecidedAt { get; set; }
}

/// <summary>
/// Lệnh mở barie, có command_id duy nhất và thời hạn (02_Quy_tac mục 8).
/// Lệnh hết hạn không được phát lại sau khôi phục.
/// </summary>
public class GateCommand : AuditableEntity
{
    public Guid GateDecisionId { get; set; }
    public Guid TripId { get; set; }

    public string CommandId { get; set; } = string.Empty;

    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    public GateCommandState State { get; set; } = GateCommandState.Requested;

    public string? ControllerFeedback { get; set; }

    /// <summary>Sự kiện xe thực sự qua cổng. Không suy ra từ ACK mở barie.</summary>
    public string? PassageEventRef { get; set; }
    public DateTimeOffset? PassageConfirmedAt { get; set; }
    public string? PassageConfirmedBy { get; set; }

    public bool IsExpiredAt(DateTimeOffset at) => at >= ExpiresAt;

    public bool VehicleConfirmedPassed =>
        !string.IsNullOrWhiteSpace(PassageEventRef) || PassageConfirmedAt.HasValue;
}

/// <summary>Phê duyệt ngoại lệ có thẩm quyền (R-026). Mọi duyệt phải có lý do và biên bản.</summary>
public class Approval : AuditableEntity
{
    public Guid? TripId { get; set; }

    /// <summary>REWEIGH, MANUAL_WEIGHT, BRIDGE_SWITCH, RESUME, ALLOCATION, SAMPLE_RETAKE...</summary>
    public string ActionType { get; set; } = string.Empty;

    public string RequestedBy { get; set; } = string.Empty;
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }

    public string Reason { get; set; } = string.Empty;

    /// <summary>Biên bản/tệp kèm.</summary>
    public string? DocumentRef { get; set; }

    public bool IsGranted => !string.IsNullOrWhiteSpace(ApprovedBy) && ApprovedAt.HasValue;

    /// <summary>D-08: trường hợp rủi ro cao cần người lập khác người duyệt.</summary>
    public bool SeparationOfDutiesSatisfied =>
        IsGranted && !string.Equals(RequestedBy, ApprovedBy, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Nhật ký audit trước–sau. Không xóa dấu vết số đo (R-026).</summary>
public class AuditLogEntry : AuditableEntity
{
    public string ActionType { get; set; } = string.Empty;
    public Guid? TripId { get; set; }
    public string Actor { get; set; } = string.Empty;
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? Reason { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>Sự cố và xác nhận khôi phục (R-025). Resume chỉ sau kiểm tra và xác nhận.</summary>
public class Incident : AuditableEntity
{
    public Guid? TripId { get; set; }
    public Guid SiteId { get; set; }

    public string IssueCode { get; set; } = string.Empty;

    /// <summary>Phạm vi ảnh hưởng: DEVICE, LANE, BRIDGE, TRIP, SITE (D-07).</summary>
    public string Scope { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public DateTimeOffset OpenedAt { get; set; }
    public string OpenedBy { get; set; } = string.Empty;

    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ResolvedBy { get; set; }

    /// <summary>Đã xử lý xong các giao dịch chưa rõ kết quả trước khi cho tiếp tục.</summary>
    public bool UnknownTransactionsResolved { get; set; }

    public bool CanResume => UnknownTransactionsResolved && ResolvedAt.HasValue;
}
