using Zenith.Raknet;

var raknet = new RakNetServer(19132);
raknet.StartAsync()
    .Wait();