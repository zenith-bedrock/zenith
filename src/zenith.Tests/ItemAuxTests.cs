using Xunit;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>
/// ADR §147: an item stack's identity includes its aux (variant) value, so stacks that differ only in aux
/// are different stacks everywhere they are compared, stored or sent.
/// </summary>
public class ItemAuxTests
{
    public ItemAuxTests() => Blocks.EnsureLoaded();

    [Fact]
    public void Stacks_that_differ_only_in_aux_are_different_identities()
    {
        Assert.NotEqual(StackId.FromItem(300, aux: 1), StackId.FromItem(300, aux: 2));
        Assert.Equal(StackId.FromItem(300), StackId.FromItem(300, aux: 0));
    }

    [Fact]
    public void A_block_stack_never_carries_aux()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StackId(StackKind.Block, Blocks.Stone, 1));
    }

    [Fact]
    public void An_out_of_range_aux_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StackId.FromItem(300, aux: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => StackId.FromItem(300, aux: StackId.MaxAux + 1));
    }

    [Fact]
    public void SlotBlob_round_trips_aux()
    {
        var slots = new InventorySlot[PlayerInventory.FullInventorySize];
        slots[3] = new InventorySlot(StackId.FromItem(300, aux: 14), 2);
        slots[4] = new InventorySlot(StackId.FromItem(300), 1);

        var blob = SlotBlob.Pack(slots);
        Assert.Equal(SlotBlob.Version4, blob[0]);
        var dest = new InventorySlot[PlayerInventory.FullInventorySize];
        Assert.True(SlotBlob.TryUnpack(blob, dest));

        Assert.Equal(14, dest[3].Id.Aux);
        Assert.Equal(0, dest[4].Id.Aux);
        Assert.NotEqual(dest[3].Id, dest[4].Id);
    }

    [Fact]
    public void SlotBlob_v2_still_loads_with_aux_zero()
    {
        var blob = new byte[1 + PlayerInventory.FullInventorySize * 9];
        blob[0] = SlotBlob.Version2;
        blob[1] = (byte)StackKind.Item;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(2, 4), 300);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(6, 4), 5);
        for (var i = 1; i < PlayerInventory.FullInventorySize; i++)
            blob[1 + i * 9] = (byte)StackKind.Block; // air, count 0

        var dest = new InventorySlot[PlayerInventory.FullInventorySize];
        Assert.True(SlotBlob.TryUnpack(blob, dest));
        Assert.Equal(StackId.FromItem(300), dest[0].Id);
        Assert.Equal(5, dest[0].Count);
    }

    [Fact]
    public void SlotBlob_rejects_an_out_of_range_aux_instead_of_loading_it()
    {
        var slots = new InventorySlot[PlayerInventory.FullInventorySize];
        slots[0] = new InventorySlot(StackId.FromItem(300, aux: 7), 1);
        var blob = SlotBlob.Pack(slots);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(1 + 1 + 4, 4), -5); // aux field of slot 0

        Assert.False(SlotBlob.TryUnpack(blob, new InventorySlot[PlayerInventory.FullInventorySize]));
    }
}
