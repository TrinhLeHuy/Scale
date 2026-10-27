namespace TTC.Weighing.Application.Queries;

/// <summary>Một lượt gọi API ra hệ thống ngoài (đẩy / kéo) — dòng trên lưới nhật ký.</summary>
public sealed record ApiCallLogRow(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Direction,       // PUSH | PULL
    string System,          // DIGISCALE | FRM | ERP | PORTAL | OTHER
    string Operation,       // logicalName (new_phieucan...) hoặc endpoint
    string? BusinessKey,    // số phiếu / số đăng tài / mã lệnh đốn
    bool IsPolicy,          // gọi bắt chính sách (IsQuetCS = Yes)
    int? HttpStatus,
    bool Success,
    int DurationMs,
    string? Error);

/// <summary>Chi tiết một lượt gọi: URL, nội dung gửi / nhận (đã che mật khẩu, token).</summary>
public sealed record ApiCallLogDetail(
    ApiCallLogRow Row,
    string Method,
    string Url,
    string? RequestBody,
    string? ResponseBody,
    string? Actor,
    string? ClientName);

public sealed record ApiCallLogFilter(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string? System = null,
    string? Direction = null,
    bool? Success = null,
    bool PolicyOnly = false,
    string? Search = null,
    int Limit = 1000,
    IReadOnlyList<string>? AnyOf = null); // lọc theo chuyến: số đăng tài / lệnh đốn (khớp mã chứng từ hoặc nội dung gửi)

public interface IApiCallLogQueries
{
    Task<IReadOnlyList<ApiCallLogRow>> ListAsync(ApiCallLogFilter filter, CancellationToken ct = default);

    Task<ApiCallLogDetail?> GetAsync(Guid id, CancellationToken ct = default);
}
