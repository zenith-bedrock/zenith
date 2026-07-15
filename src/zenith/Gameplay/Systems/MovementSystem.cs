using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// Aplica <see cref="MovementInputState"/> no tick e replica pose aos peers.
/// Void soft-rescue (ADR §40): teleport to flat spawn without death/Respawn wire.
/// </summary>
sealed class MovementSystem : IGameSystem
{
    /// <summary>Y below FlatMinY - margin triggers soft-rescue to FlatSpawnY.</summary>
    public const float VoidRescueMargin = 8f;

    private readonly PlayerManager _players;

    public MovementSystem(PlayerManager players) => _players = players;

    public static float VoidRescueY => Blocks.FlatMinY - VoidRescueMargin;

    public void Tick(GameClock clock)
    {
        _ = clock;
        if (_players.Count == 0) return;

        foreach (var player in _players.Online)
        {
            if (!player.TryConsumeMovementInput(out var input)) continue;

            player.PositionX = input.X;
            player.PositionY = input.Y;
            player.PositionZ = input.Z;
            player.Pitch = input.Pitch;
            player.Yaw = input.Yaw;
            player.HeadYaw = input.Yaw;

            if (player.PositionY < VoidRescueY)
                SoftRescueFromVoid(player);

            ReplicateToPeers(player);
        }
    }

    private static void SoftRescueFromVoid(global::Zenith.Player.Player player)
    {
        player.PositionY = Blocks.FlatSpawnY;
        player.Pitch = 0;
        // Keep XZ — player falls back onto flat at same column when possible.
        player.Session.Protocol.Entity.SendMoveAbsolute(
            actorRuntimeId: (ulong)player.RuntimeId,
            x: player.PositionX,
            y: player.PositionY,
            z: player.PositionZ,
            pitch: player.Pitch,
            yaw: player.Yaw,
            headYaw: player.HeadYaw);
    }

    private void ReplicateToPeers(global::Zenith.Player.Player mover)
    {
        if (_players.Count < 2) return;

        foreach (var peer in _players.Online)
        {
            if (ReferenceEquals(peer, mover) || !peer.IsInGame) continue;

            peer.Session.Protocol.Entity.SendMoveAbsolute(
                actorRuntimeId: (ulong)mover.RuntimeId,
                x: mover.PositionX,
                y: mover.PositionY,
                z: mover.PositionZ,
                pitch: mover.Pitch,
                yaw: mover.Yaw,
                headYaw: mover.HeadYaw);
        }
    }
}
