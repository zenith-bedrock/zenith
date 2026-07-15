using System.Net;
using Xunit;
using Zenith.Raknet;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Network;
using Zenith.Raknet.Stream;

namespace Zenith.Raknet.Tests;

file sealed class CapturingListener : IRakNetSessionListener
{
    public byte[]? LastGameBody { get; private set; }

    public bool HandleGamePacket(RakNetSession session, ref BinaryStream stream)
    {
        LastGameBody = stream.Buffer[stream.Offset..stream.Length].ToArray();
        stream.Dispose();
        return true;
    }
}

/// <summary>
/// Mobile/cellular MTU often delivers GamePacket (0xFE) as split parts in non-index order.
/// Reassembly must concatenate by SplitIndex, not Dictionary enumeration order.
/// </summary>
public class FragmentReassemblyTests
{
    [Fact]
    public void Split_parts_arriving_out_of_order_reassemble_in_index_order()
    {
        var server = new RakNetServer(0);
        var listener = new CapturingListener();
        server.SessionListener = listener;

        var session = new RakNetSession
        {
            EndPoint = new IPEndPoint(IPAddress.Loopback, 19133),
            Id = 1,
            Server = server,
            MTU = 576
        };

        // Body after 0xFE — identifiable pattern; wrong concat order would scramble it.
        var body = new byte[12];
        for (var i = 0; i < body.Length; i++)
            body[i] = (byte)(0xA0 + i);

        var full = new byte[1 + body.Length];
        full[0] = (byte)MessageIdentifier.Game;
        body.CopyTo(full, 1);

        // Three parts: [0xFE,A0,A1,A2] [A3..A7] [A8..AB]
        var parts = new[]
        {
            full.AsSpan(0, 4).ToArray(),
            full.AsSpan(4, 5).ToArray(),
            full.AsSpan(9, 4).ToArray()
        };
        Assert.Equal(full.Length, parts.Sum(p => p.Length));

        const short splitId = 7;
        // Deliver index 2, then 0, then 1 (HashDictionary-hostile order).
        var deliveryOrder = new[] { 2, 0, 1 };
        uint messageIndex = 10;
        foreach (var index in deliveryOrder)
        {
            var ok = session.HandleFrameForTests(new Frame
            {
                Reliability = Reliability.ReliableOrdered,
                MessageIndex = messageIndex++,
                OrderIndex = 0,
                OrderChannel = 0,
                SplitInfo = new Frame.SplitPacketInfo(Count: 3, Id: splitId, Index: index),
                Buffer = parts[index]
            });
            if (index != 1)
                Assert.False(ok); // not complete yet
        }

        Assert.NotNull(listener.LastGameBody);
        Assert.Equal(body, listener.LastGameBody);
    }
}
