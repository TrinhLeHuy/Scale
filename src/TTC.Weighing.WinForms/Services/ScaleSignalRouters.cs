using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Infrastructure.Devices.SerialScale;
using TTC.Weighing.WinForms.Development;

namespace TTC.Weighing.WinForms.Services;

/// <summary>
/// Nguồn số cân xe cho từng bàn cân:
/// <list type="number">
/// <item>Số nhập tay đã được duyệt (đặt qua <see cref="Manual"/>) — ưu tiên, giữ nguyên luồng duyệt hiện có.</item>
/// <item>Bàn cân đã cấu hình cổng COM (màn Kết nối cân) → số đầu cân thật.</item>
/// <item>Còn lại → nguồn cũ (chưa có số cho tới khi nhập tay).</item>
/// </list>
/// Mất tín hiệu COM (quá thời gian cấu hình) trả null → màn Cân hiện "MẤT TÍN HIỆU", không giữ số cũ.
/// </summary>
public sealed class ScaleSignalRouter(IScaleConnectionService connections, SimulatedScaleSignalGateway manual, IClock clock)
    : IScaleSignalGateway
{
    /// <summary>Nguồn số nhập tay (đã duyệt). Màn Cân đặt / xóa số qua đây.</summary>
    public SimulatedScaleSignalGateway Manual => manual;

    /// <summary>Bàn cân đã nối đầu cân qua cổng COM.</summary>
    public bool HasSerial(Guid weighbridgeId) => connections.FindEnabled(weighbridgeId) is not null;

    public Task<ScaleSignalSnapshot?> GetCurrentAsync(Guid weighbridgeId, CancellationToken ct = default)
    {
        if (manual.TargetWeight is not null)
            return manual.GetCurrentAsync(weighbridgeId, ct);

        if (connections.FindEnabled(weighbridgeId) is not { } settings)
            return manual.GetCurrentAsync(weighbridgeId, ct);

        var live = connections.GetLive(settings.Key);
        if (!live.Fresh || live.Kg is not { } kg)
            return Task.FromResult<ScaleSignalSnapshot?>(null);

        var now = clock.UtcNow;
        return Task.FromResult<ScaleSignalSnapshot?>(new ScaleSignalSnapshot
        {
            DeviceId = SerialScaleHub.DeviceIdFor(settings.Key),
            RawWeight = live.RawValue ?? kg,
            RawUom = live.RawUnit ?? settings.Unit,
            NormalizedKg = kg,
            DeviceStatus = live.Status,
            StableFlag = live.StableFlag ?? live.IsStable,
            OccurredAt = live.ReceivedAt ?? now,
            ReceivedAt = live.ReceivedAt ?? now,
            StableDuration = live.StableDuration,
            ObservedDeltaKg = live.ObservedDeltaKg,
            SignalContinuous = true
        });
    }
}

/// <summary>Nguồn số cân mẫu tạp chất: cổng COM nếu đã cấu hình, không thì nguồn cũ.</summary>
public sealed class SampleScaleRouter(IScaleConnectionService connections, Infrastructure.Adapters.SampleScaleGatewayAdapter fallback)
    : ISampleScaleGateway
{
    public Task<SampleScaleSignal?> GetCurrentAsync(Guid sampleScaleId, CancellationToken ct = default)
    {
        if (connections.FindEnabled(ScaleConnectionSettings.SampleScaleKey) is null)
            return fallback.GetCurrentAsync(sampleScaleId, ct);

        var live = connections.GetLive(ScaleConnectionSettings.SampleScaleKey);
        if (!live.Fresh || live.Kg is not { } kg)
            return Task.FromResult<SampleScaleSignal?>(null);

        return Task.FromResult<SampleScaleSignal?>(new SampleScaleSignal(
            WeightKg: kg,
            IsStable: live.IsStable,
            StableDuration: live.StableDuration,
            DeviceStatus: live.Status == ScaleDeviceStatus.Unknown ? ScaleDeviceStatus.Ok : live.Status,
            OccurredAt: live.ReceivedAt ?? DateTimeOffset.UtcNow));
    }
}
