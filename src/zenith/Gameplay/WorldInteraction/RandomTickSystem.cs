using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.WorldInteraction.RandomTicks;
using Zenith.World;

namespace Zenith.Gameplay.WorldInteraction;

/// <summary>
/// Random ticks (ADR §169). Each tick, in every resident column somebody is looking at, every section that holds
/// tickable blocks gets <see cref="Rulebook.GameRules.RandomTickSpeed"/> random cells sampled; a cell whose block has a
/// behaviour runs it. A column whose <see cref="WorldColumn.RandomTickMask"/> is zero is skipped without a look, so a
/// world of stone, water and air costs one integer test per column. Chunks nobody views are asleep, and ticks never
/// load a chunk.
/// </summary>
sealed class RandomTickSystem : IGameSystem
{
    private readonly World.World _world;
    private readonly RandomTickBehaviors _behaviors;
    private readonly Random _random;
    private readonly RandomTickContext _context;
    private readonly List<(int X, int Y, int Z, int BlockRuntimeId)> _changes = new();
    private readonly Action<WorldColumn> _visit;
    private int _speed;

    public RandomTickSystem(World.World world, RandomTickBehaviors behaviors, Random? random = null)
    {
        behaviors.Validate();
        _world = world;
        _behaviors = behaviors;
        _random = random ?? new Random();
        _context = new RandomTickContext(world, _random, _changes);
        _visit = VisitColumn;
    }

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        _speed = _world.Rules.RandomTickSpeed;
        if (_speed <= 0 || online.Count == 0) return;

        _changes.Clear();
        _world.ForEachLoadedColumn(_visit);
        if (_changes.Count == 0) return;

        foreach (var peer in online)
            if (peer.IsInGame) peer.Client.World.PublishUpdateBlocks(_changes);
    }

    private void VisitColumn(WorldColumn column)
    {
        var mask = column.RandomTickMask;
        if (mask == 0 || !_world.ChunkResidency.HasViewers(column.Coord.X, column.Coord.Z)) return;

        var originX = column.Coord.X << 4;
        var originZ = column.Coord.Z << 4;
        while (mask != 0)
        {
            var sectionIndex = System.Numerics.BitOperations.TrailingZeroCount(mask);
            mask &= mask - 1;
            if (column.SectionAt(sectionIndex) is not { } section) continue;

            var originY = WorldColumn.MinY + (sectionIndex << 4);
            for (var n = 0; n < _speed; n++)
            {
                var cell = _random.Next(Section.Volume);
                var id = section.Get(cell);
                if (!_behaviors.TryGet(id, out var behavior)) continue;

                // IndexOf is (x << 8) | (z << 4) | y
                _context.Aim(originX + (cell >> 8), originY + (cell & 15), originZ + ((cell >> 4) & 15), id);
                behavior.OnRandomTick(_context);
            }
        }
    }
}
