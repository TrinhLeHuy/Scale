using System.Globalization;
using System.Text.Json;
using TTC.Weighing.Application.UseCases.Measurement;
using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Domain.Rules;

namespace TTC.Weighing.Infrastructure.Devices;

/// <summary>
/// Đọc payload theo hợp đồng sự kiện chuẩn hóa trong contracts/device-events.schema.json.
/// Đây là giao thức nội bộ đề xuất; adapter thiết bị chịu trách nhiệm chuyển định dạng thật
/// sang cấu trúc này sau khi test mapping (03_Du_lieu mục 4).
/// </summary>
public sealed class NormalizedDeviceEventPayloadReader : IDeviceEventPayloadReader
{
    public IReadOnlyList<IotReading> ReadIotReadings(IEnumerable<DeviceEvent> events)
    {
        var readings = new List<IotReading>();

        foreach (var deviceEvent in events.Where(e => e.EventType == "IOT_MASS_READING"))
        {
            using var document = JsonDocument.Parse(deviceEvent.PayloadJson);
            var root = document.RootElement;

            // Khối lượng truyền dạng chuỗi thập phân để không mất độ chính xác.
            if (!TryReadDecimal(root, "value_kg", out var valueKg))
                continue;

            readings.Add(new IotReading(
                EventId: deviceEvent.EventId,
                Sequence: deviceEvent.Sequence,
                ValueKg: valueKg,
                CounterEpoch: ReadString(root, "counter_epoch"),
                OccurredAt: deviceEvent.OccurredAt));
        }

        return readings;
    }

    public IReadOnlyList<(string PassageId, bool IsForward)> ReadBagPassages(
        IEnumerable<DeviceEvent> events)
    {
        var passages = new List<(string, bool)>();

        foreach (var deviceEvent in events.Where(e => e.EventType == "BAG_PASSAGE"))
        {
            using var document = JsonDocument.Parse(deviceEvent.PayloadJson);
            var root = document.RootElement;

            var passageId = ReadString(root, "passage_id") ?? deviceEvent.EventId;
            var direction = ReadString(root, "direction");

            // Chiều không xác định thì không tính là lượt bao đi, tránh đếm sai.
            if (direction is not ("FORWARD" or "REVERSE"))
                continue;

            passages.Add((passageId, direction == "FORWARD"));
        }

        return passages;
    }

    private static bool TryReadDecimal(JsonElement root, string property, out decimal value)
    {
        value = 0m;

        if (!root.TryGetProperty(property, out var element))
            return false;

        return element.ValueKind switch
        {
            JsonValueKind.String => decimal.TryParse(
                element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value),
            JsonValueKind.Number => element.TryGetDecimal(out value),
            _ => false
        };
    }

    private static string? ReadString(JsonElement root, string property)
        => root.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
}
