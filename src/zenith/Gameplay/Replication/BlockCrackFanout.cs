using Zenith.Gameplay.Ports;
using Zenith.Player;

namespace Zenith.Gameplay.Replication;

/// <summary>
/// Fan-out dig crack LevelEvents (ADR §42). Protocol stays session-scoped;
/// callers decide recipients — same pattern as EquipmentSystem / BlockEditSystem UpdateBlock.
/// </summary>
static class BlockCrackFanout
{
    public static void Start(
        IReadOnlyList<Player.Player> online,
        IClientChannel miner,
        int blockX,
        int blockY,
        int blockZ,
        int breakTicks)
    {
        miner.World.SendBlockStartCrack(blockX, blockY, blockZ, breakTicks);
        if (online.Count < 2) return;

        foreach (var peer in online)
        {
            if (ReferenceEquals(peer.Client, miner) || !peer.IsInGame) continue;
            peer.Client.World.SendBlockStartCrack(blockX, blockY, blockZ, breakTicks);
        }
    }

    public static void Stop(
        IReadOnlyList<Player.Player> online,
        IClientChannel miner,
        int blockX,
        int blockY,
        int blockZ)
    {
        miner.World.SendBlockStopCrack(blockX, blockY, blockZ);
        if (online.Count < 2) return;

        foreach (var peer in online)
        {
            if (ReferenceEquals(peer.Client, miner) || !peer.IsInGame) continue;
            peer.Client.World.SendBlockStopCrack(blockX, blockY, blockZ);
        }
    }

    /// <summary>LevelEvent 3602 — crack rate change only (ADR §27 / Mojang UpdateBlockCracking).</summary>
    public static void UpdateSpeed(
        IReadOnlyList<Player.Player> online,
        IClientChannel miner,
        int blockX,
        int blockY,
        int blockZ,
        int breakTicks)
    {
        miner.World.SendBlockBreakSpeed(blockX, blockY, blockZ, breakTicks);
        if (online.Count < 2) return;

        foreach (var peer in online)
        {
            if (ReferenceEquals(peer.Client, miner) || !peer.IsInGame) continue;
            peer.Client.World.SendBlockBreakSpeed(blockX, blockY, blockZ, breakTicks);
        }
    }
}
