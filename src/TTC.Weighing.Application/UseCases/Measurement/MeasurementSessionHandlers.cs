using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Domain.Rules;

namespace TTC.Weighing.Application.UseCases.Measurement;

public sealed record OpenMeasurementSessionCommand(
    Guid TripId,
    Guid WarehouseId,
    Guid? ConveyorLaneId,
    string ItemCode,
    Guid? DocumentLineId,
    IReadOnlyList<Guid> DeviceIds,
    MassBasis MassBasis,
    IotReadingMode ReadingMode,
    string? CounterEpoch,
    decimal? CounterStart,
    string CorrelationId);

public sealed record OpenMeasurementSessionResult(
    bool IsSuccess, Guid? SessionId, string? FailureReason);

/// <summary>
/// B-010 — Tạo phiên đo trước khi nhận dữ liệu IoT/đếm bao (R-017, R-018).
/// Mã hàng bị khóa bởi phiên kho, AI không tự nhận diện SKU.
/// </summary>
public sealed class OpenMeasurementSessionHandler(
    ITripRepository trips,
    IMeasurementSessionRepository sessions,
    IUnitOfWork uow,
    IClock clock,
    ICurrentUser user,
    IAuditLogger audit)
{
    public const string RequiredPermission = "warehouse.session.open";

    public async Task<OpenMeasurementSessionResult> HandleAsync(
        OpenMeasurementSessionCommand cmd, CancellationToken ct = default)
    {
        var trip = await trips.GetByIdAsync(cmd.TripId, ct);
        if (trip is null)
            return new(false, null, "Chuyến không tồn tại.");

        if (!user.HasPermission(RequiredPermission, trip.SiteId))
            return new(false, null, "Không có quyền mở phiên đo tại nhà máy này.");

        if (!trip.IsOperable)
            return new(false, null,
                $"Chuyến không cho phép thao tác: {trip.Lifecycle} / {trip.ExecutionStatus}.");

        if (string.IsNullOrWhiteSpace(cmd.ItemCode))
            return new(false, null, "Mã hàng bắt buộc — phiên kho khóa mã hàng cho dữ liệu AI.");

        if (cmd.DeviceIds.Count == 0)
            return new(false, null, "Phải khai báo thiết bị được phép ghi vào phiên.");

        // CUMULATIVE bắt buộc có counter_epoch để xác định chu kỳ bộ đếm (B-011).
        if (cmd.ReadingMode == IotReadingMode.Cumulative && string.IsNullOrWhiteSpace(cmd.CounterEpoch))
            return new(false, null, "Chế độ CUMULATIVE bắt buộc khai báo counter_epoch.");

        // Một nguồn đo vật lý không gán đồng thời cho hai chuyến (B-010).
        foreach (var deviceId in cmd.DeviceIds)
        {
            var busy = await sessions.GetOpenSessionByDeviceAsync(deviceId, ct);
            if (busy is not null)
                return new(false, null,
                    $"Thiết bị {deviceId} đang thuộc phiên đo của chuyến {busy.TripId}.");
        }

        var session = new MeasurementSession
        {
            TripId = cmd.TripId,
            WarehouseId = cmd.WarehouseId,
            ConveyorLaneId = cmd.ConveyorLaneId,
            ItemCode = cmd.ItemCode,
            DocumentLineId = cmd.DocumentLineId,
            DeviceIds = [.. cmd.DeviceIds],
            MassBasis = cmd.MassBasis,
            ReadingMode = cmd.ReadingMode,
            CounterEpoch = cmd.CounterEpoch,
            CounterStart = cmd.CounterStart,
            Status = MeasurementSessionStatus.Open,
            OpenedAt = clock.UtcNow,
            CreatedAt = clock.UtcNow,
            CreatedBy = user.UserId
        };

        await sessions.AddAsync(session, ct);

        await audit.LogAsync("MEASUREMENT_SESSION_OPENED", cmd.TripId, user.UserId, null,
            $"{{\"sessionId\":\"{session.Id}\",\"itemCode\":\"{cmd.ItemCode}\",\"mode\":\"{cmd.ReadingMode}\"}}",
            null, cmd.CorrelationId, ct);

        await uow.SaveChangesAsync(ct);

        return new(true, session.Id, null);
    }
}

public sealed record CloseMeasurementSessionCommand(
    Guid SessionId,
    bool ReverseDirectionAccepted,
    string CorrelationId);

public sealed record CloseMeasurementSessionResult(
    bool IsSuccess,
    MeasurementSessionStatus Status,
    decimal? IotMassKg,
    int? NetBagCount,
    bool DataComplete,
    IReadOnlyList<string> Issues);

/// <summary>
/// Đóng phiên đo, tổng hợp số liệu và kiểm tra đầy đủ dữ liệu (B-011, B-012, B-013).
/// Thiếu dữ liệu thì đóng ở trạng thái ClosedIncomplete, không tự điền 0.
/// </summary>
public sealed class CloseMeasurementSessionHandler(
    IMeasurementSessionRepository sessions,
    IDeviceEventRepository deviceEvents,
    IDeviceEventPayloadReader payloadReader,
    IUnitOfWork uow,
    IClock clock,
    ICurrentUser user,
    IAuditLogger audit)
{
    public const string RequiredPermission = "warehouse.session.close";

    public async Task<CloseMeasurementSessionResult> HandleAsync(
        CloseMeasurementSessionCommand cmd, CancellationToken ct = default)
    {
        var session = await sessions.GetByIdAsync(cmd.SessionId, ct);
        if (session is null)
            return Fail("Phiên đo không tồn tại.");

        if (!user.HasPermission(RequiredPermission, Guid.Empty) &&
            !user.HasPermission(RequiredPermission, session.WarehouseId))
            return Fail("Không có quyền đóng phiên đo.");

        if (session.Status is MeasurementSessionStatus.Closed or MeasurementSessionStatus.ClosedIncomplete)
            return Fail("Phiên đo đã đóng.");

        var events = await deviceEvents.ListBySessionAsync(cmd.SessionId, ct);
        var issues = new List<string>();

        // Kiểm tra dải sequence để phát hiện thiếu dữ liệu (B-013).
        var appliedEvents = events
            .Where(e => e.ProcessingStatus == DeviceEventProcessingStatus.Applied)
            .ToList();

        if (MeasurementAggregationRules.HasSequenceGap(
                appliedEvents.Select(e => e.Sequence), out var firstMissing))
        {
            issues.Add($"Thiếu sự kiện tại sequence {firstMissing} — dữ liệu phiên chưa đầy đủ.");
        }

        var iotReadings = payloadReader.ReadIotReadings(appliedEvents);
        var bagPassages = payloadReader.ReadBagPassages(appliedEvents);

        decimal? iotMass = null;
        if (iotReadings.Count > 0)
        {
            var aggregate = session.ReadingMode == IotReadingMode.Cumulative
                ? MeasurementAggregationRules.AggregateCumulative(iotReadings)
                : MeasurementAggregationRules.AggregateDelta(iotReadings);

            if (aggregate.IsUsable)
            {
                iotMass = aggregate.TotalKg;
            }
            else
            {
                issues.Add(aggregate.HoldReason ?? "Số đo IoT không dùng được.");

                if (aggregate.RequiresHold)
                {
                    session.Status = MeasurementSessionStatus.Held;
                    session.HoldReason = aggregate.HoldReason;

                    await audit.LogAsync("MEASUREMENT_SESSION_HELD", session.TripId, user.UserId,
                        null, $"{{\"reason\":\"{aggregate.HoldReason}\"}}", null, cmd.CorrelationId, ct);

                    await uow.SaveChangesAsync(ct);

                    return new(false, MeasurementSessionStatus.Held, null, null, false, issues);
                }
            }
        }

        int? netBags = null;
        if (bagPassages.Count > 0)
        {
            var bagResult = MeasurementAggregationRules.AggregateBagPassages(
                bagPassages, cmd.ReverseDirectionAccepted);

            session.ForwardBagCount = bagResult.ForwardCount;
            session.ReverseBagCount = bagResult.ReverseCount;

            if (bagResult.IsUsable)
                netBags = bagResult.NetCount;
            else
                issues.Add(bagResult.HoldReason ?? "Số đếm bao không dùng được.");
        }

        session.IotMassKg = iotMass;
        session.NetBagCount = netBags;
        session.SequenceWatermark = appliedEvents.Count > 0 ? appliedEvents.Max(e => e.Sequence) : null;
        session.DataComplete = issues.Count == 0;
        session.Status = issues.Count == 0
            ? MeasurementSessionStatus.Closed
            : MeasurementSessionStatus.ClosedIncomplete;
        session.ClosedAt = clock.UtcNow;
        session.UpdatedAt = clock.UtcNow;
        session.UpdatedBy = user.UserId;

        await audit.LogAsync("MEASUREMENT_SESSION_CLOSED", session.TripId, user.UserId, null,
            $"{{\"status\":\"{session.Status}\",\"iotKg\":{iotMass?.ToString() ?? "null"}," +
            $"\"netBags\":{netBags?.ToString() ?? "null"}}}",
            null, cmd.CorrelationId, ct);

        await uow.SaveChangesAsync(ct);

        return new(true, session.Status, iotMass, netBags, session.DataComplete, issues);
    }

    private static CloseMeasurementSessionResult Fail(string reason)
        => new(false, MeasurementSessionStatus.Open, null, null, false, [reason]);
}

/// <summary>
/// Đọc payload sự kiện thiết bị đã chuẩn hóa. Adapter chịu trách nhiệm chuyển định dạng thật
/// sang cấu trúc này sau khi test mapping (03_Du_lieu mục 4).
/// </summary>
public interface IDeviceEventPayloadReader
{
    IReadOnlyList<IotReading> ReadIotReadings(IEnumerable<DeviceEvent> events);

    IReadOnlyList<(string PassageId, bool IsForward)> ReadBagPassages(IEnumerable<DeviceEvent> events);
}
