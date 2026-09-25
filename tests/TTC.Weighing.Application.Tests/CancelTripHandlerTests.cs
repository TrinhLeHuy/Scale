using NSubstitute;
using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.UseCases.Trips;
using TTC.Weighing.Domain.Common;
using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Application.Tests;

/// <summary>
/// Unit tests cho CancelTripHandler — Req 5.1, 5.3, 5.4, 5.5, 5.6.
/// </summary>
public class CancelTripHandlerTests
{
    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly Guid SiteId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2025, 1, 15, 8, 0, 0, TimeSpan.Zero);
    private const string UserId = "user-01";
    private const string CorrelationId = "corr-001";
    private const int CurrentVersion = 1;

    private readonly ITripRepository _trips = Substitute.For<ITripRepository>();
    private readonly IFrmPlanLookup _frmLookup = Substitute.For<IFrmPlanLookup>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly IAuditLogger _audit = Substitute.For<IAuditLogger>();
    private readonly CancelTripHandler _sut;

    public CancelTripHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _user.UserId.Returns(UserId);
        _user.HasPermission(CancelTripHandler.RequiredPermission, SiteId).Returns(true);
        _sut = new CancelTripHandler(_trips, _frmLookup, _uow, _clock, _user, _audit);
    }

    private static CancelTripCommand DefaultCommand(string reason = "Xe rời đi") =>
        new(TripId, CurrentVersion, reason, "confirm-user", CorrelationId);

    /// <summary>
    /// Tạo Trip cơ bản ở trạng thái cho phép hủy: Registered, chưa có WeighSession Committed.
    /// </summary>
    private static Trip CancellableTrip(
        TripLifecycle lifecycle = TripLifecycle.Registered,
        string? referenceTripNo = null,
        string sourceSystem = "MANUAL")
    {
        var trip = new Trip
        {
            TripNo = "TT-001",
            SiteId = SiteId,
            Lifecycle = lifecycle,
            SourceSystem = sourceSystem,
            ReferenceTripNo = referenceTripNo,
            Version = CurrentVersion,
            AssignedWeighbridgeId = Guid.NewGuid(),
            QueueSequenceNumber = 5,
        };

        // Set Id via reflection since it has protected setter
        typeof(AuditableEntity)
            .GetProperty(nameof(AuditableEntity.Id))!
            .SetValue(trip, TripId);

        return trip;
    }

    private static QrToken IssuedToken() => new()
    {
        TripId = TripId,
        TokenHash = "abc123hash",
        Type = QrTokenType.Trip,
        Status = QrTokenStatus.Issued,
        IssuedAt = Now.AddHours(-1),
        SiteId = SiteId,
    };

    private void SetupTrip(Trip trip)
    {
        _trips.GetForUpdateAsync(TripId, CurrentVersion, Arg.Any<CancellationToken>())
            .Returns(trip);
    }

    // ──────────────────────────────────────────────────────
    // Req 5.1, 5.3 — Hủy thành công: Lifecycle → Cancelled, QrToken → Revoked
    // ──────────────────────────────────────────────────────

    /// <summary>
    /// Req 5.1/5.3 — Hủy trip Registered thành công: Lifecycle → Cancelled,
    /// QrToken.Status → Revoked, giải phóng hàng chờ, ghi audit log.
    /// </summary>
    [Theory]
    [InlineData(TripLifecycle.Registered)]
    [InlineData(TripLifecycle.TempRegistered)]
    public async Task Cancel_trip_in_allowed_lifecycle_succeeds(TripLifecycle lifecycle)
    {
        var trip = CancellableTrip(lifecycle);
        var token = IssuedToken();
        trip.QrTokens.Add(token);
        SetupTrip(trip);

        var result = await _sut.HandleAsync(DefaultCommand());

        Assert.True(result.IsSuccess);
        Assert.Null(result.FailureReason);
        Assert.Equal(TripLifecycle.Cancelled, trip.Lifecycle);
        Assert.Equal(QrTokenStatus.Revoked, token.Status);
        Assert.NotNull(token.RevokedAt);
        Assert.Null(trip.AssignedWeighbridgeId);
        Assert.Null(trip.QueueSequenceNumber);

        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _audit.Received(1).LogAsync(
            "TRIP_CANCELLED",
            TripId,
            UserId,
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            "Xe rời đi",
            CorrelationId,
            Arg.Any<CancellationToken>());
    }

    /// <summary>Req 5.3 — Nhiều QrToken Issued đều được Revoked, token đã Revoked giữ nguyên.</summary>
    [Fact]
    public async Task Cancel_revokes_all_issued_qr_tokens_leaving_already_revoked_unchanged()
    {
        var trip = CancellableTrip();
        var issuedToken1 = IssuedToken();
        var issuedToken2 = IssuedToken();
        var alreadyRevoked = new QrToken
        {
            TripId = TripId,
            TokenHash = "old-hash",
            Type = QrTokenType.Temp,
            Status = QrTokenStatus.Revoked,
            RevokedAt = Now.AddHours(-2),
            IssuedAt = Now.AddHours(-3),
            SiteId = SiteId,
        };

        trip.QrTokens.AddRange([issuedToken1, issuedToken2, alreadyRevoked]);
        SetupTrip(trip);

        var result = await _sut.HandleAsync(DefaultCommand());

        Assert.True(result.IsSuccess);
        Assert.Equal(QrTokenStatus.Revoked, issuedToken1.Status);
        Assert.Equal(QrTokenStatus.Revoked, issuedToken2.Status);
        // Already-revoked token giữ nguyên RevokedAt cũ
        Assert.Equal(Now.AddHours(-2), alreadyRevoked.RevokedAt);
    }

    // ──────────────────────────────────────────────────────
    // Req 5.5 — Từ chối khi đã có WeighSession Committed
    // ──────────────────────────────────────────────────────

    /// <summary>Req 5.5 — Có WeighSession Committed → từ chối hủy, Lifecycle giữ nguyên.</summary>
    [Fact]
    public async Task Reject_cancel_when_committed_weigh_session_exists()
    {
        var trip = CancellableTrip();
        trip.WeighSessions.Add(new WeighSession
        {
            TripId = TripId,
            Status = WeighSessionStatus.Committed,
            WeighbridgeId = Guid.NewGuid(),
            OwnershipToken = "own-token",
            OpenedAt = Now.AddMinutes(-30),
        });
        SetupTrip(trip);

        var result = await _sut.HandleAsync(DefaultCommand());

        Assert.False(result.IsSuccess);
        Assert.Contains("Committed", result.FailureReason);
        Assert.Equal(TripLifecycle.Registered, trip.Lifecycle); // Giữ nguyên
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>WeighSession Open hoặc Abandoned không chặn hủy.</summary>
    [Theory]
    [InlineData(WeighSessionStatus.Open)]
    [InlineData(WeighSessionStatus.Abandoned)]
    public async Task Allow_cancel_with_non_committed_weigh_sessions(WeighSessionStatus status)
    {
        var trip = CancellableTrip();
        trip.WeighSessions.Add(new WeighSession
        {
            TripId = TripId,
            Status = status,
            WeighbridgeId = Guid.NewGuid(),
            OwnershipToken = "own-token",
            OpenedAt = Now.AddMinutes(-30),
        });
        trip.QrTokens.Add(IssuedToken());
        SetupTrip(trip);

        var result = await _sut.HandleAsync(DefaultCommand());

        Assert.True(result.IsSuccess);
        Assert.Equal(TripLifecycle.Cancelled, trip.Lifecycle);
    }

    // ──────────────────────────────────────────────────────
    // Version mismatch → concurrency error
    // ──────────────────────────────────────────────────────

    /// <summary>Version mismatch — GetForUpdateAsync trả null → concurrency error.</summary>
    [Fact]
    public async Task Version_mismatch_returns_concurrency_error()
    {
        _trips.GetForUpdateAsync(TripId, CurrentVersion, Arg.Any<CancellationToken>())
            .Returns((Trip?)null);

        var result = await _sut.HandleAsync(DefaultCommand());

        Assert.False(result.IsSuccess);
        Assert.Contains("version", result.FailureReason, StringComparison.OrdinalIgnoreCase);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ──────────────────────────────────────────────────────
    // Thiếu lý do → từ chối
    // ──────────────────────────────────────────────────────

    /// <summary>Req 5.2 — Lý do hủy bắt buộc: trống hoặc whitespace → từ chối.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Missing_or_blank_reason_is_rejected(string? reason)
    {
        var trip = CancellableTrip();
        SetupTrip(trip);

        var cmd = new CancelTripCommand(TripId, CurrentVersion, reason!, "confirm-user", CorrelationId);
        var result = await _sut.HandleAsync(cmd);

        Assert.False(result.IsSuccess);
        Assert.Contains("lý do", result.FailureReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TripLifecycle.Registered, trip.Lifecycle); // Giữ nguyên
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ──────────────────────────────────────────────────────
    // Req 5.4 — Nguồn FRM → gọi NotifyCancellationAsync, xử lý UNKNOWN
    // ──────────────────────────────────────────────────────

    /// <summary>
    /// Req 5.4 — Trip nguồn FRM → gọi NotifyCancellationAsync, trả FrmNotifyStatus = Acknowledged.
    /// </summary>
    [Fact]
    public async Task Frm_source_trip_sends_cancellation_notification()
    {
        var trip = CancellableTrip(
            referenceTripNo: "LD-2025-001",
            sourceSystem: "FRM");
        trip.QrTokens.Add(IssuedToken());
        SetupTrip(trip);

        _frmLookup.NotifyCancellationAsync(
            SiteId, TripId, "LD-2025-001", "Xe rời đi", CorrelationId,
            Arg.Any<CancellationToken>())
            .Returns(new IntegrationSendResult(true, IntegrationMessageStatus.Acknowledged, null));

        var result = await _sut.HandleAsync(DefaultCommand());

        Assert.True(result.IsSuccess);
        Assert.Equal(IntegrationMessageStatus.Acknowledged, result.FrmNotifyStatus);
        Assert.Equal(TripLifecycle.Cancelled, trip.Lifecycle);
        await _frmLookup.Received(1).NotifyCancellationAsync(
            SiteId, TripId, "LD-2025-001", "Xe rời đi", CorrelationId,
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Req 5.4 + R-024 — FRM trả UNKNOWN: trip vẫn Cancelled, FrmNotifyStatus = Unknown.
    /// IntegrationMonitor xử lý retry sau.
    /// </summary>
    [Fact]
    public async Task Frm_unknown_response_still_cancels_trip()
    {
        var trip = CancellableTrip(
            referenceTripNo: "LD-2025-002",
            sourceSystem: "FRM");
        trip.QrTokens.Add(IssuedToken());
        SetupTrip(trip);

        _frmLookup.NotifyCancellationAsync(
            SiteId, TripId, "LD-2025-002", "Xe rời đi", CorrelationId,
            Arg.Any<CancellationToken>())
            .Returns(new IntegrationSendResult(false, IntegrationMessageStatus.Unknown, "Timeout"));

        var result = await _sut.HandleAsync(DefaultCommand());

        Assert.True(result.IsSuccess);
        Assert.Equal(IntegrationMessageStatus.Unknown, result.FrmNotifyStatus);
        Assert.Equal(TripLifecycle.Cancelled, trip.Lifecycle);
    }

    /// <summary>Trip không thuộc FRM → không gọi NotifyCancellationAsync.</summary>
    [Fact]
    public async Task Non_frm_source_does_not_send_cancellation_notification()
    {
        var trip = CancellableTrip(sourceSystem: "MANUAL");
        trip.QrTokens.Add(IssuedToken());
        SetupTrip(trip);

        var result = await _sut.HandleAsync(DefaultCommand());

        Assert.True(result.IsSuccess);
        Assert.Null(result.FrmNotifyStatus);
        await _frmLookup.DidNotReceiveWithAnyArgs()
            .NotifyCancellationAsync(default, default, default, default!, default!, default);
    }

    // ──────────────────────────────────────────────────────
    // Bổ sung — Lifecycle không hợp lệ
    // ──────────────────────────────────────────────────────

    /// <summary>Lifecycle ngoài {TempRegistered, Registered} → từ chối hủy.</summary>
    [Theory]
    [InlineData(TripLifecycle.InProgress)]
    [InlineData(TripLifecycle.Closed)]
    [InlineData(TripLifecycle.Cancelled)]
    public async Task Reject_cancel_for_invalid_lifecycle(TripLifecycle lifecycle)
    {
        var trip = CancellableTrip(lifecycle);
        SetupTrip(trip);

        var result = await _sut.HandleAsync(DefaultCommand());

        Assert.False(result.IsSuccess);
        Assert.Equal(lifecycle, trip.Lifecycle); // Giữ nguyên
    }

    // ──────────────────────────────────────────────────────
    // Bổ sung — Thiếu quyền trip.cancel
    // ──────────────────────────────────────────────────────

    /// <summary>Thiếu quyền trip.cancel → từ chối hủy.</summary>
    [Fact]
    public async Task Missing_permission_rejects_cancel()
    {
        var trip = CancellableTrip();
        SetupTrip(trip);
        _user.HasPermission(CancelTripHandler.RequiredPermission, SiteId).Returns(false);

        var result = await _sut.HandleAsync(DefaultCommand());

        Assert.False(result.IsSuccess);
        Assert.Contains("quyền", result.FailureReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TripLifecycle.Registered, trip.Lifecycle); // Giữ nguyên
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
