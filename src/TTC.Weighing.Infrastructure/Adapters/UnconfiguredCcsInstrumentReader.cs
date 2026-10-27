using TTC.Weighing.Application.Abstractions;

namespace TTC.Weighing.Infrastructure.Adapters;

/// <summary>
/// Stub máy đo Brix/Pol: chưa có thiết bị/giao thức nên luôn trả "chưa kết nối".
/// Không bịa số đo. Thay bằng adapter thật (COM/TCP) khi có máy.
/// </summary>
public sealed class UnconfiguredCcsInstrumentReader : ICcsInstrumentReader
{
    public Task<CcsInstrumentReading> ReadAsync(CcsInstrument instrument, CancellationToken ct = default)
        => Task.FromResult(new CcsInstrumentReading(null, false, "NOT_CONFIGURED", null));
}
