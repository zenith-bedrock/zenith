using Zenith.Gameplay.Replication;
using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.WorldInteraction;

/// <summary>
/// Reacts to world changes (ADR §164): every cell written since last tick is fanned out to its six neighbours, each
/// neighbour is asked whether it lost what it hangs on (<see cref="BlockBehaviors"/>), and unsupported blocks break
/// and drop like any other. Cascades (a door's upper half after its lower half, a plant on a plant) continue through the
/// same queue, bounded per tick. Also drains <see cref="ScheduledTicks"/> that came due. Runs after block edits and gravity.
/// </summary>
sealed class BlockUpdateSystem : IGameSystem
{
    /// <summary>Changed cells fanned out per tick — a cascade longer than this continues next tick.</summary>
    public const int MaxChangedCellsPerTick = 256;

    /// <summary>Scheduled cells evaluated per tick.</summary>
    public const int MaxScheduledPerTick = 256;

    private const int MaxY = 320;

    private static readonly (int Dx, int Dy, int Dz)[] Neighbours =
    [
        (0, 0, 0), (0, 1, 0), (0, -1, 0), (1, 0, 0), (-1, 0, 0), (0, 0, 1), (0, 0, -1)
    ];

    private readonly World.World _world;
    private readonly PlayerManager _players;
    private readonly List<(int X, int Y, int Z, int BlockRuntimeId)> _updates = new();

    public BlockUpdateSystem(World.World world, PlayerManager players)
    {
        _world = world;
        _players = players;
    }

    public void Tick(GameClock clock, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        var updates = _updates;
        updates.Clear();

        var changed = 0;
        while (changed < MaxChangedCellsPerTick && _world.BlockUpdates.TryDequeue(out var x, out var y, out var z))
        {
            changed++;
            foreach (var (dx, dy, dz) in Neighbours)
                Evaluate(x + dx, y + dy, z + dz, online, updates);
        }

        var scheduled = 0;
        while (scheduled < MaxScheduledPerTick && _world.ScheduledTicks.TryPopDue(clock.CurrentTick, out var sx, out var sy, out var sz))
        {
            scheduled++;
            Evaluate(sx, sy, sz, online, updates);
        }

        if (updates.Count == 0) return;
        foreach (var peer in online)
        {
            if (peer.IsInGame) peer.Client.World.PublishUpdateBlocks(updates);
        }
    }

    private void Evaluate(
        int x, int y, int z,
        IReadOnlyList<global::Zenith.Player.Player> online,
        List<(int X, int Y, int Z, int BlockRuntimeId)> updates)
    {
        if (y < Blocks.FlatMinY || y > MaxY) return;
        var id = _world.GetBlock(x, y, z);
        if (id == World.World.AirRuntimeId || !BlockBehaviors.LostSupport(_world, x, y, z, id)) return;
        if (!_world.CanAcceptBlockWrite(x, y, z, World.World.AirRuntimeId) ||
            !_world.TrySetBlock(x, y, z, World.World.AirRuntimeId))
            return;

        updates.Add((x, y, z, World.World.AirRuntimeId));
        if (!_world.Rules.DoTileDrops || BlockBehaviors.IsDoorUpperHalf(id)) return;
        var drop = BlockLoot.DropOf(id);
        if (drop.Count > 0)
            _ = FloorDropFanout.TryDepositBatch(_world, _players, online, x, y, z,
                [new FloorDropFanout.DepositRequest(drop.Id, drop.Count)]);
    }
}
