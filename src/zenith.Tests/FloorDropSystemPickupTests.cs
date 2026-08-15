using Zenith.Packets;
using Zenith.Player;
using Zenith.World;
using Xunit;
using Zenith.Gameplay.WorldInteraction;

namespace Zenith.Tests;

/// <summary>
/// Phase XXIII — regression coverage for a real bug found via live-client testing: picking up more
/// than one floor-drop cell in the same tick threw "Collection was modified" because
/// <see cref="FloorDropStore.Snapshot"/> used to be a lazy <c>yield return</c> generator over the
/// live dictionary, while <see cref="FloorDropSystem"/>'s pickup loop mutates that same dictionary
/// (via <c>TryTakeUpTo</c>) mid-enumeration. The uncaught exception also permanently killed
/// <c>GameLoop</c>'s tick task (it re-throws system failures), matching reports of "only 1 of 3
/// items picked up" and "no more damage/inventory after death" (players routinely pick up several
/// drops — their own death loot, or several mob kills — in one tick).
/// </summary>
public class FloorDropSystemPickupTests
{
    public FloorDropSystemPickupTests() => Blocks.EnsureLoaded();

    [Fact]
    public void Picking_up_two_separate_drop_cells_in_one_tick_does_not_throw_and_collects_both()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("looter");
        ClearInventory(player);
        player.PositionX = 0f;
        player.PositionY = Blocks.FlatSpawnY;
        player.PositionZ = 0f;

        // Two distinct cells, both within pickup reach of the same player position, so the pickup
        // loop must process both in the same FloorDropSystem.Tick() call.
        // Both cells well within EntityHitboxes.PickupExpand (±1.3 in X) of a player standing at x=0.
        Assert.True(fx.World.FloorDrops.TryAddOrMerge(0, (int)Blocks.FlatSpawnY, 0, StackId.FromItem(fx.Context.ItemPalette.Require("minecraft:string")), 1, entityRuntimeIdIfNew: 101, out _, pickupDelayTicks: 0));
        Assert.True(fx.World.FloorDrops.TryAddOrMerge(-1, (int)Blocks.FlatSpawnY, 0, StackId.FromItem(fx.Context.ItemPalette.Require("minecraft:string")), 1, entityRuntimeIdIfNew: 102, out _, pickupDelayTicks: 0));
        Assert.Equal(2, fx.World.FloorDrops.Count);

        var system = new FloorDropSystem(fx.World);
        var exception = Record.Exception(() => system.Tick(fx.Clock, fx.Players.Online));

        Assert.Null(exception);
        Assert.Empty(fx.World.FloorDrops.Snapshot());
        var stringCount = Enumerable.Range(0, PlayerInventory.FullInventorySize)
            .Select(player.Inventory.Get)
            .Where(slot => slot.Id == StackId.FromItem(fx.Context.ItemPalette.Require("minecraft:string")))
            .Sum(slot => slot.Count);
        Assert.Equal(2, stringCount);
    }

    /// <summary>
    /// Phase XXVI — a full pickup only ever sent TakeItemActor (the pickup animation), never
    /// RemoveActor. TakeItemActor alone does not despawn the entity on a Bedrock client (confirmed
    /// against PocketMine/Dragonfly, which always pair it with a real removal) — without RemoveActor,
    /// and with the cell already gone from the store so no later despawn tick can catch it either,
    /// the item stayed rendered on the ground forever after being picked up.
    /// </summary>
    [Fact]
    public void Full_pickup_sends_RemoveActor_so_the_client_does_not_keep_rendering_it()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("looter");
        ClearInventory(player);
        player.PositionX = 0f;
        player.PositionY = Blocks.FlatSpawnY;
        player.PositionZ = 0f;

        const long entityRuntimeId = 555;
        Assert.True(fx.World.FloorDrops.TryAddOrMerge(
            0, (int)Blocks.FlatSpawnY, 0, StackId.FromItem(fx.Context.ItemPalette.Require("minecraft:string")), 1,
            entityRuntimeIdIfNew: entityRuntimeId, out _, pickupDelayTicks: 0));

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        new FloorDropSystem(fx.World).Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);

        var expected = new RemoveActorPacket { ActorUniqueId = entityRuntimeId }.Encode().ToArray();
        Assert.Contains(expected, ConcatCaptured(fx));
    }

    private static void FlushRaknet(PlayerManager players)
    {
        foreach (var p in players.Online)
            p.Session.RakSession.Tick();
    }

    private static byte[] ConcatCaptured(IntentTestFixture fx)
    {
        var total = 0;
        foreach (var chunk in fx.Transport.Captured)
            total += chunk.Length;
        var buf = new byte[total];
        var offset = 0;
        foreach (var chunk in fx.Transport.Captured)
        {
            chunk.CopyTo(buf, offset);
            offset += chunk.Length;
        }

        return buf;
    }

    [Fact]
    public void Snapshot_is_a_real_copy_unaffected_by_concurrent_mutation()
    {
        var store = new FloorDropStore();
        Assert.True(store.TryAddOrMerge(0, 64, 0, StackId.FromBlock(Blocks.Stone), 1, entityRuntimeIdIfNew: 1, out _));
        Assert.True(store.TryAddOrMerge(1, 64, 0, StackId.FromBlock(Blocks.Stone), 1, entityRuntimeIdIfNew: 2, out _));

        var snapshot = store.Snapshot();
        Assert.True(store.TryTake(0, 64, 0, out _, out _, out _));
        Assert.True(store.TryTake(1, 64, 0, out _, out _, out _));

        // The earlier snapshot must still report both cells — mutating the store afterward must not
        // retroactively change (or, worse, throw while re-enumerating) an already-captured Snapshot().
        Assert.Equal(2, snapshot.Count);
    }

    private static void ClearInventory(Player.Player player)
    {
        for (var slot = 0; slot < PlayerInventory.FullInventorySize; slot++)
            player.Inventory.TrySetBlock(slot, Blocks.Air, 0);
    }
}
