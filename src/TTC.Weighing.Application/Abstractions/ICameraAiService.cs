namespace TTC.Weighing.Application.Abstractions;

/// <summary>
/// Dịch vụ AI phân tích hình ảnh camera tại bàn cân.
/// Phát hiện người trên xe, kiểm tra thùng hàng, xác nhận vị trí xe trên cân.
/// Kết quả AI KHÔNG tự động chặn cân — UI hiển thị cảnh báo, server quyết định chốt cân.
///
/// Khi có API camera thật, implement interface này thay cho SimulatedCameraAiService.
/// </summary>
public interface ICameraAiService
{
    /// <summary>
    /// Phân tích hình ảnh camera tại bàn cân. Trả kết quả đánh giá.
    /// Gọi định kỳ hoặc khi cần kiểm tra trước chốt cân.
    /// </summary>
    Task<CameraAiAnalysisResult> AnalyzeAsync(
        Guid weighbridgeId, CancellationToken ct = default);
}

/// <summary>
/// Kết quả phân tích AI camera tại bàn cân.
/// </summary>
public sealed record CameraAiAnalysisResult(
    /// <summary>Phát hiện người trên xe hoặc gần xe trên cân.</summary>
    bool PersonDetected,

    /// <summary>Mức độ tin cậy phát hiện người (0.0 – 1.0). Null nếu không áp dụng.</summary>
    decimal? PersonConfidence,

    /// <summary>Xe nằm ngay ngắn trên bàn cân (không lệch, không vượt quá mép).</summary>
    VehicleAlignmentStatus Alignment,

    /// <summary>Mức độ tin cậy đánh giá vị trí xe (0.0 – 1.0).</summary>
    decimal? AlignmentConfidence,

    /// <summary>Trạng thái thùng hàng / bạt phủ.</summary>
    CargoStatus CargoStatus,

    /// <summary>Mức độ tin cậy đánh giá thùng hàng (0.0 – 1.0).</summary>
    decimal? CargoConfidence,

    /// <summary>Thời điểm phân tích.</summary>
    DateTimeOffset AnalyzedAt,

    /// <summary>Phiên bản model AI.</summary>
    string ModelVersion,

    /// <summary>Đường dẫn ảnh gốc dùng để phân tích (có thể null nếu stream).</summary>
    string? SnapshotRef,

    /// <summary>Có cảnh báo nào cần hiển thị không.</summary>
    bool HasWarnings);

/// <summary>Trạng thái vị trí xe trên bàn cân.</summary>
public enum VehicleAlignmentStatus
{
    /// <summary>Xe ngay ngắn, nằm hoàn toàn trên bàn cân.</summary>
    Aligned,

    /// <summary>Xe lệch nhẹ nhưng vẫn chấp nhận được.</summary>
    SlightlyMisaligned,

    /// <summary>Xe lệch nhiều — cảnh báo ảnh hưởng số cân.</summary>
    Misaligned,

    /// <summary>Không phát hiện xe trên cân.</summary>
    NoVehicle,

    /// <summary>Không thể đánh giá (camera lỗi, tầm nhìn bị che).</summary>
    Unknown
}

/// <summary>Trạng thái thùng hàng / bạt phủ xe.</summary>
public enum CargoStatus
{
    /// <summary>Thùng hàng mở, sẵn sàng cân / kiểm tra.</summary>
    Open,

    /// <summary>Thùng hàng đóng / bạt phủ kín.</summary>
    Closed,

    /// <summary>Thùng hàng mở một phần.</summary>
    PartiallyOpen,

    /// <summary>Xe trống (không có hàng).</summary>
    Empty,

    /// <summary>Không thể xác định.</summary>
    Unknown
}
