using Zenith.Raknet.Stream;

namespace Zenith.Raknet.Network.Protocol;

public class OpenConnectionReply1 : IPacket
{
    public byte Id => (byte)Enumerator.MessageIdentifier.OpenConnectionReply1;
    
    public ulong Guid { get; set; }
    public bool UseSecurity { get; set; }
    public uint Cookie { get; set; }
    public ushort MTUSize { get; set; }
    
    public byte[] Encode()
    {
        var writer = new BinaryStreamWriter();
        writer.WriteByte(Id);
        writer.WriteMagic();
        writer.WriteUInt64BE(Guid);
        writer.WriteBool(UseSecurity);
        if (UseSecurity)
        {
            // TODO: writer.WriteUInt32BE(Cookie);
        }
        writer.WriteUInt16BE(MTUSize);
        return writer.GetBufferDisposing();
    }

    public void Decode(BinaryStreamReader stream) {}
}