using Zenith.Raknet.Stream;

namespace Zenith.Raknet.Network.Protocol;

public class OpenConnectionReply1 : IPacket
{
    public byte Id => (byte)Enumerator.MessageIdentifier.OpenConnectionReply1;
    
    public ulong Guid { get; set; }
    public bool UseSecurity { get; set; }
    public uint Cookie { get; set; }
    public ushort MTUSize { get; set; }
    
    public Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteByte(Id);
        writer.WriteMagic();
        writer.WriteULong(Guid);
        writer.WriteBool(UseSecurity);
        if (UseSecurity)
        {
            writer.WriteUInt(Cookie);
        }
        writer.WriteUShort(MTUSize);
        return writer.GetBufferDisposing();
    }

    public void Decode(BinaryStream stream) {}
}