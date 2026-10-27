namespace TTC.Weighing.WinForms.Presenters;

/// <summary>
/// Đọc lệnh đốn + hai lượt cân hiệu lực của chuyến từ DB (kết nối riêng, không dùng DbContext của tab).
/// Lần 1/lần 2 xác định theo thời điểm cân. Dùng chung cho màn Cân và màn CCS.
/// </summary>
public static class DigiScaleWeighContextReader
{
    public static async Task<DigiScaleWeighContext?> ReadAsync(string? connStr, Guid tripId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connStr)) return null;

        await using var conn = new Npgsql.NpgsqlConnection(connStr);
        await conn.OpenAsync(ct);

        string tripNo, source;
        string? ldId, maLd, bu;
        await using (var cmd = new Npgsql.NpgsqlCommand(
            "SELECT trip_no, source_system, frm_lenh_don_id, frm_ma_lenh_don, frm_lenh_don_bu " +
            "FROM trips WHERE id = $1 AND deleted_at IS NULL", conn))
        {
            cmd.Parameters.AddWithValue(tripId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;
            tripNo = reader.GetString(0);
            source = reader.IsDBNull(1) ? "" : reader.GetString(1);
            ldId = reader.IsDBNull(2) ? null : reader.GetString(2);
            maLd = reader.IsDBNull(3) ? null : reader.GetString(3);
            bu = reader.IsDBNull(4) ? null : reader.GetString(4);
        }

        var events = new List<(decimal Kg, DateTimeOffset At, string? BridgeCode)>();
        await using (var cmd = new Npgsql.NpgsqlCommand(
            "SELECT we.normalized_kg, we.measured_at, wb.code " +
            "FROM weigh_events we " +
            "LEFT JOIN weigh_sessions ws ON ws.id = we.weigh_session_id " +
            "LEFT JOIN weighbridges wb ON wb.id = ws.weighbridge_id " +
            "WHERE we.trip_id = $1 AND we.is_effective AND we.deleted_at IS NULL " +
            "ORDER BY we.measured_at", conn))
        {
            cmd.Parameters.AddWithValue(tripId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                events.Add((reader.GetDecimal(0), reader.GetFieldValue<DateTimeOffset>(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        var first = events.Count > 0 ? events[0] : ((decimal, DateTimeOffset, string?)?)null;
        var second = events.Count > 1 ? events[1] : ((decimal, DateTimeOffset, string?)?)null;

        return new DigiScaleWeighContext(
            tripId, tripNo, source, ldId, maLd, bu,
            first?.Item1, first?.Item2, second?.Item1, second?.Item2)
        {
            SecondBridgeCode = second?.Item3
        };
    }
}
