using Microsoft.EntityFrameworkCore;
using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Domain.Rules;
using TTC.Weighing.Infrastructure.Persistence;

namespace TTC.Weighing.Scenario;

/// <summary>Kết quả seed, dùng làm tham chiếu cho kịch bản.</summary>
public sealed record SeedResult(
    Guid SiteId,
    string SiteCode,
    Guid GateId,
    Guid LaneId,
    Guid BridgeAId,
    Guid BridgeBId,
    Guid WarehouseId,
    Guid CaneProfileId,
    int CaneProfileVersion,
    Guid MaterialOutProfileId,
    int MaterialOutProfileVersion,
    Guid ScaleDeviceId,
    Guid IotDeviceId,
    Guid BagAiDeviceId);

/// <summary>
/// Nạp danh mục nền cho môi trường thử: nhà máy, cổng, làn, bàn cân, thiết bị, hồ sơ nghiệp vụ, BOM.
/// Chạy lại nhiều lần không tạo trùng.
///
/// Ngưỡng trong hồ sơ ở đây là GIÁ TRỊ THỬ để kịch bản chạy được, không phải giá trị
/// đã ký duyệt cho vận hành. D-04 và D-05 phải chốt trước khi lên production.
/// </summary>
public static class MasterDataSeeder
{
    private const string SiteCode = "TTC-01";

    public static async Task<SeedResult> EnsureAsync(WeighingDbContext db, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var site = await db.Sites.FirstOrDefaultAsync(s => s.Code == SiteCode, ct);

        if (site is null)
        {
            site = new Site
            {
                Code = SiteCode,
                Name = "Nhà máy thử nghiệm số 1",
                TimeZoneId = "SE Asia Standard Time",
                CreatedAt = now,
                CreatedBy = "seeder"
            };

            await db.Sites.AddAsync(site, ct);
            await db.SaveChangesAsync(ct);
        }

        var gate = await EnsureGateAsync(db, site.Id, now, ct);
        var bridgeA = await EnsureBridgeAsync(db, site.Id, "BAN-CAN-01", now, ct);
        var bridgeB = await EnsureBridgeAsync(db, site.Id, "BAN-CAN-02", now, ct);
        var lane = await EnsureLaneAsync(db, site.Id, gate.Id, bridgeA.Id, now, ct);

        var scale = await EnsureDeviceAsync(db, site.Id, lane.Id, "SCALE-01", "SCALE", now, ct);
        await EnsureDeviceAsync(db, site.Id, lane.Id, "CAM-POS-01", "POSITION_AI", now, ct);
        await EnsureDeviceAsync(db, site.Id, lane.Id, "QR-READER-01", "QR", now, ct);
        await EnsureDeviceAsync(db, site.Id, lane.Id, "BARRIER-01", "BARRIER", now, ct);
        var iot = await EnsureDeviceAsync(db, site.Id, lane.Id, "IOT-MASS-01", "IOT_MASS", now, ct);
        var bagAi = await EnsureDeviceAsync(db, site.Id, lane.Id, "CAM-BAG-01", "BAG_AI", now, ct);

        var caneProfile = await EnsureCaneProfileAsync(db, now, ct);
        var materialProfile = await EnsureMaterialOutProfileAsync(db, now, ct);

        await EnsureBomAsync(db, now, ct);

        await db.SaveChangesAsync(ct);

        return new SeedResult(
            SiteId: site.Id,
            SiteCode: site.Code,
            GateId: gate.Id,
            LaneId: lane.Id,
            BridgeAId: bridgeA.Id,
            BridgeBId: bridgeB.Id,
            WarehouseId: lane.Id,
            CaneProfileId: caneProfile.Id,
            CaneProfileVersion: caneProfile.ProfileVersion,
            MaterialOutProfileId: materialProfile.Id,
            MaterialOutProfileVersion: materialProfile.ProfileVersion,
            ScaleDeviceId: scale.Id,
            IotDeviceId: iot.Id,
            BagAiDeviceId: bagAi.Id);
    }

    private static async Task<Gate> EnsureGateAsync(
        WeighingDbContext db, Guid siteId, DateTimeOffset now, CancellationToken ct)
    {
        var gate = await db.Gates.FirstOrDefaultAsync(g => g.SiteId == siteId && g.Code == "CONG-01", ct);
        if (gate is not null) return gate;

        gate = new Gate
        {
            SiteId = siteId,
            Code = "CONG-01",
            Name = "Cổng chính",
            CreatedAt = now,
            CreatedBy = "seeder"
        };

        await db.Gates.AddAsync(gate, ct);
        await db.SaveChangesAsync(ct);

        return gate;
    }

    private static async Task<Weighbridge> EnsureBridgeAsync(
        WeighingDbContext db, Guid siteId, string code, DateTimeOffset now, CancellationToken ct)
    {
        var bridge = await db.Weighbridges
            .FirstOrDefaultAsync(w => w.SiteId == siteId && w.Code == code, ct);

        if (bridge is not null) return bridge;

        bridge = new Weighbridge
        {
            SiteId = siteId,
            Code = code,
            MinCapacityKg = 500m,
            MaxCapacityKg = 80_000m,
            ResolutionKg = 10m,
            CreatedAt = now,
            CreatedBy = "seeder"
        };

        await db.Weighbridges.AddAsync(bridge, ct);
        await db.SaveChangesAsync(ct);

        return bridge;
    }

    private static async Task<Lane> EnsureLaneAsync(
        WeighingDbContext db, Guid siteId, Guid gateId, Guid bridgeId,
        DateTimeOffset now, CancellationToken ct)
    {
        var lane = await db.Lanes.FirstOrDefaultAsync(l => l.SiteId == siteId && l.Code == "LAN-01", ct);
        if (lane is not null) return lane;

        lane = new Lane
        {
            SiteId = siteId,
            GateId = gateId,
            WeighbridgeId = bridgeId,
            Code = "LAN-01",
            CreatedAt = now,
            CreatedBy = "seeder"
        };

        await db.Lanes.AddAsync(lane, ct);
        await db.SaveChangesAsync(ct);

        return lane;
    }

    private static async Task<Device> EnsureDeviceAsync(
        WeighingDbContext db, Guid siteId, Guid laneId, string code, string deviceClass,
        DateTimeOffset now, CancellationToken ct)
    {
        var device = await db.Devices
            .FirstOrDefaultAsync(d => d.SiteId == siteId && d.Code == code, ct);

        if (device is not null) return device;

        device = new Device
        {
            SiteId = siteId,
            LaneId = laneId,
            Code = code,
            DeviceClass = deviceClass,
            MappingVersion = "scenario-0.1",
            IsEnabled = true,
            CreatedAt = now,
            CreatedBy = "seeder"
        };

        await db.Devices.AddAsync(device, ct);
        await db.SaveChangesAsync(ct);

        return device;
    }

    private static async Task<WorkflowProfile> EnsureCaneProfileAsync(
        WeighingDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var profile = await db.WorkflowProfiles
            .FirstOrDefaultAsync(p => p.Code == "CANE_IN" && p.ProfileVersion == 1, ct);

        if (profile is not null) return profile;

        profile = new WorkflowProfile
        {
            Code = "CANE_IN",
            Name = "Nhập mía",
            ProfileVersion = 1,
            SourceSystem = "FRM",
            Direction = ProfileDirection.Inbound,
            Checkpoints =
            [
                CheckpointType.Registration, CheckpointType.GateIn,
                CheckpointType.FirstWeight, CheckpointType.ImpuritySample,
                CheckpointType.CcsSample, CheckpointType.SecondWeight,
                CheckpointType.Reconciliation, CheckpointType.Sync, CheckpointType.GateOut
            ],

            // Nhập mía: lần 1 cả xe có hàng, lần 2 bì xe (02_Quy_tac 4.1).
            WeightRoleSequence = [WeightRole.Gross, WeightRole.Tare],

            GateCheckpointsRequired = true,
            ImpurityRequiredBeforeSecondWeight = true,

            // GIÁ TRỊ THỬ. Cửa sổ 5 giây có căn cứ ở S02 tr.17; các ngưỡng còn lại cần D-04/D-05.
            Thresholds = new WeighThresholds
            {
                StabilityWindow = TimeSpan.FromSeconds(5),
                StabilityDeltaKg = 1m,
                ScaleSignalMaxAge = TimeSpan.FromSeconds(2),
                AiEvidenceMaxAge = TimeSpan.FromSeconds(3),
                PositionConfidenceThreshold = 0.90m
            },

            EffectiveFrom = now.AddYears(-1),
            ApprovedBy = "SCENARIO_ONLY",
            ApprovedAt = now.AddYears(-1),
            CreatedAt = now,
            CreatedBy = "seeder"
        };

        await db.WorkflowProfiles.AddAsync(profile, ct);
        await db.SaveChangesAsync(ct);

        return profile;
    }

    private static async Task<WorkflowProfile> EnsureMaterialOutProfileAsync(
        WeighingDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var profile = await db.WorkflowProfiles
            .FirstOrDefaultAsync(p => p.Code == "MATERIAL_OUT_BAGGED" && p.ProfileVersion == 1, ct);

        if (profile is not null) return profile;

        profile = new WorkflowProfile
        {
            Code = "MATERIAL_OUT_BAGGED",
            Name = "Xuất vật tư hàng bao",
            ProfileVersion = 1,
            SourceSystem = "ERP",
            Direction = ProfileDirection.Outbound,
            Checkpoints =
            [
                CheckpointType.Registration, CheckpointType.GateIn,
                CheckpointType.FirstWeight, CheckpointType.WarehouseOut,
                CheckpointType.SecondWeight, CheckpointType.Reconciliation,
                CheckpointType.Sync, CheckpointType.GateOut
            ],

            // Xuất hàng: lần 1 bì xe, lần 2 cả xe có hàng.
            WeightRoleSequence = [WeightRole.Tare, WeightRole.Gross],

            GateCheckpointsRequired = true,
            WarehouseConfirmationRequired = true,
            BomSnapshotRequired = true,

            Thresholds = new WeighThresholds
            {
                StabilityWindow = TimeSpan.FromSeconds(5),
                StabilityDeltaKg = 1m,
                ScaleSignalMaxAge = TimeSpan.FromSeconds(2),
                AiEvidenceMaxAge = TimeSpan.FromSeconds(3),
                PositionConfidenceThreshold = 0.90m
            },

            EffectiveFrom = now.AddYears(-1),
            ApprovedBy = "SCENARIO_ONLY",
            ApprovedAt = now.AddYears(-1),
            CreatedAt = now,
            CreatedBy = "seeder"
        };

        await db.WorkflowProfiles.AddAsync(profile, ct);
        await db.SaveChangesAsync(ct);

        return profile;
    }

    private static async Task EnsureBomAsync(
        WeighingDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        if (await db.BomSnapshots.AnyAsync(s => s.ItemCode == "SUGAR-50KG", ct))
            return;

        // Cố tình chỉ có khối lượng danh nghĩa, không có min/max, để thấy engine trả MissingData
        // thay vì tự dựng khoảng dung sai (S04 tr.29, D-06).
        await db.BomSnapshots.AddAsync(new BomSnapshot
        {
            SourceSystem = "ERP",
            ItemCode = "SUGAR-50KG",
            Uom = "BAG",
            NominalKgPerUnit = 50m,
            MassBasis = MassBasis.PackagedGoods,
            SourceVersion = "seed-1",
            SnapshotHash = "seed-hash-1",
            EffectiveAt = now.AddMonths(-1),
            CreatedAt = now,
            CreatedBy = "seeder"
        }, ct);
    }
}
