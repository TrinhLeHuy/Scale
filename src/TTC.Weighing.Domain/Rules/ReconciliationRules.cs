using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Domain.Rules;

/// <summary>Chính sách dung sai đã duyệt. Null ở bất kỳ trường bắt buộc = chưa duyệt (D-06).</summary>
public sealed record TolerancePolicy
{
    public string? PolicyId { get; init; }
    public int? PolicyVersion { get; init; }

    /// <summary>
    /// Biên sai số của bàn cân dùng trong công thức hàng bao (S03 tr.16 nêu 20 kg).
    /// KHÔNG mặc định áp cho mọi loại hàng.
    /// </summary>
    public decimal? ScaleMarginKg { get; init; }

    /// <summary>Dung sai tuyệt đối cho so sánh cân xe ↔ IoT.</summary>
    public decimal? VehicleVsIotToleranceKg { get; init; }

    /// <summary>Dung sai số bao cho so sánh đếm AI ↔ xác nhận kho.</summary>
    public int? BagCountTolerance { get; init; }

    public bool IsApproved => !string.IsNullOrWhiteSpace(PolicyId) && PolicyVersion.HasValue;
}

/// <summary>Số đo độc lập của một chuyến. Mỗi nguồn giữ riêng, không trộn (02_Quy_tac mục 6).</summary>
public sealed record ReconciliationInput
{
    public decimal? VehicleNetKg { get; init; }
    public MassBasis VehicleNetBasis { get; init; } = MassBasis.Unknown;

    public decimal? IotMassKg { get; init; }
    public MassOrigin? IotMassOrigin { get; init; }
    public MassBasis IotMassBasis { get; init; } = MassBasis.Unknown;

    public int? AiBagCount { get; init; }

    /// <summary>Số kho/ERP đã xác nhận. KHÔNG dùng planned thay cho số này.</summary>
    public decimal? WarehouseConfirmedQuantity { get; init; }
    public string? WarehouseConfirmedUom { get; init; }

    public decimal? PlannedQuantity { get; init; }

    // Quy cách từ BOM snapshot
    public decimal? NominalKgPerUnit { get; init; }
    public decimal? MinKgPerUnit { get; init; }
    public decimal? MaxKgPerUnit { get; init; }
    public MassBasis BomMassBasis { get; init; } = MassBasis.Unknown;

    public required TolerancePolicy Tolerance { get; init; }

    // Phạm vi áp dụng theo profile
    public bool IotApplicable { get; init; }
    public bool BagCountApplicable { get; init; }
    public bool WarehouseConfirmationRequired { get; init; }
}

public sealed record ReconciliationCheckResult(
    ReconciliationCheckType CheckType,
    ReconciliationVerdict Verdict,
    decimal? ObservedValue,
    decimal? ExpectedValue,
    decimal? LowerBound,
    decimal? UpperBound,
    decimal? Difference,
    string Unit,
    string? Detail);

/// <summary>
/// R-020 — Đối soát không trộn nguồn.
/// KHÔNG cộng vehicle_net_kg với iot_mass_kg. KHÔNG lấy trung bình ba nguồn.
/// Thiếu dung sai đã duyệt hoặc thiếu quy đổi thì chưa kết luận PASS.
/// </summary>
public static class ReconciliationRules
{
    public static IReadOnlyList<ReconciliationCheckResult> Evaluate(ReconciliationInput input) =>
    [
        CheckVehicleNetVsIotMass(input),
        CheckAiBagCountVsWarehouseCount(input),
        CheckVehicleNetVsNominalRange(input),
        CheckTripActualVsSourcePlan(input)
    ];

    /// <summary>
    /// Chỉ so sánh khi cùng phạm vi hàng, cùng cơ sở khối lượng và IoT là nguồn đo thật.
    /// </summary>
    private static ReconciliationCheckResult CheckVehicleNetVsIotMass(ReconciliationInput i)
    {
        const ReconciliationCheckType type = ReconciliationCheckType.VehicleNetVsIotMass;

        if (!i.IotApplicable)
            return Na(type, "Profile không áp dụng nguồn đo IoT.");

        if (i.VehicleNetKg is null || i.IotMassKg is null)
            return Missing(type, "Thiếu số cân xe hoặc số đo IoT.");

        // IoT quy đổi từ số bao không phải phép đo độc lập — không dùng để xác nhận lẫn nhau.
        if (i.IotMassOrigin != MassOrigin.Measured)
            return Missing(type,
                "IoT khai báo DERIVED_FROM_COUNT nên không phải nguồn cân kg độc lập (B-011).");

        if (i.VehicleNetBasis == MassBasis.Unknown || i.IotMassBasis == MassBasis.Unknown)
            return Missing(type, "Chưa khai báo rõ cơ sở khối lượng của một trong hai nguồn.");

        if (i.VehicleNetBasis != i.IotMassBasis)
            return Missing(type,
                $"Cơ sở khối lượng khác nhau ({i.VehicleNetBasis} so với {i.IotMassBasis}). " +
                "Cần dữ liệu bao bì hợp lệ để quy về cùng cơ sở, không trừ hằng số.");

        var difference = i.VehicleNetKg.Value - i.IotMassKg.Value;

        if (i.Tolerance.VehicleVsIotToleranceKg is null)
            return new(type, ReconciliationVerdict.Pending,
                i.VehicleNetKg, i.IotMassKg, null, null, difference, "kg",
                "Chưa có dung sai đã duyệt cho so sánh cân xe với IoT (D-06).");

        var tolerance = i.Tolerance.VehicleVsIotToleranceKg.Value;
        var passed = Math.Abs(difference) <= tolerance;

        return new(type,
            passed ? ReconciliationVerdict.Pass : ReconciliationVerdict.Mismatch,
            i.VehicleNetKg, i.IotMassKg,
            i.IotMassKg - tolerance, i.IotMassKg + tolerance,
            difference, "kg",
            passed ? null : $"Lệch {difference:N3} kg, vượt dung sai {tolerance:N3} kg.");
    }

    private static ReconciliationCheckResult CheckAiBagCountVsWarehouseCount(ReconciliationInput i)
    {
        const ReconciliationCheckType type = ReconciliationCheckType.AiBagCountVsWarehouseCount;

        if (!i.BagCountApplicable)
            return Na(type, "Profile không áp dụng đếm bao.");

        if (i.AiBagCount is null)
            return Missing(type, "Thiếu số bao đếm được từ AI. Không gán bằng 0 bao.");

        if (i.WarehouseConfirmedQuantity is null)
            return Missing(type, "Thiếu số bao kho đã xác nhận.");

        var difference = i.AiBagCount.Value - i.WarehouseConfirmedQuantity.Value;

        if (i.Tolerance.BagCountTolerance is null)
            return new(type, ReconciliationVerdict.Pending,
                i.AiBagCount, i.WarehouseConfirmedQuantity, null, null, difference, "bao",
                "Chưa có dung sai số bao đã duyệt (D-06).");

        var tolerance = i.Tolerance.BagCountTolerance.Value;
        var passed = Math.Abs(difference) <= tolerance;

        return new(type,
            passed ? ReconciliationVerdict.Pass : ReconciliationVerdict.Mismatch,
            i.AiBagCount, i.WarehouseConfirmedQuantity,
            i.WarehouseConfirmedQuantity - tolerance, i.WarehouseConfirmedQuantity + tolerance,
            difference, "bao",
            passed ? null : $"Lệch {difference:N0} bao, vượt dung sai {tolerance} bao.");
    }

    /// <summary>
    /// Cân xe ↔ khoảng khối lượng tương ứng số kho xác nhận và quy cách (S03 tr.16).
    /// lower = min × ref - margin; upper = max × ref + margin.
    /// BOM chỉ có khối lượng danh nghĩa thì chưa dựng được khoảng.
    /// </summary>
    private static ReconciliationCheckResult CheckVehicleNetVsNominalRange(ReconciliationInput i)
    {
        const ReconciliationCheckType type = ReconciliationCheckType.VehicleNetVsNominalRange;

        if (!i.WarehouseConfirmationRequired)
            return Na(type, "Profile không yêu cầu đối chiếu với quy cách kho.");

        if (i.VehicleNetKg is null)
            return Missing(type, "Thiếu số cân xe.");

        if (i.WarehouseConfirmedQuantity is null)
            return Missing(type, "Thiếu số lượng kho đã xác nhận làm cơ sở quy đổi.");

        if (i.MinKgPerUnit is null || i.MaxKgPerUnit is null)
        {
            var nominalNote = i.NominalKgPerUnit is null
                ? "BOM không có khối lượng danh nghĩa."
                : $"BOM chỉ có khối lượng danh nghĩa {i.NominalKgPerUnit} kg/đơn vị, chưa có min/max.";

            return Missing(type,
                $"{nominalNote} Chưa dựng được khoảng dung sai, không tự áp ±20 kg (S04 tr.29, D-06).");
        }

        if (i.Tolerance.ScaleMarginKg is null)
            return new(type, ReconciliationVerdict.Pending,
                i.VehicleNetKg, null, null, null, null, "kg",
                "Chưa có biên sai số bàn cân đã duyệt cho profile này (D-06).");

        if (i.BomMassBasis != MassBasis.Unknown &&
            i.VehicleNetBasis != MassBasis.Unknown &&
            i.BomMassBasis != i.VehicleNetBasis)
        {
            return Missing(type,
                $"Cơ sở khối lượng BOM ({i.BomMassBasis}) khác cân xe ({i.VehicleNetBasis}).");
        }

        var referenceCount = i.WarehouseConfirmedQuantity.Value;
        var margin = i.Tolerance.ScaleMarginKg.Value;

        var lower = i.MinKgPerUnit.Value * referenceCount - margin;
        var upper = i.MaxKgPerUnit.Value * referenceCount + margin;

        var net = i.VehicleNetKg.Value;
        var passed = net >= lower && net <= upper;

        // Lượng danh nghĩa chỉ để tham chiếu, không phải kg đã cân.
        var nominalKg = i.NominalKgPerUnit * referenceCount;

        return new(type,
            passed ? ReconciliationVerdict.Pass : ReconciliationVerdict.Mismatch,
            net, nominalKg, lower, upper,
            nominalKg is null ? null : net - nominalKg, "kg",
            passed
                ? null
                : $"Cân xe {net:N3} kg ngoài khoảng [{lower:N3}; {upper:N3}] kg.");
    }

    /// <summary>
    /// Số thực tế của chuyến ↔ kế hoạch. KHÔNG mặc định phải bằng toàn bộ đơn hàng (R-038).
    /// </summary>
    private static ReconciliationCheckResult CheckTripActualVsSourcePlan(ReconciliationInput i)
    {
        const ReconciliationCheckType type = ReconciliationCheckType.TripActualVsSourcePlan;

        if (i.PlannedQuantity is null)
            return Na(type, "Nguồn không cung cấp số kế hoạch cho dòng hàng này.");

        if (i.WarehouseConfirmedQuantity is null)
            return Missing(type, "Thiếu số xác nhận thực tế của chuyến.");

        var difference = i.WarehouseConfirmedQuantity.Value - i.PlannedQuantity.Value;

        // Giao một phần là hợp lệ; đây là thông tin đối chiếu, không phải điều kiện chặn.
        return new(type, ReconciliationVerdict.Pass,
            i.WarehouseConfirmedQuantity, i.PlannedQuantity, null, null, difference,
            i.WarehouseConfirmedUom ?? "đơn vị",
            difference == 0
                ? null
                : $"Chuyến này giao {difference:N3} so với kế hoạch — giao một phần là hợp lệ.");
    }

    /// <summary>
    /// Lượng quy đổi từ đếm bao. Phải hiển thị là "kg quy đổi từ đếm bao",
    /// KHÔNG ghi thành kg đã cân (02_Quy_tac mục 6).
    /// </summary>
    public static decimal? ConvertBagCountToNominalKg(int? bagCount, decimal? nominalKgPerUnit)
        => bagCount is null || nominalKgPerUnit is null ? null : bagCount.Value * nominalKgPerUnit.Value;

    private static ReconciliationCheckResult Na(ReconciliationCheckType type, string detail)
        => new(type, ReconciliationVerdict.NotApplicable, null, null, null, null, null, "kg", detail);

    private static ReconciliationCheckResult Missing(ReconciliationCheckType type, string detail)
        => new(type, ReconciliationVerdict.MissingData, null, null, null, null, null, "kg", detail);
}
