using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TTC.Weighing.Infrastructure.Adapters;

namespace TTC.Weighing.Infrastructure.Integration;

/// <summary>Chính sách thu mua đã bắt được cho một chuyến (theo số phiếu cân = số đăng tài).</summary>
public sealed record TripPolicy(
    string SoPhieuCan, string? MaLenhDon, string? ChinhSachThuMua,
    decimal? TapChatCS, decimal? CcsChinhSach, decimal? WeightTon, DateTimeOffset FetchedAt, bool IsPreview);

/// <summary>
/// Lưu chính sách FRM của từng chuyến (bảng trip_policies) để mọi màn hình hiện lại được sau khi cân xong:
/// màn Cân (chuyến đã cân đủ), Xem thêm, phiếu in. Chưa có dòng → đọc lượt bắt chính sách thành công gần nhất
/// trong nhật ký API (chuyến cân trước khi có bảng này).
/// </summary>
public sealed class TripPolicyStore(IConfiguration configuration, ILogger<TripPolicyStore> logger)
{
    private readonly string? _connStr = configuration.GetConnectionString("WeighingDb");
    private bool _schemaReady;

    private async Task<Npgsql.NpgsqlConnection?> OpenAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_connStr)) return null;
        var conn = new Npgsql.NpgsqlConnection(_connStr);
        await conn.OpenAsync(ct);
        if (!_schemaReady)
        {
            await using var cmd = new Npgsql.NpgsqlCommand(@"
CREATE TABLE IF NOT EXISTS trip_policies (
  so_phieu_can varchar(60) PRIMARY KEY,
  ma_lenh_don varchar(60),
  chinh_sach_thu_mua text,
  tap_chat_cs numeric(9,4),
  ccs_chinh_sach numeric(9,4),
  weight_ton numeric(15,3),
  is_preview boolean NOT NULL DEFAULT false,
  fetched_at timestamptz NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz
);", conn);
            await cmd.ExecuteNonQueryAsync(ct);
            _schemaReady = true;
        }
        return conn;
    }

    /// <summary>Ghi / cập nhật chính sách của chuyến. Lỗi ghi không làm hỏng lượt cân.</summary>
    public async Task SaveAsync(string soPhieuCan, string? maLenhDon, FrmPolicyResult policy, decimal? weightTon,
        bool isPreview, CancellationToken ct = default)
    {
        if (!policy.HasPolicy || string.IsNullOrWhiteSpace(soPhieuCan)) return;
        try
        {
            await using var conn = await OpenAsync(ct);
            if (conn is null) return;
            // Bản xem trước (trước cân lần 2) không ghi đè bản chính thức đã có.
            await using var cmd = new Npgsql.NpgsqlCommand(@"
INSERT INTO trip_policies (so_phieu_can, ma_lenh_don, chinh_sach_thu_mua, tap_chat_cs, ccs_chinh_sach, weight_ton, is_preview, fetched_at)
VALUES ($1,$2,$3,$4,$5,$6,$7,now())
ON CONFLICT (so_phieu_can) DO UPDATE SET
  ma_lenh_don = EXCLUDED.ma_lenh_don, chinh_sach_thu_mua = EXCLUDED.chinh_sach_thu_mua,
  tap_chat_cs = EXCLUDED.tap_chat_cs, ccs_chinh_sach = EXCLUDED.ccs_chinh_sach,
  weight_ton = EXCLUDED.weight_ton, is_preview = EXCLUDED.is_preview, fetched_at = now(), updated_at = now()
WHERE NOT (EXCLUDED.is_preview AND NOT trip_policies.is_preview)", conn);
            cmd.Parameters.AddWithValue(soPhieuCan);
            cmd.Parameters.AddWithValue((object?)maLenhDon ?? DBNull.Value);
            cmd.Parameters.AddWithValue((object?)policy.ChinhSachThuMua ?? DBNull.Value);
            cmd.Parameters.AddWithValue((object?)policy.TapChatCS ?? DBNull.Value);
            cmd.Parameters.AddWithValue((object?)policy.CcsChinhSach ?? DBNull.Value);
            cmd.Parameters.AddWithValue((object?)weightTon ?? DBNull.Value);
            cmd.Parameters.AddWithValue(isPreview);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "TripPolicy: lưu chính sách lỗi phieu={So}", soPhieuCan);
        }
    }

    public async Task<TripPolicy?> GetAsync(string soPhieuCan, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(soPhieuCan)) return null;
        await using var conn = await OpenAsync(ct);
        if (conn is null) return null;

        await using (var cmd = new Npgsql.NpgsqlCommand(
            "SELECT ma_lenh_don, chinh_sach_thu_mua, tap_chat_cs, ccs_chinh_sach, weight_ton, fetched_at, is_preview " +
            "FROM trip_policies WHERE so_phieu_can = $1", conn))
        {
            cmd.Parameters.AddWithValue(soPhieuCan);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
                return new TripPolicy(soPhieuCan,
                    r.IsDBNull(0) ? null : r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1),
                    r.IsDBNull(2) ? null : r.GetDecimal(2), r.IsDBNull(3) ? null : r.GetDecimal(3),
                    r.IsDBNull(4) ? null : r.GetDecimal(4), r.GetFieldValue<DateTimeOffset>(5), r.GetBoolean(6));
        }

        // Chuyến cân trước khi có bảng: lấy lượt bắt chính sách FRM thành công gần nhất trong nhật ký API.
        try
        {
            await using var log = new Npgsql.NpgsqlCommand(
                "SELECT response_body, occurred_at FROM api_call_logs " +
                "WHERE is_policy AND system = 'FRM' AND success AND business_key = $1 " +
                "ORDER BY occurred_at DESC LIMIT 1", conn);
            log.Parameters.AddWithValue(soPhieuCan);
            await using var r = await log.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct) && !r.IsDBNull(0))
            {
                var parsed = FrmPolicyAdapter.Parse(r.GetString(0));
                if (parsed.HasPolicy)
                    return new TripPolicy(soPhieuCan, null, parsed.ChinhSachThuMua, parsed.TapChatCS,
                        parsed.CcsChinhSach, null, r.GetFieldValue<DateTimeOffset>(1), false);
            }
        }
        catch (Npgsql.PostgresException) { /* chưa có bảng nhật ký */ }

        return null;
    }
}
