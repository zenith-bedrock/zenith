using Zenith.Raknet.Stream;

namespace zenith.Network.Protocol;

class ResourcePackStackPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.RESOURCE_PACK_STACK_PACKET;

    public bool MustAccept { get; set; }
    // TODO: behavior packs
    // TODO: texture packs
    public string GameVersion { get; set; }
    // TODO: experiments
    public bool ExperimentsPreviouslyToggled { get; set; }
    public bool HasEditorPacks { get; set; }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteBool(MustAccept);
        writer.WriteUnsignedVarInt(0);
        writer.WriteUnsignedVarInt(0);
        writer.WriteVarString(GameVersion);
        writer.WriteInt(0, BinaryStream.Endianess.Little);
        writer.WriteBool(ExperimentsPreviouslyToggled);
        writer.WriteBool(HasEditorPacks);

        return writer.GetBufferDisposing();
    }

    public override void Decode(BinaryStream stream) { }
}