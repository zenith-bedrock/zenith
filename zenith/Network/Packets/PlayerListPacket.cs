using Zenith.Raknet.Stream;

namespace Zenith.Network.Packets;

/// <summary>
/// PlayerList (0x3f). ADD inclui skin completa no wire — usamos placeholder 64×64.
/// </summary>
class PlayerListPacket : DataPacket
{
    public const byte TypeAdd = 0;
    public const byte TypeRemove = 1;

    public override int Id => (int)ProtocolInfo.PLAYER_LIST_PACKET;

    public byte Type { get; set; }
    public PlayerListEntry[] Entries { get; set; } = [];

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteByte(Type);
        writer.WriteUnsignedVarInt(Entries.Length);

        foreach (var entry in Entries)
        {
            writer.WriteUuid(entry.Uuid);
            if (Type != TypeAdd) continue;

            writer.WriteVarLong(entry.ActorUniqueId);
            writer.WriteVarString(entry.Username);
            writer.WriteVarString(entry.XboxUserId);
            writer.WriteVarString(entry.PlatformChatId);
            writer.WriteInt(entry.BuildPlatform, BinaryStream.Endianess.Little);
            SkinWire.WritePlaceholder(ref writer, entry.SkinId);
            writer.WriteBool(entry.IsTeacher);
            writer.WriteBool(entry.IsHost);
            writer.WriteBool(entry.IsSubClient);
            writer.WriteUInt(entry.Color, BinaryStream.Endianess.Little);
        }

        if (Type == TypeAdd)
        {
            foreach (var entry in Entries)
                writer.WriteBool(entry.Verified);
        }

        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}

readonly struct PlayerListEntry
{
    public Guid Uuid { get; init; }
    public long ActorUniqueId { get; init; }
    public string Username { get; init; }
    public string XboxUserId { get; init; }
    public string PlatformChatId { get; init; }
    public int BuildPlatform { get; init; }
    public string SkinId { get; init; }
    public bool IsTeacher { get; init; }
    public bool IsHost { get; init; }
    public bool IsSubClient { get; init; }
    public uint Color { get; init; }
    public bool Verified { get; init; }

    public static PlayerListEntry ForAdd(Guid uuid, long uniqueId, string username) => new()
    {
        Uuid = uuid,
        ActorUniqueId = uniqueId,
        Username = username,
        XboxUserId = "",
        PlatformChatId = "",
        BuildPlatform = -1,
        SkinId = $"{username}.Zenith",
        Color = 0xffffffff,
        Verified = false
    };

    public static PlayerListEntry ForRemove(Guid uuid) => new()
    {
        Uuid = uuid,
        Username = "",
        XboxUserId = "",
        PlatformChatId = "",
        SkinId = ""
    };
}
