using Zenith.Gameplay.Policy;
using Xunit;
using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.WorldInteraction;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>
/// Plan 002, phase 4a: a block that starts to fall is never lost or duplicated, not by landing on something that appeared mid-fall
/// and not by a crash at any point of its life. A "crash" here is a second world booted from a snapshot of the storage as it was at
/// that instant: the bytes already written, none that were only in memory.
/// </summary>
public sealed class FallingBlockDurabilityTests
{
    private const int X = 0;
    private const int Z = 0;
    private static readonly int Ground = Blocks.FlatGrassY;
    private static readonly int Rest = Ground + 1;
    private static readonly int Origin = Ground + 3;
    private static readonly ChunkCoord Coord = new(0, 0);

    public FallingBlockDurabilityTests() => Blocks.EnsureLoaded();

    private sealed class Rig
    {
        public required InMemoryChunkStorage Storage { get; init; }
        public required World.World World { get; init; }
        public required GravitySystem Gravity { get; init; }
        public required RecordingLogger Log { get; init; }

        public static Rig Boot(InMemoryChunkStorage storage)
        {
            var log = new RecordingLogger();
            var world = new World.World(storage, log);
            return new Rig { Storage = storage, World = world, Gravity = new GravitySystem(world, new PlayerManager(), new EnvironmentChangePolicies(), log), Log = log };
        }

        public GameClock Clock { get; } = new();

        public void Tick()
        {
            Clock.AdvanceBy(1);
            Gravity.Tick(Clock, Array.Empty<Zenith.Player.Player>());
        }

        /// <summary>What the disk holds now: the column images written so far, nothing that is still only in memory.</summary>
        public void Save() => World.PersistDirtyColumns(int.MaxValue);

        /// <summary>
        /// The server dies right now. A new server starts from what had been written and, as when a player arrives or a neighbour
        /// is touched, loads the column. <see cref="CrashWithoutLoading"/> is the same without that load.
        /// </summary>
        public Rig Crash()
        {
            var rebooted = CrashWithoutLoading();
            rebooted.World.GetBlock(X, Ground, Z);
            return rebooted;
        }

        public Rig CrashWithoutLoading() => Boot(Storage.Snapshot());

        public bool IsQuiet() => World.GravityPending.Count == 0 && World.FallingBlocks.Active.Count == 0;

        public void TickUntilQuiet(int budget = 300)
        {
            Tick(); // the first tick adopts what the loaded column carried
            for (var i = 0; i < budget && !IsQuiet(); i++) Tick();
            Assert.True(IsQuiet(), "gravity did not settle");
        }

        public int Count(int block)
        {
            var found = 0;
            for (var y = WorldColumn.MinY; y <= WorldColumn.MaxY; y++)
                if (World.GetBlock(X, y, Z) == block) found++;
            return found;
        }

        /// <summary>Blocks of this kind in the world plus the ones in the air: every representation there is.</summary>
        public int Representations(int block) =>
            Count(block) + World.FallingBlocks.Active.Count(entry => entry.BlockRuntimeId == block);

        public WorldColumn Column
        {
            get
            {
                World.GetBlock(X, Ground, Z);
                Assert.True(World.TryGetLoadedColumn(0, 0, out var column));
                return column;
            }
        }
    }

    private static Rig SandAtOrigin(params int[] extraAbove)
    {
        var rig = Rig.Boot(new InMemoryChunkStorage());
        rig.World.SetQuietly(X, Origin, Z, Blocks.Sand);
        rig.World.GravityPending.TryEnqueue(X, Origin, Z);
        for (var i = 0; i < extraAbove.Length; i++)
        {
            rig.World.SetQuietly(X, Origin + 1 + i, Z, extraAbove[i]);
            rig.World.GravityPending.TryEnqueue(X, Origin + 1 + i, Z);
        }

        rig.Save();
        return rig;
    }

    private static WorldColumn Image(Rig rig)
    {
        var blob = rig.Storage.GetColumnAsync(Coord).AsTask().GetAwaiter().GetResult();
        Assert.NotNull(blob);
        return ColumnCodec.Decode(blob, Coord, 0, Blocks.Palette);
    }

    // ---- the lifecycle, crashed at every step ----

    [Fact]
    public void A_crash_at_any_tick_boundary_resolves_to_exactly_one_block_and_no_record()
    {
        var rig = SandAtOrigin();

        void CrashHere(string when)
        {
            var rebooted = rig.Crash();
            // Before it is adopted the image holds the block either at its origin or as a record; never both, never neither.
            var column = rebooted.Column;
            var recorded = column.PendingFalls.Count;
            var placed = rebooted.Count(Blocks.Sand);
            Assert.True(placed + recorded == 1, $"{when}: {placed} in the world and {recorded} recorded, expected one representation");

            rebooted.TickUntilQuiet();

            Assert.Empty(rebooted.Column.PendingFalls);
            Assert.Equal(1, rebooted.Count(Blocks.Sand));
            Assert.True(rebooted.Count(Blocks.Sand) == 1 && rebooted.World.GetBlock(X, placedAt(rebooted), Z) == Blocks.Sand, when);
            // And what was saved after recovering holds no record either.
            rebooted.Save();
            Assert.Empty(Image(rebooted).PendingFalls);
        }

        // A block still sitting at its origin was never re-queued (that queue is memory only); it is still one block.
        static int placedAt(Rig r)
        {
            for (var y = WorldColumn.MinY; y <= WorldColumn.MaxY; y++)
                if (r.World.GetBlock(X, y, Z) == Blocks.Sand) return y;
            return int.MinValue;
        }

        CrashHere("before the first tick");
        var finished = false;
        for (var tick = 1; tick <= 80 && !finished; tick++)
        {
            rig.Tick();
            Assert.Equal(1, rig.Representations(Blocks.Sand)); // live invariant at every boundary
            CrashHere($"tick {tick}, before it was saved (the disk still holds the previous image)");
            rig.Save();
            CrashHere($"tick {tick}, after it was saved");
            finished = rig.IsQuiet();
        }

        Assert.True(finished);
        Assert.Equal(Blocks.Sand, rig.World.GetBlock(X, Rest, Z));
    }

    [Fact]
    public void No_saved_image_shows_the_cleared_origin_without_its_record_or_a_landed_block_with_one()
    {
        var rig = SandAtOrigin();

        var before = Image(rig);
        Assert.Equal(Blocks.Sand, before.Get(X, Origin, Z));
        Assert.Empty(before.PendingFalls);

        rig.Tick(); // BeginFall: records, then clears
        rig.Save();
        var inFlight = Image(rig);
        Assert.Equal(Blocks.Air, inFlight.Get(X, Origin, Z));
        var record = Assert.Single(inFlight.PendingFalls);
        Assert.Equal(Origin, record.OriginY);
        Assert.Equal(Blocks.Sand, record.BlockRuntimeId);

        for (var i = 0; i < 100 && !rig.IsQuiet(); i++)
        {
            rig.Tick();
            rig.Save();
            var image = Image(rig);
            var landed = image.Get(X, Rest, Z) == Blocks.Sand;
            Assert.True(landed ? image.PendingFalls.Count == 0 : image.PendingFalls.Count == 1,
                $"tick {i}: landed={landed}, records={image.PendingFalls.Count}");
        }

        Assert.Equal(Blocks.Sand, Image(rig).Get(X, Rest, Z));
    }

    [Fact]
    public void Two_stacked_blocks_in_flight_both_survive_a_crash()
    {
        var rig = SandAtOrigin(Blocks.Sand);
        rig.Tick();
        rig.Tick();
        rig.Save();
        Assert.Equal(2, rig.Representations(Blocks.Sand));

        var rebooted = rig.Crash();
        rebooted.TickUntilQuiet();

        Assert.Equal(Blocks.Sand, rebooted.World.GetBlock(X, Rest, Z));
        Assert.Equal(Blocks.Sand, rebooted.World.GetBlock(X, Rest + 1, Z));
        Assert.Equal(2, rebooted.Count(Blocks.Sand));
        Assert.Empty(rebooted.Column.PendingFalls);
    }

    [Fact]
    public void A_column_nobody_has_loaded_keeps_its_record_in_storage_until_it_is_loaded()
    {
        var rig = SandAtOrigin();
        rig.Tick();
        rig.Save();

        var rebooted = rig.CrashWithoutLoading();
        for (var i = 0; i < 10; i++) rebooted.Tick();
        Assert.Empty(rebooted.World.FallingBlocks.Active);
        Assert.Single(Image(rebooted).PendingFalls); // still on disk, nothing lost while it waits

        rebooted.World.GetBlock(X, Ground, Z); // somebody needs the column
        rebooted.TickUntilQuiet();
        Assert.Equal(Blocks.Sand, rebooted.World.GetBlock(X, Rest, Z));
        Assert.Equal(1, rebooted.Count(Blocks.Sand));
    }

    // ---- recovery is idempotent and the destination is verified ----

    [Fact]
    public async Task Recovery_adopts_each_record_once_however_often_it_is_asked()
    {
        var rig = SandAtOrigin();
        rig.Tick();
        rig.Save();

        var rebooted = rig.Crash();
        rebooted.Tick();
        Assert.Single(rebooted.World.FallingBlocks.Active);
        Assert.False(rebooted.World.TryTakeColumnWithSavedFalls(out _));

        // The column being asked for again (a second load request, a second tick) adds nothing.
        await rebooted.World.GetOrCreateColumnAsync(0, 0);
        rebooted.Tick();
        rebooted.Tick();
        Assert.True(rebooted.World.FallingBlocks.Active.Count <= 1);
        Assert.Equal(1, rebooted.Representations(Blocks.Sand));
    }

    [Fact]
    public void Booting_twice_from_the_same_image_gives_the_same_result()
    {
        var rig = SandAtOrigin();
        rig.Tick();
        rig.Tick();
        rig.Save();

        var first = rig.Crash();
        var second = rig.Crash();
        first.TickUntilQuiet();
        second.TickUntilQuiet();

        for (var y = WorldColumn.MinY; y <= WorldColumn.MaxY; y++)
            Assert.Equal(first.World.GetBlock(X, y, Z), second.World.GetBlock(X, y, Z));
    }

    [Fact]
    public void A_block_placed_in_the_landing_cell_before_recovery_is_never_overwritten()
    {
        var rig = SandAtOrigin();
        rig.Tick();
        rig.Save();

        var rebooted = rig.Crash();
        rebooted.World.SetBlock(X, Rest, Z, Blocks.Stone); // the landing cell, taken after the crash
        rebooted.TickUntilQuiet();

        Assert.Equal(Blocks.Stone, rebooted.World.GetBlock(X, Rest, Z));
        Assert.Equal(Blocks.Sand, rebooted.World.GetBlock(X, Rest + 1, Z));
        Assert.Equal(1, rebooted.Count(Blocks.Sand));
    }

    [Fact]
    public void A_block_placed_in_the_origin_cell_before_recovery_is_never_overwritten_and_the_fall_rests_above_it()
    {
        var rig = SandAtOrigin();
        rig.Tick();
        rig.Save();

        var rebooted = rig.Crash();
        rebooted.World.SetBlock(X, Origin, Z, Blocks.Stone);
        rebooted.TickUntilQuiet();

        Assert.Equal(Blocks.Stone, rebooted.World.GetBlock(X, Origin, Z));
        Assert.Equal(Blocks.Sand, rebooted.World.GetBlock(X, Origin + 1, Z));
        Assert.Equal(1, rebooted.Count(Blocks.Sand));
    }

    [Fact]
    public void A_block_placed_mid_fall_in_the_landing_cell_is_never_overwritten()
    {
        var rig = SandAtOrigin();
        rig.Tick();
        rig.World.SetBlock(X, Rest, Z, Blocks.Stone);
        rig.TickUntilQuiet();

        Assert.Equal(Blocks.Stone, rig.World.GetBlock(X, Rest, Z));
        Assert.Equal(Blocks.Sand, rig.World.GetBlock(X, Rest + 1, Z));
        Assert.Equal(1, rig.Count(Blocks.Sand));
        Assert.Empty(rig.Column.PendingFalls);
    }

    // ---- which cells accept a falling block ----

    private static int Id(string name)
    {
        Assert.True(Blocks.TryGetDefaultRuntimeId(name, out var id), name);
        return id;
    }

    [Fact]
    public void The_landing_cell_is_taken_over_only_when_it_is_air_fire_a_liquid_a_plant_or_a_torch()
    {
        var accepted = new (string Name, int Id)[]
        {
            ("water", Blocks.Water),
            ("fire", Id("minecraft:fire")),
            ("short grass", Id("minecraft:short_grass")),
            ("torch", Id("minecraft:torch")),
        };
        foreach (var (name, id) in accepted)
        {
            var rig = SandAtOrigin();
            rig.World.SetBlock(X, Rest, Z, id);
            rig.TickUntilQuiet();
            Assert.True(Blocks.Sand == rig.World.GetBlock(X, Rest, Z), $"{name} should give way");
            Assert.Equal(1, rig.Count(Blocks.Sand));
        }
    }

    [Fact]
    public void Anything_that_blocks_movement_or_is_an_open_door_stops_the_fall_and_is_kept()
    {
        var openDoor = Id("minecraft:wooden_door");
        Assert.True(Blocks.Catalog.States.TrySet(ref openDoor, StateKeys.Open, true));

        var kept = new (string Name, int Id)[]
        {
            ("stone", Blocks.Stone),
            ("open door", openDoor),
        };
        foreach (var (name, id) in kept)
        {
            var rig = SandAtOrigin();
            rig.World.SetBlock(X, Rest, Z, id);
            rig.TickUntilQuiet();
            Assert.True(id == rig.World.GetBlock(X, Rest, Z), $"{name} must stay");
            Assert.True(Blocks.Sand == rig.World.GetBlock(X, Rest + 1, Z), $"sand rests above {name}");
            Assert.Equal(1, rig.Count(Blocks.Sand));
        }
    }

    // ---- when nothing accepts the block ----

    [Fact]
    public void A_block_with_no_accepting_cell_stays_in_flight_is_saved_logged_once_and_lands_when_room_appears()
    {
        var rig = SandAtOrigin();
        rig.Tick();
        for (var y = Rest; y <= WorldColumn.MaxY; y++) rig.World.SetBlock(X, y, Z, Blocks.Stone); // the whole column above the ground, filled

        for (var i = 0; i < 3 * GravitySystem.StuckRetryTicks; i++) rig.Tick();

        Assert.Single(rig.World.FallingBlocks.Active);
        Assert.True(rig.World.FallingBlocks.Active[0].IsStuck);
        Assert.Equal(1, rig.Log.Lines.Count(line => line.StartsWith("error:") && line.Contains("no cell that accepts it")));

        // Saved, and still conserved after a restart.
        rig.Save();
        Assert.Single(Image(rig).PendingFalls);
        var rebooted = rig.Crash();
        for (var i = 0; i < 2 * GravitySystem.StuckRetryTicks; i++) rebooted.Tick();
        Assert.Single(rebooted.World.FallingBlocks.Active);
        Assert.Single(rebooted.Column.PendingFalls);

        // Room appears: it lands within one retry period.
        rebooted.World.SetBlock(X, WorldColumn.MaxY, Z, Blocks.Air);
        for (var i = 0; i < GravitySystem.StuckRetryTicks + 1; i++) rebooted.Tick();
        Assert.True(rebooted.IsQuiet());
        Assert.Equal(Blocks.Sand, rebooted.World.GetBlock(X, WorldColumn.MaxY, Z));
        Assert.Empty(rebooted.Column.PendingFalls);
    }

    [Fact]
    public void Falling_into_the_void_is_the_one_exit_and_it_clears_the_record_with_it()
    {
        var rig = Rig.Boot(new InMemoryChunkStorage());
        for (var y = WorldColumn.MinY; y <= Ground; y++) rig.World.SetBlock(X, y, Z, Blocks.Air);
        rig.World.SetQuietly(X, WorldColumn.MinY + 4, Z, Blocks.Sand);
        rig.World.GravityPending.TryEnqueue(X, WorldColumn.MinY + 4, Z);
        rig.Save();

        rig.TickUntilQuiet();
        rig.Save();

        Assert.Equal(0, rig.Count(Blocks.Sand));
        Assert.Empty(Image(rig).PendingFalls);
    }

    // ---- residency and shutdown ----

    [Fact]
    public void A_column_with_a_block_in_the_air_is_not_evicted_until_it_lands()
    {
        var rig = SandAtOrigin();
        rig.Tick();
        Assert.False(rig.World.TryEvictChunk(0, 0));

        rig.TickUntilQuiet();
        Assert.True(rig.World.TryEvictChunk(0, 0));
    }

    [Fact]
    public async Task A_graceful_stop_lands_the_block_and_the_saved_image_has_no_record()
    {
        var rig = SandAtOrigin();
        rig.Tick();
        rig.Tick();

        rig.Gravity.SettleAllPending();
        await rig.World.FlushPersistenceAsync();

        var rebooted = rig.Crash();
        Assert.Empty(rebooted.Column.PendingFalls);
        Assert.Equal(Blocks.Sand, rebooted.World.GetBlock(X, Rest, Z));
        Assert.Equal(1, rebooted.Count(Blocks.Sand));
    }

    [Fact]
    public async Task A_graceful_stop_right_after_boot_settles_falls_that_were_saved_but_not_yet_adopted()
    {
        var rig = SandAtOrigin();
        rig.Tick();
        rig.Save();

        var rebooted = rig.Crash();
        rebooted.World.GetBlock(X, Ground, Z); // the column is loaded; no tick has adopted its fall yet
        rebooted.Gravity.SettleAllPending();
        await rebooted.World.FlushPersistenceAsync();

        var again = rebooted.Crash();
        Assert.Empty(again.Column.PendingFalls);
        Assert.Equal(Blocks.Sand, again.World.GetBlock(X, Rest, Z));
    }

    // ---- the column image itself ----

    [Fact]
    public void A_version_one_column_image_still_loads_and_has_no_falls()
    {
        var rig = Rig.Boot(new InMemoryChunkStorage());
        rig.World.SetBlock(X, Origin, Z, Blocks.Stone);
        var v2 = ColumnCodec.Encode(rig.Column, Blocks.Palette);

        // A version 1 image is the same bytes without the trailing falls section (a single zero count) and with version 1.
        var v1 = v2[..^1];
        v1[2] = 1;

        var decoded = ColumnCodec.Decode(v1, Coord, 0, Blocks.Palette);
        Assert.Equal(Blocks.Stone, decoded.Get(X, Origin, Z));
        Assert.Empty(decoded.PendingFalls);
    }

    [Fact]
    public void A_fall_record_outside_its_column_is_rejected_as_corrupt()
    {
        var rig = Rig.Boot(new InMemoryChunkStorage());
        var column = rig.Column;
        column.AddPendingFall(20, 4, Origin, Blocks.Sand); // local x 20 does not exist
        var blob = ColumnCodec.Encode(column, Blocks.Palette);

        Assert.Throws<InvalidDataException>(() => ColumnCodec.Decode(blob, Coord, 0, Blocks.Palette));
    }
}
