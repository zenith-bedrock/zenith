using Zenith.Gameplay.Runtime;
using Zenith.Player;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// Aplica <see cref="MovementInputState"/> no tick e replica pose aos peers.
/// Fan-out a todos online (exceto self) é aceitável neste estágio; VisibilitySystem futuro
/// poderá restringir peers relevantes.
/// </summary>
sealed class MovementSystem : IGameSystem
{
    private readonly PlayerManager _players;

    public MovementSystem(PlayerManager players) => _players = players;

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

            ReplicateToPeers(player);
        }
    }

    private void ReplicateToPeers(global::Zenith.Player.Player mover)
    {
        if (_players.Count < 2) return;

        foreach (var peer in _players.Online)
        {
            if (ReferenceEquals(peer, mover)) continue;

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
