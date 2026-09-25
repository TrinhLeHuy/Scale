using Microsoft.EntityFrameworkCore;
using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.UseCases.Trips;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Infrastructure.Persistence;

namespace TTC.Weighing.Infrastructure.Repositories;

/// <summary>
/// Cấp số thứ tự hàng chờ dùng bảng queue_sequence_tracker với INSERT ON CONFLICT
/// để đảm bảo atomic increment không trùng khi nhiều trạm đồng thời (Req 2.2, 2.3).
/// </summary>
public sealed class SequentialQueueSequenceGenerator(
    WeighingDbContext db,
    IClock clock) : IQueueSequenceGenerator
{
    public async Task<int> NextAsync(
        Guid siteId, VehicleType vehicleType, CancellationToken ct = default)
    {
        var operatingDate = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var vehicleTypeStr = vehicleType.ToString();

        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO queue_sequence_tracker (site_id, vehicle_type, operating_date, last_value)
            VALUES (@siteId, @vt, @od, 1)
            ON CONFLICT (site_id, vehicle_type, operating_date)
            DO UPDATE SET last_value = queue_sequence_tracker.last_value + 1
            RETURNING last_value
            """;

        var pSiteId = cmd.CreateParameter();
        pSiteId.ParameterName = "@siteId";
        pSiteId.Value = siteId;
        cmd.Parameters.Add(pSiteId);

        var pVt = cmd.CreateParameter();
        pVt.ParameterName = "@vt";
        pVt.Value = vehicleTypeStr;
        cmd.Parameters.Add(pVt);

        var pOd = cmd.CreateParameter();
        pOd.ParameterName = "@od";
        pOd.Value = operatingDate;
        cmd.Parameters.Add(pOd);

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }
}
