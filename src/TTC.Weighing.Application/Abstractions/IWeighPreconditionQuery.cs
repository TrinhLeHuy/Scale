using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Application.Abstractions;

/// <summary>
/// Điều kiện nghiệp vụ trước một lượt cân (B-001.7).
/// Lưu ý phân biệt: thiếu tạp chất là CHẶN cân lần 2 (S02 tr.21),
/// thiếu chữ đường là CẢNH BÁO, vẫn cho cân (S02 tr.20-22, R-014).
/// </summary>
public interface IWeighPreconditionQuery
{
    Task<WeighPreconditions> EvaluateAsync(
        Guid tripId, WeightRole role, int segmentIndex, CancellationToken ct = default);
}

public sealed record WeighPreconditions
{
    /// <summary>Đã có lượt cân trước hợp lệ khi lượt này yêu cầu.</summary>
    public required bool PrecedingWeightsSatisfied { get; init; }

    /// <summary>Với profile mía: đã đo tạp chất trước cân lần 2.</summary>
    public required bool ImpuritySatisfied { get; init; }

    /// <summary>Với profile vật tư: đã xác nhận vào kho trước cân lần 2.</summary>
    public required bool WarehouseSatisfied { get; init; }

    /// <summary>Chính sách FRM đạt theo hợp đồng đã xác nhận. Khác với chữ đường đang chờ.</summary>
    public required bool PolicySatisfied { get; init; }

    /// <summary>Chữ đường đang chờ — chỉ cảnh báo, KHÔNG chặn cân.</summary>
    public bool CcsPending { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];
}
