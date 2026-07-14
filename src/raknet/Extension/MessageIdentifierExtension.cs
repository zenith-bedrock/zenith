using Zenith.Raknet.Enumerator;

namespace Zenith.Raknet.Extension;

public static class MessageIdentifierExtension
{
    
    public static MessageIdentifier FromByte(byte id)
    {
        if (id is >= 0x80 and <= 0x8d)
            return MessageIdentifier.FrameSetPacketBegin;
        if (Enum.IsDefined(typeof(MessageIdentifier), (int)id))
            return (MessageIdentifier)id;
        throw new ArgumentException("Invalid Packet ID");
    }
}