using Zenith.Gameplay.Replication;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.WorldInteraction;

/// <summary>What a block that is destroyed by the world rather than a player (unsupported, washed away) leaves on the floor.</summary>
static class BlockSpill
{
    /// <summary>A block's drop leaves the cell with a little hop: up at 0.2 and aside by up to 0.1 (vanilla's item spawn).</summary>
    public const float PopUp = 0.2f;

    public static float PopSideways(Random random) => (random.NextSingle() * 2f - 1f) * 0.1f;

    public static void Drop(
        World.World world, PlayerManager players, IReadOnlyList<Player.Player> online, int x, int y, int z, int blockRuntimeId,
        Random? random = null)
    {
        if (!world.Rules.DoTileDrops || !BlockShapes.YieldsDrop(blockRuntimeId)) return;
        var drops = new List<BlockDrop>(2);
        BlockLoot.Roll(blockRuntimeId, random ?? Random.Shared, drops);
        if (drops.Count > 0)
            _ = FloorDropFanout.TryDepositBatch(
                world, players, online, x, y, z, [.. drops.Select(drop => new FloorDropFanout.DepositRequest(drop.Id, drop.Count))],
                velocityX: PopSideways(random ?? Random.Shared), velocityY: PopUp, velocityZ: PopSideways(random ?? Random.Shared));
    }
}
