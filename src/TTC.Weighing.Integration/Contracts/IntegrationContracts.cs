using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Integration.Contracts;

/// <summary>
/// Kết quả gọi hệ thống ngoài. Phân biệt rõ 4 tình huống, không ép về true/false (03_Du_lieu mục 6).
/// HTTP 200 không tự đồng nghĩa nghiệp vụ thành công.
/// </summary>
public sealed record IntegrationOutcome
{
    public required IntegrationMessageStatus Status { get; init; }

    /// <summary>Payload phản hồi gốc, đã che dữ liệu nhạy cảm.</summary>
    public string? MaskedResponse { get; init; }

    public string? BusinessErrorCode { get; init; }
    public string? BusinessErrorMessage { get; init; }

    /// <summary>Tham chiếu do hệ thống đích trả về, nếu hợp đồng có.</summary>
    public string? ExternalReference { get; init; }

    public static IntegrationOutcome Acknowledged(string? externalRef = null) =>
        new() { Status = IntegrationMessageStatus.Acknowledged, ExternalReference = externalRef };

    public static IntegrationOutcome BusinessFailure(string code, string message) =>
        new() { Status = IntegrationMessageStatus.Failed, BusinessErrorCode = code, BusinessErrorMessage = message };

    /// <summary>Timeout hoặc mất phản hồi. Phải tra cứu/đối soát trước khi gửi lại (R-024).</summary>
    public static IntegrationOutcome Unknown(string? detail = null) =>
        new() { Status = IntegrationMessageStatus.Unknown, BusinessErrorMessage = detail };

    /// <summary>Chưa có hợp đồng API đã xác minh — giữ chờ xử lý, không tự tạo endpoint (R-023, R-037).</summary>
    public static IntegrationOutcome HeldPendingContract(string operation) =>
        new()
        {
            Status = IntegrationMessageStatus.Held,
            BusinessErrorCode = "CONTRACT_NOT_VERIFIED",
            BusinessErrorMessage =
                $"Operation '{operation}' chưa có hợp đồng API đã xác minh (D-02). Thông điệp giữ ở trạng thái HELD."
        };
}

/// <summary>Yêu cầu gửi kết quả cân. BusinessKey đảm bảo chống lặp (03_Du_lieu mục 7).</summary>
public sealed record SubmitWeighResultRequest
{
    public required Guid TripId { get; init; }
    public required int Revision { get; init; }

    /// <summary>Khóa nghiệp vụ: hệ thống đích + trip + loại kết quả + revision + chứng từ/dòng.</summary>
    public required string BusinessKey { get; init; }

    public required string Operation { get; init; }
    public required string PayloadJson { get; init; }
    public string? InterfaceReference { get; init; }
}
