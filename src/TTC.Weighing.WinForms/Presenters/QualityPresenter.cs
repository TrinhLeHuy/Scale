using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.UseCases.Quality;

namespace TTC.Weighing.WinForms.Presenters;

/// <summary>Dữ liệu hiển thị tín hiệu cân mẫu, đã tính độ mới tại tầng presenter.</summary>
public sealed record SampleScaleView(
    decimal? WeightKg,
    bool IsStable,
    TimeSpan StableDuration,
    bool SignalFresh);

public enum PaymentFiguresFailure { None, NoLenhDon, NotWeighedOut, NoPolicy }

/// <summary>Kết quả lấy CCS/tạp chất thanh toán từ chính sách FRM.</summary>
public sealed record PaymentFiguresResult(
    bool HasPolicy,
    decimal? TapChatThanhToan,
    decimal? CcsThanhToan,
    string? ChinhSach,
    bool PaymentPushed,
    PaymentFiguresFailure? Failure,
    string? Error)
{
    public static PaymentFiguresResult Fail(PaymentFiguresFailure failure, string? error)
        => new(false, null, null, null, false, failure, error);
}

/// <summary>
/// Presenter cho UI-05 (QualityForm). Form không gọi thẳng handler/gateway.
/// Mọi quyết định nghiệp vụ do handler ở Application quyết định.
/// </summary>
public sealed class QualityPresenter(
    IQrScanHandler qrScanHandler,
    ISampleScaleGateway sampleScaleGateway,
    RecordImpuritySampleHandler recordImpurity,
    ReceiveCcsResultHandler receiveCcs,
    IClock clock,
    TTC.Weighing.Infrastructure.Adapters.FrmMasterDataCache frmCache,
    TTC.Weighing.Infrastructure.Adapters.DigiScaleTicketAdapter digiScaleTickets,
    ICcsInstrumentReader ccsInstruments)
{
    /// <summary>Đọc một lần số Brix hoặc Pol từ máy đo phòng CCS.</summary>
    public Task<CcsInstrumentReading> ReadCcsInstrumentAsync(CcsInstrument instrument, CancellationToken ct = default)
        => ccsInstruments.ReadAsync(instrument, ct);

    /// <summary>Độ mới tối đa dùng riêng cho hiển thị cân mẫu. Không dùng làm điều kiện ghi kết quả.</summary>
    private static readonly TimeSpan DisplayStaleAfter = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Quét QR tại trạm lấy mẫu, tra cứu Trip và hiển thị thông tin chuyến mía.
    /// </summary>
    public Task<QrScanResult> ScanQrAsync(
        string qrContent, Guid siteId, CancellationToken ct = default)
        => qrScanHandler.DecodeAndLookupAsync(qrContent, siteId, ct);

    /// <summary>
    /// Đọc tín hiệu hiện tại từ đầu cân mẫu tạp chất.
    /// Trả view model đã tính độ mới cho hiển thị.
    /// </summary>
    public async Task<SampleScaleView> ReadSampleScaleAsync(
        Guid sampleScaleId, CancellationToken ct = default)
    {
        var signal = await sampleScaleGateway.GetCurrentAsync(sampleScaleId, ct);

        if (signal is null)
            return new SampleScaleView(null, false, TimeSpan.Zero, false);

        var fresh = clock.UtcNow - signal.OccurredAt <= DisplayStaleAfter;

        return new SampleScaleView(
            WeightKg: signal.WeightKg,
            IsStable: signal.IsStable,
            StableDuration: signal.StableDuration,
            SignalFresh: fresh);
    }

    /// <summary>
    /// Ghi kết quả cân tạp chất (mẫu trước + mẫu sau).
    /// Handler sẽ validate, tính tỷ lệ, lưu QualitySample.
    /// </summary>
    public Task<RecordImpuritySampleResult> RecordImpurityAsync(
        RecordImpuritySampleCommand cmd, CancellationToken ct = default)
        => recordImpurity.HandleAsync(cmd, ct);

    /// <summary>
    /// Tiếp nhận kết quả chữ đường (CCS). Có thể đến sau khi xe đã ra cổng.
    /// 
    /// Handler sẽ validate, xác định status, lưu QualitySample.
    /// </summary>
    public Task<ReceiveCcsResultResult> RecordCcsAsync(
        ReceiveCcsResultCommand cmd, CancellationToken ct = default)
        => receiveCcs.HandleAsync(cmd, ct);

    /// <summary>Push phiếu tạp chất lên DigiScale (bước 8).</summary>
    public Task<TTC.Weighing.Infrastructure.Adapters.TicketResult> PushTapChatAsync(
        string lenhDonId, string maLenhDon, string sttCan, string lanDo, DateTime ngay,
        string klBefore, string klAfter, string tapChatThucTe, string? lenhDonBu = null, CancellationToken ct = default)
        => digiScaleTickets.PushTapChatAsync(lenhDonId, maLenhDon, sttCan, lanDo, ngay, klBefore, klAfter, tapChatThucTe, lenhDonBu, ct);

    /// <summary>
    /// Lệnh đốn của CHUYẾN đang đo: theo ID lệnh đốn lưu lúc đăng tài; chỉ khi chuyến không có ID mới tra theo biển số.
    /// </summary>
    public TTC.Weighing.Infrastructure.Adapters.FrmLenhDonInfo? FindLenhDonForTrip(string? lenhDonId, string? plate)
        => frmCache.FindLenhDonBySourceKey(lenhDonId)
           ?? (string.IsNullOrWhiteSpace(lenhDonId) && !string.IsNullOrWhiteSpace(plate) ? FindLenhDonByPlate(plate) : null);

    /// <summary>Hàng hóa mía theo lệnh đốn: Mía cháy / Mía khúc / Mía cây; null nếu không xác định.</summary>
    public string? CommodityForTrip(string? lenhDonId, string? maLenhDon)
        => Services.SugarcaneCommodity.ForTrip(frmCache, lenhDonId, maLenhDon);

    /// <summary>Tìm LenhDon theo biển số xe.</summary>
    public TTC.Weighing.Infrastructure.Adapters.FrmLenhDonInfo? FindLenhDonByPlate(string plate)
    {
        var normalized = plate.Replace(" ", "").Replace("-", "").Replace(".", "").ToUpperInvariant();
        var activeBu = frmCache.ActiveBusinessUnit;

        return frmCache.AllLenhDon
            .Where(ld => ld.StatusCodeName == "Đã duyệt")
            .Where(ld => activeBu is null || string.Equals(ld.BusinessUnit, activeBu, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(ld => ld.NgayCap)
            .FirstOrDefault(ld =>
            {
                if (ld.VehiclePlate is null) return false;
                return ld.VehiclePlate.Replace(" ", "").Replace("-", "").Replace(".", "").ToUpperInvariant() == normalized;
            });
    }

    /// <summary>Lấy thông tin FRM lệnh đốn từ trip DB — dùng raw SQL riêng tránh EF concurrent access.</summary>
    public async Task<(string? LenhDonId, string? MaLenhDon, string? Bu)> GetTripFrmDataAsync(Guid tripId, CancellationToken ct = default)
    {
        try
        {
            var connStr = frmCache.ConnectionString;
            if (string.IsNullOrWhiteSpace(connStr)) return (null, null, null);

            await using var conn = new Npgsql.NpgsqlConnection(connStr);
            await conn.OpenAsync(ct);
            await using var cmd = new Npgsql.NpgsqlCommand(
                "SELECT frm_lenh_don_id, frm_ma_lenh_don, frm_lenh_don_bu FROM trips WHERE id = $1", conn);
            cmd.Parameters.AddWithValue(tripId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
                return (reader[0] as string, reader[1] as string, reader[2] as string);
        }
        catch { }
        return (null, null, null);
    }

    /// <summary>Tạp chất thực tế (%) — mẫu tạp chất hiệu lực của chuyến; null nếu chưa đo.</summary>
    public async Task<decimal?> GetActualImpurityAsync(Guid tripId, CancellationToken ct = default)
    {
        var connStr = frmCache.ConnectionString;
        if (string.IsNullOrWhiteSpace(connStr)) return null;

        await using var conn = new Npgsql.NpgsqlConnection(connStr);
        await conn.OpenAsync(ct);
        await using var cmd = new Npgsql.NpgsqlCommand(
            "SELECT impurity_percent FROM quality_samples " +
            "WHERE trip_id = $1 AND type = 'Impurity' AND is_effective AND deleted_at IS NULL " +
            "ORDER BY revision DESC LIMIT 1", conn);
        cmd.Parameters.AddWithValue(tripId);
        return await cmd.ExecuteScalarAsync(ct) is decimal pct ? pct : null;
    }

    /// <summary>
    /// CCS + tạp chất THANH TOÁN theo chính sách FRM (Action BatChinhSachCanDo, gọi qua DigiScale):
    /// gửi phiếu cân lần 2 với IsQuetCS = Yes để lấy chính sách, rồi gửi lại lần 2 kèm đủ số thanh toán
    /// (cùng cách màn Cân làm sau khi cân ra). Chỉ chạy khi chuyến đã cân lần 2.
    /// TL thanh toán = (lần 1 − lần 2) × (1 − tạp chất chính sách %); TL hàng ≤ 0 thì không gửi số thanh toán.
    /// </summary>
    public async Task<PaymentFiguresResult> FetchPaymentFiguresAsync(Guid tripId, CancellationToken ct = default)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var ctx = await DigiScaleWeighContextReader.ReadAsync(frmCache.ConnectionString, tripId, ct);
        if (ctx is null || !ctx.HasLenhDon)
            return PaymentFiguresResult.Fail(PaymentFiguresFailure.NoLenhDon, null);
        if (ctx is not { FirstKg: { } firstKg, SecondKg: { } secondKg, SecondAt: { } secondAt })
            return PaymentFiguresResult.Fail(PaymentFiguresFailure.NotWeighedOut, null);

        var weightTon = (secondKg / 1000m).ToString("F2", inv);
        var policy = await digiScaleTickets.PushPhieuCanAsync(
            "100000001", "2", Program.LoggedInUserName, Program.LoggedInUserAccount,
            ctx.LenhDonId!, ctx.MaLenhDon!, weightTon, secondAt.ToLocalTime().DateTime,
            ctx.TripNo, ctx.SecondBridgeCode, "0", "0", "0", ctx.LenhDonBu, ct, isQuetCS: true);

        if (!policy.HasPolicy)
            return PaymentFiguresResult.Fail(PaymentFiguresFailure.NoPolicy, policy.ErrorMessage);

        var tcPolicy = policy.TapChatCS ?? 0m;
        var ccsText = policy.CcsChinhSach?.ToString(inv) ?? "0";
        var netKg = firstKg - secondKg;
        var pushed = false;
        string? pushError = null;
        if (netKg > 0)
        {
            var tlThanhToanTon = netKg / 1000m * (1m - tcPolicy / 100m);
            var sent = await digiScaleTickets.PushPhieuCanAsync(
                "100000001", "2", Program.LoggedInUserName, Program.LoggedInUserAccount,
                ctx.LenhDonId!, ctx.MaLenhDon!, weightTon, secondAt.ToLocalTime().DateTime,
                ctx.TripNo, ctx.SecondBridgeCode,
                tlThanhToanTon.ToString("F3", inv), tcPolicy.ToString(inv), ccsText,
                ctx.LenhDonBu, ct, isQuetCS: false,
                tapChatCs: tcPolicy.ToString(inv), ccsChinhSach: ccsText);
            pushed = sent.IsSuccess;
            pushError = sent.IsSuccess ? null : sent.ErrorMessage;
        }

        return new PaymentFiguresResult(true, policy.TapChatCS, policy.CcsChinhSach, policy.ChinhSachThuMia,
            pushed, null, pushError);
    }

    /// <summary>Push phiếu CCS lên DigiScale (bước 8).</summary>
    public Task<TTC.Weighing.Infrastructure.Adapters.TicketResult> PushCcsAsync(
        string lenhDon, string maLenhDon, string sttCan, string lanDo, DateTime ngay,
        string brix, string ccsDo, string pol, string polDoc, string caLamViec, string? nhanVienDoCcs = null, string? lenhDonBu = null, CancellationToken ct = default)
        => digiScaleTickets.PushCcsAsync(lenhDon, maLenhDon, sttCan, lanDo, ngay, brix, ccsDo, pol, polDoc, caLamViec, nhanVienDoCcs, lenhDonBu, ct);
}
