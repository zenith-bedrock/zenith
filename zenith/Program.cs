using zenith.Log;
using Zenith.Raknet;

var raknet = new RakNetServer(19132)
{
    Logger = new Logger()
};
raknet.StartAsync()
    .Wait();