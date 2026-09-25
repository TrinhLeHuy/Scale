using NSubstitute;
using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.UseCases.Trips;

namespace TTC.Weighing.Application.Tests;

/// <summary>
/// Unit tests cho CheckPlanHandler — Req 1.1, 1.2, 1.3, 1.4, 1.5.
/// </summary>
public class CheckPlanHandlerTests
{
    private static readonly Guid SiteId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2025, 1, 15, 8, 0, 0, TimeSpan.Zero);

    private readonly IFrmPlanLookup _planLookup = Substitute.For<IFrmPlanLookup>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly CheckPlanHandler _sut;

    public CheckPlanHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _sut = new CheckPlanHandler(_planLookup, _clock, _user);
    }

    private void GrantPermission() =>
        _user.HasPermission(CheckPlanHandler.RequiredPermission, SiteId).Returns(true);

    private void DenyPermission() =>
        _user.HasPermission(CheckPlanHandler.RequiredPermission, SiteId).Returns(false);

    /// <summary>Req 1.1 — Quyền thiếu trip.register → trả lỗi, không gọi FRM.</summary>
    [Fact]
    public async Task Missing_permission_returns_error_without_calling_frm()
    {
        DenyPermission();
        var cmd = new CheckPlanCommand(SiteId, "51A-123.45", Guid.NewGuid().ToString());

        var result = await _sut.HandleAsync(cmd);

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Plans);
        Assert.NotNull(result.ErrorMessage);
        await _planLookup.DidNotReceiveWithAnyArgs()
            .LookupAsync(default, default!, default, default);
    }

    /// <summary>Req 1.2 — Lookup thành công trả danh sách kế hoạch.</summary>
    [Fact]
    public async Task Successful_lookup_returns_plan_list()
    {
        GrantPermission();
        var plan = new PlanEntry(
            Source: "FRM",
            ReferenceNo: "LD-2025-001",
            CommodityType: "Mía nguyên liệu",
            OwnerOrCustomer: "Nguyễn Văn A",
            WarehouseCode: "KHO-A",
            SugarcaneType: "Mía lùi",
            SugarcaneVariety: "K95-84",
            HarvestMethod: "Chặt tay",
            Station: "Trạm 3",
            CropSeason: "2024-2025");

        _planLookup.LookupAsync(SiteId, "51A-123.45", Now, Arg.Any<CancellationToken>())
            .Returns(new PlanLookupResult(true, [plan], null));

        var cmd = new CheckPlanCommand(SiteId, "51A-123.45", Guid.NewGuid().ToString());
        var result = await _sut.HandleAsync(cmd);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Plans);
        Assert.Equal("LD-2025-001", result.Plans[0].ReferenceNo);
        Assert.Equal("FRM", result.Plans[0].Source);
    }

    /// <summary>Req 1.3 — Lookup trả rỗng khi không tìm thấy kế hoạch.</summary>
    [Fact]
    public async Task No_matching_plan_returns_empty_list()
    {
        GrantPermission();
        _planLookup.LookupAsync(SiteId, "99Z-999.99", Now, Arg.Any<CancellationToken>())
            .Returns(new PlanLookupResult(true, [], null));

        var cmd = new CheckPlanCommand(SiteId, "99Z-999.99", Guid.NewGuid().ToString());
        var result = await _sut.HandleAsync(cmd);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Plans);
        Assert.Null(result.ErrorMessage);
    }

    /// <summary>Req 1.4 — FRM lỗi kết nối → IsSuccess = false với ErrorMessage.</summary>
    [Fact]
    public async Task Frm_connection_error_returns_failure_with_message()
    {
        GrantPermission();
        _planLookup.LookupAsync(SiteId, "51A-123.45", Now, Arg.Any<CancellationToken>())
            .Returns(new PlanLookupResult(false, [], "FRM: Connection timed out after 5000ms"));

        var cmd = new CheckPlanCommand(SiteId, "51A-123.45", Guid.NewGuid().ToString());
        var result = await _sut.HandleAsync(cmd);

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Plans);
        Assert.Contains("FRM", result.ErrorMessage!);
    }

    /// <summary>Req 1.5 — Nhiều kế hoạch khớp cùng biển số.</summary>
    [Fact]
    public async Task Multiple_matching_plans_returns_all()
    {
        GrantPermission();
        var frmPlan = new PlanEntry(
            "FRM", "LD-2025-010", "Mía nguyên liệu", "Trần Văn B", "KHO-A",
            "Mía lùi", "K95-84", "Chặt tay", "Trạm 1", "2024-2025");
        var erpPlan = new PlanEntry(
            "ERP", "SHIP-2025-020", "Phân bón", "Công ty XYZ", "KHO-B",
            null, null, null, null, null);

        _planLookup.LookupAsync(SiteId, "51A-500.00", Now, Arg.Any<CancellationToken>())
            .Returns(new PlanLookupResult(true, [frmPlan, erpPlan], null));

        var cmd = new CheckPlanCommand(SiteId, "51A-500.00", Guid.NewGuid().ToString());
        var result = await _sut.HandleAsync(cmd);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Plans.Count);
        Assert.Contains(result.Plans, p => p.Source == "FRM");
        Assert.Contains(result.Plans, p => p.Source == "ERP");
    }
}
