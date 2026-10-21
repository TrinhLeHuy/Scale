using System.Text.Json;
using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace TTC.Weighing.Infrastructure.Adapters;

/// <summary>
/// In phiếu cân theo mẫu nhà máy (R-005.1 → R-005.5).
/// Tổng hợp dữ liệu từ Trip + WeighEvent (Gross/Tare) + QualitySample (Impurity/CCS) + Site.
/// Lỗi in KHÔNG ảnh hưởng dữ liệu cân đã lưu — bọc toàn bộ logic in trong try-catch.
/// </summary>
public sealed class WeighTicketPrintAdapter(
    ITripRepository trips,
    IWeighSessionRepository weighSessions,
    IQualitySampleRepository qualitySamples,
    IWeighTicketRepository weighTickets,
    IUnitOfWork uow,
    IClock clock,
    ICurrentUser user,
    IAuditLogger audit,
    WeighingDbContext db) : IWeighTicketPrintService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<PrintTicketResult> PrintAsync(
        Guid tripId, PrinterConfig config, bool isReprint, CancellationToken ct = default)
    {
        // 1. Load trip
        var trip = await trips.GetByIdAsync(tripId, ct);
        if (trip is null)
            return new PrintTicketResult(false, null, "Chuyến không tồn tại.");

        // 2. Lấy WeighEvent hiệu lực cho vai trò Gross và Tare
        var grossEvent = await weighSessions.GetEffectiveEventAsync(
            tripId, WeightRole.Gross, segmentIndex: 0, ct);
        var tareEvent = await weighSessions.GetEffectiveEventAsync(
            tripId, WeightRole.Tare, segmentIndex: 0, ct);

        // 3. Lấy QualitySample: Impurity và CCS
        var impuritySample = await qualitySamples.GetEffectiveAsync(
            tripId, QualitySampleType.Impurity, ct);
        var ccsSample = await qualitySamples.GetEffectiveAsync(
            tripId, QualitySampleType.Ccs, ct);

        // 4. Lấy thông tin Site
        var site = await db.Sites
            .Where(s => s.Id == trip.SiteId)
            .FirstOrDefaultAsync(ct);

        var siteName = site?.Name ?? "N/A";
        var siteCode = site?.Code ?? "NA";

        // 5. Tính net weight
        decimal? netKg = null;
        if (grossEvent is not null && tareEvent is not null)
            netKg = grossEvent.NormalizedKg - tareEvent.NormalizedKg;

        // 6. Xử lý in lại hoặc in mới
        string ticketNumber;
        WeighTicket? existingTicket = null;

        if (isReprint)
        {
            // In lại: tìm phiếu gốc, tăng PrintCount, tạo bản reprint mới
            existingTicket = await weighTickets.GetByTripAsync(tripId, ct);
            if (existingTicket is null)
                return new PrintTicketResult(false, null, "Không tìm thấy phiếu cân gốc để in lại.");

            ticketNumber = existingTicket.TicketNumber;

            // Tăng PrintCount trên phiếu gốc
            await weighTickets.IncrementPrintCountAsync(existingTicket.Id, ct);
        }
        else
        {
            // In mới: sinh số phiếu cân
            ticketNumber = await GenerateTicketNumberAsync(siteCode, ct);

            // Cập nhật TicketNumber trên Trip
            trip.TicketNumber = ticketNumber;
            trip.UpdatedAt = clock.UtcNow;
            trip.UpdatedBy = user.UserId;
        }

        // 7. Build WeighTicketData
        var vehicleTypeName = trip.VehicleType?.ToString();
        var ticketData = new WeighTicketData(
            TicketNumber: ticketNumber,
            TripNo: trip.TripNo,
            ReferenceNo: trip.ReferenceTripNo,
            Plate: trip.TractorPlate,
            TrailerPlate: trip.TrailerPlate,
            VehicleTypeName: vehicleTypeName,
            CustomerOrOwner: trip.SugarcaneOwner,
            Commodity: trip.SugarcaneType,
            // TODO: Warehouse entity chưa tồn tại trong Domain — cần tạo khi có schema.
            // Khi có, tra cứu: db.Set<Warehouse>().Where(w => w.Id == trip.AssignedWarehouseId.Value).Select(w => w.Name).FirstOrDefaultAsync(ct)
            Warehouse: null,
            ShipmentNo: trip.ReferenceTripNo,
            GrossKg: grossEvent?.NormalizedKg,
            GrossTime: grossEvent?.MeasuredAt,
            TareKg: tareEvent?.NormalizedKg,
            TareTime: tareEvent?.MeasuredAt,
            NetKg: netKg,
            ImpurityPercent: impuritySample?.ImpurityPercent,
            CcsValue: ccsSample?.CcsValue,
            BrixValue: null,  // TODO: Lưu Brix vào QualitySample khi có schema mới
            PolValue: null,   // TODO: Lưu Pol vào QualitySample khi có schema mới
            PolDocValue: null, // TODO: Lưu Pol cải chính vào QualitySample khi có schema mới
            SugarcaneType: trip.SugarcaneType,
            SugarcaneOwner: trip.SugarcaneOwner,
            SiteName: siteName,
            PrintedAt: clock.UtcNow,
            IsReprint: isReprint);

        // 8. Ghi WeighTicket entity với SnapshotJson
        var snapshotJson = JsonSerializer.Serialize(ticketData, JsonOptions);
        var now = clock.UtcNow;

        var ticket = new WeighTicket
        {
            TripId = tripId,
            TicketNumber = ticketNumber,
            TemplateCode = config.TemplateCode,
            PrintedAt = now,
            PrintedBy = user.DisplayName,
            IsReprint = isReprint,
            PrintCount = isReprint ? (existingTicket!.PrintCount + 1) : 1,
            SnapshotJson = snapshotJson,
            CreatedAt = now,
            CreatedBy = user.UserId
        };

        await weighTickets.AddAsync(ticket, ct);

        // 9. Ghi audit log
        var auditAction = isReprint ? "WEIGH_TICKET_REPRINTED" : "WEIGH_TICKET_PRINTED";
        await audit.LogAsync(
            auditAction,
            tripId,
            user.UserId,
            isReprint ? $"{{\"originalTicketId\":\"{existingTicket?.Id}\"}}" : null,
            $"{{\"ticketNumber\":\"{ticketNumber}\",\"templateCode\":\"{config.TemplateCode}\",\"copies\":{config.Copies},\"isReprint\":{(isReprint ? "true" : "false")}}}",
            isReprint ? "In lại phiếu cân" : null,
            Guid.NewGuid().ToString(),
            ct);

        // 10. Lưu dữ liệu — TRƯỚC khi in để đảm bảo dữ liệu không mất dù in lỗi
        await uow.SaveChangesAsync(ct);

        // 11. Thực hiện in phiếu — lỗi in KHÔNG ảnh hưởng dữ liệu đã lưu
        try
        {
            // TODO: Implement actual printing logic using System.Drawing.Printing
            // hoặc reporting library (ReportViewer, FastReport, etc.) tùy theo nhà máy.
            // Hiện tại placeholder — logic in thực tế phụ thuộc vào thư viện in được chọn.
            //
            // Ví dụ sử dụng System.Drawing.Printing:
            // var printDoc = new PrintDocument();
            // printDoc.PrinterSettings.PrinterName = config.PrinterName;
            // printDoc.PrinterSettings.Copies = (short)config.Copies;
            // printDoc.PrintPage += (sender, e) => RenderTicket(e.Graphics, ticketData);
            // printDoc.Print();
        }
        catch (Exception ex)
        {
            // Lỗi in không ảnh hưởng dữ liệu cân đã lưu (R-005.4).
            // Dữ liệu phiếu cân đã được ghi DB thành công, cho phép in lại sau.
            return new PrintTicketResult(true, ticketNumber,
                $"Dữ liệu phiếu đã lưu nhưng máy in gặp lỗi: {ex.Message}. Vui lòng in lại.");
        }

        return new PrintTicketResult(true, ticketNumber, null);
    }

    public Task<IReadOnlyList<PrinterInfo>> GetAvailablePrintersAsync(
        CancellationToken ct = default)
    {
        var printers = new List<PrinterInfo>();

        try
        {
            // System.Drawing.Printing chỉ khả dụng trên Windows khi có System.Drawing.Common.
            // Sử dụng reflection để tránh hard-dependency trên project Infrastructure (net8.0).
            // Logic in thực tế sẽ được gọi từ WinForms layer (có sẵn System.Drawing).
            var printSettingsType = Type.GetType(
                "System.Drawing.Printing.PrinterSettings, System.Drawing.Common");

            if (printSettingsType is not null)
            {
                var installedPrinters = printSettingsType.GetProperty("InstalledPrinters");
                var collection = installedPrinters?.GetValue(null) as System.Collections.IEnumerable;

                if (collection is not null)
                {
                    foreach (var name in collection)
                    {
                        var printerName = name?.ToString() ?? "";
                        var settings = Activator.CreateInstance(printSettingsType);
                        printSettingsType.GetProperty("PrinterName")?.SetValue(settings, printerName);
                        var isValid = (bool)(printSettingsType.GetProperty("IsValid")?.GetValue(settings) ?? false);
                        printers.Add(new PrinterInfo(printerName, isValid));
                    }
                }
            }
        }
        catch (Exception)
        {
            // Trên môi trường không có System.Drawing (Linux, CI/test), trả danh sách rỗng.
        }

        return Task.FromResult<IReadOnlyList<PrinterInfo>>(printers);
    }

    /// <summary>
    /// Sinh số phiếu cân duy nhất theo format: PC-{siteCode}-{yyyyMMdd}-{sequence}.
    /// Dùng sequence riêng biệt với trip_no_seq.
    /// </summary>
    private async Task<string> GenerateTicketNumberAsync(string siteCode, CancellationToken ct)
    {
        var today = clock.UtcNow.ToString("yyyyMMdd");

        // Đếm số phiếu cân đã in trong ngày cho site, cộng thêm 1 làm sequence.
        // Cách này đơn giản và phù hợp khi chưa có PostgreSQL sequence riêng cho phiếu cân.
        var countToday = await db.WeighTickets
            .CountAsync(t =>
                !t.IsReprint
                && t.TicketNumber.StartsWith($"PC-{siteCode}-{today}"), ct);

        var sequence = countToday + 1;
        return $"PC-{siteCode}-{today}-{sequence:D4}";
    }
}
