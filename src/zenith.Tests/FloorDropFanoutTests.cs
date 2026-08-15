using Zenith.Player;
using Zenith.Raknet.Stream;
using Zenith.World;
using Xunit;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Inventory;
using Zenith.Gameplay.Replication;

namespace Zenith.Tests;

public class FloorDropFanoutTests
{
    public FloorDropFanoutTests() => Blocks.EnsureLoaded();

    [Fact]
    public void TryDeposit_lands_on_origin_when_free()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("dropper");

        var ok = FloorDropFanout.TryDeposit(
            fx.World, fx.Players, fx.Players.Online, 10, 64, 10, StackId.FromBlock(Blocks.Dirt), 3);

        Assert.True(ok);
        Assert.True(fx.World.FloorDrops.TryTake(10, 64, 10, out var id, out var count, out _));
        Assert.Equal(StackId.FromBlock(Blocks.Dirt), id);
        Assert.Equal(3, count);
        _ = player;
    }

    [Fact]
    public void TryDeposit_spirals_to_a_free_cell_when_origin_holds_a_different_stack()
    {
        var fx = new IntentTestFixture();
        _ = fx.AddInGamePlayer("dropper");

        Assert.True(fx.World.FloorDrops.TryAddOrMerge(
            20, 64, 20, StackId.FromBlock(Blocks.Stone), 1, entityRuntimeIdIfNew: 1, out _));

        var ok = FloorDropFanout.TryDeposit(
            fx.World, fx.Players, fx.Players.Online, 20, 64, 20, StackId.FromBlock(Blocks.Dirt), 1, searchRadius: 1);

        Assert.True(ok);
        // Origin cell still holds the original Stone — Dirt landed on a neighboring cell.
        Assert.True(fx.World.FloorDrops.TryTake(20, 64, 20, out var originId, out _, out _));
        Assert.Equal(StackId.FromBlock(Blocks.Stone), originId);

        var foundNeighbor = false;
        for (var dx = -1; dx <= 1 && !foundNeighbor; dx++)
        for (var dz = -1; dz <= 1 && !foundNeighbor; dz++)
        {
            if (dx == 0 && dz == 0) continue;
            if (fx.World.FloorDrops.TryTake(20 + dx, 64, 20 + dz, out var id, out _, out _))
            {
                Assert.Equal(StackId.FromBlock(Blocks.Dirt), id);
                foundNeighbor = true;
            }
        }
        Assert.True(foundNeighbor, "expected the Dirt stack to land on a neighboring cell within radius 1");
    }

    [Fact]
    public void TryDeposit_spirals_when_matching_origin_stack_has_no_space()
    {
        var fx = new IntentTestFixture();
        _ = fx.AddInGamePlayer("dropper");
        var dirt = StackId.FromBlock(Blocks.Dirt);
        Assert.True(fx.World.FloorDrops.TryAddOrMerge(
            24, 64, 24, dirt, 64, entityRuntimeIdIfNew: 1, out _));

        Assert.True(FloorDropFanout.TryDeposit(
            fx.World, fx.Players, fx.Players.Online, 24, 64, 24, dirt, 1, searchRadius: 1));

        Assert.Equal(65, fx.World.FloorDrops.Snapshot().Where(drop => drop.Id == dirt).Sum(drop => drop.Count));
        Assert.Equal(2, fx.World.FloorDrops.Count);
    }

    [Fact]
    public void TryDeposit_returns_false_when_no_free_cell_within_radius()
    {
        var fx = new IntentTestFixture();
        _ = fx.AddInGamePlayer("dropper");

        // Fill origin + every cell within radius 1 with a different id so nothing can merge.
        for (var dx = -1; dx <= 1; dx++)
        for (var dz = -1; dz <= 1; dz++)
            Assert.True(fx.World.FloorDrops.TryAddOrMerge(
                30 + dx, 64, 30 + dz, StackId.FromBlock(Blocks.Stone), 1, entityRuntimeIdIfNew: 1, out _));

        var ok = FloorDropFanout.TryDeposit(
            fx.World, fx.Players, fx.Players.Online, 30, 64, 30, StackId.FromBlock(Blocks.Dirt), 1, searchRadius: 1);

        Assert.False(ok);
    }

    [Fact]
    public void TryDeposit_publishes_AddItemActor_to_online_peers()
    {
        var fx = new IntentTestFixture();
        var a = fx.AddInGamePlayer("alice");
        _ = fx.AddInGamePlayer("bob");

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        FloorDropFanout.TryDeposit(
            fx.World, fx.Players, fx.Players.Online, 40, 64, 40, StackId.FromBlock(Blocks.Dirt), 1);
        FlushRaknet(fx.Players);

        Assert.True(fx.Transport.Captured.Count >= 2, $"expected AddItemActor to reach both peers, got {fx.Transport.Captured.Count}");
        _ = a;
    }

    /// <summary>
    /// Phase XXVI — dropped items previously always sent zero velocity (AddItemActorPacket's
    /// Velocity fields existed on the wire but no caller ever populated them), so a Q-drop had no
    /// toss arc at all client-side, unlike vanilla/PocketMine/Dragonfly. Verifies TryDepositBatch's
    /// velocity parameters actually reach the wire, without needing to reconstruct the full packet
    /// (including its item descriptor) — the velocity floats are immediately followed by a known,
    /// fixed two-byte suffix (metadata count varint 0, FromFishing bool false), so that tail alone is
    /// a reliable, unambiguous anchor to search for in the captured stream.
    /// </summary>
    [Fact]
    public void TryDepositBatch_created_cell_carries_the_requested_toss_velocity_onto_the_wire()
    {
        var fx = new IntentTestFixture();
        _ = fx.AddInGamePlayer("dropper");

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        const float velocityX = -0.15f;
        const float velocityY = 0.2f;
        const float velocityZ = 0.26f;
        var ok = FloorDropFanout.TryDepositBatch(
            fx.World, fx.Players, fx.Players.Online, 60, 64, 60,
            [new FloorDropFanout.DepositRequest(StackId.FromBlock(Blocks.Dirt), 1)],
            searchRadius: 3,
            velocityX: velocityX, velocityY: velocityY, velocityZ: velocityZ);
        FlushRaknet(fx.Players);

        Assert.True(ok);

        var tail = new BinaryStream();
        tail.WriteFloat(velocityX, BinaryStream.Endianess.Little);
        tail.WriteFloat(velocityY, BinaryStream.Endianess.Little);
        tail.WriteFloat(velocityZ, BinaryStream.Endianess.Little);
        tail.WriteUnsignedVarInt(0); // empty EntityMetadata
        tail.WriteBool(false); // FromFishing
        Assert.Contains(tail.GetBufferDisposing().ToArray(), ConcatCaptured(fx));
    }

    /// <summary>An already-settled pile getting topped off is not a fresh toss — even if a caller
    /// (incorrectly) passed a nonzero velocity into a batch that lands on an existing cell, the
    /// republish must still render static, matching every other merge/republish on the ground.</summary>
    [Fact]
    public void TryDepositBatch_merge_onto_an_existing_cell_never_carries_velocity()
    {
        var fx = new IntentTestFixture();
        _ = fx.AddInGamePlayer("dropper");
        Assert.True(FloorDropFanout.TryDeposit(
            fx.World, fx.Players, fx.Players.Online, 70, 64, 70, StackId.FromBlock(Blocks.Dirt), 1));

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        var ok = FloorDropFanout.TryDepositBatch(
            fx.World, fx.Players, fx.Players.Online, 70, 64, 70,
            [new FloorDropFanout.DepositRequest(StackId.FromBlock(Blocks.Dirt), 1)],
            searchRadius: 3,
            velocityX: 0.3f, velocityY: 0.2f, velocityZ: 0.3f);
        FlushRaknet(fx.Players);

        Assert.True(ok);

        var tail = new BinaryStream();
        tail.WriteFloat(0f, BinaryStream.Endianess.Little);
        tail.WriteFloat(0f, BinaryStream.Endianess.Little);
        tail.WriteFloat(0f, BinaryStream.Endianess.Little);
        tail.WriteUnsignedVarInt(0);
        tail.WriteBool(false);
        Assert.Contains(tail.GetBufferDisposing().ToArray(), ConcatCaptured(fx));
    }

    /// <summary>End-to-end: a real Q-drop through InventorySystem (not a direct FloorDropFanout call)
    /// must actually reach the wire with the toss velocity, not just prove the plumbing works in
    /// isolation. Yaw=45° keeps both horizontal components comfortably nonzero (Yaw=0 makes X exactly
    /// -sin(0), i.e. negative zero — a different bit pattern than the +0.0 a naive expected literal
    /// would use, even though they compare equal as floats). Expected values are computed with the
    /// identical formula InventorySystem uses (same yaw-to-direction convention ProjectileSystem
    /// already uses for arrow launch) rather than hand-rounded literals, so this can't drift from
    /// floating-point rounding independent of the production code actually being correct.</summary>
    [Fact]
    public void InventorySystem_drop_action_sends_a_nonzero_toss_velocity()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("q-dropper");
        player.PositionX = 2;
        player.PositionY = 64;
        player.PositionZ = 2;
        player.Yaw = 45f;
        Assert.True(player.Inventory.TrySet(0, StackId.FromBlock(Blocks.Dirt), 4));

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(9001, [
            InventoryStackAction.Drop(0, 4)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);

        const float dropTossSpeed = 0.3f;
        const float dropTossUpwardVelocity = 0.2f;
        var radians = player.Yaw * (MathF.PI / 180f);
        var expectedVelocityX = -MathF.Sin(radians) * dropTossSpeed;
        var expectedVelocityZ = MathF.Cos(radians) * dropTossSpeed;
        Assert.NotEqual(0f, expectedVelocityX);
        Assert.NotEqual(0f, expectedVelocityZ);

        var tail = new BinaryStream();
        tail.WriteFloat(expectedVelocityX, BinaryStream.Endianess.Little);
        tail.WriteFloat(dropTossUpwardVelocity, BinaryStream.Endianess.Little);
        tail.WriteFloat(expectedVelocityZ, BinaryStream.Endianess.Little);
        tail.WriteUnsignedVarInt(0);
        tail.WriteBool(false);
        Assert.Contains(tail.GetBufferDisposing().ToArray(), ConcatCaptured(fx));
    }

    [Fact]
    public void TryDeposit_zero_count_or_empty_id_is_a_no_op_success()
    {
        var fx = new IntentTestFixture();
        _ = fx.AddInGamePlayer("dropper");

        Assert.True(FloorDropFanout.TryDeposit(
            fx.World, fx.Players, fx.Players.Online, 50, 64, 50, StackId.FromBlock(Blocks.Dirt), 0));
        Assert.False(fx.World.FloorDrops.TryTake(50, 64, 50, out _, out _, out _));
    }

    private static void FlushRaknet(Zenith.Player.PlayerManager players)
    {
        foreach (var p in players.Online)
            p.Session.RakSession.Tick();
    }

    private static byte[] ConcatCaptured(IntentTestFixture fx)
    {
        var total = 0;
        foreach (var chunk in fx.Transport.Captured)
            total += chunk.Length;
        var buf = new byte[total];
        var offset = 0;
        foreach (var chunk in fx.Transport.Captured)
        {
            chunk.CopyTo(buf, offset);
            offset += chunk.Length;
        }

        return buf;
    }
}
