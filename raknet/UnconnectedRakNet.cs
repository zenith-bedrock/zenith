using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Network;
using Zenith.Raknet.Network.Protocol;
using Zenith.Raknet.Stream;

namespace Zenith.Raknet;

public class UnconnectedRakNet
{
    private readonly RakNetServer _server;

    public UnconnectedRakNet(RakNetServer server) => _server = server;

    public bool Handle(System.Net.IPEndPoint remoteEndPoint, byte[] buffer)
    {
        var pid = buffer[0];
        var reader = new BinaryStream(buffer[1..]);

        _server.Logger?.Debug($"Unconnected PID: {pid}");

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
                var reply1Buffer = new OpenConnectionReply1
                {
                    Guid = _server.Guid,
                    UseSecurity = false,
                    MTUSize = (ushort)Math.Min(request1.MTUSize + 28, 1492)
                }.Encode();
                _server.Send(remoteEndPoint, reply1Buffer);
                return true;
            case (byte)MessageIdentifier.OpenConnectionRequest2:
                var request2 = IPacket.From<OpenConnectionRequest2>(reader);
                var reply2Buffer = new OpenConnectionReply2
                {
                    ServerGuid = _server.Guid,
                    ClientAddress = remoteEndPoint,
                    MTUSize = request2.MTUSize,
                    ServerSecurity = false
                }.Encode();

                _server.AddSession(new RakNetSession
                {
                    Id = _server.NextSessionId(),
                    EndPoint = remoteEndPoint,
                    MTU = request2.MTUSize,
                    Server = _server
                });

                _server.Send(remoteEndPoint, reply2Buffer);
                return true;
        }
        return true;
    }
}