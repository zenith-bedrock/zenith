using Zenith.World;

namespace Zenith.Player;

/// <summary>
/// Intenção de movimento escrita pela thread de rede; consumida pelo MovementSystem no tick.
/// Extensível depois (jump/sprint/sneak) sem misturar outros canais de input.
/// </summary>
struct MovementInputState
{
    public bool HasValue;
    public float X;
    public float Y;
    public float Z;
    public float Pitch;
    public float Yaw;

    /// <summary>Domain pose — <paramref name="y"/> is feet.</summary>
    public static MovementInputState From(float x, float y, float z, float pitch, float yaw) => new()
    {
        HasValue = true,
        X = x,
        Y = y,
        Z = z,
        Pitch = pitch,
        Yaw = yaw
    };

    /// <summary>
    /// Wire AuthInput / StartGame eye-space Y → domain feet (ADR §26).
    /// </summary>
    public static MovementInputState FromClientAuthInput(
        float x, float eyeY, float z, float pitch, float yaw) =>
        From(x, eyeY - Blocks.PlayerEyeHeight, z, pitch, yaw);

    public bool IsSecure() =>
        IsFinite(X) && IsFinite(Y) && IsFinite(Z) && IsFinite(Pitch) && IsFinite(Yaw);

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
