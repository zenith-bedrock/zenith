using Zenith.Raknet.Stream;

namespace Zenith.Network.Packets;

/// <summary>Sincroniza o tempo do mundo (ticks 0..23999) com o cliente.</summary>
class SetTimePacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.SET_TIME_PACKET;

    public int Time { get; set; }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteVarInt(Time);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream)
    {
        Time = stream.ReadVarInt();
    }
}
