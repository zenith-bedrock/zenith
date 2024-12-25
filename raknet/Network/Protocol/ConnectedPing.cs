using Zenith.Raknet.Stream;

namespace Zenith.Raknet.Network.Protocol;

public class ConnectedPing : IPacket
{
    public byte Id => (byte)Enumerator.MessageIdentifier.ConnectedPing;

    public ulong SendPingTime { get; set; }

    public void Decode(BinaryStream stream)
    {
        SendPingTime = stream.ReadULong();
    }
}