using Zenith.Packets;
using Zenith.Session;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Network;
using Zenith.Server;
using Xunit;

namespace Zenith.Tests;

/// <summary>
/// Locks StartGame hash flag + Bedrock egress reliability — empty skybox regressions.
/// </summary>
public class StartGameWireTests
{
    [Fact]
    public void Game_packet_egress_uses_reliable_ordered()
    {
        Assert.Equal(Reliability.ReliableOrdered, NetworkSession.GamePacketReliability);
        Assert.True(Frame.IsOrdered(NetworkSession.GamePacketReliability));
        Assert.False(Frame.IsOrdered(Reliability.Reliable));
    }

    [Fact]
    public void Protocol_identity_matches_client_2168_layout()
    {
        Assert.Equal(2168, ServerIdentity.ProtocolVersion);
        Assert.Equal("1.26.40", ServerIdentity.VersionName);
    }

    [Fact]
    public void StartGame_wire_ends_with_use_block_network_id_hashes_true()
    {
        var packet = new StartGamePacket
        {
            LevelName = "test",
            EntityId = 2,
            PositionX = 0,
            PositionY = -60f,
            PositionZ = 0,
            UseBlockNetworkIdHashes = true
        };

        var bytes = packet.Encode().ToArray();
        Assert.True(bytes.Length >= 8);

        // Tail layout (see StartGamePacket.Encode, ADR §93 — IsLoggingChat removed at 2168+):
        // ClientSideGeneration, UseBlockNetworkIdHashes, ServerAuthoritativeSound,
        // has ServerJoinInformation, then four empty varstrings (Server/Scenario/World/Owner).
        Assert.Equal(0, bytes[^8]); // ClientSideGeneration
        Assert.Equal(1, bytes[^7]); // UseBlockNetworkIdHashes
        Assert.Equal(0, bytes[^6]); // ServerAuthoritativeSound
        Assert.Equal(0, bytes[^5]); // ServerJoinInformation absent
        Assert.Equal(0, bytes[^4]);
        Assert.Equal(0, bytes[^3]);
        Assert.Equal(0, bytes[^2]);
        Assert.Equal(0, bytes[^1]);
    }

    [Fact]
    public void StartGame_wire_respects_use_block_network_id_hashes_false()
    {
        var packet = new StartGamePacket
        {
            LevelName = "test",
            EntityId = 1,
            UseBlockNetworkIdHashes = false
        };

        var bytes = packet.Encode().ToArray();
        Assert.Equal(0, bytes[^7]);
    }

    [Fact]
    public void StartGame_player_permissions_is_a_raw_byte_not_a_varint()
    {
        // PlayerPermissionLevel's underlying is int8 -> one raw byte, no zigzag compression.
        // OPERATOR=2 must reach the wire as 0x02, not zigzag(2)=0x04 (ADR §93).
        var packet = new StartGamePacket { LevelName = "test", EntityId = 1 };
        var bytes = packet.Encode().ToArray();
        Assert.Contains((byte)0x02, bytes);

        var stream = new Zenith.Raknet.Stream.BinaryStream(bytes);
        _ = stream.ReadUnsignedVarInt(); // packet id
        _ = stream.ReadVarLong(); // EntityId
        _ = stream.ReadUnsignedVarLong(); // EntityRuntimeID
        _ = stream.ReadVarInt(); // PlayerGameMode
        for (var i = 0; i < 5; i++) _ = stream.ReadFloat(Zenith.Raknet.Stream.BinaryStream.Endianess.Little); // pos xyz + pitch/yaw
        _ = stream.ReadLong(Zenith.Raknet.Stream.BinaryStream.Endianess.Little); // Seed
        _ = stream.ReadShort(Zenith.Raknet.Stream.BinaryStream.Endianess.Little); // BiomeType
        _ = stream.ReadVarString(); // BiomeName
        _ = stream.ReadVarInt(); // Dimension
        _ = stream.ReadVarInt(); // Generator
        _ = stream.ReadVarInt(); // GameType
        _ = stream.ReadBool(); // Hardcore
        _ = stream.ReadVarInt(); // Difficulty
        _ = stream.ReadVarInt(); _ = stream.ReadVarInt(); _ = stream.ReadVarInt(); // WorldSpawn BlockPos
        _ = stream.ReadBool(); // AchievementsDisabled
        _ = stream.ReadVarInt(); // EditorWorldType
        _ = stream.ReadBool(); _ = stream.ReadBool(); // CreatedInEditor, ExportedFromEditor
        _ = stream.ReadVarInt(); // DayCycleLockTime
        _ = stream.ReadUnsignedVarInt(); // EducationEditionOffer (unsigned per ADR §93)
        _ = stream.ReadBool(); // EducationFeaturesEnabled
        _ = stream.ReadVarString(); // EducationProductID
        _ = stream.ReadFloat(Zenith.Raknet.Stream.BinaryStream.Endianess.Little); // RainLevel
        _ = stream.ReadFloat(Zenith.Raknet.Stream.BinaryStream.Endianess.Little); // LightningLevel
        _ = stream.ReadBool(); _ = stream.ReadBool(); _ = stream.ReadBool(); // Confirmed/MultiPlayer/LAN
        _ = stream.ReadVarInt(); _ = stream.ReadVarInt(); // XBL/PlatformBroadcastMode
        _ = stream.ReadBool(); _ = stream.ReadBool(); // CommandsEnabled, TexturePackRequired
        _ = stream.ReadUnsignedVarInt(); // GameRules length
        _ = stream.ReadUInt(Zenith.Raknet.Stream.BinaryStream.Endianess.Little); // Experiments length
        _ = stream.ReadBool(); _ = stream.ReadBool(); _ = stream.ReadBool(); // ExperimentsToggled/BonusChest/StartWithMap

        Assert.Equal(0x02, stream.ReadByte()); // PlayerPermissions — raw byte, not varint
    }
}
