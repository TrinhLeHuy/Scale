using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TTC.Weighing.Application.UseCases.Integration;
using TTC.Weighing.Application.UseCases.Reconcile;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Domain.Rules;
using TTC.Weighing.Infrastructure.Persistence;

namespace TTC.Weighing.Infrastructure.Integration;

/// <summary>
/// Chính sách dung sai đọc từ cấu hình đã duyệt. Chưa duyệt thì trả policy rỗng để
/// engine kết luận Pending, đúng theo D-06 — không tự đặt ngưỡng.
/// </summary>
public sealed class ApprovedTolerancePolicyProvider(TolerancePolicy? approved = null)
    : ITolerancePolicyProvider
{
    private readonly TolerancePolicy _policy = approved ?? new TolerancePolicy();

    public Task<TolerancePolicy> GetAsync(
        Guid profileId, int profileVersion, CancellationToken ct = default)
        => Task.FromResult(_policy);
}

/// <summary>
/// Dựng payload kết quả từ dữ liệu thật và lưu snapshot ra tệp.
/// ContractVerified = false vì hợp đồng FRM/ERP chưa được xác minh (D-02), nên thông điệp
/// sẽ nằm ở trạng thái HELD thay vì được gửi đi.
/// </summary>
public sealed class SnapshotIntegrationPayloadBuilder(WeighingDbContext db, string outboxPath)
    : IIntegrationPayloadBuilder
{
    public async Task<IntegrationPayload> BuildAsync(
        Guid tripId, string operation, string? shipmentNo, CancellationToken ct = default)
    {
        var trip = await db.Trips.FirstAsync(t => t.Id == tripId, ct);

        var weights = await db.WeighEvents
            .Where(e => e.TripId == tripId && e.IsEffective)
            .OrderBy(e => e.MeasuredAt)
            .Select(e => new
            {
                role = e.Role.ToString(),
                raw_weight = e.RawWeight.ToString(System.Globalization.CultureInfo.InvariantCulture),
                raw_uom = e.RawUom,
                normalized_kg = e.NormalizedKg.ToString(System.Globalization.CultureInfo.InvariantCulture),
                measured_at = e.MeasuredAt,
                source = e.Source.ToString(),
                revision = e.Revision
            })
            .ToListAsync(ct);

        var impurity = await db.QualitySamples
            .Where(s => s.TripId == tripId
                && s.Type == QualitySampleType.Impurity
                && s.IsEffective
                && s.Status == QualityResultStatus.Valid)
            .Select(s => s.ImpurityPercent)
            .FirstOrDefaultAsync(ct);

        // Chữ đường chỉ đưa vào payload khi kết quả hợp lệ; đang chờ thì để null,
        // không in số 0 và không gửi giá trị không rõ nguồn.
        var ccs = await db.QualitySamples
            .Where(s => s.TripId == tripId
                && s.Type == QualitySampleType.Ccs
                && s.IsEffective
                && s.Status == QualityResultStatus.Valid)
            .Select(s => s.CcsValue)
            .FirstOrDefaultAsync(ct);

        var payload = new
        {
            operation,
            trip_no = trip.TripNo,
            source_reference = trip.ReferenceTripNo,
            shipment_no = shipmentNo,
            tractor_plate = trip.TractorPlate,
            trailer_plate = trip.TrailerPlate,
            weights,
            impurity_percent = impurity,
            ccs_value = ccs,
            built_at = DateTimeOffset.UtcNow
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

        Directory.CreateDirectory(outboxPath);
        var file = Path.Combine(outboxPath, $"{trip.TripNo}-{operation}-{hash[..8]}.json");
        await File.WriteAllTextAsync(file, json, ct);

        return new IntegrationPayload(
            PayloadRef: file,
            PayloadHash: hash,
            InterfaceReference: null,

            // Không tự nhận là đã có hợp đồng: D-02 phải cung cấp request/response đã kiểm chứng.
            ContractVerified: false,

            // Chưa chứng minh API chống trùng nên không cho retry tự động (03_Du_lieu mục 7).
            IdempotencySupported: false);
    }
}
