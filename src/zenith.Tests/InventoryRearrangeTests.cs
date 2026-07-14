using Zenith.Network.Packets;
using Zenith.Network.Protocol;
using Zenith.Player;
using Xunit;

namespace Zenith.Tests;

public class InventoryContainerMapTests
{
    [Theory]
    [InlineData(InventoryContainerMap.Hotbar, 0, 0)]
    [InlineData(InventoryContainerMap.CombinedHotbarAndInventory, 8, 8)]
    [InlineData(InventoryContainerMap.Inventory, 0, 9)]
    [InlineData(InventoryContainerMap.Inventory, 26, 35)]
    [InlineData(InventoryContainerMap.Cursor, 0, PlayerInventory.CursorSlot)]
    public void Maps_wire_containers_to_flat(byte container, byte slot, int expectedFlat)
    {
        Assert.True(InventoryContainerMap.TryMap(container, slot, out var flat));
        Assert.Equal(expectedFlat, flat);
    }

    [Fact]
    public void Rejects_invalid_wire_slots()
    {
        Assert.False(InventoryContainerMap.TryMap(InventoryContainerMap.Hotbar, 9, out _));
        Assert.False(InventoryContainerMap.TryMap(InventoryContainerMap.Inventory, 27, out _));
        Assert.False(InventoryContainerMap.TryMap(99, 0, out _));
    }

    [Fact]
    public void Flat_to_wire_round_trip()
    {
        Assert.True(InventoryContainerMap.TryToWire(0, out var c0, out var s0));
        Assert.Equal(InventoryContainerMap.Hotbar, c0);
        Assert.Equal(0, s0);

        Assert.True(InventoryContainerMap.TryToWire(9, out var c9, out var s9));
        Assert.Equal(InventoryContainerMap.Inventory, c9);
        Assert.Equal(0, s9);

        Assert.True(InventoryContainerMap.TryToWire(PlayerInventory.CursorSlot, out var cc, out var sc));
        Assert.Equal(InventoryContainerMap.Cursor, cc);
        Assert.Equal(0, sc);
    }
}

public class InventoryPacketEncodeTests
{
    [Fact]
    public void ContainerOpen_encode_is_non_empty()
    {
        var packet = new ContainerOpenPacket
        {
            WindowId = 0,
            WindowType = ContainerOpenPacket.WindowTypeInventory,
            BlockX = 1,
            BlockY = 64,
            BlockZ = 2,
            ActorUniqueId = -1
        };
        Assert.True(packet.Encode().Length > 1);
    }

    [Fact]
    public void ItemStackResponse_error_and_ok_encode()
    {
        Assert.True(ItemStackResponsePacket.Error(5).Encode().Length > 1);
        Assert.True(ItemStackResponsePacket.Ok(5, [
            new StackResponseContainerInfo
            {
                Container = new FullContainerName { ContainerId = InventoryContainerMap.Hotbar },
                SlotInfo =
                [
                    new StackResponseSlotInfo
                    {
                        Slot = 0,
                        HotbarSlot = 0,
                        Count = 1,
                        StackNetworkId = 3
                    }
                ]
            }
        ]).Encode().Length > 1);
    }
}
