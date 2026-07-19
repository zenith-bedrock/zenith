using System.Text;
using System.Text.Json;
using Zenith.Packets;
using Zenith.Raknet.Stream;
using Zenith.Session;
using Xunit;

namespace Zenith.Tests;

public class ClientProfileParserTests
{
    [Fact]
    public void Parse_reads_xid_device_and_platform()
    {
        var identity = MakeJwt(new { xid = "25332747913222912", xname = "Steve" });
        var clientData = MakeJwt(new
        {
            DeviceId = "dev-abc",
            DeviceOS = 7,
            PlatformOnlineId = "plat-online"
        });

        var profile = ClientProfileParser.Parse(identity, identityChainJson: null, clientData);
        Assert.Equal("25332747913222912", profile.Xuid);
        Assert.Equal("dev-abc", profile.DeviceId);
        Assert.Equal(7, profile.BuildPlatform);
        Assert.Equal("plat-online", profile.PlatformChatId);
    }

    [Fact]
    public void Parse_reads_XUID_from_chain_extraData()
    {
        var chainPayload = MakeJwt(new
        {
            extraData = new { XUID = "111222333", displayName = "Alex" }
        });
        var chain = JsonSerializer.Serialize(new { chain = new[] { chainPayload } });
        var profile = ClientProfileParser.Parse(identityToken: null, chain, clientDataJwt: null);
        Assert.Equal("111222333", profile.Xuid);
        Assert.Equal(-1, profile.BuildPlatform);
    }

    [Fact]
    public void PlayerList_encode_includes_xbox_and_build_platform()
    {
        var entry = PlayerListEntry.ForAdd(
            Guid.Parse("00010203-0405-0607-0809-0a0b0c0d0e0f"),
            1,
            "Steve",
            xboxUserId: "25332747913222912",
            platformChatId: "plat",
            buildPlatform: 7);
        var packet = new PlayerListPacket
        {
            Type = PlayerListPacket.TypeAdd,
            Entries = [entry]
        };

        var encoded = packet.Encode().ToArray();
        var reader = new BinaryStream(encoded);
        Assert.Equal((int)ProtocolInfo.PLAYER_LIST_PACKET, (int)reader.ReadUnsignedVarInt());
        Assert.Equal(PlayerListPacket.TypeAdd, reader.ReadByte());
        Assert.Equal(1, (int)reader.ReadUnsignedVarInt());
        _ = reader.ReadUuid();
        _ = reader.ReadVarLong();
        Assert.Equal("Steve", reader.ReadVarString());
        Assert.Equal("25332747913222912", reader.ReadVarString());
        Assert.Equal("plat", reader.ReadVarString());
        Assert.Equal(7, reader.ReadInt(BinaryStream.Endianess.Little));
    }

    [Fact]
    public void AddPlayer_encode_includes_device_fields()
    {
        var packet = new AddPlayerPacket
        {
            Uuid = Guid.Parse("00010203-0405-0607-0809-0a0b0c0d0e0f"),
            Username = "Steve",
            ActorRuntimeId = 1,
            PlatformChatId = "plat",
            DeviceId = "dev-abc",
            BuildPlatform = 7,
            HeldItem = NetworkItemStack.Empty
        };

        var encoded = packet.Encode().ToArray();
        var reader = new BinaryStream(encoded);
        Assert.Equal((int)ProtocolInfo.ADD_PLAYER_PACKET, (int)reader.ReadUnsignedVarInt());
        _ = reader.ReadUuid();
        Assert.Equal("Steve", reader.ReadVarString());
        _ = reader.ReadUnsignedVarLong();
        Assert.Equal("plat", reader.ReadVarString());
        // Skip pose + item + metadata + abilities — assert trailing device fields by scanning end.
        Assert.Contains("dev-abc"u8.ToArray(), encoded);
        // BuildPlatform LE int at end
        Assert.Equal(7, BitConverter.ToInt32(encoded.AsSpan(^4)));
    }

    private static string MakeJwt(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        static string B64Url(string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        return $"{B64Url("{}")}.{B64Url(json)}.sig";
    }
}
