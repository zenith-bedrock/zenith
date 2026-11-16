using System.Numerics;

namespace Zenith.Diagnostics;

/// <summary>
/// The fixed bucket layout every timing's histogram shares: values below 4 get their own bucket, and every power of two above that
/// is split into four equal steps, so a bucket is at most 25% wide and the same layout covers nanoseconds to hours. Recording is one
/// <c>Interlocked.Increment</c> on a preallocated slot; percentiles are derived on the reader side only.
/// </summary>
public static class DurationHistogram
{
    public const int BucketCount = 256;

    /// <summary>The bucket of <see cref="long.MaxValue"/>; the buckets above it are never used.</summary>
    public const int LastReachableBucket = 247;

    public static int BucketOf(long value)
    {
        if (value < 4) return value < 0 ? 0 : (int)value;
        var msb = 63 - BitOperations.LeadingZeroCount((ulong)value);
        return (msb - 1) * 4 + (int)((value >> (msb - 2)) & 3);
    }

    /// <summary>The smallest value that lands in <paramref name="bucket"/>.</summary>
    public static long LowerBound(int bucket)
    {
        if (bucket < 4) return bucket;
        var msb = bucket / 4 + 1;
        return (long)(4 + bucket % 4) << (msb - 2);
    }

    /// <summary>The largest value that lands in <paramref name="bucket"/>; a percentile reports this so it never understates.</summary>
    public static long UpperBound(int bucket) => bucket >= LastReachableBucket ? long.MaxValue : LowerBound(bucket + 1) - 1;

    /// <summary>
    /// The value at quantile <paramref name="quantile"/> (0..1] of the <paramref name="counts"/> slice, or 0 when it is empty:
    /// the upper bound of the bucket holding the observation with that rank.
    /// </summary>
    public static long Percentile(ReadOnlySpan<long> counts, double quantile)
    {
        long total = 0;
        foreach (var count in counts) total += count;
        if (total == 0) return 0;
        var rank = Math.Max(1L, (long)Math.Ceiling(total * Math.Clamp(quantile, 0d, 1d)));
        long seen = 0;
        for (var bucket = 0; bucket < counts.Length; bucket++)
        {
            seen += counts[bucket];
            if (seen >= rank) return UpperBound(bucket);
        }

        return UpperBound(counts.Length - 1);
    }

    /// <summary>The upper bound of the highest non-empty bucket, or 0 when there is none.</summary>
    public static long MaximumBound(ReadOnlySpan<long> counts)
    {
        for (var bucket = counts.Length - 1; bucket >= 0; bucket--)
            if (counts[bucket] != 0) return UpperBound(bucket);
        return 0;
    }
}
