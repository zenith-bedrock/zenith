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
    public const int DGRAM_MTU_OVERHEAD = 36;

    public required IPEndPoint EndPoint { get; init; }
    public int Id { get; init; }
    public required RakNetServer Server { get; init; }
    public required ushort MTU { get; init; }
    public long LastSeen { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    protected readonly HashSet<uint> ReceivedFrameSequences = new();
    protected readonly HashSet<uint> LostFrameSequences = new();
    protected readonly uint[] InputHighestSequenceIndex = new uint[32];
    protected readonly Dictionary<short, Dictionary<int, Frame>> FragmentsQueue = new();

    protected readonly uint[] InputOrderIndex = new uint[32];
    protected readonly Dictionary<byte, Dictionary<uint, Frame>> InputOrderingQueue = new();
    protected int LastInputSequence = -1;

    // Janela deslizante de dedup para qualquer frame reliable (Reliable, ReliableOrdered,
    // ReliableSequenced, ...), independente de reliability específica. Um frame reliable pode
    // ser retransmitido dentro de um FrameSet novo (sequence diferente) quando o servidor
    // não confirma a tempo; sem isso, o mesmo MessageIndex é processado de novo e pode
    // duplicar efeitos do lado do jogo (ex: LoginPacket reprocessado -> PlayerManager.TryAdd
    // falha achando que já tem alguém logado -> Disconnect indevido). Espelha
    // reliableWindowStart/End/reliableWindow do RakLib (ReceiveReliabilityLayer.php).
    private const uint RELIABLE_WINDOW_SIZE = 2048;
    protected uint ReliableWindowStart;
    protected uint ReliableWindowEnd = RELIABLE_WINDOW_SIZE;
    protected readonly HashSet<uint> ReliableWindow = new();

    protected readonly uint[] OutputOrderIndex = new uint[32];
    protected readonly uint[] OutputSequenceIndex = new uint[32];

    protected readonly HashSet<Frame> OutputFrames = new();
    protected readonly Dictionary<uint, List<Frame>> OutputBackup = new();

    // Mantido em paralelo a OutputFrames em vez de recalculado via LINQ Sum a cada
    // QueueFrame: eram O(n) por chamada (O(n²) num burst de frames), e esse é
    // literalmente o hot path de todo Send/SendFrame.
    private int _outputFramesByteLength;

    protected uint OutputSequence;
    protected int OutputSplitIndex;
    protected uint OutputReliableIndex;

    private bool _closed;

    public RakNetSession()
    {
        for (byte index = 0; index < 32; index++)
        {
            InputOrderingQueue[index] = new();
        }
    }

    public void Disconnect(DisconnectReason reason = DisconnectReason.ServerDisconnect)
    {
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

        if (ReceivedFrameSequences.Count > 0)
        {
            var ack = new ACK
            {
                Sequences = ReceivedFrameSequences.ToList()
            };
            ReceivedFrameSequences.Clear();
            Server.Send(EndPoint, ack.Encode());
        }

        if (LostFrameSequences.Count > 0)
        {
            var nack = new NACK
            {
                Sequences = LostFrameSequences.ToList()
            };
            LostFrameSequences.Clear();
            Server.Send(EndPoint, nack.Encode());
        }

        SendQueue(OutputFrames.Count);
    }

    private void SendQueue(int count)
    {
        if (OutputFrames.Count == 0) return;

        var frameSet = new FrameSet
        {
            Sequence = OutputSequence++,
            Packets = OutputFrames.Take(count).ToList()
        };

        OutputBackup[frameSet.Sequence] = frameSet.Packets;

        foreach (var frame in frameSet.Packets)
        {
            OutputFrames.Remove(frame);
            _outputFramesByteLength -= frame.GetByteLength();
        }

        Server.Send(EndPoint, frameSet.Encode());
    }

    public void SendFrame(Frame frame, Priority priority)
    {
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

        var maxSize = Math.Max(MTU - 36, 1);
        var splitSize = (int)Math.Ceiling((double)frame.Buffer.Length / maxSize);

        if (frame.Buffer.Length > maxSize)
        {
            // Cada fragmento é um frame reliable próprio no fio (tem seu próprio ACK/NACK
            // e seu próprio slot na janela de dedup reliable do peer), então cada um precisa
            // de um MessageIndex único - reusar um só entre todos os fragmentos faz o peer
            // (que faz a mesma dedup por MessageIndex) descartar todos menos o primeiro como
            // retransmissão duplicada.
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

                QueueFrame(newFrame, priority);
            }
        }
        else
        {
            frame.MessageIndex = OutputReliableIndex++;
            QueueFrame(frame, priority);
        }
    }

    private void QueueFrame(Frame frame, Priority priority)
    {
        var length = DGRAM_HEADER_SIZE + _outputFramesByteLength;

        if (length + frame.GetByteLength() > MTU + DGRAM_MTU_OVERHEAD) SendQueue(OutputFrames.Count);

        OutputFrames.Add(frame);
        _outputFramesByteLength += frame.GetByteLength();
        if (priority == Priority.Immediate) SendQueue(1);
    }

    public void Incoming(byte[] buffer)
    {
        LastSeen = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        Server.Logger?.Debug($"[{EndPoint} Incoming {buffer.Length} bytes.");

        var flag = buffer[0] & 0xf0;
        var reader = new BinaryStream(buffer[1..]);
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
        foreach (var sequence in ack.Sequences)
        {
            OutputBackup.Remove(sequence);
        }
    }

    public void HandleNack(ref BinaryStream reader)
    {
        var nack = IPacket.From<NACK>(ref reader);
        foreach (var sequence in nack.Sequences)
        {
            if (!OutputBackup.TryGetValue(sequence, out var frames)) continue;
            foreach (var frame in frames)
            {
                SendFrame(frame, Priority.Immediate);
            }
        }
    }

    private void HandleIncomingFrameSet(ref BinaryStream reader)
    {
        var frameSet = new FrameSet();
        frameSet.Decode(ref reader);

        if (ReceivedFrameSequences.Contains(frameSet.Sequence)) return; // TODO: duplicate framesets

        LostFrameSequences.Remove(frameSet.Sequence);

        if (frameSet.Sequence < LastInputSequence || frameSet.Sequence == LastInputSequence) return; // TODO: out of order

        ReceivedFrameSequences.Add(frameSet.Sequence);

        if (frameSet.Sequence - LastInputSequence > 1)
        {
            for (
                var index = (uint)(LastInputSequence + 1);
                index < frameSet.Sequence;
                index++
            ) LostFrameSequences.Add(index);
        }

        LastInputSequence = (int)frameSet.Sequence;

        foreach (var packet in frameSet.Packets)
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
    /// peer malicioso/quebrado, não vale a pena guardar. Espelha reliableWindowStart/
    /// reliableWindowEnd/reliableWindow do RakLib (ReceiveReliabilityLayer::handleEncapsulatedPacket).
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

    private bool HandleFragment(Frame frame)
    {
        if (!frame.IsSplit()) return false;

        var splitInfo = frame.SplitInfo!;

        if (splitInfo.Count <= 0 || splitInfo.Count > MAX_FRAGMENT_COUNT_PER_MESSAGE || splitInfo.Index < 0 || splitInfo.Index >= splitInfo.Count)
        {
            Server.Logger?.Warning($"[{EndPoint}] Dropped fragment with invalid split info (count={splitInfo.Count}, index={splitInfo.Index}).");
            return false;
        }

        if (FragmentsQueue.TryGetValue(splitInfo.Id, out var fragment))
        {
            fragment[splitInfo.Index] = frame;

            if (fragment.Count != splitInfo.Count) return false;
            var stream = new BinaryStream();
            foreach (var frag in fragment) stream.Write(frag.Value.Buffer);

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

        FragmentsQueue[splitInfo.Id] = new()
        {
            [splitInfo.Index] = frame
        };
        return false;
    }

    private bool HandleIncomingBatch(byte[] buffer)
    {
        var pid = buffer[0];
        var reader = new BinaryStream(buffer[1..]);

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
    private bool HandleFrame(Frame frame)
    {
        // OrderChannel vem do fio como um byte cru (0-255), mas os arrays de tracking
        // (InputOrderIndex, InputHighestSequenceIndex) têm exatamente Frame.MAX_ORDER_CHANNELS
        // slots. Sem essa validação, um frame malformado/hostil com OrderChannel >= 32 derruba
        // a sessão inteira com IndexOutOfRangeException assim que qualquer código abaixo tenta
        // indexar por ele. RakLib rejeita a mesma condição (ver ReceiveReliabilityLayer, "bad
        // order channel").
        if (Frame.IsSequencedOrOrdered(frame.Reliability) && frame.OrderChannel >= Frame.MAX_ORDER_CHANNELS)
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