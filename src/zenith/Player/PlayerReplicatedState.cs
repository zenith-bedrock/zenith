using Zenith.World;

namespace Zenith.Player;

/// <summary>
/// What peers were last told about this player, so each replication system can diff against it and send only changes
/// (EquipmentSystem for the held item, MovementSystem for pose and flags, the emote relay for its rate limit).
/// </summary>
sealed class PlayerReplicatedState
{
    /// <summary>Last held item replicated to peers (EquipmentSystem).</summary>
    public int HotbarSlot { get; set; } = -1;
    public StackId HeldStackId { get; set; }
    public int HeldCount { get; set; } = int.MinValue;

    /// <summary>Last pose sent to peers via MoveActorAbsolute (MovementSystem dirty-check).</summary>
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Pitch { get; set; }
    public float Yaw { get; set; }
    public float HeadYaw { get; set; }

    public bool Sneaking { get; set; }
    public bool Sprinting { get; set; }
    public bool OnGround { get; set; } = true;

    /// <summary>GameClock tick of the last relayed emote (rate limit §53).</summary>
    public ulong LastEmoteTick { get; set; }

    /// <summary>Forgets the pose flags so the next tick re-announces them (respawn).</summary>
    public void ForgetPoseFlags()
    {
        Sneaking = false;
        Sprinting = false;
    }
}
