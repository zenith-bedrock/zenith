using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// Aplica <see cref="BlockEditIntent"/> no tick e replica UpdateBlock aos peers in-game.
/// Break timing (§27): exige progresso AuthInput when BreakTicks &gt; 0.
/// Floor drops (§26): se TryAdd falha, bloco quebra e cai em <see cref="FloorDropStore"/>.
/// </summary>
sealed class BlockSystem : IGameSystem
{
    private readonly PlayerManager _players;
    private readonly World.World _world;

    public BlockSystem(PlayerManager players, World.World world)
    {
        _players = players;
        _world = world;
    }

    public void Tick(GameClock clock)
    {
        if (_players.Count == 0) return;

        foreach (var player in _players.Online)
        {
            while (player.TryConsumeBlockEdit(out var edit))
                ApplyEdit(player, edit, clock);
        }

        PickupFloorDrops(clock);
    }

    private void ApplyEdit(global::Zenith.Player.Player player, in BlockEditIntent edit, GameClock clock)
    {
        if (!edit.IsInWorldBounds())
            return;

        if (!IsWithinReach(player, edit.X, edit.Y, edit.Z))
            return;

        var creative = player.GameMode == GameMode.Creative;
        var inventoryChanged = false;
        if (edit.BlockRuntimeId != World.World.AirRuntimeId)
        {
            if (_world.GetBlock(edit.X, edit.Y, edit.Z) != World.World.AirRuntimeId)
                return;

            if (!creative)
            {
                var slot = edit.HotbarSlot;
                if (!PlayerInventory.IsValidHotbarSlot(slot) || !player.Inventory.TryConsumeOne(slot))
                    return;
                inventoryChanged = true;
            }
        }
        else
        {
            var previous = _world.GetBlock(edit.X, edit.Y, edit.Z);
            if (previous == World.World.AirRuntimeId)
                return;

            if (!creative)
            {
                var need = Blocks.BreakTicks(previous);
                if (need > 0)
                {
                    if (!player.HasBreakTarget || !player.IsBreakTarget(edit.X, edit.Y, edit.Z))
                    {
                        player.Session.Context.Logger.Debug(
                            $"Break rejected (no/wrong start_break) for {player.Username} @ {edit.X},{edit.Y},{edit.Z}");
                        return;
                    }

                    var elapsed = clock.CurrentTick >= player.BreakStartedTick
                        ? clock.CurrentTick - player.BreakStartedTick
                        : 0;
                    if (elapsed < (ulong)need)
                    {
                        player.Session.Context.Logger.Debug(
                            $"Break rejected (early) for {player.Username}: {elapsed}/{need} ticks");
                        return;
                    }
                }
            }

            if (player.HasBreakTarget)
                player.Session.Protocol.World.SendBlockStopCrack(
                    player.BreakTargetX, player.BreakTargetY, player.BreakTargetZ);
            player.AbortBreak();

            if (previous == Blocks.Chest)
            {
                if (player.OpenChest is { } open &&
                    open.X == edit.X && open.Y == edit.Y && open.Z == edit.Z)
                    player.OpenChest = null;

                var dumped = _world.Chests.RemoveAndDump(edit.X, edit.Y, edit.Z);
                if (!creative)
                {
                    foreach (var (rid, count) in dumped)
                    {
                        if (!player.Inventory.TryAdd(rid, count))
                            _world.FloorDrops.AddOrMerge(edit.X, edit.Y, edit.Z, rid, count);
                        else
                            inventoryChanged = true;
                    }
                }
            }

            if (!creative)
            {
                if (!player.Inventory.TryAdd(previous))
                {
                    _world.FloorDrops.AddOrMerge(edit.X, edit.Y, edit.Z, previous, 1);
                    player.Session.Context.Logger.Debug(
                        $"Break → floor drop for {player.Username} @ {edit.X},{edit.Y},{edit.Z}");
                }
                else
                    inventoryChanged = true;
            }
        }

        _world.SetBlock(edit.X, edit.Y, edit.Z, edit.BlockRuntimeId);

        if (edit.BlockRuntimeId == Blocks.Chest)
            _world.Chests.Ensure(edit.X, edit.Y, edit.Z);

        if (inventoryChanged)
            player.Session.Protocol.Inventory.SendInventoryContent(player.Inventory);

        foreach (var peer in _players.Online)
        {
            if (!peer.IsInGame) continue;
            peer.Session.Protocol.World.SendUpdateBlock(edit.X, edit.Y, edit.Z, edit.BlockRuntimeId);
        }
    }

    private void PickupFloorDrops(GameClock clock)
    {
        _ = clock;
        const float reachSq = 1.5f * 1.5f;
        foreach (var (pos, runtimeId, count) in _world.FloorDrops.Snapshot())
        {
            foreach (var player in _players.Online)
            {
                if (!player.IsInGame) continue;
                var dx = player.PositionX - (pos.X + 0.5f);
                var dy = player.PositionY - (pos.Y + 0.5f);
                var dz = player.PositionZ - (pos.Z + 0.5f);
                if (dx * dx + dy * dy + dz * dz > reachSq) continue;
                if (!player.Inventory.TryAdd(runtimeId, count)) continue;
                if (!_world.FloorDrops.TryTake(pos.X, pos.Y, pos.Z, out _, out _)) continue;
                player.Session.Protocol.Inventory.SendInventoryContent(player.Inventory);
                break;
            }
        }
    }

    internal static bool IsWithinReach(global::Zenith.Player.Player player, int x, int y, int z)
    {
        var eyeX = player.PositionX;
        var eyeY = player.PositionY + Blocks.PlayerEyeHeight;
        var eyeZ = player.PositionZ;
        var dx = eyeX - (x + 0.5f);
        var dy = eyeY - (y + 0.5f);
        var dz = eyeZ - (z + 0.5f);
        var max = Player.Player.MaxBlockReach;
        return dx * dx + dy * dy + dz * dz <= max * max;
    }
}
