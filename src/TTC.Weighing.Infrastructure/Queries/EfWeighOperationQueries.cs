using Microsoft.EntityFrameworkCore;
using TTC.Weighing.Application.Queries;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Infrastructure.Persistence;

namespace TTC.Weighing.Infrastructure.Queries;

/// <summary>
/// EF implementation cho IWeighOperationQueries — lưới xe đã cân trên WeighOperationForm (UI-03).
/// Join trips → weigh_sessions → weigh_events (Gross/Tare) → integration_messages (SyncStatus).
/// Req 1.2, 4.6, 8.6.
/// </summary>
public sealed class EfWeighOperationQueries(WeighingDbContext db, Adapters.FrmMasterDataCache frmCache) : IWeighOperationQueries
{
    public async Task<IReadOnlyList<WeighedTripRow>> ListAsync(
        Guid siteId, Guid weighbridgeId, WeighedTripFilter filter,
        CancellationToken ct = default)
    {
        // Lấy tripId có phiên cân tại bàn cân này
        var tripIdsAtBridge = db.WeighSessions
            .Where(ws => ws.WeighbridgeId == weighbridgeId)
            .Select(ws => ws.TripId)
            .Distinct();

        var query = db.Trips
            .AsNoTracking()
            .Where(t => t.SiteId == siteId && t.DeletedAt == null)
            .FilterByOrgUnit()
            .Where(t => tripIdsAtBridge.Contains(t.Id));

        // Lọc theo khoảng ngày [from, to) trên CreatedAt
        if (filter.FromUtc.HasValue)
            query = query.Where(t => t.CreatedAt >= filter.FromUtc.Value);

        if (filter.ToUtc.HasValue)
            query = query.Where(t => t.CreatedAt < filter.ToUtc.Value);

        // Tách Mía (FRM) / Vật tư (ERP)
        if (filter.Category.ToSourceSystem() is { } sourceSystem)
            query = query.Where(t => t.SourceSystem == sourceSystem);

        // Chỉ lấy chuyến đã có ít nhất một lượt cân hiệu lực
        query = query.Where(t =>
            db.WeighEvents.Any(e => e.TripId == t.Id && e.IsEffective));

        var rows = await query
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new
            {
                TripId = t.Id,
                t.TripNo,
                t.TicketNumber,
                t.ReferenceTripNo,
                Plate = t.TractorPlate,
                Trailer = t.TrailerPlate,
                Gross = db.WeighEvents
                    .Where(e => e.TripId == t.Id
                        && e.Role == WeightRole.Gross
                        && e.IsEffective)
                    .Select(e => new { e.NormalizedKg, e.MeasuredAt })
                    .FirstOrDefault(),
                Tare = db.WeighEvents
                    .Where(e => e.TripId == t.Id
                        && e.Role == WeightRole.Tare
                        && e.IsEffective)
                    .Select(e => new { e.NormalizedKg, e.MeasuredAt })
                    .FirstOrDefault(),
                // SyncStatus: lấy trạng thái mới nhất từ integration_messages
                SyncStatus = db.IntegrationMessages
                    .Where(m => m.TripId == t.Id)
                    .OrderByDescending(m => m.CreatedAt)
                    .Select(m => (IntegrationMessageStatus?)m.Status)
                    .FirstOrDefault(),
                t.FrmLenhDonId,
                // Tạp chất / CCS thực tế: mẫu hiệu lực mới nhất của chuyến (mía).
                ImpurityActual = db.QualitySamples
                    .Where(q => q.TripId == t.Id && q.Type == QualitySampleType.Impurity && q.IsEffective
                        && q.ImpurityPercent != null)
                    .OrderByDescending(q => q.ReceivedAt)
                    .Select(q => q.ImpurityPercent)
                    .FirstOrDefault(),
                CcsActual = db.QualitySamples
                    .Where(q => q.TripId == t.Id && q.Type == QualitySampleType.Ccs && q.IsEffective
                        && q.CcsValue != null)
                    .OrderByDescending(q => q.ReceivedAt)
                    .Select(q => q.CcsValue)
                    .FirstOrDefault(),
                // Customer: chủ mía hoặc tổ chức từ chứng từ nguồn
                Customer = t.SugarcaneOwner ?? db.TripDocumentLines
                    .Where(dl => dl.TripId == t.Id)
                    .Join(db.Set<Domain.Entities.ExternalDocument>(),
                        dl => dl.DocumentId, doc => doc.Id,
                        (dl, doc) => doc.OrganizationCode)
                    .FirstOrDefault(),
                // Commodity: loại mía hoặc mã profile
                Commodity = t.SugarcaneType ?? db.WorkflowProfiles
                    .Where(p => p.Id == t.WorkflowProfileId)
                    .Select(p => p.SourceSystem == "FRM" ? "Mía nhập" : "Vật tư")
                    .FirstOrDefault(),
                // ShipmentNo từ reference hoặc chứng từ
                ShipmentNo = t.ReferenceTripNo ?? db.TripDocumentLines
                    .Where(dl => dl.TripId == t.Id)
                    .Join(db.Set<Domain.Entities.ExternalDocument>(),
                        dl => dl.DocumentId, doc => doc.Id,
                        (dl, doc) => doc.ShipmentNo)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var paidByTicket = await LoadPaidFiguresAsync(
            rows.Select(r => r.TicketNumber).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!).Distinct().ToArray(), ct);

        var result = new List<WeighedTripRow>(rows.Count);

        foreach (var r in rows)
        {
            var paid = r.TicketNumber is { } tk && paidByTicket.TryGetValue(tk, out var p) ? p : default;
            var hasGross = r.Gross is not null;
            var hasTare = r.Tare is not null;

            // FirstWeightOnly: mới có đúng 1 lượt cân (Gross HOẶC Tare).
            // Vật tư xuất (04/06) cân lần 1 là Tare (xe rỗng vào) nên không được bắt buộc Gross.
            if (filter.FirstWeightOnly && hasGross == hasTare)
                continue;

            // CompletedOnly: phải có cả Gross và Tare
            if (filter.CompletedOnly && (!hasGross || !hasTare))
                continue;

            // Xác định cân vào/ra dựa theo thời gian, không suy từ vai trò
            var first = r.Gross is null ? r.Tare
                : r.Tare is null ? r.Gross
                : r.Gross.MeasuredAt <= r.Tare.MeasuredAt ? r.Gross : r.Tare;

            var second = r.Gross is null || r.Tare is null ? null
                : ReferenceEquals(first, r.Gross) ? r.Tare : r.Gross;

            decimal? net = null;
            if (hasGross && hasTare)
                net = Math.Abs(r.Gross!.NormalizedKg - r.Tare!.NormalizedKg);

            result.Add(new WeighedTripRow(
                TripId: r.TripId,
                RegNo: r.TripNo,
                TicketNo: r.TicketNumber,
                TripNumber: r.ReferenceTripNo,
                ShipmentNo: r.ShipmentNo,
                Plate: r.Plate,
                Trailer: r.Trailer,
                TimeIn: first?.MeasuredAt,
                WeightIn: first?.NormalizedKg / 1000m,
                TimeOut: second?.MeasuredAt,
                WeightOut: second?.NormalizedKg / 1000m,
                NetWeight: net / 1000m,
                // Mía: chủ mía trống → khách hàng doanh nghiệp của lệnh đốn (dữ liệu FRM đã kéo về).
                Customer: !string.IsNullOrWhiteSpace(r.Customer) ? r.Customer
                    : (frmCache.FindLenhDonBySourceKey(r.FrmLenhDonId)
                       ?? frmCache.FindLenhDonByCode(r.ShipmentNo))?.OwnerName ?? r.Customer,
                Commodity: r.Commodity,
                SyncStatus: r.SyncStatus,
                ImpurityActual: r.ImpurityActual,
                ImpurityPaid: paid.Impurity,
                CcsActual: r.CcsActual,
                CcsPaid: paid.Ccs));
        }

        return result;
    }

    /// <summary>
    /// Tạp chất / CCS thanh toán theo chính sách FRM đã lưu (bảng trip_policies, theo mã phiếu cân; bỏ bản xem trước).
    /// CCS = 0 nghĩa là FRM chưa có kết quả CCS → không hiện. Bảng chưa có / lỗi đọc → trống, không chặn lưới.
    /// </summary>
    private async Task<Dictionary<string, (decimal? Impurity, decimal? Ccs)>> LoadPaidFiguresAsync(
        string[] tickets, CancellationToken ct)
    {
        var map = new Dictionary<string, (decimal?, decimal?)>(StringComparer.Ordinal);
        if (tickets.Length == 0) return map;
        try
        {
            var conn = db.Database.GetDbConnection();
            var opened = conn.State != System.Data.ConnectionState.Open;
            if (opened) await conn.OpenAsync(ct);
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT so_phieu_can, tap_chat_cs, ccs_chinh_sach FROM trip_policies " +
                                  "WHERE NOT is_preview AND so_phieu_can = ANY(@tickets)";
                cmd.Parameters.Add(new Npgsql.NpgsqlParameter("tickets", tickets));
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    decimal? tc = reader.IsDBNull(1) ? null : reader.GetDecimal(1);
                    decimal? ccs = reader.IsDBNull(2) ? null : reader.GetDecimal(2);
                    map[reader.GetString(0)] = (tc, ccs is 0m ? null : ccs);
                }
            }
            finally
            {
                if (opened) await conn.CloseAsync();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { /* chỉ là số hiển thị */ }
        return map;
    }

    public async Task<IReadOnlyList<WeighedTripRow>> ListPendingAsync(
        Guid siteId, TripCategory category = TripCategory.All, CancellationToken ct = default)
    {
        var query = db.Trips
            .AsNoTracking()
            .Where(t => t.SiteId == siteId && t.DeletedAt == null)
            .FilterByOrgUnit();

        if (category.ToSourceSystem() is { } sourceSystem)
            query = query.Where(t => t.SourceSystem == sourceSystem);

        var rows = await query
            .Where(t => t.Lifecycle == TripLifecycle.TempRegistered
                || t.Lifecycle == TripLifecycle.Registered
                || t.Lifecycle == TripLifecycle.InProgress)
            // Chưa có WeighEvent nào
            .Where(t => !db.WeighEvents.Any(e => e.TripId == t.Id))
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new WeighedTripRow(
                t.Id,
                t.TripNo,
                null,
                t.ReferenceTripNo,
                t.ReferenceTripNo,
                t.TractorPlate,
                t.TrailerPlate,
                null,
                null,
                null,
                null,
                null,
                t.SugarcaneOwner,
                t.SugarcaneType,
                null,
                null, null, null, null)) // chưa cân → chưa có tạp chất / CCS
            .ToListAsync(ct);

        return rows;
    }
}
