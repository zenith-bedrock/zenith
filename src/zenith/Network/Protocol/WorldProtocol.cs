using Zenith.Network.Packets;
using Zenith.Network.Session;

namespace Zenith.Network.Protocol;

/// <summary>
/// Coluna de chunk já decidida pelo gameplay/handler — Protocol só empacota e transmite.
/// </summary>
readonly record struct ChunkColumn(int X, int Z, int DimensionId, int SubChunkCount, byte[] ExtraPayload);

/// <summary>Transmite intenções de mundo/spawn/chunks. Não seleciona visibilidade nem grade.</summary>
sealed class WorldProtocol
{
    private readonly NetworkSession _session;

    public WorldProtocol(NetworkSession session) => _session = session;

    public void SendStartGame(
        string levelName,
        long entityRuntimeId,
        float x,
        float y,
        float z,
        float pitch,
        float yaw,
        int spawnBlockX = 0,
        int spawnBlockY = 0,
        int spawnBlockZ = 0,
        bool useBlockNetworkIdHashes = true,
        int gameMode = 0)
    {
        _session.SendDataPacket(new StartGamePacket
        {
            LevelName = levelName,
            EntityId = entityRuntimeId,
            PositionX = x,
            PositionY = y,
            PositionZ = z,
            Pitch = pitch,
            Yaw = yaw,
            SpawnBlockX = spawnBlockX,
            SpawnBlockY = spawnBlockY,
            SpawnBlockZ = spawnBlockZ,
            UseBlockNetworkIdHashes = useBlockNetworkIdHashes,
            GameMode = gameMode,
            GameType = gameMode
        });
    }

    public void SendChunkRadiusUpdated(int radius)
    {
        _session.SendDataPacket(new ChunkRadiusUpdatedPacket { Radius = radius });
    }

    /// <summary>Empty BiomeDefinitionList (0x7a) — Vedrock sends after ItemRegistry.</summary>
    public void SendEmptyBiomeDefinitionList()
    {
        _session.SendDataPacket(new BiomeDefinitionListPacket());
    }

    /// <summary>Vedrock flush cadence: several LevelChunks per GamePacket batch.</summary>
    public const int LevelChunkBatchSize = 4;

    public void PublishChunks(IReadOnlyList<ChunkColumn> columns)
    {
        if (columns.Count == 0) return;

        var packets = new DataPacket[columns.Count];
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            packets[i] = new LevelChunkPacket
            {
                ChunkX = column.X,
                ChunkZ = column.Z,
                DimensionId = column.DimensionId,
                SubChunkCount = column.SubChunkCount,
                ExtraPayload = column.ExtraPayload
            };
        }

        _session.SendDataPacket(packets);
    }

    /// <summary>Uma coluna LevelChunk (base). Overlays vêm depois via <see cref="SendUpdateBlock"/>.</summary>
    public void SendLevelChunk(ChunkColumn column)
    {
        _session.SendDataPacket(new LevelChunkPacket
        {
            ChunkX = column.X,
            ChunkZ = column.Z,
            DimensionId = column.DimensionId,
            SubChunkCount = column.SubChunkCount,
            ExtraPayload = column.ExtraPayload
        });
    }

    public void SendChunkPublisher(int blockX, int blockY, int blockZ, int radiusBlocks)
    {
        _session.SendDataPacket(new NetworkChunkPublisherUpdatePacket
        {
            BlockX = blockX,
            BlockY = blockY,
            BlockZ = blockZ,
            Radius = radiusBlocks
        });
    }

    public void SendSpawnPosition(int spawnType, int x, int y, int z)
    {
        _session.SendDataPacket(new SetSpawnPositionPacket
        {
            SpawnType = spawnType,
            X = x,
            Y = y,
            Z = z
        });
    }

    public void SendWorldSpawnPosition(int x, int y, int z) =>
        SendSpawnPosition(SetSpawnPositionPacket.TYPE_WORLD_SPAWN, x, y, z);

    public void SendSpawnComplete()
    {
        _session.SendDataPacket(new PlayStatusPacket { Status = 3 }); // PLAYER_SPAWN
    }

    public void SendTime(int worldTime)
    {
        _session.SendDataPacket(new SetTimePacket { Time = worldTime });
    }

    public void SendUpdateBlock(int x, int y, int z, int blockRuntimeId, int flags = UpdateBlockPacket.FlagNetwork, int dataLayerId = 0)
    {
        _session.SendDataPacket(new UpdateBlockPacket
        {
            X = x,
            Y = y,
            Z = z,
            BlockRuntimeId = blockRuntimeId,
            Flags = flags,
            DataLayerId = dataLayerId
        });
    }

    public void SendLevelEvent(int eventType, float x, float y, float z, int eventData = 0)
    {
        _session.SendDataPacket(new LevelEventPacket
        {
            EventType = eventType,
            X = x,
            Y = y,
            Z = z,
            EventData = eventData
        });
    }

    public void SendBlockStartCrack(int blockX, int blockY, int blockZ, int breakTicks)
    {
        var data = breakTicks <= 0 ? 65535 : Math.Max(1, 65535 / breakTicks);
        SendLevelEvent(
            LevelEventPacket.EventStartBlockCracking,
            blockX + 0.5f,
            blockY + 0.5f,
            blockZ + 0.5f,
            data);
    }

    public void SendBlockStopCrack(int blockX, int blockY, int blockZ) =>
        SendLevelEvent(
            LevelEventPacket.EventStopBlockCracking,
            blockX + 0.5f,
            blockY + 0.5f,
            blockZ + 0.5f);
}
