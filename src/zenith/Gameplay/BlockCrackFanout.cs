using Zenith.Network.Session;
using Zenith.Player;

namespace Zenith.Gameplay;

/// <summary>
/// Fan-out dig crack LevelEvents (ADR §42). Protocol stays session-scoped;
/// callers decide recipients — same pattern as EquipmentSystem / BlockSystem UpdateBlock.
/// </summary>
static class BlockCrackFanout
{
    public static void Start(
        PlayerManager players,
        NetworkSession miner,
        int blockX,
        int blockY,
        int blockZ,
        int breakTicks)
    {
        miner.Protocol.World.SendBlockStartCrack(blockX, blockY, blockZ, breakTicks);
        if (players.Count < 2) return;

        foreach (var peer in players.Online)
        {
            if (ReferenceEquals(peer.Session, miner) || !peer.IsInGame) continue;
            peer.Session.Protocol.World.SendBlockStartCrack(blockX, blockY, blockZ, breakTicks);
        }
    }

    public static void Stop(
        PlayerManager players,
        NetworkSession miner,
        int blockX,
        int blockY,
        int blockZ)
    {
        miner.Protocol.World.SendBlockStopCrack(blockX, blockY, blockZ);
        if (players.Count < 2) return;

        foreach (var peer in players.Online)
        {
            if (ReferenceEquals(peer.Session, miner) || !peer.IsInGame) continue;
            peer.Session.Protocol.World.SendBlockStopCrack(blockX, blockY, blockZ);
        }
    }
}
