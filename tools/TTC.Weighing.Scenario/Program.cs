using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.Queries;
using TTC.Weighing.Application.UseCases.Gate;
using TTC.Weighing.Application.UseCases.Integration;
using TTC.Weighing.Application.UseCases.Measurement;
using TTC.Weighing.Application.UseCases.Quality;
using TTC.Weighing.Application.UseCases.Reconcile;
using TTC.Weighing.Application.UseCases.Trips;
using TTC.Weighing.Application.UseCases.Weighing;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Infrastructure;
using TTC.Weighing.Infrastructure.Devices;
using TTC.Weighing.Infrastructure.Evidence;
using TTC.Weighing.Infrastructure.Integration;
using TTC.Weighing.Infrastructure.Persistence;
using TTC.Weighing.Infrastructure.Queries;
using TTC.Weighing.Scenario;

var connectionString =
    Environment.GetEnvironmentVariable("TTC_WEIGHING_ConnectionStrings__WeighingDb")
    ?? "Host=localhost;Port=5432;Database=ttc_weighing;Username=ttc_dev;Password=ttc_dev_local_only";

var artifactRoot = Path.Combine(AppContext.BaseDirectory, "scenario-artifacts");

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine("TTC — Kịch bản nhập mía trên PostgreSQL thật");
Console.WriteLine(new string('=', 78));

var log = new ScenarioLog();

// ---------------------------------------------------------------------------
// Nạp danh mục nền
// ---------------------------------------------------------------------------
log.Step("Nạp danh mục nền và kiểm tra kết nối cơ sở dữ liệu");

var bootstrapServices = new ServiceCollection();
bootstrapServices.AddWeighingInfrastructure(connectionString);
await using var bootstrap = bootstrapServices.BuildServiceProvider();

SeedResult seed;
await using (var scope = bootstrap.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WeighingDbContext>();

    var pending = await db.Database.GetPendingMigrationsAsync();
    if (pending.Any())
    {
        Console.WriteLine($"   Còn migration chưa áp: {string.Join(", ", pending)}");
        Console.WriteLine("   Chạy: dotnet ef database update --project src/TTC.Weighing.Infrastructure");
        return 1;
    }

    seed = await MasterDataSeeder.EnsureAsync(db, CancellationToken.None);

    log.Info($"Nhà máy {seed.SiteCode}, cổng và làn đã cấu hình, 2 bàn cân, 6 thiết bị.");
    log.Info("Hồ sơ CANE_IN và MATERIAL_OUT_BAGGED đã có, BOM SUGAR-50KG chỉ có khối lượng danh nghĩa.");
}

// ---------------------------------------------------------------------------
// Dựng container cho kịch bản
// ---------------------------------------------------------------------------
var clock = new SystemClock();
var scale = new ScriptedScaleGateway(clock) { DeviceId = seed.ScaleDeviceId };
var position = new ScriptedPositionGateway(clock);
var identity = new LaneOperatorIdentityVerifier();
var barrier = new ScriptedBarrierGateway();

var services = new ServiceCollection();
services.AddWeighingInfrastructure(connectionString);

services.AddSingleton<IClock>(clock);
services.AddSingleton<IScaleSignalGateway>(scale);
services.AddSingleton<IPositionEvidenceGateway>(position);
services.AddSingleton<IVehicleIdentityVerifier>(identity);
services.AddSingleton<IBarrierControllerGateway>(barrier);
services.AddSingleton<ICurrentUser>(new ScenarioUser(seed.SiteId));

services.AddScoped<IWeighPreconditionQuery, EfWeighPreconditionQuery>();
services.AddScoped<IDeviceEventPayloadReader, NormalizedDeviceEventPayloadReader>();

services.AddSingleton<IEvidenceStore>(
    new FileSystemEvidenceStore(Path.Combine(artifactRoot, "evidence")));

// D-06 chưa duyệt dung sai nên cung cấp policy rỗng; engine sẽ kết luận Pending khi cần.
services.AddSingleton<ITolerancePolicyProvider>(new ApprovedTolerancePolicyProvider());

services.AddScoped<IIntegrationPayloadBuilder>(sp =>
    new SnapshotIntegrationPayloadBuilder(
        sp.GetRequiredService<WeighingDbContext>(),
        Path.Combine(artifactRoot, "outbox")));

// Stubs cho dependencies mới của RegisterTripHandler — chờ infrastructure implementation
services.AddSingleton<IQueueSequenceGenerator, ScenarioQueueSequenceGenerator>();
services.AddSingleton<ICameraService, ScenarioCameraService>();

services.AddScoped<RegisterTripHandler>();
services.AddScoped<QueueTripHandler>();
services.AddScoped<ResolveQrHandler>();
services.AddScoped<OpenWeighSessionHandler>();
services.AddScoped<EvaluateAndCommitWeightHandler>();
services.AddScoped<RecordImpuritySampleHandler>();
services.AddScoped<ReceiveCcsResultHandler>();
services.AddScoped<ReconcileTripHandler>();
services.AddScoped<EvaluateGateHandler>();
services.AddScoped<ConfirmVehiclePassageHandler>();
services.AddScoped<SubmitTripResultHandler>();

await using var provider = services.BuildServiceProvider();

async Task<T> UseAsync<T>(Func<IServiceProvider, Task<T>> action)
{
    await using var scope = provider.CreateAsyncScope();
    return await action(scope.ServiceProvider);
}

Guid currentTripId = Guid.Empty;

// Mọi operation thay đổi dữ liệu đều kiểm tra version mong đợi, nên phải đọc lại trước mỗi lệnh.
async Task<int> CurrentVersionAsync() => await UseAsync(sp => TripVersionAsync(sp, currentTripId));

// ---------------------------------------------------------------------------
// Kịch bản
// ---------------------------------------------------------------------------
log.Section("Phần 1 — Đăng tài và QR");

log.Step("Đăng tài khi chưa có chứng từ nguồn: chỉ được phát QR tạm (R-004)");

var tempResult = await UseAsync(sp => sp.GetRequiredService<RegisterTripHandler>()
    .HandleAsync(new RegisterTripCommand(
        SiteId: seed.SiteId,
        GateId: seed.GateId,
        WorkflowProfileId: seed.CaneProfileId,
        WorkflowProfileVersion: seed.CaneProfileVersion,
        ReferenceTripNo: null,
        PlanSource: null,
        CommodityType: null,
        OwnerOrCustomer: null,
        WarehouseCode: null,
        TractorPlate: "51C-777.77",
        TrailerPlate: null,
        DriverRef: "TX-9001",
        VehicleType: null,
        SugarcaneType: null,
        SugarcaneVariety: null,
        SugarcaneOwner: null,
        HarvestMethod: null,
        Station: null,
        CropSeason: null,
        CorrelationId: Guid.NewGuid().ToString("N"))));

log.Info($"Chuyến tạm {tempResult.TripNo}, loại QR {tempResult.QrType}.");
log.Expect(tempResult.QrType == QrTokenType.Temp, "Thiếu chứng từ nguồn thì chỉ phát QR tạm");

if (tempResult.QrToken is not null)
{
    var tempResolve = await UseAsync(sp => sp.GetRequiredService<ResolveQrHandler>()
        .HandleAsync(new ResolveQrCommand(
            tempResult.QrToken, Guid.Empty, seed.SiteId, CheckpointType.FirstWeight)));

    log.Detail($"Lý do chặn: {string.Join(" | ", tempResolve.Blockers)}");
    log.Expect(!tempResolve.AllowsOfficialWeighing, "QR tạm không đủ điều kiện cân chính thức");
}
else
{
    log.Detail("QR token null cho đăng tài tạm — bỏ qua bước resolve QR (QR tạm vẫn được phát).");
}

log.Step("Đăng tài chính thức với lệnh đốn từ FRM");

var register = await UseAsync(sp => sp.GetRequiredService<RegisterTripHandler>()
    .HandleAsync(new RegisterTripCommand(
        SiteId: seed.SiteId,
        GateId: seed.GateId,
        WorkflowProfileId: seed.CaneProfileId,
        WorkflowProfileVersion: seed.CaneProfileVersion,
        ReferenceTripNo: $"LD-{DateTime.UtcNow:yyyyMMddHHmmss}",
        PlanSource: "FRM",
        CommodityType: null,
        OwnerOrCustomer: null,
        WarehouseCode: null,
        TractorPlate: "51C-123.45",
        TrailerPlate: "51R-678.90",
        DriverRef: "TX-0007",
        VehicleType: VehicleType.Truck,
        SugarcaneType: null,
        SugarcaneVariety: null,
        SugarcaneOwner: null,
        HarvestMethod: null,
        Station: null,
        CropSeason: null,
        CorrelationId: Guid.NewGuid().ToString("N"))));

var tripId = register.TripId!.Value;
currentTripId = tripId;

log.Info($"Chuyến {register.TripNo}, loại QR {register.QrType}.");
log.Expect(register.QrType == QrTokenType.Trip, "Có chứng từ nguồn thì phát QR chuyến chính thức");

var resolve = await UseAsync(sp => sp.GetRequiredService<ResolveQrHandler>()
    .HandleAsync(new ResolveQrCommand(
        register.QrToken!, Guid.Empty, seed.SiteId, CheckpointType.FirstWeight)));

log.Expect(resolve.AllowsOfficialWeighing, "QR chuyến đủ điều kiện thao tác cân");

log.Section("Phần 2 — Vào cổng");

log.Step("Xin mở barie khi chưa xác thực danh tính xe: phải bị từ chối (R-039)");

identity.Confirmed = false;

var versionForDeniedGate = await CurrentVersionAsync();

var deniedGate = await UseAsync(sp => sp.GetRequiredService<EvaluateGateHandler>()
    .HandleAsync(new EvaluateGateCommand(
        tripId, seed.GateId, seed.LaneId, GateDirection.In,
        versionForDeniedGate,
        Guid.NewGuid().ToString("N"))));

log.Detail($"Lý do: {deniedGate.Decision.DenyReason.Replace(Environment.NewLine, "  ")}");
log.Expect(!deniedGate.Allowed, "QR hợp lệ nhưng chưa xác thực xe thì không mở barie");
log.Expect(barrier.IssuedCommands.Count == 0, "Không phát lệnh mở nào khi bị từ chối");

log.Step("Nhân viên tại làn xác nhận đúng xe, xin mở barie vào");

identity.Confirmed = true;

var versionForGateIn = await CurrentVersionAsync();

var gateIn = await UseAsync(sp => sp.GetRequiredService<EvaluateGateHandler>()
    .HandleAsync(new EvaluateGateCommand(
        tripId, seed.GateId, seed.LaneId, GateDirection.In,
        versionForGateIn,
        Guid.NewGuid().ToString("N"))));

log.Info($"Lệnh mở {gateIn.CommandId}, hết hạn {gateIn.CommandExpiresAt:HH:mm:ss}.");
log.Expect(gateIn.Allowed, "Đủ điều kiện thì cho mở barie");
log.Expect(barrier.IssuedCommands.Count == 1, "Đã gửi đúng một lệnh xuống bộ điều khiển");

var passage = await UseAsync(sp => sp.GetRequiredService<ConfirmVehiclePassageHandler>()
    .HandleAsync(new ConfirmVehiclePassageCommand(
        tripId, gateIn.CommandId!, "scenario://gate/passage-1", Guid.NewGuid().ToString("N"))));

log.Expect(passage.IsSuccess, "Xe qua cổng được xác nhận bằng bước riêng, không suy từ ACK mở barie");

log.Section("Phần 3 — Cân lần 1");

log.Step("Mở phiên cân trên bàn cân 1");

var versionForSession1 = await CurrentVersionAsync();

var session1 = await UseAsync(sp => sp.GetRequiredService<OpenWeighSessionHandler>()
    .HandleAsync(new OpenWeighSessionCommand(
        TripId: tripId,
        ExpectedTripVersion: versionForSession1,
        WeighbridgeId: seed.BridgeAId,
        Checkpoint: CheckpointType.FirstWeight,
        SegmentIndex: 0,
        CorrelationId: Guid.NewGuid().ToString("N"))));

log.Info($"Phiên {session1.SessionId}, vai trò {session1.Role}, cửa sổ ổn định " +
         $"{session1.StabilityWindow?.TotalSeconds:F1}s.");
log.Expect(session1.IsSuccess, "Mở được phiên cân");
log.Expect(session1.Role == WeightRole.Gross, "Hồ sơ nhập mía: lượt đầu là cân cả xe có hàng");

log.Step("Chuyến KHÁC xin mở phiên trên cùng bàn cân khi phiên đang mở (B-003)");

// Đăng tài chuyến thứ hai để kiểm tra B-003: một bàn cân chỉ có một phiên đang sở hữu.
var otherTrip = await UseAsync(sp => sp.GetRequiredService<RegisterTripHandler>()
    .HandleAsync(new RegisterTripCommand(
        SiteId: seed.SiteId,
        GateId: seed.GateId,
        WorkflowProfileId: seed.CaneProfileId,
        WorkflowProfileVersion: seed.CaneProfileVersion,
        ReferenceTripNo: "LD-B003-TEST",
        PlanSource: "FRM",
        CommodityType: null,
        OwnerOrCustomer: null,
        WarehouseCode: null,
        TractorPlate: "51C-888.88",
        TrailerPlate: null,
        DriverRef: "TX-B003",
        VehicleType: VehicleType.Truck,
        SugarcaneType: null,
        SugarcaneVariety: null,
        SugarcaneOwner: null,
        HarvestMethod: null,
        Station: null,
        CropSeason: null,
        CorrelationId: Guid.NewGuid().ToString("N"))));

var versionForDuplicate = await UseAsync(sp => TripVersionAsync(sp, otherTrip.TripId!.Value));

var session1Duplicate = await UseAsync(sp => sp.GetRequiredService<OpenWeighSessionHandler>()
    .HandleAsync(new OpenWeighSessionCommand(
        TripId: otherTrip.TripId!.Value,
        ExpectedTripVersion: versionForDuplicate,
        WeighbridgeId: seed.BridgeAId,
        Checkpoint: CheckpointType.FirstWeight,
        SegmentIndex: 0,
        CorrelationId: Guid.NewGuid().ToString("N"))));

log.Detail($"Lý do: {session1Duplicate.FailureReason}");
log.Expect(!session1Duplicate.IsSuccess, "Một bàn cân chỉ có một phiên đang sở hữu — chuyến khác bị chặn");

log.Step("Chốt cân khi xe vừa lên bàn, tín hiệu chưa ổn định đủ 5 giây");

scale.CurrentKg = 30_050.000m;
scale.StableDuration = TimeSpan.FromSeconds(1.8);
scale.ObservedDeltaKg = 3.5m;

var earlyCommit = await UseAsync(sp => sp.GetRequiredService<EvaluateAndCommitWeightHandler>()
    .HandleAsync(new EvaluateAndCommitWeightCommand(
        session1.SessionId!.Value, seed.LaneId,
        Guid.NewGuid().ToString("N"), $"{session1.SessionId}:first")));

foreach (var check in earlyCommit.Decision.Failed)
    log.Detail($"{check.Code}  {check.Name}: {check.Detail}");

log.Expect(!earlyCommit.IsCommitted, "Chưa đủ cửa sổ ổn định thì không chốt cân");
log.Expect(earlyCommit.Decision.Failed.Any(c => c.Code == "B-001.6"),
    "Điều kiện bị chặn đúng là nhóm ổn định trọng lượng");

log.Step("Tín hiệu ổn định liên tục, chốt cân cả xe có hàng");

scale.StableDuration = TimeSpan.FromSeconds(5.4);
scale.ObservedDeltaKg = 0.3m;

var grossCommit = await UseAsync(sp => sp.GetRequiredService<EvaluateAndCommitWeightHandler>()
    .HandleAsync(new EvaluateAndCommitWeightCommand(
        session1.SessionId!.Value, seed.LaneId,
        Guid.NewGuid().ToString("N"), $"{session1.SessionId}:first")));

log.Info($"Đã chốt {grossCommit.NormalizedKg:N3} kg.");

foreach (var warning in grossCommit.Warnings)
    log.Detail($"Cảnh báo: {warning}");

log.Expect(grossCommit.IsCommitted, "Đủ 8 nhóm điều kiện thì chốt được cân");
log.Expect(grossCommit.NormalizedKg == 30_050.000m, "Số cân lưu đúng giá trị đầu cân");

log.Section("Phần 4 — Tạp chất và chữ đường");

log.Step("Cân lần 2 khi chưa đo tạp chất: phải bị chặn (S02 tr.21)");

var versionForSession2 = await CurrentVersionAsync();

var session2Early = await UseAsync(sp => sp.GetRequiredService<OpenWeighSessionHandler>()
    .HandleAsync(new OpenWeighSessionCommand(
        TripId: tripId,
        ExpectedTripVersion: versionForSession2,
        WeighbridgeId: seed.BridgeAId,
        Checkpoint: CheckpointType.SecondWeight,
        SegmentIndex: 1,
        CorrelationId: Guid.NewGuid().ToString("N"))));

scale.CurrentKg = 12_000.000m;
scale.StableDuration = TimeSpan.FromSeconds(6.1);
scale.ObservedDeltaKg = 0.2m;

var blockedTare = await UseAsync(sp => sp.GetRequiredService<EvaluateAndCommitWeightHandler>()
    .HandleAsync(new EvaluateAndCommitWeightCommand(
        session2Early.SessionId!.Value, seed.LaneId,
        Guid.NewGuid().ToString("N"), $"{session2Early.SessionId}:second")));

foreach (var check in blockedTare.Decision.Failed)
    log.Detail($"{check.Code}  {check.Name}: {check.Detail}");

log.Expect(!blockedTare.IsCommitted, "Mía chưa đo tạp chất thì chặn cân lần 2");
log.Expect(blockedTare.Decision.Failed.Any(c => c.Code == "B-001.7"),
    "Bị chặn ở nhóm điều kiện nghiệp vụ trước lượt cân");

log.Step("Ghi kết quả cân tạp chất: mẫu 20 kg còn 17 kg");

var impurity = await UseAsync(sp => sp.GetRequiredService<RecordImpuritySampleHandler>()
    .HandleAsync(new RecordImpuritySampleCommand(
        TripId: tripId,
        SampleBeforeKg: 20.000m,
        SampleAfterKg: 17.000m,
        Source: "DEVICE",
        CorrelationId: Guid.NewGuid().ToString("N"))));

log.Info($"Tạp chất {impurity.ImpurityPercent:N4} %.");
log.Expect(impurity.IsSuccess, "Ghi được kết quả tạp chất");
log.Expect(impurity.ImpurityPercent == 15m, "Công thức (trước - sau) / trước × 100 cho 15%");
log.Expect(!impurity.BelowMinimumSampleWarning, "Mẫu 20 kg trên ngưỡng cảnh báo 10 kg");

log.Step("Nguồn FRM báo chữ đường đang chờ: chỉ cảnh báo, không chặn cân (R-014)");

var ccs = await UseAsync(sp => sp.GetRequiredService<ReceiveCcsResultHandler>()
    .HandleAsync(new ReceiveCcsResultCommand(
        TripId: tripId,
        CcsValue: null,
        Source: "FRM",
        SourceReportsPending: true,
        CorrelationId: Guid.NewGuid().ToString("N"))));

log.Expect(ccs.Status == QualityResultStatus.Pending, "Chữ đường ở trạng thái Pending, không phải lỗi");

log.Section("Phần 5 — Cân lần 2 và đối soát");

log.Step("Chốt cân bì xe sau khi đã đo tạp chất");

var tareCommit = await UseAsync(sp => sp.GetRequiredService<EvaluateAndCommitWeightHandler>()
    .HandleAsync(new EvaluateAndCommitWeightCommand(
        session2Early.SessionId!.Value, seed.LaneId,
        Guid.NewGuid().ToString("N"), $"{session2Early.SessionId}:second")));

log.Info($"Đã chốt bì xe {tareCommit.NormalizedKg:N3} kg.");

foreach (var warning in tareCommit.Warnings)
    log.Detail($"Cảnh báo: {warning}");

log.Expect(tareCommit.IsCommitted, "Đã đo tạp chất thì cân lần 2 đi qua");
log.Expect(tareCommit.Warnings.Any(w => w.Contains("Chữ đường")),
    "Vẫn cảnh báo chữ đường chưa có, nhưng không chặn");

var lifecycleAfterWeighing = await UseAsync(sp => TripLifecycleAsync(sp, tripId));
log.Expect(lifecycleAfterWeighing == TripLifecycle.WeighingCompleted,
    "Đủ lượt cân thì chuyển WEIGHING_COMPLETED, chưa mặc định đạt đối soát");

log.Step("Xin ra cổng trước khi đối soát: phải bị từ chối (R-006)");

var versionForEarlyGateOut = await CurrentVersionAsync();

var earlyGateOut = await UseAsync(sp => sp.GetRequiredService<EvaluateGateHandler>()
    .HandleAsync(new EvaluateGateCommand(
        tripId, seed.GateId, seed.LaneId, GateDirection.Out,
        versionForEarlyGateOut,
        Guid.NewGuid().ToString("N"))));

log.Detail($"Lý do: {earlyGateOut.Decision.DenyReason.Replace(Environment.NewLine, "  ")}");
log.Expect(!earlyGateOut.Allowed, "Chưa đủ checkpoint giai đoạn ra thì không mở cổng");

log.Step("Chạy đối soát chuyến");

var reconcile = await UseAsync(sp => sp.GetRequiredService<ReconcileTripHandler>()
    .HandleAsync(new ReconcileTripCommand(tripId, Guid.NewGuid().ToString("N"))));

log.Info($"Net cân xe {reconcile.VehicleNetKg:N3} kg (chênh lệch cân xe, không phải sản phẩm tinh).");

foreach (var check in reconcile.Checks)
    log.Detail($"{check.CheckType,-28} {check.Verdict,-13} {check.Detail}");

log.Expect(reconcile.VehicleNetKg == 18_050.000m, "Nhập mía: net = lần 1 trừ lần 2");
log.Expect(reconcile.IsPassed, "Hồ sơ mía không áp dụng IoT và đếm bao nên các phép đó NotApplicable");

log.Section("Phần 6 — Tích hợp và ra cổng");

log.Step("Đưa kết quả vào sổ thông điệp khi hợp đồng API chưa xác minh (D-02)");

var submit = await UseAsync(sp => sp.GetRequiredService<SubmitTripResultHandler>()
    .HandleAsync(new SubmitTripResultCommand(
        TripId: tripId,
        Destination: "FRM",
        Operation: "WEIGH_RESULT",
        ShipmentNo: null,
        CorrelationId: Guid.NewGuid().ToString("N"))));

log.Info($"Business key: {submit.BusinessKey}");
log.Info($"Trạng thái: {submit.Status}. {submit.Note}");
log.Expect(submit.Status == IntegrationMessageStatus.Held,
    "Chưa có hợp đồng đã kiểm chứng thì giữ HELD, không gửi mù");

var lifecycleAfterSubmit = await UseAsync(sp => TripLifecycleAsync(sp, tripId));
log.Expect(lifecycleAfterSubmit != TripLifecycle.Posted,
    "Chưa được hệ thống đích xác nhận thì chuyến không chuyển POSTED");

log.Step("Gửi lại cùng nội dung: không được tạo thông điệp trùng (R-024)");

var submitAgain = await UseAsync(sp => sp.GetRequiredService<SubmitTripResultHandler>()
    .HandleAsync(new SubmitTripResultCommand(
        TripId: tripId,
        Destination: "FRM",
        Operation: "WEIGH_RESULT",
        ShipmentNo: null,
        CorrelationId: Guid.NewGuid().ToString("N"))));

log.Detail(submitAgain.Note ?? string.Empty);

var messageCount = await UseAsync(sp => sp.GetRequiredService<WeighingDbContext>()
    .IntegrationMessages.CountAsync(m => m.TripId == tripId));

log.Expect(messageCount == 1, "Cùng business key chỉ có một bản ghi trong sổ thông điệp");

log.Step("Xin ra cổng sau khi đã đạt đối soát");

var versionForGateOut = await CurrentVersionAsync();

var gateOut = await UseAsync(sp => sp.GetRequiredService<EvaluateGateHandler>()
    .HandleAsync(new EvaluateGateCommand(
        tripId, seed.GateId, seed.LaneId, GateDirection.Out,
        versionForGateOut,
        Guid.NewGuid().ToString("N"))));

log.Expect(gateOut.Allowed, "Đạt đối soát thì đủ điều kiện ra cổng");

// ---------------------------------------------------------------------------
// Kiểm chứng trên cơ sở dữ liệu
// ---------------------------------------------------------------------------
log.Section("Phần 7 — Kiểm chứng dữ liệu đã ghi");

log.Step("Đọc lại chuyến từ cơ sở dữ liệu qua read model");

await using (var scope = provider.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WeighingDbContext>();

    var weighEvents = await db.WeighEvents
        .Where(e => e.TripId == tripId)
        .OrderBy(e => e.MeasuredAt)
        .ToListAsync();

    foreach (var weighEvent in weighEvents)
    {
        log.Detail($"{weighEvent.Role,-6} rev{weighEvent.Revision} " +
                   $"{weighEvent.NormalizedKg,12:N3} kg  nguồn {weighEvent.Source}  " +
                   $"ổn định {weighEvent.StableWindow.TotalSeconds:F1}s  " +
                   $"bằng chứng {weighEvent.EvidenceRefs.Count}");
    }

    log.Expect(weighEvents.Count == 2, "Đúng hai lượt cân được ghi");
    log.Expect(weighEvents.All(e => e.IsEffective), "Không có lượt cân nào bị thay thế");
    log.Expect(weighEvents.All(e => e.EvidenceRefs.Count > 0),
        "Mỗi lượt cân có bằng chứng đã lưu (B-001.8)");
    log.Expect(weighEvents.All(e => e.Source == WeighSource.Auto),
        "Cả hai lượt là nguồn AUTO, không có can thiệp tay");

    var auditActions = await db.AuditLogEntries
        .Where(a => a.TripId == tripId)
        .OrderBy(a => a.OccurredAt)
        .Select(a => a.ActionType)
        .ToListAsync();

    log.Info($"Audit: {string.Join(" -> ", auditActions)}");

    log.Expect(auditActions.Contains("WEIGH_COMMIT_BLOCKED"),
        "Lần chốt cân bị chặn cũng được ghi audit");
    log.Expect(auditActions.Contains("GATE_DENIED"), "Lần từ chối mở barie được ghi audit");
    log.Expect(auditActions.Count(a => a == "WEIGH_COMMITTED") == 2,
        "Hai lần chốt cân thành công đều có audit");

    var gateCommands = await db.GateCommands.Where(c => c.TripId == tripId).ToListAsync();

    log.Expect(gateCommands.Count == 2, "Hai lệnh mở barie: một vào, một ra");
    log.Expect(gateCommands.Count(c => c.VehicleConfirmedPassed) == 1,
        "Chỉ lượt vào đã được xác nhận xe qua cổng");

    var samples = await db.QualitySamples.Where(s => s.TripId == tripId).ToListAsync();

    log.Expect(samples.Count == 2, "Có bản ghi tạp chất và bản ghi chữ đường");
    log.Expect(samples.Single(s => s.Type == QualitySampleType.Ccs).CcsValue is null,
        "Chữ đường đang chờ thì không lưu giá trị, không ghi 0");

    var reconciliation = await db.Reconciliations
        .Include(r => r.Checks)
        .FirstAsync(r => r.TripId == tripId && r.IsEffective);

    log.Expect(reconciliation.Checks.Count == 4, "Lưu đủ bốn phép so sánh độc lập");
    log.Expect(reconciliation.Checks.All(c => c.TolerancePolicyId is null),
        "Chưa có chính sách dung sai đã duyệt nên không gắn mã chính sách (D-06)");
}

log.Step("Đọc tổng quan nhà máy qua read model của UI-01");

await using (var scope = provider.CreateAsyncScope())
{
    var dashboard = scope.ServiceProvider.GetRequiredService<IDashboardQueries>();
    var overview = await dashboard.GetOverviewAsync(seed.SiteId);

    log.Info($"Chờ cân {overview.WaitingForWeighing}  ·  trong nhà máy {overview.InsidePlant}  ·  " +
             $"cân 1 chưa cân 2 {overview.FirstWeightOnly}  ·  PAUSED {overview.PausedTrips}");
    log.Info($"Tổng net cân xe {overview.TotalVehicleNetKgInRange:N0} kg  ·  " +
             $"tích hợp cần xử lý {overview.IntegrationAttention}  ·  " +
             $"can thiệp tay {overview.ManualInterventions}");

    log.Detail($"Vòng đời: tạm {overview.Lifecycle.TempRegistered}, " +
               $"đã đăng tài {overview.Lifecycle.Registered}, " +
               $"đang trong nhà máy {overview.Lifecycle.InProgress}, " +
               $"đủ lượt cân {overview.Lifecycle.WeighingCompleted}, " +
               $"đã đối soát {overview.Lifecycle.Reconciled}");

    log.Expect(overview.IntegrationAttention >= 1,
        "Thông điệp HELD được đếm vào mục tích hợp cần xử lý");
    log.Expect(overview.Lifecycle.TempRegistered >= 1, "Chuyến phiếu tạm nằm ở chặng đầu luồng");
}

log.Info($"Bằng chứng và payload đã lưu tại: {artifactRoot}");

log.Summary();
return log.Failures == 0 ? 0 : 1;

// ---------------------------------------------------------------------------
static async Task<int> TripVersionAsync(IServiceProvider sp, Guid tripId)
{
    var db = sp.GetRequiredService<WeighingDbContext>();
    return await db.Trips.Where(t => t.Id == tripId).Select(t => t.Version).FirstAsync();
}

static async Task<TripLifecycle> TripLifecycleAsync(IServiceProvider sp, Guid tripId)
{
    var db = sp.GetRequiredService<WeighingDbContext>();
    return await db.Trips.Where(t => t.Id == tripId).Select(t => t.Lifecycle).FirstAsync();
}
