using Xunit;

namespace Zenith.Diagnostics.Tests;

public sealed class DurationHistogramTests
{
    [Fact]
    public void Every_value_lands_in_a_bucket_whose_bounds_contain_it()
    {
        var random = new Random(7);
        var values = new List<long> { 0, 1, 2, 3, 4, 5, 7, 8, 9, 15, 16, 1000, 20_000_000, long.MaxValue };
        for (var i = 0; i < 5000; i++) values.Add(random.NextInt64(0, long.MaxValue));

        foreach (var value in values)
        {
            var bucket = DurationHistogram.BucketOf(value);
            Assert.InRange(bucket, 0, DurationHistogram.BucketCount - 1);
            Assert.InRange(value, DurationHistogram.LowerBound(bucket), DurationHistogram.UpperBound(bucket));
        }
    }

    [Fact]
    public void Buckets_are_contiguous_ordered_and_at_most_a_quarter_wide()
    {
        for (var bucket = 0; bucket < DurationHistogram.LastReachableBucket; bucket++)
        {
            Assert.Equal(DurationHistogram.LowerBound(bucket) + 1 + (DurationHistogram.UpperBound(bucket) - DurationHistogram.LowerBound(bucket)),
                DurationHistogram.LowerBound(bucket + 1));
            if (bucket >= 4)
                Assert.True(
                    (DurationHistogram.UpperBound(bucket) - DurationHistogram.LowerBound(bucket) + 1) <= DurationHistogram.LowerBound(bucket) / 4 + 1,
                    $"bucket {bucket} is wider than 25%");
        }
    }

    [Fact]
    public void Percentile_reports_the_bucket_holding_the_ranked_sample_and_never_understates()
    {
        var counts = new long[DurationHistogram.BucketCount];
        for (var i = 1; i <= 100; i++) counts[DurationHistogram.BucketOf(i * 1000)]++;

        var p50 = DurationHistogram.Percentile(counts, 0.50);
        var p99 = DurationHistogram.Percentile(counts, 0.99);

        Assert.InRange(p50, 50_000, 50_000 * 5 / 4 + 1);
        Assert.InRange(p99, 99_000, 99_000 * 5 / 4 + 1);
        Assert.True(DurationHistogram.Percentile(counts, 1.0) >= 100_000);
        Assert.Equal(0, DurationHistogram.Percentile(new long[DurationHistogram.BucketCount], 0.99));
    }

    [Fact]
    public void A_single_slow_sample_among_many_fast_ones_shows_in_the_tail_not_the_median()
    {
        var counts = new long[DurationHistogram.BucketCount];
        counts[DurationHistogram.BucketOf(100)] = 9990;
        counts[DurationHistogram.BucketOf(1_000_000)] = 10;

        Assert.True(DurationHistogram.Percentile(counts, 0.50) < 200);
        Assert.True(DurationHistogram.Percentile(counts, 0.9999) >= 1_000_000);
        Assert.True(DurationHistogram.MaximumBound(counts) >= 1_000_000);
    }
}
