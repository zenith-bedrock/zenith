using Zenith.Binary;
using Zenith.Ecs;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Survival;
using Zenith.Packets;
using Zenith.Player;
using Zenith.Session.Handler;
using Xunit;

namespace Zenith.Tests;

public class TeleportAcknowledgementTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Predicted_position_waits_for_ack_and_ack_survives_latest_input(bool playerAction)
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("teleport");
        var movement = new MovementSystem(fx.Players);
        player.BeginTeleport(); var x = player.PositionX;
        player.Intents.SubmitMovementInput(MovementInputState.From(x + 50, player.PositionY, 0, 0, 0));
        movement.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(x, player.PositionX); Assert.True(player.AwaitingTeleport);
        if (playerAction)
        {
            var r = new BinaryStream(new PlayerActionPacket
            {
                ActorRuntimeId = (ulong)player.RuntimeId, Action = 30
            }.Encode().ToArray());
            _ = r.ReadUnsignedVarInt();
            new InGameSessionHandler().HandleDataPacket(player.Session,
                new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.PLAYER_ACTION_PACKET }, ref r);
        }
        else
        {
            var ack = MovementInputState.From(x + 1, player.PositionY, 0, 0, 0);
            ack.HandledTeleport = true; player.Intents.SubmitMovementInput(ack);
        }
        Assert.True(player.AwaitingTeleport); // handler/mailbox does not decide gameplay
        player.Intents.SubmitMovementInput(MovementInputState.From(x + 2, player.PositionY, 0, 0, 0));
        movement.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(x + 2, player.PositionX); Assert.False(player.AwaitingTeleport);
    }

    [Fact]
    public void New_teleport_discards_queued_pose_and_ack_from_previous_context()
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("new-teleport");
        player.Intents.SubmitHandledTeleport();
        player.Intents.SubmitMovementInput(MovementInputState.From(40, player.PositionY, 0, 0, 0));
        player.BeginTeleport();
        Assert.False(player.Intents.TryPeekMovementInput(out _));
        player.Intents.SubmitMovementInput(MovementInputState.From(50, player.PositionY, 0, 0, 0));
        new MovementSystem(fx.Players).Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(0f, player.PositionX); Assert.True(player.AwaitingTeleport);
    }

    [Fact]
    public void Respawn_position_cannot_be_overwritten_before_teleport_ack()
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("respawn-teleport");
        player.PositionX = 40;
        player.ApplyDamage(DamageSource.Generic, 20, fx.Clock.CurrentTick);
        player.Intents.SubmitRespawn();
        var movement = new MovementSystem(fx.Players); movement.Tick(fx.Clock, fx.Players.Online);
        Assert.False(player.IsDead); Assert.True(player.AwaitingTeleport);
        player.Intents.SubmitMovementInput(MovementInputState.From(40, player.PositionY, 0, 0, 0));
        movement.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(0f, player.PositionX);
    }

    [Fact]
    public void Dismount_begins_teleport_barrier()
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("dismount-teleport");
        var carts = new MinecartSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = carts.SpawnMinecart(player.PositionX + 1, player.PositionY, player.PositionZ);
        Assert.True(carts.Stores.Identities.TryGet(id, out var identity));
        player.Intents.SubmitInteractIntent(identity.ActorUniqueId); carts.Tick(fx.Clock, fx.Players.Online);
        Assert.NotNull(player.RidingEntityId);
        player.Intents.SubmitInteractIntent(identity.ActorUniqueId); carts.Tick(fx.Clock, fx.Players.Online);
        Assert.Null(player.RidingEntityId); Assert.True(player.AwaitingTeleport);
        var position = player.PositionX;
        player.Intents.SubmitMovementInput(MovementInputState.From(position + 40, player.PositionY, 0, 0, 0));
        new MovementSystem(fx.Players).Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(position, player.PositionX);
    }

    [Fact]
    public void Disconnected_player_clears_barrier_without_applying_input()
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("closed-teleport");
        player.BeginTeleport(); player.IsInGame = false;
        player.Intents.SubmitHandledTeleport();
        player.Intents.SubmitMovementInput(MovementInputState.From(40, player.PositionY, 0, 0, 0));
        new MovementSystem(fx.Players).Tick(fx.Clock, fx.Players.Online);
        Assert.False(player.AwaitingTeleport); Assert.Equal(0f, player.PositionX);
    }
}
