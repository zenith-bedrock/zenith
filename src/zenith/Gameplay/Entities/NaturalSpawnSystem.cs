using Zenith.Gameplay.Runtime;
using Zenith.World;

namespace Zenith.Gameplay.Entities;

/// <summary>
/// Natural mob spawning: every category runs a spawn round on its own interval; a round spawns a weighted-random group
/// near each living in-game player, until the category's cap (per online player) is reached. Honours the
/// <c>doMobSpawning</c> game rule. Spawns go through <see cref="MobSpawner"/>, the same path as <c>/summon</c>.
///
/// Simplifications against vanilla, on purpose and visible: there is no light level (hostile entries use time of day,
/// and candidates are terrain-surface columns, so nothing spawns underground), no per-chunk mob-cap area (one global
/// cap scaled by player count), and no structure or water spawns yet.
/// </summary>
sealed class NaturalSpawnSystem(World.World world, MobSpawner spawner, SpawnTable table, Random? random = null) : IGameSystem
{
    /// <summary>A candidate is rejected closer than this to any player (vanilla keeps spawns out of sight at 24).</summary>
    internal const float MinPlayerDistance = 24f;
    /// <summary>...and is picked no farther than this from its player, inside the species' despawn radius.</summary>
    internal const float MaxPlayerDistance = 48f;
    private const float GroupScatter = 3f;
    private const int PlacementAttempts = 10;

    private readonly Random _random = random ?? new Random();
    private readonly List<SpawnEntry> _eligible = [];

    internal long SpawnRequests { get; private set; }

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (!world.Rules.DoMobSpawning) return;

        var players = online.Where(p => p.IsInGame && !p.IsDead).ToArray();
        if (players.Length == 0) return;

        foreach (var category in table.Categories)
        {
            if (clock.CurrentTick % (ulong)category.IntervalTicks != 0) continue;
            var cap = category.CapPerPlayer * players.Length;
            var alive = table.In(category).Select(e => e.Mob).Distinct().Sum(spawner.LiveCount);
            foreach (var player in players)
            {
                if (alive >= cap) break;
                alive += SpawnGroupNear(player, category, players, clock.WorldTime, cap - alive);
            }
        }
    }

    private int SpawnGroupNear(Player.Player player, SpawnCategory category, Player.Player[] players, int worldTime, int room)
    {
        if (!TryFindSurface(player, players, out var x, out var y, out var z)) return 0;
        var biome = world.SampleBiome((int)MathF.Floor(x), (int)MathF.Floor(z));

        _eligible.Clear();
        foreach (var entry in table.In(category))
            if (entry.AppliesTo(biome, worldTime) && spawner.IsRegistered(entry.Mob))
                _eligible.Add(entry);
        if (_eligible.Count == 0) return 0;

        var pick = PickWeighted(_eligible);
        var size = Math.Min(room, _random.Next(pick.GroupMin, pick.GroupMax + 1));
        var spawned = 0;
        for (var i = 0; i < size; i++)
        {
            var gx = x + (i == 0 ? 0f : (float)(_random.NextDouble() * 2 - 1) * GroupScatter);
            var gz = z + (i == 0 ? 0f : (float)(_random.NextDouble() * 2 - 1) * GroupScatter);
            var gy = y;
            if (i != 0 && !TryStandingY(gx, gz, out gy)) continue;
            if (!spawner.Request(pick.Mob, gx, gy, gz)) continue;
            SpawnRequests++;
            spawned++;
        }

        return spawned;
    }

    private bool TryFindSurface(Player.Player near, Player.Player[] all, out float x, out float y, out float z)
    {
        x = y = z = 0f;
        for (var attempt = 0; attempt < PlacementAttempts; attempt++)
        {
            var angle = _random.NextDouble() * Math.PI * 2;
            var distance = MinPlayerDistance + _random.NextDouble() * (MaxPlayerDistance - MinPlayerDistance);
            var cx = near.PositionX + (float)(Math.Cos(angle) * distance);
            var cz = near.PositionZ + (float)(Math.Sin(angle) * distance);
            if (!TryStandingY(cx, cz, out var cy)) continue;
            if (all.Any(p => Dist2(p.PositionX, p.PositionZ, cx, cz) < MinPlayerDistance * MinPlayerDistance)) continue;
            x = cx;
            y = cy;
            z = cz;
            return true;
        }

        return false;
    }

    /// <summary>Feet Y on the terrain surface at the column, when a ground mob can stand there (solid below, free body above).</summary>
    private bool TryStandingY(float x, float z, out float y)
    {
        var bx = (int)MathF.Floor(x);
        var bz = (int)MathF.Floor(z);
        var feet = world.SampleSpawnFeetY(bx, bz);
        y = feet;
        return Blocks.CanSupportGroundActor(world.GetBlock(bx, feet - 1, bz)) && world.IsSpawnFeetClear(bx, feet, bz);
    }

    private SpawnEntry PickWeighted(List<SpawnEntry> entries)
    {
        var roll = _random.Next(entries.Sum(e => e.Weight));
        foreach (var entry in entries)
        {
            roll -= entry.Weight;
            if (roll < 0) return entry;
        }

        return entries[^1];
    }

    private static float Dist2(float ax, float az, float bx, float bz)
    {
        var dx = ax - bx;
        var dz = az - bz;
        return dx * dx + dz * dz;
    }
}
