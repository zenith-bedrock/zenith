using Zenith.Nbt;
using Zenith.Raknet.Stream;

namespace Zenith.Packets;

class StartGamePacket : DataPacket
{
    public const int GeneratorInfinite = 1;
    public const int EducationEditionOfferNone = 0;
    public const int XblBroadcastModeDefault = 0;
    public const int PlatformBroadcastModeDefault = 0;
    public const int ChatRestrictionNone = 0;
    public const int ServerEditorConnectionPolicyNone = 0;
    public const int DefaultServerChunkTickRadius = 4;

    public override int Id => (int)ProtocolInfo.START_GAME_PACKET;

    public string LevelName = "";
    public long EntityId = 0;
    public int GameMode = AbilityBits.WireGameModeSurvival;
    public float PositionX;
    public float PositionY = -60f; // flat spawn; valor real vem de SendStartGame(player.PositionY)
    public float PositionZ;
    public float Pitch;
    public float Yaw;
    public long Seed = 0;
    public short BiomeType = 0;
    public string BiomeName = "plains";
    public int Dimension = DimensionId.Overworld;
    public int Generator = GeneratorInfinite;
    public int GameType = AbilityBits.WireGameModeSurvival;
    public int Difficulty = 0;
    public int SpawnBlockX = 0;
    public int SpawnBlockY = 0;
    public int SpawnBlockZ = 0;
    public int EditorType = 0;
    public int StopTime = 0;

    /// <summary>Filled by Protocol from server identity (Packets must not reference Server).</summary>
    public string BaseGameVersion { get; set; } = "";
    public string GameVersion { get; set; } = "";

    /// <summary>Must be true when chunk palettes use FNV network_id hashes (not legacy runtime ids).</summary>
    public bool UseBlockNetworkIdHashes { get; set; } = true;

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
        writer.WriteVarInt(EducationEditionOfferNone); // EducationEditionOffer
        writer.WriteBool(false); // EducationFeaturesEnabled
        writer.WriteVarString(""); // EducationProductID
        writer.WriteFloat(0, BinaryStream.Endianess.Little); // RainLevel
        writer.WriteFloat(0, BinaryStream.Endianess.Little); // LightningLevel
        writer.WriteBool(true); // ConfirmedPlatformLockedContent
        writer.WriteBool(true); // MultiPlayerGame
        writer.WriteBool(true); // LANBroadcastEnabled
        writer.WriteVarInt(XblBroadcastModeDefault); // XBLBroadcastMode
        writer.WriteVarInt(PlatformBroadcastModeDefault); // PlatformBroadcastMode
        writer.WriteBool(true); // CommandsEnabled
        writer.WriteBool(false); // TexturePackRequired
        
        // GameRules slice (length 0)
        writer.WriteUnsignedVarInt(0);
        // Experiments slice (length 0)
        writer.WriteUInt(0, BinaryStream.Endianess.Little); // SliceUint32Length
        writer.WriteBool(false); // ExperimentsPreviouslyToggled
        writer.WriteBool(false); // BonusChestEnabled
        writer.WriteBool(false); // StartWithMapEnabled
        // StartGame Operator vs AbilityData Member — intentional split (current wire).
        writer.WriteVarInt(AbilityBits.PlayerPermissionOperator); // PlayerPermissions
        writer.WriteInt(DefaultServerChunkTickRadius, BinaryStream.Endianess.Little); // ServerChunkTickRadius
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
        writer.WriteVarString(BaseGameVersion); // BaseGameVersion
        writer.WriteInt(0, BinaryStream.Endianess.Little); // LimitedWorldWidth
        writer.WriteInt(0, BinaryStream.Endianess.Little); // LimitedWorldDepth
        writer.WriteBool(false); // NewNether
        
        // EducationSharedResourceURI
        writer.WriteVarString(""); // ButtonName
        writer.WriteVarString(""); // LinkURI
        
        // ForceExperimentalGameplay Optional[bool]
        writer.WriteBool(false); // has value = false
        
        writer.WriteByte(ChatRestrictionNone); // ChatRestrictionLevel
        writer.WriteBool(false); // DisablePlayerInteractions
        writer.WriteVarInt(ServerEditorConnectionPolicyNone); // ServerEditorConnectionPolicy
        writer.WriteBool(false); // AllowAnonymousBlockDropsInEditorWorlds
        writer.WriteVarString(""); // LevelID
        writer.WriteVarString(LevelName); // WorldName
        writer.WriteVarString(""); // TemplateContentIdentity
        writer.WriteBool(false); // Trial
        
        // PlayerMovementSettings
        writer.WriteVarInt(0); // RewindHistorySize
        writer.WriteBool(true); // ServerAuthoritativeBlockBreaking — AuthInput BlockActions
        
        writer.WriteLong(0, BinaryStream.Endianess.Little); // Time
        writer.WriteVarInt(0); // EnchantmentSeed
        
        // Blocks Slice
        writer.WriteUnsignedVarInt(0); // length 0
        
        writer.WriteVarString(""); // MultiPlayerCorrelationID
        writer.WriteBool(true); // ServerAuthoritativeInventory
        writer.WriteVarString(GameVersion); // GameVersion
        
        // PropertyData (empty NBT compound, network encoding)
        var propertyData = NbtCodec.Encode(
            new NbtNamedTag("", NbtTag.Compound(new NbtCompound())),
            NbtEncoding.Network);
        writer.Write(propertyData);        
        writer.WriteULong(0, BinaryStream.Endianess.Little); // ServerBlockStateChecksum
        writer.WriteLong(0, BinaryStream.Endianess.Little); // WorldTemplateID pt 1
        writer.WriteLong(0, BinaryStream.Endianess.Little); // WorldTemplateID pt 2
        
        writer.WriteBool(false); // ClientSideGeneration
        writer.WriteBool(UseBlockNetworkIdHashes);
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
