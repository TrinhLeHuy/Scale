using TTC.Weighing.Application.UseCases.Trips;

namespace TTC.Weighing.Application.Tests;

/// <summary>
/// Unit tests cho QrTokenFactory — Encode/Decode QR content (Req 8.2).
/// </summary>
public class QrTokenFactoryTests
{
    // ───────── Create & Hash ─────────

    [Fact]
    public void Create_returns_non_empty_token_and_hash()
    {
        var (rawToken, tokenHash) = QrTokenFactory.Create();

        Assert.False(string.IsNullOrWhiteSpace(rawToken));
        Assert.False(string.IsNullOrWhiteSpace(tokenHash));
    }

    [Fact]
    public void Create_returns_different_tokens_each_call()
    {
        var (raw1, _) = QrTokenFactory.Create();
        var (raw2, _) = QrTokenFactory.Create();

        Assert.NotEqual(raw1, raw2);
    }

    [Fact]
    public void Hash_is_deterministic()
    {
        var (rawToken, tokenHash) = QrTokenFactory.Create();

        Assert.Equal(tokenHash, QrTokenFactory.Hash(rawToken));
    }

    // ───────── Encode QR_chuyến ─────────

    [Fact]
    public void Encode_trip_qr_contains_token_prefix_sequence_bridge_warehouse()
    {
        var (_, tokenHash) = QrTokenFactory.Create();
        var seq = 42;
        var bridge = "BC-01";
        var warehouse = "KHO-A";

        var encoded = QrTokenFactory.Encode(tokenHash, seq, bridge, warehouse);
        var decoded = QrTokenFactory.Decode(encoded);

        Assert.NotNull(decoded);
        Assert.Equal(tokenHash[..8], decoded.TokenPrefix);
        Assert.Equal(seq, decoded.QueueSequenceNumber);
        Assert.Equal(bridge, decoded.WeighbridgeCode);
        Assert.Equal(warehouse, decoded.WarehouseCode);
    }

    [Fact]
    public void Encode_trip_qr_with_null_bridge_and_warehouse_omits_those_fields()
    {
        var (_, tokenHash) = QrTokenFactory.Create();
        var seq = 7;

        var encoded = QrTokenFactory.Encode(tokenHash, seq, null, null);
        var decoded = QrTokenFactory.Decode(encoded);

        Assert.NotNull(decoded);
        Assert.Equal(tokenHash[..8], decoded.TokenPrefix);
        Assert.Equal(seq, decoded.QueueSequenceNumber);
        Assert.Null(decoded.WeighbridgeCode);
        Assert.Null(decoded.WarehouseCode);
    }

    // ───────── Encode QR_tạm ─────────

    [Fact]
    public void Encode_temp_qr_contains_only_token_prefix()
    {
        var (_, tokenHash) = QrTokenFactory.Create();

        var encoded = QrTokenFactory.Encode(tokenHash);
        var decoded = QrTokenFactory.Decode(encoded);

        Assert.NotNull(decoded);
        Assert.Equal(tokenHash[..8], decoded.TokenPrefix);
        Assert.Null(decoded.QueueSequenceNumber);
        Assert.Null(decoded.WeighbridgeCode);
        Assert.Null(decoded.WarehouseCode);
    }

    // ───────── Decode round-trip ─────────

    [Theory]
    [InlineData(1, "BC-01", "KHO-A")]
    [InlineData(999, "BC-LONG-CODE", "WH-002")]
    [InlineData(1, null, null)]
    public void Decode_round_trip_preserves_all_fields(
        int seq, string? bridge, string? warehouse)
    {
        var (_, tokenHash) = QrTokenFactory.Create();

        var encoded = QrTokenFactory.Encode(tokenHash, seq, bridge, warehouse);
        var decoded = QrTokenFactory.Decode(encoded);

        Assert.NotNull(decoded);
        Assert.Equal(tokenHash[..8], decoded.TokenPrefix);
        Assert.Equal(seq, decoded.QueueSequenceNumber);
        Assert.Equal(bridge, decoded.WeighbridgeCode);
        Assert.Equal(warehouse, decoded.WarehouseCode);
    }

    [Fact]
    public void Decode_temp_round_trip_preserves_token_prefix_only()
    {
        var (_, tokenHash) = QrTokenFactory.Create();

        var encoded = QrTokenFactory.Encode(tokenHash);
        var decoded = QrTokenFactory.Decode(encoded);

        Assert.NotNull(decoded);
        Assert.Equal(tokenHash[..8], decoded.TokenPrefix);
        Assert.Null(decoded.QueueSequenceNumber);
        Assert.Null(decoded.WeighbridgeCode);
        Assert.Null(decoded.WarehouseCode);
    }

    [Fact]
    public void Decode_returns_null_for_invalid_content()
    {
        var decoded = QrTokenFactory.Decode("NOT-VALID-BASE64!!!");

        Assert.Null(decoded);
    }
}
