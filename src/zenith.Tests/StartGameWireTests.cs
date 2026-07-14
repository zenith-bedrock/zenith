using Zenith.Network.Packets;
using Zenith.Network.Session;
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
    public void Protocol_identity_matches_client_1001_layout()
    {
        Assert.Equal(1001, ServerIdentity.ProtocolVersion);
        Assert.Equal("1.26.33", ServerIdentity.VersionName);
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
        Assert.True(bytes.Length >= 9);

        // Tail layout (see StartGamePacket.Encode):
        // ClientSideGeneration, UseBlockNetworkIdHashes, ServerAuthoritativeSound, IsLoggingChat,
        // has ServerJoinInformation, then four empty varstrings (Server/Scenario/World/Owner).
        Assert.Equal(0, bytes[^9]); // ClientSideGeneration
        Assert.Equal(1, bytes[^8]); // UseBlockNetworkIdHashes
        Assert.Equal(0, bytes[^7]); // ServerAuthoritativeSound
        Assert.Equal(0, bytes[^6]); // IsLoggingChat
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
        Assert.Equal(0, bytes[^8]);
    }
}
