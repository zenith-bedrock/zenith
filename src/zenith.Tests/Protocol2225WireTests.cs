using Xunit;
using Zenith.Packets;
using Zenith.Binary;

namespace Zenith.Tests;

/// <summary>
/// Wire deltas between protocol 2193 and 2225 (Mojang bedrock-protocol-docs v1.26.51 → v1.26.62), limited to
/// the packets Zenith implements. Each test pins one delta.
/// </summary>
public sealed class Protocol2225WireTests
{
    [Fact]
    public void LevelChunk_ends_with_is_client_biome_update_false()
    {
        var packet = new LevelChunkPacket { ExtraPayload = [1, 2, 3] };

        var bytes = packet.Encode().ToArray();

        Assert.Equal(0, bytes[^1]);
        Assert.Equal([1, 2, 3, 0], bytes[^4..]);
    }

    [Fact]
    public void AddActor_and_AddPlayer_end_with_absent_passenger_block_data()
    {
        var actor = new AddActorPacket { EntityType = "minecraft:cow" }.Encode().ToArray();
        Assert.Equal(0, actor[^1]); // optional passenger block data absent
        Assert.Equal(0, actor[^2]); // entity links count

        var player = new AddPlayerPacket { Username = "p", DeviceId = "d", BuildPlatform = 7 }.Encode().ToArray();
        // links(0) + passenger(absent) + varstring "d" + li32 build platform
        Assert.Equal([0, 0, 1, (byte)'d', 7, 0, 0, 0], player[^8..]);
    }

    [Fact]
    public void PlayerList_add_writes_playfab_id_after_xuid_and_skin_no_longer_carries_it()
    {
        var entry = PlayerListEntry.ForAdd(Guid.NewGuid(), 5, "alice", xboxUserId: "xuid1", platformChatId: "chat1");
        var bytes = new PlayerListPacket { Type = PlayerListPacket.TypeAdd, Entries = [entry] }.Encode().ToArray();

        var reader = new BinaryStream(bytes);
        reader.ReadUnsignedVarInt(); // packet id
        Assert.Equal(1, (int)reader.ReadUnsignedVarInt()); // entry count
        reader.ReadUnsignedVarInt(); // union variant
        reader.ReadByte(); // action
        reader.ReadUuid();
        reader.ReadVarLong();
        Assert.Equal("alice", reader.ReadVarString());
        Assert.Equal("xuid1", reader.ReadVarString());
        Assert.Equal("", reader.ReadVarString()); // PlayFab ID
        Assert.Equal("chat1", reader.ReadVarString()); // Platform Online ID
        reader.ReadInt(BinaryStream.Endianess.Little); // build platform
        Assert.Equal(entry.SkinId, reader.ReadVarString()); // skin starts with its id, then the resource patch
        Assert.StartsWith("{", reader.ReadVarString());
    }

    [Fact]
    public void Animate_hand_is_optional_and_trailing()
    {
        var without = new AnimatePacket { Action = AnimatePacket.ActionSwingArm, ActorRuntimeId = 1 }.Encode().ToArray();
        var withHand = new AnimatePacket { Action = AnimatePacket.ActionSwingArm, ActorRuntimeId = 1, Hand = 1 }.Encode().ToArray();

        Assert.Equal(0, without[^1]); // swing source absent … then hand absent
        Assert.Equal(without.Length - 1 + 2, withHand.Length); // hand: present flag + value replaces the absent flag
        Assert.Equal([1, 1], withHand[^2..]);
    }

    [Fact]
    public void ItemStackRequest_reserved_action_is_skipped_and_later_action_ids_shift()
    {
        Assert.Equal(16, ItemStackRequestPacket.ActionCraftReservedAction);
        Assert.Equal(17, ItemStackRequestPacket.ActionCraftNonImplemented);
        Assert.Equal(18, ItemStackRequestPacket.ActionCraftResultsDeprecated);
    }
}
