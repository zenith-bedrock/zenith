using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Network;
using Zenith.Raknet.Network.Protocol;
using Zenith.Raknet.Stream;

namespace Zenith.Raknet;

public class UnconnectedRakNet
{
    private readonly RakNetServer _server;

    public UnconnectedRakNet(RakNetServer server)
    {
        _server = server;
    }

    public bool Handle(System.Net.IPEndPoint remoteEndPoint, byte[] buffer)
    {
        var pid = buffer[0];
        var reader = new BinaryStreamReader(buffer[1..]);

        _server.Logger?.Debug($"PID: {pid}");

        switch (pid)
        {
            case (byte)MessageIdentifier.UnconnectedPing:
                var ping = IPacket.From<UnconnectedPing>(reader);

                var pongBuffer = new UnconnectedPong
                {
                    Time = ping.Time,
                    ServerGuid = _server.Guid,
                    Message = $"MCPE;Zenith Bedrock;766;1.21.50;8192;18192;{_server.Guid};Test;Survival;1;19132;19132;"
                }.Encode();
                _server.Send(remoteEndPoint, pongBuffer);
                return true;
            case (byte)MessageIdentifier.OpenConnectionRequest1:
                var request1 = IPacket.From<OpenConnectionRequest1>(reader);
                _server.Logger?.Debug($"OpenConnectionRequest1: {request1.Protocol} {request1.MTUSize}");
                var reply1Buffer = new OpenConnectionReply1
                {
                    Guid = _server.Guid,
                    UseSecurity = false,
                    MTUSize = (ushort) Math.Min(request1.MTUSize + 28, 1492)
                }.Encode();
                _server.Send(remoteEndPoint, reply1Buffer);
                return true;
        }
        return true;
    }
}