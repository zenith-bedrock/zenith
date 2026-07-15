using System.Buffers;
using System.IO.Compression;
using Zenith.Event;
using Zenith.Network.Packets;
using Zenith.Network.Protocol;
using Zenith.Server;
using Zenith.Network.Session.Handler;
using Zenith.Raknet;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Network;
using Zenith.Raknet.Stream;

namespace Zenith.Network.Session;

/// <summary>
/// Sessão de protocolo Bedrock de um cliente conectado, sentada em cima do transporte
/// <see cref="RakNetSession"/>. Representa estado da conexão/rede; outbound tipado fica em
/// <see cref="Protocol"/>. Estado de jogo (Player, etc.) vive fora e não fala com RakNetSession.
///
/// Dona do <see cref="ISessionHandler"/> ativo.
/// </summary>
class NetworkSession
{
    public RakNetSession RakSession { get; }
    public ServerContext Context { get; }
    public BedrockProtocol Protocol { get; }

    /// <summary>Só deixa de ser null depois que o LoginSessionHandler valida a identidade.</summary>
    public Player.Player? Player { get; set; }
    public byte CompressionAlgorithm { get; set; } = PacketCompression.NONE;

    /// <summary>Evita dois CompleteSpawnAsync se o cliente reenviar RequestChunkRadius.</summary>
    public int PreSpawnLoadStarted;

    private ISessionHandler _handler;

    public NetworkSession(RakNetSession rakSession, ISessionHandler initialHandler, ServerContext context)
    {
        RakSession = rakSession;
        Context = context;
        Protocol = new BedrockProtocol(this);
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

    /// <summary>
    /// Bedrock game packets must be ordered: StartGame (hash flag) before LevelChunk,
    /// or the client decodes FNV palette ids as legacy runtime ids → empty skybox.
    /// </summary>
    internal const Reliability GamePacketReliability = Reliability.ReliableOrdered;

    /// <summary>Envelope de envio usado apenas pelos módulos <c>*Protocol</c> nesta assembly.</summary>
    internal void SendDataPacket(params DataPacket[] packets) =>
        SendDataPacket(RakNetSession.Priority.Normal, CompressionAlgorithm, packets);

    internal void SendDataPacket(RakNetSession.Priority priority, byte compression, params DataPacket[] packets)
    {
        var gamePacket = new GamePacket
        {
            Compression = compression,
            CompressionThreshold = Context.Config.Network.CompressionThreshold,
            Packets = new List<DataPacket>(packets)
        };

        var frame = new Frame
        {
            Reliability = GamePacketReliability,
            OrderChannel = 0,
            Buffer = gamePacket.Encode().ToArray()
        };

        RakSession.SendFrame(frame, priority);
    }

    public void Disconnect() => RakSession.Disconnect();

    /// <summary>
    /// Chamado pelo <see cref="Zenith.Network.ZenithSessionListener"/> quando a sessão de
    /// transporte é encerrada (client disconnect, kick ou timeout). Avisa o handler ativo
    /// pra ele poder limpar o que precisar, remove o Player do PlayerManager (se já tinha
    /// logado) e publica PlayerQuitEvent pra quem quiser reagir a isso sem precisar tocar
    /// aqui dentro.
    /// </summary>
    public void HandleClose(DisconnectReason reason)
    {
        Context.Logger.Info($"Session closed ({reason}): {RakSession.EndPoint}");

        if (Player is not null)
        {
            if (Player.IsInGame)
                PlayerVisibility.AnnounceLeave(Player, Context.PlayerManager.Online);

            Context.World.PersistInventory(Player.Uuid, Player.Inventory);

            Player.IsInGame = false;
            Context.PlayerManager.Remove(Player);
            Context.EventBus.Publish(new PlayerQuitEvent(Player));
        }

        RakSession.HasGameIdentity = false;
        _handler.OnDisable(this);
    }

    /// <summary>
    /// Chamado pelo <see cref="Zenith.Network.ZenithSessionListener"/> sempre que chega um
    /// datagrama de game packet pra essa sessão. Cuida da descompressão e distribui cada
    /// data packet contido pro handler ativo.
    /// </summary>
    public bool HandleGamePacket(ref BinaryStream stream)
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
            // MemoryStream direto sobre o array existente (com offset), sem copiar o
            // conteúdo restante pra um array novo só pra alimentar o DeflateStream.
            using var memoryStream = new MemoryStream(stream.Buffer, stream.Offset, stream.Length - stream.Offset, writable: false);
            using var inflater = new DeflateStream(memoryStream, CompressionMode.Decompress);

            var decompressed = InflateToPooledBuffer(inflater, out var decompressedLength);
            stream.Dispose();

            stream = new BinaryStream(decompressed[..decompressedLength]);
            ArrayPool<byte>.Shared.Return(decompressed);
        }

        // TODO: snappy compression

        var gamePacket = IPacket.From<GamePacket>(ref stream);
        foreach (var buffer in gamePacket.Buffers)
        {
            HandleDataPacket(buffer);
        }

        stream.Dispose();
        return false;
    }

    /// <summary>
    /// Descomprime <paramref name="inflater"/> inteiro pra um buffer alugado do ArrayPool,
    /// crescendo por dobragem quando necessário. Quem chama é responsável por devolver o
    /// buffer retornado ao pool (<see cref="ArrayPool{T}.Return"/>) depois de copiar o que
    /// precisar dele.
    /// </summary>
    private static byte[] InflateToPooledBuffer(DeflateStream inflater, out int length)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        length = 0;

        int read;
        while ((read = inflater.Read(buffer, length, buffer.Length - length)) > 0)
        {
            length += read;
            if (length == buffer.Length)
            {
                var bigger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                System.Buffer.BlockCopy(buffer, 0, bigger, 0, length);
                ArrayPool<byte>.Shared.Return(buffer);
                buffer = bigger;
            }
        }

        return buffer;
    }

    private void HandleDataPacket(byte[] buffer)
    {
        var stream = new BinaryStream(buffer);

        var header = new DataPacket.HeaderInfo();
        header.Decode(ref stream);

        if (!_handler.HandleDataPacket(this, header, ref stream))
        {
            Context.Logger.Warning($"Unhandled Data Packet: {header.Id} (handler: {_handler.GetType().Name})");
        }

        stream.Dispose();
    }
}
