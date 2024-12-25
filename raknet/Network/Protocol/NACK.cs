namespace Zenith.Raknet.Network.Protocol;

public class NACK : AcknowledgePacket
{
    public override byte Id => (byte)Enumerator.MessageIdentifier.Nack;
}