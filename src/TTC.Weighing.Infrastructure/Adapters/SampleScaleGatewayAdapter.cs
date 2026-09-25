using Microsoft.Extensions.Logging;
using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Infrastructure.Adapters;

/// <summary>
/// Stub adapter cho đầu cân mẫu tạp chất tại trạm lấy mẫu (Req 2.2, 2.3).
/// Khác với <see cref="IScaleSignalGateway"/> dùng cho bàn cân xe — đầu cân mẫu
/// có giao thức riêng tùy thiết bị tại nhà máy.
///
/// TODO: Thay thế bằng implementation thật kết nối đầu cân mẫu trong sprint tích hợp thiết bị.
/// Giao thức giao tiếp (serial/TCP/Modbus) phụ thuộc vào model cân tại nhà máy.
/// </summary>
public sealed class SampleScaleGatewayAdapter(
    IClock clock,
    ILogger<SampleScaleGatewayAdapter> logger) : ISampleScaleGateway
{
    /// <summary>
    /// Đọc tín hiệu hiện tại từ đầu cân mẫu.
    /// Stub: trả <c>null</c> — tương đương thiết bị chưa kết nối / chưa sẵn sàng.
    /// Implementation thật sẽ giao tiếp với đầu cân qua giao thức phần cứng cụ thể
    /// (serial RS-232, TCP socket, Modbus RTU/TCP, v.v.) và trả <see cref="SampleScaleSignal"/>
    /// với trọng lượng, trạng thái ổn định, và thời gian ổn định liên tục.
    /// </summary>
    public Task<SampleScaleSignal?> GetCurrentAsync(
        Guid sampleScaleId, CancellationToken ct = default)
    {
        logger.LogDebug(
            "SampleScaleGateway.GetCurrentAsync stub: sampleScaleId={SampleScaleId}. " +
            "Thiết bị chưa được tích hợp — trả null (device not ready).",
            sampleScaleId);

        // Trả null biểu thị thiết bị chưa sẵn sàng hoặc mất kết nối,
        // phù hợp với contract của ISampleScaleGateway.
        return Task.FromResult<SampleScaleSignal?>(null);
    }
}
