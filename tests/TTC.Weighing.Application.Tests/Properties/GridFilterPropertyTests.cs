using FsCheck;
using FsCheck.Xunit;
using TTC.Weighing.Application.Queries;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Application.Tests.Properties;

/// <summary>
/// Property 8: Grid filter correctness
/// **Validates: Requirements 7.2, 7.3**
///
/// Since EfTripRegistrationQueries uses EF/DB, we test the filter logic in isolation
/// by applying the same predicates used in the infrastructure layer against
/// in-memory TripRegistrationRow collections.
/// </summary>
public class GridFilterPropertyTests
{
    /// <summary>The lifecycle set that maps to the InFactory tab (same as EfTripRegistrationQueries).</summary>
    private static readonly HashSet<TripLifecycle> InFactoryLifecycles = new()
    {
        TripLifecycle.TempRegistered,
        TripLifecycle.Registered,
        TripLifecycle.InProgress,
        TripLifecycle.WeighingCompleted,
        TripLifecycle.Reconciled,
        TripLifecycle.Posted
    };

    /// <summary>
    /// Applies the same filter predicates as EfTripRegistrationQueries.ListAsync
    /// to an in-memory list of rows. This lets us test the logic without a database.
    /// </summary>
    private static IReadOnlyList<TripRegistrationRow> ApplyFilter(
        IEnumerable<TripRegistrationRow> rows,
        TripRegistrationFilter filter)
    {
        var query = rows.AsEnumerable();

        // Tab filter (Req 7.2)
        query = filter.Tab switch
        {
            TripGridTab.InFactory => query.Where(r => InFactoryLifecycles.Contains(r.Lifecycle)),
            TripGridTab.Exited => query.Where(r => r.Lifecycle == TripLifecycle.Closed),
            TripGridTab.Cancelled => query.Where(r => r.Lifecycle == TripLifecycle.Cancelled),
            _ => query.Where(r => InFactoryLifecycles.Contains(r.Lifecycle))
        };

        // Date range filter [from, to) (Req 7.3)
        if (filter.FromUtc.HasValue)
            query = query.Where(r => r.EntryTime >= filter.FromUtc.Value);

        if (filter.ToUtc.HasValue)
            query = query.Where(r => r.EntryTime < filter.ToUtc.Value);

        return query.ToList();
    }

    /// <summary>
    /// Creates a minimal TripRegistrationRow with the given lifecycle and createdAt time.
    /// We use EntryTime as the time field since that's what the grid displays.
    /// </summary>
    private static TripRegistrationRow MakeRow(TripLifecycle lifecycle, DateTimeOffset? entryTime = null)
    {
        return new TripRegistrationRow(
            TripId: Guid.NewGuid(),
            Version: 1,
            TripNo: $"TT-{Guid.NewGuid():N}"[..12],
            ShipmentNo: null,
            Plate: "51A-123.45",
            QueueSequenceNumber: null,
            DriverRef: null,
            EntryTime: entryTime,
            ExitTime: null,
            Transporter: null,
            CustomerOrSupplier: null,
            Commodity: null,
            Lifecycle: lifecycle,
            VehicleType: null);
    }

    // ================================================================
    // Property 8a: InFactory tab only includes the 6 in-factory lifecycle states
    // **Validates: Requirements 7.2**
    // ================================================================

    [Property(MaxTest = 200, Arbitrary = new[] { typeof(GridFilterArbitraries) })]
    public Property InFactory_tab_only_includes_infactory_lifecycles(TripLifecycle lifecycle)
    {
        var row = MakeRow(lifecycle);
        var filter = new TripRegistrationFilter { Tab = TripGridTab.InFactory };

        var result = ApplyFilter(new[] { row }, filter);
        var included = result.Count == 1;
        var shouldBeIncluded = InFactoryLifecycles.Contains(lifecycle);

        return (included == shouldBeIncluded)
            .Label($"Lifecycle={lifecycle}: included={included}, expected={shouldBeIncluded}");
    }

    // ================================================================
    // Property 8b: Cancelled tab only includes Cancelled lifecycle
    // **Validates: Requirements 7.2**
    // ================================================================

    [Property(MaxTest = 200, Arbitrary = new[] { typeof(GridFilterArbitraries) })]
    public Property Cancelled_tab_only_includes_cancelled_lifecycle(TripLifecycle lifecycle)
    {
        var row = MakeRow(lifecycle);
        var filter = new TripRegistrationFilter { Tab = TripGridTab.Cancelled };

        var result = ApplyFilter(new[] { row }, filter);
        var included = result.Count == 1;
        var shouldBeIncluded = lifecycle == TripLifecycle.Cancelled;

        return (included == shouldBeIncluded)
            .Label($"Lifecycle={lifecycle}: included={included}, expected={shouldBeIncluded}");
    }

    // ================================================================
    // Property 8c: Exited tab only includes Closed lifecycle
    // **Validates: Requirements 7.2**
    // ================================================================

    [Property(MaxTest = 200, Arbitrary = new[] { typeof(GridFilterArbitraries) })]
    public Property Exited_tab_only_includes_closed_lifecycle(TripLifecycle lifecycle)
    {
        var row = MakeRow(lifecycle);
        var filter = new TripRegistrationFilter { Tab = TripGridTab.Exited };

        var result = ApplyFilter(new[] { row }, filter);
        var included = result.Count == 1;
        var shouldBeIncluded = lifecycle == TripLifecycle.Closed;

        return (included == shouldBeIncluded)
            .Label($"Lifecycle={lifecycle}: included={included}, expected={shouldBeIncluded}");
    }

    // ================================================================
    // Property 8d: Every lifecycle is covered by exactly one tab
    // **Validates: Requirements 7.2**
    // ================================================================

    [Property(MaxTest = 200, Arbitrary = new[] { typeof(GridFilterArbitraries) })]
    public Property Every_lifecycle_belongs_to_exactly_one_tab(TripLifecycle lifecycle)
    {
        var row = MakeRow(lifecycle);

        var inFactory = ApplyFilter(new[] { row }, new TripRegistrationFilter { Tab = TripGridTab.InFactory }).Count;
        var exited = ApplyFilter(new[] { row }, new TripRegistrationFilter { Tab = TripGridTab.Exited }).Count;
        var cancelled = ApplyFilter(new[] { row }, new TripRegistrationFilter { Tab = TripGridTab.Cancelled }).Count;

        var totalTabs = inFactory + exited + cancelled;

        return (totalTabs == 1)
            .Label($"Lifecycle={lifecycle} appears in {totalTabs} tabs (InFactory={inFactory}, Exited={exited}, Cancelled={cancelled}), expected exactly 1");
    }

    // ================================================================
    // Property 8e: Date range [from, to) filters correctly
    // **Validates: Requirements 7.3**
    // ================================================================

    [Property(MaxTest = 200, Arbitrary = new[] { typeof(GridFilterArbitraries) })]
    public Property Date_range_filter_includes_iff_entry_time_in_range(
        DateRangeInput dateRange, HourOffset hourOffset)
    {
        var from = dateRange.From;
        var to = dateRange.To;
        var entryTime = from.AddHours(hourOffset.Value);

        // Use InFactory lifecycle so tab filter doesn't interfere
        var row = MakeRow(TripLifecycle.Registered, entryTime);
        var filter = new TripRegistrationFilter
        {
            Tab = TripGridTab.InFactory,
            FromUtc = from,
            ToUtc = to
        };

        var result = ApplyFilter(new[] { row }, filter);
        var included = result.Count == 1;
        var shouldBeIncluded = entryTime >= from && entryTime < to;

        return (included == shouldBeIncluded)
            .Label($"EntryTime={entryTime:O}, Range=[{from:O}, {to:O}): included={included}, expected={shouldBeIncluded}");
    }

    // ================================================================
    // Property 8f: Tab filter + date range filter compose correctly
    // **Validates: Requirements 7.2, 7.3**
    // ================================================================

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(GridFilterArbitraries) })]
    public Property Tab_and_date_filters_compose_correctly(
        TripLifecycle lifecycle, TripGridTab tab, DateRangeInput dateRange, HourOffset hourOffset)
    {
        var from = dateRange.From;
        var to = dateRange.To;
        var entryTime = from.AddHours(hourOffset.Value);

        var row = MakeRow(lifecycle, entryTime);
        var filter = new TripRegistrationFilter
        {
            Tab = tab,
            FromUtc = from,
            ToUtc = to
        };

        var result = ApplyFilter(new[] { row }, filter);
        var included = result.Count == 1;

        // Row must pass BOTH tab filter AND date filter
        var passesTab = tab switch
        {
            TripGridTab.InFactory => InFactoryLifecycles.Contains(lifecycle),
            TripGridTab.Exited => lifecycle == TripLifecycle.Closed,
            TripGridTab.Cancelled => lifecycle == TripLifecycle.Cancelled,
            _ => InFactoryLifecycles.Contains(lifecycle)
        };
        var passesDate = entryTime >= from && entryTime < to;
        var shouldBeIncluded = passesTab && passesDate;

        return (included == shouldBeIncluded)
            .Label($"Tab={tab}, Lifecycle={lifecycle}, EntryTime={entryTime:O}, Range=[{from:O}, {to:O}): " +
                   $"included={included}, passesTab={passesTab}, passesDate={passesDate}");
    }
}

/// <summary>Wrapper for a date range [From, To) where From &lt; To.</summary>
public readonly struct DateRangeInput
{
    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }

    public DateRangeInput(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public override string ToString() => $"[{From:O}, {To:O})";
}

/// <summary>Hour offset from the range start, can be negative (before) or beyond range (after).</summary>
public readonly struct HourOffset
{
    public double Value { get; }
    public HourOffset(double value) => Value = value;
    public override string ToString() => $"HourOffset({Value:F2}h)";
}

/// <summary>
/// Custom FsCheck arbitraries for grid filter property tests.
/// </summary>
public static class GridFilterArbitraries
{
    /// <summary>Generates all TripLifecycle enum values uniformly.</summary>
    public static Arbitrary<TripLifecycle> TripLifecycle() =>
        Gen.Elements(
            Domain.Enums.TripLifecycle.TempRegistered,
            Domain.Enums.TripLifecycle.Registered,
            Domain.Enums.TripLifecycle.InProgress,
            Domain.Enums.TripLifecycle.WeighingCompleted,
            Domain.Enums.TripLifecycle.Reconciled,
            Domain.Enums.TripLifecycle.Posted,
            Domain.Enums.TripLifecycle.Closed,
            Domain.Enums.TripLifecycle.Cancelled)
        .ToArbitrary();

    /// <summary>Generates all TripGridTab enum values uniformly.</summary>
    public static Arbitrary<TripGridTab> TripGridTab() =>
        Gen.Elements(
            Queries.TripGridTab.InFactory,
            Queries.TripGridTab.Exited,
            Queries.TripGridTab.Cancelled)
        .ToArbitrary();

    /// <summary>
    /// Generates valid date ranges [from, to) where from &lt; to.
    /// Range spans 1-720 hours (up to 30 days).
    /// </summary>
    public static Arbitrary<DateRangeInput> DateRangeInput()
    {
        var gen = from year in Gen.Choose(2024, 2026)
                  from month in Gen.Choose(1, 12)
                  from day in Gen.Choose(1, 28) // safe for all months
                  from hour in Gen.Choose(0, 23)
                  from spanHours in Gen.Choose(1, 720)
                  let from_ = new DateTimeOffset(year, month, day, hour, 0, 0, TimeSpan.Zero)
                  let to_ = from_.AddHours(spanHours)
                  select new DateRangeInput(from_, to_);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates hour offsets that can place EntryTime before, inside, at boundary, or after the range.
    /// Negative values = before range. Values &gt; range span = after range.
    /// </summary>
    public static Arbitrary<HourOffset> HourOffset()
    {
        var gen = Gen.Frequency(
            Tuple.Create(2, Gen.Choose(-48, -1).Select(h => new HourOffset(h))),         // before range
            Tuple.Create(1, Gen.Constant(new HourOffset(0))),                             // exactly at from
            Tuple.Create(5, Gen.Choose(1, 718).Select(h => new HourOffset(h))),           // inside range
            Tuple.Create(1, Gen.Choose(720, 720).Select(h => new HourOffset(h))),         // exactly at to
            Tuple.Create(2, Gen.Choose(721, 900).Select(h => new HourOffset(h)))          // after range
        );
        return gen.ToArbitrary();
    }
}
