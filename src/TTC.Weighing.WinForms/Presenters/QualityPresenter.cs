using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.UseCases.Quality;

namespace TTC.Weighing.WinForms.Presenters;

/// <summary>Dữ liệu hiển thị tín hiệu cân mẫu, đã tính độ mới tại tầng presenter.</summary>
public sealed record SampleScaleView(
    decimal? WeightKg,
    bool IsStable,
    TimeSpan StableDuration,
    bool SignalFresh);

/// <summary>
/// Presenter cho UI-05 (QualityForm). Form không gọi thẳng handler/gateway.
/// Mọi quyết định nghiệp vụ do handler ở Application quyết định.
/// </summary>
public sealed class QualityPresenter(
    IQrScanHandler qrScanHandler,
    ISampleScaleGateway sampleScaleGateway,
    RecordImpuritySampleHandler recordImpurity,
    ReceiveCcsResultHandler receiveCcs,
    IClock clock,
    TTC.Weighing.Infrastructure.Adapters.DigiScaleTicketAdapter digiScaleTickets)
{
    /// <summary>Độ mới tối đa dùng riêng cho hiển thị cân mẫu. Không dùng làm điều kiện ghi kết quả.</summary>
    private static readonly TimeSpan DisplayStaleAfter = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Quét QR tại trạm lấy mẫu, tra cứu Trip và hiển thị thông tin chuyến mía.
    /// </summary>
    public Task<QrScanResult> ScanQrAsync(
        string qrContent, Guid siteId, CancellationToken ct = default)
        => qrScanHandler.DecodeAndLookupAsync(qrContent, siteId, ct);

    /// <summary>
    /// Đọc tín hiệu hiện tại từ đầu cân mẫu tạp chất.
    /// Trả view model đã tính độ mới cho hiển thị.
    /// </summary>
    public async Task<SampleScaleView> ReadSampleScaleAsync(
        Guid sampleScaleId, CancellationToken ct = default)
    {
        var signal = await sampleScaleGateway.GetCurrentAsync(sampleScaleId, ct);

        if (signal is null)
            return new SampleScaleView(null, false, TimeSpan.Zero, false);

        var fresh = clock.UtcNow - signal.OccurredAt <= DisplayStaleAfter;

        return new SampleScaleView(
            WeightKg: signal.WeightKg,
            IsStable: signal.IsStable,
            StableDuration: signal.StableDuration,
            SignalFresh: fresh);
    }

    /// <summary>
    /// Ghi kết quả cân tạp chất (mẫu trước + mẫu sau).
    /// Handler sẽ validate, tính tỷ lệ, lưu QualitySample.
    /// </summary>
    public Task<RecordImpuritySampleResult> RecordImpurityAsync(
        RecordImpuritySampleCommand cmd, CancellationToken ct = default)
        => recordImpurity.HandleAsync(cmd, ct);

    /// <summary>
    /// Tiếp nhận kết quả chữ đường (CCS). Có thể đến sau khi xe đã ra cổng.
    /// Handler sẽ validate, xác định status, lưu QualitySample.
    /// </summary>
    public Task<ReceiveCcsResultResult> RecordCcsAsync(
        ReceiveCcsResultCommand cmd, CancellationToken ct = default)
        => receiveCcs.HandleAsync(cmd, ct);

    /// <summary>Push phiếu tạp chất lên DigiScale (bước 8).</summary>
    public Task<TTC.Weighing.Infrastructure.Adapters.TicketResult> PushTapChatAsync(
        string lenhDon, string sttCan, string lanDo, DateTime ngay,
        string klBefore, string klAfter, string tapChatThucTe, CancellationToken ct = default)
        => digiScaleTickets.PushTapChatAsync(lenhDon, sttCan, lanDo, ngay, klBefore, klAfter, tapChatThucTe, ct);

    /// <summary>Push phiếu CCS lên DigiScale (bước 8).</summary>
    public Task<TTC.Weighing.Infrastructure.Adapters.TicketResult> PushCcsAsync(
        string lenhDon, string sttCan, string lanDo, DateTime ngay,
        string brix, string ccsDo, string pol, string polDoc, string caLamViec, CancellationToken ct = default)
        => digiScaleTickets.PushCcsAsync(lenhDon, sttCan, lanDo, ngay, brix, ccsDo, pol, polDoc, caLamViec, ct);
}
