using Zenith.Packets;
using Zenith.Player;
using Zenith.Protocol;
using Zenith.World;
using Xunit;
using Zenith.Gameplay.WorldInteraction;

namespace Zenith.Tests;

public class ChestPairTests
{
    public ChestPairTests() => Blocks.EnsureLoaded();

    [Fact]
    public void TryResolve_same_facing_adjacent_on_pair_axis()
    {
        var fx = new IntentTestFixture();
        var south = Blocks.ChestForFacing(Blocks.CardinalSouth);
        fx.World.SetBlock(0, 64, 0, south);
        fx.World.SetBlock(1, 64, 0, south);

        Assert.True(ChestPairing.TryResolve(fx.World, 0, 64, 0, out var pair));
        Assert.Equal(0, pair.PrimaryX);
        Assert.Equal(0, pair.PrimaryZ);
        Assert.Equal(1, pair.PartnerX);
        Assert.Equal(0, pair.PartnerZ);

        Assert.True(ChestPairing.TryResolve(fx.World, 1, 64, 0, out var fromPartner));
        Assert.Equal(pair, fromPartner);
    }

    [Fact]
    public void TryResolve_rejects_mismatched_facing_or_wrong_axis()
    {
        var fx = new IntentTestFixture();
        fx.World.SetBlock(0, 64, 0, Blocks.ChestForFacing(Blocks.CardinalSouth));
        fx.World.SetBlock(1, 64, 0, Blocks.ChestForFacing(Blocks.CardinalNorth));
        Assert.False(ChestPairing.TryResolve(fx.World, 0, 64, 0, out _));

        // South-facing pairs on X only — neighbor on Z must not resolve.
        fx.World.SetBlock(1, 64, 0, Blocks.Air);
        fx.World.SetBlock(0, 64, 1, Blocks.ChestForFacing(Blocks.CardinalSouth));
        Assert.False(ChestPairing.TryResolve(fx.World, 0, 64, 0, out _));
    }

    [Fact]
    public void AlignFacingWithNeighbor_adopts_pair_axis_neighbor()
    {
        var fx = new IntentTestFixture();
        var north = Blocks.ChestForFacing(Blocks.CardinalNorth);
        fx.World.SetBlock(5, 70, 5, north);
        var yawEast = Blocks.ChestForFacing(Blocks.CardinalEast);
        var aligned = ChestPairing.AlignFacingWithNeighbor(fx.World, 6, 70, 5, yawEast);
        Assert.Equal(north, aligned);
    }

    [Fact]
    public void TryPeekMovementInput_exposes_pending_sneak_for_same_packet_use()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("sneak");
        Assert.False(player.IsSneaking);
        player.SubmitMovementInput(MovementInputState.FromClientAuthInput(
            0, Blocks.FlatSpawnY + Blocks.PlayerEyeHeight, 0, 0, 0, sneaking: true));
        Assert.True(player.TryPeekMovementInput(out var pending));
        Assert.True(pending.Sneaking);
        Assert.False(player.IsSneaking);
        Assert.True(player.TryConsumeMovementInput(out _));
    }

    [Fact]
    public void Open_paired_chests_is_54_and_ISR_crosses_halves()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("pair");
        var south = Blocks.ChestForFacing(Blocks.CardinalSouth);
        fx.World.SetBlock(2, 64, 2, south);
        fx.World.SetBlock(3, 64, 2, south);
        fx.World.Chests.Ensure(2, 64, 2);
        fx.World.Chests.Ensure(3, 64, 2);
        player.PositionX = 2.5f;
        player.PositionY = 64;
        player.PositionZ = 2.5f;

        Assert.True(player.SubmitWindowIntent(InventoryWindowIntent.OpenChest(3, 64, 2)));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.NotNull(player.OpenChest);
        Assert.Equal(ChestStore.DoubleSize, player.OpenChest!.Value.SlotCount);
        Assert.Equal(1, fx.World.Chests.OpenerCount(2, 64, 2));
        Assert.Equal(1, fx.World.Chests.OpenerCount(3, 64, 2));

        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 5));
        var partnerFlat = InventoryContainerMap.ChestBase + ChestStore.SingleSize;
        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(1, [
            InventoryStackAction.Transfer(
                0, partnerFlat, 3,
                new WireSlot(InventoryContainerMap.Hotbar, 0),
                new WireSlot(InventoryContainerMap.Chest, (byte)ChestStore.SingleSize))
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(2, player.Inventory.Get(0).Count);
        Assert.Equal(3, fx.World.Chests.Get(3, 64, 2, 0).Count);
        Assert.Equal(Blocks.Dirt, fx.World.Chests.Get(3, 64, 2, 0).Id.Value);
    }

    [Fact]
    public void Break_one_half_dumps_that_cell_and_dissolves_pair()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("breaker");
        player.PositionX = 2.5f;
        player.PositionY = 64;
        player.PositionZ = 2.5f;

        var south = Blocks.ChestForFacing(Blocks.CardinalSouth);
        fx.World.SetBlock(2, 64, 2, south);
        fx.World.SetBlock(3, 64, 2, south);
        fx.World.Chests.Ensure(2, 64, 2);
        fx.World.Chests.Ensure(3, 64, 2);
        Assert.True(fx.World.Chests.TrySet(2, 64, 2, 0, InventorySlot.OfBlock(Blocks.Dirt, 4)));
        Assert.True(fx.World.Chests.TrySet(3, 64, 2, 0, InventorySlot.OfBlock(Blocks.Stone, 2)));

        Assert.True(player.SubmitWindowIntent(InventoryWindowIntent.OpenChest(2, 64, 2)));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(ChestStore.DoubleSize, player.OpenChest!.Value.SlotCount);

        var need = Blocks.BreakTicks(south);
        player.BeginBreak(2, 64, 2, fx.Clock.CurrentTick, need);
        if (need > 0)
            fx.Clock.AdvanceBy(need);
        Assert.True(player.SubmitBlockEdit(
            need > 0
                ? BlockEditIntent.BreakWithDig(2, 64, 2, player.BreakStartedTick, player.BreakRequiredTicks)
                : BlockEditIntent.Set(2, 64, 2, Blocks.Air)));
        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Air, fx.World.GetBlock(2, 64, 2));
        Assert.Equal(south, fx.World.GetBlock(3, 64, 2));
        Assert.False(fx.World.Chests.TryGetSlots(2, 64, 2, out _));
        Assert.True(fx.World.Chests.TryGetSlots(3, 64, 2, out _));
        Assert.Equal(2, fx.World.Chests.Get(3, 64, 2, 0).Count);
        Assert.Null(player.OpenChest);
        Assert.False(ChestPairing.TryResolve(fx.World, 3, 64, 2, out _));
    }

    [Fact]
    public async Task Persist_round_trip_keeps_two_ct_halves()
    {
        var storage = new InMemoryChunkStorage();
        var world = new World.World(storage);
        var south = Blocks.ChestForFacing(Blocks.CardinalSouth);
        world.SetBlock(10, 64, 10, south);
        world.SetBlock(11, 64, 10, south);
        world.Chests.Ensure(10, 64, 10);
        world.Chests.Ensure(11, 64, 10);
        Assert.True(world.Chests.TrySet(10, 64, 10, 1, InventorySlot.OfBlock(Blocks.Dirt, 9)));
        Assert.True(world.Chests.TrySet(11, 64, 10, 2, InventorySlot.OfBlock(Blocks.Sand, 3)));
        world.PersistChest(10, 64, 10);
        world.PersistChest(11, 64, 10);
        await world.FlushPersistenceAsync();

        var reloaded = new World.World(storage);
        await reloaded.GetOrCreateColumnAsync(0, 0); // ADR §114 — chests hydrate lazily on first touch
        Assert.Equal(9, reloaded.Chests.Get(10, 64, 10, 1).Count);
        Assert.Equal(3, reloaded.Chests.Get(11, 64, 10, 2).Count);
        Assert.Equal(Blocks.Dirt, reloaded.Chests.Get(10, 64, 10, 1).Id.Value);
        Assert.Equal(Blocks.Sand, reloaded.Chests.Get(11, 64, 10, 2).Id.Value);
    }

    [Fact]
    public void GetOpen_concatenates_primary_then_partner()
    {
        var store = new ChestStore();
        store.Ensure(0, 1, 0);
        store.Ensure(1, 1, 0);
        Assert.True(store.TrySet(0, 1, 0, 0, InventorySlot.OfBlock(Blocks.Dirt, 1)));
        Assert.True(store.TrySet(1, 1, 0, 0, InventorySlot.OfBlock(Blocks.Stone, 2)));
        var view = OpenChestView.Double(new ChestPair(0, 1, 0, 1, 1, 0));
        Assert.Equal(Blocks.Dirt, store.GetOpen(view, 0).Id.Value);
        Assert.Equal(Blocks.Stone, store.GetOpen(view, ChestStore.SingleSize).Id.Value);
    }
}
