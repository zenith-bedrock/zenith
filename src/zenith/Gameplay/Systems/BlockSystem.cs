using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// Aplica <see cref="BlockEditIntent"/> no tick e replica UpdateBlock aos peers in-game.
/// Break timing (§27): dig auth snapshotted on the intent when BreakTicks &gt; 0.
/// Floor drops (§26): se TryAdd falha, bloco quebra e cai em <see cref="FloorDropStore"/> + AddItemActor wire.
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

        var online = _players.Online;
        var updates = new List<(int X, int Y, int Z, int BlockRuntimeId)>();
        foreach (var player in online)
        {
            while (player.TryConsumeBlockEdit(out var edit))
            {
                if (player.IsDead) continue;
                if (ApplyEdit(player, edit, clock, online))
                    updates.Add((edit.X, edit.Y, edit.Z, edit.BlockRuntimeId));
            }
        }

        if (updates.Count > 0)
        {
            List<(int X, int Y, int Z, int BlockRuntimeId)>? joinerSubset = null;
            foreach (var peer in online)
            {
                // InGame peers always; joiners who already Know the column get live
                // UpdateBlock during PreSpawn/SpawnResponse (§14).
                if (peer.IsInGame)
                {
                    peer.Session.Protocol.World.PublishUpdateBlocks(updates);
                    continue;
                }

                joinerSubset ??= new List<(int X, int Y, int Z, int BlockRuntimeId)>(updates.Count);
                joinerSubset.Clear();
                for (var i = 0; i < updates.Count; i++)
                {
                    var u = updates[i];
                    var cx = PlayerChunkTracker.BlockToChunk(u.X);
                    var cz = PlayerChunkTracker.BlockToChunk(u.Z);
                    if (peer.Chunks.Knows(cx, cz))
                        joinerSubset.Add(u);
                }

                if (joinerSubset.Count > 0)
                    peer.Session.Protocol.World.PublishUpdateBlocks(joinerSubset);
            }
        }

        PickupFloorDrops(clock, online);
    }

    /// <returns>True when the world mutation was applied (peers need UpdateBlock).</returns>
    private bool ApplyEdit(
        global::Zenith.Player.Player player,
        in BlockEditIntent edit,
        GameClock clock,
        IReadOnlyList<global::Zenith.Player.Player> online)
    {
        if (!edit.IsInWorldBounds())
            return false;

        if (!IsWithinReach(player, edit.X, edit.Y, edit.Z))
        {
            ResyncCellToBreaker(player, edit.X, edit.Y, edit.Z);
            return false;
        }

        var creative = player.GameMode == GameMode.Creative;
        var inventoryChanged = false;
        if (edit.BlockRuntimeId != World.World.AirRuntimeId)
        {
            if (!Blocks.IsPlaceable(edit.BlockRuntimeId))
            {
                ResyncCellToBreaker(player, edit.X, edit.Y, edit.Z);
                return false;
            }

            if (_world.GetBlock(edit.X, edit.Y, edit.Z) != World.World.AirRuntimeId)
            {
                ResyncCellToBreaker(player, edit.X, edit.Y, edit.Z);
                return false;
            }

            // Body / peer obstruction before consume — avoid self-trap and hotbar steal (§15).
            if (IsPlaceObstructedByPlayers(player, edit.X, edit.Y, edit.Z, online))
            {
                ResyncCellToBreaker(player, edit.X, edit.Y, edit.Z);
                return false;
            }

            if (!creative)
            {
                var slot = edit.HotbarSlot;
                if (!PlayerInventory.IsValidHotbarSlot(slot) || !player.Inventory.TryConsumeOne(slot))
                {
                    ResyncCellToBreaker(player, edit.X, edit.Y, edit.Z);
                    return false;
                }
                inventoryChanged = true;
            }
        }
        else
        {
            var previous = _world.GetBlock(edit.X, edit.Y, edit.Z);
            if (previous == World.World.AirRuntimeId)
            {
                ResyncCellToBreaker(player, edit.X, edit.Y, edit.Z);
                return false;
            }

            if (!creative)
            {
                var need = edit.DigAuthorized
                    ? edit.DigRequiredTicks
                    : Blocks.BreakTicks(previous);
                if (need > 0)
                {
                    if (!edit.DigAuthorized)
                    {
                        player.Session.Context.Logger.Debug(
                            $"Break rejected (no dig auth) for {player.Username} @ {edit.X},{edit.Y},{edit.Z}");
                        ResyncCellToBreaker(player, edit.X, edit.Y, edit.Z);
                        return false;
                    }

                    var elapsed = clock.CurrentTick >= edit.DigStartedTick
                        ? clock.CurrentTick - edit.DigStartedTick
                        : 0;
                    if (elapsed < (ulong)need)
                    {
                        player.Session.Context.Logger.Debug(
                            $"Break rejected (early) for {player.Username}: {elapsed}/{need} ticks");
                        ResyncCellToBreaker(player, edit.X, edit.Y, edit.Z);
                        return false;
                    }
                }
            }

            // Stop crack at the broken cell — dig target may already be cleared after queue (§27).
            BlockCrackFanout.Stop(_players, player.Session, edit.X, edit.Y, edit.Z);
            if (player.IsBreakTarget(edit.X, edit.Y, edit.Z))
                player.AbortBreak();

            var wasChest = Blocks.IsChest(previous);
            if (wasChest)
            {
                if (player.OpenChest is { } open &&
                    open.X == edit.X && open.Y == edit.Y && open.Z == edit.Z)
                    player.OpenChest = null;

                var dumped = _world.Chests.RemoveAndDump(edit.X, edit.Y, edit.Z);
                _world.DeletePersistedChest(edit.X, edit.Y, edit.Z);
                if (!creative)
                {
                    foreach (var (rid, count) in dumped)
                    {
                        var mergeRid = Blocks.NormalizeMergeRuntimeId(rid);
                        var added = player.Inventory.TryAddUpTo(mergeRid, count);
                        if (added > 0)
                            inventoryChanged = true;
                        var surplus = count - added;
                        if (surplus > 0)
                            DepositFloorDrop(online, edit.X, edit.Y, edit.Z, mergeRid, surplus);
                    }
                }
            }

            if (!creative)
            {
                // Oriented chest → item form (south) so stacks merge.
                var dropRid = Blocks.NormalizeMergeRuntimeId(wasChest ? Blocks.Chest : previous);
                var added = player.Inventory.TryAddUpTo(dropRid, 1);
                if (added > 0)
                    inventoryChanged = true;
                if (added < 1 && DepositFloorDrop(online, edit.X, edit.Y, edit.Z, dropRid, 1))
                {
                    player.Session.Context.Logger.Debug(
                        $"Break → floor drop for {player.Username} @ {edit.X},{edit.Y},{edit.Z}");
                }
            }
        }

        _world.SetBlock(edit.X, edit.Y, edit.Z, edit.BlockRuntimeId);

        if (Blocks.IsChest(edit.BlockRuntimeId))
        {
            _world.Chests.Ensure(edit.X, edit.Y, edit.Z);
            _world.PersistChest(edit.X, edit.Y, edit.Z);
        }

        if (inventoryChanged)
        {
            player.Session.Protocol.Inventory.SendInventoryContent(player.Inventory);
            _world.PersistInventory(player.Uuid, player.Inventory);
        }

        return true;
    }

    /// <summary>Self UpdateBlock with server truth — kills client ghost after reject (§27).</summary>
    private void ResyncCellToBreaker(global::Zenith.Player.Player player, int x, int y, int z)
    {
        if (!player.IsInGame) return;
        player.Session.Protocol.World.SendUpdateBlock(x, y, z, _world.GetBlock(x, y, z));
    }

    private bool DepositFloorDrop(
        IReadOnlyList<global::Zenith.Player.Player> online,
        int x,
        int y,
        int z,
        int itemRuntimeId,
        int count)
    {
        var entityId = _players.AllocateRuntimeId();
        if (!_world.FloorDrops.TryAddOrMerge(x, y, z, itemRuntimeId, count, entityId, out var deposit) ||
            deposit is null)
            return false;

        var d = deposit.Value;
        PublishFloorDrop(online, d);
        return true;
    }

    private static void PublishFloorDrop(
        IReadOnlyList<global::Zenith.Player.Player> online,
        FloorDropStore.DepositResult deposit)
    {
        if (!deposit.Created && !deposit.CountChanged) return;

        var cx = PlayerChunkTracker.BlockToChunk(deposit.X);
        var cz = PlayerChunkTracker.BlockToChunk(deposit.Z);
        var px = deposit.X + 0.5f;
        var py = deposit.Y + 0.125f;
        var pz = deposit.Z + 0.5f;

        foreach (var peer in online)
        {
            // InGame always; PreSpawn joiners only if they Know the column (§14).
            if (!peer.IsInGame && !peer.Chunks.Knows(cx, cz)) continue;

            var entity = peer.Session.Protocol.Entity;
            var item = peer.Session.Protocol.Inventory.DescribeStack(deposit.ItemRuntimeId, deposit.Count);
            if (!deposit.Created && deposit.CountChanged)
                entity.SendRemoveActor(deposit.EntityRuntimeId);
            if (item.NetworkId == 0) continue; // invalid/air item crashes Bedrock near player
            entity.SendAddItemActor(deposit.EntityRuntimeId, item, px, py, pz);
        }
    }

    private void PickupFloorDrops(GameClock clock, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        _ = clock;
        _world.FloorDrops.TickPickupDelays();

        foreach (var (pos, runtimeId, count, entityRuntimeId, pickupDelay) in _world.FloorDrops.Snapshot())
        {
            if (pickupDelay > 0) continue;

            var mergeRid = Blocks.NormalizeMergeRuntimeId(runtimeId);
            foreach (var player in online)
            {
                if (!player.IsInGame || player.IsDead) continue;
                if (!IsWithinFloorPickupReach(player, pos.X, pos.Y, pos.Z)) continue;

                var invSnap = player.Inventory.CaptureSnapshot();
                var added = player.Inventory.TryAddUpTo(mergeRid, count);
                if (added == 0) continue;

                if (!_world.FloorDrops.TryTakeUpTo(
                        pos.X, pos.Y, pos.Z, added,
                        out _, out _, out var takenEntity, out var remainingPublish))
                {
                    player.Inventory.RestoreSnapshot(invSnap);
                    continue;
                }

                var eid = takenEntity != 0 ? takenEntity : entityRuntimeId;
                var cx = PlayerChunkTracker.BlockToChunk(pos.X);
                var cz = PlayerChunkTracker.BlockToChunk(pos.Z);
                foreach (var peer in online)
                {
                    if (!peer.IsInGame && !peer.Chunks.Knows(cx, cz)) continue;
                    peer.Session.Protocol.Entity.SendTakeItemActor(
                        (ulong)eid,
                        (ulong)player.RuntimeId);
                }

                // Partial: Take despawns entity; republish remaining stack (same entity id / delay).
                if (remainingPublish is { } rem)
                    PublishFloorDrop(online, rem);

                player.Session.Protocol.Inventory.SendInventoryContent(player.Inventory);
                _world.PersistInventory(player.Uuid, player.Inventory);
                break;
            }
        }
    }

    /// <summary>
    /// Pickup — standing AABB expanded by <see cref="EntityHitboxes.PickupExpand"/>
    /// vs item entity AABB at cell. Domain <see cref="Player.Player.PositionY"/> is feet.
    /// </summary>
    internal static bool IsWithinFloorPickupReach(global::Zenith.Player.Player player, int x, int y, int z)
    {
        var playerBb = EntityHitboxes.PlayerStanding(player.PositionX, player.PositionY, player.PositionZ)
            .Expand(EntityHitboxes.PickupExpand);
        var itemBb = EntityHitboxes.ItemAtCell(x, y, z);
        return playerBb.Intersects(itemBb);
    }

    /// <summary>
    /// True when the place cell intersects the placer or another InGame player's standing BB
    /// (inset by <see cref="EntityHitboxes.PlaceCollisionEpsilon"/>). Floor drops are not scanned.
    /// </summary>
    internal static bool IsPlaceObstructedByPlayers(
        global::Zenith.Player.Player placer,
        int x,
        int y,
        int z,
        IReadOnlyList<global::Zenith.Player.Player> online)
    {
        var cell = EntityHitboxes.BlockCell(x, y, z);
        var placerBb = EntityHitboxes.StandingForPlaceCheck(
            placer.PositionX, placer.PositionY, placer.PositionZ);
        if (cell.Intersects(placerBb))
            return true;

        for (var i = 0; i < online.Count; i++)
        {
            var peer = online[i];
            if (!peer.IsInGame || ReferenceEquals(peer, placer))
                continue;

            var peerBb = EntityHitboxes.StandingForPlaceCheck(
                peer.PositionX, peer.PositionY, peer.PositionZ);
            if (cell.Intersects(peerBb))
                return true;
        }

        return false;
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
