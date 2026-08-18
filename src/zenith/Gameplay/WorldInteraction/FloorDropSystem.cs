using System.Collections.Generic;
using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.World;
using Zenith.Gameplay.Replication;

namespace Zenith.Gameplay.WorldInteraction;

/// <summary>
/// Advances the authoritative lifecycle of floor-drop entities: pickup delay, despawn and pickup.
/// This is separate from block edits because each drop must be evaluated every gameplay tick.
/// </summary>
sealed class FloorDropSystem : IGameSystem
{
    private readonly World.World _world;
    private readonly List<(int X, int Y, int Z, StackId Id, int Count, long EntityRuntimeId)> _despawnScratch = new();

    public FloorDropSystem(World.World world)
    {
        _world = world;
    }

    public void Tick(GameClock clock, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        _ = clock;
        _world.FloorDrops.TickPickupDelays();

        _despawnScratch.Clear();
        _world.FloorDrops.TickDespawn(_despawnScratch);
        NotifyDespawns(_despawnScratch, online);

        if (_world.FloorDrops.Count == 0)
            return;

        foreach (var drop in _world.FloorDrops.Snapshot())
            TryPickup(drop, online);
    }

    private static void NotifyDespawns(
        List<(int X, int Y, int Z, StackId Id, int Count, long EntityRuntimeId)> despawned,
        IReadOnlyList<global::Zenith.Player.Player> online)
    {
        foreach (var (x, y, z, _, _, entityRuntimeId) in despawned)
        {
            var cx = PlayerChunkTracker.BlockToChunk(x);
            var cz = PlayerChunkTracker.BlockToChunk(z);
            foreach (var peer in online)
            {
                if (!peer.IsInGame && !peer.Chunks.Knows(cx, cz)) continue;
                peer.Session.Protocol.Entity.SendRemoveActor(entityRuntimeId);
            }
        }
    }

    /// <summary>Drop × Players search — any in-range player may pick it up, so this genuinely needs
    /// to scan <paramref name="online"/> (unlike a single-player operation with online threaded in
    /// only for replication). The inventory mutation itself (<c>TryAddUpTo</c>) never took
    /// <c>online</c> — only the search and the post-pickup notification do.</summary>
    private void TryPickup(
        ((int X, int Y, int Z) Pos, StackId Id, int Count, long EntityRuntimeId, int PickupDelayTicks, int AgeTicks) drop,
        IReadOnlyList<global::Zenith.Player.Player> online)
    {
        if (drop.PickupDelayTicks > 0) return;

        var pickupId = drop.Id.IsBlock
            ? StackId.FromBlock(Blocks.NormalizeMergeRuntimeId(drop.Id.Value))
            : drop.Id;
        foreach (var player in online)
        {
            if (!player.IsInGame || player.IsDead) continue;
            if (!IsWithinPickupReach(player, drop.Pos.X, drop.Pos.Y, drop.Pos.Z)) continue;

            var invSnap = player.Inventory.CaptureSnapshot();
            var added = player.Inventory.TryAddUpTo(pickupId, drop.Count);
            if (added == 0) continue;

            if (!_world.FloorDrops.TryTakeUpTo(
                    drop.Pos.X, drop.Pos.Y, drop.Pos.Z, added,
                    out _, out _, out var takenEntity, out var remainingPublish))
            {
                player.Inventory.RestoreSnapshot(invSnap);
                continue;
            }

            var eid = takenEntity != 0 ? takenEntity : drop.EntityRuntimeId;
            NotifyPickup(player, eid, drop.Pos.X, drop.Pos.Z, remainingPublish, online);

            player.Session.Protocol.Inventory.SendInventoryContent(player.Inventory);
            _world.PersistInventory(player);
            return;
        }
    }

    private static void NotifyPickup(
        global::Zenith.Player.Player player, long eid, int x, int z, FloorDropStore.DepositResult? remainingPublish,
        IReadOnlyList<global::Zenith.Player.Player> online)
    {
        var cx = PlayerChunkTracker.BlockToChunk(x);
        var cz = PlayerChunkTracker.BlockToChunk(z);
        foreach (var peer in online)
        {
            if (!peer.IsInGame && !peer.Chunks.Knows(cx, cz)) continue;
            peer.Session.Protocol.Entity.SendTakeItemActor((ulong)eid, (ulong)player.RuntimeId);
            // TakeItemActor is only the pickup animation, not a despawn (confirmed against
            // PocketMine/Dragonfly: both always pair it with a real actor removal). A full
            // take has no later tick to emit one — the cell is already gone from the store —
            // so without this the client renders an orphaned item forever after pickup.
            if (remainingPublish is null)
                peer.Session.Protocol.Entity.SendRemoveActor(eid);
        }

        // Partial pickup replaces the actor with the remaining authoritative stack.
        if (remainingPublish is { } rem)
            FloorDropFanout.Publish(online, rem);
    }

    /// <summary>
    /// Pickup — standing AABB expanded by <see cref="EntityHitboxes.PickupExpand"/>
    /// vs item entity AABB at cell. Domain <see cref="Player.Player.PositionY"/> is feet.
    /// </summary>
    internal static bool IsWithinPickupReach(global::Zenith.Player.Player player, int x, int y, int z)
    {
        var playerBb = EntityHitboxes.PlayerStanding(player.PositionX, player.PositionY, player.PositionZ)
            .Expand(EntityHitboxes.PickupExpand);
        var itemBb = EntityHitboxes.ItemAtCell(x, y, z);
        return playerBb.Intersects(itemBb);
    }
}
