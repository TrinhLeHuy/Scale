using System.Security.Cryptography;
using System.Text;
using NSubstitute;
using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Domain.Common;
using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Infrastructure.Adapters;

namespace TTC.Weighing.Application.Tests;

/// <summary>
/// Unit tests cho QrScanHandlerAdapter — Req 1.1, 1.3, 1.4.
/// Decode QR → hash → lookup QrToken → validate → load Trip → build QrScanResult.
/// </summary>
public class QrScanHandlerAdapterTests
{
    private static readonly Guid SiteId = Guid.NewGuid();
    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly Guid WeighbridgeId = Guid.NewGuid();
    private static readonly Guid WarehouseId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2025, 6, 15, 8, 0, 0, TimeSpan.Zero);

    private readonly IQrTokenRepository _qrTokens = Substitute.For<IQrTokenRepository>();
    private readonly ITripRepository _trips = Substitute.For<ITripRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly QrScanHandlerAdapter _sut;

    public QrScanHandlerAdapterTests()
    {
        _clock.UtcNow.Returns(Now);
        _sut = new QrScanHandlerAdapter(_qrTokens, _trips, _clock);
    }

    /// <summary>Compute SHA256 hex the same way the adapter does, to set up stubs.</summary>
    private static string ComputeSha256Hex(string input)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static void SetEntityId(AuditableEntity entity, Guid id) =>
        typeof(AuditableEntity)
            .GetProperty(nameof(AuditableEntity.Id))!
            .SetValue(entity, id);

    private static QrToken ValidTripToken(string tokenHash, Guid tripId) => new()
    {
        TokenHash = tokenHash,
        Type = QrTokenType.Trip,
        Status = QrTokenStatus.Issued,
        TripId = tripId,
        SiteId = SiteId,
        IssuedAt = Now.AddHours(-1),
        ExpiresAt = Now.AddHours(8), // Còn hạn
        RevokedAt = null,
    };

    private static Trip ValidTrip()
    {
        var trip = new Trip
        {
            TripNo = "TT-2025-001",
            SiteId = SiteId,
            Lifecycle = TripLifecycle.Registered,
            TractorPlate = "51A-12345",
            TrailerPlate = "51R-67890",
            SugarcaneOwner = "Nguyễn Văn A",
            SugarcaneType = "Mía tươi",
            SugarcaneVariety = "ROC22",
            ReferenceTripNo = "LD-2025-100",
            AssignedWeighbridgeId = WeighbridgeId,
            AssignedWarehouseId = WarehouseId,
            Version = 1,
        };
        SetEntityId(trip, TripId);
        return trip;
    }

    // ──────────────────────────────────────────────────────
    // Req 1.1 — QR hợp lệ → trả IsSuccess + thông tin chuyến
    // ──────────────────────────────────────────────────────

    /// <summary>
    /// Req 1.1 — QR_chuyến hợp lệ: decode thành công, token tồn tại, chưa hết hạn,
    /// chưa thu hồi, loại Trip → IsSuccess = true, đầy đủ thông tin chuyến.
    /// </summary>
    [Fact]
    public async Task Valid_trip_qr_returns_success_with_trip_info()
    {
        var qrContent = "VALID-QR-CONTENT-12345";
        var tokenHash = ComputeSha256Hex(qrContent);
        var token = ValidTripToken(tokenHash, TripId);
        var trip = ValidTrip();

        _qrTokens.GetByHashAsync(tokenHash, Arg.Any<CancellationToken>()).Returns(token);
        _trips.GetByIdAsync(TripId, Arg.Any<CancellationToken>()).Returns(trip);

        var result = await _sut.DecodeAndLookupAsync(qrContent, SiteId);

        Assert.True(result.IsSuccess);
        Assert.Equal(TripId, result.TripId);
        Assert.Equal(1, result.TripVersion);
        Assert.Equal("TT-2025-001", result.TripNo);
        Assert.Equal("51A-12345", result.Plate);
        Assert.Equal("51R-67890", result.TrailerPlate);
        Assert.Equal("Nguyễn Văn A", result.CustomerOrOwner);
        Assert.Equal("Mía tươi", result.Commodity);
        Assert.Equal("LD-2025-100", result.ShipmentNo);
        Assert.Equal("Mía tươi", result.SugarcaneType);
        Assert.Equal("ROC22", result.SugarcaneVariety);
        Assert.Equal("Nguyễn Văn A", result.SugarcaneOwner);
        Assert.Equal(WeighbridgeId, result.AssignedWeighbridgeId);
        Assert.Equal(WarehouseId, result.AssignedWarehouseId);
        Assert.Equal(QrTokenType.Trip, result.QrType);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
    }

    // ──────────────────────────────────────────────────────
    // Req 1.3 — QR hết hạn → trả ErrorCode + ErrorMessage
    // ──────────────────────────────────────────────────────

    /// <summary>
    /// Req 1.3 — QR đã hết hạn (ExpiresAt < Now): IsSuccess = false,
    /// ErrorCode = QR_EXPIRED, ErrorMessage chứa lý do.
    /// </summary>
    [Fact]
    public async Task Expired_qr_returns_error()
    {
        var qrContent = "EXPIRED-QR-CONTENT";
        var tokenHash = ComputeSha256Hex(qrContent);
        var token = ValidTripToken(tokenHash, TripId);
        token.ExpiresAt = Now.AddHours(-1); // Đã hết hạn

        _qrTokens.GetByHashAsync(tokenHash, Arg.Any<CancellationToken>()).Returns(token);

        var result = await _sut.DecodeAndLookupAsync(qrContent, SiteId);

        Assert.False(result.IsSuccess);
        Assert.Equal("QR_EXPIRED", result.ErrorCode);
        Assert.NotNull(result.ErrorMessage);
        Assert.Null(result.TripId);
    }

    // ──────────────────────────────────────────────────────
    // Req 1.3 — QR đã thu hồi → trả ErrorCode
    // ──────────────────────────────────────────────────────

    /// <summary>
    /// Req 1.3 — QR đã bị thu hồi (RevokedAt != null): IsSuccess = false,
    /// ErrorCode = QR_REVOKED.
    /// </summary>
    [Fact]
    public async Task Revoked_qr_returns_error()
    {
        var qrContent = "REVOKED-QR-CONTENT";
        var tokenHash = ComputeSha256Hex(qrContent);
        var token = ValidTripToken(tokenHash, TripId);
        token.RevokedAt = Now.AddMinutes(-30); // Đã thu hồi

        _qrTokens.GetByHashAsync(tokenHash, Arg.Any<CancellationToken>()).Returns(token);

        var result = await _sut.DecodeAndLookupAsync(qrContent, SiteId);

        Assert.False(result.IsSuccess);
        Assert.Equal("QR_REVOKED", result.ErrorCode);
        Assert.NotNull(result.ErrorMessage);
        Assert.Null(result.TripId);
    }

    // ──────────────────────────────────────────────────────
    // Req 1.4 — QR tạm → QrType = Temp, IsSuccess = false
    // ──────────────────────────────────────────────────────

    /// <summary>
    /// Req 1.4 — QR thuộc loại Temp: IsSuccess = false, QrType = Temp,
    /// ErrorCode = QR_TEMP, kèm thông báo xe chưa đăng ký kế hoạch.
    /// </summary>
    [Fact]
    public async Task Temp_qr_returns_failure_with_temp_type()
    {
        var qrContent = "TEMP-QR-CONTENT";
        var tokenHash = ComputeSha256Hex(qrContent);
        var token = new QrToken
        {
            TokenHash = tokenHash,
            Type = QrTokenType.Temp,
            Status = QrTokenStatus.Issued,
            TripId = TripId,
            SiteId = SiteId,
            IssuedAt = Now.AddHours(-1),
            ExpiresAt = Now.AddHours(8),
            RevokedAt = null,
        };

        _qrTokens.GetByHashAsync(tokenHash, Arg.Any<CancellationToken>()).Returns(token);

        var result = await _sut.DecodeAndLookupAsync(qrContent, SiteId);

        Assert.False(result.IsSuccess);
        Assert.Equal(QrTokenType.Temp, result.QrType);
        Assert.Equal("QR_TEMP", result.ErrorCode);
        Assert.NotNull(result.ErrorMessage);
        Assert.Equal(TripId, result.TripId); // QR tạm vẫn gắn TripId để tra cứu
    }

    // ──────────────────────────────────────────────────────
    // Req 1.1 — QR không decode được (nội dung rỗng) → lỗi cụ thể
    // ──────────────────────────────────────────────────────

    /// <summary>
    /// QR content rỗng hoặc whitespace → IsSuccess = false,
    /// ErrorCode = QR_INVALID.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Empty_or_whitespace_qr_content_returns_invalid_error(string? qrContent)
    {
        var result = await _sut.DecodeAndLookupAsync(qrContent!, SiteId);

        Assert.False(result.IsSuccess);
        Assert.Equal("QR_INVALID", result.ErrorCode);
        Assert.NotNull(result.ErrorMessage);
    }

    // ──────────────────────────────────────────────────────
    // Bổ sung — QR không tìm thấy trong hệ thống
    // ──────────────────────────────────────────────────────

    /// <summary>
    /// QR content hợp lệ nhưng token hash không tồn tại trong DB → QR_NOT_FOUND.
    /// </summary>
    [Fact]
    public async Task Unknown_qr_token_returns_not_found()
    {
        var qrContent = "UNKNOWN-QR-CONTENT-XYZ";
        var tokenHash = ComputeSha256Hex(qrContent);

        _qrTokens.GetByHashAsync(tokenHash, Arg.Any<CancellationToken>()).Returns((QrToken?)null);

        var result = await _sut.DecodeAndLookupAsync(qrContent, SiteId);

        Assert.False(result.IsSuccess);
        Assert.Equal("QR_NOT_FOUND", result.ErrorCode);
        Assert.NotNull(result.ErrorMessage);
    }

    // ──────────────────────────────────────────────────────
    // Bổ sung — Token hợp lệ nhưng Trip không gắn
    // ──────────────────────────────────────────────────────

    /// <summary>
    /// Token hợp lệ nhưng TripId = null → TRIP_NOT_FOUND.
    /// </summary>
    [Fact]
    public async Task Token_without_trip_returns_trip_not_found()
    {
        var qrContent = "TOKEN-NO-TRIP";
        var tokenHash = ComputeSha256Hex(qrContent);
        var token = ValidTripToken(tokenHash, TripId);
        token.TripId = null; // Không gắn chuyến

        _qrTokens.GetByHashAsync(tokenHash, Arg.Any<CancellationToken>()).Returns(token);

        var result = await _sut.DecodeAndLookupAsync(qrContent, SiteId);

        Assert.False(result.IsSuccess);
        Assert.Equal("TRIP_NOT_FOUND", result.ErrorCode);
        Assert.NotNull(result.ErrorMessage);
    }

    /// <summary>
    /// Token gắn TripId nhưng Trip không tồn tại trong DB → TRIP_NOT_FOUND.
    /// </summary>
    [Fact]
    public async Task Token_with_nonexistent_trip_returns_trip_not_found()
    {
        var qrContent = "TOKEN-DANGLING-TRIP";
        var tokenHash = ComputeSha256Hex(qrContent);
        var nonExistentTripId = Guid.NewGuid();
        var token = ValidTripToken(tokenHash, nonExistentTripId);

        _qrTokens.GetByHashAsync(tokenHash, Arg.Any<CancellationToken>()).Returns(token);
        _trips.GetByIdAsync(nonExistentTripId, Arg.Any<CancellationToken>()).Returns((Trip?)null);

        var result = await _sut.DecodeAndLookupAsync(qrContent, SiteId);

        Assert.False(result.IsSuccess);
        Assert.Equal("TRIP_NOT_FOUND", result.ErrorCode);
    }

    // ──────────────────────────────────────────────────────
    // Bổ sung — Token không có ExpiresAt (không hết hạn) vẫn hợp lệ
    // ──────────────────────────────────────────────────────

    /// <summary>
    /// Token không có ExpiresAt (null) → không hết hạn, vẫn IsSuccess = true.
    /// </summary>
    [Fact]
    public async Task Token_without_expiry_is_valid()
    {
        var qrContent = "NO-EXPIRY-QR";
        var tokenHash = ComputeSha256Hex(qrContent);
        var token = ValidTripToken(tokenHash, TripId);
        token.ExpiresAt = null; // Không hết hạn
        var trip = ValidTrip();

        _qrTokens.GetByHashAsync(tokenHash, Arg.Any<CancellationToken>()).Returns(token);
        _trips.GetByIdAsync(TripId, Arg.Any<CancellationToken>()).Returns(trip);

        var result = await _sut.DecodeAndLookupAsync(qrContent, SiteId);

        Assert.True(result.IsSuccess);
        Assert.Equal(TripId, result.TripId);
    }

    // ──────────────────────────────────────────────────────
    // Bổ sung — Thu hồi kiểm tra trước hết hạn
    // ──────────────────────────────────────────────────────

    /// <summary>
    /// Token vừa thu hồi vừa hết hạn → trả QR_REVOKED (thu hồi kiểm tra trước).
    /// </summary>
    [Fact]
    public async Task Revoked_token_checked_before_expiry()
    {
        var qrContent = "REVOKED-AND-EXPIRED";
        var tokenHash = ComputeSha256Hex(qrContent);
        var token = ValidTripToken(tokenHash, TripId);
        token.RevokedAt = Now.AddMinutes(-10);
        token.ExpiresAt = Now.AddMinutes(-5); // Cũng hết hạn

        _qrTokens.GetByHashAsync(tokenHash, Arg.Any<CancellationToken>()).Returns(token);

        var result = await _sut.DecodeAndLookupAsync(qrContent, SiteId);

        Assert.False(result.IsSuccess);
        Assert.Equal("QR_REVOKED", result.ErrorCode); // Thu hồi kiểm tra trước hết hạn
    }
}
