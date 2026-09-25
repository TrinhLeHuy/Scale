namespace TTC.Weighing.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>
/// Người dùng hiện tại. Quyền phải kiểm tra ở server, không dựa vào việc UI ẩn nút (04_Man_hinh mục 1).
/// </summary>
public interface ICurrentUser
{
    string UserId { get; }
    string DisplayName { get; }
    IReadOnlyCollection<string> Roles { get; }
    IReadOnlyCollection<Guid> AllowedSiteIds { get; }

    bool HasPermission(string permission, Guid siteId);
}

/// <summary>Audit trước–sau cho mọi hành động quan trọng (R-026). Không xóa dấu vết số đo.</summary>
public interface IAuditLogger
{
    Task LogAsync(
        string actionType,
        Guid? tripId,
        string actor,
        string? beforeJson,
        string? afterJson,
        string? reason,
        string correlationId,
        CancellationToken ct = default);
}

/// <summary>
/// Kiểm tra giao dịch tích hợp còn ở trạng thái UNKNOWN (R-024).
/// Còn treo thì không phát sinh lượt cân/lệnh barie mới.
/// </summary>
public interface IIntegrationStateQuery
{
    Task<bool> HasUnresolvedUnknownAsync(Guid tripId, CancellationToken ct = default);
}

/// <summary>
/// Xác thực danh tính xe theo D-03. Chưa có ANPR nghiệm thu thì dùng xác nhận của nhân viên tại làn.
/// </summary>
public interface IVehicleIdentityVerifier
{
    Task<VehicleIdentityResult> VerifyAsync(Guid tripId, Guid laneId, CancellationToken ct = default);
}

public sealed record VehicleIdentityResult(bool Verified, string? Method, string? VerifiedBy);
