using Xunit;
using Zenith.Event;
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
    public async Task LevelDb_persists_overlays_across_World_instances()
    {
        var dir = Path.Combine(Path.GetTempPath(), "zenith-ov-" + Guid.NewGuid().ToString("N"));
        try
        {
            {
                var storage = new LevelDbChunkStorage(dir);
                var world = new World.World(storage);
                world.SetBlock(1, -60, 2, Blocks.Stone);
                await world.GetOrCreateColumnAsync(0, 0);
                storage.Dispose();
            }

            {
                using var storage = new LevelDbChunkStorage(dir);
                var world = new World.World(storage);
                Assert.Equal(Blocks.Stone, world.GetBlock(1, -60, 2));
                var column = await world.GetOrCreateColumnAsync(0, 0);
                Assert.Contains(column.Overlays, o => o.X == 1 && o.Z == 2 && o.BlockRuntimeId == Blocks.Stone);
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

public class EventBusTests
{
    [Fact]
    public void Publish_isolates_listener_exceptions()
    {
        var bus = new EventBus();
        var secondRan = false;
        bus.Subscribe<string>(_ => throw new InvalidOperationException("boom"));
        bus.Subscribe<string>(_ => secondRan = true);

        bus.Publish("hi");
        Assert.True(secondRan);
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
        Assert.False(PlayerInventory.IsValidStackCount(-1));
        Assert.False(PlayerInventory.IsValidStackCount(65));
        Assert.True(PlayerInventory.IsValidStackCount(64));
    }

    [Fact]
    public void TrySet_rejects_out_of_range_before_mutating()
    {
        var inv = new PlayerInventory();
        Assert.False(inv.TrySet(-1, Blocks.Stone, 1));
        Assert.False(inv.TrySet(0, Blocks.Stone, -3));
        Assert.False(inv.TrySet(0, Blocks.Stone, 99));
        Assert.Equal(64, inv.Get(0).Count);
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
        Assert.True(inv.TrySet(0, Blocks.Stone, 60));
        Assert.True(inv.TryAdd(Blocks.Stone, 5));
        Assert.Equal(64, inv.Get(0).Count);
        Assert.Equal(Blocks.Stone, inv.Get(1).RuntimeId);
        Assert.Equal(1, inv.Get(1).Count);

        Assert.True(inv.TryAdd(Blocks.GrassBlock, 3));
        Assert.Equal(Blocks.GrassBlock, inv.Get(2).RuntimeId);
        Assert.Equal(3, inv.Get(2).Count);
    }

    [Fact]
    public void Break_into_inventory_matches_BlockSystem_economy()
    {
        Blocks.EnsureLoaded();
        var world = new World.World(new InMemoryChunkStorage());
        var inv = new PlayerInventory();
        Assert.True(inv.TrySet(0, Blocks.Stone, 1));

        // Place: consume one
        Assert.True(inv.TryConsumeOne(0));
        world.SetBlock(0, Blocks.FlatGrassY, 0, Blocks.Stone);
        Assert.True(inv.Get(0).IsEmpty);

        // Break: give previous block back
        var previous = world.GetBlock(0, Blocks.FlatGrassY, 0);
        Assert.Equal(Blocks.Stone, previous);
        Assert.True(inv.TryAdd(previous));
        world.SetBlock(0, Blocks.FlatGrassY, 0, Blocks.Air);
        Assert.Equal(1, inv.Get(0).Count);
        Assert.Equal(Blocks.Stone, inv.Get(0).RuntimeId);
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
