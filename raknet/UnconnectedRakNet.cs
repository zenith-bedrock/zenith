namespace Zenith.Raknet;

// declare a class named UnconnectedRakNet with RakNetServer as variable
public class UnconnectedRakNet
{
    private readonly RakNetServer _server;

    public UnconnectedRakNet(RakNetServer server)
    {
        _server = server;
    }

    public bool Handle() {
        return true;
    }
}