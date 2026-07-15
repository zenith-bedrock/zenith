using Zenith.Gameplay.Runtime;
using Zenith.Network.Protocol;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// Aplica <see cref="MovementInputState"/> no tick e replica pose aos peers quando dirty (ADR §44).
/// Void soft-rescue (ADR §40): teleport to flat spawn without death/Respawn wire.
/// </summary>
sealed class MovementSystem : IGameSystem
{
    /// <summary>Y below FlatMinY - margin triggers soft-rescue to FlatSpawnY.</summary>
    public const float VoidRescueMargin = 8f;

    private readonly PlayerManager _players;
    private readonly List<global::Zenith.Player.Player> _dirty = new();
    private readonly List<AbsoluteActorPose> _posesForPeer = new();

    public MovementSystem(PlayerManager players) => _players = players;

    public static float VoidRescueY => Blocks.FlatMinY - VoidRescueMargin;

    public void Tick(GameClock clock)
    {
        _ = clock;
        if (_players.Count == 0) return;

        var online = _players.Online;
        _dirty.Clear();

        foreach (var player in online)
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

            if (!IsPoseDirty(player))
                continue;

            _dirty.Add(player);
        }

        if (_dirty.Count == 0) return;

        foreach (var peer in online)
        {
            if (!peer.IsInGame) continue;

            _posesForPeer.Clear();
            foreach (var mover in _dirty)
            {
                if (ReferenceEquals(mover, peer)) continue;
                _posesForPeer.Add(new AbsoluteActorPose
                {
                    ActorRuntimeId = (ulong)mover.RuntimeId,
                    X = mover.PositionX,
                    Y = mover.PositionY,
                    Z = mover.PositionZ,
                    Pitch = mover.Pitch,
                    Yaw = mover.Yaw,
                    HeadYaw = mover.HeadYaw
                });
            }

            peer.Session.Protocol.Entity.SendMoveAbsolutes(_posesForPeer);
        }

        foreach (var mover in _dirty)
            RememberReplicatedPose(mover);
    }

    private static bool IsPoseDirty(global::Zenith.Player.Player player) =>
        player.PositionX != player.LastReplicatedX ||
        player.PositionY != player.LastReplicatedY ||
        player.PositionZ != player.LastReplicatedZ ||
        player.Pitch != player.LastReplicatedPitch ||
        player.Yaw != player.LastReplicatedYaw ||
        player.HeadYaw != player.LastReplicatedHeadYaw;

    private static void RememberReplicatedPose(global::Zenith.Player.Player player)
    {
        player.LastReplicatedX = player.PositionX;
        player.LastReplicatedY = player.PositionY;
        player.LastReplicatedZ = player.PositionZ;
        player.LastReplicatedPitch = player.Pitch;
        player.LastReplicatedYaw = player.Yaw;
        player.LastReplicatedHeadYaw = player.HeadYaw;
    }

    private static void SoftRescueFromVoid(global::Zenith.Player.Player player)
    {
        player.PositionY = Blocks.FlatSpawnY;
        player.Pitch = 0;
        player.Session.Protocol.Entity.SendMovePlayerTeleport(
            entityRuntimeId: (ulong)player.RuntimeId,
            x: player.PositionX,
            y: player.PositionY,
            z: player.PositionZ,
            pitch: player.Pitch,
            yaw: player.Yaw,
            headYaw: player.HeadYaw);
    }
}
