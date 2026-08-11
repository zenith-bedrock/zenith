using System.Collections.Generic;
using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.Session;
using Zenith.World;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// Applies authorized <see cref="BlockEditIntent"/> mutations and replicates UpdateBlock outcomes.
/// Dig authorization is prepared beforehand by <see cref="BlockDigSystem"/>.
/// </summary>
sealed class BlockEditSystem : IGameSystem
{
    private readonly PlayerManager _players;
    private readonly List<(int X, int Y, int Z, int BlockRuntimeId)> _updatesScratch = new();
    private readonly List<(int X, int Y, int Z, int BlockRuntimeId)> _joinerSubsetScratch = new();
    private readonly World.World _world;

    public BlockEditSystem(PlayerManager players, World.World world)
    {
        _players = players;
        _world = world;
    }

    public void Tick(GameClock clock, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        if (online.Count == 0) return;

        var updates = _updatesScratch;
        updates.Clear();
        foreach (var player in online)
        {
            while (player.TryConsumeBlockEdit(out var edit))
            {
                // The online list is captured once per tick. A disconnect can remove a player
                // after that snapshot; its queued client edit must not mutate world state.
                if (!player.IsInGame || player.IsDead) continue;
                if (ApplyEdit(player, edit, clock, online))
                    updates.Add((edit.X, edit.Y, edit.Z, edit.BlockRuntimeId));
            }
        }

        if (updates.Count > 0)
        {
            var joinerSubset = _joinerSubsetScratch;
            foreach (var peer in online)
            {
                // InGame peers always; joiners who already Know the column get live
                // UpdateBlock during PreSpawn/SpawnResponse (§14).
                if (peer.IsInGame)
                {
                    peer.Session.Protocol.World.PublishUpdateBlocks(updates);
                    continue;
                }

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
            // DigAuthorized Predict already cleared dig lock — still StopCrack on break reject (§27).
            if (edit.BlockRuntimeId == World.World.AirRuntimeId)
                RejectBreakToBreaker(player, online, edit.X, edit.Y, edit.Z);
            else
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

            if (!_world.CanAcceptBlockWrite(edit.X, edit.Y, edit.Z, edit.BlockRuntimeId) ||
                (Blocks.IsChest(edit.BlockRuntimeId) && !_world.Chests.CanAcceptNew(edit.X, edit.Y, edit.Z)))
            {
                player.Session.Context.Logger.Debug(
                    $"Place refused (store SoftCap) for {player.Username} @ {edit.X},{edit.Y},{edit.Z}");
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

            if (!_world.TrySetBlock(edit.X, edit.Y, edit.Z, edit.BlockRuntimeId))
            {
                if (!creative && PlayerInventory.IsValidHotbarSlot(edit.HotbarSlot))
                {
                    player.Inventory.TryAddUpTo(
                        StackId.FromBlock(Blocks.NormalizeMergeRuntimeId(edit.BlockRuntimeId)), 1);
                }

                ResyncCellToBreaker(player, edit.X, edit.Y, edit.Z);
                return false;
            }

            if (Blocks.IsChest(edit.BlockRuntimeId))
            {
                if (!_world.Chests.TryEnsure(edit.X, edit.Y, edit.Z))
                {
                    _ = _world.TrySetBlock(edit.X, edit.Y, edit.Z, World.World.AirRuntimeId);
                    if (!creative && PlayerInventory.IsValidHotbarSlot(edit.HotbarSlot))
                    {
                        player.Inventory.TryAddUpTo(
                            StackId.FromBlock(Blocks.NormalizeMergeRuntimeId(edit.BlockRuntimeId)), 1);
                    }

                    ResyncCellToBreaker(player, edit.X, edit.Y, edit.Z);
                    return false;
                }

                _world.PersistChest(edit.X, edit.Y, edit.Z);
            }

            BlockSoundFanout.Place(
                online, player.Session, edit.X, edit.Y, edit.Z, edit.BlockRuntimeId);
        }
        else
        {
            var previous = _world.GetBlock(edit.X, edit.Y, edit.Z);
            if (previous == World.World.AirRuntimeId)
            {
                RejectBreakToBreaker(player, online, edit.X, edit.Y, edit.Z);
                return false;
            }

            if (!creative)
            {
                var held = player.Inventory.Get(player.SelectedHotbarSlot);
                var heldId = held.IsEmpty ? default : held.Id;
                var need = edit.DigAuthorized
                    ? edit.DigRequiredTicks
                    : Blocks.BreakTicks(previous, heldId);
                // need < 0 = no DigProfile (ADR §55); need > 0 requires dig auth + elapsed.
                if (need < 0 || (need > 0 && !edit.DigAuthorized))
                {
                    player.Session.Context.Logger.Debug(
                        $"Break rejected (no dig auth) for {player.Username} @ {edit.X},{edit.Y},{edit.Z}");
                    RejectBreakToBreaker(player, online, edit.X, edit.Y, edit.Z);
                    return false;
                }

                if (need > 0)
                {
                    var elapsed = clock.CurrentTick >= edit.DigStartedTick
                        ? clock.CurrentTick - edit.DigStartedTick
                        : 0;
                    // Inclusive last mining tick: client often sends predict_destroy when progress
                    // completes on tick (need-1) relative to DigStartedTick (AuthInput start).
                    // Requiring elapsed >= need made crack UI finish while the gate still rejected.
                    var minElapsed = need > 1 ? (ulong)(need - 1) : (ulong)need;
                    if (elapsed < minElapsed)
                    {
                        player.Session.Context.Logger.Debug(
                            $"Break rejected (early) for {player.Username}: {elapsed}/{need} ticks (min={minElapsed})");
                        RejectBreakToBreaker(player, online, edit.X, edit.Y, edit.Z);
                        return false;
                    }
                }
            }

            // Resolve pair + SoftCap check while the broken cell still has its chest rid (§56 / §36).
            var wasChest = Blocks.IsChest(previous);
            OpenChestView pairView = default;
            if (wasChest)
                pairView = ChestPairing.ViewFor(_world, edit.X, edit.Y, edit.Z);

            if (!_world.CanAcceptBlockWrite(edit.X, edit.Y, edit.Z, edit.BlockRuntimeId) ||
                !_world.TrySetBlock(edit.X, edit.Y, edit.Z, edit.BlockRuntimeId))
            {
                player.Session.Context.Logger.Debug(
                    $"Break refused (overlay SoftCap) for {player.Username} @ {edit.X},{edit.Y},{edit.Z}");
                RejectBreakToBreaker(player, online, edit.X, edit.Y, edit.Z);
                return false;
            }

            // Stop crack at the broken cell — dig target may already be cleared after queue (§27).
            BlockCrackFanout.Stop(online, player.Session, edit.X, edit.Y, edit.Z);
            BlockSoundFanout.Break(online, player.Session, edit.X, edit.Y, edit.Z, previous);
            if (player.IsBreakTarget(edit.X, edit.Y, edit.Z))
                player.AbortBreak();

            if (wasChest)
            {
                var lidBroken = _world.Chests.ClearOpeners(edit.X, edit.Y, edit.Z);
                var lidPartner = false;
                var partnerX = 0;
                var partnerY = 0;
                var partnerZ = 0;
                if (pairView.TryGetPartner(out partnerX, out partnerY, out partnerZ))
                    lidPartner = _world.Chests.ClearOpeners(partnerX, partnerY, partnerZ);

                foreach (var peer in online)
                {
                    if (peer.OpenChest is { } open && open.Contains(edit.X, edit.Y, edit.Z))
                        _ = peer.TryClearOpenContainer(out _);
                }

                if (lidBroken)
                    ChestLidFanout.Close(online, player.Session, edit.X, edit.Y, edit.Z);
                if (lidPartner)
                    ChestLidFanout.Close(online, player.Session, partnerX, partnerY, partnerZ);

                var dumped = _world.Chests.RemoveAndDump(edit.X, edit.Y, edit.Z);
                _world.DeletePersistedChest(edit.X, edit.Y, edit.Z);
                // Contents never void — Creative InstantBuild still dumps store to floor (§74).
                foreach (var (stackId, count) in dumped)
                {
                    var id = stackId.IsBlock
                        ? StackId.FromBlock(Blocks.NormalizeMergeRuntimeId(stackId.Value))
                        : stackId;
                    if (creative)
                    {
                        DepositFloorDrop(online, edit.X, edit.Y, edit.Z, id, count);
                        continue;
                    }

                    var added = player.Inventory.TryAddUpTo(id, count);
                    if (added > 0)
                        inventoryChanged = true;
                    var surplus = count - added;
                    if (surplus > 0)
                        DepositFloorDrop(online, edit.X, edit.Y, edit.Z, id, surplus);
                }
            }

            if (!creative && ShouldDropBrokenBlock(previous, player))
            {
                // Oriented chest → item form (south) so stacks merge.
                var dropRid = Blocks.NormalizeMergeRuntimeId(wasChest ? Blocks.Chest : previous);
                var dropId = StackId.FromBlock(dropRid);
                var added = player.Inventory.TryAddUpTo(dropId, 1);
                if (added > 0)
                    inventoryChanged = true;
                if (added < 1 && DepositFloorDrop(online, edit.X, edit.Y, edit.Z, dropId, 1))
                {
                    player.Session.Context.Logger.Debug(
                        $"Break → floor drop for {player.Username} @ {edit.X},{edit.Y},{edit.Z}");
                }
            }
        }

        if (inventoryChanged)
        {
            player.Session.Protocol.Inventory.SendInventoryContent(player.Inventory);
            _world.PersistInventory(player);
        }

        NotifyGravityAfterEdit(edit.X, edit.Y, edit.Z, edit.BlockRuntimeId);
        return true;
    }

    /// <summary>Enqueue gravity evaluation after a successful world mutation (ADR §57).</summary>
    private void NotifyGravityAfterEdit(int x, int y, int z, int placedOrAirRid)
    {
        var pending = _world.GravityPending;
        if (Blocks.IsGravity(placedOrAirRid))
            pending.TryEnqueue(x, y, z);

        // Break / replace to air (or any non-gravity): cell above may lose support.
        if (placedOrAirRid == World.World.AirRuntimeId || !Blocks.IsGravity(placedOrAirRid))
        {
            var aboveY = y + 1;
            if (Blocks.IsGravity(_world.GetBlock(x, aboveY, z)))
                pending.TryEnqueue(x, aboveY, z);
        }
    }

    /// <summary>Self UpdateBlock with server truth — kills client ghost after reject (§27).</summary>
    private void ResyncCellToBreaker(global::Zenith.Player.Player player, int x, int y, int z)
    {
        if (!player.IsInGame) return;
        player.Session.Protocol.World.SendUpdateBlock(x, y, z, _world.GetBlock(x, y, z));
    }

    /// <summary>
    /// DigAuthorized Predict clears the dig lock before tick (chain-break Continue). On reject the
    /// crack LevelEvent must still Stop — otherwise peers/miner keep cracking while Resync restores
    /// the block (§27 dig lifecycle).
    /// </summary>
    private void RejectBreakToBreaker(
        global::Zenith.Player.Player player,
        IReadOnlyList<global::Zenith.Player.Player> online,
        int x,
        int y,
        int z)
    {
        BlockCrackFanout.Stop(online, player.Session, x, y, z);
        if (player.IsBreakTarget(x, y, z))
            player.AbortBreak();
        ResyncCellToBreaker(player, x, y, z);
    }

    /// <summary>
    /// Survival block loot gate (ADR §74): when the dig profile requires the correct tool,
    /// empty hand / wrong tool breaks the cell but drops nothing.
    /// </summary>
    private static bool ShouldDropBrokenBlock(int previousRuntimeId, global::Zenith.Player.Player player)
    {
        if (!DigProfiles.TryGet(previousRuntimeId, out var profile) ||
            !profile.RequiresCorrectToolForDrops)
            return true;

        var held = player.Inventory.Get(player.SelectedHotbarSlot);
        var tool = held.Id.IsItem ? Tools.AsTool(held.Id.Value) : ToolInfo.None;
        return BreakDuration.IsHarvestable(profile, tool);
    }

    private bool DepositFloorDrop(
        IReadOnlyList<global::Zenith.Player.Player> online,
        int x,
        int y,
        int z,
        StackId id,
        int count) =>
        FloorDropFanout.TryDeposit(_world, _players, online, x, y, z, id, count);

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
