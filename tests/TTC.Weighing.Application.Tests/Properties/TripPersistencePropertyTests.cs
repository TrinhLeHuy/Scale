using FsCheck;
using FsCheck.Xunit;
using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Application.Tests.Properties;

/// <summary>
/// Property 9: Trip entity persistence round-trip.
/// **Validates: Requirements 11.1, 11.2, 11.3, 11.4, 11.5, 11.6**
///
/// - Với mọi Trip hợp lệ với bất kỳ tổ hợp giá trị trường mới (bao gồm null) → set rồi đọc lại → mọi trường giữ nguyên
/// - Trip cũ với tất cả trường mới null vẫn load và IsOperable trả đúng kết quả
/// - VehicleType enum round-trips through string conversion (EF stores as string)
/// </summary>
public class TripPersistencePropertyTests
{
    // --- FsCheck generators ---

    /// <summary>Generates a nullable VehicleType (null, Container, Truck, Trailer).</summary>
    private static Gen<VehicleType?> GenNullableVehicleType() =>
        Gen.OneOf(
            Gen.Constant<VehicleType?>(null),
            Gen.Elements(VehicleType.Container, VehicleType.Truck, VehicleType.Trailer)
                .Select(v => (VehicleType?)v));

    /// <summary>Generates a nullable positive int for QueueSequenceNumber (null or 1-10000).</summary>
    private static Gen<int?> GenNullableQueueSequence() =>
        Gen.OneOf(
            Gen.Constant<int?>(null),
            Gen.Choose(1, 10000).Select(n => (int?)n));

    /// <summary>Generates a nullable Guid (null or random Guid).</summary>
    private static Gen<Guid?> GenNullableGuid() =>
        Gen.OneOf(
            Gen.Constant<Guid?>(null),
            Gen.Fresh(() => (Guid?)Guid.NewGuid()));

    /// <summary>Generates a nullable non-empty string (null or random alphanumeric 2-50 chars).</summary>
    private static Gen<string?> GenNullableString() =>
        Gen.OneOf(
            Gen.Constant<string?>(null),
            from len in Gen.Choose(2, 50)
            from chars in Gen.ArrayOf(len, Gen.Elements(
                "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 -_".ToCharArray()))
            select (string?)new string(chars));

    /// <summary>
    /// Record capturing all new Trip fields for property-based testing.
    /// </summary>
    private sealed record TripNewFields(
        VehicleType? VehicleType,
        int? QueueSequenceNumber,
        Guid? AssignedWeighbridgeId,
        Guid? AssignedWarehouseId,
        Guid? GateId,
        string? SugarcaneType,
        string? SugarcaneVariety,
        string? SugarcaneOwner,
        string? HarvestMethod,
        string? Station,
        string? CropSeason);

    /// <summary>Generates arbitrary combinations of all new Trip fields (including null for each).</summary>
    private static Gen<TripNewFields> GenTripNewFields() =>
        from vehicleType in GenNullableVehicleType()
        from queueSeq in GenNullableQueueSequence()
        from weighbridgeId in GenNullableGuid()
        from warehouseId in GenNullableGuid()
        from gateId in GenNullableGuid()
        from sugarcaneType in GenNullableString()
        from sugarcaneVariety in GenNullableString()
        from sugarcaneOwner in GenNullableString()
        from harvestMethod in GenNullableString()
        from station in GenNullableString()
        from cropSeason in GenNullableString()
        select new TripNewFields(
            vehicleType, queueSeq, weighbridgeId, warehouseId, gateId,
            sugarcaneType, sugarcaneVariety, sugarcaneOwner, harvestMethod,
            station, cropSeason);

    /// <summary>Creates a Trip with base fields populated and applies the given new fields.</summary>
    private static Trip CreateTripWithFields(TripNewFields fields)
    {
        var trip = new Trip
        {
            TripNo = "TT-TEST-001",
            SiteId = Guid.NewGuid(),
            WorkflowProfileId = Guid.NewGuid(),
            WorkflowProfileVersion = 1,
            SourceSystem = "FRM",
            ReferenceTripNo = "REF-001",
            TractorPlate = "51A-12345",
            Lifecycle = TripLifecycle.Registered,
            ExecutionStatus = ExecutionStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "test-operator",
            // Apply new fields
            VehicleType = fields.VehicleType,
            QueueSequenceNumber = fields.QueueSequenceNumber,
            AssignedWeighbridgeId = fields.AssignedWeighbridgeId,
            AssignedWarehouseId = fields.AssignedWarehouseId,
            GateId = fields.GateId,
            SugarcaneType = fields.SugarcaneType,
            SugarcaneVariety = fields.SugarcaneVariety,
            SugarcaneOwner = fields.SugarcaneOwner,
            HarvestMethod = fields.HarvestMethod,
            Station = fields.Station,
            CropSeason = fields.CropSeason
        };
        return trip;
    }

    // ================================================================
    // Property 9a: All new fields round-trip through set/get
    // For any Trip with arbitrary combinations of new field values
    // (including null), setting all fields and reading them back
    // preserves all values.
    // ================================================================

    [Property(MaxTest = 200)]
    public Property AllNewFields_SetThenGet_PreservesValues()
    {
        return Prop.ForAll(
            GenTripNewFields().ToArbitrary(),
            fields =>
            {
                var trip = CreateTripWithFields(fields);

                // Read back all new fields and verify they match
                var vehicleTypeMatch = trip.VehicleType == fields.VehicleType;
                var queueSeqMatch = trip.QueueSequenceNumber == fields.QueueSequenceNumber;
                var weighbridgeMatch = trip.AssignedWeighbridgeId == fields.AssignedWeighbridgeId;
                var warehouseMatch = trip.AssignedWarehouseId == fields.AssignedWarehouseId;
                var gateMatch = trip.GateId == fields.GateId;
                var sugarcaneTypeMatch = trip.SugarcaneType == fields.SugarcaneType;
                var sugarcaneVarietyMatch = trip.SugarcaneVariety == fields.SugarcaneVariety;
                var sugarcaneOwnerMatch = trip.SugarcaneOwner == fields.SugarcaneOwner;
                var harvestMethodMatch = trip.HarvestMethod == fields.HarvestMethod;
                var stationMatch = trip.Station == fields.Station;
                var cropSeasonMatch = trip.CropSeason == fields.CropSeason;

                return (vehicleTypeMatch && queueSeqMatch && weighbridgeMatch &&
                        warehouseMatch && gateMatch && sugarcaneTypeMatch &&
                        sugarcaneVarietyMatch && sugarcaneOwnerMatch &&
                        harvestMethodMatch && stationMatch && cropSeasonMatch)
                    .Label($"Field mismatch — " +
                           $"VehicleType: {vehicleTypeMatch}, QueueSeq: {queueSeqMatch}, " +
                           $"Weighbridge: {weighbridgeMatch}, Warehouse: {warehouseMatch}, " +
                           $"Gate: {gateMatch}, SugarcaneType: {sugarcaneTypeMatch}, " +
                           $"SugarcaneVariety: {sugarcaneVarietyMatch}, SugarcaneOwner: {sugarcaneOwnerMatch}, " +
                           $"HarvestMethod: {harvestMethodMatch}, Station: {stationMatch}, " +
                           $"CropSeason: {cropSeasonMatch}");
            });
    }

    // ================================================================
    // Property 9b: Legacy trip (all new fields null) still loads
    // and IsOperable returns correct result.
    // ================================================================

    [Property(MaxTest = 100)]
    public Property LegacyTrip_AllNewFieldsNull_IsOperableCorrect()
    {
        // Generate lifecycle + execution status combos to verify IsOperable
        var genLifecycle = Gen.Elements(
            TripLifecycle.TempRegistered,
            TripLifecycle.Registered,
            TripLifecycle.InProgress,
            TripLifecycle.WeighingCompleted,
            TripLifecycle.Reconciled,
            TripLifecycle.Posted,
            TripLifecycle.Closed,
            TripLifecycle.Cancelled);

        var genExecStatus = Gen.Elements(ExecutionStatus.Active, ExecutionStatus.Paused);

        return Prop.ForAll(
            genLifecycle.ToArbitrary(),
            genExecStatus.ToArbitrary(),
            (lifecycle, execStatus) =>
            {
                // Legacy trip: all new fields are null (default)
                var trip = new Trip
                {
                    TripNo = "TT-LEGACY-001",
                    SiteId = Guid.NewGuid(),
                    WorkflowProfileId = Guid.NewGuid(),
                    WorkflowProfileVersion = 1,
                    SourceSystem = "FRM",
                    Lifecycle = lifecycle,
                    ExecutionStatus = execStatus,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CreatedBy = "legacy-operator"
                    // All new fields left as default (null)
                };

                // Verify all new fields are indeed null
                var allNewFieldsNull =
                    trip.VehicleType == null &&
                    trip.QueueSequenceNumber == null &&
                    trip.AssignedWeighbridgeId == null &&
                    trip.AssignedWarehouseId == null &&
                    trip.GateId == null &&
                    trip.SugarcaneType == null &&
                    trip.SugarcaneVariety == null &&
                    trip.SugarcaneOwner == null &&
                    trip.HarvestMethod == null &&
                    trip.Station == null &&
                    trip.CropSeason == null;

                // IsOperable = Active AND NOT (Closed or Cancelled)
                var expectedIsOperable =
                    execStatus == ExecutionStatus.Active &&
                    lifecycle is not (TripLifecycle.Closed or TripLifecycle.Cancelled);

                var isOperableCorrect = trip.IsOperable == expectedIsOperable;

                return (allNewFieldsNull && isOperableCorrect)
                    .Label($"Legacy trip Lifecycle={lifecycle}, ExecStatus={execStatus}: " +
                           $"AllNewFieldsNull={allNewFieldsNull}, " +
                           $"IsOperable={trip.IsOperable} (expected={expectedIsOperable})");
            });
    }

    // ================================================================
    // Property 9c: VehicleType enum round-trips through string
    // conversion (EF Core stores enum as string).
    // ================================================================

    [Property(MaxTest = 100)]
    public Property VehicleType_StringRoundTrip_PreservesValue()
    {
        var genVehicleType = Gen.Elements(
            VehicleType.Container,
            VehicleType.Truck,
            VehicleType.Trailer);

        return Prop.ForAll(
            genVehicleType.ToArbitrary(),
            vehicleType =>
            {
                // Simulate EF Core string storage: enum → ToString → Enum.Parse
                var asString = vehicleType.ToString();
                var parsed = Enum.Parse<VehicleType>(asString);

                return (parsed == vehicleType)
                    .Label($"VehicleType.{vehicleType} → \"{asString}\" → {parsed}: " +
                           $"round-trip {(parsed == vehicleType ? "OK" : "FAILED")}");
            });
    }

    // ================================================================
    // Property 9d: Nullable VehicleType round-trips through string
    // (null stays null, non-null parses back correctly).
    // ================================================================

    [Property(MaxTest = 100)]
    public Property NullableVehicleType_StringRoundTrip_PreservesValue()
    {
        return Prop.ForAll(
            GenNullableVehicleType().ToArbitrary(),
            vehicleType =>
            {
                // Simulate nullable enum string storage
                string? asString = vehicleType?.ToString();
                VehicleType? parsed = asString != null
                    ? Enum.Parse<VehicleType>(asString)
                    : null;

                return (parsed == vehicleType)
                    .Label($"Nullable VehicleType {vehicleType?.ToString() ?? "null"} → " +
                           $"\"{asString ?? "null"}\" → {parsed?.ToString() ?? "null"}: " +
                           $"round-trip {(parsed == vehicleType ? "OK" : "FAILED")}");
            });
    }
}
