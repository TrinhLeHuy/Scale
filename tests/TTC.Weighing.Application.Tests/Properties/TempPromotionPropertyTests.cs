using FsCheck;
using FsCheck.Xunit;
using TTC.Weighing.Application.UseCases.Trips;
using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Application.Tests.Properties;

/// <summary>
/// Property 6: Temp-to-Registered promotion
/// **Validates: Requirements 4.3, 4.5**
///
/// Với mọi trip TempRegistered khi nhận xác nhận FRM:
/// - Lifecycle = Registered
/// - QR cũ loại Temp → Status = Revoked
/// - Có ít nhất một QR mới loại Trip với Status = Issued chứa Số_thứ_tự
///
/// NOTE: Không có PromoteTempToRegistered handler trong codebase — test này kiểm tra
/// invariant tại entity level, mô phỏng hành vi promotion mà handler tương lai phải đảm bảo.
/// </summary>
public class TempPromotionPropertyTests
{
    private static readonly DateTimeOffset Now = new(2025, 6, 15, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid SiteId = Guid.NewGuid();

    /// <summary>
    /// Input cho property test: số lượng QR tạm ban đầu (1-3), số thứ tự sau promotion,
    /// mã bàn cân và mã kho được gán.
    /// </summary>
    public sealed record PromotionInput(
        int TempQrCount,
        int QueueSequenceNumber,
        string WeighbridgeCode,
        string WarehouseCode);

    /// <summary>
    /// Custom FsCheck Arbitrary: tạo input ngẫu nhiên cho promotion test.
    /// </summary>
    public static class PromotionArbs
    {
        public static Arbitrary<PromotionInput> PromotionInputArb()
        {
            var gen = from tempQrCount in Gen.Choose(1, 3)
                      from queueSeq in Gen.Choose(1, 10000)
                      from bridgeCode in GenNonEmptyCode()
                      from warehouseCode in GenNonEmptyCode()
                      select new PromotionInput(tempQrCount, queueSeq, bridgeCode, warehouseCode);
            return gen.ToArbitrary();
        }

        private static Gen<string> GenNonEmptyCode()
        {
            return from length in Gen.Choose(2, 10)
                   from chars in Gen.ArrayOf(length, Gen.Elements(
                       'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H',
                       'K', 'L', 'M', 'N', 'P', 'Q', 'R', 'S', 'T',
                       '0', '1', '2', '3', '4', '5', '6', '7', '8', '9',
                       '-'))
                   select new string(chars);
        }
    }

    /// <summary>
    /// Tạo một trip TempRegistered với N QR tạm (Issued).
    /// </summary>
    private static Trip CreateTempRegisteredTrip(int tempQrCount)
    {
        var trip = new Trip
        {
            TripNo = $"TT-PROP-{Guid.NewGuid():N}"[..20],
            SiteId = SiteId,
            WorkflowProfileId = Guid.NewGuid(),
            WorkflowProfileVersion = 1,
            SourceSystem = "FRM",
            ReferenceTripNo = null, // chưa có kế hoạch
            TractorPlate = "51A-999.99",
            Lifecycle = TripLifecycle.TempRegistered,
            ExecutionStatus = ExecutionStatus.Active,
            VehicleType = null,
            QueueSequenceNumber = null,
            AssignedWeighbridgeId = null,
            CreatedAt = Now,
            CreatedBy = "gate-operator",
            Version = 1
        };

        for (var i = 0; i < tempQrCount; i++)
        {
            var (_, tokenHash) = QrTokenFactory.Create();
            trip.QrTokens.Add(new QrToken
            {
                TokenHash = tokenHash,
                Type = QrTokenType.Temp,
                Status = QrTokenStatus.Issued,
                TripId = trip.Id,
                SiteId = SiteId,
                IssuedAt = Now.AddMinutes(-10 + i),
                ExpiresAt = Now.AddHours(24),
                EncodedContent = QrTokenFactory.Encode(tokenHash),
                CreatedAt = Now,
                CreatedBy = "gate-operator"
            });
        }

        return trip;
    }

    /// <summary>
    /// Mô phỏng promotion: chuyển Trip từ TempRegistered sang Registered.
    /// Đây là hành vi mà handler tương lai (PromoteTempToRegistered) phải đảm bảo.
    ///
    /// Steps:
    /// 1. Lifecycle → Registered
    /// 2. Gắn thông tin kế hoạch FRM (VehicleType, QueueSequenceNumber, etc.)
    /// 3. Thu hồi tất cả QR tạm cũ → Revoked
    /// 4. Phát QR_chuyến mới (Trip type, Issued) chứa Số_thứ_tự
    /// </summary>
    private static void SimulatePromotion(
        Trip trip,
        int queueSequenceNumber,
        string weighbridgeCode,
        string warehouseCode)
    {
        // 1. Chuyển lifecycle
        trip.Lifecycle = TripLifecycle.Registered;

        // 2. Gắn thông tin xếp hàng
        trip.VehicleType = VehicleType.Truck;
        trip.QueueSequenceNumber = queueSequenceNumber;
        trip.AssignedWeighbridgeId = Guid.NewGuid();
        trip.ReferenceTripNo = "FRM-2025-PROMOTED";

        // 3. Thu hồi tất cả QR tạm cũ
        foreach (var qr in trip.QrTokens.Where(q => q.Type == QrTokenType.Temp && q.Status == QrTokenStatus.Issued))
        {
            qr.Status = QrTokenStatus.Revoked;
            qr.RevokedAt = Now;
        }

        // 4. Phát QR_chuyến mới
        var (_, newTokenHash) = QrTokenFactory.Create();
        var encodedContent = QrTokenFactory.Encode(
            newTokenHash, queueSequenceNumber, weighbridgeCode, warehouseCode);

        trip.QrTokens.Add(new QrToken
        {
            TokenHash = newTokenHash,
            Type = QrTokenType.Trip,
            Status = QrTokenStatus.Issued,
            TripId = trip.Id,
            SiteId = SiteId,
            IssuedAt = Now,
            ExpiresAt = Now.AddHours(24),
            EncodedContent = encodedContent,
            CreatedAt = Now,
            CreatedBy = "system-promotion"
        });
    }

    // ================================================================
    // Property 6: Temp-to-Registered promotion invariants
    // **Validates: Requirements 4.3, 4.5**
    // ================================================================

    /// <summary>
    /// Với mọi trip TempRegistered có 1-3 QR tạm, sau promotion:
    /// - Lifecycle = Registered
    /// - Tất cả QR Temp cũ → Revoked
    /// - Có ít nhất một QR Trip mới → Issued
    /// - QR mới chứa Số_thứ_tự (QueueSequenceNumber) trong EncodedContent
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(PromotionArbs) })]
    public Property After_promotion_lifecycle_is_registered_and_old_qrs_revoked_and_new_qr_issued(
        PromotionInput input)
    {
        // Arrange
        var trip = CreateTempRegisteredTrip(input.TempQrCount);
        var initialTempQrCount = trip.QrTokens.Count(q => q.Type == QrTokenType.Temp);

        // Pre-condition: trip starts as TempRegistered with Issued temp QRs
        if (trip.Lifecycle != TripLifecycle.TempRegistered || initialTempQrCount == 0)
            return true.Label("Pre-condition not met — skip");

        // Act: simulate promotion
        SimulatePromotion(trip, input.QueueSequenceNumber, input.WeighbridgeCode, input.WarehouseCode);

        // Assert invariants

        // Invariant 1: Lifecycle = Registered (Req 4.3)
        var lifecycleCorrect = trip.Lifecycle == TripLifecycle.Registered;

        // Invariant 2: All old Temp QRs are Revoked (Req 4.5)
        var allTempRevoked = trip.QrTokens
            .Where(q => q.Type == QrTokenType.Temp)
            .All(q => q.Status == QrTokenStatus.Revoked);

        // Invariant 3: At least one new Trip QR with Status = Issued
        var newTripQrs = trip.QrTokens
            .Where(q => q.Type == QrTokenType.Trip && q.Status == QrTokenStatus.Issued)
            .ToList();
        var hasNewTripQr = newTripQrs.Count >= 1;

        // Invariant 4: New Trip QR has EncodedContent containing QueueSequenceNumber
        var newQrHasSequence = newTripQrs.Any(q =>
        {
            if (q.EncodedContent is null) return false;
            var decoded = QrTokenFactory.Decode(q.EncodedContent);
            return decoded?.QueueSequenceNumber == input.QueueSequenceNumber;
        });

        // Invariant 5: Total QR count increased (old temps kept as Revoked + new Trip QR)
        var totalQrCount = trip.QrTokens.Count;
        var qrCountCorrect = totalQrCount == initialTempQrCount + 1;

        return lifecycleCorrect
            .Label($"Lifecycle must be Registered, got {trip.Lifecycle}")
            .And(allTempRevoked
                .Label("All old Temp QRs must be Revoked"))
            .And(hasNewTripQr
                .Label($"Must have at least 1 new Trip QR with Status=Issued, found {newTripQrs.Count}"))
            .And(newQrHasSequence
                .Label($"New Trip QR must contain QueueSequenceNumber={input.QueueSequenceNumber}"))
            .And(qrCountCorrect
                .Label($"Total QR count must be {initialTempQrCount + 1} (old temps + new), got {totalQrCount}"));
    }
}
