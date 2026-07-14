using Zenith.Raknet.Stream;

namespace Zenith.Network.Packets;

class StartGamePacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.START_GAME_PACKET;

    public string LevelName;
    public long EntityId = 0;
    public int GameMode = 0;
    public float PositionX;
    public float PositionY = -60f; // flat spawn; valor real vem de SendStartGame(player.PositionY)
    public float PositionZ;
    public float Pitch;
    public float Yaw;
    public long Seed = 0;
    public short BiomeType = 0;
    public string BiomeName = "plains";
    public int Dimension = 0;
    public int Generator = 1;
    public int GameType = 0;
    public int Difficulty = 0;
    public int SpawnBlockX = 0;
    public int SpawnBlockY = 0;
    public int SpawnBlockZ = 0;
    public int EditorType = 0;
    public int StopTime = 0;

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        
        // Entity / Player settings
        writer.WriteVarLong(EntityId);
        writer.WriteUnsignedVarLong(EntityId); // EntityRuntimeID
        writer.WriteVarInt(GameMode); // PlayerGameMode
        writer.WriteFloat(PositionX, BinaryStream.Endianess.Little); // PlayerPosition x
        writer.WriteFloat(PositionY, BinaryStream.Endianess.Little); // PlayerPosition y
        writer.WriteFloat(PositionZ, BinaryStream.Endianess.Little); // PlayerPosition z
        writer.WriteFloat(Pitch, BinaryStream.Endianess.Little); // Pitch
        writer.WriteFloat(Yaw, BinaryStream.Endianess.Little); // Yaw
        
        // Level settings
        writer.WriteLong(Seed, BinaryStream.Endianess.Little); // WorldSeed
        writer.WriteShort(BiomeType, BinaryStream.Endianess.Little); // SpawnBiomeType
        writer.WriteVarString(BiomeName); // UserDefinedBiomeName
        writer.WriteVarInt(Dimension); // Dimension
        writer.WriteVarInt(Generator); // Generator
        writer.WriteVarInt(GameType); // WorldGameMode
        writer.WriteBool(false); // Hardcore
        writer.WriteVarInt(Difficulty); // Difficulty
        
        // WorldSpawn (BlockPos)
        writer.WriteVarInt(SpawnBlockX);
        writer.WriteVarInt(SpawnBlockY);
        writer.WriteVarInt(SpawnBlockZ);
        
        writer.WriteBool(false); // AchievementsDisabled
        writer.WriteVarInt(EditorType); // EditorWorldType
        writer.WriteBool(false); // CreatedInEditor
        writer.WriteBool(false); // ExportedFromEditor
        writer.WriteVarInt(StopTime); // DayCycleLockTime
        writer.WriteVarInt(0); // EducationEditionOffer
        writer.WriteBool(false); // EducationFeaturesEnabled
        writer.WriteVarString(""); // EducationProductID
        writer.WriteFloat(0, BinaryStream.Endianess.Little); // RainLevel
        writer.WriteFloat(0, BinaryStream.Endianess.Little); // LightningLevel
        writer.WriteBool(true); // ConfirmedPlatformLockedContent
        writer.WriteBool(true); // MultiPlayerGame
        writer.WriteBool(true); // LANBroadcastEnabled
        writer.WriteVarInt(0); // XBLBroadcastMode
        writer.WriteVarInt(0); // PlatformBroadcastMode
        writer.WriteBool(true); // CommandsEnabled
        writer.WriteBool(false); // TexturePackRequired
        
        // GameRules slice (length 0)
        writer.WriteUnsignedVarInt(0);
        // Experiments slice (length 0)
        writer.WriteUInt(0, BinaryStream.Endianess.Little); // SliceUint32Length
        writer.WriteBool(false); // ExperimentsPreviouslyToggled
        writer.WriteBool(false); // BonusChestEnabled
        writer.WriteBool(false); // StartWithMapEnabled
        writer.WriteVarInt(2); // PlayerPermissions (Operator)
        writer.WriteInt(4, BinaryStream.Endianess.Little); // ServerChunkTickRadius
        writer.WriteBool(false); // HasLockedBehaviourPack
        writer.WriteBool(false); // HasLockedTexturePack
        writer.WriteBool(false); // FromLockedWorldTemplate
        writer.WriteBool(true); // MSAGamerTagsOnly
        writer.WriteBool(false); // FromWorldTemplate
        writer.WriteBool(false); // WorldTemplateSettingsLocked
        writer.WriteBool(false); // OnlySpawnV1Villagers
        writer.WriteBool(false); // PersonaDisabled
        writer.WriteBool(false); // CustomSkinsDisabled
        writer.WriteBool(false); // EmoteChatMuted
        writer.WriteVarString("1.26.33"); // BaseGameVersion
        writer.WriteInt(0, BinaryStream.Endianess.Little); // LimitedWorldWidth
        writer.WriteInt(0, BinaryStream.Endianess.Little); // LimitedWorldDepth
        writer.WriteBool(false); // NewNether
        
        // EducationSharedResourceURI
        writer.WriteVarString(""); // ButtonName
        writer.WriteVarString(""); // LinkURI
        
        // ForceExperimentalGameplay Optional[bool]
        writer.WriteBool(false); // has value = false
        
        writer.WriteByte(0); // ChatRestrictionLevel
        writer.WriteBool(false); // DisablePlayerInteractions
        writer.WriteVarInt(0); // ServerEditorConnectionPolicy
        writer.WriteBool(false); // AllowAnonymousBlockDropsInEditorWorlds
        writer.WriteVarString(""); // LevelID
        writer.WriteVarString(LevelName); // WorldName
        writer.WriteVarString(""); // TemplateContentIdentity
        writer.WriteBool(false); // Trial
        
        // PlayerMovementSettings
        writer.WriteVarInt(0); // RewindHistorySize
        writer.WriteBool(false); // ServerAuthoritativeBlockBreaking
        
        writer.WriteLong(0, BinaryStream.Endianess.Little); // Time
        writer.WriteVarInt(0); // EnchantmentSeed
        
        // Blocks Slice
        writer.WriteUnsignedVarInt(0); // length 0
        
        writer.WriteVarString(""); // MultiPlayerCorrelationID
        writer.WriteBool(false); // ServerAuthoritativeInventory
        writer.WriteVarString("1.26.33"); // GameVersion
        
        // PropertyData (NBT compound)
        writer.WriteByte(0x0a); // Compound
        writer.WriteVarString(""); // Name
        writer.WriteByte(0x00); // End
        
        writer.WriteULong(0, BinaryStream.Endianess.Little); // ServerBlockStateChecksum
        writer.WriteLong(0, BinaryStream.Endianess.Little); // WorldTemplateID pt 1
        writer.WriteLong(0, BinaryStream.Endianess.Little); // WorldTemplateID pt 2
        
        writer.WriteBool(false); // ClientSideGeneration
        writer.WriteBool(false); // UseBlockNetworkIDHashes
        writer.WriteBool(false); // ServerAuthoritativeSound
        writer.WriteBool(false); // IsLoggingChat
        
        // ServerJoinInformation Optional
        writer.WriteBool(false); // has value = false
        
        writer.WriteVarString(""); // ServerID
        writer.WriteVarString(""); // ScenarioID
        writer.WriteVarString(""); // WorldID
        writer.WriteVarString(""); // OwnerID
        
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}