using FsCheck;
using FsCheck.Xunit;
using NSubstitute;
using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.UseCases.Trips;
using TTC.Weighing.Domain.Common;
using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Application.Tests.Properties;

/// <summary>
/// Property 7: Cancellation logic
/// **Validates: Requirements 5.1, 5.3, 5.5, 9.3**
///
/// - Lifecycle ∈ {TempRegistered, Registered} + không có WeighSession Committed
///   → hủy thành công, Lifecycle = Cancelled, mọi QrToken Issued → Revoked
/// - Có WeighSession Committed → hủy thất bại, Lifecycle giữ nguyên
/// </summary>
public class CancellationPropertyTests
{
    private static readonly DateTimeOffset Now = new(2025, 6, 15, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid SiteId = Guid.NewGuid();
    private static readonly Guid TripId = Guid.NewGuid();
    private const int CurrentVersion = 1;
    private const string UserId = "prop-test-user";
    private const string CorrelationId = "corr-prop-001";

    // ── FsCheck Arbitraries ──────────────────────────────────────────

    /// <summary>
    /// Lifecycle cho phép hủy: TempRegistered hoặc Registered.
    /// </summary>
    private static Arbitrary<TripLifecycle> CancellableLifecycleArb()
    {
        return Gen.Elements(TripLifecycle.TempRegistered, TripLifecycle.Registered)
            .ToArbitrary();
    }

    /// <summary>
    /// Generates 0–3 QrTokens with Status = Issued.
    /// </summary>
    private static Arbitrary<List<QrToken>> IssuedQrTokensArb()
    {
        var tokenGen = Gen.Fresh(() => new QrToken
        {
            TripId = TripId,
            TokenHash = Guid.NewGuid().ToString("N")[..16],
            Type = QrTokenType.Trip,
            Status = QrTokenStatus.Issued,
            IssuedAt = Now.AddHours(-1),
            SiteId = SiteId,
        });

        return Gen.Choose(0, 3)
            .SelectMany(count => Gen.ListOf(count, tokenGen))
            .Select(tokens => tokens.ToList())
            .ToArbitrary();
    }

    /// <summary>
    /// Generates 0–2 WeighSessions with Status ∈ {Open, Abandoned} (no Committed).
    /// </summary>
    private static Arbitrary<List<WeighSession>> NonCommittedSessionsArb()
    {
        var sessionGen = Gen.Elements(WeighSessionStatus.Open, WeighSessionStatus.Abandoned)
            .Select(status => new WeighSession
            {
                TripId = TripId,
                Status = status,
                WeighbridgeId = Guid.NewGuid(),
                OwnershipToken = Guid.NewGuid().ToString("N"),
                OpenedAt = Now.AddMinutes(-30),
            });

        return Gen.Choose(0, 2)
            .SelectMany(count => Gen.ListOf(count, sessionGen))
            .Select(sessions => sessions.ToList())
            .ToArbitrary();
    }

    /// <summary>
    /// Generates a list with at least 1 WeighSession Committed, plus 0–1 non-committed.
    /// </summary>
    private static Arbitrary<List<WeighSession>> WithCommittedSessionArb()
    {
        var committedGen = Gen.Constant(new WeighSession
        {
            TripId = TripId,
            Status = WeighSessionStatus.Committed,
            WeighbridgeId = Guid.NewGuid(),
            OwnershipToken = Guid.NewGuid().ToString("N"),
            OpenedAt = Now.AddMinutes(-30),
        });

        var optionalNonCommittedGen = Gen.Elements(WeighSessionStatus.Open, WeighSessionStatus.Abandoned)
            .Select(status => new WeighSession
            {
                TripId = TripId,
                Status = status,
                WeighbridgeId = Guid.NewGuid(),
                OwnershipToken = Guid.NewGuid().ToString("N"),
                OpenedAt = Now.AddMinutes(-20),
            });

        return committedGen.SelectMany(committed =>
            Gen.Choose(0, 1).SelectMany(extraCount =>
                Gen.ListOf(extraCount, optionalNonCommittedGen)
                    .Select(extras =>
                    {
                        var list = new List<WeighSession> { committed };
                        list.AddRange(extras);
                        return list;
                    })))
            .ToArbitrary();
    }

    /// <summary>
    /// Non-empty cancel reason strings.
    /// </summary>
    private static Arbitrary<string> CancelReasonArb()
    {
        return Arb.Default.NonEmptyString()
            .Filter(s => !string.IsNullOrWhiteSpace(s.Get))
            .Convert(s => s.Get, NonEmptyString.NewNonEmptyString);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static CancelTripHandler CreateHandler(
        ITripRepository trips,
        out IFrmPlanLookup frmLookup,
        out IUnitOfWork uow)
    {
        frmLookup = Substitute.For<IFrmPlanLookup>();
        uow = Substitute.For<IUnitOfWork>();
        var clock = Substitute.For<IClock>();
        var user = Substitute.For<ICurrentUser>();
        var audit = Substitute.For<IAuditLogger>();

        clock.UtcNow.Returns(Now);
        user.UserId.Returns(UserId);
        user.HasPermission(CancelTripHandler.RequiredPermission, SiteId).Returns(true);

        return new CancelTripHandler(trips, frmLookup, uow, clock, user, audit);
    }

    private static Trip BuildTrip(
        TripLifecycle lifecycle,
        List<QrToken> qrTokens,
        List<WeighSession> weighSessions)
    {
        var trip = new Trip
        {
            TripNo = "TT-PROP-001",
            SiteId = SiteId,
            Lifecycle = lifecycle,
            SourceSystem = "MANUAL",
            Version = CurrentVersion,
            AssignedWeighbridgeId = Guid.NewGuid(),
            QueueSequenceNumber = 5,
        };

        // Set Id via reflection since it has protected setter
        typeof(AuditableEntity)
            .GetProperty(nameof(AuditableEntity.Id))!
            .SetValue(trip, TripId);

        trip.QrTokens.AddRange(qrTokens);
        trip.WeighSessions.AddRange(weighSessions);

        return trip;
    }

    // ── Composite generators (FsCheck 2.x ForAll supports max 3 args) ──

    /// <summary>
    /// Combined arbitrary for the success case: (lifecycle, qrTokens, sessions, reason).
    /// </summary>
    private static Arbitrary<Tuple<TripLifecycle, List<QrToken>, List<WeighSession>, string>>
        CancellableScenarioArb()
    {
        var gen = from lifecycle in CancellableLifecycleArb().Generator
                  from tokens in IssuedQrTokensArb().Generator
                  from sessions in NonCommittedSessionsArb().Generator
                  from reason in CancelReasonArb().Generator
                  select Tuple.Create(lifecycle, tokens, sessions, reason);
        return gen.ToArbitrary();
    }

    /// <summary>
    /// Combined arbitrary for the failure case: (lifecycle, qrTokens, sessionsWithCommitted, reason).
    /// </summary>
    private static Arbitrary<Tuple<TripLifecycle, List<QrToken>, List<WeighSession>, string>>
        CommittedScenarioArb()
    {
        var gen = from lifecycle in CancellableLifecycleArb().Generator
                  from tokens in IssuedQrTokensArb().Generator
                  from sessions in WithCommittedSessionArb().Generator
                  from reason in CancelReasonArb().Generator
                  select Tuple.Create(lifecycle, tokens, sessions, reason);
        return gen.ToArbitrary();
    }

    // ================================================================
    // Property 7a: Cancellable trip (allowed lifecycle, no Committed session)
    //   → cancel succeeds, Lifecycle = Cancelled, all Issued QrTokens → Revoked
    // **Validates: Requirements 5.1, 5.3, 9.3**
    // ================================================================

    [Property(MaxTest = 100)]
    public Property Cancellable_trip_succeeds_and_revokes_all_tokens()
    {
        return Prop.ForAll(
            CancellableScenarioArb(),
            (Tuple<TripLifecycle, List<QrToken>, List<WeighSession>, string> scenario) =>
            {
                var (lifecycle, qrTokens, sessions, reason) = scenario;

                // Arrange
                var trip = BuildTrip(lifecycle, qrTokens, sessions);
                var tripsRepo = Substitute.For<ITripRepository>();
                tripsRepo.GetForUpdateAsync(TripId, CurrentVersion, Arg.Any<CancellationToken>())
                    .Returns(trip);

                var handler = CreateHandler(tripsRepo, out _, out var uow);

                var cmd = new CancelTripCommand(
                    TripId, CurrentVersion, reason, "confirm-user", CorrelationId);

                // Act
                var result = handler.HandleAsync(cmd).GetAwaiter().GetResult();

                // Assert
                var isSuccess = result.IsSuccess;
                var lifecycleCancelled = trip.Lifecycle == TripLifecycle.Cancelled;
                var allTokensRevoked = trip.QrTokens
                    .Where(t => t.Status == QrTokenStatus.Issued || t.Status == QrTokenStatus.Revoked)
                    .All(t => t.Status == QrTokenStatus.Revoked);
                var queueCleared = trip.AssignedWeighbridgeId is null
                    && trip.QueueSequenceNumber is null;

                return isSuccess
                    .Label("IsSuccess must be true")
                    .And(lifecycleCancelled
                        .Label($"Lifecycle must be Cancelled, got {trip.Lifecycle}"))
                    .And(allTokensRevoked
                        .Label("All originally-Issued QrTokens must be Revoked"))
                    .And(queueCleared
                        .Label("Queue assignment must be cleared (WeighbridgeId and SequenceNumber null)"));
            });
    }

    // ================================================================
    // Property 7b: Trip with Committed WeighSession → cancel fails, Lifecycle unchanged
    // **Validates: Requirements 5.5**
    // ================================================================

    [Property(MaxTest = 100)]
    public Property Trip_with_committed_session_rejects_cancel_and_preserves_lifecycle()
    {
        return Prop.ForAll(
            CommittedScenarioArb(),
            (Tuple<TripLifecycle, List<QrToken>, List<WeighSession>, string> scenario) =>
            {
                var (lifecycle, qrTokens, sessions, reason) = scenario;

                // Arrange
                var trip = BuildTrip(lifecycle, qrTokens, sessions);
                var originalLifecycle = trip.Lifecycle;
                var originalTokenStatuses = trip.QrTokens
                    .Select(t => t.Status).ToList();

                var tripsRepo = Substitute.For<ITripRepository>();
                tripsRepo.GetForUpdateAsync(TripId, CurrentVersion, Arg.Any<CancellationToken>())
                    .Returns(trip);

                var handler = CreateHandler(tripsRepo, out _, out var uow);

                var cmd = new CancelTripCommand(
                    TripId, CurrentVersion, reason, "confirm-user", CorrelationId);

                // Act
                var result = handler.HandleAsync(cmd).GetAwaiter().GetResult();

                // Assert
                var isFailed = !result.IsSuccess;
                var lifecycleUnchanged = trip.Lifecycle == originalLifecycle;
                var tokensUnchanged = trip.QrTokens
                    .Select(t => t.Status)
                    .SequenceEqual(originalTokenStatuses);

                return isFailed
                    .Label("IsSuccess must be false when Committed session exists")
                    .And(lifecycleUnchanged
                        .Label($"Lifecycle must stay {originalLifecycle}, got {trip.Lifecycle}"))
                    .And(tokensUnchanged
                        .Label("QrToken statuses must remain unchanged on rejection"));
            });
    }
}
