using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Zenith.Event;
using Zenith.Gameplay.Policy;
using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.World;
using Zenith.Gameplay.Inventory;
using Zenith.Gameplay.Replication;
using Zenith.Gameplay.Survival;

namespace Zenith.Gameplay.WorldInteraction;

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
    private readonly ItemDurability _durability;
    private readonly List<BlockWrite> _extra = new();
    private readonly List<BlockCellChange> _cells = new();
    private readonly List<BlockDrop> _rolled = new();
    private readonly Random _random = new();
    private readonly BlockEditPolicies _policies;
    private readonly EventBus? _events;
    private int _appliedId;
    private ulong _tick;
    private long _nextOperation;

    /// <param name="policies">The vetoes consulted before an edit; none means nothing can refuse (composition always supplies the permission policy).</param>
    /// <param name="events">Where the applied-edit fact is published; none means nobody is told.</param>
    public BlockEditSystem(
        PlayerManager players, World.World world, ItemDurability durability,
        BlockEditPolicies? policies = null, EventBus? events = null)
    {
        _players = players;
        _world = world;
        _durability = durability;
        _policies = policies ?? new BlockEditPolicies();
        _events = events;
    }

    public void Tick(GameClock clock, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        if (online.Count == 0) return;

        _tick = clock.CurrentTick;
        var updates = _updatesScratch;
        updates.Clear();
        foreach (var player in online)
        {
            while (player.Intents.TryConsumeBlockEdit(out var edit))
            {
                // The online list is captured once per tick. A disconnect can remove a player
                // after that snapshot; its queued client edit must not mutate world state.
                if (!player.IsInGame || player.IsDead) continue;
                _extra.Clear();
                if (edit.IsToggle)
                {
                    if (ApplyToggle(player, edit))
                        foreach (var write in _extra) updates.Add((write.X, write.Y, write.Z, write.BlockRuntimeId));
                    continue;
                }

                if (ApplyEdit(player, edit, clock, online))
                {
                    updates.Add((edit.X, edit.Y, edit.Z, _appliedId));
                    foreach (var write in _extra) updates.Add((write.X, write.Y, write.Z, write.BlockRuntimeId));
                }
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
                    peer.Client.World.PublishUpdateBlocks(updates);
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
                    peer.Client.World.PublishUpdateBlocks(joinerSubset);
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

        var applied = edit.BlockRuntimeId != World.World.AirRuntimeId
            ? TryPlace(player, edit, online)
            : TryBreak(player, edit, clock, online);
        if (!applied) return false;

        return true;
    }

    /// <summary>A refused placement: the placer's client gets the cell's real state back.</summary>
    private bool RejectPlace(global::Zenith.Player.Player player, in BlockEditIntent edit)
    {
        ResyncCellToBreaker(player, edit.X, edit.Y, edit.Z);
        return false;
    }

    /// <summary>A refused break: stop the crack, drop the dig target, and resync the cell.</summary>
    private bool RejectBreak(global::Zenith.Player.Player player, IReadOnlyList<global::Zenith.Player.Player> online, in BlockEditIntent edit, string? reason = null)
    {
        if (reason is not null)
            player.Level.Logger?.Debug($"{reason} for {player.Username} @ {edit.X},{edit.Y},{edit.Z}");
        RejectBreakToBreaker(player, online, edit.X, edit.Y, edit.Z);
        return false;
    }

    private bool TryPlace(global::Zenith.Player.Player player, in BlockEditIntent edit, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        if (!Blocks.IsPlaceable(edit.BlockRuntimeId) &&
            Blocks.Catalog.KindOf(edit.BlockRuntimeId) != BlockKind.DoubleSlab)
            return RejectPlace(player, edit);

        var existing = _world.GetBlock(edit.X, edit.Y, edit.Z);
        if (!BlockShapes.CanPlace(_world, edit.X, edit.Y, edit.Z, edit.BlockRuntimeId, existing))
            return RejectPlace(player, edit);

        // Body / peer obstruction before consume — avoid self-trap and hotbar steal (§15).
        if (IsPlaceObstructedByPlayers(player, edit.X, edit.Y, edit.Z, online))
            return RejectPlace(player, edit);

        if (!World.WorldColumn.InRange(edit.Y))
        {
            player.Level.Logger?.Debug(
                $"Place refused (outside the world's height) for {player.Username} @ {edit.X},{edit.Y},{edit.Z}");
            return RejectPlace(player, edit);
        }

        // Plan the whole edit (the clicked cell plus whatever a coupled block takes along) without touching anything, and let the
        // policies judge it before an item is consumed or a cell is written.
        var placedId = edit.BlockRuntimeId;
        _extra.Clear();
        BlockShapes.OnPlaced(_world, edit.X, edit.Y, edit.Z, placedId, _extra);
        _extra.RemoveAll(write => !World.WorldColumn.InRange(write.Y));
        var decision = Evaluate(player, BlockEditKind.Place, edit, existing);
        if (!decision.IsAllowed)
            return DenyPlace(player, edit, decision);

        var consumedPlacementStack = false;
        InventorySnapshot placementSnapshot = default;
        if (player.GameMode != GameMode.Creative)
        {
            var slot = edit.HotbarSlot;
            if (!PlayerInventory.IsValidHotbarSlot(slot) ||
                (edit.HasExpectedPlacementStackId &&
                 player.Inventory.Get(slot).Id != edit.ExpectedPlacementStackId))
                return RejectPlace(player, edit);

            placementSnapshot = player.Inventory.CaptureSnapshot();
            if (!player.Inventory.TryConsumeOne(slot))
                return RejectPlace(player, edit);
            consumedPlacementStack = true;
        }

        if (!_world.TrySetBlock(edit.X, edit.Y, edit.Z, placedId))
        {
            if (consumedPlacementStack)
                player.Inventory.RestoreSnapshot(placementSnapshot);

            _extra.Clear();
            return RejectPlace(player, edit);
        }

        _appliedId = placedId;
        CommitExtraWrites();

        if (Blocks.IsChest(edit.BlockRuntimeId))
        {
            _world.Chests.Ensure(edit.X, edit.Y, edit.Z);
            _world.MarkBlockEntityDirty(edit.X, edit.Z);
            ChestActorFanout.PublishWithPartner(online, _world, edit.X, edit.Y, edit.Z);
        }

        BlockSoundFanout.Place(online, player.Client, edit.X, edit.Y, edit.Z, edit.BlockRuntimeId);

        if (consumedPlacementStack)
        {
            player.Client.Inventory.SendInventoryContent(player.Inventory);
            _world.Persistence.PersistInventory(player);
        }

        PublishApplied(player, BlockEditKind.Place);
        return true;
    }

    private bool TryBreak(global::Zenith.Player.Player player, in BlockEditIntent edit, GameClock clock, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        var previous = _world.GetBlock(edit.X, edit.Y, edit.Z);
        if (previous == World.World.AirRuntimeId)
            return RejectBreak(player, online, edit);

        var creative = player.GameMode == GameMode.Creative;
        if (!creative && !IsBreakAuthorized(player, edit, previous, clock))
            return RejectBreak(player, online, edit);

        // Resolve pair + SoftCap check while the broken cell still has its chest rid (§56 / §36).
        var wasChest = Blocks.IsChest(previous);
        var pairView = wasChest ? ChestPairing.ViewFor(_world, edit.X, edit.Y, edit.Z) : default;

        // Plan the whole edit (a door takes its other half along) without touching anything, and let the policies judge it.
        _extra.Clear();
        BlockShapes.OnRemoved(_world, edit.X, edit.Y, edit.Z, previous, _extra);
        _extra.RemoveAll(write => !World.WorldColumn.InRange(write.Y));
        var decision = Evaluate(player, BlockEditKind.Break, edit, previous);
        if (!decision.IsAllowed)
            return DenyBreak(player, online, edit, decision);

        var shouldDrop = !creative && _world.Rules.DoTileDrops && ShouldDropBrokenBlock(previous, player);
        if (!TryPlanDrops(player, edit, previous, wasChest, shouldDrop, out var floorDropRequests))
            return RejectBreak(player, online, edit);

        if (!_world.TrySetBlock(edit.X, edit.Y, edit.Z, edit.BlockRuntimeId))
            return RejectBreak(player, online, edit, "Break refused (outside the world's height)");

        _appliedId = World.World.AirRuntimeId;
        CommitExtraWrites();

        // Phase XXV — mining exhaustion source (survival only, matching HungerSystem's own
        // Creative skip). Small per-block value: mining alone barely dents hunger in vanilla
        // either, sprinting dominates.
        if (!creative)
        {
            player.Exhaustion += HungerSystem.MiningExhaustionPerBlock;
            WearHeldTool(player);
        }

        // Stop crack at the broken cell — dig target may already be cleared after queue (§27).
        BlockCrackFanout.Stop(online, player.Client, edit.X, edit.Y, edit.Z);
        BlockSoundFanout.Break(online, player.Client, edit.X, edit.Y, edit.Z, previous);
        if (player.Dig.IsBreakTarget(edit.X, edit.Y, edit.Z))
            player.Dig.AbortBreak();

        if (wasChest)
            ReleaseBrokenChest(player, edit, pairView, online);

        if (floorDropRequests is { Count: > 0 })
        {
            if (!FloorDropFanout.TryDepositBatch(
                    _world, _players, online, edit.X, edit.Y, edit.Z, floorDropRequests,
                    velocityX: BlockSpill.PopSideways(_random), velocityY: BlockSpill.PopUp, velocityZ: BlockSpill.PopSideways(_random)))
                throw new InvalidOperationException("Prevalidated block-drop floor commit failed.");

            player.Level.Logger?.Debug($"Break → floor drop for {player.Username} @ {edit.X},{edit.Y},{edit.Z}");
        }

        if (shouldDrop && BlockExperience.TryRoll(previous, _random, out var points))
            Zenith.Gameplay.Entities.MobKillReward.Grant(player, points, online, _world, _players, _random, edit.X + .5f, edit.Y + .5f, edit.Z + .5f);

        PublishApplied(player, BlockEditKind.Break);
        return true;
    }

    /// <summary>Asks the policies about the edit just planned: the clicked cell first, then every cell it drags along.</summary>
    private PolicyDecision Evaluate(global::Zenith.Player.Player player, BlockEditKind kind, in BlockEditIntent edit, int before)
    {
        var cells = _cells;
        cells.Clear();
        cells.Add(new BlockCellChange(edit.X, edit.Y, edit.Z, before, edit.BlockRuntimeId));
        foreach (var write in _extra)
            cells.Add(new BlockCellChange(write.X, write.Y, write.Z, _world.GetBlock(write.X, write.Y, write.Z), write.BlockRuntimeId));
        return _policies.Evaluate(new BlockEditProposal(player.Principal, kind, _tick, CollectionsMarshal.AsSpan(cells)));
    }

    /// <summary>Asks the policies about a use-in-place edit: every cell the interaction would change, none of them "clicked".</summary>
    private PolicyDecision EvaluateInteraction(global::Zenith.Player.Player player)
    {
        var cells = _cells;
        cells.Clear();
        foreach (var write in _extra)
            cells.Add(new BlockCellChange(write.X, write.Y, write.Z, _world.GetBlock(write.X, write.Y, write.Z), write.BlockRuntimeId));
        return _policies.Evaluate(new BlockEditProposal(player.Principal, BlockEditKind.Interact, _tick, CollectionsMarshal.AsSpan(cells)));
    }

    /// <summary>A vetoed placement: nothing was consumed or written; the placer's client gets every cell it predicted put back.</summary>
    private bool DenyPlace(global::Zenith.Player.Player player, in BlockEditIntent edit, PolicyDecision decision)
    {
        player.Level.Logger?.Debug($"Place denied for {player.Username} @ {edit.X},{edit.Y},{edit.Z}: {decision.Reason}");
        foreach (var cell in _cells) ResyncCellToBreaker(player, cell.X, cell.Y, cell.Z);
        _extra.Clear();
        return false;
    }

    private bool DenyBreak(
        global::Zenith.Player.Player player, IReadOnlyList<global::Zenith.Player.Player> online, in BlockEditIntent edit, PolicyDecision decision)
    {
        foreach (var cell in _cells) ResyncCellToBreaker(player, cell.X, cell.Y, cell.Z);
        _extra.Clear();
        return RejectBreak(player, online, edit, $"Break denied ({decision.Reason})");
    }

    /// <summary>The one fact of an applied edit, after every effect of it is done.</summary>
    private void PublishApplied(global::Zenith.Player.Player player, BlockEditKind kind)
    {
        if (_events is null) return;
        _events.Publish(new BlocksChanged(
            ++_nextOperation, player.Uuid, player.RuntimeId, kind, _tick, [.. _cells]));
    }

    /// <summary>
    /// Survival digging gate. A block with no dig profile (need &lt; 0, ADR §55) cannot be broken, and one that takes time needs the
    /// dig authorization the dig system prepared and the elapsed ticks it recorded.
    /// </summary>
    private bool IsBreakAuthorized(global::Zenith.Player.Player player, in BlockEditIntent edit, int previous, GameClock clock)
    {
        var held = player.Inventory.Get(player.SelectedHotbarSlot);
        var heldId = held.IsEmpty ? default : held.Id;
        var current = DigPenalty.BreakTicksFor(player, previous, heldId);
        // The dig was timed when it began; if the breaker has since landed or surfaced the client digs faster, so the faster of the two counts.
        var need = edit.DigAuthorized
            ? (edit.DigRequiredTicks > 0 && current >= 0 ? Math.Min(edit.DigRequiredTicks, current) : edit.DigRequiredTicks)
            : current;
        if (need < 0 || (need > 0 && !edit.DigAuthorized))
        {
            player.Level.Logger?.Debug($"Break rejected (no dig auth) for {player.Username} @ {edit.X},{edit.Y},{edit.Z}");
            return false;
        }

        if (need == 0) return true;

        var elapsed = clock.CurrentTick >= edit.DigStartedTick
            ? clock.CurrentTick - edit.DigStartedTick
            : 0;
        // Inclusive last mining tick: client often sends predict_destroy when progress
        // completes on tick (need-1) relative to DigStartedTick (AuthInput start).
        // Requiring elapsed >= need made crack UI finish while the gate still rejected.
        var minElapsed = need > 1 ? (ulong)(need - 1) : (ulong)need;
        if (elapsed >= minElapsed) return true;

        player.Level.Logger?.Debug(
            $"Break rejected (early) for {player.Username}: {elapsed}/{need} ticks (min={minElapsed})");
        return false;
    }

    /// <summary>
    /// What the break would put on the floor, and whether the floor can take it. A bounded floor store is part of the authoritative
    /// destination for a concrete block drop, so the break is refused before the source is removed when it cannot; accepted breaks
    /// publish ground loot before the ordinary pickup lifecycle. A chest's contents are always part of the plan.
    /// </summary>
    private bool TryPlanDrops(
        global::Zenith.Player.Player player, in BlockEditIntent edit, int previous, bool wasChest, bool shouldDrop,
        out List<FloorDropFanout.DepositRequest>? requests)
    {
        requests = null;
        if (wasChest)
        {
            requests = [];
            if (_world.Chests.TryGetSlots(edit.X, edit.Y, edit.Z, out var chestSlots))
            {
                foreach (var stack in chestSlots)
                {
                    if (stack.IsEmpty) continue;
                    var id = stack.Id.IsBlock
                        ? StackId.FromBlock(Blocks.NormalizeMergeRuntimeId(stack.Id.Value))
                        : stack.Id;
                    requests.Add(new FloorDropFanout.DepositRequest(id, stack.Count));
                }
            }

            if (shouldDrop)
            {
                var chestId = StackId.FromBlock(Blocks.NormalizeMergeRuntimeId(Blocks.Chest));
                requests.Add(new FloorDropFanout.DepositRequest(chestId, 1));
            }

            if (requests.Count > 0 && !FloorDropFanout.CanDepositBatch(_world, edit.X, edit.Y, edit.Z, requests))
            {
                player.Level.Logger?.Debug(
                    $"Break refused (no chest-loot destination) for {player.Username} @ {edit.X},{edit.Y},{edit.Z}");
                return false;
            }

            return true;
        }

        if (!shouldDrop) return true;

        _rolled.Clear();
        BlockLoot.Roll(previous, _random, _rolled);
        if (_rolled.Count == 0) return true;

        requests = [.. _rolled.Select(drop => new FloorDropFanout.DepositRequest(drop.Id, drop.Count))];
        if (FloorDropFanout.CanDepositBatch(_world, edit.X, edit.Y, edit.Z, requests)) return true;

        player.Level.Logger?.Debug(
            $"Break refused (no loot destination) for {player.Username} @ {edit.X},{edit.Y},{edit.Z}");
        return false;
    }

    /// <summary>Closes the lids, clears every viewer's open window on the chest, and dumps its storage (the loot is already planned).</summary>
    private void ReleaseBrokenChest(global::Zenith.Player.Player player, in BlockEditIntent edit, OpenChestView pairView, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        var lidBroken = _world.Chests.ClearOpeners(edit.X, edit.Y, edit.Z);
        var lidPartner = false;
        var partnerX = 0;
        var partnerY = 0;
        var partnerZ = 0;
        if (pairView.TryGetOther(edit.X, edit.Y, edit.Z, out partnerX, out partnerY, out partnerZ))
            lidPartner = _world.Chests.ClearOpeners(partnerX, partnerY, partnerZ);

        foreach (var peer in online)
        {
            if (peer.Windows.OpenChest is { } open && open.Contains(edit.X, edit.Y, edit.Z))
                _ = peer.Windows.TryClearOpenContainer(out _);
        }

        if (lidBroken)
            ChestLidFanout.Close(online, player.Client, edit.X, edit.Y, edit.Z);
        if (lidPartner)
            ChestLidFanout.Close(online, player.Client, partnerX, partnerY, partnerZ);

        _ = _world.Chests.RemoveAndDump(edit.X, edit.Y, edit.Z);

        // The partner is a single chest again.
        if (pairView.TryGetOther(edit.X, edit.Y, edit.Z, out partnerX, out partnerY, out partnerZ))
            ChestActorFanout.Publish(online, _world, partnerX, partnerY, partnerZ);
    }

    /// <summary>Applies the shape writes a placement/removal produced (the other half of a door); drops those a world refuses.</summary>
    private void CommitExtraWrites(SetBlockFlags flags = SetBlockFlags.Default)
    {
        for (var i = _extra.Count - 1; i >= 0; i--)
        {
            var write = _extra[i];
            if (!_world.TrySetBlock(write.X, write.Y, write.Z, write.BlockRuntimeId, flags))
                _extra.RemoveAt(i);
        }
    }

    /// <summary>Right-click open/close for doors, trapdoors and fence gates.</summary>
    private bool ApplyToggle(global::Zenith.Player.Player player, in BlockEditIntent edit)
    {
        if (!edit.IsInWorldBounds() || !IsWithinReach(player, edit.X, edit.Y, edit.Z)) return false;
        if (!BlockShapes.TryInteract(_world, edit.X, edit.Y, edit.Z, _extra)) return false;
        // The interaction is planned (nothing written yet), so the policies judge every cell it would flip before any of them does.
        var decision = EvaluateInteraction(player);
        if (!decision.IsAllowed)
        {
            player.Level.Logger?.Debug($"Interact denied for {player.Username} @ {edit.X},{edit.Y},{edit.Z}: {decision.Reason}");
            foreach (var cell in _cells) ResyncCellToBreaker(player, cell.X, cell.Y, cell.Z);
            _extra.Clear();
            return false;
        }

        // Opening or closing changes nothing a neighbour depends on.
        CommitExtraWrites(SetBlockFlags.None);
        if (_extra.Count == 0) return false;
        // A write the world refused was dropped from _extra; the fact names only what really changed.
        _cells.RemoveAll(cell => !_extra.Exists(write => write.X == cell.X && write.Y == cell.Y && write.Z == cell.Z));
        PublishApplied(player, BlockEditKind.Interact);
        return true;
    }

    /// <summary>Self UpdateBlock with server truth — kills client ghost after reject (§27).</summary>
    private void ResyncCellToBreaker(global::Zenith.Player.Player player, int x, int y, int z)
    {
        if (!player.IsInGame) return;
        player.Client.World.SendUpdateBlock(x, y, z, _world.GetBlock(x, y, z));
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
        BlockCrackFanout.Stop(online, player.Client, x, y, z);
        if (player.Dig.IsBreakTarget(x, y, z))
            player.Dig.AbortBreak();
        ResyncCellToBreaker(player, x, y, z);
    }

    /// <summary>
    /// A durable held item (ADR §148) takes one point of wear per block it breaks, and is gone when it reaches its
    /// maximum. Approximation, not yet checked against BDS: vanilla charges swords 2 and spares blocks that break
    /// instantly; here every dig-profiled block costs 1 and non-tools never reach this (they are not durable).
    /// </summary>
    private void WearHeldTool(global::Zenith.Player.Player player)
    {
        var slot = player.SelectedHotbarSlot;
        if (!PlayerInventory.IsValidHotbarSlot(slot)) return;

        var outcome = ItemWear.Apply(_durability, player.Inventory.Get(slot), 1, out var after);
        if (outcome == ItemWear.Outcome.NotDurable) return;

        if (!player.Inventory.TrySet(slot, after.IsEmpty ? StackId.FromBlock(Blocks.Air) : after.Id, after.IsEmpty ? 0 : after.Count))
            return;
        // The client predicts its own wear, but only the server's stack is authoritative: resend it, and persist.
        player.Client.Inventory.SendInventoryContent(player.Inventory);
        _world.Persistence.PersistInventory(player);
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
