using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.UseCases.Gate;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Scenario;

/// <summary>
/// Đầu cân theo kịch bản. Kịch bản điều khiển giá trị và thời gian ổn định để kiểm chứng B-001.
/// Đây KHÔNG phải adapter thiết bị thật; tích hợp đầu cân thật cần test giao thức tại hiện trường.
/// </summary>
public sealed class ScriptedScaleGateway(IClock clock) : IScaleSignalGateway
{
    public Guid DeviceId { get; set; } = Guid.NewGuid();
    public decimal CurrentKg { get; set; }
    public TimeSpan StableDuration { get; set; }
    public decimal ObservedDeltaKg { get; set; }
    public ScaleDeviceStatus DeviceStatus { get; set; } = ScaleDeviceStatus.Ok;
    public bool SignalContinuous { get; set; } = true;

    public Task<ScaleSignalSnapshot?> GetCurrentAsync(
        Guid weighbridgeId, CancellationToken ct = default)
        => Task.FromResult<ScaleSignalSnapshot?>(new ScaleSignalSnapshot
        {
            DeviceId = DeviceId,
            RawWeight = CurrentKg,
            RawUom = "kg",
            NormalizedKg = CurrentKg,
            DeviceStatus = DeviceStatus,
            StableFlag = StableDuration > TimeSpan.Zero,
            OccurredAt = clock.UtcNow,
            ReceivedAt = clock.UtcNow,
            StableDuration = StableDuration,
            ObservedDeltaKg = ObservedDeltaKg,
            SignalContinuous = SignalContinuous
        });
}

/// <summary>Bằng chứng AI vị trí theo kịch bản. Ngưỡng thật cần D-05.</summary>
public sealed class ScriptedPositionGateway(IClock clock) : IPositionEvidenceGateway
{
    public PositionVerdict Verdict { get; set; } = PositionVerdict.Valid;
    public decimal? Confidence { get; set; } = 0.96m;
    public bool RegionConfigValid { get; set; } = true;

    public Task<PositionEvidenceSnapshot?> GetLatestAsync(
        Guid laneId, CancellationToken ct = default)
        => Task.FromResult<PositionEvidenceSnapshot?>(new PositionEvidenceSnapshot
        {
            DeviceId = Guid.Empty,
            Verdict = Verdict,
            Confidence = Confidence,
            ModelVersion = "scenario-0.0",
            RegionVersion = "scenario-0.0",
            EvidenceRef = $"scenario://position/{Guid.NewGuid():N}",
            OccurredAt = clock.UtcNow,
            RegionConfigValid = RegionConfigValid
        });
}

/// <summary>
/// Xác thực danh tính xe bằng xác nhận của nhân viên tại làn.
/// Đây là phương án khi chưa có ANPR đã nghiệm thu; D-03 phải chốt cách làm chính thức.
/// </summary>
public sealed class LaneOperatorIdentityVerifier : IVehicleIdentityVerifier
{
    public bool Confirmed { get; set; } = true;
    public string ConfirmedBy { get; set; } = "scenario.gate.operator";

    public Task<VehicleIdentityResult> VerifyAsync(
        Guid tripId, Guid laneId, CancellationToken ct = default)
        => Task.FromResult(Confirmed
            ? new VehicleIdentityResult(true, "LANE_OPERATOR_CONFIRMATION", ConfirmedBy)
            : new VehicleIdentityResult(false, null, null));
}

/// <summary>Bộ điều khiển barie theo kịch bản. Không gửi lệnh tới thiết bị thật.</summary>
public sealed class ScriptedBarrierGateway : IBarrierControllerGateway
{
    public bool Ready { get; set; } = true;
    public bool SafetyConditionsMet { get; set; } = true;

    public List<string> IssuedCommands { get; } = [];

    public Task<BarrierControllerState> GetStateAsync(Guid gateId, CancellationToken ct = default)
        => Task.FromResult(new BarrierControllerState(Ready, SafetyConditionsMet, true));

    public Task RequestOpenAsync(
        Guid gateId, string commandId, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        IssuedCommands.Add(commandId);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Người dùng của kịch bản. Vận hành thật phải lấy từ nguồn xác thực và phân quyền đã duyệt (D-08).
/// </summary>
public sealed class ScenarioUser(Guid siteId) : ICurrentUser
{
    public string UserId => "scenario.operator";
    public string DisplayName => "Người chạy kịch bản";

    public IReadOnlyCollection<string> Roles =>
        ["GATE", "WEIGH_OPERATOR", "SUPERVISOR", "WAREHOUSE", "RECONCILER", "IT"];

    public IReadOnlyCollection<Guid> AllowedSiteIds => [siteId, Guid.Empty];

    public bool HasPermission(string permission, Guid siteId) => AllowedSiteIds.Contains(siteId);
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>Cấp số thứ tự hàng chờ cho kịch bản. Atomic trong bộ nhớ.</summary>
public sealed class ScenarioQueueSequenceGenerator : Application.UseCases.Trips.IQueueSequenceGenerator
{
    private int _counter;

    public Task<int> NextAsync(Guid siteId, VehicleType vehicleType, CancellationToken ct = default)
        => Task.FromResult(Interlocked.Increment(ref _counter));
}

/// <summary>Camera stub cho kịch bản. Không chụp ảnh thật.</summary>
public sealed class ScenarioCameraService : ICameraService
{
    public Task<string?> CaptureSnapshotAsync(
        Guid gateId, string cameraPosition, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    public Task<CameraStatus> GetStatusAsync(
        Guid gateId, string cameraPosition, CancellationToken ct = default)
        => Task.FromResult(new CameraStatus(IsOnline: true, LastError: null));
}
