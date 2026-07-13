using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Network;
using Zenith.Raknet.Network.Protocol;
using Zenith.Raknet.Stream;

namespace Zenith.Raknet;

public class UnconnectedRakNet
{
    // Abaixo de ~50 bytes não sobra espaço útil depois do overhead de frame (36 bytes) +
    // datagram header (4 bytes); acima de 1492 estoura o MTU padrão de Ethernet (1500) com
    // folga pra headers IP/UDP. Um MTU malicioso fora desse range não é só "ineficiente",
    // é uma DoS: com MTU <= 36, `MTU - 36` fica <= 0 e o split loop do SendFrame trava
    // o servidor inteiro num loop infinito.
    private const ushort MIN_MTU = 400;
    private const ushort MAX_MTU = 1492;

    private static ushort ClampMtu(int requested) => (ushort)Math.Clamp(requested, MIN_MTU, MAX_MTU);

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
                    MTUSize = ClampMtu(request1.MTUSize + 28)
                }.Encode();
                _server.Send(remoteEndPoint, reply1Buffer);
                return true;
            case (byte)MessageIdentifier.OpenConnectionRequest2:
                var request2 = IPacket.From<OpenConnectionRequest2>(reader);
                var mtu = ClampMtu(request2.MTUSize);

                var reply2Buffer = new OpenConnectionReply2
                {
                    ServerGuid = _server.Guid,
                    ClientAddress = remoteEndPoint,
                    MTUSize = mtu,
                    ServerSecurity = false
                }.Encode();

                if (_server.HasSession(remoteEndPoint))
                {
                    // Retransmissão do handshake (comum em UDP com perda de pacote): o cliente
                    // já tem sessão, só reenvia o reply em vez de criar (e vazar) outra sessão
                    // e disparar OnSessionOpen de novo pro mesmo peer.
                    _server.Send(remoteEndPoint, reply2Buffer);
                    return true;
                }

                if (_server.Connections.Count >= _server.MaxConnections)
                {
                    _server.Logger?.Warning($"Rejected connection from {remoteEndPoint}: server full.");
                    return true;
                }

                var session = new RakNetSession
                {
                    Id = _server.NextSessionId(),
                    EndPoint = remoteEndPoint,
                    MTU = mtu,
                    Server = _server
                };

                _server.AddSession(session);
                _server.SessionListener?.OnSessionOpen(session);

                _server.Send(remoteEndPoint, reply2Buffer);
                return true;
        }
        return true;
    }
}