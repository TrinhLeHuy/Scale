using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Application.UseCases.Trips;

public sealed record RegisterTripCommand(
    Guid SiteId,
    Guid GateId,
    Guid WorkflowProfileId,
    int WorkflowProfileVersion,
    // Thông tin kế hoạch (từ CheckPlanHandler)
    string? ReferenceTripNo,
    string? PlanSource,
    string? CommodityType,
    string? OwnerOrCustomer,
    string? WarehouseCode,
    // Thông tin xe
    string? TractorPlate,
    string? TrailerPlate,
    string? DriverRef,
    VehicleType? VehicleType,
    // Thông tin mía mở rộng (từ FRM)
    string? SugarcaneType,
    string? SugarcaneVariety,
    string? SugarcaneOwner,
    string? HarvestMethod,
    string? Station,
    string? CropSeason,
    // Thông tin lệnh đốn FRM (lưu để push DigiScale sau)
    string? FrmLenhDonId = null,
    string? FrmMaLenhDon = null,
    string? FrmLenhDonBu = null,
    string? FrmLoaiXeCode = null,
    // Điều khiển
    string? CorrelationId = null,
    bool IssueTemporaryQrOnly = false,
    string? OrganizationUnitCode = null);

public sealed record RegisterTripResult(
    bool IsSuccess,
    Guid? TripId,
    string? TripNo,
    int? QueueSequenceNumber,
    string? QrToken,
    QrTokenType? QrType,
    Guid? AssignedWeighbridgeId,
    Guid? AssignedWarehouseId,
    string? FailureReason);

/// <summary>
/// R-005 — Đăng tài và phát QR. Thiếu dữ liệu kế hoạch thì chỉ phát QR tạm,
/// và QR tạm KHÔNG đủ điều kiện cân chính thức (R-004).
/// Req 2.1, 2.4, 2.5, 3.1–3.5, 4.1–4.2, 10.2.
/// </summary>
public sealed class RegisterTripHandler(
    ITripRepository trips,
    IQrTokenRepository qrTokens,
    IWorkflowProfileRepository profiles,
    ITripNumberGenerator tripNumbers,
    IQueueSequenceGenerator queueSequence,
    QueueTripHandler queueTrip,
    IWeighbridgeRepository weighbridges,
    ICameraService camera,
    IUnitOfWork uow,
    IClock clock,
    ICurrentUser user,
    IAuditLogger audit)
{
    public const string RequiredPermission = "trip.register";

    /// <summary>Profile code mía nhập — bắt buộc VehicleType (Req 2.1).</summary>
    internal const string InboundSugarcaneProfileCode = "CANE_IN";

    public async Task<RegisterTripResult> HandleAsync(
        RegisterTripCommand cmd, CancellationToken ct = default)
    {
        // 1. Kiểm tra quyền
        if (!user.HasPermission(RequiredPermission, cmd.SiteId))
            return Fail("Không có quyền đăng tài tại nhà máy này.");

        // 2. Validate hồ sơ nghiệp vụ
        var profile = await profiles.GetEffectiveAsync(
            cmd.WorkflowProfileId, cmd.WorkflowProfileVersion, ct);

        if (profile is null)
            return Fail("Không tìm thấy hồ sơ nghiệp vụ.");

        if (!profile.IsApprovedForProduction)
            return Fail($"Hồ sơ {profile.Code} chưa được duyệt cho vận hành.");

        if (!profile.IsEffectiveAt(clock.UtcNow))
            return Fail($"Hồ sơ {profile.Code} không còn hiệu lực.");

        // 3. Validate VehicleType bắt buộc cho mía nhập (Req 2.1, 2.4)
        var isInboundSugarcane = IsInboundSugarcaneProfile(profile);
        if (isInboundSugarcane && cmd.VehicleType is null)
            return Fail("Loại xe (VehicleType) bắt buộc cho hồ sơ mía nhập.");

        // 4. Xác định luồng
        var hasSourceReference = !string.IsNullOrWhiteSpace(cmd.ReferenceTripNo);
        var issueTemp = cmd.IssueTemporaryQrOnly || !hasSourceReference;

        // 5. Sinh TripNo
        var tripNo = await tripNumbers.NextAsync(cmd.SiteId, profile.Code, ct);

        // 6. Xếp hàng cho luồng Registered
        int? queueSeqNumber = null;
        Guid? assignedWeighbridgeId = null;
        Guid? assignedWarehouseId = null;
        string? weighbridgeCode = null;

        var trip = new Trip
        {
            TripNo = tripNo,
            SiteId = cmd.SiteId,
            OrganizationUnitCode = cmd.OrganizationUnitCode,
            WorkflowProfileId = profile.Id,
            WorkflowProfileVersion = profile.ProfileVersion,
            SourceSystem = profile.SourceSystem,
            ReferenceTripNo = cmd.ReferenceTripNo,
            TractorPlate = cmd.TractorPlate,
            TrailerPlate = cmd.TrailerPlate,
            DriverRef = cmd.DriverRef,
            Lifecycle = issueTemp ? TripLifecycle.TempRegistered : TripLifecycle.Registered,
            ExecutionStatus = ExecutionStatus.Active,
            // Trường mới
            GateId = cmd.GateId,
            VehicleType = cmd.VehicleType,
            SugarcaneType = cmd.SugarcaneType ?? cmd.CommodityType,
            SugarcaneVariety = cmd.SugarcaneVariety,
            SugarcaneOwner = cmd.SugarcaneOwner,
            HarvestMethod = cmd.HarvestMethod,
            Station = cmd.Station,
            CropSeason = cmd.CropSeason,
            FrmLenhDonId = cmd.FrmLenhDonId,
            FrmMaLenhDon = cmd.FrmMaLenhDon,
            FrmLenhDonBu = cmd.FrmLenhDonBu,
            FrmLoaiXeCode = cmd.FrmLoaiXeCode,
            Version = 1,
            CreatedAt = clock.UtcNow,
            CreatedBy = user.UserId
        };

        if (!issueTemp && cmd.VehicleType is not null)
        {
            // Gọi IQueueSequenceGenerator → số thứ tự (Req 2.2, 2.3)
            queueSeqNumber = await queueSequence.NextAsync(
                cmd.SiteId, cmd.VehicleType.Value, ct);

            trip.QueueSequenceNumber = queueSeqNumber;

            // Gọi QueueTripHandler → gán bàn cân, kho (Req 9.1, 9.2)
            var queueResult = await queueTrip.HandleAsync(
                new QueueTripCommand(trip.Id, cmd.SiteId, cmd.VehicleType.Value, cmd.WarehouseCode), ct);

            if (queueResult.IsSuccess)
            {
                assignedWeighbridgeId = queueResult.AssignedWeighbridgeId;
                assignedWarehouseId = queueResult.AssignedWarehouseId;
                trip.AssignedWeighbridgeId = assignedWeighbridgeId;
                trip.AssignedWarehouseId = assignedWarehouseId;

                // Lấy mã bàn cân để mã hóa vào QR
                if (assignedWeighbridgeId is not null)
                {
                    var bridge = await weighbridges.GetByIdAsync(assignedWeighbridgeId.Value, ct);
                    weighbridgeCode = bridge?.Code;
                }
            }
        }

        // 7. Tạo QrToken
        var (rawToken, tokenHash) = QrTokenFactory.Create();

        // Mã hóa nội dung QR (Req 2.5, 8.2)
        string? encodedContent;
        if (!issueTemp)
        {
            encodedContent = QrTokenFactory.Encode(
                tokenHash, queueSeqNumber, weighbridgeCode, cmd.WarehouseCode);
        }
        else
        {
            encodedContent = QrTokenFactory.Encode(tokenHash);
        }

        var token = new QrToken
        {
            TokenHash = tokenHash,
            Type = issueTemp ? QrTokenType.Temp : QrTokenType.Trip,
            Status = QrTokenStatus.Issued,
            TripId = trip.Id,
            SiteId = cmd.SiteId,
            IssuedAt = clock.UtcNow,
            ExpiresAt = clock.UtcNow.AddHours(24),
            EncodedContent = encodedContent,
            CreatedAt = clock.UtcNow,
            CreatedBy = user.UserId
        };

        trip.QrTokens.Add(token);

        await trips.AddAsync(trip, ct);
        await qrTokens.AddAsync(token, ct);

        // 8. Chụp ảnh camera vào — không chặn nếu lỗi (Req 10.2, 10.3)
        _ = Task.Run(async () =>
        {
            try
            {
                await camera.CaptureSnapshotAsync(cmd.GateId, "In", ct);
            }
            catch
            {
                // Camera offline — không chặn đăng tài
            }
        }, CancellationToken.None);

        // 9. Audit log
        await audit.LogAsync("TRIP_REGISTERED", trip.Id, user.UserId, null,
            $"{{\"tripNo\":\"{tripNo}\",\"profile\":\"{profile.Code}\",\"qrType\":\"{token.Type}\",\"vehicleType\":\"{cmd.VehicleType}\",\"queueSeq\":{queueSeqNumber?.ToString() ?? "null"}}}",
            null, cmd.CorrelationId, ct);

        // 10. SaveChanges
        await uow.SaveChangesAsync(ct);

        return new(true, trip.Id, tripNo, queueSeqNumber, rawToken, token.Type,
            assignedWeighbridgeId, assignedWarehouseId, null);
    }

    /// <summary>
    /// Xác định profile có phải mía nhập hay không.
    /// Mía nhập: profile code bắt đầu bằng CANE_IN hoặc direction = Inbound với SourceSystem FRM.
    /// </summary>
    internal static bool IsInboundSugarcaneProfile(WorkflowProfile profile)
        => profile.Code.StartsWith(InboundSugarcaneProfileCode, StringComparison.OrdinalIgnoreCase)
           || (profile.Direction == ProfileDirection.Inbound
               && string.Equals(profile.SourceSystem, "FRM", StringComparison.OrdinalIgnoreCase));

    private static RegisterTripResult Fail(string reason)
        => new(false, null, null, null, null, null, null, null, reason);
}

public sealed record ResolveQrCommand(
    string RawToken,
    Guid ReaderDeviceId,
    Guid SiteId,
    CheckpointType IntendedCheckpoint);

/// <summary>
/// Kết quả tra QR. Lookup thành công KHÔNG có nghĩa được chốt cân (03_Du_lieu mục 8).
/// </summary>
public sealed record ResolveQrResult(
    bool IsFound,
    Guid? TripId,
    int? TripVersion,
    string? TripNo,
    QrTokenType? QrType,
    TripLifecycle? Lifecycle,
    ExecutionStatus? ExecutionStatus,
    bool AllowsOfficialWeighing,
    IReadOnlyList<string> Blockers);

public sealed class ResolveQrHandler(
    IQrTokenRepository qrTokens,
    ITripRepository trips,
    IClock clock,
    ICurrentUser user)
{
    public const string RequiredPermission = "trip.qr.resolve";

    public async Task<ResolveQrResult> HandleAsync(ResolveQrCommand cmd, CancellationToken ct = default)
    {
        if (!user.HasPermission(RequiredPermission, cmd.SiteId))
            return NotFound(["Không có quyền tra QR tại nhà máy này."]);

        var hash = QrTokenFactory.Hash(cmd.RawToken);
        var token = await qrTokens.GetByHashAsync(hash, ct);

        if (token is null)
            return NotFound(["QR không tồn tại trong hệ thống."]);

        var blockers = new List<string>();

        if (!token.IsValidAt(clock.UtcNow))
            blockers.Add($"QR ở trạng thái {token.Status} hoặc đã hết hạn.");

        if (token.SiteId != cmd.SiteId)
            blockers.Add("QR không thuộc nhà máy hiện tại.");

        // QR tạm cho phép cân offline — không chặn ở đây.

        if (token.TripId is null)
            return new(true, null, null, null, token.Type, null, null, false,
                [.. blockers, "QR xe nội bộ chưa được gán chuyến đang hoạt động."]);

        var trip = await trips.GetByIdAsync(token.TripId.Value, ct);
        if (trip is null)
            return NotFound([.. blockers, "Không tìm thấy chuyến liên kết QR."]);

        if (trip.Lifecycle is TripLifecycle.Closed or TripLifecycle.Cancelled)
            blockers.Add($"Chuyến đã {trip.Lifecycle}.");

        if (trip.ExecutionStatus == ExecutionStatus.Paused)
            blockers.Add($"Chuyến đang PAUSED (mã sự cố {trip.PauseIssueCode ?? "không rõ"}).");

        return new(
            IsFound: true,
            TripId: trip.Id,
            TripVersion: trip.Version,
            TripNo: trip.TripNo,
            QrType: token.Type,
            Lifecycle: trip.Lifecycle,
            ExecutionStatus: trip.ExecutionStatus,
            AllowsOfficialWeighing: token.AllowsOfficialWeighing && blockers.Count == 0,
            Blockers: blockers);
    }

    private static ResolveQrResult NotFound(IReadOnlyList<string> blockers)
        => new(false, null, null, null, null, null, null, false, blockers);
}

/// <summary>Sinh và băm token QR. Chỉ lưu hash, không lưu token thô.
/// Encode/Decode cho EncodedContent: JSON Base64 chứa thông tin xếp hàng (Req 2.5, 8.2).
/// </summary>
public static class QrTokenFactory
{
    public static (string RawToken, string TokenHash) Create()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        return (raw, Hash(raw));
    }

    public static string Hash(string rawToken)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    /// <summary>
    /// Mã hóa nội dung QR_chuyến: token prefix 8 ký tự, số thứ tự, bàn cân, kho.
    /// Kết quả là JSON → Base64 URL-safe.
    /// </summary>
    public static string Encode(
        string tokenHash,
        int? queueSequenceNumber,
        string? weighbridgeCode,
        string? warehouseCode)
    {
        var payload = new Dictionary<string, object?>
        {
            ["t"] = TokenPrefix(tokenHash)
        };

        if (queueSequenceNumber is not null)
            payload["s"] = queueSequenceNumber.Value;

        if (weighbridgeCode is not null)
            payload["b"] = weighbridgeCode;

        if (warehouseCode is not null)
            payload["w"] = warehouseCode;

        return JsonToBase64(payload);
    }

    /// <summary>
    /// Mã hóa nội dung QR_tạm: chỉ chứa token prefix 8 ký tự.
    /// </summary>
    public static string Encode(string tokenHash)
    {
        var payload = new Dictionary<string, object?>
        {
            ["t"] = TokenPrefix(tokenHash)
        };

        return JsonToBase64(payload);
    }

    /// <summary>
    /// Giải mã nội dung QR từ Base64 → Dictionary chứa các trường.
    /// </summary>
    public static QrEncodedContent? Decode(string encodedContent)
    {
        try
        {
            var json = Encoding.UTF8.GetString(
                Convert.FromBase64String(ToStandardBase64(encodedContent)));

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tokenPrefix = root.TryGetProperty("t", out var t) ? t.GetString() : null;
            int? seq = root.TryGetProperty("s", out var s) ? s.GetInt32() : null;
            var bridge = root.TryGetProperty("b", out var b) ? b.GetString() : null;
            var warehouse = root.TryGetProperty("w", out var w) ? w.GetString() : null;

            return new QrEncodedContent(tokenPrefix, seq, bridge, warehouse);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>8 ký tự đầu của token hash, dùng để lookup nhanh.</summary>
    internal static string TokenPrefix(string tokenHash)
        => tokenHash.Length >= 8 ? tokenHash[..8] : tokenHash;

    private static string JsonToBase64(Dictionary<string, object?> payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string ToStandardBase64(string urlSafe)
    {
        var s = urlSafe.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return s;
    }
}

/// <summary>Nội dung giải mã từ QR EncodedContent.</summary>
public sealed record QrEncodedContent(
    string? TokenPrefix,
    int? QueueSequenceNumber,
    string? WeighbridgeCode,
    string? WarehouseCode);

/// <summary>Sinh số đăng tài tăng tại server (R-005).</summary>
public interface ITripNumberGenerator
{
    Task<string> NextAsync(Guid siteId, string profileCode, CancellationToken ct = default);
}

/// <summary>
/// Cấp số thứ tự hàng chờ tự tăng theo loại xe, tách riêng per-site per-vehicle-type per-operating-date.
/// Dùng cơ chế atomic tại database, reset hằng ngày. (Req 2.2, 2.3)
/// </summary>
public interface IQueueSequenceGenerator
{
    /// <summary>
    /// Cấp số thứ tự tiếp theo cho loại xe tại nhà máy trong ngày vận hành hiện tại.
    /// Đảm bảo không trùng lặp khi nhiều trạm đăng tài đồng thời.
    /// </summary>
    Task<int> NextAsync(Guid siteId, VehicleType vehicleType, CancellationToken ct = default);
}
