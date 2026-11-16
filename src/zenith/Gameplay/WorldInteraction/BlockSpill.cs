using Zenith.Gameplay.Replication;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.WorldInteraction;

/// <summary>What a block that is destroyed by the world rather than a player (unsupported, washed away) leaves on the floor.</summary>
static class BlockSpill
{
    public static void Drop(
        World.World world, PlayerManager players, IReadOnlyList<Player.Player> online, int x, int y, int z, int blockRuntimeId)
    {
        if (!world.Rules.DoTileDrops || !BlockShapes.YieldsDrop(blockRuntimeId)) return;
        var drop = BlockLoot.DropOf(blockRuntimeId);
        if (drop.Count > 0)
            _ = FloorDropFanout.TryDepositBatch(world, players, online, x, y, z, [new FloorDropFanout.DepositRequest(drop.Id, drop.Count)]);
    }
}
