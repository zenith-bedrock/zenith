using Zenith.Gameplay.Policy;
using Zenith.Gameplay.Replication;
using Zenith.Gameplay.WorldInteraction.Fire;
using Zenith.Gameplay.WorldInteraction.Fluids;
using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.WorldInteraction;

/// <summary>
/// Reacts to world changes (ADR §164): every cell written since last tick is fanned out to its six neighbours, each
/// neighbour is asked whether it can still stand (<see cref="BlockShapes.CanExist"/>) and whether its shape changed
/// (<see cref="BlockShapes.UpdateShape"/>); unsupported blocks break
/// and drop like any other. Cascades (a door's upper half after its lower half, a plant on a plant) continue through the
/// same queue, bounded per tick. Also drains <see cref="ScheduledTicks"/> that came due. Runs after block edits and gravity.
/// <para/>
/// Losing support and being reshaped by a neighbour are environment changes (cause <see cref="EnvironmentCause.SupportLoss"/>) and
/// are asked per cell: a protected block that has lost its support stays, with no drop, even if that leaves a torch on air. That is
/// deliberate: a protected cell changes only with bypass. Nothing replays it later; it is judged again the next time something
/// updates its neighbourhood.
/// </summary>
sealed class BlockUpdateSystem : IGameSystem
{
    /// <summary>Changed cells fanned out per tick — a cascade longer than this continues next tick.</summary>
    public const int MaxChangedCellsPerTick = 256;

    /// <summary>Scheduled cells evaluated per tick.</summary>
    public const int MaxScheduledPerTick = 256;

    private readonly World.World _world;
    private readonly PlayerManager _players;
    private readonly List<(int X, int Y, int Z, int BlockRuntimeId)> _updates = new();
    private readonly FluidFlow _water;
    private readonly FluidFlow _lava;
    private readonly FireSpread _fire;
    private readonly EnvironmentChangePolicies _environment;
    private readonly Random _random = new();
    private ulong _now;
    private IReadOnlyList<global::Zenith.Player.Player> _online = [];

    public BlockUpdateSystem(World.World world, PlayerManager players, EnvironmentChangePolicies environment)
    {
        _world = world;
        _players = players;
        _environment = environment;
        _water = new FluidFlow(world, FluidSpec.Water, _updates, DropWashedAway, environment);
        _lava = new FluidFlow(world, FluidSpec.Lava, _updates, DropWashedAway, environment);
        _fire = new FireSpread(world, _random, _updates, environment);
    }

    public void Tick(GameClock clock, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        var updates = _updates;
        updates.Clear();
        _now = clock.CurrentTick;
        _water.Now = _lava.Now = _fire.Now = _now;
        _online = online;

        var changed = 0;
        while (changed < MaxChangedCellsPerTick && _world.BlockUpdates.TryDequeue(out var x, out var y, out var z))
        {
            changed++;
            Evaluate(x, y, z, online, updates);
            foreach (var (dx, dy, dz) in BlockFaces.Neighbours)
                Evaluate(x + dx, y + dy, z + dz, online, updates);
        }

        var scheduled = 0;
        while (scheduled < MaxScheduledPerTick && _world.ScheduledTicks.TryPopDue(clock.CurrentTick, out var sx, out var sy, out var sz))
        {
            scheduled++;
            if (_world.TryGetBlock(sx, sy, sz, out var due) && Blocks.FluidOf(due) is var fluid and not FluidType.None)
                FlowOf(fluid).Tick(sx, sy, sz);
            else if (Blocks.IsFire(due))
            {
                if (_fire.Tick(sx, sy, sz)) _ = _world.ScheduledTicks.Schedule(sx, sy, sz, _now + (ulong)FireSpread.NextDelay(_random));
            }
            else
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
        // A cell in a chunk nobody has loaded is not ours to wake: it is re-evaluated when the chunk streams in and a neighbour changes.
        if (!_world.TryGetBlock(x, y, z, out var id) || id == World.World.AirRuntimeId) return;

        // A liquid reacts to a neighbour's change after its own delay, not now. Lava next to water sets at once.
        if (Blocks.FluidOf(id) is var fluid and not FluidType.None)
        {
            var flow = FlowOf(fluid);
            if (!flow.TryReact(x, y, z, id))
                _ = _world.ScheduledTicks.Schedule(x, y, z, _now + (ulong)flow.Spec.TickDelay);
            return;
        }

        // Fire lives on its own clock: any change at or beside it makes sure one is running.
        if (Blocks.IsFire(id))
        {
            _ = _world.ScheduledTicks.Schedule(x, y, z, _now + (ulong)FireSpread.NextDelay(_random));
            return;
        }

        if (!BlockShapes.CanExist(_world, x, y, z, id))
        {
            BreakUnsupported(x, y, z, id, online, updates);
            return;
        }

        // Neighbour-dependent shape (wall connections): rewrite in place when it changed. A reshape depends on what the
        // neighbours are, not on how they are shaped, so it does not wake them again.
        var shaped = BlockShapes.UpdateShape(_world, x, y, z, id);
        if (shaped != id && _environment.Allows(EnvironmentCause.SupportLoss, _now, x, y, z, id, shaped) &&
            _world.TrySetBlock(x, y, z, shaped, SetBlockFlags.None))
            updates.Add((x, y, z, shaped));
    }

    private FluidFlow FlowOf(FluidType fluid) => fluid == FluidType.Lava ? _lava : _water;

    private void BreakUnsupported(
        int x, int y, int z, int id,
        IReadOnlyList<global::Zenith.Player.Player> online,
        List<(int X, int Y, int Z, int BlockRuntimeId)> updates)
    {
        // Asked before the write and the drop: a vetoed block neither breaks nor spills.
        if (!_environment.Allows(EnvironmentCause.SupportLoss, _now, x, y, z, id, World.World.AirRuntimeId)) return;
        if (!_world.TrySetBlock(x, y, z, World.World.AirRuntimeId))
            return;

        updates.Add((x, y, z, World.World.AirRuntimeId));
        BlockSpill.Drop(_world, _players, online, x, y, z, id);
    }

    /// <summary>Water is about to take a cell that held a block: it drops what it would drop broken.</summary>
    private void DropWashedAway(int x, int y, int z, int id) => BlockSpill.Drop(_world, _players, _online, x, y, z, id);
}
