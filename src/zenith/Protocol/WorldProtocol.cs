using Zenith.Packets;
using Zenith.Server;
using Zenith.Session;
using Zenith.World;

namespace Zenith.Protocol;

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
        int gameMode = AbilityBits.WireGameModeSurvival)
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
            GameType = gameMode,
            BaseGameVersion = ServerIdentity.VersionName,
            GameVersion = ServerIdentity.VersionName
        });
    }

    public void SendChunkRadiusUpdated(int radius)
    {
        _session.SendDataPacket(new ChunkRadiusUpdatedPacket { Radius = radius });
    }

    /// <summary>Empty BiomeDefinitionList (0x7a) — clients expect this once after ItemRegistry.</summary>
    public void SendEmptyBiomeDefinitionList()
    {
        _session.SendDataPacket(new BiomeDefinitionListPacket());
    }

    /// <summary>How many LevelChunks to pack into one GamePacket envelope on spawn stream.</summary>
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
        _session.SendDataPacket(new PlayStatusPacket { Status = PlayStatusPacket.PlayerSpawn }); // PLAYER_SPAWN
    }

    public void SendTime(int worldTime)
    {
        _session.SendDataPacket(new SetTimePacket { Time = worldTime });
    }

    public void SendUpdateBlock(int x, int y, int z, int blockRuntimeId, int flags = UpdateBlockPacket.FlagNeighborsAndNetwork, int dataLayerId = 0)
    {
        _session.SendDataPacket(CreateUpdateBlock(x, y, z, blockRuntimeId, flags, dataLayerId));
    }

    /// <summary>One GamePacket envelope for N UpdateBlocks (ADR §44).</summary>
    public void PublishUpdateBlocks(IReadOnlyList<(int X, int Y, int Z, int BlockRuntimeId)> updates)
    {
        if (updates.Count == 0) return;
        if (updates.Count == 1)
        {
            var u = updates[0];
            SendUpdateBlock(u.X, u.Y, u.Z, u.BlockRuntimeId);
            return;
        }

        var packets = new DataPacket[updates.Count];
        for (var i = 0; i < updates.Count; i++)
        {
            var u = updates[i];
            packets[i] = CreateUpdateBlock(u.X, u.Y, u.Z, u.BlockRuntimeId);
        }

        _session.SendDataPacket(packets);
    }

    private static UpdateBlockPacket CreateUpdateBlock(
        int x, int y, int z, int blockRuntimeId,
        int flags = UpdateBlockPacket.FlagNeighborsAndNetwork,
        int dataLayerId = 0) =>
        new()
        {
            X = x,
            Y = y,
            Z = z,
            BlockRuntimeId = blockRuntimeId,
            Flags = flags,
            DataLayerId = dataLayerId
        };

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

    public void SendBlockEvent(int x, int y, int z, int eventType, int eventData)
    {
        _session.SendDataPacket(new BlockEventPacket
        {
            X = x,
            Y = y,
            Z = z,
            EventType = eventType,
            EventData = eventData
        });
    }

    public void SendChestLidOpen(int blockX, int blockY, int blockZ) =>
        SendBlockEvent(
            blockX, blockY, blockZ,
            BlockEventPacket.EventChangeChestState,
            BlockEventPacket.ChestStateOpen);

    public void SendChestLidClose(int blockX, int blockY, int blockZ) =>
        SendBlockEvent(
            blockX, blockY, blockZ,
            BlockEventPacket.EventChangeChestState,
            BlockEventPacket.ChestStateClosed);

    public void SendBlockStartCrack(int blockX, int blockY, int blockZ, int breakTicks)
    {
        // START/STOP crack LevelEvents use integer block coordinates (not float entity pos).
        SendLevelEvent(
            LevelEventPacket.EventStartBlockCracking,
            blockX,
            blockY,
            blockZ,
            Blocks.CrackEventData(breakTicks));
    }

    public void SendBlockStopCrack(int blockX, int blockY, int blockZ) =>
        SendLevelEvent(
            LevelEventPacket.EventStopBlockCracking,
            blockX,
            blockY,
            blockZ);

    /// <summary>LevelEvent UpdateBlockCracking (3602) — rate change only.</summary>
    public void SendBlockBreakSpeed(int blockX, int blockY, int blockZ, int breakTicks)
    {
        SendLevelEvent(
            LevelEventPacket.EventBlockBreakSpeed,
            blockX,
            blockY,
            blockZ,
            Blocks.CrackEventData(breakTicks));
    }

    public void SendPlaySound(string soundName, float x, float y, float z, float volume = 1f, float pitch = 1f) =>
        _session.SendDataPacket(new PlaySoundPacket
        {
            SoundName = soundName,
            PositionX = x,
            PositionY = y,
            PositionZ = z,
            Volume = volume,
            Pitch = pitch
        });

    public void SendStopSound(string soundName = "", bool stopAll = false, bool stopMusic = false) =>
        _session.SendDataPacket(new StopSoundPacket
        {
            SoundName = soundName,
            StopAllSounds = stopAll,
            StopMusic = stopMusic
        });
}
