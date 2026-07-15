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
        slots[0] = new InventorySlot(Blocks.Stone, 10);
        slots[5] = new InventorySlot(Blocks.Chest, 2);
        var blob = SlotBlob.Pack(slots);
        var dest = new InventorySlot[PlayerInventory.FullInventorySize];
        Assert.True(SlotBlob.TryUnpack(blob, dest));
        Assert.Equal(Blocks.Stone, dest[0].RuntimeId);
        Assert.Equal(10, dest[0].Count);
        Assert.Equal(Blocks.Chest, dest[5].RuntimeId);
        Assert.True(dest[1].IsEmpty);
    }

    [Fact]
    public async Task InMemory_chest_and_inventory_persist()
    {
        var storage = new InMemoryChunkStorage();
        var slots = new InventorySlot[36];
        slots[0] = new InventorySlot(Blocks.Dirt, 3);
        var invBlob = SlotBlob.Pack(slots);

        var uuid = Guid.Parse("11111111-1111-1111-1111-111111111111");
        await storage.PutInventoryAsync(uuid, invBlob);
        var got = await storage.GetInventoryAsync(uuid);
        Assert.NotNull(got);

        var chestSlots = new InventorySlot[ChestStore.Size];
        chestSlots[0] = new InventorySlot(Blocks.Stone, 1);
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
        Assert.True(packed.TrySet(0, Blocks.Sand, 7));
        world.PersistInventory(uuid, packed);

        var loaded = new PlayerInventory(seedStarterHotbar: true);
        Assert.True(world.TryLoadInventory(uuid, loaded));
        Assert.Equal(Blocks.Sand, loaded.Get(0).RuntimeId);
        Assert.Equal(7, loaded.Get(0).Count);
        Assert.True(loaded.Get(1).IsEmpty);
    }

    [Fact]
    public async Task InMemory_FlushAsync_is_noop_and_Get_sees_Put()
    {
        var storage = new InMemoryChunkStorage();
        var uuid = Guid.NewGuid();
        var slots = new InventorySlot[36];
        slots[0] = new InventorySlot(Blocks.Dirt, 4);
        await storage.PutInventoryAsync(uuid, SlotBlob.Pack(slots));
        await storage.FlushAsync();
        var got = await storage.GetInventoryAsync(uuid);
        Assert.NotNull(got);
        var dest = new InventorySlot[36];
        Assert.True(SlotBlob.TryUnpack(got!, dest));
        Assert.Equal(Blocks.Dirt, dest[0].RuntimeId);
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
            slots[2] = new InventorySlot(Blocks.Chest, 1);
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
                Assert.Equal(Blocks.Chest, dest[2].RuntimeId);
                Assert.Equal(1, dest[2].Count);
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }
}
