using Zenith.Rulebook;
using Zenith.Gameplay.Runtime;
using Zenith.World;

namespace Zenith.Gameplay.Entities;

/// <summary>
/// Natural mob spawning: every category runs a spawn round on its own interval; a round spawns a weighted-random group
/// near each living in-game player, until the category's cap (per online player) is reached. Honours the
/// <c>doMobSpawning</c> game rule. Spawns go through <see cref="MobSpawner"/>, the same path as <c>/summon</c>.
///
/// Light decides what may spawn where (<see cref="LightModel"/>): monsters want a cell at brightness 7 or less, so they appear at
/// night on the surface, in the dark all day under a roof and in caves; grazing animals want 7 or more and the grass they stand on.
/// Simplifications against vanilla, on purpose and visible: no per-chunk mob-cap area (one global cap scaled by player count),
/// no weather, and no structure or water spawns yet.
/// </summary>
[GameSystem("natural-spawn")]
sealed class NaturalSpawnSystem(World.World world, MobSpawner spawner, SpawnTable table, Random? random = null) : IGameSystem
{
    /// <summary>A candidate is rejected closer than this to any player (vanilla keeps spawns out of sight at 24).</summary>
    internal const float MinPlayerDistance = 24f;
    /// <summary>...and is picked no farther than this from its player, inside the species' despawn radius.</summary>
    internal const float MaxPlayerDistance = 48f;
    private const float GroupScatter = 3f;
    private const int PlacementAttempts = 10;

    /// <summary>A monster candidate looks underground (around the player's height) this often instead of at the surface.</summary>
    private const double CaveShare = 0.5;
    private const int CaveReachDown = 24;
    private const int CaveReachUp = 8;
    private const int CaveScanDepth = 12;

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
            if (category.Name == "monster" && world.Difficulty.Value == Difficulty.Peaceful) continue;
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
        if (!TryFindSpot(player, players, category.Name == "monster", out var x, out var y, out var z, out var underground)) return 0;
        var biome = world.SampleBiome((int)MathF.Floor(x), (int)MathF.Floor(z));
        var ground = Blocks.Catalog.States.FamilyOf(world.GetBlock((int)MathF.Floor(x), (int)MathF.Floor(y) - 1, (int)MathF.Floor(z)))?.Name;
        var brightness = -1;

        _eligible.Clear();
        foreach (var entry in table.In(category))
        {
            if (!entry.AppliesTo(biome, worldTime) || !entry.StandsOn(ground) || !spawner.IsRegistered(entry.Mob)) continue;
            if (entry.Light != SpawnLight.Any)
            {
                if (brightness < 0) brightness = LightModel.Brightness(world, (int)MathF.Floor(x), (int)MathF.Floor(y), (int)MathF.Floor(z), worldTime);
                if (!entry.LightOk(brightness)) continue;
            }

            _eligible.Add(entry);
        }

        if (_eligible.Count == 0) return 0;

        var pick = PickWeighted(_eligible);
        var size = Math.Min(room, _random.Next(pick.GroupMin, pick.GroupMax + 1));
        var spawned = 0;
        for (var i = 0; i < size; i++)
        {
            var gx = x + (i == 0 ? 0f : (float)(_random.NextDouble() * 2 - 1) * GroupScatter);
            var gz = z + (i == 0 ? 0f : (float)(_random.NextDouble() * 2 - 1) * GroupScatter);
            var gy = y;
            if (i != 0 && !(underground ? TryStandingNear(gx, gz, y, out gy) : TryStandingY(gx, gz, out gy))) continue;
            if (!spawner.Request(pick.Mob, gx, gy, gz)) continue;
            SpawnRequests++;
            spawned++;
        }

        return spawned;
    }

    private bool TryFindSpot(Player.Player near, Player.Player[] all, bool mayGoUnderground, out float x, out float y, out float z, out bool underground)
    {
        x = y = z = 0f;
        underground = false;
        for (var attempt = 0; attempt < PlacementAttempts; attempt++)
        {
            var angle = _random.NextDouble() * Math.PI * 2;
            var distance = MinPlayerDistance + _random.NextDouble() * (MaxPlayerDistance - MinPlayerDistance);
            var cx = near.PositionX + (float)(Math.Cos(angle) * distance);
            var cz = near.PositionZ + (float)(Math.Sin(angle) * distance);
            var cave = mayGoUnderground && _random.NextDouble() < CaveShare;
            float cy;
            if (cave)
            {
                var from = (int)MathF.Floor(near.PositionY) + _random.Next(-CaveReachDown, CaveReachUp + 1);
                if (!TryStandingNear(cx, cz, from, out cy, CaveScanDepth)) continue;
            }
            else if (!TryStandingY(cx, cz, out cy)) continue;

            if (all.Any(p => Dist2(p.PositionX, p.PositionZ, cx, cz) < MinPlayerDistance * MinPlayerDistance)) continue;
            x = cx;
            y = cy;
            z = cz;
            underground = cave;
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

    /// <summary>
    /// Feet Y of a standing spot in the column at or just below <paramref name="from"/> (or, with no depth given, within three cells of it
    /// either way): solid ground under two free cells, in a loaded column. Used for caves, where there is no surface to sample.
    /// </summary>
    private bool TryStandingNear(float x, float z, float from, out float y, int depth = 3)
    {
        var bx = (int)MathF.Floor(x);
        var bz = (int)MathF.Floor(z);
        var start = (int)MathF.Floor(from);
        for (var step = 0; step <= depth; step++)
            foreach (var feet in step == 0 ? [start] : depth == 3 ? new[] { start + step, start - step } : [start - step])
            {
                if (feet <= WorldColumn.MinY || feet >= WorldColumn.MaxY - 2) continue;
                if (!world.TryGetBlock(bx, feet - 1, bz, out var below) || !Blocks.CanSupportGroundActor(below)) continue;
                if (!world.TryGetBlock(bx, feet, bz, out var body) || !Blocks.CanOccupy(body) || Blocks.IsFluid(body)) continue;
                if (!world.TryGetBlock(bx, feet + 1, bz, out var head) || !Blocks.CanOccupy(head) || Blocks.IsFluid(head)) continue;
                y = feet;
                return true;
            }

        y = 0f;
        return false;
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
