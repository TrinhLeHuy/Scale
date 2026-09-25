using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Domain.Rules;

namespace TTC.Weighing.Domain.Tests;

public class ReconciliationRulesTests
{
    private static TolerancePolicy ApprovedTolerance() => new()
    {
        PolicyId = "TOL-01",
        PolicyVersion = 1,
        ScaleMarginKg = 20m,
        VehicleVsIotToleranceKg = 50m,
        BagCountTolerance = 0
    };

    /// <summary>Bối cảnh hàng bao xuất kho, mọi nguồn đầy đủ và dung sai đã duyệt.</summary>
    private static ReconciliationInput BaggedOutbound() => new()
    {
        VehicleNetKg = 30_050m,
        VehicleNetBasis = MassBasis.PackagedGoods,

        IotMassKg = 30_040m,
        IotMassOrigin = MassOrigin.Measured,
        IotMassBasis = MassBasis.PackagedGoods,

        AiBagCount = 600,
        WarehouseConfirmedQuantity = 600m,
        WarehouseConfirmedUom = "BAG",
        PlannedQuantity = 600m,

        NominalKgPerUnit = 50m,
        MinKgPerUnit = 49.5m,
        MaxKgPerUnit = 50.5m,
        BomMassBasis = MassBasis.PackagedGoods,

        Tolerance = ApprovedTolerance(),

        IotApplicable = true,
        BagCountApplicable = true,
        WarehouseConfirmationRequired = true
    };

    private static ReconciliationCheckResult Check(
        IReadOnlyList<ReconciliationCheckResult> results, ReconciliationCheckType type)
        => results.Single(r => r.CheckType == type);

    [Fact]
    public void All_sources_within_tolerance_pass()
    {
        var results = ReconciliationRules.Evaluate(BaggedOutbound());

        Assert.Equal(ReconciliationVerdict.Pass,
            Check(results, ReconciliationCheckType.VehicleNetVsIotMass).Verdict);
        Assert.Equal(ReconciliationVerdict.Pass,
            Check(results, ReconciliationCheckType.AiBagCountVsWarehouseCount).Verdict);
        Assert.Equal(ReconciliationVerdict.Pass,
            Check(results, ReconciliationCheckType.VehicleNetVsNominalRange).Verdict);
    }

    /// <summary>D-06 chưa duyệt dung sai thì chưa được kết luận PASS.</summary>
    [Fact]
    public void Unapproved_tolerance_yields_pending_not_pass()
    {
        var input = BaggedOutbound() with { Tolerance = new TolerancePolicy() };
        var results = ReconciliationRules.Evaluate(input);

        Assert.Equal(ReconciliationVerdict.Pending,
            Check(results, ReconciliationCheckType.VehicleNetVsIotMass).Verdict);
        Assert.Equal(ReconciliationVerdict.Pending,
            Check(results, ReconciliationCheckType.AiBagCountVsWarehouseCount).Verdict);
        Assert.Equal(ReconciliationVerdict.Pending,
            Check(results, ReconciliationCheckType.VehicleNetVsNominalRange).Verdict);
    }

    /// <summary>
    /// B-011: IoT quy đổi từ số bao không phải phép đo độc lập,
    /// nên không dùng để xác nhận cân xe.
    /// </summary>
    [Fact]
    public void Iot_derived_from_count_is_missing_data_not_pass()
    {
        var input = BaggedOutbound() with { IotMassOrigin = MassOrigin.DerivedFromCount };
        var check = Check(
            ReconciliationRules.Evaluate(input), ReconciliationCheckType.VehicleNetVsIotMass);

        Assert.Equal(ReconciliationVerdict.MissingData, check.Verdict);
        Assert.Contains("DERIVED_FROM_COUNT", check.Detail);
    }

    /// <summary>Cơ sở khối lượng khác nhau thì thiếu dữ liệu, không trừ hằng số cho khớp.</summary>
    [Fact]
    public void Different_mass_basis_yields_missing_data()
    {
        var input = BaggedOutbound() with { IotMassBasis = MassBasis.ProductNet };
        var check = Check(
            ReconciliationRules.Evaluate(input), ReconciliationCheckType.VehicleNetVsIotMass);

        Assert.Equal(ReconciliationVerdict.MissingData, check.Verdict);
    }

    [Fact]
    public void Unknown_mass_basis_yields_missing_data()
    {
        var input = BaggedOutbound() with
        {
            VehicleNetBasis = MassBasis.Unknown,
            IotMassBasis = MassBasis.Unknown
        };

        Assert.Equal(ReconciliationVerdict.MissingData,
            Check(ReconciliationRules.Evaluate(input),
                ReconciliationCheckType.VehicleNetVsIotMass).Verdict);
    }

    [Fact]
    public void Vehicle_iot_difference_beyond_tolerance_is_mismatch()
    {
        var input = BaggedOutbound() with { IotMassKg = 29_000m };
        var check = Check(
            ReconciliationRules.Evaluate(input), ReconciliationCheckType.VehicleNetVsIotMass);

        Assert.Equal(ReconciliationVerdict.Mismatch, check.Verdict);
        Assert.Equal(1_050m, check.Difference);
    }

    /// <summary>
    /// S04 tr.29: BOM chỉ có khối lượng danh nghĩa thì chưa dựng được khoảng dung sai.
    /// Không tự áp ±20 kg cho mọi loại hàng.
    /// </summary>
    [Fact]
    public void Bom_without_min_max_yields_missing_data()
    {
        var input = BaggedOutbound() with { MinKgPerUnit = null, MaxKgPerUnit = null };
        var check = Check(
            ReconciliationRules.Evaluate(input), ReconciliationCheckType.VehicleNetVsNominalRange);

        Assert.Equal(ReconciliationVerdict.MissingData, check.Verdict);
        Assert.Contains("min/max", check.Detail);
    }

    /// <summary>S03 tr.16: lower = min × ref - margin, upper = max × ref + margin.</summary>
    [Fact]
    public void Nominal_range_uses_documented_formula()
    {
        var check = Check(
            ReconciliationRules.Evaluate(BaggedOutbound()),
            ReconciliationCheckType.VehicleNetVsNominalRange);

        Assert.Equal(49.5m * 600m - 20m, check.LowerBound);
        Assert.Equal(50.5m * 600m + 20m, check.UpperBound);
    }

    [Fact]
    public void Vehicle_net_outside_nominal_range_is_mismatch()
    {
        var input = BaggedOutbound() with { VehicleNetKg = 35_000m };
        var check = Check(
            ReconciliationRules.Evaluate(input), ReconciliationCheckType.VehicleNetVsNominalRange);

        Assert.Equal(ReconciliationVerdict.Mismatch, check.Verdict);
    }

    /// <summary>Thiếu số đếm AI thì MissingData, KHÔNG gán bằng 0 bao.</summary>
    [Fact]
    public void Missing_ai_bag_count_is_missing_data_not_zero()
    {
        var input = BaggedOutbound() with { AiBagCount = null };
        var check = Check(
            ReconciliationRules.Evaluate(input), ReconciliationCheckType.AiBagCountVsWarehouseCount);

        Assert.Equal(ReconciliationVerdict.MissingData, check.Verdict);
        Assert.Contains("Không gán bằng 0", check.Detail);
    }

    /// <summary>NotApplicable chỉ dùng khi profile nói không áp dụng, không dùng để che mất kết nối.</summary>
    [Fact]
    public void Profile_without_iot_marks_check_not_applicable()
    {
        var input = BaggedOutbound() with { IotApplicable = false };
        var check = Check(
            ReconciliationRules.Evaluate(input), ReconciliationCheckType.VehicleNetVsIotMass);

        Assert.Equal(ReconciliationVerdict.NotApplicable, check.Verdict);
    }

    [Fact]
    public void Profile_without_bag_counting_marks_check_not_applicable()
    {
        var input = BaggedOutbound() with { BagCountApplicable = false };

        Assert.Equal(ReconciliationVerdict.NotApplicable,
            Check(ReconciliationRules.Evaluate(input),
                ReconciliationCheckType.AiBagCountVsWarehouseCount).Verdict);
    }

    /// <summary>R-038: giao một phần là hợp lệ, không mặc định phải bằng toàn bộ đơn hàng.</summary>
    [Fact]
    public void Partial_delivery_against_plan_still_passes()
    {
        var input = BaggedOutbound() with { WarehouseConfirmedQuantity = 400m };
        var check = Check(
            ReconciliationRules.Evaluate(input), ReconciliationCheckType.TripActualVsSourcePlan);

        Assert.Equal(ReconciliationVerdict.Pass, check.Verdict);
        Assert.Equal(-200m, check.Difference);
    }

    /// <summary>
    /// 02_Quy_tac mục 6: kết quả đếm bao nhân quy cách là "kg quy đổi",
    /// không phải kg đã cân.
    /// </summary>
    [Fact]
    public void Bag_count_converts_to_nominal_kg()
    {
        Assert.Equal(30_000m, ReconciliationRules.ConvertBagCountToNominalKg(600, 50m));
    }

    [Fact]
    public void Conversion_returns_null_without_bom_nominal_weight()
    {
        Assert.Null(ReconciliationRules.ConvertBagCountToNominalKg(600, null));
        Assert.Null(ReconciliationRules.ConvertBagCountToNominalKg(null, 50m));
    }

    /// <summary>
    /// Ví dụ giả trong 02_Quy_tac mục 6: cân 30.050, IoT 30.040, 600 bao × 50 kg danh nghĩa.
    /// Chưa có dung sai đã duyệt thì chưa kết luận PASS.
    /// </summary>
    [Fact]
    public void Document_example_without_approved_tolerance_is_not_pass()
    {
        var input = BaggedOutbound() with
        {
            Tolerance = new TolerancePolicy(),
            MinKgPerUnit = null,
            MaxKgPerUnit = null
        };

        var results = ReconciliationRules.Evaluate(input);

        Assert.DoesNotContain(results.Where(r =>
                r.CheckType != ReconciliationCheckType.TripActualVsSourcePlan),
            r => r.Verdict == ReconciliationVerdict.Pass);
    }
}
