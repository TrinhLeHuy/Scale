using NSubstitute;
using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.UseCases.Trips;
using TTC.Weighing.Domain.Common;
using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Application.Tests;

/// <summary>
/// Unit tests cho UpdateSugarcaneTypeHandler — Req 12.1, 12.2, 12.4.
/// </summary>
public class UpdateSugarcaneTypeHandlerTests
{
    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly Guid SiteId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2025, 1, 15, 8, 0, 0, TimeSpan.Zero);
    private const string UserId = "user-kho-01";
    private const string CorrelationId = "corr-update-001";

    private readonly ITripRepository _trips = Substitute.For<ITripRepository>();
    private readonly IIntegrationMessageRepository _messages = Substitute.For<IIntegrationMessageRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly IAuditLogger _audit = Substitute.For<IAuditLogger>();
    private readonly UpdateSugarcaneTypeHandler _sut;

    public UpdateSugarcaneTypeHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _user.UserId.Returns(UserId);
        _sut = new UpdateSugarcaneTypeHandler(_trips, _messages, _uow, _clock, _user, _audit);
    }

    private static UpdateSugarcaneTypeCommand DefaultCommand(
        string newType = "Mía cháy",
        string source = "FRM") =>
        new(TripId, newType, source, CorrelationId);

    /// <summary>
    /// Tạo Trip ở trạng thái cho phép cập nhật loại mía.
    /// </summary>
    private static Trip ActiveTrip(
        TripLifecycle lifecycle = TripLifecycle.InProgress,
        string? sugarcaneType = "Mía tươi")
    {
        var trip = new Trip
        {
            TripNo = "TT-100",
            SiteId = SiteId,
            Lifecycle = lifecycle,
            SourceSystem = "FRM",
            SugarcaneType = sugarcaneType,
            Version = 1,
        };

        typeof(AuditableEntity)
            .GetProperty(nameof(AuditableEntity.Id))!
            .SetValue(trip, TripId);

        return trip;
    }

    private void SetupTrip(Trip? trip)
    {
        _trips.GetByIdAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(trip);
    }

    private void SetupMessages(params IntegrationMessage[] msgs)
    {
        _messages.ListByTripAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(msgs.ToList().AsReadOnly());
    }

    // ──────────────────────────────────────────────────────
    // Req 12.1, 12.2 — Cập nhật thành công, ghi audit, trả OldValue/NewValue
    // ──────────────────────────────────────────────────────

    /// <summary>
    /// Req 12.1/12.2 — Cập nhật loại mía thành công: SugarcaneType được đổi,
    /// ghi audit log với giá trị cũ và mới, trả OldValue/NewValue đúng.
    /// </summary>
    [Fact]
    public async Task Update_sugarcane_type_succeeds_with_audit_and_values()
    {
        var trip = ActiveTrip(sugarcaneType: "Mía tươi");
        SetupTrip(trip);
        SetupMessages(); // Không có message → chưa Posted

        var result = await _sut.HandleAsync(DefaultCommand("Mía cháy", "FRM"));

        Assert.True(result.IsSuccess);
        Assert.Equal("Mía tươi", result.OldValue);
        Assert.Equal("Mía cháy", result.NewValue);
        Assert.False(result.RequiresAdjustmentRequest);
        Assert.Null(result.FailureReason);

        // Trip entity đã cập nhật
        Assert.Equal("Mía cháy", trip.SugarcaneType);
        Assert.Equal(Now, trip.UpdatedAt);
        Assert.Equal(UserId, trip.UpdatedBy);

        // Audit log đã ghi
        await _audit.Received(1).LogAsync(
            Arg.Is("SUGARCANE_TYPE_UPDATED"),
            Arg.Is<Guid?>(TripId),
            Arg.Is(UserId),
            Arg.Is<string?>(s => s != null && s.Contains("Mía tươi")),
            Arg.Is<string?>(s => s != null && s.Contains("Mía cháy") && s.Contains("FRM")),
            Arg.Is<string?>(s => s == null),
            Arg.Is(CorrelationId),
            Arg.Any<CancellationToken>());

        // UoW đã lưu
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ──────────────────────────────────────────────────────
    // Req 12.4 — Trip đã Posted → RequiresAdjustmentRequest = true
    // ──────────────────────────────────────────────────────

    /// <summary>
    /// Req 12.4 — Nếu chuyến đã gửi kết quả đến ERP/FRM (Acknowledged),
    /// trả RequiresAdjustmentRequest = true để tạo yêu cầu điều chỉnh.
    /// </summary>
    [Fact]
    public async Task Update_posted_trip_requires_adjustment_request()
    {
        var trip = ActiveTrip(TripLifecycle.Posted, "Mía tươi");
        SetupTrip(trip);
        SetupMessages(new IntegrationMessage
        {
            TripId = TripId,
            Destination = "FRM",
            Operation = "PostWeighResult",
            BusinessKey = "FRM-TT100-RESULT-1",
            Status = IntegrationMessageStatus.Acknowledged,
        });

        var result = await _sut.HandleAsync(DefaultCommand("Mía cháy"));

        Assert.True(result.IsSuccess);
        Assert.True(result.RequiresAdjustmentRequest);
        Assert.Equal("Mía tươi", result.OldValue);
        Assert.Equal("Mía cháy", result.NewValue);

        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Req 12.4 — Message ở trạng thái khác Acknowledged (ví dụ New, Failed)
    /// → RequiresAdjustmentRequest = false, vì chưa thực sự Posted.
    /// </summary>
    [Theory]
    [InlineData(IntegrationMessageStatus.New)]
    [InlineData(IntegrationMessageStatus.Failed)]
    [InlineData(IntegrationMessageStatus.Held)]
    [InlineData(IntegrationMessageStatus.Unknown)]
    public async Task Non_acknowledged_messages_do_not_require_adjustment(IntegrationMessageStatus status)
    {
        var trip = ActiveTrip(TripLifecycle.InProgress, "Mía tươi");
        SetupTrip(trip);
        SetupMessages(new IntegrationMessage
        {
            TripId = TripId,
            Destination = "FRM",
            Operation = "PostWeighResult",
            BusinessKey = "FRM-TT100-RESULT-1",
            Status = status,
        });

        var result = await _sut.HandleAsync(DefaultCommand("Mía cháy"));

        Assert.True(result.IsSuccess);
        Assert.False(result.RequiresAdjustmentRequest);
    }

    // ──────────────────────────────────────────────────────
    // Trip Cancelled → từ chối
    // ──────────────────────────────────────────────────────

    /// <summary>
    /// Trip đã Cancelled → từ chối cập nhật, SugarcaneType giữ nguyên.
    /// </summary>
    [Fact]
    public async Task Reject_update_for_cancelled_trip()
    {
        var trip = ActiveTrip(TripLifecycle.Cancelled, "Mía tươi");
        SetupTrip(trip);

        var result = await _sut.HandleAsync(DefaultCommand("Mía cháy"));

        Assert.False(result.IsSuccess);
        Assert.Contains("hủy", result.FailureReason, StringComparison.OrdinalIgnoreCase);
        Assert.False(result.RequiresAdjustmentRequest);

        // SugarcaneType giữ nguyên
        Assert.Equal("Mía tươi", trip.SugarcaneType);

        // Không lưu, không audit
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _audit.DidNotReceiveWithAnyArgs().LogAsync(
            default!, default, default!, default, default, default, default!, default);
    }

    // ──────────────────────────────────────────────────────
    // Trip không tồn tại → từ chối
    // ──────────────────────────────────────────────────────

    /// <summary>Trip không tồn tại → trả lỗi.</summary>
    [Fact]
    public async Task Reject_update_for_nonexistent_trip()
    {
        SetupTrip(null);

        var result = await _sut.HandleAsync(DefaultCommand());

        Assert.False(result.IsSuccess);
        Assert.Contains("không tồn tại", result.FailureReason, StringComparison.OrdinalIgnoreCase);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ──────────────────────────────────────────────────────
    // Req 12.1 — Cho phép cập nhật ở các lifecycle hợp lệ
    // ──────────────────────────────────────────────────────

    /// <summary>
    /// Req 12.1 — Cho phép cập nhật loại mía ở mọi lifecycle trừ Cancelled.
    /// </summary>
    [Theory]
    [InlineData(TripLifecycle.TempRegistered)]
    [InlineData(TripLifecycle.Registered)]
    [InlineData(TripLifecycle.InProgress)]
    [InlineData(TripLifecycle.WeighingCompleted)]
    [InlineData(TripLifecycle.Reconciled)]
    [InlineData(TripLifecycle.Posted)]
    [InlineData(TripLifecycle.Closed)]
    public async Task Allow_update_for_all_non_cancelled_lifecycles(TripLifecycle lifecycle)
    {
        var trip = ActiveTrip(lifecycle, "Mía tươi");
        SetupTrip(trip);
        SetupMessages(); // Không có message Acknowledged

        var result = await _sut.HandleAsync(DefaultCommand("Mía cháy"));

        Assert.True(result.IsSuccess);
        Assert.Equal("Mía cháy", trip.SugarcaneType);
    }
}
