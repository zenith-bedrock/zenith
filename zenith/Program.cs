using zenith.Network;
using zenith.Log;
using Zenith.Raknet;

var raknet = new RakNetServer(19132)
{
    Logger = new Logger(),
    SessionListener = new SessionListener()
};
raknet.StartAsync()
    .Wait();