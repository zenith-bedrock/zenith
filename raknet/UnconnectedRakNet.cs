using Zenith.Raknet.Enumerator;
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
        buffer = buffer[1..];

        _server.Logger?.Debug($"PID: {pid}");

        switch (pid)
        {
            case (byte)MessageIdentifier.UnconnectedPing: // UnconnectedPing
                var reader = new BinaryStreamReader(buffer);
                var time = reader.ReadUInt64BE();
                var magic = reader.ReadMagic();
                var clientGuid = reader.ReadUInt64BE();

                var writer = new BinaryStreamWriter();
                writer.WriteByte((byte)MessageIdentifier.UnconnectedPong);
                writer.WriteUInt64BE(time);
                writer.WriteUInt64BE(_server.Guid);
                writer.WriteMagic();
                writer.WriteString($"MCPE;Zenith;766;1.21.50;8192;18192;{_server.Guid};Test;Survival;1;19132;19132;");

                _server.Send(remoteEndPoint, writer.GetBuffer());

                return true;
        }
        return true;
    }
}