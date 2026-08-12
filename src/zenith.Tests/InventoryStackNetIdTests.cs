using Zenith.Gameplay.Systems;
using Zenith.Player;
using Zenith.Protocol;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public class InventoryStackNetIdTests
{
    public InventoryStackNetIdTests() => Blocks.EnsureLoaded();

    [Fact]
    public void DescribeForWire_keeps_id_when_rid_and_count_unchanged()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("wire");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 8));
        var inv = player.Session.Protocol.Inventory;

        var a = inv.DescribeForWire(0, player.Inventory.Get(0));
        var b = inv.DescribeForWire(0, player.Inventory.Get(0));
        Assert.NotEqual(0, a.StackNetworkId);
        Assert.Equal(a.StackNetworkId, b.StackNetworkId);
    }

    [Fact]
    public void DescribeForWire_remints_when_count_changes()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("remint");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 8));
        var inv = player.Session.Protocol.Inventory;

        var a = inv.DescribeForWire(0, player.Inventory.Get(0));
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 7));
        var b = inv.DescribeForWire(0, player.Inventory.Get(0));
        Assert.NotEqual(a.StackNetworkId, b.StackNetworkId);
    }

    [Fact]
    public void DescribeForWire_projects_palette_item_that_is_not_a_tool()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("bone-wire");
        var bone = fx.Context.ItemPalette.Require("minecraft:bone");
        Assert.True(player.Inventory.TrySetItem(0, bone, 3));

        var wire = player.Session.Protocol.Inventory.DescribeForWire(0, player.Inventory.Get(0));

        Assert.Equal(bone, wire.NetworkId);
        Assert.Equal((ushort)3, wire.Count);
        Assert.Equal(0, wire.BlockRuntimeId);
        Assert.NotEqual(0, wire.StackNetworkId);
    }

    [Fact]
    public void BeginOpenContainerSession_remints_open_container_stack_ids()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("new-view");
        var inv = player.Session.Protocol.Inventory;
        var reference = InventorySlotReference.OpenContainer(0);
        var dirt = InventorySlot.OfBlock(Blocks.Dirt, 1);

        inv.BeginOpenContainerSession(1);
        var first = inv.DescribeForWire(reference, dirt);
        inv.BeginOpenContainerSession(2);
        var second = inv.DescribeForWire(reference, dirt);

        Assert.NotEqual(0, first.StackNetworkId);
        Assert.NotEqual(first.StackNetworkId, second.StackNetworkId);
    }

    [Fact]
    public void MatchesAdvertised_skips_non_positive_client_ids()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("soft");
        var inv = player.Session.Protocol.Inventory;
        Assert.True(inv.MatchesAdvertisedStackNetId(0, 0));
        Assert.True(inv.MatchesAdvertisedStackNetId(0, -1));
    }

    [Fact]
    public void MatchesAdvertised_rejects_wrong_positive_id()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("mismatch");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 1));
        var inv = player.Session.Protocol.Inventory;
        var wire = inv.DescribeForWire(0, player.Inventory.Get(0));
        Assert.True(inv.MatchesAdvertisedStackNetId(0, wire.StackNetworkId));
        Assert.False(inv.MatchesAdvertisedStackNetId(0, wire.StackNetworkId + 99));
    }

    [Fact]
    public void InventorySystem_rejects_transfer_on_stack_net_id_mismatch()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("isr");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 4));
        Assert.True(player.Inventory.TrySetBlock(9, Blocks.Air, 0));
        var inv = player.Session.Protocol.Inventory;
        inv.SendInventoryContent(player.Inventory);
        var advertised = inv.DescribeForWire(0, player.Inventory.Get(0)).StackNetworkId;

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(
            requestId: 7,
            actions:
            [
                InventoryStackAction.Transfer(
                    0, 9, 4,
                    new WireSlot(InventoryContainerMap.Hotbar, 0, advertised + 1),
                    new WireSlot(InventoryContainerMap.Inventory, 9, 0))
            ])));

        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(4, player.Inventory.Get(0).Count);
        Assert.Equal(Blocks.Stone, player.Inventory.Get(0).Id.Value);
        Assert.True(player.Inventory.Get(9).IsEmpty);
    }

    [Fact]
    public void InventorySystem_accepts_transfer_when_stack_net_id_matches()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("ok");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 4));
        Assert.True(player.Inventory.TrySetBlock(9, Blocks.Air, 0));
        var inv = player.Session.Protocol.Inventory;
        inv.SendInventoryContent(player.Inventory);
        var advertised = inv.DescribeForWire(0, player.Inventory.Get(0)).StackNetworkId;

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(
            requestId: 8,
            actions:
            [
                InventoryStackAction.Transfer(
                    0, 9, 4,
                    new WireSlot(InventoryContainerMap.Hotbar, 0, advertised),
                    new WireSlot(InventoryContainerMap.Inventory, 9, 0))
            ])));

        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.Inventory.Get(0).IsEmpty);
        Assert.Equal(4, player.Inventory.Get(9).Count);
        Assert.Equal(Blocks.Stone, player.Inventory.Get(9).Id.Value);
    }
}
