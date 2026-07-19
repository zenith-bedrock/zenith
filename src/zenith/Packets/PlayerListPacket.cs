using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// PlayerList (0x3f). ADD embeds full <see cref="SerializedSkin"/> (join fidelity).
/// Fallback: classic RGBA via SkinWire, else white placeholder.
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
            WriteSkin(ref writer, entry);
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

    private static void WriteSkin(ref BinaryStream writer, PlayerListEntry entry)
    {
        if (entry.Skin is { } full && full.Image.Data is { Length: > 0 })
        {
            full.Write(ref writer);
            return;
        }

        if (entry.SkinRgba is { Length: > 0 } pixels && entry.SkinWidth > 0 && entry.SkinHeight > 0)
            SkinWire.Write(ref writer, entry.SkinId, pixels, entry.SkinWidth, entry.SkinHeight);
        else
            SkinWire.WritePlaceholder(ref writer, entry.SkinId);
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
    public SerializedSkin? Skin { get; init; }
    public byte[]? SkinRgba { get; init; }
    public uint SkinWidth { get; init; }
    public uint SkinHeight { get; init; }
    public bool IsTeacher { get; init; }
    public bool IsHost { get; init; }
    public bool IsSubClient { get; init; }
    public uint Color { get; init; }
    public bool Verified { get; init; }

    public static PlayerListEntry ForAdd(
        Guid uuid,
        long uniqueId,
        string username,
        SerializedSkin? skin = null,
        byte[]? skinRgba = null,
        uint skinWidth = 0,
        uint skinHeight = 0,
        bool verified = false,
        string xboxUserId = "",
        string platformChatId = "",
        int buildPlatform = -1) => new()
    {
        Uuid = uuid,
        ActorUniqueId = uniqueId,
        Username = username,
        XboxUserId = xboxUserId ?? "",
        PlatformChatId = platformChatId ?? "",
        BuildPlatform = buildPlatform,
        SkinId = skin?.Id is { Length: > 0 } id ? id : $"{username}.Zenith",
        Skin = skin,
        SkinRgba = skinRgba,
        SkinWidth = skinWidth,
        SkinHeight = skinHeight,
        Color = 0xffffffff,
        Verified = verified
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
