using Microsoft.EntityFrameworkCore;
using TTC.Weighing.Application.UseCases.Trips;
using TTC.Weighing.Infrastructure.Persistence;

namespace TTC.Weighing.Infrastructure.Repositories;

/// <summary>
/// R-005 — Số đăng tài tự sinh theo ngày: YYYYMMDD-NNN (NNN tăng dần trong ngày).
/// </summary>
public sealed class SequentialTripNumberGenerator(WeighingDbContext db) : ITripNumberGenerator
{
    public const string SequenceName = "trip_no_seq";

    public async Task<string> NextAsync(
        Guid siteId, string profileCode, CancellationToken ct = default)
    {
        // Tăng sequence toàn cục qua raw ADO.NET — không dùng SqlQueryRaw (non-composable)
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT nextval('{SequenceName}')";
        var seqVal = await cmd.ExecuteScalarAsync(ct);
        _ = Convert.ToInt64(seqVal);

        // Đếm chuyến trong ngày → NNN
        var todayUtc = DateTime.UtcNow.Date;
        var tomorrowUtc = todayUtc.AddDays(1);

        var countToday = await db.Trips
            .Where(t => t.SiteId == siteId
                && t.CreatedAt >= todayUtc
                && t.CreatedAt < tomorrowUtc)
            .CountAsync(ct);

        return $"{DateTime.UtcNow:yyyyMMdd}-{countToday + 1:D3}";
    }
}
