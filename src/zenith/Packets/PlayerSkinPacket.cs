using Zenith.Raknet.Stream;

namespace Zenith.Packets;

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
        SkinName = stream.ReadVarString();
        OldSkinName = stream.ReadVarString();
        IsVerified = stream.ReadBool();
    }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteUuid(Guid.Parse(Uuid));
        Skin.Write(ref writer);
        writer.WriteVarString(SkinName);
        writer.WriteVarString(OldSkinName);
        writer.WriteBool(IsVerified);
        return writer.GetBufferDisposing();
    }
}
