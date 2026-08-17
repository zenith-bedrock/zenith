using Xunit;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>
/// ADR §124 — before this, inventory/armor/playerdata/chest Puts each scheduled their own
/// <c>Task.Run</c>, an unbounded thread-pool-scheduling delay before the write ever reached ZLDB;
/// under a hard kill (<c>kill -9</c>/<c>taskkill /F</c>) between "gameplay called Persist" and
/// "the queued Task.Run actually executed," the write was lost with no documented bound on how
/// large that window could get. All Puts now enqueue onto one queue drained by a single background
/// worker polling every 50ms (<c>LevelDbChunkStorage.WriteWorkerPollMs</c>), giving a concrete,
/// testable worst-case instead of "whatever the thread pool gets around to."
/// <para/>
/// These tests prove the periodic worker alone — not <c>FlushAsync</c>, not a read-triggered
/// force-drain — lands writes within that bound. <c>TryGetProperty("zldb.num-entries", ...)</c> is
/// used as the probe specifically because it queries ZLDB's memtable directly and does not itself
/// force-drain the queue (unlike <c>GetInventoryAsync</c>/etc., which always force-drain before
/// reading for the S39b quit→rejoin race) — so a count change here can only be the background
/// worker's own doing.
/// </summary>
public sealed class LevelDbChunkStorageDurabilityTests
{
    private static string NewTempDir() =>
        Path.Combine(Path.GetTempPath(), "zenith-durability-" + Guid.NewGuid().ToString("N"));

    private static async Task WaitForEntryCountAsync(LevelDbChunkStorage storage, int expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (storage.TryGetProperty("zldb.num-entries", out var raw) && int.Parse(raw) >= expected)
                return;
            await Task.Delay(5);
        }

        Assert.Fail($"zldb.num-entries never reached {expected} within {timeout}.");
    }

    [Fact]
    public async Task Chest_put_lands_via_the_background_worker_alone_without_FlushAsync()
    {
        var dir = NewTempDir();
        try
        {
            using var storage = new LevelDbChunkStorage(dir);
            await storage.PutChestAsync(1, 2, 3, [9, 9, 9]);

            // No FlushAsync, no Get* call (which would force-drain itself) — only the periodic
            // worker can be responsible for this landing. Bound is generous vs. the 50ms poll to
            // avoid CI flakiness, not because the guarantee itself is looser.
            await WaitForEntryCountAsync(storage, expected: 1, TimeSpan.FromSeconds(2));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Inventory_playerdata_and_armor_puts_all_land_via_the_background_worker_alone()
    {
        var dir = NewTempDir();
        try
        {
            using var storage = new LevelDbChunkStorage(dir);
            var uuid = Guid.NewGuid();
            await storage.PutInventoryAsync(uuid, [1]);
            await storage.PutArmorAsync(uuid, [2]);
            await storage.PutPlayerDataAsync(uuid, [3]);

            await WaitForEntryCountAsync(storage, expected: 3, TimeSpan.FromSeconds(2));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Simulates the exact crash window this fix bounds: many writes queued back-to-back, then the
    /// storage is closed (as <c>Dispose</c> would run on any *graceful* shutdown path) without an
    /// explicit <c>FlushAsync</c> — proving <c>Dispose</c>'s own final drain (not just the periodic
    /// worker) also lands everything queued, and a reopen sees it all.
    /// </summary>
    [Fact]
    public async Task Writes_queued_just_before_Dispose_are_not_lost()
    {
        var dir = NewTempDir();
        try
        {
            var uuid = Guid.NewGuid();
            {
                using var storage = new LevelDbChunkStorage(dir);
                for (var i = 0; i < 50; i++)
                    await storage.PutChestAsync(i, 0, 0, [(byte)i]);
                await storage.PutInventoryAsync(uuid, [42]);
                // No FlushAsync — Dispose (below, via `using`) must still drain everything.
            }

            using var reopened = new LevelDbChunkStorage(dir);
            var seen = 0;
            await reopened.ForEachChestAsync((_, _, _, _) => seen++);
            Assert.Equal(50, seen);

            var inv = await reopened.GetInventoryAsync(uuid);
            Assert.NotNull(inv);
            Assert.Equal(42, inv![0]);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }
}
