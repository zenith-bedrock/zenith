using System.Buffers.Binary;
using Zenith.Player;

namespace Zenith.World;

/// <summary>
/// Packed reconnect pose + GameMode for LevelDB <c>pd:{uuid}</c> (ADR §60).
/// v1: version + f32 x,y,z + f32 yaw,pitch + u8 gamemode.
/// v2 (Phase XI.4): v1 + i32 experience level + i32 experience points.
/// v3 (Phase XXV): v2 + f32 health, hunger, saturation, exhaustion — closing the cross-reference
/// audit finding that only XP+pose survived a reconnect while health/hunger/effects silently reset
/// to defaults. Effects themselves are still not persisted (timed, expected to lapse naturally;
/// see the Phase XXV findings doc for why that's a smaller, deliberately deferred gap).
/// </summary>
static class PlayerDataBlob
{
    public const byte Version1 = 1;
    public const byte Version2 = 2;
    public const byte Version3 = 3;
    public const byte Version = Version3;
    public const int V1ByteLength = 1 + 5 * sizeof(float) + 1;
    public const int V2ByteLength = V1ByteLength + 2 * sizeof(int);
    public const int V3ByteLength = V2ByteLength + 4 * sizeof(float);

    /// <summary>Same horizontal/vertical bounds as <see cref="BlockEditIntent.IsInWorldBounds"/>.</summary>
    public static bool IsPoseInWorldBounds(float x, float y, float z) =>
        y is >= -64f and <= 320f &&
        x is > -30_000_000f and < 30_000_000f &&
        z is > -30_000_000f and < 30_000_000f;

    public static bool IsFinitePose(float x, float y, float z, float yaw, float pitch) =>
        float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(z) &&
        float.IsFinite(yaw) && float.IsFinite(pitch);

    public static byte[] Pack(
        float x, float y, float z, float yaw, float pitch, GameMode mode,
        int experienceLevel = 0, int experiencePoints = 0,
        float health = 20f, float hunger = 20f, float saturation = 5f, float exhaustion = 0f)
    {
        var bytes = new byte[V3ByteLength];
        bytes[0] = Version3;
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(1, 4), x);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(5, 4), y);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(9, 4), z);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(13, 4), yaw);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(17, 4), pitch);
        bytes[21] = (byte)mode;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22, 4), experienceLevel);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(26, 4), experiencePoints);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(30, 4), health);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(34, 4), hunger);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(38, 4), saturation);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(42, 4), exhaustion);
        return bytes;
    }

    public static bool TryUnpack(
        ReadOnlySpan<byte> data,
        out float x,
        out float y,
        out float z,
        out float yaw,
        out float pitch,
        out GameMode mode,
        out int experienceLevel,
        out int experiencePoints,
        out float health,
        out float hunger,
        out float saturation,
        out float exhaustion)
    {
        x = y = z = yaw = pitch = 0;
        mode = GameMode.Survival;
        experienceLevel = 0;
        experiencePoints = 0;
        health = 20f;
        hunger = 20f;
        saturation = 5f;
        exhaustion = 0f;
        if (data.Length < 1 || data[0] is not (Version1 or Version2 or Version3) || data.Length < V1ByteLength)
            return false;

        x = BinaryPrimitives.ReadSingleLittleEndian(data.Slice(1, 4));
        y = BinaryPrimitives.ReadSingleLittleEndian(data.Slice(5, 4));
        z = BinaryPrimitives.ReadSingleLittleEndian(data.Slice(9, 4));
        yaw = BinaryPrimitives.ReadSingleLittleEndian(data.Slice(13, 4));
        pitch = BinaryPrimitives.ReadSingleLittleEndian(data.Slice(17, 4));
        var rawMode = data[21];
        if (rawMode is not ((byte)GameMode.Survival or (byte)GameMode.Creative))
            return false;
        mode = (GameMode)rawMode;

        if (!IsFinitePose(x, y, z, yaw, pitch) || !IsPoseInWorldBounds(x, y, z))
            return false;

        // v1 blobs (pre-Phase XI.4) simply have no saved experience — level/points stay 0. Each
        // check is `data[0] >= VersionN`, not `==`, so a v3 blob still reads its v2 fields too —
        // the original v2-only check here (`== Version2`) would have silently skipped XP for any
        // later version, exactly the kind of stale-literal bug this project's own history warns
        // against (see ChunkPayloads.SubChunkVersion's doc comment for the same class of mistake).
        if (data[0] >= Version2 && data.Length >= V2ByteLength)
        {
            experienceLevel = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(22, 4));
            experiencePoints = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(26, 4));
            if (experienceLevel < 0 || experiencePoints < 0) return false;
        }

        // v1/v2 blobs have no saved vitals — health/hunger/saturation/exhaustion stay at their
        // full-health/full-food defaults (set above), matching what a fresh spawn already looks
        // like, rather than inventing a value that was never actually saved.
        if (data[0] >= Version3 && data.Length >= V3ByteLength)
        {
            health = BinaryPrimitives.ReadSingleLittleEndian(data.Slice(30, 4));
            hunger = BinaryPrimitives.ReadSingleLittleEndian(data.Slice(34, 4));
            saturation = BinaryPrimitives.ReadSingleLittleEndian(data.Slice(38, 4));
            exhaustion = BinaryPrimitives.ReadSingleLittleEndian(data.Slice(42, 4));
            if (!float.IsFinite(health) || health <= 0f || health > 1000f) return false;
            if (!float.IsFinite(hunger) || hunger < 0f || hunger > 20f) return false;
            if (!float.IsFinite(saturation) || saturation < 0f || saturation > 20f) return false;
            if (!float.IsFinite(exhaustion) || exhaustion < 0f || exhaustion > 40f) return false;
        }

        return true;
    }
}
