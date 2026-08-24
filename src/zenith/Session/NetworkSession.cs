using System.Buffers;
using System.IO.Compression;
using System.Threading.Channels;
using Zenith.Event;
using Zenith.Packets;
using Zenith.Protocol;
using Zenith.Server;
using Zenith.Session.Handler;
using Zenith.Raknet;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Network;
using Zenith.Raknet.Stream;
using Zenith.World;

namespace Zenith.Session;

/// <summary>
/// Sessão de protocolo Bedrock de um cliente conectado, sentada em cima do transporte
/// <see cref="RakNetSession"/>. Representa estado da conexão/rede; outbound tipado fica em
/// <see cref="Protocol"/>. Estado de jogo (Player, etc.) vive fora e não fala com RakNetSession.
///
/// Dona do <see cref="ISessionHandler"/> ativo.
/// </summary>
class NetworkSession
{
    private const int WorldStreamQueueCapacity = 64;
    private readonly Channel<WorldStreamWork> _worldStreamQueue =
        Channel.CreateBounded<WorldStreamWork>(new BoundedChannelOptions(WorldStreamQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    private readonly CancellationTokenSource _worldStreamShutdown = new();
    private readonly Task _worldStreamWorker;
    private int _pendingWorldStreamColumns;
    private long _nextWorldStreamSequence;
    private long _completedWorldStreamSequence;

    public RakNetSession RakSession { get; }
    public ServerContext Context { get; }
    public BedrockProtocol Protocol { get; }

    /// <summary>Só deixa de ser null depois que o LoginSessionHandler valida a identidade.</summary>
    public Player.Player? Player { get; set; }

    /// <summary>
    /// Join/mid-game skin wire DTO (Packets). Null until ClientData parse or mid-game
    /// PlayerSkin. Lives on Session — not Player — so domain never references Packets (ADR §49).
    /// </summary>
    public SerializedSkin? Skin { get; set; }

    /// <summary>TrustedSkin from ClientData (PlayerList verified flag).</summary>
    public bool SkinTrusted { get; set; }

    /// <summary>
    /// Join identity / device fields for PlayerList, AddPlayer, chat (ADR §59).
    /// </summary>
    public ClientProfile Profile { get; set; } = ClientProfile.Empty;

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
        _worldStreamWorker = Task.Run(ProcessWorldStreamAsync);
    }

    /// <summary>
    /// Enqueues an already-decided column for off-tick LevelChunk encoding/compression and
    /// transmission. The bounded queue is deliberately specific to the post-spawn world stream;
    /// latency-sensitive gameplay remains on the owning tick and default channel.
    /// </summary>
    internal bool QueueWorldColumn(in ColumnReadResult column, byte orderChannel)
        => QueueWorldColumn(in column, orderChannel, out _);

    internal bool QueueWorldColumn(in ColumnReadResult column, byte orderChannel, out long sequence)
    {
        sequence = Interlocked.Increment(ref _nextWorldStreamSequence);
        Interlocked.Increment(ref _pendingWorldStreamColumns);
        if (_worldStreamQueue.Writer.TryWrite(new WorldStreamWork(column, orderChannel, sequence)))
            return true;

        Interlocked.Decrement(ref _pendingWorldStreamColumns);
        sequence = 0;
        return false;
    }

    internal bool WorldStreamIdle => Volatile.Read(ref _pendingWorldStreamColumns) == 0;
    internal long WorldStreamCompletedThrough => Volatile.Read(ref _completedWorldStreamSequence);

    private async Task ProcessWorldStreamAsync()
    {
        try
        {
            await foreach (var work in _worldStreamQueue.Reader
                               .ReadAllAsync(_worldStreamShutdown.Token)
                               .ConfigureAwait(false))
            {
                try
                {
                    ColumnSend.EmitToSession(this, work.Column, work.OrderChannel);
                    Volatile.Write(ref _completedWorldStreamSequence, work.Sequence);
                }
                finally
                {
                    Interlocked.Decrement(ref _pendingWorldStreamColumns);
                }
            }
        }
        catch (OperationCanceledException) when (_worldStreamShutdown.IsCancellationRequested)
        {
            // Session teardown cancels pending bulk world transmission.
        }
        catch (Exception exception)
        {
            Context.Logger.Error($"World stream worker failed: {exception}");
        }
    }

    /// <summary>
    /// Synchronous teardown (called from <see cref="HandleClose"/>, which cannot await): signal
    /// cancellation now, dispose the CTS once the worker has actually observed it and exited —
    /// disposing immediately would race the worker's still-in-flight read of <c>_shutdown.Token</c>.
    /// </summary>
    private void StopWorldStreamWorker()
    {
        _worldStreamQueue.Writer.TryComplete();
        _worldStreamShutdown.Cancel();
        _ = _worldStreamWorker.ContinueWith(
            static (_, state) => ((CancellationTokenSource)state!).Dispose(),
            _worldStreamShutdown,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private readonly record struct WorldStreamWork(ColumnReadResult Column, byte OrderChannel, long Sequence);

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

    /// <summary>
    /// Default channel: the join sequence (StartGame + PreSpawn's <c>PublishChunks</c> batch) plus
    /// every latency-sensitive gameplay packet (moves, Death/Respawn, chat, UI, …). Must stay ordered
    /// relative to StartGame — see <see cref="GamePacketReliability"/>'s own doc comment.
    /// </summary>
    internal const byte DefaultOrderChannel = 0;

    /// <summary>
    /// Bulk post-spawn world streaming (<c>ChunkStreamSystem</c>'s ring, one <c>LevelChunkPacket</c>
    /// per column). RakNet's Reliable Ordered guarantee is per-channel, not global — sharing channel 0
    /// with this meant a single dropped chunk in a multi-column burst held up every later-queued
    /// gameplay packet (Death/Respawn included) behind it client-side until retransmission filled the
    /// gap, even though nothing about those packets actually depended on chunk delivery order. Root
    /// cause of the <c>smoke:respawn</c> timeout (roadmap Yes-next priority 2) — found by confirming
    /// server-side send genuinely happens (no exception, no logic bug) while the client-side wait
    /// never resolves. Zenith's own <c>RakNetSession</c> already tracks up to 32 independent ordered
    /// channels (<c>Frame.MAX_ORDER_CHANNELS</c>) — this was simply never used. No ordering constraint
    /// requires this stream to share a channel with anything else: by the time it runs, the client
    /// already has its hash-flag context from PreSpawn's channel-0 batch.
    /// </summary>
    internal const byte WorldStreamOrderChannel = 1;

    /// <summary>Envelope de envio usado apenas pelos módulos <c>*Protocol</c> nesta assembly.</summary>
    internal void SendDataPacket(params DataPacket[] packets) =>
        SendDataPacket(RakNetSession.Priority.Normal, CompressionAlgorithm, DefaultOrderChannel, packets);

    internal void SendDataPacket(byte orderChannel, params DataPacket[] packets) =>
        SendDataPacket(RakNetSession.Priority.Normal, CompressionAlgorithm, orderChannel, packets);

    internal void SendDataPacket(RakNetSession.Priority priority, byte compression, params DataPacket[] packets) =>
        SendDataPacket(priority, compression, DefaultOrderChannel, packets);

    internal void SendDataPacket(
        RakNetSession.Priority priority, byte compression, byte orderChannel, params DataPacket[] packets)
    {
        // packetNames does a LINQ Select + string.Join per call — method arguments are evaluated
        // eagerly regardless of whether Debug logging is enabled, so without this guard every single
        // outbound send pays for it even when nothing will ever be printed (Phase XXVII).
        if (Context.Logger.IsDebugEnabled)
        {
            var packetNames = string.Join(", ", packets.Select(DescribeOutboundPacket));
            Context.Logger.Debug($"Protocol outbound -> {RakSession.EndPoint} channel={orderChannel} {packetNames}");
        }

        var gamePacket = new GamePacket
        {
            Compression = compression,
            CompressionThreshold = Context.Config.Network.CompressionThreshold,
            Packets = new List<DataPacket>(packets)
        };

        var frame = new Frame
        {
            Reliability = GamePacketReliability,
            OrderChannel = orderChannel,
            Buffer = gamePacket.EncodeOwned()
        };

        Context.Diagnostics.RecordPacketSent(packets.Length, frame.Buffer.Length);
        RakSession.SendFrame(frame, priority);
    }

    private static string DescribeOutboundPacket(DataPacket packet) => packet switch
    {
        AddActorPacket actor =>
            $"AddActorPacket(0x{actor.Id:X}, type={actor.EntityType}, uid={actor.EntityUniqueId}, rid={actor.EntityRuntimeId}, attrs=none)",
        UpdateAttributesPacket attributes =>
            $"UpdateAttributesPacket(0x{attributes.Id:X}, rid={attributes.ActorRuntimeId}, attrs={string.Join('|', attributes.Attributes.Select(attribute => attribute.Name))})",
        PlayerListPacket players => DescribePlayerList(players),
        InventoryContentPacket inventory =>
            $"InventoryContentPacket(0x{inventory.Id:X}, window={inventory.WindowId}, slots={inventory.Slots.Length}, nonAir={inventory.Slots.Count(slot => slot.NetworkId != 0)})",
        _ => $"{packet.GetType().Name}(0x{packet.Id:X})"
    };

    private static string DescribePlayerList(PlayerListPacket packet)
    {
        var skin = packet.Entries.FirstOrDefault(entry => entry.Skin is not null).Skin;
        var skinSummary = skin is { } value
            ? $"skin={value.Image.Width}x{value.Image.Height}/{value.Image.Data?.Length ?? 0}B persona={value.PersonaPieces?.Length ?? 0} tint={value.TintPieces?.Length ?? 0} anim={value.Animations?.Length ?? 0}"
            : $"rgbaFallback={packet.Entries.Count(entry => entry.SkinRgba is { Length: > 0 })}";
        return $"PlayerListPacket(0x{packet.Id:X}, type={(packet.Type == PlayerListPacket.TypeAdd ? "add" : "remove")}, entries={packet.Entries.Length}, {skinSummary})";
    }

    public void Disconnect() => RakSession.Disconnect();

    /// <summary>
    /// Bedrock DisconnectPacket + flush outbound frames, then RakNet close.
    /// Idempotent if the transport is already closed.
    /// </summary>
    public void DisconnectWithMessage(string message)
    {
        if (RakSession.IsClosed)
            return;

        Protocol.Login.SendDisconnect(message);
        RakSession.FlushOutgoing();
        RakSession.Disconnect();
    }

    /// <summary>
    /// Flush pending game packets then close transport — used after PlayStatus reject (§43).
    /// </summary>
    public void FlushAndDisconnect()
    {
        if (RakSession.IsClosed)
            return;

        RakSession.FlushOutgoing();
        RakSession.Disconnect();
    }

    /// <summary>
    /// Chamado pelo <see cref="ZenithSessionListener"/> quando a sessão de
    /// transporte é encerrada (client disconnect, kick ou timeout). Avisa o handler ativo
    /// pra ele poder limpar o que precisar, remove o Player do PlayerManager (se já tinha
    /// logado) e publica PlayerQuitEvent pra quem quiser reagir a isso sem precisar tocar
    /// aqui dentro.
    /// </summary>
    public void HandleClose(DisconnectReason reason)
    {
        Context.Logger.Info($"Session closed ({reason}): {RakSession.EndPoint}");
        StopWorldStreamWorker();

        if (Player is not null)
        {
            var wasInGame = Player.IsInGame;
            var online = Context.PlayerManager.SnapshotOnline();
            if (wasInGame)
                PlayerVisibility.AnnounceLeave(Player, online);

            // The network thread owns transport teardown, not ChestStore mutation. InventorySystem
            // releases this opener on its next tick against the authoritative world state.
            if (Player.OpenChest.HasValue)
                Context.PlayerManager.SubmitDisconnectedContainerCleanup(Player);

            // Ephemeral login uuid — skip disk so we do not litter inv:/pd: (ADR §60).
            // Deferred to the GameLoop tick (ADR §104b Adendo), not called directly here:
            // PlayerInventory's backing array has no lock, and InventorySystem/other GameLoop
            // systems can be concurrently mutating this exact player's inventory on the GameLoop
            // thread while this callback runs on the network thread. Same reasoning as the
            // ChestStore deferral above.
            if (Player.IdentityStable)
                Context.PlayerManager.SubmitDisconnectedInventoryPersist(Player);

            // Connection-lifecycle exception: this scalar prevents further peer fan-out while the
            // session is removed. It grants no authority to mutate gameplay/world state.
            Player.IsInGame = false;
            Context.PlayerManager.Remove(Player);
            Context.EventBus.Publish(new PlayerQuitEvent(Player, wasInGame));
        }

        RakSession.HasGameIdentity = false;
        _handler.OnDisable(this);
    }

    /// <summary>
    /// Chamado pelo <see cref="ZenithSessionListener"/> sempre que chega um
    /// datagrama de game packet pra essa sessão. Cuida da descompressão e distribui cada
    /// data packet contido pro handler ativo.
    /// </summary>
    public bool HandleGamePacket(ref BinaryStream stream)
    {
        var compressionType = stream.PeekByte();

        switch (compressionType)
        {
            case PacketCompression.ZLIB or PacketCompression.SNAPPY or PacketCompression.NONE:
                stream.ReadByte();
                break;
            default:
                compressionType = PacketCompression.NOT_PRESENT;
                break;
        }

        byte[]? pooledDecompressed = null;
        try
        {
            if (compressionType == PacketCompression.ZLIB)
            {
                // MemoryStream direto sobre o array existente (com offset), sem copiar o
                // conteúdo restante pra um array novo só pra alimentar o DeflateStream.
                using var memoryStream = new MemoryStream(stream.Buffer, stream.Offset, stream.Length - stream.Offset, writable: false);
                using var inflater = new DeflateStream(memoryStream, CompressionMode.Decompress);

                var decompressed = InflateToPooledBuffer(inflater, out var decompressedLength);
                stream.Dispose();

                pooledDecompressed = decompressed;
                stream = new BinaryStream(decompressed, decompressedLength);
            }

            if (compressionType == PacketCompression.SNAPPY)
            {
                // The server only ever advertises ZLIB in NetworkSettingsPacket (LoginSessionHandler),
                // so a compliant client never sends this prefix. Rejecting it explicitly avoids
                // silently parsing still-compressed bytes as plaintext packet data.
                throw new NotSupportedException("Snappy-compressed game packet received, but Snappy decompression is not implemented.");
            }

            // Inbound batch framing (Phase XXVII): each subpacket used to be copied into its own
            // byte[] (GamePacket.Decode) purely so it could be handed to a handler that immediately
            // wrapped it in a fresh BinaryStream and consumed it synchronously — no ownership ever
            // needed to survive past that call. ReadSubstream gives the same synchronous, bounded
            // access over the SAME backing buffer (the decompressed pooled array, or the original
            // frame buffer when uncompressed) with no per-subpacket allocation. The length prefix is
            // client-controlled; ReadSubstream itself rejects a declared length exceeding what's
            // actually left in the batch before creating the window, so a malformed length cannot
            // read into whatever follows.
            while (!stream.IsEndOfFile)
            {
                var length = stream.ReadUnsignedVarInt();
                var subpacket = stream.ReadSubstream(length);
                HandleDataPacket(ref subpacket);
            }

            return false;
        }
        finally
        {
            stream.Dispose();
            if (pooledDecompressed is not null)
                ArrayPool<byte>.Shared.Return(pooledDecompressed);
        }
    }

    /// <summary>Limite de segurança contra zip bomb.</summary>
    internal const int MaxDecompressedSize = 2 * 1024 * 1024;

    /// <summary>
    /// Descomprime <paramref name="inflater"/> inteiro pra um buffer alugado do ArrayPool,
    /// crescendo por dobragem quando necessário. Quem chama é responsável por devolver o
    /// buffer retornado ao pool (<see cref="ArrayPool{T}.Return"/>) depois de terminar de
    /// usar o <see cref="BinaryStream"/> que o envolve.
    /// </summary>
    internal static byte[] InflateToPooledBuffer(DeflateStream inflater, out int length)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        length = 0;

        int read;
        while ((read = inflater.Read(buffer, length, buffer.Length - length)) > 0)
        {
            length += read;

            if (length > MaxDecompressedSize)
            {
                ArrayPool<byte>.Shared.Return(buffer);
                throw new InvalidOperationException(
                    $"Decompressed data exceeds maximum size ({MaxDecompressedSize} bytes).");
            }

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

    private void HandleDataPacket(ref BinaryStream stream)
    {
        Context.Diagnostics.RecordPacketReceived(stream.Length - stream.Offset);

        var header = new DataPacket.HeaderInfo();
        header.Decode(ref stream);

        if (!_handler.HandleDataPacket(this, header, ref stream))
        {
            Context.Logger.Warning($"Unhandled Data Packet: {header.Id} (handler: {_handler.GetType().Name})");
        }

        stream.Dispose();
    }
}
