using Zenith.Packets;
using Zenith.Raknet.Stream;
using Xunit;

namespace Zenith.Tests;

/// <summary>
/// Byte-level checks for PlayerListPacket's protocol 2168+ shape (ADR §92): per-entry tagged
/// union (variant + payload's own action, both written), no packet-level Type byte or trailing
/// trusted-skin bool array, trusted_skin_flag/profile_hash living inside the skin write instead.
/// </summary>
public class PlayerListPacketTests
{
    [Fact]
    public void Remove_entry_is_variant_action_uuid_only()
    {
        var uuid = Guid.Parse("00010203-0405-0607-0809-0a0b0c0d0e0f");
        var packet = new PlayerListPacket
        {
            Type = PlayerListPacket.TypeRemove,
            Entries = [PlayerListEntry.ForRemove(uuid)]
        };
        var bytes = packet.Encode().ToArray();

        var stream = new BinaryStream(bytes);
        Assert.Equal((int)ProtocolInfo.PLAYER_LIST_PACKET, (int)stream.ReadUnsignedVarInt());
        Assert.Equal(1, (int)stream.ReadUnsignedVarInt()); // entries count
        // Wire union index is RemoveEntry=0/AddEntry=1 — opposite of Zenith's own
        // TypeAdd=0/TypeRemove=1 domain constants (caught live, see PlayerListPacket.cs).
        Assert.Equal(0, (int)stream.ReadUnsignedVarInt()); // union variant (Remove)
        Assert.Equal(PlayerListPacket.TypeRemove, stream.ReadByte()); // payload's own action member — domain value
        Assert.Equal(uuid, stream.ReadUuid());
        Assert.True(stream.IsEndOfFile); // no add-only fields for a remove entry
    }

    [Fact]
    public void Add_entry_places_trusted_skin_flag_inside_skin_not_a_trailing_array()
    {
        var uuid = Guid.Parse("00010203-0405-0607-0809-0a0b0c0d0e0f");
        var entry = PlayerListEntry.ForAdd(
            uuid, uniqueId: 42, username: "Steve",
            verified: true, xboxUserId: "xuid-1", platformChatId: "plat", buildPlatform: 7);
        var packet = new PlayerListPacket { Type = PlayerListPacket.TypeAdd, Entries = [entry] };
        var bytes = packet.Encode().ToArray();

        var stream = new BinaryStream(bytes);
        Assert.Equal((int)ProtocolInfo.PLAYER_LIST_PACKET, (int)stream.ReadUnsignedVarInt());
        Assert.Equal(1, (int)stream.ReadUnsignedVarInt()); // entries count
        Assert.Equal(1, (int)stream.ReadUnsignedVarInt()); // union variant (Add) — wire-inverted, see above
        Assert.Equal(PlayerListPacket.TypeAdd, stream.ReadByte()); // payload's own action member — domain value
        Assert.Equal(uuid, stream.ReadUuid());
        Assert.Equal(42, stream.ReadVarLong());
        Assert.Equal("Steve", stream.ReadVarString());
        Assert.Equal("xuid-1", stream.ReadVarString());
        Assert.Equal("plat", stream.ReadVarString());
        Assert.Equal(7, stream.ReadInt(BinaryStream.Endianess.Little));

        var skin = SerializedSkin.Read(ref stream); // placeholder skin — byte-compatible with SkinWire.Write
        Assert.NotEqual("", skin.Id);

        Assert.Equal("True", stream.ReadVarString()); // trusted_skin_flag — inside the skin write, not trailing
        Assert.Equal("", stream.ReadVarString()); // profile_hash

        Assert.False(stream.ReadBool()); // is_teacher
        Assert.False(stream.ReadBool()); // is_host
        Assert.False(stream.ReadBool()); // is_sub_client
        Assert.Equal(0xffffffffu, stream.ReadUInt(BinaryStream.Endianess.Little)); // color
        Assert.True(stream.IsEndOfFile); // no trailing trusted-skin bool array (removed at 2168+)
    }
}
