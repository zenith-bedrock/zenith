using Zenith.Gameplay.WorldInteraction;
using Zenith.Gameplay.Replication;
using Zenith.Player;
using Zenith.Session;
using Zenith.World;

namespace Zenith.Protocol;

/// <summary>
/// Sends a column to a client: one LevelChunk serialised from the live <see cref="WorldColumn"/> (ADR §167), so every edit
/// made so far is already in it. Shared by PreSpawn and ChunkStreamSystem.
/// </summary>
static class ColumnSend
{
    public static void EmitToSession(NetworkSession session, WorldColumn column, byte orderChannel)
    {
        var wire = column.GetWire();
        session.Protocol.World.SendLevelChunk(new ChunkColumn(
            column.Coord.X,
            column.Coord.Z,
            column.DimensionId,
            wire.SubChunkCount,
            wire.Payload), orderChannel);
        session.RecordColumnSent(column.Coord, wire.Version);

        // Item catch-up follows the worker's publication acknowledgement on the tick (ADR §163).
    }

    /// <summary>
    /// Join catch-up (§14): a client that finished loading may hold a column from before an edit that was broadcast while it
    /// was not yet in game. Re-sends the known columns whose edit version moved on, and the floor drops of every known one.
    /// </summary>
    public static void ResyncColumns(
        NetworkSession session,
        World.World world,
        IReadOnlyList<(int X, int Z)> columns)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            var (cx, cz) = columns[i];
            if (world.TryGetLoadedColumn(cx, cz, out var column) &&
                !session.HasColumnVersion(column.Coord, column.EditVersion))
                EmitToSession(session, column, NetworkSession.WorldStreamOrderChannel);

            EmitFloorDropsInColumn(session, world, cx, cz);
            EmitExperienceOrbsInColumn(session, world, cx, cz);
            EmitChestActorsInColumn(session, world, cx, cz);
        }
    }

    /// <summary>The chest tiles of one column, so a double chest streamed in later still draws as one.</summary>
    public static void EmitChestActorsInColumn(NetworkSession session, World.World world, int chunkX, int chunkZ)
    {
        if (!world.TryGetLoadedColumn(chunkX, chunkZ, out var column)) return;
        var originX = ChunkMath.ChunkOrigin(chunkX);
        var originZ = ChunkMath.ChunkOrigin(chunkZ);
        var channel = session.Protocol.World;
        column.ForEachEntity((lx, y, lz, entity) =>
        {
            if (entity is ContainerEntity)
                ChestActorFanout.Describe(channel, world, originX + lx, y, originZ + lz);
        });
    }

    /// <summary>The experience orbs lying in one column, for a client that arrives or catches up after they spawned.</summary>
    public static void EmitExperienceOrbsInColumn(NetworkSession session, World.World world, int chunkX, int chunkZ)
    {
        foreach (var orb in world.ExperienceOrbs.Active)
        {
            if (PlayerChunkTracker.BlockToChunk(orb.Pose.X) != chunkX || PlayerChunkTracker.BlockToChunk(orb.Pose.Z) != chunkZ) continue;
            ExperienceOrbSystem.Describe(session.Protocol.Entity, orb);
        }
    }

    /// <summary>AddItemActor for floor drops in one column (late join / stream catch-up, §26 wire).</summary>
    public static void EmitFloorDropsInColumn(NetworkSession session, World.World world, int chunkX, int chunkZ)
    {
        foreach (var drop in world.FloorDrops.SnapshotViews())
        {
            if (PlayerChunkTracker.BlockToChunk(drop.Pose.X) != chunkX ||
                PlayerChunkTracker.BlockToChunk(drop.Pose.Z) != chunkZ)
                continue;
            if (drop.EntityRuntimeId == 0) continue;

            var item = session.Protocol.Inventory.DescribeStack(drop.Id, drop.Count);
            if (item.NetworkId == 0) continue; // invalid/air — do not spawn AddItemActor
            session.Protocol.Entity.SendAddItemActor(
                drop.EntityRuntimeId,
                item,
                drop.Pose.X,
                drop.Pose.Y + EntityHitboxes.ItemNetworkOffset,
                drop.Pose.Z,
                drop.Pose.VelocityX, drop.Pose.VelocityY, drop.Pose.VelocityZ);
        }
    }
}
