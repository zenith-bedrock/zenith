using Zenith.Session;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay;

/// <summary>
/// Fan-out chest lid BlockEvents (ADR §28 adendo). Protocol stays session-scoped;
/// recipients = subject + InGame peers who <see cref="PlayerChunkTracker.Knows"/> the column.
/// </summary>
static class ChestLidFanout
{
    public static void Open(
        IReadOnlyList<Player.Player> online,
        NetworkSession subject,
        int blockX,
        int blockY,
        int blockZ)
    {
        subject.Protocol.World.SendChestLidOpen(blockX, blockY, blockZ);
        FanPeers(online, subject, blockX, blockY, blockZ, open: true);
    }

    public static void Close(
        IReadOnlyList<Player.Player> online,
        NetworkSession subject,
        int blockX,
        int blockY,
        int blockZ)
    {
        subject.Protocol.World.SendChestLidClose(blockX, blockY, blockZ);
        FanPeers(online, subject, blockX, blockY, blockZ, open: false);
    }

    /// <summary>
    /// Drop opener ref for <paramref name="player"/> if they have a chest UI open.
    /// Fans close when last viewer leaves. Clears <see cref="Player.Player.OpenChest"/>.
    /// </summary>
    public static void ReleaseOpener(
        IReadOnlyList<Player.Player> online,
        World.World world,
        Player.Player player)
    {
        if (player.OpenChest is not { } pos) return;
        player.OpenChest = null;
        if (!world.Chests.TryRemoveOpener(pos.X, pos.Y, pos.Z, player.RuntimeId))
            return;
        Close(online, player.Session, pos.X, pos.Y, pos.Z);
    }

    private static void FanPeers(
        IReadOnlyList<Player.Player> online,
        NetworkSession subject,
        int blockX,
        int blockY,
        int blockZ,
        bool open)
    {
        if (online.Count < 2) return;

        var cx = PlayerChunkTracker.BlockToChunk(blockX);
        var cz = PlayerChunkTracker.BlockToChunk(blockZ);

        foreach (var peer in online)
        {
            if (ReferenceEquals(peer.Session, subject) || !peer.IsInGame) continue;
            if (!peer.Chunks.Knows(cx, cz)) continue;

            if (open)
                peer.Session.Protocol.World.SendChestLidOpen(blockX, blockY, blockZ);
            else
                peer.Session.Protocol.World.SendChestLidClose(blockX, blockY, blockZ);
        }
    }
}
