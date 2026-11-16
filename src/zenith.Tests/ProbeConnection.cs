using Zenith.Binary;
using Zenith.Network;
using Zenith.Packets;

namespace Zenith.Tests;

/// <summary>
/// A client as the server sees one: an <see cref="IGameTransportConnection"/> that keeps every batch the server sends, plus
/// the batch framing a real client would use to answer. Wired to a real <see cref="GameSessionHost"/>, so a packet sent here travels
/// the same path as one off the wire (framing, compression, session handler, intents) and what comes back is what a client would receive.
/// </summary>
sealed class ProbeConnection : IGameTransportConnection
{
    private readonly List<byte[]> _sent = [];
    private readonly GameSessionHost _host;
    private bool _compressionNegotiated;

    public ProbeConnection(GameSessionHost host, string name = "probe")
    {
        _host = host;
        EndPoint = name;
        if (!host.TryOpen(this)) throw new InvalidOperationException("The host refused the probe connection.");
    }

    public string EndPoint { get; }
    public bool IsClosed { get; private set; }
    public bool HasGameIdentity { get; set; }
    public TransportIdentity? TransportIdentity => null;
    public bool RequiresTransportIdentityBinding => false;
    public bool IncludesGamePacketIdentifier => false;

    public void SendGameBatch(ReadOnlyMemory<byte> batch, GameTransportPriority priority, byte orderChannel) => Keep(batch);
    public void SendUnreliableGameBatch(ReadOnlyMemory<byte> batch) => Keep(batch);

    // The world stream worker sends from its own thread, as a real transport's callers do.
    private void Keep(ReadOnlyMemory<byte> batch)
    {
        lock (_sent) _sent.Add(batch.ToArray());
    }
    public void FlushOutgoing() { }
    public void Disconnect() => IsClosed = true;

    /// <summary>The peer going away: the transport tells the host the connection closed.</summary>
    public void Close(GameTransportCloseReason reason = GameTransportCloseReason.ClientDisconnect) => _host.Close(this, reason);

    /// <summary>Sends packets to the server in one batch, framed the way the session expects before and after NetworkSettings.</summary>
    public void Send(params DataPacket[] packets)
    {
        var payload = new BinaryStream();
        foreach (var packet in packets)
        {
            var body = packet.Encode();
            payload.WriteUnsignedVarInt(body.Length);
            payload.Write(body);
        }

        var bytes = payload.GetBufferDisposing().ToArray();
        var framed = _compressionNegotiated ? [PacketCompression.NONE, .. bytes] : bytes;
        var stream = new BinaryStream(framed);
        _host.Receive(this, ref stream);
    }

    /// <summary>Everything the server sent since the last call, as packet id plus the bytes after the id.</summary>
    public List<ReceivedPacket> Drain()
    {
        byte[][] batches;
        lock (_sent)
        {
            batches = [.. _sent];
            _sent.Clear();
        }

        var packets = new List<ReceivedPacket>();
        foreach (var batch in batches)
            Unbatch(batch, packets);
        return packets;
    }

    private void Unbatch(byte[] batch, List<ReceivedPacket> into)
    {
        var offset = 0;
        if (_compressionNegotiated)
        {
            var algorithm = batch[0];
            offset = 1;
            if (algorithm == PacketCompression.ZLIB)
            {
                using var inflater = new System.IO.Compression.DeflateStream(
                    new MemoryStream(batch, 1, batch.Length - 1), System.IO.Compression.CompressionMode.Decompress);
                var inflated = new MemoryStream();
                inflater.CopyTo(inflated);
                batch = inflated.ToArray();
                offset = 0;
            }
        }

        var stream = new BinaryStream(batch[offset..]);
        while (!stream.IsEndOfFile)
        {
            var length = stream.ReadUnsignedVarInt();
            var body = new BinaryStream(stream.ReadSpan(length).ToArray());
            var id = body.ReadUnsignedVarInt();
            var rest = body.ReadSpan(length - body.Offset).ToArray();
            into.Add(new ReceivedPacket(id, rest));
            if (id == (int)ProtocolInfo.NETWORK_SETTINGS_PACKET) _compressionNegotiated = true;
        }
    }

    /// <summary>After NetworkSettings the server compresses; the probe then frames its own batches with the algorithm byte.</summary>
    public void AcknowledgeCompression() => _compressionNegotiated = true;
}

readonly record struct ReceivedPacket(int Id, byte[] Body);

/// <summary>
/// A packet the client sends but the server has no encoder for (the server only ever decodes it): the id plus body bytes the
/// caller lays out by hand, exactly as the decoder reads them.
/// </summary>
delegate void BodyWriter(ref Zenith.Binary.BinaryStream writer);

sealed class ClientPacket(int id, BodyWriter body) : DataPacket
{
    public override int Id => id;

    public override Span<byte> Encode()
    {
        var writer = new Zenith.Binary.BinaryStream();
        writer.WriteUnsignedVarInt(id);
        body(ref writer);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref Zenith.Binary.BinaryStream stream) => throw new NotSupportedException();

    public static ClientPacket ResourcePackResponse(byte status) => new(
        (int)ProtocolInfo.RESOURCE_PACK_CLIENT_RESPONSE_PACKET,
        (ref Zenith.Binary.BinaryStream writer) =>
        {
            writer.WriteUnsignedVarInt(status);
            writer.WriteVarString("");
        });

    /// <summary>InventoryTransaction / ItemUse laid out as <c>InventoryTransactionPacket.Decode</c> reads it.</summary>
    public static ClientPacket UseItem(int action, int x, int y, int z, byte face, int hotbarSlot, NetworkItemStack held) => new(
        (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET,
        (ref Zenith.Binary.BinaryStream w) =>
        {
            w.WriteVarInt(0);                                   // legacy request id
            w.WriteBool(false);                                 // no legacy slots
            w.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeItemUse);
            w.WriteUnsignedVarInt(0);                           // no inventory actions
            w.WriteVarInt(action);
            w.WriteByte(0);                                     // trigger
            w.WriteVarInt(x);
            w.WriteVarInt(y);
            w.WriteVarInt(z);
            w.WriteByte(face);
            w.WriteVarInt(hotbarSlot);
            w.WriteByte(InventoryTransactionPacket.HandMainhand);
            held.WriteNetworkItemStackDescriptor(ref w);        // what the client believes it holds
            for (var i = 0; i < 6; i++) w.WriteFloat(0f, Zenith.Binary.BinaryStream.Endianess.Little);
            w.WriteUnsignedVarInt(0);                           // clicked block runtime id: none supplied
            w.WriteByte(0);
            w.WriteByte(0);
        });

    public static ClientPacket DestroyBlock(int x, int y, int z, NetworkItemStack held, byte face = 1) =>
        UseItem(InventoryTransactionPacket.UseDestroyBlock, x, y, z, face, 0, held);

    /// <summary>Right-click on the face of block (x,y,z): place what is held against it.</summary>
    public static ClientPacket ClickBlock(int x, int y, int z, byte face, NetworkItemStack held, int hotbarSlot = 0) =>
        UseItem(InventoryTransactionPacket.UseClickBlock, x, y, z, face, hotbarSlot, held);
}
