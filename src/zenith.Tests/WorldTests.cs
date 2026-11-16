using Xunit;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Tests;

public class BlockEditIntentTests
{
    [Fact]
    public void IsInWorldBounds_rejects_extreme_coords()
    {
        Assert.False(BlockEditIntent.Set(40_000_000, 64, 0, 1).IsInWorldBounds());
        Assert.False(BlockEditIntent.Set(0, 9999, 0, 1).IsInWorldBounds());
        Assert.True(BlockEditIntent.Set(0, 64, 0, 1).IsInWorldBounds());
    }
}

public class BlockEditQueueTests
{
    [Fact]
    public void Submit_drains_in_fifo_order()
    {
        var player = new Player.Player("queue", null!, runtimeId: 1, Guid.NewGuid());
        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(1, 64, 0, 1)));
        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(2, 64, 0, 1)));
        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(3, 64, 0, 1)));

        Assert.True(player.Intents.TryConsumeBlockEdit(out var a));
        Assert.True(player.Intents.TryConsumeBlockEdit(out var b));
        Assert.True(player.Intents.TryConsumeBlockEdit(out var c));
        Assert.False(player.Intents.TryConsumeBlockEdit(out _));

        Assert.Equal(1, a.X);
        Assert.Equal(2, b.X);
        Assert.Equal(3, c.X);
    }

    [Fact]
    public void Submit_rejects_when_queue_full_preserving_accepted()
    {
        var player = new Player.Player("full", null!, runtimeId: 1, Guid.NewGuid());
        for (var i = 0; i < Player.PlayerIntents.MaxPendingBlockEdits; i++)
            Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(i, 64, 0, 1)));

        Assert.False(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(99, 64, 0, 1)));

        for (var i = 0; i < Player.PlayerIntents.MaxPendingBlockEdits; i++)
        {
            Assert.True(player.Intents.TryConsumeBlockEdit(out var edit));
            Assert.Equal(i, edit.X);
        }

        Assert.False(player.Intents.TryConsumeBlockEdit(out _));
    }
}

public class InMemoryChunkStorageTests
{
    [Fact]
    public async Task Put_then_Get_returns_same_column_bytes()
    {
        var storage = new InMemoryChunkStorage();
        await storage.PutColumnAsync(new ChunkCoord(1, 2), [1, 2, 3]);
        var got = await storage.GetColumnAsync(new ChunkCoord(1, 2));
        Assert.Equal(new byte[] { 1, 2, 3 }, got);
        Assert.Null(await storage.GetColumnAsync(new ChunkCoord(9, 9)));
    }
}

public class WorldColumnModelTests
{
    public WorldColumnModelTests() => Blocks.EnsureLoaded();

    [Fact]
    public async Task GetOrCreateColumn_returns_flat_terrain_with_edits_applied()
    {
        var world = new World.World(new InMemoryChunkStorage());
        world.SetBlock(3, Blocks.FlatSpawnY, 5, Blocks.Stone);

        var column = await world.GetOrCreateColumnAsync(0, 0);
        Assert.True(column.GetWire().SubChunkCount > 0);
        Assert.Equal(Blocks.Stone, world.GetBlock(3, Blocks.FlatSpawnY, 5));
        Assert.Equal(Blocks.GrassBlock, world.GetBlock(0, Blocks.FlatGrassY, 0));
    }

    [Fact]
    public void Edits_in_distinct_chunks_stay_isolated()
    {
        var world = new World.World(new InMemoryChunkStorage());
        world.SetBlock(3, Blocks.FlatSpawnY, 5, Blocks.Stone);
        world.SetBlock(20, Blocks.FlatSpawnY, 5, Blocks.Dirt);

        Assert.Equal(Blocks.Stone, world.GetBlock(3, Blocks.FlatSpawnY, 5));
        Assert.Equal(Blocks.Dirt, world.GetBlock(20, Blocks.FlatSpawnY, 5));
        Assert.Equal(Blocks.Air, world.GetBlock(3 + 16, Blocks.FlatSpawnY, 5));
        Assert.Equal(2, world.LoadedColumnCount);
    }

    [Fact]
    public void Overwrite_and_restore_round_trip()
    {
        var world = new World.World(new InMemoryChunkStorage());
        world.SetBlock(3, Blocks.FlatSpawnY, 5, Blocks.Stone);
        world.SetBlock(3, Blocks.FlatSpawnY, 5, Blocks.Dirt);
        Assert.Equal(Blocks.Dirt, world.GetBlock(3, Blocks.FlatSpawnY, 5));
        world.SetBlock(3, Blocks.FlatSpawnY, 5, Blocks.Air);
        Assert.Equal(Blocks.Air, world.GetBlock(3, Blocks.FlatSpawnY, 5));
    }

    [Fact]
    public void Breaking_base_terrain_sticks()
    {
        var world = new World.World(new InMemoryChunkStorage());
        world.SetBlock(0, Blocks.FlatGrassY, 0, Blocks.Air);
        Assert.Equal(Blocks.Air, world.GetBlock(0, Blocks.FlatGrassY, 0));
    }

    [Fact]
    public void There_is_no_cap_on_edited_cells()
    {
        var world = new World.World(new InMemoryChunkStorage());
        for (var i = 0; i < 12_000; i++)
            Assert.True(world.TrySetBlock(i % 16, -60 + (i / 256) % 16, (i / 16) % 16, i % 2 == 0 ? Blocks.Stone : Blocks.Dirt));
    }

    [Fact]
    public void Writes_outside_the_height_range_are_refused()
    {
        var world = new World.World(new InMemoryChunkStorage());
        Assert.False(world.TrySetBlock(0, 400, 0, Blocks.Stone));
        Assert.False(world.TrySetBlock(0, -100, 0, Blocks.Stone));
    }

    [Fact]
    public async Task LevelDb_persists_edits_across_World_instances()
    {
        var dir = Path.Combine(Path.GetTempPath(), "zenith-col-" + Guid.NewGuid().ToString("N"));
        try
        {
            {
                var storage = new LevelDbChunkStorage(dir);
                var world = new World.World(storage);
                world.SetBlock(1, -60, 2, Blocks.Stone);
                world.SetBlock(20, -60, 2, Blocks.Dirt);
                await world.FlushPersistenceAsync();
                storage.Dispose();
            }

            {
                using var storage = new LevelDbChunkStorage(dir);
                var world = new World.World(storage);
                await world.GetOrCreateColumnAsync(0, 0);
                await world.GetOrCreateColumnAsync(1, 0);
                Assert.Equal(Blocks.Stone, world.GetBlock(1, -60, 2));
                Assert.Equal(Blocks.Dirt, world.GetBlock(20, -60, 2));
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }
}

public class PlayerInventoryTests
{
    [Fact]
    public void Rejects_invalid_slot_and_count()
    {
        Assert.False(PlayerInventory.IsValidHotbarSlot(-1));
        Assert.False(PlayerInventory.IsValidHotbarSlot(9));
        Assert.True(PlayerInventory.IsValidHotbarSlot(0));
        Assert.True(PlayerInventory.IsValidInventorySlot(9));
        Assert.True(PlayerInventory.IsValidInventorySlot(35));
        Assert.False(PlayerInventory.IsValidInventorySlot(36));
        Assert.False(PlayerInventory.IsValidStackCount(-1));
        Assert.False(PlayerInventory.IsValidStackCount(65));
        Assert.True(PlayerInventory.IsValidStackCount(64));
    }

    [Fact]
    public void TrySet_accepts_storage_slots()
    {
        var inv = new PlayerInventory();
        Assert.True(inv.TrySetBlock(9, Blocks.Stone, 3));
        Assert.Equal(Blocks.Stone, inv.Get(9).Id.Value);
        Assert.Equal(3, inv.Get(9).Count);
    }

    [Fact]
    public void TryConsumeOne_rejects_storage_slot()
    {
        var inv = new PlayerInventory();
        Assert.True(inv.TrySetBlock(9, Blocks.Stone, 5));
        Assert.False(inv.TryConsumeOne(9));
        Assert.Equal(5, inv.Get(9).Count);
    }

    [Fact]
    public void TryAdd_uses_storage_when_hotbar_full()
    {
        var inv = new PlayerInventory();
        for (var i = 0; i < PlayerInventory.HotbarSize; i++)
            Assert.True(inv.TrySetBlock(i, Blocks.GrassBlock, PlayerInventory.MaxStack));

        Assert.True(inv.TryAddBlock(Blocks.Stone, 1));
        Assert.Equal(Blocks.Stone, inv.Get(9).Id.Value);
        Assert.Equal(1, inv.Get(9).Count);
    }

    [Fact]
    public void SnapshotMainInventory_reflects_all_36_slots()
    {
        var inv = new PlayerInventory();
        Assert.True(inv.TrySetBlock(9, Blocks.Stone, 2));
        Assert.True(inv.TrySetBlock(35, Blocks.GrassBlock, 4));
        var snap = inv.SnapshotMainInventory();
        Assert.Equal(PlayerInventory.FullInventorySize, snap.Length);
        Assert.Equal(Blocks.Stone, snap[9].Id.Value);
        Assert.Equal(2, snap[9].Count);
        Assert.Equal(Blocks.GrassBlock, snap[35].Id.Value);
        Assert.Equal(4, snap[35].Count);
    }

    [Fact]
    public void TrySet_rejects_out_of_range_before_mutating()
    {
        var inv = new PlayerInventory();
        Assert.False(inv.TrySetBlock(-2, Blocks.Stone, 1));
        Assert.False(inv.TrySetBlock(36, Blocks.Stone, 1));
        Assert.False(inv.TrySetBlock(0, Blocks.Stone, -3));
        Assert.False(inv.TrySetBlock(0, Blocks.Stone, 99));
        Assert.Equal(64, inv.Get(0).Count);
    }

    [Fact]
    public void TryTransfer_and_TrySwap_move_between_hotbar_and_storage()
    {
        var inv = new PlayerInventory();
        Assert.True(inv.TrySetBlock(0, Blocks.Stone, 10));
        Assert.True(inv.TryTransfer(0, 9, 4));
        Assert.Equal(6, inv.Get(0).Count);
        Assert.Equal(4, inv.Get(9).Count);
        Assert.Equal(Blocks.Stone, inv.Get(9).Id.Value);

        Assert.True(inv.TrySwap(0, 9));
        Assert.Equal(4, inv.Get(0).Count);
        Assert.Equal(6, inv.Get(9).Count);
    }

    [Fact]
    public void TryTransfer_rejects_different_runtime_on_dest()
    {
        var inv = new PlayerInventory();
        Assert.True(inv.TrySetBlock(0, Blocks.Stone, 5));
        Assert.True(inv.TrySetBlock(9, Blocks.GrassBlock, 3));
        Assert.False(inv.TryTransfer(0, 9, 1));
        Assert.Equal(5, inv.Get(0).Count);
        Assert.Equal(3, inv.Get(9).Count);
    }

    [Fact]
    public void Cursor_round_trip_via_transfer()
    {
        var inv = new PlayerInventory();
        Assert.True(inv.TrySetBlock(0, Blocks.Stone, 8));
        Assert.True(inv.TryTransfer(0, PlayerInventory.CursorSlot, 3));
        Assert.Equal(5, inv.Get(0).Count);
        Assert.Equal(3, inv.Cursor.Count);
        Assert.True(inv.TryTransfer(PlayerInventory.CursorSlot, 9, 3));
        Assert.True(inv.Cursor.IsEmpty);
        Assert.Equal(3, inv.Get(9).Count);
    }

    [Fact]
    public void Snapshot_restore_round_trip()
    {
        var inv = new PlayerInventory();
        Assert.True(inv.TrySetBlock(0, Blocks.Stone, 2));
        Assert.True(inv.TrySetBlock(PlayerInventory.CursorSlot, Blocks.GrassBlock, 1));
        var snap = inv.CaptureSnapshot();
        Assert.True(inv.TrySetBlock(0, Blocks.Air, 0));
        Assert.True(inv.TrySetBlock(PlayerInventory.CursorSlot, Blocks.Air, 0));
        inv.RestoreSnapshot(snap);
        Assert.Equal(2, inv.Get(0).Count);
        Assert.Equal(1, inv.Cursor.Count);
    }

    [Fact]
    public void TryConsumeOne_decrements_stack()
    {
        var inv = new PlayerInventory();
        Assert.True(inv.TryConsumeOne(0));
        Assert.Equal(63, inv.Get(0).Count);
    }

    [Fact]
    public void TryAdd_stacks_onto_same_runtime_then_empty_slot()
    {
        var inv = new PlayerInventory();
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(inv.TrySetBlock(i, Blocks.Air, 0));

        Assert.True(inv.TrySetBlock(0, Blocks.Stone, 60));
        Assert.True(inv.TryAddBlock(Blocks.Stone, 5));
        Assert.Equal(64, inv.Get(0).Count);
        Assert.Equal(Blocks.Stone, inv.Get(1).Id.Value);
        Assert.Equal(1, inv.Get(1).Count);

        Assert.True(inv.TryAddBlock(Blocks.GrassBlock, 3));
        Assert.Equal(Blocks.GrassBlock, inv.Get(2).Id.Value);
        Assert.Equal(3, inv.Get(2).Count);
    }

    [Fact]
    public void TryAdd_failure_does_not_sticky_fill_partial_stack()
    {
        var inv = new PlayerInventory();
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(inv.TrySetBlock(i, Blocks.Dirt, 64));
        Assert.True(inv.TrySetBlock(0, Blocks.Dirt, 63));

        Assert.False(inv.TryAddBlock(Blocks.Dirt, 5));
        Assert.Equal(63, inv.Get(0).Count);
        Assert.Equal(64, inv.Get(1).Count);
    }

    [Fact]
    public void TryAddUpTo_fills_partial_stack_space_when_bag_otherwise_full()
    {
        var inv = new PlayerInventory();
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(inv.TrySetBlock(i, Blocks.Dirt, 64));
        Assert.True(inv.TrySetBlock(0, Blocks.Dirt, 63));

        Assert.Equal(1, inv.TryAddUpToBlock(Blocks.Dirt, 5));
        Assert.Equal(64, inv.Get(0).Count);
    }

    [Fact]
    public void TryAddUpTo_merges_chest_facings_into_existing_south_stack()
    {
        Blocks.EnsureLoaded();
        var inv = new PlayerInventory();
        Assert.True(inv.TrySetBlock(0, Blocks.Chest, 32));
        var north = Blocks.ChestForFacing(Blocks.CardinalNorth);
        Assert.NotEqual(Blocks.Chest, north);

        Assert.Equal(5, inv.TryAddUpToBlock(north, 5));
        Assert.Equal(37, inv.Get(0).Count);
        Assert.Equal(Blocks.Chest, inv.Get(0).Id.Value);
    }

    [Fact]
    public void Break_into_inventory_matches_BlockSystem_economy()
    {
        Blocks.EnsureLoaded();
        var world = new World.World(new InMemoryChunkStorage());
        var inv = new PlayerInventory();
        Assert.True(inv.TrySetBlock(0, Blocks.Stone, 1));

        // Place: consume one
        Assert.True(inv.TryConsumeOne(0));
        world.SetBlock(0, Blocks.FlatGrassY, 0, Blocks.Stone);
        Assert.True(inv.Get(0).IsEmpty);

        // Break: give previous block back
        var previous = world.GetBlock(0, Blocks.FlatGrassY, 0);
        Assert.Equal(Blocks.Stone, previous);
        Assert.True(inv.TryAddBlock(previous));
        world.SetBlock(0, Blocks.FlatGrassY, 0, Blocks.Air);
        Assert.Equal(1, inv.Get(0).Count);
        Assert.Equal(Blocks.Stone, inv.Get(0).Id.Value);
    }
}

public class ChunkPayloadsTests
{
    [Fact]
    public void Flat_wire_starts_with_subchunk_header()
    {
        Blocks.EnsureLoaded();
        var terrain = ChunkPayloads.BuildFlatOverworld();
        var terrainBiome = terrain.BiomeNetworkId;
        var column = new WorldColumn(new ChunkCoord(0, 0), 0, terrainBiome);
        for (var i = 0; i < terrain.Sections.Length; i++)
            if (terrain.Sections[i] is { } s) column.SetSection(i, s);
        var wire = column.GetWire();
        Assert.True(wire.SubChunkCount >= 1);
        Assert.Equal(SubChunkWire.Version, wire.Payload[0]);
        Assert.Equal(0, wire.Payload[^1]); // border
    }
}
