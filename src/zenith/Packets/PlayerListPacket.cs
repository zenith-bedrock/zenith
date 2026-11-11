using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// PlayerList (0x3f), protocol 2168+ shape (ADR §92). No more packet-level <c>Type</c> byte +
/// parallel per-entry arrays (uuid list, then add-only fields list, then trailing trusted-skin
/// bool list) — each entry is now a self-contained tagged union (<c>RemoveEntry | AddEntry</c>),
/// carrying its own action twice: once as the union's variant index, once again as the payload's
/// own leading member (same "double write" Cereal convention as §82's entity metadata). Zenith
/// never mixes Add/Remove in one packet (every call site sends exactly one entry of one type —
/// see <c>EntityProtocol.SendPlayerListAdd</c>/<c>SendPlayerListRemove</c>), so the packet-level
/// <see cref="Type"/> field stays as the domain API; only the wire encoding changed.
/// The old trailing "trusted skins" bool array is gone too — the flag now lives inside the skin
/// structure itself (<c>trusted_skin_flag</c> + <c>profile_hash</c>, same fields §91 already
/// found and fixed locally for <see cref="PlayerSkinPacket"/> — <see cref="SerializedSkin"/>'s
/// shared Write stops short of them for the same reason documented there).
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
        writer.WriteUnsignedVarInt(Entries.Length);

        // Wire union index is RemoveEntry=0/AddEntry=1 (declaration order) — the OPPOSITE of
        // Zenith's own TypeAdd=0/TypeRemove=1 domain constants. Confirmed by reading gophertunnel's
        // playerListAction: it computes the variant separately (1 only when domain action==Add,
        // 0 otherwise) and leaves the *second* field ("legacy" action byte) as the unmodified
        // domain value — so the two "double write" fields are genuinely different values here,
        // not a repeat of the same one like §82's entity metadata. Caught live: sending Type
        // (0=Add) for both fields made a real client decode an intended Add as "remove".
        var wireVariant = Type == TypeAdd ? 1 : 0;

        foreach (var entry in Entries)
        {
            writer.WriteUnsignedVarInt(wireVariant);
            writer.WriteByte(Type); // payload's own action member — domain value, unmodified
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
            // NOTE: gophertunnel packs this as A|R<<8|G<<16|B<<24 then writes it big-endian —
            // net byte order [B,G,R,A]. Unverified against this little-endian write because the
            // only value Zenith ever sends is 0xffffffff (white), which is byte-identical either
            // way; revisit if a non-white player colour is ever needed (ADR §92).
            writer.WriteUInt(entry.Color, BinaryStream.Endianess.Little);
        }

        return writer.GetBufferDisposing();
    }

    private static void WriteSkin(ref BinaryStream writer, PlayerListEntry entry)
    {
        if (entry.Skin is { } full && full.Image.Data is { Length: > 0 })
            full.Write(ref writer);
        else if (entry.SkinRgba is { Length: > 0 } pixels && entry.SkinWidth > 0 && entry.SkinHeight > 0)
            SkinWire.Write(ref writer, entry.SkinId, pixels, entry.SkinWidth, entry.SkinHeight);
        else
            SkinWire.WritePlaceholder(ref writer, entry.SkinId);

        // trusted_skin_flag (name-coded enum) + profile_hash — same two fields §91 added locally
        // to PlayerSkinPacket; the old trailing per-packet "trusted skins" bool array is gone.
        writer.WriteVarString(entry.Verified ? "True" : "False");
        writer.WriteVarString("");
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
