using Zenith.World;
using Xunit;

namespace Zenith.Tests;

/// <summary>
/// ADR §114 — the chunk-prefixed binary key format (<see cref="WorldStorageKeys"/>) exists so
/// <see cref="LevelDbChunkStorage.LoadOverlaysForChunkAsync"/>/<see cref="LevelDbChunkStorage.LoadChestsForChunkAsync"/>
/// can Seek+prefix-walk one chunk instead of scanning the whole table. These tests exercise that
/// path directly against the real LevelDB backend, including negative chunk coordinates.
/// </summary>
public sealed class LevelDbChunkStoragePerChunkTests
{
    private static string NewTempDir() =>
        Path.Combine(Path.GetTempPath(), "zenith-perchunk-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task LoadOverlaysForChunkAsync_returns_only_the_requested_chunk()
    {
        var dir = NewTempDir();
        try
        {
            using var storage = new LevelDbChunkStorage(dir);
            await storage.PutOverlayAsync(1, 10, 2, 100);   // chunk (0,0)
            await storage.PutOverlayAsync(5, 11, 5, 101);   // chunk (0,0)
            await storage.PutOverlayAsync(20, 12, 2, 102);  // chunk (1,0)
            await storage.FlushAsync();

            var chunk00 = await storage.LoadOverlaysForChunkAsync(0, 0);
            Assert.Equal(2, chunk00.Count);
            Assert.Contains(chunk00, o => o.X == 1 && o.Y == 10 && o.Z == 2 && o.BlockRuntimeId == 100);
            Assert.Contains(chunk00, o => o.X == 5 && o.Y == 11 && o.Z == 5 && o.BlockRuntimeId == 101);

            var chunk10 = await storage.LoadOverlaysForChunkAsync(1, 0);
            Assert.Single(chunk10);
            Assert.Equal(102, chunk10[0].BlockRuntimeId);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task LoadOverlaysForChunkAsync_handles_negative_chunk_coordinates()
    {
        var dir = NewTempDir();
        try
        {
            using var storage = new LevelDbChunkStorage(dir);
            await storage.PutOverlayAsync(-1, 64, -1, 55);   // chunk (-1,-1)
            await storage.PutOverlayAsync(-17, 64, -17, 56); // chunk (-2,-2)
            await storage.FlushAsync();

            var chunkNeg1 = await storage.LoadOverlaysForChunkAsync(-1, -1);
            Assert.Single(chunkNeg1);
            Assert.Equal((-1, 64, -1, 55), (chunkNeg1[0].X, chunkNeg1[0].Y, chunkNeg1[0].Z, chunkNeg1[0].BlockRuntimeId));

            var chunkNeg2 = await storage.LoadOverlaysForChunkAsync(-2, -2);
            Assert.Single(chunkNeg2);
            Assert.Equal(56, chunkNeg2[0].BlockRuntimeId);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task LoadOverlaysForChunkAsync_returns_empty_for_an_untouched_chunk()
    {
        var dir = NewTempDir();
        try
        {
            using var storage = new LevelDbChunkStorage(dir);
            await storage.PutOverlayAsync(1, 10, 2, 100);
            await storage.FlushAsync();

            var empty = await storage.LoadOverlaysForChunkAsync(99, 99);
            Assert.Empty(empty);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task LoadChestsForChunkAsync_returns_only_the_requested_chunk_and_is_isolated_from_overlays()
    {
        var dir = NewTempDir();
        try
        {
            using var storage = new LevelDbChunkStorage(dir);
            await storage.PutOverlayAsync(1, 10, 2, 100); // same block position family, different store
            await storage.PutChestAsync(1, 10, 2, [1, 2, 3]);
            await storage.PutChestAsync(20, 10, 2, [4, 5, 6]); // chunk (1,0)
            await storage.FlushAsync();

            var chunk00Chests = await storage.LoadChestsForChunkAsync(0, 0);
            Assert.Single(chunk00Chests);
            Assert.Equal((1, 10, 2), (chunk00Chests[0].X, chunk00Chests[0].Y, chunk00Chests[0].Z));
            Assert.Equal(new byte[] { 1, 2, 3 }, chunk00Chests[0].Blob);

            var chunk00Overlays = await storage.LoadOverlaysForChunkAsync(0, 0);
            Assert.Single(chunk00Overlays);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }
}
