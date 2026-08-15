using System.Net;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Network;
using Zenith.Raknet.Network.Protocol;
using Zenith.Raknet.Stream;

namespace Zenith.Raknet;

public class RakNetSession
{
    public enum Priority
    {
        Normal,
        Immediate
    }

    public const int DGRAM_HEADER_SIZE = 4;
    public const int IP_UDP_HEADER_SIZE = 28;

    public required IPEndPoint EndPoint { get; init; }
    public int Id { get; init; }
    public required RakNetServer Server { get; init; }
    public required ushort MTU { get; init; }
    public long LastSeen { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>
    /// Set by the game layer after a Player is bound (login). Used when choosing which
    /// same-IP session to evict at MaxConnectionsPerAddress — prefer unbound ghosts.
    /// </summary>
    public bool HasGameIdentity { get; set; }

    protected readonly HashSet<uint> ReceivedFrameSequences = new();
    protected readonly HashSet<uint> LostFrameSequences = new();
    protected readonly uint[] InputHighestSequenceIndex = new uint[32];

    /// <summary>
    /// Timestamp is when reassembly for that split id started. Without an eviction sweep, a split
    /// id whose peer never delivers the final fragment (lost fragment, disconnect mid-transfer)
    /// occupied its slot forever — after <see cref="MAX_CONCURRENT_FRAGMENTED_MESSAGES"/> such
    /// abandoned reassemblies accumulate over a session's lifetime, the session could no longer
    /// receive ANY further split packet (cross-reference audit finding, Phase XXIII-B polish pass).
    /// </summary>
    protected readonly Dictionary<short, (long StartedAtMs, Dictionary<int, Frame> Parts)> FragmentsQueue = new();

    protected readonly uint[] InputOrderIndex = new uint[32];
    protected readonly Dictionary<byte, Dictionary<uint, Frame>> InputOrderingQueue = new();
    protected int LastInputSequence = -1;

    // Janela deslizante de dedup para qualquer frame reliable (Reliable, ReliableOrdered,
    // ReliableSequenced, ...), independente de reliability específica. Um frame reliable pode
    // ser retransmitido dentro de um FrameSet novo (sequence diferente) quando o servidor
    // não confirma a tempo; sem isso, o mesmo MessageIndex é processado de novo e pode
    // duplicar efeitos do lado do jogo (ex: LoginPacket reprocessado -> PlayerManager.TryAdd
    // falha achando que já tem alguém logado -> Disconnect indevido).
    private const uint RELIABLE_WINDOW_SIZE = 2048;
    protected uint ReliableWindowStart;
    protected uint ReliableWindowEnd = RELIABLE_WINDOW_SIZE;
    protected readonly HashSet<uint> ReliableWindow = new();

    protected readonly uint[] OutputOrderIndex = new uint[32];
    protected readonly uint[] OutputSequenceIndex = new uint[32];

    protected readonly HashSet<Frame> OutputFrames = new();

    /// <summary>
    /// Timestamp is the "sent at" wall-clock time (ms) — <see cref="ResendStaleBackupLocked"/> uses
    /// it to retransmit a reliable FrameSet that was never ACKed *or* NACKed. Retransmission was
    /// previously NACK-only: if the one NACK datagram reporting the loss was itself dropped by UDP
    /// (exactly as likely as any other datagram), the peer never learned it was missing and the
    /// entry sat here forever, permanently stalling that order channel (cross-reference audit
    /// finding, Phase XXIII-B polish pass). No RTT estimation exists yet, so the timeout is a fixed,
    /// conservative value rather than adaptive — see <see cref="ResendTimeoutMs"/>.
    /// </summary>
    protected readonly Dictionary<uint, (long SentAtMs, List<Frame> Frames)> OutputBackup = new();

    // Mantido em paralelo a OutputFrames em vez de recalculado via LINQ Sum a cada
    // QueueFrame: eram O(n) por chamada (O(n²) num burst de frames), e esse é
    // literalmente o hot path de todo Send/SendFrame.
    private int _outputFramesByteLength;

    protected uint OutputSequence;
    protected int OutputSplitIndex;
    protected uint OutputReliableIndex;

    /// <summary>
    /// Serializa mutações de saída (OutputFrames, índices, backup) e ACK/NACK de backup
    /// contra Tick / Incoming / fan-out cross-session no GameLoop.
    /// </summary>
    private readonly object _sessionLock = new();

    private bool _closed;

    public RakNetSession()
    {
        for (byte index = 0; index < 32; index++)
        {
            InputOrderingQueue[index] = new();
        }
    }

    public bool IsClosed => _closed;

    public void Disconnect(DisconnectReason reason = DisconnectReason.ServerDisconnect)
    {
        if (_closed) return;

        var disconnect = new Disconnect();

        var frame = new Frame
        {
            Reliability = Reliability.ReliableOrdered,
            OrderChannel = 0,
            Buffer = disconnect.Encode().ToArray()
        };

        SendFrame(frame, Priority.Immediate);

        Close(reason);
    }

    /// <summary>
    /// Marca a sessão como encerrada, avisa o listener e remove do servidor. Idempotente:
    /// só dispara o hook e a remoção na primeira chamada, então é seguro chamar de múltiplos
    /// lugares (timeout no Tick, pacote de disconnect do cliente, Disconnect() explícito)
    /// sem disparar OnSessionClose mais de uma vez pra mesma sessão.
    /// </summary>
    private void Close(DisconnectReason reason)
    {
        if (_closed) return;
        _closed = true;

        Server.SessionListener?.OnSessionClose(this, reason);
        Server.RemoveSession(this);
    }

    public void Tick()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (LastSeen + 15000 < now)
        {
            Disconnect(DisconnectReason.Timeout);
            return;
        }

        lock (_sessionLock)
        {
            FlushAcknowledgeLocked<ACK>(ReceivedFrameSequences);
            FlushAcknowledgeLocked<NACK>(LostFrameSequences);
            ResendStaleBackupLocked(now);

            SendQueueLocked(OutputFrames.Count);
        }
    }

    /// <summary>Fixed, conservative resend timeout — see <see cref="OutputBackup"/>'s doc comment.</summary>
    private const long ResendTimeoutMs = 1500;

    /// <summary>Caller must hold <see cref="_sessionLock"/>.</summary>
    private void ResendStaleBackupLocked(long now)
    {
        if (OutputBackup.Count == 0) return;

        List<uint>? stale = null;
        foreach (var (sequence, entry) in OutputBackup)
        {
            if (now - entry.SentAtMs < ResendTimeoutMs) continue;
            (stale ??= new List<uint>()).Add(sequence);
        }
        if (stale is null) return;

        foreach (var sequence in stale)
        {
            if (!OutputBackup.Remove(sequence, out var entry)) continue;
            // Same "resend as-backed-up" requirement as HandleNack: QueueFrameLocked re-batches
            // into a fresh FrameSet without re-deriving reliability identity.
            foreach (var frame in entry.Frames)
                QueueFrameLocked(frame, Priority.Immediate);
        }
    }

    /// <summary>Drains a pending ACK/NACK sequence set into its packet and sends it - ACK and
    /// NACK are otherwise handled identically (same AcknowledgePacket shape, same
    /// build-clear-send sequence), only the packet type and source set differ.
    /// Caller must hold <see cref="_sessionLock"/>.</summary>
    private void FlushAcknowledgeLocked<T>(HashSet<uint> sequences) where T : AcknowledgePacket, new()
    {
        if (sequences.Count == 0) return;

        var packet = new T { Sequences = sequences.ToList() };
        sequences.Clear();
        Server.Send(EndPoint, packet.Encode());
    }

    /// <summary>Drain all pending outbound frames to UDP (call before Close / disconnect kick).</summary>
    public void FlushOutgoing()
    {
        lock (_sessionLock)
        {
            if (_closed) return;
            while (OutputFrames.Count > 0)
                SendQueueLocked(OutputFrames.Count);
        }
    }

    /// <summary>Caller must hold <see cref="_sessionLock"/>.</summary>
    private void SendQueueLocked(int count)
    {
        if (OutputFrames.Count == 0) return;

        var frameSet = new FrameSet
        {
            Sequence = OutputSequence,
            Packets = OutputFrames.Take(count).ToList()
        };
        // The wire only carries 24 bits (WriteTriad/ReadTriad) — wrapping the in-memory counter to
        // match keeps OutputBackup's keys aligned with what a peer's ACK/NACK actually reports once
        // a long-lived session crosses 2^24 FrameSets (cross-reference audit finding).
        OutputSequence = (OutputSequence + 1) & SequenceMask;

        OutputBackup[frameSet.Sequence] = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), frameSet.Packets);

        foreach (var frame in frameSet.Packets)
        {
            OutputFrames.Remove(frame);
            _outputFramesByteLength -= frame.GetByteLength();
        }

        // Encode sob o lock; UDP I/O curto mantido aqui para evitar reentrância
        // frágil com QueueFrame → SendQueue aninhados.
        Server.Send(EndPoint, frameSet.Encode());
    }

    public void SendFrame(Frame frame, Priority priority)
    {
        lock (_sessionLock)
        {
            SendFrameLocked(frame, priority);
        }
    }

    /// <summary>Caller must hold <see cref="_sessionLock"/>.</summary>
    private void SendFrameLocked(Frame frame, Priority priority)
    {
        // OrderChannel fora de 0..MAX_ORDER_CHANNELS-1 crashava nos arrays de índice.
        if (!Frame.IsValidOrderChannel(frame.OrderChannel))
        {
            Server.Logger?.Warning($"[{EndPoint}] Dropped frame with invalid OrderChannel={frame.OrderChannel}.");
            return;
        }

        if (Frame.IsSequenced(frame.Reliability))
        {
            frame.OrderIndex = OutputOrderIndex[frame.OrderChannel];
            frame.SequenceIndex = OutputSequenceIndex[frame.OrderChannel]++;
        }
        else if (Frame.IsOrdered(frame.Reliability))
        {
            frame.OrderIndex = OutputOrderIndex[frame.OrderChannel]++;
            OutputSequenceIndex[frame.OrderChannel] = 0;
        }

        // RakNet negotiates an IP-packet MTU. FrameSets travel as UDP payload,
        // so they must leave room for the 20-byte IP and 8-byte UDP headers.
        // A split frame also carries its own frame/split headers.
        var maxDatagramSize = Math.Max(MTU - IP_UDP_HEADER_SIZE, 1);
        var splitFrameOverhead = new Frame
        {
            Reliability = frame.Reliability,
            SplitInfo = new Frame.SplitPacketInfo(0, 0, 0)
        }.GetByteLength();
        var maxSize = Math.Max(maxDatagramSize - DGRAM_HEADER_SIZE - splitFrameOverhead, 1);

        if (frame.Buffer.Length > maxSize)
        {
            // Cada fragmento é um frame reliable próprio no fio (tem seu próprio ACK/NACK
            // e seu próprio slot na janela de dedup reliable do peer), então cada um precisa
            // de um MessageIndex único - reusar um só entre todos os fragmentos faz o peer
            // (que faz a mesma dedup por MessageIndex) descartar todos menos o primeiro como
            // retransmissão duplicada.
            var splitSize = (int)Math.Ceiling((double)frame.Buffer.Length / maxSize);
            var splitId = (short)(OutputSplitIndex++ % 65_536);
            for (var i = 0; i < frame.Buffer.Length; i += maxSize)
            {
                var chunkLength = Math.Min(maxSize, frame.Buffer.Length - i);
                var newFrame = new Frame
                {
                    Reliability = frame.Reliability,
                    MessageIndex = OutputReliableIndex++,
                    SequenceIndex = frame.SequenceIndex,
                    OrderIndex = frame.OrderIndex,
                    OrderChannel = frame.OrderChannel,
                    SplitInfo = new Frame.SplitPacketInfo(splitSize, splitId, i / maxSize),
                    Buffer = frame.Buffer.AsSpan(i, chunkLength).ToArray()
                };

                QueueFrameLocked(newFrame, priority);
            }
        }
        else
        {
            frame.MessageIndex = OutputReliableIndex++;
            QueueFrameLocked(frame, priority);
        }
    }

    /// <summary>Caller must hold <see cref="_sessionLock"/>.</summary>
    private void QueueFrameLocked(Frame frame, Priority priority)
    {
        var length = DGRAM_HEADER_SIZE + _outputFramesByteLength;

        // The negotiated MTU includes IP and UDP headers. Packing beyond the
        // effective UDP payload made strict RakNet peers reject oversized UDP
        // datagrams even when every individual frame had been fragmented.
        var maxDatagramSize = Math.Max(MTU - IP_UDP_HEADER_SIZE, 1);
        if (length + frame.GetByteLength() > maxDatagramSize)
            SendQueueLocked(OutputFrames.Count);

        OutputFrames.Add(frame);
        _outputFramesByteLength += frame.GetByteLength();
        if (priority == Priority.Immediate) SendQueueLocked(1);
    }

    public void Incoming(byte[] buffer)
    {
        LastSeen = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        Server.Logger?.Debug($"[{EndPoint} Incoming {buffer.Length} bytes.");

        var flag = buffer[0] & 0xf0;
        var reader = new BinaryStream(buffer, 1, buffer.Length - 1);
        switch (flag)
        {
            default:
                Server.Logger?.Debug($"[{EndPoint}] Unknown flag: {flag:X2}");
                break;
            case (byte)MessageIdentifier.Ack:
                Server.Logger?.Debug($"[{EndPoint}] Received ACK.");
                HandleAck(ref reader);
                break;
            case (byte)MessageIdentifier.Nack:
                Server.Logger?.Debug($"[{EndPoint}] Received NACK.");
                HandleNack(ref reader);
                break;
            case (byte)BitFlags.Valid:
                HandleIncomingFrameSet(ref reader);
                break;
        }
        reader.Dispose();
    }

    public void HandleAck(ref BinaryStream reader)
    {
        var ack = IPacket.From<ACK>(ref reader);
        lock (_sessionLock)
        {
            foreach (var sequence in ack.Sequences)
            {
                OutputBackup.Remove(sequence);
            }
        }
    }

    public void HandleNack(ref BinaryStream reader)
    {
        var nack = IPacket.From<NACK>(ref reader);
        lock (_sessionLock)
        {
            foreach (var sequence in nack.Sequences)
            {
                if (!OutputBackup.Remove(sequence, out var entry)) continue;
                foreach (var frame in entry.Frames)
                {
                    // Retransmit the frame AS-BACKED-UP — do not route through SendFrameLocked,
                    // which unconditionally re-derives OrderIndex/SequenceIndex/MessageIndex as if
                    // this were a brand-new logical message. `frame` here already carries the
                    // metadata from its first (and only correct) assignment in SendFrameLocked;
                    // re-deriving it on resend silently reassigns a LATER OrderIndex, permanently
                    // orphaning the original slot — every peer waiting on that exact order index
                    // (Reliable Ordered is per-channel FIFO) blocks forever, since nothing will
                    // ever arrive claiming it again. QueueFrameLocked only re-batches the frame
                    // into a fresh FrameSet/datagram (a real resend needs a new outer sequence
                    // number) without touching its reliability identity. Root cause of the
                    // smoke:respawn timeout (roadmap Yes-next priority 2) — ADR §94.
                    QueueFrameLocked(frame, Priority.Immediate);
                }
            }
        }
    }

    /// <summary>24-bit wire sequence space — <see cref="FrameSet.Sequence"/> only ever carries the
    /// low 24 bits (<c>WriteTriad</c>/<c>ReadTriad</c>).</summary>
    private const uint SequenceMask = 0xFFFFFF;
    private const uint SequenceHalfSpace = 0x800000;

    /// <summary>
    /// RFC1982-style circular "is `a` strictly newer than `b`" over the 24-bit wire sequence space.
    /// A plain numeric `&lt;`/`==` comparison (the pre-fix code) treats every FrameSet sent after a
    /// long-lived session's <see cref="OutputSequence"/> wraps past 2^24 as permanently "stale" —
    /// the receiver silently stops accepting any further input forever (cross-reference audit
    /// finding, Phase XXIII-B polish pass). Only matters after ~16.7M FrameSets on one connection,
    /// but that's an ordinary outcome for a long-running always-on server, not a hypothetical.
    /// </summary>
    private static bool IsSequenceNewer(uint a, uint b)
    {
        var diff = (a - b) & SequenceMask;
        return diff != 0 && diff < SequenceHalfSpace;
    }

    private void HandleIncomingFrameSet(ref BinaryStream reader)
    {
        var frameSet = new FrameSet();
        frameSet.Decode(ref reader);

        List<Frame>? packets = null;
        lock (_sessionLock)
        {
            // Duplicate frameset (retransmit we already saw) — drop.
            if (ReceivedFrameSequences.Contains(frameSet.Sequence)) return;

            LostFrameSequences.Remove(frameSet.Sequence);

            // Stale/old frameset (<= last accepted sequence) — drop. Per-message ordering
            // within a channel is handled separately in HandleFrame via InputOrderIndex.
            // LastInputSequence starts at -1 (sentinel: "nothing received yet") — always accept
            // the very first FrameSet regardless of its wire value.
            if (LastInputSequence >= 0 && !IsSequenceNewer(frameSet.Sequence, (uint)LastInputSequence)) return;

            ReceivedFrameSequences.Add(frameSet.Sequence);

            if (LastInputSequence >= 0)
            {
                var gap = (frameSet.Sequence - (uint)LastInputSequence) & SequenceMask;
                if (gap > 1)
                {
                    var index = ((uint)LastInputSequence + 1) & SequenceMask;
                    for (var i = 0u; i < gap - 1; i++)
                    {
                        LostFrameSequences.Add(index);
                        index = (index + 1) & SequenceMask;
                    }
                }
            }

            LastInputSequence = (int)frameSet.Sequence;
            packets = frameSet.Packets;
        }

        foreach (var packet in packets!)
        {
            HandleFrame(packet);
        }
    }

    /// <summary>
    /// Janela deslizante de MessageIndex já vistos, pra descartar retransmissões de frames
    /// reliable já processados (o mesmo MessageIndex pode chegar de novo dentro de um
    /// FrameSet com Sequence diferente, se o ACK anterior se perdeu ou chegou tarde).
    /// Fora da janela (mais velho que o início ou longe demais à frente) também é
    /// descartado - nesse segundo caso seria um MessageIndex implausível vindo de um
    /// peer malicioso/quebrado, não vale a pena guardar.
    /// </summary>
    private bool TryAcceptReliableMessage(uint messageIndex)
    {
        if (messageIndex < ReliableWindowStart || messageIndex > ReliableWindowEnd || ReliableWindow.Contains(messageIndex))
        {
            return false;
        }

        ReliableWindow.Add(messageIndex);

        if (messageIndex == ReliableWindowStart)
        {
            while (ReliableWindow.Remove(ReliableWindowStart))
            {
                ReliableWindowStart++;
                ReliableWindowEnd++;
            }
        }

        return true;
    }

    // Limites de fragmentação: sem isso, um cliente malicioso pode declarar um SplitInfo.Count
    // gigante e nunca completar a mensagem, ou abrir fragmentos com IDs diferentes em paralelo
    // sem limite, acumulando memória na sessão indefinidamente (leak/DoS por exaustão de RAM).
    private const int MAX_CONCURRENT_FRAGMENTED_MESSAGES = 32;
    private const int MAX_FRAGMENT_COUNT_PER_MESSAGE = 512;

    /// <summary>Generous relative to any real transfer — see <see cref="FragmentsQueue"/>'s doc comment.</summary>
    private const long FragmentTimeoutMs = 30_000;

    /// <summary>
    /// Not lock-protected, deliberately: <see cref="FragmentsQueue"/> (like the rest of the
    /// input-side ordering/fragment state) is only ever touched from the single UDP receive-loop
    /// thread that calls <see cref="HandleFragment"/> — sweeping here keeps that invariant instead
    /// of introducing a second, cross-thread access path guarded by a different lock.
    /// </summary>
    private void EvictStaleFragments(long now)
    {
        if (FragmentsQueue.Count == 0) return;

        List<short>? stale = null;
        foreach (var (id, entry) in FragmentsQueue)
        {
            if (now - entry.StartedAtMs < FragmentTimeoutMs) continue;
            (stale ??= new List<short>()).Add(id);
        }
        if (stale is null) return;

        foreach (var id in stale)
        {
            Server.Logger?.Warning($"[{EndPoint}] Evicted abandoned fragment reassembly id={id} after {FragmentTimeoutMs}ms.");
            FragmentsQueue.Remove(id);
        }
    }

    private bool HandleFragment(Frame frame)
    {
        if (!frame.IsSplit()) return false;

        var splitInfo = frame.SplitInfo!;
        EvictStaleFragments(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        if (splitInfo.Count <= 0 || splitInfo.Count > MAX_FRAGMENT_COUNT_PER_MESSAGE || splitInfo.Index < 0 || splitInfo.Index >= splitInfo.Count)
        {
            Server.Logger?.Warning($"[{EndPoint}] Dropped fragment with invalid split info (count={splitInfo.Count}, index={splitInfo.Index}).");
            return false;
        }

        if (FragmentsQueue.TryGetValue(splitInfo.Id, out var entry))
        {
            var fragment = entry.Parts;
            fragment[splitInfo.Index] = frame;

            if (fragment.Count != splitInfo.Count) return false;

            // Dictionary enumeration order is NOT split-index order. Concatenating via foreach
            // corrupts remounted payloads (GamePacket length-prefix decode → "need N, have M").
            // Mobile/cellular MTU often splits login/NetworkSettings; LAN desktop often does not.
            var stream = new BinaryStream();
            for (var i = 0; i < splitInfo.Count; i++)
            {
                if (!fragment.TryGetValue(i, out var part))
                {
                    Server.Logger?.Warning(
                        $"[{EndPoint}] Incomplete fragment set id={splitInfo.Id}: missing index {i}/{splitInfo.Count}.");
                    FragmentsQueue.Remove(splitInfo.Id);
                    return false;
                }

                stream.Write(part.Buffer);
            }

            var newFrame = new Frame
            {
                Reliability = frame.Reliability,
                MessageIndex = frame.MessageIndex,
                SequenceIndex = frame.SequenceIndex,
                OrderIndex = frame.OrderIndex,
                OrderChannel = frame.OrderChannel,
                Buffer = stream.GetBufferDisposing().ToArray()
            };

            FragmentsQueue.Remove(splitInfo.Id);
            return DispatchFrame(newFrame);
        }

        if (FragmentsQueue.Count >= MAX_CONCURRENT_FRAGMENTED_MESSAGES)
        {
            Server.Logger?.Warning($"[{EndPoint}] Too many concurrent fragmented messages, dropping fragment.");
            return false;
        }

        FragmentsQueue[splitInfo.Id] = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), new Dictionary<int, Frame>
        {
            [splitInfo.Index] = frame
        });
        return false;
    }

    private bool HandleIncomingBatch(byte[] buffer)
    {
        var pid = buffer[0];
        var reader = new BinaryStream(buffer, 1, buffer.Length - 1);

        Server.Logger?.Debug($"Connected PID: {pid}");

        switch (pid)
        {
            case (byte)MessageIdentifier.ConnectedPing:
                var connectedPing = IPacket.From<ConnectedPing>(ref reader);

                var connectedPong = new ConnectedPong
                {
                    SendPingTime = connectedPing.SendPingTime,
                    SendPongTime = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };

                SendFrame(new Frame
                {
                    Reliability = Reliability.ReliableOrdered,
                    OrderChannel = 0,
                    Buffer = connectedPong.Encode().ToArray()
                }, Priority.Normal);
                return true;
            case (byte)MessageIdentifier.ConnectionRequest:
                var connectionRequest = IPacket.From<ConnectionRequest>(ref reader);

                var connectionRequestAccepted = new ConnectionRequestAccepted
                {
                    Address = EndPoint,
                    SystemIndex = 0,
                    SendPingTime = connectionRequest.SendPingTime,
                    SendPongTime = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };

                SendFrame(new Frame
                {
                    Reliability = Reliability.ReliableOrdered,
                    OrderChannel = 0,
                    Buffer = connectionRequestAccepted.Encode().ToArray()
                }, Priority.Normal);
                return true;
            case (byte)MessageIdentifier.Disconnect:
                Close(DisconnectReason.ClientDisconnect);
                reader.Dispose();
                return true;
            case (byte)MessageIdentifier.Game:
                return Server.SessionListener?.HandleGamePacket(this, ref reader) ?? false;
        }

        Server.Logger?.Debug($"[{EndPoint}] Unhandled {pid}");
        reader.Dispose();
        return false;
    }

    /// <summary>
    /// Ponto de entrada por frame recebido dentro de um FrameSet. Faz a dedup de mensagens
    /// reliable (ver <see cref="ReliableWindow"/>) uma única vez por frame de fio - split
    /// parts incluídos, já que cada parte carrega seu próprio MessageIndex reliable de
    /// verdade. A reconstrução de split (<see cref="HandleFragment"/>) recicla o
    /// MessageIndex da última parte só pra fins de reliability/ordering do pacote
    /// remontado; por isso ela chama <see cref="DispatchFrame"/> diretamente em vez de
    /// voltar aqui, senão essa mesma mensagem seria rejeitada como duplicata dela mesma.
    /// </summary>
    internal bool HandleFrameForTests(Frame frame) => HandleFrame(frame);

    private bool HandleFrame(Frame frame)
    {
        // OrderChannel vem do fio como um byte cru (0-255), mas os arrays de tracking
        // (InputOrderIndex, InputHighestSequenceIndex) têm exatamente Frame.MAX_ORDER_CHANNELS
        // slots. Sem essa validação, um frame malformado/hostil com OrderChannel >= 32 derruba
        // a sessão inteira com IndexOutOfRangeException assim que qualquer código abaixo tenta
        // indexar por ele — drop instead.
        if (Frame.IsSequencedOrOrdered(frame.Reliability) && !Frame.IsValidOrderChannel(frame.OrderChannel))
        {
            Server.Logger?.Warning($"[{EndPoint}] Dropped frame with invalid order channel {frame.OrderChannel}.");
            return false;
        }

        if (Frame.IsReliable(frame.Reliability) && !TryAcceptReliableMessage(frame.MessageIndex))
        {
            return false; // duplicate/out-of-range retransmit of a reliable frame, already handled
        }

        return frame.IsSplit() ? HandleFragment(frame) : DispatchFrame(frame);
    }

    /// <summary>Roteamento por reliability (sequenced/ordered/plain), assumindo que a dedup
    /// reliable já rodou (via <see cref="HandleFrame"/>) ou não se aplica (frame remontado).</summary>
    private bool DispatchFrame(Frame frame)
    {
        if (Frame.IsSequenced(frame.Reliability))
        {
            if (frame.SequenceIndex < InputHighestSequenceIndex[frame.OrderChannel] || frame.OrderIndex < InputOrderIndex[frame.OrderChannel])
            {
                return false;
            }

            InputHighestSequenceIndex[frame.OrderChannel] = frame.SequenceIndex + 1;
            return HandleIncomingBatch(frame.Buffer);
        }

        if (!Frame.IsOrdered(frame.Reliability)) return HandleIncomingBatch(frame.Buffer);
        
        if (frame.OrderIndex == InputOrderIndex[frame.OrderChannel])
        {
            InputHighestSequenceIndex[frame.OrderChannel] = 0;
            InputOrderIndex[frame.OrderChannel] = frame.OrderIndex + 1;

            HandleIncomingBatch(frame.Buffer);
            var index = InputOrderIndex[frame.OrderChannel];

            var outOfOrderQueue = InputOrderingQueue[frame.OrderChannel];

            for (; outOfOrderQueue.ContainsKey(index); index++)
            {
                if (outOfOrderQueue.TryGetValue(index, out var frameQueue))
                {
                    HandleIncomingBatch(frameQueue.Buffer);
                    outOfOrderQueue.Remove(index);
                }
                else break;
            }

            InputOrderingQueue[frame.OrderChannel] = outOfOrderQueue;
            InputOrderIndex[frame.OrderChannel] = index;
            return true;
        }

        if (frame.OrderIndex <= InputOrderIndex[frame.OrderChannel]) return false; // old/duplicate, discard
        if (!InputOrderingQueue.TryGetValue(frame.OrderChannel, out var unordered)) return true;
        unordered[frame.OrderIndex] = frame; // out of order: hold it until the gap is filled, don't handle yet
        return true;

    }
}
