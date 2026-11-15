using Zenith.Binary;
using Zenith.Gameplay.Survival;
using Zenith.Packets;
using Zenith.Player;
using Zenith.Session.Handler;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public class FoodUseInteropTests
{
    private static (IntentTestFixture Fx, Player.Player Player, HungerSystem System) Fixture(string food = "apple", int count = 2)
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("food-use");
        player.Inventory.Clear(); player.SelectedHotbarSlot = 5; player.Hunger = 10; player.Saturation = 0;
        player.Inventory.TrySetItem(5, fx.Context.ItemPalette.Require("minecraft:" + food), count);
        return (fx, player, new HungerSystem(fx.Context.ItemPalette, fx.Players));
    }

    [Fact]
    public void Wire_food_use_waits_for_duration_and_only_consumes_the_selected_stack()
    {
        var (fx, player, system) = Fixture();
        player.Inventory.TrySetItem(0, fx.Context.ItemPalette.Require("minecraft:apple"), 3);
        SendUse(player); system.Tick(fx.Clock, fx.Players.Online);
        Assert.NotNull(player.FoodUse); Assert.Equal(10f, player.Hunger);
        fx.Clock.AdvanceBy(31); SendUse(player); system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(2, player.Inventory.Get(5).Count); Assert.Equal(10f, player.Hunger);
        fx.Clock.Advance(); system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(1, player.Inventory.Get(5).Count); Assert.Equal(3, player.Inventory.Get(0).Count);
        Assert.Equal(14f, player.Hunger); Assert.Equal(2.4f, player.Saturation);
        Assert.Null(player.FoodUse);
        system.Tick(fx.Clock, fx.Players.Online); Assert.Equal(1, player.Inventory.Get(5).Count);
    }

    [Fact]
    public void Wire_release_cancels_before_the_food_is_consumed()
    {
        var (fx, player, system) = Fixture();
        SendUse(player); system.Tick(fx.Clock, fx.Players.Online);
        fx.Clock.AdvanceBy(10); SendUse(player, release: true); system.Tick(fx.Clock, fx.Players.Online);
        fx.Clock.AdvanceBy(32); system.Tick(fx.Clock, fx.Players.Online);
        Assert.Null(player.FoodUse); Assert.Equal(2, player.Inventory.Get(5).Count); Assert.Equal(10f, player.Hunger);
    }

    [Fact]
    public void Switching_slots_cancels_even_when_both_hold_the_same_food()
    {
        var (fx, player, system) = Fixture();
        player.Inventory.TrySetItem(0, fx.Context.ItemPalette.Require("minecraft:apple"), 3);
        SendUse(player); system.Tick(fx.Clock, fx.Players.Online);
        player.SelectedHotbarSlot = 0; fx.Clock.AdvanceBy(32); system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(3, player.Inventory.Get(0).Count); Assert.Equal(2, player.Inventory.Get(5).Count);
        Assert.Equal(10f, player.Hunger); Assert.Null(player.FoodUse);
    }

    [Theory]
    [InlineData("cooked_mutton", 32, 16f, 9.6f)]
    [InlineData("dried_kelp", 16, 11f, 0.6f)]
    [InlineData("pumpkin_pie", 32, 18f, 4.8f)]
    public void Expanded_foods_use_their_own_duration_and_saturation(string name, int ticks, float hunger, float saturation)
    {
        var (fx, player, system) = Fixture(name);
        SendUse(player); system.Tick(fx.Clock, fx.Players.Online);
        fx.Clock.AdvanceBy(ticks - 1); system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(2, player.Inventory.Get(5).Count);
        fx.Clock.Advance(); system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(hunger, player.Hunger); Assert.Equal(saturation, player.Saturation);
        Assert.Equal(1, player.Inventory.Get(5).Count);
    }

    [Theory]
    [InlineData("mushroom_stew", "bowl", 32)]
    [InlineData("honey_bottle", "glass_bottle", 40)]
    public void Final_food_returns_its_container_to_the_held_slot(string name, string remainder, int ticks)
    {
        var (fx, player, system) = Fixture(name, 1);
        SendUse(player); system.Tick(fx.Clock, fx.Players.Online);
        fx.Clock.AdvanceBy(ticks); system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(StackId.FromItem(fx.Context.ItemPalette.Require("minecraft:" + remainder)), player.Inventory.Get(5).Id);
        Assert.Equal(1, player.Inventory.Get(5).Count);
    }

    [Fact]
    public void Cancel_is_retained_when_the_food_start_queue_is_full()
    {
        var (fx, player, system) = Fixture();
        for (var i = 0; i < 8; i++) Assert.True(player.Intents.SubmitEatIntent(5, player.Inventory.Get(5)));
        Assert.False(player.Intents.SubmitEatIntent(5, player.Inventory.Get(5)));
        player.Intents.SubmitCancelFoodUse(); system.Tick(fx.Clock, fx.Players.Online);
        fx.Clock.AdvanceBy(32); system.Tick(fx.Clock, fx.Players.Online);
        Assert.Null(player.FoodUse); Assert.Equal(2, player.Inventory.Get(5).Count);
    }

    [Fact]
    public void Using_item_metadata_preserves_existing_pose_flags()
    {
        var flags = EntityMetadataWriter.BuildPoseFlags(true, true, true);
        Assert.NotEqual(0, flags & EntityFlag.Bit(EntityFlag.UsingItem));
        Assert.NotEqual(0, flags & EntityFlag.Bit(EntityFlag.Sneaking));
        Assert.NotEqual(0, flags & EntityFlag.Bit(EntityFlag.Sprinting));
        Assert.Equal(0, EntityMetadataWriter.BuildPoseFlags(true, true) & EntityFlag.Bit(EntityFlag.UsingItem));
    }

    [Theory]
    [InlineData("apple", false)]
    [InlineData("golden_apple", true)]
    public void Full_hunger_only_allows_food_marked_always_edible(string name, bool consumed)
    {
        var (fx, player, system) = Fixture(name);
        player.Hunger = 20;
        SendUse(player); system.Tick(fx.Clock, fx.Players.Online);
        fx.Clock.AdvanceBy(32); system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(consumed ? 1 : 2, player.Inventory.Get(5).Count);
        Assert.Equal(20f, player.Hunger);
    }

    [Fact]
    public void Actor_event_progress_does_not_complete_the_authoritative_food_use()
    {
        var (fx, player, system) = Fixture();
        SendUse(player); system.Tick(fx.Clock, fx.Players.Online);
        var packet = new ActorEventPacket { ActorRuntimeId = (ulong)player.RuntimeId, EventId = 57, EventData = 123 };
        var reader = new BinaryStream(packet.Encode().ToArray()); reader.ReadUnsignedVarInt();
        Assert.True(new InGameSessionHandler().HandleDataPacket(player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.ACTOR_EVENT_PACKET }, ref reader));
        reader.Dispose(); fx.Clock.Advance(); system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(10f, player.Hunger); Assert.Equal(2, player.Inventory.Get(5).Count);
        Assert.NotNull(player.FoodUse);
    }

    [Fact]
    public void Container_overflow_is_dropped_without_losing_or_duplicating_the_food()
    {
        var (fx, player, system) = Fixture("honey_bottle");
        for (var slot = 0; slot < PlayerInventory.FullInventorySize; slot++)
            if (slot != 5) player.Inventory.TrySetBlock(slot, Blocks.Stone, PlayerInventory.MaxStack);
        SendUse(player); system.Tick(fx.Clock, fx.Players.Online);
        fx.Clock.AdvanceBy(40); system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(1, player.Inventory.Get(5).Count);
        var drop = Assert.Single(fx.World.FloorDrops.Snapshot());
        Assert.Equal(StackId.FromItem(fx.Context.ItemPalette.Require("minecraft:glass_bottle")), drop.Id);
        Assert.Equal(1, drop.Count);
    }

    private static void SendUse(Player.Player player, bool release = false)
    {
        var writer = new BinaryStream(); writer.WriteVarInt(0); writer.WriteBool(false);
        writer.WriteUnsignedVarInt((int)(release ? InventoryTransactionPacket.TypeItemRelease : InventoryTransactionPacket.TypeItemUse));
        writer.WriteUnsignedVarInt(0); writer.WriteVarInt(release ? 0 : InventoryTransactionPacket.UseClickAir);
        if (!release)
        {
            writer.WriteByte(0); writer.WriteVarInt(0); writer.WriteVarInt(0); writer.WriteVarInt(0); writer.WriteByte(0);
        }
        writer.WriteVarInt(5);
        if (!release) writer.WriteByte(0);
        player.Session.Protocol.Inventory.DescribeSlot(player.Inventory, 5).WriteNetworkItemStackDescriptor(ref writer);
        for (var i = 0; i < (release ? 3 : 6); i++) writer.WriteFloat(0, BinaryStream.Endianess.Little);
        if (release) writer.WriteByte(0);
        else { writer.WriteUnsignedVarInt(0); writer.WriteByte(0); writer.WriteByte(0); }
        var reader = new BinaryStream(writer.GetBufferDisposing().ToArray());
        Assert.True(new InGameSessionHandler().HandleDataPacket(player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET }, ref reader));
        reader.Dispose();
    }
}
