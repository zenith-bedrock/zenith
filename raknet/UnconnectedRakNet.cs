using System.Net;

namespace Zenith.Raknet;

public class UnconnectedRakNet
{
    private readonly RakNetServer _server;

    public UnconnectedRakNet(RakNetServer server)
    {
        _server = server;
    }

    public bool Handle(System.Net.IPEndPoint remoteEndPoint, byte[] buffer) {
        var flag = buffer[0];
        buffer = buffer[1..];

        switch (flag) {
            case 0x01: // UnconnectedPing
                return true;
        }
        return true;
    }
}