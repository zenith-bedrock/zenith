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
        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(1, 64, 0, 1)));
        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(2, 64, 0, 1)));
        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(3, 64, 0, 1)));

        Assert.True(player.TryConsumeBlockEdit(out var a));
        Assert.True(player.TryConsumeBlockEdit(out var b));
        Assert.True(player.TryConsumeBlockEdit(out var c));
        Assert.False(player.TryConsumeBlockEdit(out _));

        Assert.Equal(1, a.X);
        Assert.Equal(2, b.X);
        Assert.Equal(3, c.X);
    }

    [Fact]
    public void Submit_rejects_when_queue_full_preserving_accepted()
    {
        var player = new Player.Player("full", null!, runtimeId: 1, Guid.NewGuid());
        for (var i = 0; i < Player.Player.MaxPendingBlockEdits; i++)
            Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(i, 64, 0, 1)));

        Assert.False(player.SubmitBlockEdit(BlockEditIntent.Set(99, 64, 0, 1)));

        for (var i = 0; i < Player.Player.MaxPendingBlockEdits; i++)
        {
            Assert.True(player.TryConsumeBlockEdit(out var edit));
            Assert.Equal(i, edit.X);
        }

        Assert.False(player.TryConsumeBlockEdit(out _));
    }
}

public class InMemoryChunkStorageTests
{
    [Fact]
    public async Task Put_then_Get_returns_same_column()
    {
        var storage = new InMemoryChunkStorage();
        var column = new ChunkColumnData(new ChunkCoord(1, 2), 0, 0, [1, 2, 3]);
        await storage.PutAsync(column);
        var got = await storage.GetAsync(new ChunkCoord(1, 2));
        Assert.NotNull(got);
        Assert.Equal(column.Coord, got!.Coord);
        Assert.Equal(column.ExtraPayload, got.ExtraPayload);
    }
}

public class WorldOverlayTests
{
    [Fact]
    public async Task GetOrCreateColumn_returns_flat_base_then_overlays_on_read()
    {
        var world = new World.World(new InMemoryChunkStorage());
        world.SetBlock(3, Blocks.FlatSpawnY, 5, Blocks.Stone);

        var column = await world.GetOrCreateColumnAsync(0, 0);
        Assert.True(column.Base.SubChunkCount > 0);
        Assert.Single(column.Overlays);
        Assert.Equal(3, column.Overlays[0].X);
        Assert.Equal(Blocks.Stone, column.Overlays[0].BlockRuntimeId);
        Assert.Equal(Blocks.Stone, world.GetBlock(3, Blocks.FlatSpawnY, 5));
        Assert.Equal(Blocks.GrassBlock, world.GetBlock(0, Blocks.FlatGrassY, 0));
    }

    [Fact]
    public void GetOverlaysInColumn_isolates_distinct_chunks()
    {
        var world = new World.World(new InMemoryChunkStorage());
        world.SetBlock(3, Blocks.FlatSpawnY, 5, Blocks.Stone);   // chunk (0,0)
        world.SetBlock(20, Blocks.FlatSpawnY, 5, Blocks.Dirt);   // chunk (1,0)

        var c00 = world.GetOverlaysInColumn(0, 0);
        var c10 = world.GetOverlaysInColumn(1, 0);
        var empty = world.GetOverlaysInColumn(2, 2);

        Assert.Single(c00);
        Assert.Equal(3, c00[0].X);
        Assert.Equal(Blocks.Stone, c00[0].BlockRuntimeId);

        Assert.Single(c10);
        Assert.Equal(20, c10[0].X);
        Assert.Equal(Blocks.Dirt, c10[0].BlockRuntimeId);

        Assert.Empty(empty);
        Assert.Equal(2, world.OverrideCount);
    }

    [Fact]
    public void SetBlock_overwrite_keeps_single_index_entry()
    {
        var world = new World.World(new InMemoryChunkStorage());
        world.SetBlock(3, Blocks.FlatSpawnY, 5, Blocks.Stone);
        world.SetBlock(3, Blocks.FlatSpawnY, 5, Blocks.Dirt);

        var column = world.GetOverlaysInColumn(0, 0);
        Assert.Single(column);
        Assert.Equal(Blocks.Dirt, column[0].BlockRuntimeId);
        Assert.Equal(Blocks.Dirt, world.GetBlock(3, Blocks.FlatSpawnY, 5));
        Assert.Equal(1, world.OverrideCount);
    }

    [Fact]
    public void SetBlock_restore_base_compacts_overlay()
    {
        // FlatSpawnY base is air — stone→air matches base → remove key (§36 SoftCap).
        var world = new World.World(new InMemoryChunkStorage());
        world.SetBlock(3, Blocks.FlatSpawnY, 5, Blocks.Stone);
        Assert.Equal(1, world.OverrideCount);
        world.SetBlock(3, Blocks.FlatSpawnY, 5, Blocks.Air);

        Assert.Empty(world.GetOverlaysInColumn(0, 0));
        Assert.Equal(0, world.OverrideCount);
        Assert.Equal(Blocks.Air, world.GetBlock(3, Blocks.FlatSpawnY, 5));
    }

    [Fact]
    public void SetBlock_break_base_terrain_keeps_air_overlay()
    {
        var world = new World.World(new InMemoryChunkStorage());
        world.SetBlock(0, Blocks.FlatGrassY, 0, Blocks.Air);

        var column = world.GetOverlaysInColumn(0, 0);
        Assert.Single(column);
        Assert.Equal(Blocks.Air, column[0].BlockRuntimeId);
        Assert.Equal(Blocks.Air, world.GetBlock(0, Blocks.FlatGrassY, 0));
    }

    [Fact]
    public void TrySetBlock_softcap_refuses_new_key_allows_overwrite()
    {
        var world = new World.World(new InMemoryChunkStorage());
        for (var i = 0; i < World.World.OverrideSoftCap; i++)
            Assert.True(world.TrySetBlock(i, Blocks.FlatSpawnY, 0, Blocks.Stone));

        Assert.Equal(World.World.OverrideSoftCap, world.OverrideCount);
        Assert.False(world.TrySetBlock(World.World.OverrideSoftCap, Blocks.FlatSpawnY, 0, Blocks.Dirt));
        Assert.Equal(World.World.OverrideSoftCap, world.OverrideCount);
        Assert.True(world.TrySetBlock(0, Blocks.FlatSpawnY, 0, Blocks.Dirt)); // overwrite
        Assert.True(world.TrySetBlock(0, Blocks.FlatSpawnY, 0, Blocks.Air)); // compact frees a slot
        Assert.Equal(World.World.OverrideSoftCap - 1, world.OverrideCount);
        Assert.True(world.TrySetBlock(World.World.OverrideSoftCap, Blocks.FlatSpawnY, 0, Blocks.Sand));
    }

    [Fact]
    public async Task LevelDb_persists_overlays_across_World_instances()
    {
        var dir = Path.Combine(Path.GetTempPath(), "zenith-ov-" + Guid.NewGuid().ToString("N"));
        try
        {
            {
                var storage = new LevelDbChunkStorage(dir);
                var world = new World.World(storage);
                world.SetBlock(1, -60, 2, Blocks.Stone);
                world.SetBlock(20, -60, 2, Blocks.Dirt); // chunk (1,0) — hydrate must rebuild index
                await world.GetOrCreateColumnAsync(0, 0);
                storage.Dispose();
            }

            {
                using var storage = new LevelDbChunkStorage(dir);
                var world = new World.World(storage);
                Assert.Equal(Blocks.Stone, world.GetBlock(1, -60, 2));
                Assert.Equal(Blocks.Dirt, world.GetBlock(20, -60, 2));
                var column = await world.GetOrCreateColumnAsync(0, 0);
                Assert.Contains(column.Overlays, o => o.X == 1 && o.Z == 2 && o.BlockRuntimeId == Blocks.Stone);
                Assert.DoesNotContain(column.Overlays, o => o.X == 20);
                Assert.Single(world.GetOverlaysInColumn(1, 0));
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }
}

public class ColumnTerrainEmitterTests
{
    [Fact]
    public void Emit_sends_LevelChunk_before_UpdateBlocks_for_that_column()
    {
        var bas = new ChunkColumnData(new ChunkCoord(2, 3), 0, 1, [9]);
        var overlays = new[]
        {
            new BlockOverride(32, 64, 48, Blocks.Stone),
            new BlockOverride(33, 64, 48, Blocks.GrassBlock)
        };
        var result = new ColumnReadResult(bas, overlays);

        var sequence = new List<string>();
        ColumnTerrainEmitter.Emit(
            result,
            sendLevelChunk: c =>
            {
                sequence.Add($"chunk:{c.Coord.X},{c.Coord.Z}");
            },
            sendUpdateBlock: (x, y, z, id) =>
            {
                sequence.Add($"block:{x},{y},{z}");
            });

        Assert.Equal(
            new[] { "chunk:2,3", "block:32,64,48", "block:33,64,48" },
            sequence);
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
    public void Flat_payload_starts_with_subchunk_header()
    {
        var (count, payload) = ChunkPayloads.BuildFlatOverworld();
        Assert.Equal(1, count);
        Assert.True(payload.Length > 2);
        Assert.Equal(8, payload[0]);
        Assert.Equal(1, payload[1]);
        Assert.Equal(0, payload[^1]); // border
    }
}
