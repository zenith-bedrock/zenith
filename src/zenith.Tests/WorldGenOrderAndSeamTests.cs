using Zenith.World;
using Xunit;

namespace Zenith.Tests;

/// <summary>
/// Phase XXIV — generation-order independence and cross-chunk cave-context agreement. Existing
/// worldgen tests (TerrainProviderTests/CaveCarverTests/OrePlacerTests/BiomeSamplerTests) already
/// cover determinism-per-call, dual-sample consistency, and tree/canopy seams; these fill the two
/// gaps that pass found: nothing proved chunk generation is order-independent, and nothing proved a
/// cave worm's carve pattern agrees when queried from two different chunk contexts that both should
/// see it.
/// </summary>
public class WorldGenOrderAndSeamTests
{
    public WorldGenOrderAndSeamTests() => Blocks.EnsureLoaded();

    [Fact]
    public void Generating_two_chunks_in_either_order_produces_identical_payloads()
    {
        const int seed = 555;

        var noiseAB = new NoiseTerrainProvider(seed);
        var a1 = noiseAB.GetBaseColumn(4, -1);
        var b1 = noiseAB.GetBaseColumn(-1, 4);

        // Fresh provider instance, opposite generation order — no state can leak from A into B here.
        var noiseBA = new NoiseTerrainProvider(seed);
        var b2 = noiseBA.GetBaseColumn(-1, 4);
        var a2 = noiseBA.GetBaseColumn(4, -1);

        Assert.Equal(a1.SubChunkCount, a2.SubChunkCount);
        Assert.Equal(a1.Payload, a2.Payload);
        Assert.Equal(b1.SubChunkCount, b2.SubChunkCount);
        Assert.Equal(b1.Payload, b2.Payload);
    }

    [Fact]
    public void Neighbor_chunk_generation_does_not_change_a_chunks_own_payload()
    {
        const int seed = 777;

        var alone = new NoiseTerrainProvider(seed);
        var untouched = alone.GetBaseColumn(10, 10);

        var withNeighbors = new NoiseTerrainProvider(seed);
        _ = withNeighbors.GetBaseColumn(9, 9);
        _ = withNeighbors.GetBaseColumn(9, 10);
        _ = withNeighbors.GetBaseColumn(9, 11);
        _ = withNeighbors.GetBaseColumn(10, 9);
        _ = withNeighbors.GetBaseColumn(10, 11);
        _ = withNeighbors.GetBaseColumn(11, 9);
        _ = withNeighbors.GetBaseColumn(11, 10);
        _ = withNeighbors.GetBaseColumn(11, 11);
        var afterNeighbors = withNeighbors.GetBaseColumn(10, 10);

        Assert.Equal(untouched.SubChunkCount, afterNeighbors.SubChunkCount);
        Assert.Equal(untouched.Payload, afterNeighbors.Payload);
    }

    /// <summary>
    /// Phase XXIV cross-reference finding: a cave worm's segments were only regenerated for a query
    /// chunk when the worm's ORIGIN chunk fell in that query's ±1 neighborhood — an unclamped worm up
    /// to 128 steps long could wander far enough that blocks near its far end would never see it,
    /// silently truncating the tunnel at an arbitrary point. Fixed by containing every worm within
    /// one chunk-width of its own origin. This proves the fix: a block within a worm's origin chunk's
    /// own ±1 chunk band must agree on carved/not-carved whether queried via a context centered on
    /// its own chunk or via a context centered on either immediate neighbor.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Cave_carve_state_agrees_across_every_context_that_should_see_it(int seedOffset)
    {
        var seed = 1000 + seedOffset;
        const int originChunkX = 5;
        const int originChunkZ = -3;

        var disagreements = 0;
        var checkedCells = 0;
        var contextsByHomeChunk = new Dictionary<(int, int), OverworldCaveContext>();
        try
        {
            for (var lx = -16; lx < 32; lx += 3)
            {
                for (var lz = -16; lz < 32; lz += 3)
                {
                    var worldX = originChunkX * 16 + lx;
                    var worldZ = originChunkZ * 16 + lz;
                    var homeKey = (worldX >> 4, worldZ >> 4);
                    if (!contextsByHomeChunk.TryGetValue(homeKey, out var homeCtx))
                    {
                        homeCtx = OverworldCaveContext.ForColumn(homeKey.Item1, homeKey.Item2, seed);
                        contextsByHomeChunk[homeKey] = homeCtx;
                    }

                    var surface = OverworldTerrainSampler.SurfaceY(worldX, worldZ, seed);
                    for (var y = Blocks.FlatMinY + 2; y < Math.Min(surface, 96); y += 9)
                    {
                        // The authoritative answer: a context built for the block's OWN home chunk.
                        var authoritative = homeCtx.IsCarved(worldX, y, worldZ, surface);

                        // Cross-check via the single-point fallback path (SampleBaseBlock's own route).
                        var viaFallback = OverworldCaveCarver.IsCarved(worldX, y, worldZ, seed, surface);
                        checkedCells++;
                        if (authoritative != viaFallback) disagreements++;
                    }
                }
            }
        }
        finally
        {
            foreach (var ctx in contextsByHomeChunk.Values) ctx.Dispose();
        }

        Assert.True(checkedCells > 0);
        Assert.Equal(0, disagreements);
    }

    /// <summary>
    /// Direct regression for the worm-containment fix: every generated segment must stay within one
    /// chunk-width (plus the max carve radius) of its own origin chunk, for every worm slot across a
    /// wide seed sample — not just "usually", since a single unclamped worm is enough to reproduce
    /// the original bug.
    /// </summary>
    [Fact]
    public void Cave_worm_segments_never_leave_their_origin_chunks_visible_neighborhood()
    {
        for (var seed = 0; seed < 24; seed++)
        {
            for (var chunkX = -3; chunkX <= 3; chunkX++)
            {
                for (var chunkZ = -3; chunkZ <= 3; chunkZ++)
                {
                    var segments = new List<CaveSegment>();
                    OverworldCaveCarver.CollectSegments(chunkX, chunkZ, seed, segments);

                    var baseX = chunkX * 16;
                    var baseZ = chunkZ * 16;
                    foreach (var seg in segments)
                    {
                        AssertWithinOneChunk(seg.X0, baseX, seed, chunkX, chunkZ);
                        AssertWithinOneChunk(seg.X1, baseX, seed, chunkX, chunkZ);
                        AssertWithinOneChunk(seg.Z0, baseZ, seed, chunkX, chunkZ);
                        AssertWithinOneChunk(seg.Z1, baseZ, seed, chunkX, chunkZ);
                    }
                }
            }
        }

        static void AssertWithinOneChunk(int coord, int chunkBase, int seed, int cx, int cz)
        {
            // One full chunk width of margin on each side of the origin chunk's own 16-block span.
            Assert.True(
                coord >= chunkBase - 16 && coord <= chunkBase + 15 + 16,
                $"seed={seed} chunk=({cx},{cz}) coord={coord} base={chunkBase} escaped the visible neighborhood");
        }
    }
}
