using System.Buffers.Binary;
using Zenith.Player;

namespace Zenith.World;

/// <summary>
/// Packed reconnect pose + GameMode for LevelDB <c>pd:{uuid}</c> (ADR §60).
/// v1: version + f32 x,y,z + f32 yaw,pitch + u8 gamemode.
/// v2 (Phase XI.4): v1 + i32 experience level + i32 experience points.
/// </summary>
static class PlayerDataBlob
{
    public const byte Version1 = 1;
    public const byte Version2 = 2;
    public const byte Version = Version2;
    public const int V1ByteLength = 1 + 5 * sizeof(float) + 1;
    public const int V2ByteLength = V1ByteLength + 2 * sizeof(int);

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
        int experienceLevel = 0, int experiencePoints = 0)
    {
        var bytes = new byte[V2ByteLength];
        bytes[0] = Version2;
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(1, 4), x);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(5, 4), y);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(9, 4), z);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(13, 4), yaw);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(17, 4), pitch);
        bytes[21] = (byte)mode;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22, 4), experienceLevel);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(26, 4), experiencePoints);
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
        out int experiencePoints)
    {
        x = y = z = yaw = pitch = 0;
        mode = GameMode.Survival;
        experienceLevel = 0;
        experiencePoints = 0;
        if (data.Length < 1 || data[0] is not (Version1 or Version2) || data.Length < V1ByteLength)
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

        // v1 blobs (pre-Phase XI.4) simply have no saved experience — level/points stay 0.
        if (data[0] == Version2 && data.Length >= V2ByteLength)
        {
            experienceLevel = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(22, 4));
            experiencePoints = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(26, 4));
            if (experienceLevel < 0 || experiencePoints < 0) return false;
        }

        return true;
    }
}
