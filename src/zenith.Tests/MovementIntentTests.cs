using System.Collections.Concurrent;
using System.Net;
using Zenith.Ecs;
using Zenith.Event;
using Zenith.Gameplay.Runtime;
using Zenith.Packets;
using Zenith.Protocol;
using Zenith.Session;
using Zenith.Session.Handler;
using Zenith.Player;
using Zenith.Raknet;
using Zenith.Logging;
using Zenith.Raknet.Network;
using Zenith.Binary;
using Zenith.Server;
using Zenith.World;
using Xunit;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Inventory;
using Zenith.Gameplay.Replication;
using Zenith.Gameplay.Survival;
using Zenith.Gameplay.WorldInteraction;
using static Zenith.Tests.IntentTestHelpers;

namespace Zenith.Tests;

/// <summary>MovementSystem (death, respawn, pose) and what it replicates to other players.</summary>
public class MovementIntentTests
{

    [Fact]
    public void MovementSystem_void_triggers_death_and_dumps_survival_inventory()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("faller");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 5));
        player.Intents.SubmitMovementInput(MovementInputState.From(
            x: 3.5f,
            y: MovementSystem.VoidRescueY - 1f,
            z: 4.5f,
            pitch: 10f,
            yaw: 20f));

        new MovementSystem(fx.Players).Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.IsDead);
        Assert.Equal(0f, player.Health);
        Assert.Equal("generic", player.DeathCause);
        // Still at void pose until client Respawn — no soft-rescue teleport.
        Assert.Equal(3.5f, player.PositionX);
        Assert.Equal(MovementSystem.VoidRescueY - 1f, player.PositionY);
        Assert.Equal(4.5f, player.PositionZ);
        Assert.True(player.Inventory.Get(0).IsEmpty);
        Assert.True(fx.World.FloorDrops.Count >= 1);
    }


    [Fact]
    public void MovementSystem_repeated_dead_ticks_do_not_duplicate_death_loot()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("faller");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 5));
        player.Intents.SubmitMovementInput(MovementInputState.From(
            x: 3.5f,
            y: MovementSystem.VoidRescueY - 1f,
            z: 4.5f,
            pitch: 10f,
            yaw: 20f));
        var movement = new MovementSystem(fx.Players);

        movement.Tick(fx.Clock, fx.Players.Online);
        var stone = StackId.FromBlock(Blocks.Stone);
        var initialLoot = fx.World.FloorDrops.Snapshot()
            .Where(drop => drop.Id == stone)
            .Sum(drop => drop.Count);

        movement.Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.IsDead);
        Assert.Equal(5, initialLoot);
        Assert.Equal(initialLoot, fx.World.FloorDrops.Snapshot()
            .Where(drop => drop.Id == stone)
            .Sum(drop => drop.Count));
    }


    [Fact]
    public void MovementSystem_respawn_restores_spawn_with_empty_bag_after_death_loot()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("faller");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 7));
        player.Intents.SubmitMovementInput(MovementInputState.From(
            x: 3.5f,
            y: MovementSystem.VoidRescueY - 1f,
            z: 4.5f,
            pitch: 10f,
            yaw: 20f));
        var movement = new MovementSystem(fx.Players);
        movement.Tick(fx.Clock, fx.Players.Online);
        Assert.True(player.IsDead);
        Assert.True(player.Inventory.Get(0).IsEmpty);

        player.Intents.SubmitRespawn();
        movement.Tick(fx.Clock, fx.Players.Online);

        Assert.False(player.IsDead);
        Assert.Equal(20f, player.Health);
        Assert.Equal(0f, player.PositionX);
        Assert.Equal(Blocks.FlatSpawnY, player.PositionY);
        Assert.Equal(0f, player.PositionZ);
        Assert.Equal(0f, player.Pitch);
        Assert.True(player.Inventory.Get(0).IsEmpty);
        Assert.True(fx.World.FloorDrops.Count >= 1);
    }


    [Fact]
    public void MovementSystem_void_death_creative_keeps_inventory()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("creativefall", GameMode.Creative);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 5));
        player.Intents.SubmitMovementInput(MovementInputState.From(
            x: 3.5f,
            y: MovementSystem.VoidRescueY - 1f,
            z: 4.5f,
            pitch: 10f,
            yaw: 20f));

        new MovementSystem(fx.Players).Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.IsDead);
        Assert.Equal(5, player.Inventory.Get(0).Count);
        Assert.Equal(0, fx.World.FloorDrops.Count);
    }


    [Fact]
    public void MovementSystem_void_death_keeps_slot_when_floor_softcap_full()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("capfall");
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Air, 0));
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 3));

        for (var i = 0; i < FloorDropStore.SoftCap; i++)
            Assert.True(fx.World.FloorDrops.TryAddOrMerge(
                i, 64, 0, StackId.FromBlock(Blocks.Stone), 1, entityRuntimeIdIfNew: i + 1, out _));

        player.Intents.SubmitMovementInput(MovementInputState.From(
            x: 3.5f,
            y: MovementSystem.VoidRescueY - 1f,
            z: 4.5f,
            pitch: 10f,
            yaw: 20f));
        new MovementSystem(fx.Players).Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.IsDead);
        Assert.Equal(Blocks.Dirt, player.Inventory.Get(0).Id.Value);
        Assert.Equal(3, player.Inventory.Get(0).Count);
        Assert.Equal(FloorDropStore.SoftCap, fx.World.FloorDrops.Count);
    }


    [Fact]
    public void MovementSystem_sneak_fans_SetActorData_flags_to_peer()
    {
        var fx = new IntentTestFixture();
        var a = fx.AddInGamePlayer("alice");
        var b = fx.AddInGamePlayer("bob");
        StandNear(a, 0, 64, 0);
        StandNear(b, 2, 64, 0);

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        a.Intents.SubmitMovementInput(MovementInputState.FromClientAuthInput(
            a.PositionX,
            a.PositionY + Blocks.PlayerEyeHeight,
            a.PositionZ,
            pitch: 0f,
            yaw: 0f,
            sneaking: true));
        new MovementSystem(fx.Players).Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);

        Assert.True(a.IsSneaking);
        Assert.True(a.Replicated.Sneaking);
        Assert.False(a.IsSprinting);
        Assert.True(fx.Transport.Captured.Count >= 1, "peer must receive SetActorData FLAGS");
        _ = b;
    }


    [Fact]
    public void MovementSystem_sprint_start_clears_sneak()
    {
        var fx = new IntentTestFixture();
        var a = fx.AddInGamePlayer("sprinter");
        var b = fx.AddInGamePlayer("viewer");
        a.IsSneaking = true;
        a.Replicated.Sneaking = true;

        a.Intents.SubmitMovementInput(MovementInputState.FromClientAuthInput(
            a.PositionX,
            a.PositionY + Blocks.PlayerEyeHeight,
            a.PositionZ,
            pitch: 0f,
            yaw: 0f,
            sneaking: false,
            sprintStart: true));
        new MovementSystem(fx.Players).Tick(fx.Clock, fx.Players.Online);

        Assert.True(a.IsSprinting);
        Assert.False(a.IsSneaking);
        Assert.True(a.Replicated.Sprinting);
        Assert.False(a.Replicated.Sneaking);
        _ = b;
    }


    [Fact]
    public void MovementSystem_missed_swing_fans_Animate_to_peer()
    {
        var fx = new IntentTestFixture();
        var a = fx.AddInGamePlayer("puncher");
        var b = fx.AddInGamePlayer("viewer");

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        // Same pose so Absolute is not dirty — only swing fan-out.
        a.Replicated.X = a.PositionX;
        a.Replicated.Y = a.PositionY;
        a.Replicated.Z = a.PositionZ;
        a.Replicated.Pitch = a.Pitch;
        a.Replicated.Yaw = a.Yaw;
        a.Replicated.HeadYaw = a.HeadYaw;

        a.Intents.SubmitMovementInput(MovementInputState.FromClientAuthInput(
            a.PositionX,
            a.PositionY + Blocks.PlayerEyeHeight,
            a.PositionZ,
            pitch: a.Pitch,
            yaw: a.Yaw,
            missedSwing: true));
        new MovementSystem(fx.Players).Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);

        Assert.True(fx.Transport.Captured.Count >= 1, "peer must receive Animate SwingArm");
        _ = b;
    }


    [Fact]
    public void MovementSystem_respawn_clears_sneak_sprint()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("croucher");
        player.IsSneaking = true;
        player.IsSprinting = true;
        player.Intents.SubmitMovementInput(MovementInputState.From(
            x: 0f,
            y: MovementSystem.VoidRescueY - 1f,
            z: 0f,
            pitch: 0f,
            yaw: 0f));
        var movement = new MovementSystem(fx.Players);
        movement.Tick(fx.Clock, fx.Players.Online);
        Assert.True(player.IsDead);
        Assert.False(player.IsSneaking);
        Assert.False(player.IsSprinting);

        player.Intents.SubmitRespawn();
        movement.Tick(fx.Clock, fx.Players.Online);
        Assert.False(player.IsDead);
        Assert.False(player.IsSneaking);
        Assert.False(player.IsSprinting);
        Assert.False(player.Replicated.Sneaking);
        Assert.False(player.Replicated.Sprinting);
    }


    [Fact]
    public void PlayerVisibility_RelayEmote_sends_to_other_InGame_peers()
    {
        var fx = new IntentTestFixture();
        var a = fx.AddInGamePlayer("emoter");
        var b = fx.AddInGamePlayer("audience");

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        PeerRelay.RelayEmote(
            a,
            emoteId: "4c8cc107-7341-4d95-91eb-5386cc311b25",
            tickLength: 40,
            xuid: "",
            platformChatId: "",
            fx.Players.Online);
        FlushRaknet(fx.Players);

        Assert.True(fx.Transport.Captured.Count >= 1);
        _ = b;
    }


    [Fact]
    public void PlayerVisibility_RelaySwingArm_sends_to_other_InGame_peers()
    {
        var fx = new IntentTestFixture();
        var a = fx.AddInGamePlayer("miner");
        var b = fx.AddInGamePlayer("viewer");

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        PlayerVisibility.RelaySwingArm(a, fx.Players.Online, swingSource: "mine");
        FlushRaknet(fx.Players);

        Assert.True(fx.Transport.Captured.Count >= 1);
        _ = b;
    }


    [Fact]
    public void MovementSystem_on_ground_change_dirties_Absolute_with_flag()
    {
        var fx = new IntentTestFixture();
        var a = fx.AddInGamePlayer("alice");
        var b = fx.AddInGamePlayer("bob");
        a.PositionX = 1f;
        a.PositionY = Blocks.FlatSpawnY;
        a.PositionZ = 2f;
        a.IsOnGround = false;
        a.Replicated.X = a.PositionX;
        a.Replicated.Y = a.PositionY;
        a.Replicated.Z = a.PositionZ;
        a.Replicated.Pitch = a.Pitch;
        a.Replicated.Yaw = a.Yaw;
        a.Replicated.HeadYaw = a.HeadYaw;
        a.Replicated.OnGround = false;

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        // Same XYZ/look — only on-ground flips.
        a.Intents.SubmitMovementInput(MovementInputState.FromClientAuthInput(
            a.PositionX,
            a.PositionY + Blocks.PlayerEyeHeight,
            a.PositionZ,
            a.Pitch,
            a.Yaw,
            onGround: true));
        new MovementSystem(fx.Players).Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);

        Assert.True(a.IsOnGround);
        Assert.True(a.Replicated.OnGround);
        Assert.True(fx.Transport.Captured.Count >= 1,
            "on-ground-only change must fan Absolute so peers get FLAG_ON_GROUND");
        _ = b;
    }


    [Fact]
    public void PlayerVisibility_RefreshPeerView_sends_without_PlayerList_churn()
    {
        var fx = new IntentTestFixture();
        var alice = fx.AddInGamePlayer("alice", GameMode.Survival);
        var bob = fx.AddInGamePlayer("bob", GameMode.Survival);
        alice.Session.Profile = new ClientProfile("xuid-a", "dev-a", 7, "plat-a");

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        alice.SetGameMode(GameMode.Creative);
        PlayerVisibility.RefreshPeerView(alice, fx.Players.Online);
        FlushRaknet(fx.Players);

        Assert.True(fx.Transport.Captured.Count >= 1);
        var joined = ConcatCaptured(fx);
        Assert.Contains("dev-a"u8.ToArray(), joined);
        _ = bob;
    }


    [Fact]
    public void MovementSystem_cancels_pending_input_after_player_leaves_gameplay()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("disconnect-movement");
        var originalY = player.PositionY;
        player.Intents.SubmitMovementInput(MovementInputState.From(
            x: 20f, y: MovementSystem.VoidRescueY - 1f, z: 20f, pitch: 0f, yaw: 0f));

        player.IsInGame = false;
        new MovementSystem(fx.Players).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(0f, player.PositionX);
        Assert.Equal(originalY, player.PositionY);
        Assert.Equal(0f, player.PositionZ);
        Assert.False(player.IsDead);
    }
}
