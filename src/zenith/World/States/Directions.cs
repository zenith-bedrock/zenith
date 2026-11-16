namespace Zenith.World;

/// <summary>Horizontal compass direction, ordered clockwise from north so rotating is arithmetic.</summary>
enum Cardinal : byte { North, East, South, West }

/// <summary>A block face as the Bedrock protocol numbers it (UseItem <c>face</c>).</summary>
enum Face : byte { Down, Up, North, South, West, East }

static class Directions
{
    // Yaw 0 looks south, 90 west, 180 north, 270 east (Bedrock); indexed by the 90° bucket of yaw + 45.
    private static readonly Cardinal[] LookByBucket = [Cardinal.South, Cardinal.West, Cardinal.North, Cardinal.East];

    /// <summary>The horizontal direction a player with this yaw is looking at. Non-finite yaw reads as south.</summary>
    public static Cardinal LookFromYaw(float yawDegrees) =>
        float.IsFinite(yawDegrees) ? LookByBucket[(int)MathF.Floor((yawDegrees + 45f) / 90f) & 3] : Cardinal.South;

    public static Cardinal Opposite(this Cardinal c) => (Cardinal)(((int)c + 2) & 3);

    public static Cardinal Clockwise(this Cardinal c) => (Cardinal)(((int)c + 1) & 3);

    public static int DeltaX(this Cardinal c) => c switch { Cardinal.East => 1, Cardinal.West => -1, _ => 0 };

    public static int DeltaZ(this Cardinal c) => c switch { Cardinal.South => 1, Cardinal.North => -1, _ => 0 };

    /// <summary>Wire face byte → <see cref="Face"/>; unknown values read as a side face (east), like the old placement code.</summary>
    public static Face ToFace(byte wire) => wire <= (byte)Face.East ? (Face)wire : Face.East;
}
