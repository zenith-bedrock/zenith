using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// JigsawStructureData (0x139) — vanilla jigsaw rules required by modern clients before StartGame.
/// The payload is one Network-NBT compound, with no byte-array length prefix.
/// </summary>
sealed class JigsawStructureDataPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.JIGSAW_STRUCTURE_DATA_PACKET;

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.Write(JigsawStructureDataBlob.WireBody);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}
