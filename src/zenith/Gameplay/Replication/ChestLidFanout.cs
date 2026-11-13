using Zenith.Gameplay.Ports;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Replication;

/// <summary>
/// Fan-out chest lid BlockEvents (ADR §28 / §56). Protocol stays session-scoped;
/// recipients = subject + InGame peers who <see cref="PlayerChunkTracker.Knows"/> the column.
/// Double-chest fans both cells.
/// </summary>
static class ChestLidFanout
{
    public static void Open(
        IReadOnlyList<Player.Player> online,
        IClientChannel subject,
        int blockX,
        int blockY,
        int blockZ)
    {
        subject.World.SendChestLidOpen(blockX, blockY, blockZ);
        FanPeers(online, subject, blockX, blockY, blockZ, open: true);
    }

    public static void Close(
        IReadOnlyList<Player.Player> online,
        IClientChannel subject,
        int blockX,
        int blockY,
        int blockZ)
    {
        subject.World.SendChestLidClose(blockX, blockY, blockZ);
        FanPeers(online, subject, blockX, blockY, blockZ, open: false);
    }

    /// <summary>
    /// Drop opener ref for <paramref name="player"/> if they have a chest UI open.
    /// Fans close when last viewer leaves. Clears <see cref="Player.Player.Windows.OpenChest"/>.
    /// </summary>
    public static void ReleaseOpener(
        IReadOnlyList<Player.Player> online,
        World.World world,
        Player.Player player)
    {
        if (player.Windows.OpenChest is not { } view) return;
        _ = player.Windows.TryClearOpenContainer(out _);

        var primaryClosed = world.Chests.TryRemoveOpener(
            view.PrimaryX, view.PrimaryY, view.PrimaryZ, player.RuntimeId);
        var partnerClosed = false;
        if (view.TryGetPartner(out var px, out var py, out var pz))
            partnerClosed = world.Chests.TryRemoveOpener(px, py, pz, player.RuntimeId);

        if (primaryClosed)
            Close(online, player.Client, view.PrimaryX, view.PrimaryY, view.PrimaryZ);
        if (partnerClosed)
            Close(online, player.Client, px, py, pz);
    }

    private static void FanPeers(
        IReadOnlyList<Player.Player> online,
        IClientChannel subject,
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
            if (ReferenceEquals(peer.Client, subject) || !peer.IsInGame) continue;
            if (!peer.Chunks.Knows(cx, cz)) continue;

            if (open)
                peer.Client.World.SendChestLidOpen(blockX, blockY, blockZ);
            else
                peer.Client.World.SendChestLidClose(blockX, blockY, blockZ);
        }
    }
}
