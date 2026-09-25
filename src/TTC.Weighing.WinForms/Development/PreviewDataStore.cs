using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Domain.Rules;

namespace TTC.Weighing.WinForms.Development;

/// <summary>
/// CHỈ DÙNG XEM TRƯỚC GIAO DIỆN TRÊN MÁY DEV.
/// Dữ liệu nằm trong bộ nhớ, không phải nguồn kế hoạch FRM/ERP và không thay thế kiểm thử thật.
/// Ngưỡng ở đây là giá trị minh họa, KHÔNG phải giá trị đã duyệt cho vận hành (D-04/D-05/D-06).
/// </summary>
public sealed class PreviewDataStore
{
    public Site Site { get; }
    public Gate MainGate { get; }
    public Lane Lane { get; }
    public Weighbridge BridgeA { get; }
    public Weighbridge BridgeB { get; }
    public Guid WarehouseId { get; } = Guid.NewGuid();

    public List<Device> Devices { get; } = [];
    public List<WorkflowProfile> Profiles { get; } = [];
    public List<Trip> Trips { get; } = [];
    public List<QrToken> QrTokens { get; } = [];
    public List<WeighSession> WeighSessions { get; } = [];
    public List<WeighEvent> WeighEvents { get; } = [];
    public List<QualitySample> QualitySamples { get; } = [];
    public List<MeasurementSession> MeasurementSessions { get; } = [];
    public List<DeviceEvent> DeviceEvents { get; } = [];
    public List<Reconciliation> Reconciliations { get; } = [];
    public List<Allocation> Allocations { get; } = [];
    public List<IntegrationMessage> IntegrationMessages { get; } = [];
    public List<GateDecision> GateDecisions { get; } = [];
    public List<GateCommand> GateCommands { get; } = [];
    public List<Approval> Approvals { get; } = [];
    public List<AuditLogEntry> AuditLog { get; } = [];
    public List<Incident> Incidents { get; } = [];
    public List<ExternalDocument> Documents { get; } = [];
    public List<TripDocumentLine> DocumentLines { get; } = [];
    public List<BomSnapshot> BomSnapshots { get; } = [];
    public List<WeighTicket> WeighTickets { get; } = [];

    public WorkflowProfile CaneInProfile => Profiles[0];
    public WorkflowProfile MaterialOutProfile => Profiles[1];

    /// <summary>Chuyến mía đang ở bàn cân, dùng cho UI-03.</summary>
    public Trip ActiveTrip => Trips[0];

    private long _tripCounter = 1;

    public PreviewDataStore()
    {
        var now = DateTimeOffset.UtcNow;

        Site = new Site { Code = "TTC-DEMO", Name = "Nhà máy demo", CreatedAt = now };
        MainGate = new Gate { SiteId = Site.Id, Code = "CONG-01", Name = "Cổng chính", CreatedAt = now };

        BridgeA = NewBridge("BAN-CAN-01", now);
        BridgeB = NewBridge("BAN-CAN-02", now);

        Lane = new Lane
        {
            SiteId = Site.Id,
            GateId = MainGate.Id,
            WeighbridgeId = BridgeA.Id,
            Code = "LAN-01",
            CreatedAt = now
        };

        SeedDevices(now);
        SeedProfiles(now);
        SeedBom(now);
        SeedTrips(now);
    }

    // Công tắc để quan sát hành vi chặn của engine, chỉ có ở chế độ xem trước.
    public bool ImpurityMeasured { get; set; }
    public bool VehicleIdentityVerified { get; set; } = true;
    public bool CcsPending { get; set; } = true;
    public bool BarrierControllerReady { get; set; } = true;
    public bool BarrierSafetyOk { get; set; } = true;

    /// <summary>Bật để mô phỏng hợp đồng API đã xác minh (thực tế D-02 chưa duyệt).</summary>
    public bool PretendContractVerified { get; set; }

    /// <summary>Bật để mô phỏng chính sách dung sai đã duyệt (thực tế D-06 chưa duyệt).</summary>
    public bool PretendToleranceApproved { get; set; }

    public string NextTripNo() => $"{Site.Code}-{DateTime.UtcNow:yyyyMM}-{_tripCounter++:D6}";

    private Weighbridge NewBridge(string code, DateTimeOffset now) => new()
    {
        SiteId = Site.Id,
        Code = code,
        MinCapacityKg = 500m,
        MaxCapacityKg = 80_000m,
        ResolutionKg = 10m,
        CreatedAt = now
    };

    private void SeedDevices(DateTimeOffset now)
    {
        foreach (var (code, deviceClass) in new[]
        {
            ("SCALE-01", "SCALE"),
            ("CAM-POS-01", "POSITION_AI"),
            ("QR-READER-01", "QR"),
            ("BARRIER-01", "BARRIER"),
            ("IOT-MASS-01", "IOT_MASS"),
            ("CAM-BAG-01", "BAG_AI")
        })
        {
            Devices.Add(new Device
            {
                SiteId = Site.Id,
                LaneId = Lane.Id,
                Code = code,
                DeviceClass = deviceClass,
                MappingVersion = "preview-0.1",
                IsEnabled = true,
                CreatedAt = now
            });
        }
    }

    private void SeedProfiles(DateTimeOffset now)
    {
        // Ngưỡng minh họa để giao diện chạy được. Vận hành phải lấy từ D-04/D-05 đã ký duyệt.
        var thresholds = new WeighThresholds
        {
            StabilityWindow = TimeSpan.FromSeconds(5),
            StabilityDeltaKg = 1m,
            ScaleSignalMaxAge = TimeSpan.FromSeconds(2),
            AiEvidenceMaxAge = TimeSpan.FromSeconds(3),
            PositionConfidenceThreshold = 0.90m
        };

        Profiles.Add(new WorkflowProfile
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
            WeightRoleSequence = [WeightRole.Gross, WeightRole.Tare],
            GateCheckpointsRequired = true,
            ImpurityRequiredBeforeSecondWeight = true,
            Thresholds = thresholds,
            EffectiveFrom = now.AddDays(-30),
            ApprovedBy = "PREVIEW_ONLY",
            ApprovedAt = now.AddDays(-30),
            CreatedAt = now
        });

        Profiles.Add(new WorkflowProfile
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
            WeightRoleSequence = [WeightRole.Tare, WeightRole.Gross],
            GateCheckpointsRequired = true,
            WarehouseConfirmationRequired = true,
            BomSnapshotRequired = true,
            Thresholds = thresholds,
            EffectiveFrom = now.AddDays(-30),
            ApprovedBy = "PREVIEW_ONLY",
            ApprovedAt = now.AddDays(-30),
            CreatedAt = now
        });
    }

    private void SeedBom(DateTimeOffset now)
    {
        // BOM chỉ có khối lượng danh nghĩa — cố tình thiếu min/max để thấy engine trả MissingData (D-06).
        BomSnapshots.Add(new BomSnapshot
        {
            SourceSystem = "ERP",
            ItemCode = "SUGAR-50KG",
            Uom = "BAG",
            NominalKgPerUnit = 50m,
            MassBasis = MassBasis.PackagedGoods,
            SourceVersion = "preview-1",
            SnapshotHash = "preview-hash",
            EffectiveAt = now.AddDays(-10),
            CreatedAt = now
        });
    }

    private void SeedTrips(DateTimeOffset now)
    {
        AddTrip(CaneInProfile, "LD-2026-000123", "51C-123.45", "51R-678.90",
            TripLifecycle.Registered, now.AddMinutes(-5));

        var outbound = AddTrip(MaterialOutProfile, "SO-2026-004521", "51C-999.11", null,
            TripLifecycle.InProgress, now.AddMinutes(-40));

        var document = new ExternalDocument
        {
            SourceSystem = "ERP",
            DocumentType = "SO",
            ReferenceNo = "SO-2026-004521",
            ShipmentNo = "SHP-0001",
            SourceVersion = "1",
            Status = "RELEASED",
            CreatedAt = now
        };
        Documents.Add(document);

        DocumentLines.Add(new TripDocumentLine
        {
            TripId = outbound.Id,
            DocumentId = document.Id,
            SourceLineId = "1",
            ItemCode = "SUGAR-50KG",
            WarehouseId = WarehouseId,
            PlannedQuantity = 600m,
            PlannedUom = "BAG",
            ConfirmedQuantity = 600m,
            ConfirmedUom = "BAG",
            CreatedAt = now
        });
    }

    private Trip AddTrip(
        WorkflowProfile profile, string referenceNo, string plate, string? trailer,
        TripLifecycle lifecycle, DateTimeOffset createdAt)
    {
        var trip = new Trip
        {
            TripNo = NextTripNo(),
            SiteId = Site.Id,
            WorkflowProfileId = profile.Id,
            WorkflowProfileVersion = profile.ProfileVersion,
            SourceSystem = profile.SourceSystem,
            ReferenceTripNo = referenceNo,
            VehicleId = Guid.NewGuid(),
            TractorPlate = plate,
            TrailerPlate = trailer,
            DriverRef = "TX-0007",
            Lifecycle = lifecycle,
            ExecutionStatus = ExecutionStatus.Active,
            Version = 1,
            CreatedAt = createdAt
        };

        var token = new QrToken
        {
            TokenHash = Convert.ToHexString(Guid.NewGuid().ToByteArray()),
            Type = QrTokenType.Trip,
            Status = QrTokenStatus.Issued,
            TripId = trip.Id,
            SiteId = Site.Id,
            IssuedAt = createdAt,
            ExpiresAt = createdAt.AddHours(24),
            CreatedAt = createdAt
        };

        trip.QrTokens.Add(token);
        QrTokens.Add(token);
        Trips.Add(trip);

        return trip;
    }
}
