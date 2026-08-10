using Zenith.Session;

namespace Zenith.Gameplay;

/// <summary>
/// Fan-out server-authored LevelSoundEvent (ADR §59). Recipients = subject + all InGame
/// peers (same pattern as dig crack — not Knows-gated).
/// </summary>
static class BlockSoundFanout
{
    // Sound names match PM LevelSoundEvent string ids (stable pre-Cereal shape, unaffected
    // by the §79 protocol bump) — kept here so Gameplay never references Packets DTOs.
    public const string SoundPlace = "place";
    public const string SoundBreak = "break";
    public const string SoundHit = "hit";

    public static void Place(
        IReadOnlyList<Player.Player> online,
        NetworkSession subject,
        int blockX,
        int blockY,
        int blockZ,
        int blockRuntimeId) =>
        Emit(online, subject, SoundPlace, blockX, blockY, blockZ, blockRuntimeId);

    public static void Break(
        IReadOnlyList<Player.Player> online,
        NetworkSession subject,
        int blockX,
        int blockY,
        int blockZ,
        int blockRuntimeId) =>
        Emit(online, subject, SoundBreak, blockX, blockY, blockZ, blockRuntimeId);

    public static void Hit(
        IReadOnlyList<Player.Player> online,
        NetworkSession subject,
        int blockX,
        int blockY,
        int blockZ,
        int blockRuntimeId) =>
        Emit(online, subject, SoundHit, blockX, blockY, blockZ, blockRuntimeId);

    private static void Emit(
        IReadOnlyList<Player.Player> online,
        NetworkSession subject,
        string sound,
        int blockX,
        int blockY,
        int blockZ,
        int blockRuntimeId)
    {
        var x = blockX + 0.5f;
        var y = blockY + 0.5f;
        var z = blockZ + 0.5f;

        subject.Protocol.World.SendLevelSoundEvent(sound, x, y, z, blockRuntimeId);

        foreach (var peer in online)
        {
            if (ReferenceEquals(peer.Session, subject) || !peer.IsInGame) continue;
            peer.Session.Protocol.World.SendLevelSoundEvent(sound, x, y, z, blockRuntimeId);
        }
    }
}
