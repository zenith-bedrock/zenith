using Zenith.Raknet.Stream;

namespace Zenith.Raknet.Network.Protocol;

public class ConnectionRequest : IPacket
{
    public byte Id => (byte)Enumerator.MessageIdentifier.ConnectionRequest;

    public ulong ClientGuid { get; set; }
    public ulong SendPingTime { get; set; }
    public bool UseSecurity { get; set; }

    public void Decode(BinaryStream stream)
    {
        ClientGuid = stream.ReadULong();
        SendPingTime = stream.ReadULong();
        UseSecurity = stream.ReadBool();
    }
}