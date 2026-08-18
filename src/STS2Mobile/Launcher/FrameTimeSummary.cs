using System;
using System.Collections.Generic;
using System.Linq;

namespace STS2Mobile.Launcher;

// Pure frame-interval aggregation shared by the debug metric-validation probe
// and the later sanitized performance harness. Inputs are monotonic intervals,
// never wall-clock timestamps or user-derived fields.
internal readonly struct FrameTimeSummary
{
    public int Count { get; }
    public long P50Usec { get; }
    public long P95Usec { get; }
    public long P99Usec { get; }
    public long MaxUsec { get; }
    public long FrameBudgetUsec { get; }
    public int Over1XBudget { get; }
    public int Over2XBudget { get; }
    public int Over3XBudget { get; }
    public int MaxConsecutiveOver2X { get; }
    public int Over50Ms { get; }
    public int Over100Ms { get; }
    public int Over250Ms { get; }

    private FrameTimeSummary(
        int count,
        long p50Usec,
        long p95Usec,
        long p99Usec,
        long maxUsec,
        long frameBudgetUsec,
        int over1XBudget,
        int over2XBudget,
        int over3XBudget,
        int maxConsecutiveOver2X,
        int over50Ms,
        int over100Ms,
        int over250Ms
    )
    {
        Count = count;
        P50Usec = p50Usec;
        P95Usec = p95Usec;
        P99Usec = p99Usec;
        MaxUsec = maxUsec;
        FrameBudgetUsec = frameBudgetUsec;
        Over1XBudget = over1XBudget;
        Over2XBudget = over2XBudget;
        Over3XBudget = over3XBudget;
        MaxConsecutiveOver2X = maxConsecutiveOver2X;
        Over50Ms = over50Ms;
        Over100Ms = over100Ms;
        Over250Ms = over250Ms;
    }

    public static FrameTimeSummary Create(
        IReadOnlyCollection<long> intervalsUsec,
        long frameBudgetUsec = 16_667
    )
    {
        if (intervalsUsec == null)
            throw new ArgumentNullException(nameof(intervalsUsec));
        if (frameBudgetUsec <= 0)
            throw new ArgumentOutOfRangeException(nameof(frameBudgetUsec));

        var sequence = intervalsUsec.Where(value => value > 0).ToArray();
        var sorted = sequence.OrderBy(value => value).ToArray();
        if (sorted.Length == 0)
            throw new ArgumentException(
                "at least one positive interval is required",
                nameof(intervalsUsec)
            );

        return new FrameTimeSummary(
            sorted.Length,
            Percentile(sorted, 0.50),
            Percentile(sorted, 0.95),
            Percentile(sorted, 0.99),
            sorted[^1],
            frameBudgetUsec,
            sequence.Count(value => value > frameBudgetUsec),
            sequence.Count(value => value > frameBudgetUsec * 2),
            sequence.Count(value => value > frameBudgetUsec * 3),
            MaxConsecutiveOver(sequence, frameBudgetUsec * 2),
            sorted.Count(value => value > 50_000),
            sorted.Count(value => value > 100_000),
            sorted.Count(value => value > 250_000)
        );
    }

    private static int MaxConsecutiveOver(long[] sequence, long threshold)
    {
        int current = 0;
        int maximum = 0;
        foreach (var value in sequence)
        {
            if (value > threshold)
            {
                current++;
                maximum = Math.Max(maximum, current);
            }
            else
            {
                current = 0;
            }
        }

        return maximum;
    }

    private static long Percentile(long[] sorted, double percentile)
    {
        int nearestRank = Math.Max(1, (int)Math.Ceiling(percentile * sorted.Length));
        return sorted[nearestRank - 1];
    }
}
