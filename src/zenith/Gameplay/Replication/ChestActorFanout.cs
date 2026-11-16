using Zenith.Gameplay.Ports;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Replication;

/// <summary>
/// Tells clients about chest tiles, which is what makes two adjacent chests draw as one double chest. Sent when a chest is placed
/// or its partner changes, to every in-game player who knows the column; columns streamed later carry theirs via
/// <see cref="Describe"/>.
/// </summary>
static class ChestActorFanout
{
    /// <summary>Publishes the chest at the cell and, if it has one, its partner (whose link just changed too).</summary>
    public static void PublishWithPartner(IReadOnlyList<Player.Player> online, World.World world, int x, int y, int z)
    {
        Publish(online, world, x, y, z);
        if (ChestPairing.TryGetLink(world, x, y, z, out var partnerX, out var partnerZ, out _))
            Publish(online, world, partnerX, y, partnerZ);
    }

    /// <summary>Publishes the chest at one cell, if there still is one.</summary>
    public static void Publish(IReadOnlyList<Player.Player> online, World.World world, int x, int y, int z)
    {
        if (!Blocks.IsChest(world.GetBlock(x, y, z))) return;

        var chunkX = PlayerChunkTracker.BlockToChunk(x);
        var chunkZ = PlayerChunkTracker.BlockToChunk(z);
        foreach (var peer in online)
        {
            if (peer.IsInGame && peer.Chunks.Knows(chunkX, chunkZ))
                Describe(peer.Client.World, world, x, y, z);
        }
    }

    public static void Describe(IWorldChannel channel, World.World world, int x, int y, int z) =>
        channel.SendChestActor(
            x, y, z,
            ChestPairing.TryGetLink(world, x, y, z, out var partnerX, out var partnerZ, out var lead)
                ? new ChestLink(partnerX, partnerZ, lead)
                : null);
}
