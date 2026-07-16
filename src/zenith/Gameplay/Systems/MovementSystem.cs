using Zenith.Gameplay.Runtime;
using Zenith.Packets;
using Zenith.Protocol;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// Aplica <see cref="MovementInputState"/> no tick e replica pose aos peers quando dirty (ADR §44).
/// Void below threshold → death/Respawn handshake (ADR §40); soft-rescue retired.
/// </summary>
sealed class MovementSystem : IGameSystem
{
    /// <summary>Y below FlatMinY - margin triggers death (was soft-rescue before §40 adendo).</summary>
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
            if (player.IsDead)
            {
                // Drain stale AuthInput while on death screen; do not apply pose.
                _ = player.TryConsumeMovementInput(out _);
                if (player.TryConsumeRespawn())
                {
                    ApplyRespawn(player);
                    if (IsPoseDirty(player))
                        _dirty.Add(player);
                }
                continue;
            }

            if (!player.TryConsumeMovementInput(out var input)) continue;

            player.PositionX = input.X;
            player.PositionY = input.Y;
            player.PositionZ = input.Z;
            player.Pitch = input.Pitch;
            player.Yaw = input.Yaw;
            player.HeadYaw = input.Yaw;

            if (player.PositionY < VoidRescueY)
                BeginVoidDeath(player);

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

    /// <summary>Void fall → death screen (inventory kept). Respawn restores world spawn.</summary>
    private static void BeginVoidDeath(global::Zenith.Player.Player player)
    {
        if (!player.BeginDeath("generic")) return;

        var entity = player.Session.Protocol.Entity;
        var rid = (ulong)player.RuntimeId;
        var eyeX = player.PositionX;
        var eyeY = player.PositionY + Blocks.PlayerEyeHeight;
        var eyeZ = player.PositionZ;

        entity.SendDefaultAttributes(rid, player.Health, player.Hunger);
        entity.SendDeathInfo(player.DeathCause);
        entity.SendRespawn(eyeX, eyeY, eyeZ, RespawnPacket.StateSearchingForSpawn, rid);
    }

    private static void ApplyRespawn(global::Zenith.Player.Player player)
    {
        player.PositionX = 0f;
        player.PositionY = Blocks.FlatSpawnY;
        player.PositionZ = 0f;
        player.Pitch = 0f;
        player.CompleteRespawn();

        var entity = player.Session.Protocol.Entity;
        var rid = (ulong)player.RuntimeId;
        var eyeX = player.PositionX;
        var eyeY = player.PositionY + Blocks.PlayerEyeHeight;
        var eyeZ = player.PositionZ;

        entity.SendDefaultAttributes(rid, player.Health, player.Hunger);
        entity.SendMovePlayerTeleport(
            entityRuntimeId: rid,
            x: player.PositionX,
            y: player.PositionY,
            z: player.PositionZ,
            pitch: player.Pitch,
            yaw: player.Yaw,
            headYaw: player.HeadYaw);
        entity.SendRespawn(eyeX, eyeY, eyeZ, RespawnPacket.StateReadyToSpawn, rid);

        // Client clears bag UI on death — resync like join (SpawnResponse).
        player.Session.Protocol.Inventory.SendInventoryContent(player.Inventory);
        player.Session.Protocol.Inventory.SendUiInventoryContent(player);
    }
}
