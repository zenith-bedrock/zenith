using Zenith.Player;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public class SlotBlobPersistTests
{
    public SlotBlobPersistTests() => Blocks.EnsureLoaded();

    [Fact]
    public void SlotBlob_round_trips_36_slots()
    {
        var slots = new InventorySlot[PlayerInventory.FullInventorySize];
        slots[0] = InventorySlot.OfBlock(Blocks.Stone, 10);
        slots[5] = InventorySlot.OfBlock(Blocks.Chest, 2);
        var blob = SlotBlob.Pack(slots);
        Assert.Equal(SlotBlob.Version2, blob[0]);
        var dest = new InventorySlot[PlayerInventory.FullInventorySize];
        Assert.True(SlotBlob.TryUnpack(blob, dest));
        Assert.Equal(Blocks.Stone, dest[0].Id.Value);
        Assert.Equal(StackKind.Block, dest[0].Id.Kind);
        Assert.Equal(10, dest[0].Count);
        Assert.Equal(Blocks.Chest, dest[5].Id.Value);
        Assert.True(dest[1].IsEmpty);
    }

    [Fact]
    public void SlotBlob_v1_migrate_tool_network_id_to_Item_kind()
    {
        Tools.EnsureLoaded();
        var pick = Tools.Require("minecraft:iron_pickaxe");
        // v1: version + (i32 value, i32 count) × 36
        var blob = new byte[1 + PlayerInventory.FullInventorySize * 8];
        blob[0] = SlotBlob.Version1;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(1, 4), pick);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(5, 4), 1);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(1 + 8, 4), Blocks.Dirt);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(1 + 8 + 4, 4), 3);

        var dest = new InventorySlot[PlayerInventory.FullInventorySize];
        Assert.True(SlotBlob.TryUnpack(blob, dest));
        Assert.Equal(StackKind.Item, dest[0].Id.Kind);
        Assert.Equal(pick, dest[0].Id.Value);
        Assert.Equal(1, dest[0].Count);
        Assert.Equal(StackKind.Block, dest[1].Id.Kind);
        Assert.Equal(Blocks.Dirt, dest[1].Id.Value);
        Assert.Equal(3, dest[1].Count);
    }

    [Fact]
    public async Task InMemory_chest_and_inventory_persist()
    {
        var storage = new InMemoryChunkStorage();
        var slots = new InventorySlot[36];
        slots[0] = InventorySlot.OfBlock(Blocks.Dirt, 3);
        var invBlob = SlotBlob.Pack(slots);

        var uuid = Guid.Parse("11111111-1111-1111-1111-111111111111");
        await storage.PutInventoryAsync(uuid, invBlob);
        var got = await storage.GetInventoryAsync(uuid);
        Assert.NotNull(got);

        var chestSlots = new InventorySlot[ChestStore.Size];
        chestSlots[0] = InventorySlot.OfBlock(Blocks.Stone, 1);
        var chestBlob = SlotBlob.Pack(chestSlots);
        await storage.PutChestAsync(1, 2, 3, chestBlob);

        var seen = false;
        await storage.ForEachChestAsync((x, y, z, blob) =>
        {
            Assert.Equal(1, x);
            Assert.Equal(2, y);
            Assert.Equal(3, z);
            seen = true;
        });
        Assert.True(seen);
    }

    [Fact]
    public void World_load_inventory_replaces_seed()
    {
        var storage = new InMemoryChunkStorage();
        var world = new World.World(storage);
        var uuid = Guid.NewGuid();
        var packed = new PlayerInventory(seedStarterHotbar: false);
        Assert.True(packed.TrySetBlock(0, Blocks.Sand, 7));
        world.PersistInventory(uuid, packed);

        var loaded = new PlayerInventory(seedStarterHotbar: true);
        Assert.True(world.TryLoadInventory(uuid, loaded));
        Assert.Equal(Blocks.Sand, loaded.Get(0).Id.Value);
        Assert.Equal(7, loaded.Get(0).Count);
        Assert.True(loaded.Get(1).IsEmpty);
    }

    [Fact]
    public async Task InMemory_FlushAsync_is_noop_and_Get_sees_Put()
    {
        var storage = new InMemoryChunkStorage();
        var uuid = Guid.NewGuid();
        var slots = new InventorySlot[36];
        slots[0] = InventorySlot.OfBlock(Blocks.Dirt, 4);
        await storage.PutInventoryAsync(uuid, SlotBlob.Pack(slots));
        await storage.FlushAsync();
        var got = await storage.GetInventoryAsync(uuid);
        Assert.NotNull(got);
        var dest = new InventorySlot[36];
        Assert.True(SlotBlob.TryUnpack(got!, dest));
        Assert.Equal(Blocks.Dirt, dest[0].Id.Value);
        Assert.Equal(4, dest[0].Count);
    }

    [Fact]
    public async Task LevelDb_FlushAsync_awaits_inventory_Put()
    {
        var dir = Path.Combine(Path.GetTempPath(), "zenith-flush-" + Guid.NewGuid().ToString("N"));
        try
        {
            var uuid = Guid.NewGuid();
            var slots = new InventorySlot[36];
            slots[2] = InventorySlot.OfBlock(Blocks.Chest, 1);
            var blob = SlotBlob.Pack(slots);

            {
                var storage = new LevelDbChunkStorage(dir);
                await storage.PutInventoryAsync(uuid, blob);
                await storage.FlushAsync();
                storage.Dispose();
            }

            {
                using var storage = new LevelDbChunkStorage(dir);
                var got = await storage.GetInventoryAsync(uuid);
                Assert.NotNull(got);
                var dest = new InventorySlot[36];
                Assert.True(SlotBlob.TryUnpack(got!, dest));
                Assert.Equal(Blocks.Chest, dest[2].Id.Value);
                Assert.Equal(1, dest[2].Count);
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }
}
