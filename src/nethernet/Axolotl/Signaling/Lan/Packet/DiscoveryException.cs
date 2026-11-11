namespace Zenith.NetherNet.Axolotl.Signaling.Lan.Packet;

public class DiscoveryException : NetherNetException
{
    public DiscoveryException(string message) : base(message) { }
    public DiscoveryException(string message, Exception? inner) : base(message, inner) { }
}
