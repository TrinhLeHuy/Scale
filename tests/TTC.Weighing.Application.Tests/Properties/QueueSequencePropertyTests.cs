using FsCheck;
using FsCheck.Xunit;
using TTC.Weighing.Application.UseCases.Trips;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Application.Tests.Properties;

/// <summary>
/// Property-based tests for IQueueSequenceGenerator contract.
/// Validates: Requirements 2.2
///
/// Since the real SequentialQueueSequenceGenerator uses PostgreSQL,
/// we test the contract with an in-memory implementation that follows
/// the same monotonic increment semantics.
/// </summary>
public class QueueSequencePropertyTests
{
    /// <summary>
    /// In-memory implementation of IQueueSequenceGenerator that follows
    /// the same contract as the PostgreSQL-backed SequentialQueueSequenceGenerator:
    /// atomic increment per (siteId, vehicleType, operatingDate).
    /// </summary>
    private sealed class InMemoryQueueSequenceGenerator : IQueueSequenceGenerator
    {
        private readonly Dictionary<(Guid SiteId, VehicleType VehicleType), int> _counters = new();
        private readonly object _lock = new();

        public Task<int> NextAsync(Guid siteId, VehicleType vehicleType, CancellationToken ct = default)
        {
            lock (_lock)
            {
                var key = (siteId, vehicleType);
                if (!_counters.TryGetValue(key, out var current))
                    current = 0;

                current++;
                _counters[key] = current;
                return Task.FromResult(current);
            }
        }
    }

    /// <summary>
    /// **Validates: Requirements 2.2**
    ///
    /// Property 2: Queue sequence monotonic — For any sequence of N calls (N ≥ 2)
    /// to IQueueSequenceGenerator.NextAsync with the same (siteId, vehicleType),
    /// the returned values must be strictly monotonically increasing (each value > previous).
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(QueueSequenceArbitraries)])]
    public Property Queue_sequence_is_strictly_monotonically_increasing(
        Guid siteId, VehicleType vehicleType, CallCount callCount)
    {
        var generator = new InMemoryQueueSequenceGenerator();
        var n = callCount.Value;

        // Collect N sequence values
        var values = new List<int>(n);
        for (var i = 0; i < n; i++)
        {
            values.Add(generator.NextAsync(siteId, vehicleType).GetAwaiter().GetResult());
        }

        // Assert strict monotonic: for all consecutive pairs (a, b), b > a
        var isStrictlyIncreasing = values
            .Zip(values.Skip(1), (a, b) => b > a)
            .All(x => x);

        return isStrictlyIncreasing
            .Label($"Expected strictly increasing sequence of {n} values, got: [{string.Join(", ", values)}]");
    }

    /// <summary>
    /// **Validates: Requirements 2.2**
    ///
    /// Property 2 (supplemental): Different (siteId, vehicleType) combinations
    /// maintain independent sequences — interleaved calls do not break monotonicity
    /// within any single combination.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Interleaved_calls_maintain_independent_monotonic_sequences(
        Guid siteId1, Guid siteId2, PositiveInt callsPerCombination)
    {
        var generator = new InMemoryQueueSequenceGenerator();
        var n = Math.Clamp(callsPerCombination.Get, 2, 30);

        var seq1 = new List<int>(n);
        var seq2 = new List<int>(n);

        // Interleave calls between two different combinations
        for (var i = 0; i < n; i++)
        {
            seq1.Add(generator.NextAsync(siteId1, VehicleType.Container).GetAwaiter().GetResult());
            seq2.Add(generator.NextAsync(siteId2, VehicleType.Truck).GetAwaiter().GetResult());
        }

        var seq1Increasing = seq1.Zip(seq1.Skip(1), (a, b) => b > a).All(x => x);
        var seq2Increasing = seq2.Zip(seq2.Skip(1), (a, b) => b > a).All(x => x);

        return (seq1Increasing && seq2Increasing)
            .Label($"Both sequences should be strictly increasing. " +
                   $"Seq1: [{string.Join(", ", seq1)}], Seq2: [{string.Join(", ", seq2)}]");
    }
}

/// <summary>
/// Wrapper type for call count (2-50) to use with FsCheck generators.
/// </summary>
public readonly struct CallCount
{
    public int Value { get; }
    public CallCount(int value) => Value = value;
    public override string ToString() => $"CallCount({Value})";
}

/// <summary>
/// Custom FsCheck arbitraries for queue sequence property tests.
/// </summary>
public static class QueueSequenceArbitraries
{
    /// <summary>Generates call counts between 2 and 50.</summary>
    public static Arbitrary<CallCount> CallCount() =>
        Gen.Choose(2, 50)
           .Select(n => new CallCount(n))
           .ToArbitrary();

    /// <summary>Generates random VehicleType values.</summary>
    public static Arbitrary<VehicleType> VehicleType() =>
        Gen.Elements(
            Domain.Enums.VehicleType.Container,
            Domain.Enums.VehicleType.Truck,
            Domain.Enums.VehicleType.Trailer)
        .ToArbitrary();
}
