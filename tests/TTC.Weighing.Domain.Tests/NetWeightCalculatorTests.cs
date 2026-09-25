using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Domain.Rules;

namespace TTC.Weighing.Domain.Tests;

public class NetWeightCalculatorTests
{
    [Fact]
    public void Inbound_first_weight_is_gross_second_is_tare()
    {
        var result = NetWeightCalculator.Calculate(ProfileDirection.Inbound, 30_050m, 12_000m);

        Assert.Equal(18_050m, result.VehicleNetKg);
        Assert.False(result.RequiresExceptionHandling);
    }

    [Fact]
    public void Outbound_first_weight_is_tare_second_is_gross()
    {
        var result = NetWeightCalculator.Calculate(ProfileDirection.Outbound, 12_000m, 30_050m);

        Assert.Equal(18_050m, result.VehicleNetKg);
    }

    /// <summary>02_Quy_tac 4.1: net âm là ngoại lệ, KHÔNG được chuyển thành dương.</summary>
    [Fact]
    public void Negative_net_is_preserved_and_flagged()
    {
        var result = NetWeightCalculator.Calculate(ProfileDirection.Inbound, 12_000m, 30_050m);

        Assert.Equal(-18_050m, result.VehicleNetKg);
        Assert.True(result.IsNegative);
        Assert.True(result.RequiresExceptionHandling);
    }

    /// <summary>Net bằng 0 chỉ hợp lệ trong profile được duyệt — luôn cần xử lý ngoại lệ (R-029).</summary>
    [Fact]
    public void Zero_net_is_flagged_for_exception_handling()
    {
        var result = NetWeightCalculator.Calculate(ProfileDirection.Outbound, 12_000m, 12_000m);

        Assert.Equal(0m, result.VehicleNetKg);
        Assert.True(result.IsZero);
        Assert.True(result.RequiresExceptionHandling);
    }

    /// <summary>Giữ đủ độ phân giải, không làm tròn trung gian (R-008).</summary>
    [Fact]
    public void Decimal_precision_is_preserved()
    {
        var result = NetWeightCalculator.Calculate(ProfileDirection.Inbound, 30_050.125m, 12_000.075m);

        Assert.Equal(18_050.050m, result.VehicleNetKg);
    }
}
