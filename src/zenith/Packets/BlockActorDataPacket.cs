using Zenith.Binary;
using Zenith.Nbt;

namespace Zenith.Packets;

/// <summary>
/// BlockActorData (0x38) - S2C: the tile (block entity) data of one block, as network NBT. Chests need it for the client to know
/// two adjacent chests are one double chest (pairx, pairz, pairlead); the block state alone does not say.
/// </summary>
sealed class BlockActorDataPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.BLOCK_ACTOR_DATA_PACKET;

    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public NbtCompound Data { get; set; } = new();

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteVarInt(X);
        writer.WriteVarInt(Y);
        writer.WriteVarInt(Z);
        writer.Write(NbtCodec.Encode(new NbtNamedTag("", NbtTag.Compound(Data)), NbtEncoding.Network));
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) => throw new NotSupportedException("BlockActorData is server-to-client only.");
}
