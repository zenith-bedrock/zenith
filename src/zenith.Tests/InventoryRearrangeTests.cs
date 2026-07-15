using Zenith.Packets;
using Zenith.Protocol;
using Zenith.Player;
using Zenith.Raknet.Stream;
using Xunit;

namespace Zenith.Tests;

public class InventoryContainerMapTests
{
    [Theory]
    [InlineData(InventoryContainerMap.Hotbar, 0, 0)]
    [InlineData(InventoryContainerMap.CombinedHotbarAndInventory, 8, 8)]
    [InlineData(InventoryContainerMap.CombinedHotbarAndInventory, 9, 9)]
    [InlineData(InventoryContainerMap.CombinedHotbarAndInventory, 35, 35)]
    [InlineData(InventoryContainerMap.Inventory, 0, 9)]
    [InlineData(InventoryContainerMap.Inventory, 26, 35)]
    [InlineData(InventoryContainerMap.Cursor, 0, PlayerInventory.CursorSlot)]
    [InlineData(InventoryContainerMap.Chest, 0, InventoryContainerMap.ChestBase)]
    [InlineData(InventoryContainerMap.Chest, 26, InventoryContainerMap.ChestBase + 26)]
    public void Maps_wire_containers_to_flat(byte container, byte slot, int expectedFlat)
    {
        Assert.True(InventoryContainerMap.TryMap(container, slot, out var flat));
        Assert.Equal(expectedFlat, flat);
    }

    [Fact]
    public void Rejects_invalid_wire_slots()
    {
        Assert.False(InventoryContainerMap.TryMap(InventoryContainerMap.Hotbar, 9, out _));
        Assert.False(InventoryContainerMap.TryMap(InventoryContainerMap.CombinedHotbarAndInventory, 36, out _));
        Assert.False(InventoryContainerMap.TryMap(InventoryContainerMap.Inventory, 27, out _));
        Assert.False(InventoryContainerMap.TryMap(InventoryContainerMap.Chest, 27, out _));
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

        Assert.True(InventoryContainerMap.TryToWire(InventoryContainerMap.ChestBase + 3, out var c7, out var s7));
        Assert.Equal(InventoryContainerMap.Chest, c7);
        Assert.Equal(3, s7);
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

    [Fact]
    public void MobEquipment_encode_with_held_item_is_non_empty()
    {
        var packet = new MobEquipmentPacket
        {
            ActorRuntimeId = 2,
            Item = new NetworkItemStack(1, 10, 100),
            InventorySlot = 0,
            HotbarSlot = 0
        };
        Assert.True(packet.Encode().Length > 4);
    }

    [Fact]
    public void AddPlayer_encode_includes_legacy_held_item()
    {
        var air = new AddPlayerPacket { Username = "a", HeldItem = NetworkItemStack.Empty }.Encode();
        var held = new AddPlayerPacket
        {
            Username = "a",
            HeldItem = new NetworkItemStack(1, 3, 50)
        }.Encode();
        Assert.True(held.Length > air.Length);
    }

    [Fact]
    public void InventoryContent_encode_shape_matches_slot_count()
    {
        var empty = new InventoryContentPacket
        {
            WindowId = InventoryContentPacket.WindowInventory,
            Slots = []
        }.Encode();
        var withSlots = new InventoryContentPacket
        {
            WindowId = InventoryContentPacket.WindowInventory,
            Slots =
            [
                NetworkItemStack.Empty,
                new NetworkItemStack(1, 64, 100, StackNetworkId: 2)
            ]
        }.Encode();

        Assert.True(empty.Length > 1);
        Assert.True(withSlots.Length > empty.Length);
    }

    [Fact]
    public void ItemStackRequest_marks_PlaceInContainer_supported()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(1); // request count
        writer.WriteVarInt(1); // request id
        writer.WriteUnsignedVarInt(1); // action count
        writer.WriteByte(ItemStackRequestPacket.ActionPlaceInContainer);
        writer.WriteByte(1); // count
        // source FullContainerName + slot + net id
        writer.WriteByte(7);
        writer.WriteBool(false);
        writer.WriteByte(0);
        writer.WriteVarInt(1);
        // dest
        writer.WriteByte(28);
        writer.WriteBool(false);
        writer.WriteByte(0);
        writer.WriteVarInt(2);
        writer.WriteUnsignedVarInt(0); // filter strings
        writer.WriteInt(0, BinaryStream.Endianess.Little); // filter cause

        var buf = writer.GetBufferDisposing().ToArray();
        var stream = new BinaryStream(buf);
        var packet = new ItemStackRequestPacket();
        packet.Decode(ref stream);
        Assert.Single(packet.Requests);
        Assert.True(packet.Requests[0].AllSupported);
        Assert.True(packet.Requests[0].Actions[0].Supported);
    }

    [Fact]
    public void ItemStackRequest_decodes_CraftRecipe_net_id()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(1);
        writer.WriteVarInt(3);
        writer.WriteUnsignedVarInt(1);
        writer.WriteByte(ItemStackRequestPacket.ActionCraftRecipe);
        writer.WriteUnsignedVarInt(1); // recipe net id
        writer.WriteByte(1); // times
        writer.WriteUnsignedVarInt(0);
        writer.WriteInt(0, BinaryStream.Endianess.Little);

        var buf = writer.GetBufferDisposing().ToArray();
        var stream = new BinaryStream(buf);
        var packet = new ItemStackRequestPacket();
        packet.Decode(ref stream);
        Assert.True(packet.Requests[0].AllSupported);
        Assert.Equal(1u, packet.Requests[0].Actions[0].RecipeNetId);
    }
}
