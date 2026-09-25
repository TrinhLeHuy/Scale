using TTC.Weighing.Domain.Common;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Domain.Rules;

namespace TTC.Weighing.Domain.Entities;

/// <summary>
/// Hồ sơ nghiệp vụ cấu hình trên lõi chung (R-001). Mía và vật tư là profile, không phải hai nhánh code.
/// Có version và hiệu lực; chỉ profile đã duyệt được dùng ở production.
/// </summary>
public class WorkflowProfile : AuditableEntity
{
    /// <summary>Mã profile, ví dụ CANE_IN, MATERIAL_IN, MATERIAL_OUT_BAGGED.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public int ProfileVersion { get; set; }

    /// <summary>Nguồn kế hoạch: FRM hoặc ERP.</summary>
    public string SourceSystem { get; set; } = string.Empty;

    public ProfileDirection Direction { get; set; }

    /// <summary>Thứ tự checkpoint bắt buộc theo profile (02_Quy_tac mục 1).</summary>
    public List<CheckpointType> Checkpoints { get; set; } = [];

    /// <summary>Vai trò cân theo thứ tự thời gian của profile này.</summary>
    public List<WeightRole> WeightRoleSequence { get; set; } = [];

    public bool GateCheckpointsRequired { get; set; }
    public bool ImpurityRequiredBeforeSecondWeight { get; set; }
    public bool WarehouseConfirmationRequired { get; set; }
    public bool BomSnapshotRequired { get; set; }

    /// <summary>Ngưỡng đã duyệt. Giá trị null nghĩa là chưa duyệt, không được chốt cân tự động.</summary>
    public WeighThresholds Thresholds { get; set; } = new();

    public DateTimeOffset? EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }

    /// <summary>Người duyệt profile. Chưa duyệt thì không dùng ở production (07_Prompt).</summary>
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }

    public bool IsApprovedForProduction => !string.IsNullOrWhiteSpace(ApprovedBy) && ApprovedAt.HasValue;

    public bool IsEffectiveAt(DateTimeOffset at) =>
        (EffectiveFrom is null || at >= EffectiveFrom) &&
        (EffectiveTo is null || at < EffectiveTo);
}
