using Zenith.Binary;

namespace Zenith.Packets;

/// <summary>
/// A packet whose body is a fixed byte string captured from the stock BDS (<see cref="JoinStaticData"/>): the same for
/// every player on a given build, so it is replayed as is instead of being modelled field by field.
/// </summary>
sealed class StaticDataPacket(int id, byte[] body) : DataPacket
{
    public override int Id => id;

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.Write(body);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}
