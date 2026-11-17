using System.Collections.Concurrent;

namespace Zenith.World;

/// <summary>
/// Light as the server's gameplay needs it (plan 004 item 26), answered on demand from the blocks and never stored: the clients
/// compute their own lighting from the chunks, the server only asks "how bright is this cell" for spawning, growth and the sun.
/// <para/>
/// <b>Block light</b> comes from emitters (<see cref="Blocks.EmissionOf"/>) and loses one level for each cell it crosses (more through
/// water and leaves). <b>Sky light</b> is 15 in a cell that is open to the sky and spreads sideways the same way; the sun takes
/// <see cref="SkyDarkness"/> levels off it according to the time of day. Brightness is the larger of the two. Both are found by a
/// bounded search outward from the asked cell, so a query costs a few dozen cells in the open and a few thousand at worst in a cave.
/// Not modelled: weather, and dimensions other than the overworld.
/// </summary>
static class LightModel
{
    public const int Max = 15;

    private static readonly ConcurrentDictionary<object, int> FamilyFilters = new();

    private static readonly string[] ClearNames =
    [
        "glass", "slab", "stairs", "fence", "wall", "bars", "door", "trapdoor", "ladder", "rail", "carpet", "sign", "button",
        "lantern", "chain", "pane", "web", "scaffolding", "torch", "lever", "pressure_plate", "sapling", "flower", "head", "skull",
        "bed", "chest", "anvil", "campfire", "candle", "cake", "hopper", "cauldron", "flower_pot", "lightning_rod", "end_rod",
        "snow_layer", "vine", "lichen", "sea_pickle", "turtle_egg", "frogspawn", "amethyst_cluster", "amethyst_bud", "brewing_stand",
    ];

    private static readonly (int X, int Y, int Z)[] Steps = [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)];

    /// <summary>How much light the block takes from what passes through it: 0 clear, 15 opaque.</summary>
    public static int FilterOf(int runtimeId)
    {
        if (Blocks.IsAir(runtimeId) || Blocks.IsFire(runtimeId) || Blocks.IsPassableDecoration(runtimeId)) return 0;
        if (Blocks.IsFluid(runtimeId)) return 2;
        if (Blocks.Catalog.States.FamilyOf(runtimeId) is not { } family) return Max;
        return FamilyFilters.GetOrAdd(family, static key => Classify(((BlockFamily)key).Name));
    }

    private static int Classify(string name)
    {
        var bare = name.StartsWith("minecraft:", StringComparison.Ordinal) ? name[10..] : name;
        if (bare.Contains("leaves", StringComparison.Ordinal)) return 1;
        if (bare is "ice" or "frosted_ice") return 1;
        foreach (var clear in ClearNames)
            if (bare.Contains(clear, StringComparison.Ordinal)) return 0;
        return Max;
    }

    /// <summary>
    /// Levels the sun takes off sky light at <paramref name="worldTime"/> (a tick of the 24000 day): 0 by day, 11 in the middle of the night,
    /// ramping through dusk and dawn. Vanilla's celestial-angle curve, clear weather.
    /// </summary>
    public static int SkyDarkness(int worldTime)
    {
        var angle = (worldTime % 24000 + 24000) % 24000 / 24000f - 0.25f;
        if (angle < 0f) angle += 1f;
        var smoothed = 1f - (MathF.Cos(angle * MathF.PI) + 1f) / 2f;
        angle += (smoothed - angle) / 3f;
        var light = 1f - (MathF.Cos(angle * MathF.PI * 2f) * 2f + 0.5f);
        light = Math.Clamp(light, 0f, 1f);
        return (int)(light * 11f);
    }

    /// <summary>Brightness of a cell, 0 to 15: the larger of its block light and its sky light less what the time of day takes off.</summary>
    public static int Brightness(World world, int x, int y, int z, int worldTime) =>
        Math.Max(BlockLight(world, x, y, z), Math.Max(0, SkyLight(world, x, y, z) - SkyDarkness(worldTime)));

    public static int BlockLight(World world, int x, int y, int z) => Search(world, x, y, z, sky: false);

    /// <summary>Sky light of a cell with no time of day applied (15 when open to the sky).</summary>
    public static int SkyLight(World world, int x, int y, int z) => Search(world, x, y, z, sky: true);

    /// <summary>Whether nothing but clear air above the cell blocks the sky (water and leaves do not count as clear).</summary>
    public static bool SeesSky(World world, int x, int y, int z)
    {
        for (var up = y + 1; up <= WorldColumn.MaxY; up++)
        {
            if (!world.TryGetBlock(x, up, z, out var block)) return false;
            if (FilterOf(block) != 0) return false;
        }

        return true;
    }

    /// <summary>The sky level a cell itself gives off: 15 less what lies above it, 0 when anything opaque does.</summary>
    private static int SkySource(World world, int x, int y, int z)
    {
        var level = Max;
        for (var up = y + 1; up <= WorldColumn.MaxY; up++)
        {
            if (!world.TryGetBlock(x, up, z, out var block)) return 0;
            var filter = FilterOf(block);
            if (filter == 0) continue;
            level -= filter;
            if (level <= 0) return 0;
        }

        return level;
    }

    /// <summary>
    /// Searches outward from the cell in order of the light lost on the way, and returns the best (source level minus loss).
    /// Light reaches the asked cell through cells that are not opaque; an opaque emitter still counts as a source.
    /// </summary>
    private static int Search(World world, int qx, int qy, int qz, bool sky)
    {
        if (!world.TryGetBlock(qx, qy, qz, out var here)) return 0;
        var best = SourceLevel(world, qx, qy, qz, here, sky);
        if (best >= Max || FilterOf(here) >= Max) return best; // full already, or light does not live inside a solid block

        var buckets = new List<(int X, int Y, int Z)>?[Max];
        var costs = new Dictionary<(int, int, int), int> { [(qx, qy, qz)] = 0 };
        buckets[0] = [(qx, qy, qz)];

        for (var cost = 0; cost < Max; cost++)
        {
            var bucket = buckets[cost];
            if (bucket is null) continue;
            for (var i = 0; i < bucket.Count; i++)
            {
                var (cx, cy, cz) = bucket[i];
                if (costs[(cx, cy, cz)] != cost) continue;
                _ = world.TryGetBlock(cx, cy, cz, out var current);
                var passing = cost + Math.Max(1, FilterOf(current));
                if (passing >= Max || Max - passing <= best) continue;

                foreach (var (dx, dy, dz) in Steps)
                {
                    var next = (cx + dx, cy + dy, cz + dz);
                    if (costs.TryGetValue(next, out var known) && known <= passing) continue;
                    if (!world.TryGetBlock(next.Item1, next.Item2, next.Item3, out var block)) continue;

                    var level = SourceLevel(world, next.Item1, next.Item2, next.Item3, block, sky) - passing;
                    if (level > best) best = level;
                    if (FilterOf(block) >= Max) continue; // an opaque cell can give light but never pass it on
                    costs[next] = passing;
                    (buckets[passing] ??= []).Add(next);
                }
            }
        }

        return Math.Max(best, 0);
    }

    private static int SourceLevel(World world, int x, int y, int z, int block, bool sky) =>
        sky ? (FilterOf(block) >= Max ? 0 : SkySource(world, x, y, z)) : Blocks.EmissionOf(block);
}
