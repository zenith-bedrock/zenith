namespace Zenith.Raknet.Network.Protocol;

public class ACK : AcknowledgePacket
{
    public override byte Id => (byte)Enumerator.MessageIdentifier.Ack;
}