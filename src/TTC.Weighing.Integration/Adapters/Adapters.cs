using TTC.Weighing.Integration.Contracts;

namespace TTC.Weighing.Integration.Adapters;

/// <summary>
/// Adapter ERP. S05 là tài liệu 09/2021 — tên luồng và mã mẫu chưa chứng minh endpoint đang dùng.
/// Chỉ implement sau khi D-02 cung cấp hợp đồng đã xác minh và mapping đã test với số biết trước.
/// </summary>
public interface IErpAdapter
{
    Task<IntegrationOutcome> SubmitAsync(SubmitWeighResultRequest request, CancellationToken ct = default);

    /// <summary>Tra cứu trạng thái để giải quyết UNKNOWN. Chưa xác nhận API có hỗ trợ (D-02).</summary>
    Task<IntegrationOutcome> QueryStatusAsync(string businessKey, CancellationToken ct = default);
}

/// <summary>
/// Adapter FRM. S04 tr.92 chỉ dẫn tài liệu FRM qua liên kết ngoài — nội dung hợp đồng chưa được cung cấp.
/// Không tạo endpoint, tên field hoặc cấu trúc phản hồi FRM giả (D-02/D-09).
/// </summary>
public interface IFrmAdapter
{
    Task<IntegrationOutcome> LookupPlanAsync(string referenceNo, CancellationToken ct = default);
    Task<IntegrationOutcome> GetPolicyAsync(Guid tripId, CancellationToken ct = default);
    Task<IntegrationOutcome> SubmitAsync(SubmitWeighResultRequest request, CancellationToken ct = default);
}

/// <summary>
/// Adapter mặc định khi chưa có hợp đồng đã xác minh. Trả HELD thay vì đoán endpoint.
/// Đăng ký adapter thật sau khi D-02 hoàn tất.
/// </summary>
public sealed class UnverifiedContractErpAdapter : IErpAdapter
{
    public Task<IntegrationOutcome> SubmitAsync(
        SubmitWeighResultRequest request, CancellationToken ct = default)
        => Task.FromResult(IntegrationOutcome.HeldPendingContract($"ERP.{request.Operation}"));

    public Task<IntegrationOutcome> QueryStatusAsync(string businessKey, CancellationToken ct = default)
        => Task.FromResult(IntegrationOutcome.HeldPendingContract("ERP.QueryStatus"));
}

/// <inheritdoc cref="UnverifiedContractErpAdapter"/>
public sealed class UnverifiedContractFrmAdapter : IFrmAdapter
{
    public Task<IntegrationOutcome> LookupPlanAsync(string referenceNo, CancellationToken ct = default)
        => Task.FromResult(IntegrationOutcome.HeldPendingContract("FRM.LookupPlan"));

    public Task<IntegrationOutcome> GetPolicyAsync(Guid tripId, CancellationToken ct = default)
        => Task.FromResult(IntegrationOutcome.HeldPendingContract("FRM.GetPolicy"));

    public Task<IntegrationOutcome> SubmitAsync(
        SubmitWeighResultRequest request, CancellationToken ct = default)
        => Task.FromResult(IntegrationOutcome.HeldPendingContract($"FRM.{request.Operation}"));
}
