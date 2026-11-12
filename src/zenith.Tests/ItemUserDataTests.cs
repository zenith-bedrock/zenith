using System.Buffers.Binary;
using Xunit;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>ADR §148: wear is user data on the stack, part of its identity, and never aux.</summary>
public class ItemUserDataTests
{
    public ItemUserDataTests() => Blocks.EnsureLoaded();

    [Fact]
    public void Stacks_that_differ_only_in_damage_are_different_stacks()
    {
        var fresh = StackId.FromItem(299);
        var worn = StackId.FromItem(299, damage: 100);

        Assert.NotEqual(fresh, worn);
        Assert.Equal(worn, fresh.WithDamage(100));
        Assert.Equal(100, worn.Damage);
        Assert.Equal(0, worn.Aux);
    }

    [Fact]
    public void Damage_and_aux_are_independent_axes()
    {
        var id = StackId.FromItem(421, aux: 5, damage: 7);

        Assert.Equal(5, id.Aux);
        Assert.Equal(7, id.Damage);
        Assert.NotEqual(id, StackId.FromItem(421, aux: 7, damage: 5));
    }

    [Fact]
    public void Damage_is_bounded_and_a_block_cannot_carry_user_data()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StackId.FromItem(299, damage: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => StackId.FromItem(299, damage: ItemUserData.MaxDamage + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StackId(StackKind.Block, Blocks.Dirt, 0, new ItemUserData(1)));
        Assert.True(StackId.FromBlock(Blocks.Dirt).Data.IsDefault);
    }

    [Fact]
    public void SlotBlob_round_trips_damage_next_to_aux()
    {
        var slots = new InventorySlot[PlayerInventory.FullInventorySize];
        slots[2] = new InventorySlot(StackId.FromItem(299, damage: 137), 1);
        slots[3] = new InventorySlot(StackId.FromItem(421, aux: 5, damage: 0), 1);
        var blob = SlotBlob.Pack(slots);

        var dest = new InventorySlot[PlayerInventory.FullInventorySize];
        Assert.True(SlotBlob.TryUnpack(blob, dest));
        Assert.Equal(slots[2], dest[2]);
        Assert.Equal(137, dest[2].Id.Damage);
        Assert.Equal(0, dest[2].Id.Aux);
        Assert.Equal(slots[3], dest[3]);
    }

    [Fact]
    public void A_v3_blob_loads_with_no_damage()
    {
        const int slotBytesV3 = 13;
        var blob = new byte[1 + 2 * slotBytesV3];
        blob[0] = SlotBlob.Version3;
        blob[1] = (byte)StackKind.Item;
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(2, 4), 299);
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(10, 4), 1); // count
        blob[1 + slotBytesV3] = (byte)StackKind.Block;
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(1 + slotBytesV3 + 1, 4), Blocks.Air);

        var dest = new InventorySlot[2];
        Assert.True(SlotBlob.TryUnpack(blob, dest));
        Assert.Equal(StackId.FromItem(299), dest[0].Id);
    }

    [Fact]
    public void A_blob_with_impossible_damage_fails_instead_of_loading()
    {
        var slots = new InventorySlot[1];
        slots[0] = new InventorySlot(StackId.FromItem(299, damage: 5), 1);
        const int damageField = 1 + 1 + 4 + 4; // version, kind, value, aux

        var negative = SlotBlob.Pack(slots);
        BinaryPrimitives.WriteInt32LittleEndian(negative.AsSpan(damageField, 4), -1);
        Assert.False(SlotBlob.TryUnpack(negative, new InventorySlot[1]));

        var tooBig = SlotBlob.Pack(slots);
        BinaryPrimitives.WriteInt32LittleEndian(tooBig.AsSpan(damageField, 4), ItemUserData.MaxDamage + 1);
        Assert.False(SlotBlob.TryUnpack(tooBig, new InventorySlot[1]));

        var onBlock = SlotBlob.Pack([InventorySlot.OfBlock(Blocks.Dirt, 1)]);
        BinaryPrimitives.WriteInt32LittleEndian(onBlock.AsSpan(damageField, 4), 3);
        Assert.False(SlotBlob.TryUnpack(onBlock, new InventorySlot[1]));
    }
}

/// <summary>The protocol layer decides what reaches the wire: only durable items carry the Damage tag (ADR §148).</summary>
public class ItemUserDataWireTests
{
    [Fact]
    public void Durable_items_send_their_damage_even_at_zero_and_others_send_none()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("wire");
        var palette = fx.Context.ItemPalette;
        var inv = player.Session.Protocol.Inventory;

        var worn = inv.DescribeForWire(0, new InventorySlot(StackId.FromItem(palette.Require("minecraft:iron_pickaxe"), damage: 100), 1));
        var fresh = inv.DescribeForWire(0, new InventorySlot(StackId.FromItem(palette.Require("minecraft:iron_pickaxe")), 1));
        var stick = inv.DescribeForWire(0, new InventorySlot(StackId.FromItem(palette.Require("minecraft:stick")), 3));
        var bed = inv.DescribeForWire(0, new InventorySlot(StackId.FromItem(palette.Require("minecraft:bed"), aux: 5), 1));
        var shield = inv.DescribeForWire(0, new InventorySlot(StackId.FromItem(palette.Require("minecraft:shield")), 1));

        Assert.Equal(100, worn.Damage);
        Assert.Equal(0, worn.Meta);
        Assert.Equal(0, fresh.Damage);
        Assert.Null(stick.Damage);
        Assert.Null(bed.Damage);
        Assert.Equal(5, bed.Meta);
        Assert.True(shield.IsShield);
        Assert.Equal(0, shield.Damage);
    }
}
