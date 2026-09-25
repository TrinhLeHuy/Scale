using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Infrastructure.Persistence;

namespace TTC.Weighing.Infrastructure.Adapters;

/// <summary>
/// Giám sát trạng thái kết nối thiết bị bằng cách poll sự kiện gần nhất từ DB.
/// UI gọi mỗi 2 giây — query phải nhẹ (Req 13.1, 13.2, 13.4).
/// </summary>
public sealed class DeviceStatusMonitorAdapter(
    WeighingDbContext db,
    IClock clock) : IDeviceStatusMonitor
{
    /// <summary>
    /// Ngưỡng tín hiệu cũ (giây) theo loại thiết bị.
    /// Đầu cân gửi liên tục (~250ms), camera ít hơn (~5s heartbeat).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, TimeSpan> OnlineThresholds =
        new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase)
        {
            ["SCALE"] = TimeSpan.FromSeconds(10),
            ["QR"] = TimeSpan.FromSeconds(15),
            ["POSITION_AI"] = TimeSpan.FromSeconds(30),
            ["BAG_AI"] = TimeSpan.FromSeconds(30),
            ["IOT_MASS"] = TimeSpan.FromSeconds(30),
            ["BARRIER"] = TimeSpan.FromSeconds(30),
        };

    private static readonly TimeSpan DefaultThreshold = TimeSpan.FromSeconds(30);

    public async Task<IReadOnlyList<DeviceStatus>> GetStatusAsync(
        Guid siteId, Guid? laneId, CancellationToken ct = default)
    {
        // 1. Lấy danh sách thiết bị đang hoạt động tại site (và lane nếu chỉ định).
        var devicesQuery = db.Devices
            .AsNoTracking()
            .Where(d => d.SiteId == siteId && d.IsEnabled && d.DeletedAt == null);

        if (laneId.HasValue)
            devicesQuery = devicesQuery.Where(d => d.LaneId == laneId.Value);

        var devices = await devicesQuery
            .Select(d => new { d.Id, d.Code, d.DeviceClass })
            .ToListAsync(ct);

        if (devices.Count == 0)
            return Array.Empty<DeviceStatus>();

        // 2. Lấy sự kiện gần nhất của mỗi thiết bị (một query group-by).
        var deviceIds = devices.Select(d => d.Id).ToList();

        var latestEvents = await db.DeviceEvents
            .AsNoTracking()
            .Where(e => deviceIds.Contains(e.DeviceId))
            .GroupBy(e => e.DeviceId)
            .Select(g => new
            {
                DeviceId = g.Key,
                OccurredAt = g.Max(e => e.OccurredAt),
            })
            .ToListAsync(ct);

        // Lấy payload của sự kiện gần nhất cho thiết bị loại SCALE để đọc ScaleDeviceStatus.
        // Dùng join lại để lấy đúng bản ghi có OccurredAt = max.
        var latestScaleDeviceIds = devices
            .Where(d => d.DeviceClass.Equals("SCALE", StringComparison.OrdinalIgnoreCase))
            .Select(d => d.Id)
            .ToHashSet();

        Dictionary<Guid, (string PayloadJson, string EventType)> scalePayloads = new();

        if (latestScaleDeviceIds.Count > 0)
        {
            // Lấy sự kiện SCALE_READING gần nhất cho từng đầu cân.
            var scaleEvents = await db.DeviceEvents
                .AsNoTracking()
                .Where(e => latestScaleDeviceIds.Contains(e.DeviceId)
                            && e.EventType == "SCALE_READING")
                .GroupBy(e => e.DeviceId)
                .Select(g => new
                {
                    DeviceId = g.Key,
                    LatestOccurredAt = g.Max(e => e.OccurredAt),
                })
                .ToListAsync(ct);

            foreach (var se in scaleEvents)
            {
                var payload = await db.DeviceEvents
                    .AsNoTracking()
                    .Where(e => e.DeviceId == se.DeviceId
                                && e.OccurredAt == se.LatestOccurredAt
                                && e.EventType == "SCALE_READING")
                    .Select(e => new { e.PayloadJson, e.EventType })
                    .FirstOrDefaultAsync(ct);

                if (payload is not null)
                    scalePayloads[se.DeviceId] = (payload.PayloadJson, payload.EventType);
            }
        }

        // 3. Map kết quả.
        var eventLookup = latestEvents.ToDictionary(e => e.DeviceId);
        var now = clock.UtcNow;

        var results = new List<DeviceStatus>(devices.Count);

        foreach (var device in devices)
        {
            DateTimeOffset? lastSignalAt = null;
            TimeSpan? signalAge = null;
            bool isOnline = false;
            ScaleDeviceStatus? scaleStatus = null;
            string? faultDescription = null;

            if (eventLookup.TryGetValue(device.Id, out var evt))
            {
                lastSignalAt = evt.OccurredAt;
                signalAge = now - evt.OccurredAt;

                var threshold = OnlineThresholds.TryGetValue(device.DeviceClass, out var t)
                    ? t
                    : DefaultThreshold;

                isOnline = signalAge.Value <= threshold;
            }

            // Đọc ScaleDeviceStatus từ payload nếu là thiết bị cân.
            if (scalePayloads.TryGetValue(device.Id, out var sp))
            {
                (scaleStatus, faultDescription) = ParseScaleStatus(sp.PayloadJson);
            }

            results.Add(new DeviceStatus(
                DeviceId: device.Id,
                DeviceCode: device.Code,
                DeviceClass: device.DeviceClass,
                IsOnline: isOnline,
                ScaleStatus: scaleStatus,
                LastSignalAt: lastSignalAt,
                SignalAge: signalAge,
                FaultDescription: faultDescription));
        }

        return results;
    }

    /// <summary>
    /// Parse trạng thái đầu cân từ payload JSON của SCALE_READING event.
    /// Payload theo hợp đồng chuẩn hóa: { "device_status": "Ok|Fault|Overload|Underload|Unknown", "fault_description": "..." }
    /// </summary>
    private static (ScaleDeviceStatus? Status, string? FaultDescription) ParseScaleStatus(
        string payloadJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;

            ScaleDeviceStatus? status = null;
            string? fault = null;

            if (root.TryGetProperty("device_status", out var statusProp)
                && statusProp.ValueKind == JsonValueKind.String)
            {
                var raw = statusProp.GetString();
                if (Enum.TryParse<ScaleDeviceStatus>(raw, ignoreCase: true, out var parsed))
                    status = parsed;
            }

            if (root.TryGetProperty("fault_description", out var faultProp)
                && faultProp.ValueKind == JsonValueKind.String)
            {
                fault = faultProp.GetString();
            }

            return (status, fault);
        }
        catch (JsonException)
        {
            return (ScaleDeviceStatus.Unknown, "Không đọc được payload thiết bị.");
        }
    }
}
