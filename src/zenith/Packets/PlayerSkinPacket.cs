using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// PlayerSkin (0x5d). The wire's <c>skin</c> field (<c>SerializedSkinRef</c>) carries two more
/// fields than <see cref="SerializedSkin"/>'s shared Write/Read (used by PlayerListPacket too,
/// which does not have them at the same position — ADR §90 fix stays local to this packet
/// instead of touching the shared type): a name-coded <c>trusted_skin_flag</c> enum
/// (Unset/False/True, spelled as its member name string on the wire) and a <c>profile_hash</c>
/// string, both right after <c>overrides_player_appearance</c> and before the packet's own
/// <c>localized_new_skin_name</c>/<c>localized_old_skin_name</c>. There is no separate trailing
/// packet-level bool — that was a phantom field.
/// </summary>
sealed class PlayerSkinPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.PLAYER_SKIN_PACKET;

    public string Uuid { get; set; } = "";
    public SerializedSkin Skin { get; set; }
    public string SkinName { get; set; } = "";
    public string OldSkinName { get; set; } = "";
    public bool IsVerified { get; set; }

    public override void Decode(ref BinaryStream stream)
    {
        Uuid = stream.ReadUuid().ToString("D");
        Skin = SerializedSkin.Read(ref stream);
        IsVerified = string.Equals(stream.ReadVarString(), "True", StringComparison.OrdinalIgnoreCase);
        _ = stream.ReadVarString(); // profile_hash — unused
        SkinName = stream.ReadVarString();
        OldSkinName = stream.ReadVarString();
    }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteUuid(Guid.Parse(Uuid));
        Skin.Write(ref writer);
        writer.WriteVarString(IsVerified ? "True" : "False"); // trusted_skin_flag
        writer.WriteVarString(""); // profile_hash
        writer.WriteVarString(SkinName);
        writer.WriteVarString(OldSkinName);
        return writer.GetBufferDisposing();
    }
}
