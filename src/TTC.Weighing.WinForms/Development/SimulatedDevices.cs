using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.UseCases.Gate;
using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.WinForms.Development;

/// <summary>
/// CHỈ DÙNG CHO PHÁT TRIỂN CỤC BỘ. Không đăng ký ở môi trường vận hành.
/// Mock không chứng minh đã tích hợp thiết bị thật (07_Prompt).
///
/// Hỗ trợ giả lập tương tác: gọi <see cref="SetWeight"/> để mô phỏng xe lên/xuống cân.
/// Tín hiệu dao động vài giây rồi ổn định ở giá trị đã đặt.
/// </summary>
public sealed class SimulatedScaleSignalGateway(IClock clock) : IScaleSignalGateway
{
    /// <summary>Thời gian giả lập dao động sau khi đổi trọng lượng.</summary>
    private static readonly TimeSpan SettleAfter = TimeSpan.FromSeconds(3);

    private readonly Guid _deviceId = Guid.NewGuid();
    private readonly Random _random = new(20260911);

    /// <summary>Null = chưa có số cân (chưa nhập tay) → không trả tín hiệu.</summary>
    private decimal? _targetKg;
    private DateTimeOffset _lastChangeAt = clock.UtcNow;

    /// <summary>
    /// Đặt trọng lượng mới (giả lập xe lên/xuống cân).
    /// Tín hiệu sẽ dao động rồi ổn định sau vài giây.
    /// </summary>
    public void SetWeight(decimal kg)
    {
        _targetKg = kg;
        _lastChangeAt = clock.UtcNow;
    }

    /// <summary>Xóa số cân (xe rời cân / bắt đầu lượt mới) → hiển thị "chưa có dữ liệu".</summary>
    public void Clear()
    {
        _targetKg = null;
        _lastChangeAt = clock.UtcNow;
    }

    /// <summary>Trọng lượng mục tiêu hiện tại (null = chưa có dữ liệu).</summary>
    public decimal? TargetWeight => _targetKg;

    public Task<ScaleSignalSnapshot?> GetCurrentAsync(
        Guid weighbridgeId, CancellationToken ct = default)
    {
        if (_targetKg is not { } targetKg)
            return Task.FromResult<ScaleSignalSnapshot?>(null);

        var elapsed = clock.UtcNow - _lastChangeAt;
        var settled = elapsed > SettleAfter;
        var noise = settled ? 0m : (decimal)(_random.NextDouble() - 0.5) * 4m;
        var kg = targetKg + noise;

        var snapshot = new ScaleSignalSnapshot
        {
            DeviceId = _deviceId,
            RawWeight = kg,
            RawUom = "kg",
            NormalizedKg = kg,
            DeviceStatus = ScaleDeviceStatus.Ok,
            StableFlag = settled,
            OccurredAt = clock.UtcNow,
            ReceivedAt = clock.UtcNow,
            StableDuration = settled ? elapsed - SettleAfter : TimeSpan.Zero,
            ObservedDeltaKg = settled ? 0m : Math.Abs(noise),
            SignalContinuous = true
        };

        return Task.FromResult<ScaleSignalSnapshot?>(snapshot);
    }
}

/// <inheritdoc cref="SimulatedScaleSignalGateway"/>
public sealed class SimulatedPositionEvidenceGateway(IClock clock) : IPositionEvidenceGateway
{
    public Task<PositionEvidenceSnapshot?> GetLatestAsync(Guid laneId, CancellationToken ct = default)
        => Task.FromResult<PositionEvidenceSnapshot?>(new PositionEvidenceSnapshot
        {
            DeviceId = Guid.Empty,
            Verdict = PositionVerdict.Valid,
            Confidence = 0.94m,
            ModelVersion = "sim-0.0",
            RegionVersion = "sim-0.0",
            EvidenceRef = $"sim://evidence/{Guid.NewGuid():N}",
            OccurredAt = clock.UtcNow,
            RegionConfigValid = true
        });
}


/// <summary>
/// CHỈ DÙNG XEM TRƯỚC. Sinh sự kiện IoT và đếm bao cho một phiên đo để quan sát luồng B-011/B-012.
/// Không phải dữ liệu thiết bị thật.
/// </summary>
public sealed class PreviewDeviceEventSimulator(PreviewDataStore store, IClock clock)
{
    private const string BootSession = "preview-boot-1";

    /// <summary>Sinh dữ liệu bình thường: IoT delta cộng dồn đủ và đếm bao một chiều.</summary>
    public void EmitNormalTraffic(Guid measurementSessionId, int bagCount, decimal totalKg)
    {
        var session = store.MeasurementSessions.FirstOrDefault(s => s.Id == measurementSessionId);
        if (session is null) return;

        var iotDevice = store.Devices.First(d => d.DeviceClass == "IOT_MASS");
        var bagDevice = store.Devices.First(d => d.DeviceClass == "BAG_AI");

        var sequence = NextSequence();
        var perEvent = decimal.Round(totalKg / 10m, 3);

        for (var i = 0; i < 10; i++)
        {
            AddEvent(session, iotDevice.Id, sequence++, "IOT_MASS_READING",
                $"{{\"mode\":\"DELTA\",\"value_kg\":\"{perEvent}\",\"counter_epoch\":null," +
                "\"mass_origin\":\"MEASURED\",\"mass_basis\":\"PACKAGED_GOODS\"}");
        }

        for (var i = 0; i < bagCount; i++)
        {
            AddEvent(session, bagDevice.Id, sequence++, "BAG_PASSAGE",
                $"{{\"passage_id\":\"p-{i:D4}\",\"direction\":\"FORWARD\",\"quantity\":1," +
                $"\"item_code\":\"{session.ItemCode}\",\"confidence\":0.97}}");
        }

        session.IotMassOrigin = MassOrigin.Measured;
    }

    /// <summary>Sinh dữ liệu thiếu sequence để quan sát cảnh báo thiếu dữ liệu (B-013).</summary>
    public void EmitTrafficWithSequenceGap(Guid measurementSessionId)
    {
        var session = store.MeasurementSessions.FirstOrDefault(s => s.Id == measurementSessionId);
        if (session is null) return;

        var iotDevice = store.Devices.First(d => d.DeviceClass == "IOT_MASS");
        var sequence = NextSequence();

        AddEvent(session, iotDevice.Id, sequence, "IOT_MASS_READING",
            "{\"mode\":\"DELTA\",\"value_kg\":\"1000.000\",\"counter_epoch\":null," +
            "\"mass_origin\":\"MEASURED\",\"mass_basis\":\"PACKAGED_GOODS\"}");

        // Cố tình nhảy sequence để tạo gap.
        AddEvent(session, iotDevice.Id, sequence + 5, "IOT_MASS_READING",
            "{\"mode\":\"DELTA\",\"value_kg\":\"1000.000\",\"counter_epoch\":null," +
            "\"mass_origin\":\"MEASURED\",\"mass_basis\":\"PACKAGED_GOODS\"}");

        session.IotMassOrigin = MassOrigin.Measured;
    }

    /// <summary>Sinh bộ đếm bị reset để quan sát yêu cầu tạm giữ phiên (B-011).</summary>
    public void EmitCounterReset(Guid measurementSessionId)
    {
        var session = store.MeasurementSessions.FirstOrDefault(s => s.Id == measurementSessionId);
        if (session is null) return;

        var iotDevice = store.Devices.First(d => d.DeviceClass == "IOT_MASS");
        var sequence = NextSequence();

        AddEvent(session, iotDevice.Id, sequence, "IOT_MASS_READING",
            "{\"mode\":\"CUMULATIVE\",\"value_kg\":\"5000.000\",\"counter_epoch\":\"epoch-1\"," +
            "\"mass_origin\":\"MEASURED\",\"mass_basis\":\"PACKAGED_GOODS\"}");

        AddEvent(session, iotDevice.Id, sequence + 1, "IOT_MASS_READING",
            "{\"mode\":\"CUMULATIVE\",\"value_kg\":\"100.000\",\"counter_epoch\":\"epoch-2\"," +
            "\"mass_origin\":\"MEASURED\",\"mass_basis\":\"PACKAGED_GOODS\"}");

        session.IotMassOrigin = MassOrigin.Measured;
    }

    private long NextSequence()
        => store.DeviceEvents.Count == 0 ? 1 : store.DeviceEvents.Max(e => e.Sequence) + 1;

    private void AddEvent(
        MeasurementSession session, Guid deviceId, long sequence, string eventType, string payload)
    {
        var now = clock.UtcNow;

        store.DeviceEvents.Add(new DeviceEvent
        {
            EventId = $"{deviceId:N}-{sequence}",
            DeviceId = deviceId,
            BootSessionId = BootSession,
            Sequence = sequence,
            EventType = eventType,
            SiteId = store.Site.Id,
            LaneId = store.Lane.Id,
            OccurredAt = now,
            ReceivedAt = now,
            CorrelationId = Guid.NewGuid().ToString("N"),
            TripId = session.TripId,
            MeasurementSessionId = session.Id,
            PayloadJson = payload,
            PayloadHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(payload))),
            ProcessingStatus = DeviceEventProcessingStatus.Applied,
            CreatedAt = now,
            CreatedBy = "preview.simulator"
        });
    }
}

/// <summary>
/// Bộ điều khiển barie giả lập cho chế độ chạy trên cơ sở dữ liệu thật.
/// Không gửi lệnh tới thiết bị thật và không bỏ qua cảm biến an toàn của nhà máy.
/// </summary>
public sealed class SimulatedBarrierControllerGateway : IBarrierControllerGateway
{
    public Task<BarrierControllerState> GetStateAsync(Guid gateId, CancellationToken ct = default)
        => Task.FromResult(new BarrierControllerState(
            Ready: true, SafetyConditionsMet: true, GateMatchesLane: true));

    public Task RequestOpenAsync(
        Guid gateId, string commandId, DateTimeOffset expiresAt, CancellationToken ct = default)
        => Task.CompletedTask;
}

/// <summary>
/// Xác thực danh tính xe bằng xác nhận của nhân viên tại làn.
/// Đây là phương án khi chưa có ANPR đã nghiệm thu; D-03 phải chốt cách làm chính thức.
/// </summary>
public sealed class LaneConfirmationIdentityVerifier : IVehicleIdentityVerifier
{
    public Task<VehicleIdentityResult> VerifyAsync(
        Guid tripId, Guid laneId, CancellationToken ct = default)
        => Task.FromResult(new VehicleIdentityResult(
            true, "LANE_OPERATOR_CONFIRMATION", "local.operator"));
}

/// <summary>
/// CHỈ DÙNG CHO PHÁT TRIỂN CỤC BỘ. Giả lập đầu cân mẫu tạp chất.
/// Gọi <see cref="SetWeight"/> để mô phỏng đặt/lấy mẫu trên cân.
/// Tín hiệu dao động rồi ổn định sau 3 giây.
/// Mock không chứng minh đã tích hợp đầu cân mẫu thật (07_Prompt).
/// </summary>
public sealed class SimulatedSampleScaleGateway(IClock clock) : ISampleScaleGateway
{
    private static readonly TimeSpan SettleAfter = TimeSpan.FromSeconds(3);

    private readonly Random _random = new(20260912);

    private decimal _targetKg = 0m;
    private DateTimeOffset _lastChangeAt = clock.UtcNow;

    /// <summary>
    /// Đặt trọng lượng mới (giả lập đặt/lấy mẫu trên cân).
    /// Truyền 0 = lấy mẫu ra, &gt;0 = đặt mẫu lên.
    /// </summary>
    public void SetWeight(decimal kg)
    {
        _targetKg = kg;
        _lastChangeAt = clock.UtcNow;
    }

    /// <summary>Trọng lượng mục tiêu hiện tại.</summary>
    public decimal TargetWeight => _targetKg;

    public Task<SampleScaleSignal?> GetCurrentAsync(
        Guid sampleScaleId, CancellationToken ct = default)
    {
        var elapsed = clock.UtcNow - _lastChangeAt;
        var settled = elapsed > SettleAfter;
        var noise = settled ? 0m : (decimal)(_random.NextDouble() - 0.5) * 0.2m;
        var kg = _targetKg + noise;
        if (kg < 0) kg = 0;

        var signal = new SampleScaleSignal(
            WeightKg: kg,
            IsStable: settled,
            StableDuration: settled ? elapsed - SettleAfter : TimeSpan.Zero,
            DeviceStatus: ScaleDeviceStatus.Ok,
            OccurredAt: clock.UtcNow);

        return Task.FromResult<SampleScaleSignal?>(signal);
    }
}
