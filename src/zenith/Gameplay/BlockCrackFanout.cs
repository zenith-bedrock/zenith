using Zenith.Session;
using Zenith.Player;

namespace Zenith.Gameplay;

/// <summary>
/// Fan-out dig crack LevelEvents (ADR §42). Protocol stays session-scoped;
/// callers decide recipients — same pattern as EquipmentSystem / BlockSystem UpdateBlock.
/// </summary>
static class BlockCrackFanout
{
    public static void Start(
        IReadOnlyList<Player.Player> online,
        NetworkSession miner,
        int blockX,
        int blockY,
        int blockZ,
        int breakTicks)
    {
        miner.Protocol.World.SendBlockStartCrack(blockX, blockY, blockZ, breakTicks);
        if (online.Count < 2) return;

        foreach (var peer in online)
        {
            if (ReferenceEquals(peer.Session, miner) || !peer.IsInGame) continue;
            peer.Session.Protocol.World.SendBlockStartCrack(blockX, blockY, blockZ, breakTicks);
        }
    }

    public static void Stop(
        IReadOnlyList<Player.Player> online,
        NetworkSession miner,
        int blockX,
        int blockY,
        int blockZ)
    {
        miner.Protocol.World.SendBlockStopCrack(blockX, blockY, blockZ);
        if (online.Count < 2) return;

        foreach (var peer in online)
        {
            if (ReferenceEquals(peer.Session, miner) || !peer.IsInGame) continue;
            peer.Session.Protocol.World.SendBlockStopCrack(blockX, blockY, blockZ);
        }
    }

    /// <summary>LevelEvent 3602 — crack rate change only (ADR §27 / Mojang UpdateBlockCracking).</summary>
    public static void UpdateSpeed(
        IReadOnlyList<Player.Player> online,
        NetworkSession miner,
        int blockX,
        int blockY,
        int blockZ,
        int breakTicks)
    {
        miner.Protocol.World.SendBlockBreakSpeed(blockX, blockY, blockZ, breakTicks);
        if (online.Count < 2) return;

        foreach (var peer in online)
        {
            if (ReferenceEquals(peer.Session, miner) || !peer.IsInGame) continue;
            peer.Session.Protocol.World.SendBlockBreakSpeed(blockX, blockY, blockZ, breakTicks);
        }
    }
}
