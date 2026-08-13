using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// PlayerSkin (0x5d). PlayerList and PlayerSkin share the complete protocol-2168 SerializedSkin, including its
/// trusted-skin flag and profile hash. The remaining two strings are packet-local names.
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
        IsVerified = Skin.Trusted;
        SkinName = stream.ReadVarString();
        OldSkinName = stream.ReadVarString();
    }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteUuid(Guid.Parse(Uuid));
        (Skin with { Trusted = IsVerified, ProfileHash = "" }).Write(ref writer);
        writer.WriteVarString(SkinName);
        writer.WriteVarString(OldSkinName);
        return writer.GetBufferDisposing();
    }
}
