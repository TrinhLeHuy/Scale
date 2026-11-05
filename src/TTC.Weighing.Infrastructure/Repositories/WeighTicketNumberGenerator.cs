using System.Text.RegularExpressions;
using TTC.Weighing.Domain.Common;

namespace TTC.Weighing.Infrastructure.Repositories;

/// <summary>
/// Mã phiếu cân tự sinh (mía + vật tư): <c>PC</c> + niên vụ đầu tư 2 năm (vd 2526) + số tăng dần 7 chữ số.
/// Ví dụ: <c>PC25260000001</c>. Cân vào và cân ra dùng CHUNG một mã (sinh một lần cho chuyến, lưu
/// <c>trips.ticket_number</c>); mã này gửi DigiScale làm số phiếu cân (SoPhieuCan / WeightNumber).
/// Duy nhất: sinh trong transaction có khoá advisory + unique index trên ticket_number.
/// Số tăng dần đếm lại từ 1 cho mỗi niên vụ.
/// </summary>
public static partial class WeighTicketNumberGenerator
{
    public const string PREFIX = "PC";
    private const int SEQ_DIGITS = 7;
    private const string LOCK_KEY = "trips.ticket_number";

    /// <summary>Đã đúng mẫu PC + 4 số niên vụ + 7 số thứ tự.</summary>
    public static bool IsValid(string? ticketNo) => ticketNo is not null && TicketRegex().IsMatch(ticketNo);

    /// <summary>
    /// Trả mã phiếu cân của chuyến; chưa có thì sinh mới và lưu. Null nếu không có chuyến / không có DB.
    /// Gọi nhiều lần (cân vào, cân ra, đẩy lại) luôn ra CÙNG một mã.
    /// </summary>
    public static async Task<string?> EnsureAsync(string? connStr, Guid tripId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connStr)) return null;

        await using var conn = new Npgsql.NpgsqlConnection(connStr);
        await conn.OpenAsync(ct);

        // Index duy nhất (idempotent). Quay lui: DROP INDEX uq_trips_ticket_number.
        await using (var idx = new Npgsql.NpgsqlCommand(
            "CREATE UNIQUE INDEX IF NOT EXISTS uq_trips_ticket_number ON trips (ticket_number) WHERE ticket_number IS NOT NULL", conn))
            await idx.ExecuteNonQueryAsync(ct);

        await using var tx = await conn.BeginTransactionAsync(ct);

        await using (var lk = new Npgsql.NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext($1))", conn, tx))
        {
            lk.Parameters.AddWithValue(LOCK_KEY);
            await lk.ExecuteNonQueryAsync(ct);
        }

        string? existing, cropSeason;
        DateTimeOffset createdAt;
        await using (var cmd = new Npgsql.NpgsqlCommand(
            "SELECT ticket_number, crop_season, created_at FROM trips WHERE id = $1 AND deleted_at IS NULL FOR UPDATE", conn, tx))
        {
            cmd.Parameters.AddWithValue(tripId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return null;
            existing = r.IsDBNull(0) ? null : r.GetString(0);
            cropSeason = r.IsDBNull(1) ? null : r.GetString(1);
            createdAt = r.GetFieldValue<DateTimeOffset>(2);
        }

        if (IsValid(existing))
        {
            await tx.CommitAsync(ct);
            return existing;
        }

        // Niên vụ: của chuyến (lệnh đốn FRM lưu lúc đăng tài) → niên vụ gần nhất trong hệ thống (vật tư
        // không có lệnh đốn) → theo năm của ngày đăng tài.
        var season = SeasonCode(cropSeason);
        if (season is null)
        {
            await using var cmd = new Npgsql.NpgsqlCommand(
                "SELECT crop_season FROM trips WHERE crop_season IS NOT NULL AND crop_season <> '' " +
                "ORDER BY created_at DESC LIMIT 1", conn, tx);
            season = SeasonCode(await cmd.ExecuteScalarAsync(ct) as string);
        }
        // TODO (điểm D chưa chốt): tháng bắt đầu niên vụ. Tạm lấy năm đăng tài → năm sau (vd 2026 → 2627).
        season ??= SeasonFromYear(createdAt.ToVnTime().Year);

        var prefix = PREFIX + season;
        long max;
        await using (var cmd = new Npgsql.NpgsqlCommand(
            "SELECT COALESCE(MAX(CAST(substring(ticket_number from $2) AS bigint)), 0) FROM trips " +
            "WHERE ticket_number ~ $1", conn, tx))
        {
            cmd.Parameters.AddWithValue("^" + prefix + "[0-9]{" + SEQ_DIGITS + "}$");
            cmd.Parameters.AddWithValue(prefix.Length + 1);
            max = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
        }

        var ticketNo = prefix + (max + 1).ToString("D" + SEQ_DIGITS, System.Globalization.CultureInfo.InvariantCulture);

        // Không tăng version: chỉ thêm mã phiếu, tránh làm lỗi concurrency ở màn đang mở chuyến.
        await using (var upd = new Npgsql.NpgsqlCommand(
            "UPDATE trips SET ticket_number = $2, updated_at = now() WHERE id = $1", conn, tx))
        {
            upd.Parameters.AddWithValue(tripId);
            upd.Parameters.AddWithValue(ticketNo);
            await upd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return ticketNo;
    }

    /// <summary>"Vụ ĐT 2025-2026" / "2025-26" / "2526" → "2526". Không đọc được → null.</summary>
    public static string? SeasonCode(string? cropSeason)
    {
        if (string.IsNullOrWhiteSpace(cropSeason)) return null;
        var m = SeasonRegex().Match(cropSeason);
        if (m.Success)
        {
            var y1 = m.Groups[1].Value[2..];
            var y2 = m.Groups[2].Value.Length == 4 ? m.Groups[2].Value[2..] : m.Groups[2].Value;
            return y1 + y2;
        }
        var compact = CompactSeasonRegex().Match(cropSeason);
        return compact.Success ? compact.Groups[1].Value : null;
    }

    private static string SeasonFromYear(int year) => $"{year % 100:D2}{(year + 1) % 100:D2}";

    [GeneratedRegex(@"^PC\d{4}\d{7}$")]
    private static partial Regex TicketRegex();

    [GeneratedRegex(@"(20\d{2})\s*[-/–]\s*(20\d{2}|\d{2})")]
    private static partial Regex SeasonRegex();

    [GeneratedRegex(@"\b(\d{4})\b")]
    private static partial Regex CompactSeasonRegex();
}
