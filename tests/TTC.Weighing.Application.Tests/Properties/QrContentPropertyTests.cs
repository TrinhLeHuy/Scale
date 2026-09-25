using FsCheck;
using FsCheck.Xunit;
using TTC.Weighing.Application.UseCases.Trips;

namespace TTC.Weighing.Application.Tests.Properties;

/// <summary>
/// Property-based tests cho QR content encode/decode round-trip.
/// **Validates: Requirements 2.5, 3.3, 8.2**
///
/// Property 3: Nội dung mã hóa QR round-trip
/// - Với mọi QR_chuyến: decode(encode(hash, seq, bridge, warehouse)) chứa đúng hash[..8], seq, bridge, warehouse
/// - Với mọi QR_tạm: decode(encode(hash)) chỉ chứa token prefix, không có seq/bridge/warehouse
/// </summary>
public class QrContentPropertyTests
{
    /// <summary>
    /// Generator cho token hash hợp lệ: chuỗi hex có ít nhất 8 ký tự.
    /// QrTokenFactory.Create() trả về SHA-256 hex (64 chars), nên ta tạo hash 8..64 hex chars.
    /// </summary>
    private static Arbitrary<string> ValidTokenHashArb()
    {
        var gen = from length in Gen.Choose(8, 64)
                  from chars in Gen.ArrayOf(length, Gen.Elements(
                      '0', '1', '2', '3', '4', '5', '6', '7',
                      '8', '9', 'A', 'B', 'C', 'D', 'E', 'F'))
                  select new string(chars);

        return Arb.From(gen);
    }

    /// <summary>
    /// Generator cho mã bàn cân / mã kho: chuỗi không rỗng, chứa alphanumeric và dấu gạch ngang.
    /// </summary>
    private static Arbitrary<string> NonEmptyCodeArb()
    {
        var gen = from length in Gen.Choose(1, 20)
                  from chars in Gen.ArrayOf(length, Gen.Elements(
                      'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J',
                      'K', 'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T',
                      'U', 'V', 'W', 'X', 'Y', 'Z',
                      '0', '1', '2', '3', '4', '5', '6', '7', '8', '9',
                      '-', '_'))
                  select new string(chars);

        return Arb.From(gen);
    }

    /// <summary>
    /// Generator cho số thứ tự hàng chờ dương (> 0).
    /// </summary>
    private static Arbitrary<int> PositiveIntArb()
    {
        return Arb.From(Gen.Choose(1, 100_000));
    }

    // ───────── Property 3a: QR_chuyến round-trip ─────────

    /// <summary>
    /// Với mọi (tokenHash, seq > 0, bridgeCode, warehouseCode):
    /// decode(encode(hash, seq, bridge, warehouse)).TokenPrefix == hash[..8]
    /// decode(encode(hash, seq, bridge, warehouse)).QueueSequenceNumber == seq
    /// decode(encode(hash, seq, bridge, warehouse)).WeighbridgeCode == bridge
    /// decode(encode(hash, seq, bridge, warehouse)).WarehouseCode == warehouse
    ///
    /// **Validates: Requirements 2.5, 3.3, 8.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TripQr_Encode_Decode_RoundTrip_PreservesAllFields()
    {
        var tupleArb = Arb.From(
            from tokenHash in ValidTokenHashArb().Generator
            from seq in PositiveIntArb().Generator
            from bridgeCode in NonEmptyCodeArb().Generator
            from warehouseCode in NonEmptyCodeArb().Generator
            select Tuple.Create(tokenHash, seq, bridgeCode, warehouseCode));

        return Prop.ForAll(tupleArb, tuple =>
        {
            var (tokenHash, seq, bridgeCode, warehouseCode) = tuple;

            var encoded = QrTokenFactory.Encode(tokenHash, seq, bridgeCode, warehouseCode);
            var decoded = QrTokenFactory.Decode(encoded);

            return (decoded != null)
                .Label("decoded is not null")
                .And(() => (decoded!.TokenPrefix == tokenHash[..8])
                    .Label($"TokenPrefix: expected '{tokenHash[..8]}', got '{decoded.TokenPrefix}'"))
                .And(() => (decoded!.QueueSequenceNumber == seq)
                    .Label($"QueueSequenceNumber: expected {seq}, got {decoded.QueueSequenceNumber}"))
                .And(() => (decoded!.WeighbridgeCode == bridgeCode)
                    .Label($"WeighbridgeCode: expected '{bridgeCode}', got '{decoded.WeighbridgeCode}'"))
                .And(() => (decoded!.WarehouseCode == warehouseCode)
                    .Label($"WarehouseCode: expected '{warehouseCode}', got '{decoded.WarehouseCode}'"));
        });
    }

    // ───────── Property 3b: QR_tạm round-trip ─────────

    /// <summary>
    /// Với mọi tokenHash hợp lệ:
    /// decode(encode(hash)).TokenPrefix == hash[..8]
    /// decode(encode(hash)).QueueSequenceNumber == null
    /// decode(encode(hash)).WeighbridgeCode == null
    /// decode(encode(hash)).WarehouseCode == null
    ///
    /// **Validates: Requirements 2.5, 3.3, 8.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TempQr_Encode_Decode_RoundTrip_ContainsOnlyTokenPrefix()
    {
        return Prop.ForAll(
            ValidTokenHashArb(),
            (string tokenHash) =>
            {
                var encoded = QrTokenFactory.Encode(tokenHash);
                var decoded = QrTokenFactory.Decode(encoded);

                return (decoded != null)
                    .Label("decoded is not null")
                    .And(() => (decoded!.TokenPrefix == tokenHash[..8])
                        .Label($"TokenPrefix: expected '{tokenHash[..8]}', got '{decoded.TokenPrefix}'"))
                    .And(() => (decoded!.QueueSequenceNumber == null)
                        .Label($"QueueSequenceNumber should be null, got {decoded.QueueSequenceNumber}"))
                    .And(() => (decoded!.WeighbridgeCode == null)
                        .Label($"WeighbridgeCode should be null, got '{decoded.WeighbridgeCode}'"))
                    .And(() => (decoded!.WarehouseCode == null)
                        .Label($"WarehouseCode should be null, got '{decoded.WarehouseCode}'"));
            });
    }
}
