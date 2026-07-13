using System.IO.Compression;
using zenith.Network.Protocol;
using zenith.Session.Handler;
using Zenith.Raknet;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Network;
using Zenith.Raknet.Stream;

namespace zenith.Session;

/// <summary>
/// Sessão de protocolo Bedrock de um cliente conectado, sentada em cima do transporte
/// <see cref="RakNetSession"/>. É a única classe do lado do jogo que deveria conhecer o
/// wire format (compressão, framing, dispatch de pacote); tudo mais trabalha em cima dela.
///
/// Dona do <see cref="ISessionHandler"/> ativo. Estado de jogo (Player, etc.) deve viver fora
/// daqui e falar com a sessão só através dela, nunca com o RakNetSession diretamente.
/// </summary>
class NetworkSession
{
    public RakNetSession RakSession { get; }

    private ISessionHandler _handler;

    public NetworkSession(RakNetSession rakSession, ISessionHandler initialHandler)
    {
        RakSession = rakSession;
        _handler = initialHandler;
        _handler.OnEnable(this);
    }

    /// <summary>Troca o handler ativo, disparando OnDisable no antigo e OnEnable no novo.</summary>
    public void SetHandler(ISessionHandler handler)
    {
        _handler.OnDisable(this);
        _handler = handler;
        _handler.OnEnable(this);
    }

    public void SendDataPacket(params DataPacket[] packets) =>
        SendDataPacket(RakNetSession.Priority.Normal, PacketCompression.NONE, packets);

    public void SendDataPacket(RakNetSession.Priority priority, byte compression, params DataPacket[] packets)
    {
        var buffers = new List<byte[]>();
        foreach (var packet in packets) buffers.Add(packet.Encode().ToArray());

        var gamePacket = new GamePacket
        {
            Compression = compression,
            Buffers = buffers
        };

        var frame = new Frame
        {
            Reliability = Reliability.Reliable,
            OrderChannel = 0,
            Buffer = gamePacket.Encode().ToArray()
        };

        RakSession.SendFrame(frame, priority);
    }

    public void Disconnect() => RakSession.Disconnect();

    /// <summary>
    /// Chamado pelo <see cref="zenith.Network.ZenithSessionListener"/> quando a sessão de
    /// transporte é encerrada (client disconnect, kick ou timeout). Avisa o handler ativo
    /// pra ele poder limpar o que precisar (quando existir Player, é aqui que ele sai do
    /// mundo e tem os dados salvos).
    /// </summary>
    public void HandleClose(DisconnectReason reason)
    {
        Console.WriteLine($"Session closed ({reason}): {RakSession.EndPoint}");
        _handler.OnDisable(this);
    }

    /// <summary>
    /// Chamado pelo <see cref="zenith.Network.ZenithSessionListener"/> sempre que chega um
    /// datagrama de game packet pra essa sessão. Cuida da descompressão e distribui cada
    /// data packet contido pro handler ativo.
    /// </summary>
    public bool HandleGamePacket(BinaryStream stream)
    {
        var compressionType = stream.Buffer[0];

        switch (compressionType)
        {
            case PacketCompression.ZLIB or PacketCompression.SNAPPY or PacketCompression.NONE:
                stream.ReadByte();
                break;
            default:
                compressionType = PacketCompression.NOT_PRESENT;
                break;
        }

        if (compressionType == PacketCompression.ZLIB)
        {
            var buffer = stream.ReadRemaining().ToArray();
            stream.Dispose();

            using var memoryStream = new MemoryStream(buffer);
            using var inflater = new DeflateStream(memoryStream, CompressionMode.Decompress);
            using var outputStream = new MemoryStream();
            inflater.CopyTo(outputStream);

            stream = new BinaryStream(outputStream.ToArray());
        }

        // TODO: snappy compression

        var gamePacket = IPacket.From<GamePacket>(stream);
        foreach (var buffer in gamePacket.Buffers)
        {
            HandleDataPacket(buffer);
        }

        stream.Dispose();
        return false;
    }

    private void HandleDataPacket(byte[] buffer)
    {
        var stream = new BinaryStream(buffer);

        var header = new DataPacket.HeaderInfo();
        header.Decode(stream);

        if (!_handler.HandleDataPacket(this, header, stream))
        {
            Console.WriteLine($"Unhandled Data Packet: {header.Id} (handler: {_handler.GetType().Name})");
        }

        stream.Dispose();
    }
}
