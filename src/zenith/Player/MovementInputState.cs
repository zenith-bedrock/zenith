using Zenith.World;

namespace Zenith.Player;

/// <summary>
/// Intenção de movimento escrita pela thread de rede; consumida pelo MovementSystem no tick.
/// Pose modes (sneak/sprint) + one-shot MissedSwing ride the same AuthInput channel (§53).
/// </summary>
struct MovementInputState
{
    public bool HasValue;
    public float X;
    public float Y;
    public float Z;
    public float Pitch;
    public float Yaw;

    /// <summary>Continuous AuthInput sneak level (bit 8).</summary>
    public bool Sneaking;

    public bool SprintStart;
    public bool SprintStop;

    /// <summary>One-shot: fan SwingArm to peers this tick (§53).</summary>
    public bool MissedSwing;

    /// <summary>AuthInput VerticalCollision — peer Absolute FLAG_ON_GROUND.</summary>
    public bool OnGround;

    /// <summary>Domain pose — <paramref name="y"/> is feet.</summary>
    public static MovementInputState From(float x, float y, float z, float pitch, float yaw) => new()
    {
        HasValue = true,
        X = x,
        Y = y,
        Z = z,
        Pitch = pitch,
        Yaw = yaw,
        OnGround = true
    };

    /// <summary>
    /// Wire AuthInput / StartGame eye-space Y → domain feet (ADR §26).
    /// </summary>
    public static MovementInputState FromClientAuthInput(
        float x,
        float eyeY,
        float z,
        float pitch,
        float yaw,
        bool sneaking = false,
        bool sprintStart = false,
        bool sprintStop = false,
        bool missedSwing = false,
        bool onGround = true)
    {
        var state = From(x, eyeY - Blocks.PlayerEyeHeight, z, pitch, yaw);
        state.Sneaking = sneaking;
        state.SprintStart = sprintStart;
        state.SprintStop = sprintStop;
        state.MissedSwing = missedSwing;
        state.OnGround = onGround;
        return state;
    }

    public bool IsSecure() =>
        IsFinite(X) && IsFinite(Y) && IsFinite(Z) && IsFinite(Pitch) && IsFinite(Yaw);

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
