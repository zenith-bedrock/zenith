using Zenith.Raknet.Stream;

namespace Zenith.Network.Packets;

class ResourcePacksInfoPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.RESOURCE_PACKS_INFO_PACKET;

    public const byte COMPRESS_NOTHING = 0;
    public const byte COMPRESS_EVERYTHING = 1;

    public bool MustAccept { get; set; }
    public bool HasAddons { get; set; }
    public bool HasScripts { get; set; }
    public bool ForceDisableVibrantVisuals { get; set; }
    // public string WorldTemplateUuid { get; set; }
    public string WorldTemplateVersion { get; set; }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteBool(MustAccept);
        writer.WriteBool(HasAddons);
        writer.WriteBool(HasScripts);
        writer.WriteBool(ForceDisableVibrantVisuals);
        writer.WriteULong(0); // TODO: hack to work but this is a uuid
        writer.WriteULong(0); // TODO: hack to work but this is a uuid
        writer.WriteVarString(WorldTemplateVersion);
        writer.WriteShort(0, BinaryStream.Endianess.Little);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}