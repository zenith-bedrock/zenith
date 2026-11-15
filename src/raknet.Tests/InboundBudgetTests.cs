using System.Net;
using Zenith.Binary;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Network;
using Zenith.Raknet.Network.Protocol;
using Xunit;

namespace Zenith.Raknet.Tests;

public sealed class InboundBudgetTests
{
    private sealed class Listener : IRakNetSessionListener
    {
        public int Delivered;
        public bool HandleGamePacket(RakNetSession session, ref BinaryStream stream) { Delivered++; return true; }
    }
    private sealed class Server : RakNetServer
    {
        public Server() : base(0) { }
        public override void Send(IPEndPoint endpoint, ReadOnlySpan<byte> bytes) { }
    }
    private sealed class Probe : RakNetSession
    {
        public int Lost => LostFrameSequences.Count;
        public int Ordered => InputOrderingQueue.Values.Sum(q => q.Count);
        public void Seed(uint value) { ReliableWindowStart=value; ReliableWindowEnd=(value+2048)&0xFFFFFF; InputOrderIndex[0]=value; }
        public void Backup(uint value) => UnacknowledgedFrameSets[value]=(0, []);
        public bool HasBackup(uint value) => UnacknowledgedFrameSets.ContainsKey(value);
        public void SeedSequenced(uint value) { Seed(value); InputHighestSequenceIndex[0]=value; }
    }
    private static Probe Session(Listener? listener = null) => new()
    {
        Server = new Server { SessionListener=listener }, EndPoint=new IPEndPoint(IPAddress.Loopback, 19190), Id=1, MTU=1400
    };
    private static Frame Frame(uint message, uint order, int size=2) => new()
    {
        Reliability=Reliability.ReliableOrdered, MessageIndex=message, OrderIndex=order, OrderChannel=0,
        Buffer = new byte[size]
    };
    private static Frame Game(uint message, uint order) => new()
    {
        Reliability=Reliability.ReliableOrdered, MessageIndex=message, OrderIndex=order,
        Buffer=new byte[] { 0xfe, 1 }
    };

    [Theory]
    [InlineData(1000u, 1001u)]
    [InlineData(0xfffffeu, 0xffffffu)]
    public void Ack_ranges_never_confirm_more_than_the_peer_sent(uint start, uint end)
    {
        var w=new BinaryStream(); w.WriteUShort(1); w.WriteByte(0);
        w.WriteTriad(start, BinaryStream.Endianess.Little); w.WriteTriad(end, BinaryStream.Endianess.Little);
        var bytes=w.GetBufferDisposing().ToArray(); var r=new BinaryStream(bytes);
        var ack=new ACK(); ack.Decode(ref r);
        Assert.Equal([start,end], ack.Sequences); Assert.True(r.IsEndOfFile);
        var session=Session(); session.Backup(start); session.Backup(end); session.Backup(end+1);
        var input=new BinaryStream(bytes); session.HandleAck(ref input);
        Assert.False(session.HasBackup(start)); Assert.False(session.HasBackup(end)); Assert.True(session.HasBackup(end+1));
    }

    [Theory]
    [InlineData(100u, 99u)]
    [InlineData(0u, 4096u)]
    public void Ack_rejects_inverted_or_oversized_ranges(uint start, uint end)
    {
        var w=new BinaryStream(); w.WriteUShort(1); w.WriteByte(0);
        w.WriteTriad(start,BinaryStream.Endianess.Little); w.WriteTriad(end,BinaryStream.Endianess.Little);
        var bytes=w.GetBufferDisposing().ToArray();
        Assert.Throws<InvalidDataException>(() => DecodeAck(bytes));
    }
    private static void DecodeAck(byte[] bytes) { var r=new BinaryStream(bytes); new ACK().Decode(ref r); }

    [Fact]
    public void Aggregate_ack_budget_rejects_before_applying_any_backup_removal()
    {
        var w=new BinaryStream(); w.WriteUShort(2);
        w.WriteByte(0); w.WriteTriad(0,BinaryStream.Endianess.Little); w.WriteTriad(3000,BinaryStream.Endianess.Little);
        w.WriteByte(0); w.WriteTriad(5000,BinaryStream.Endianess.Little); w.WriteTriad(7000,BinaryStream.Endianess.Little);
        var bytes=w.GetBufferDisposing().ToArray(); var s=Session(); s.Backup(1);
        Assert.Throws<InvalidDataException>(() => ApplyAck(s,bytes));
        Assert.True(s.HasBackup(1));
    }
    private static void ApplyAck(Probe session,byte[] bytes) { var r=new BinaryStream(bytes); session.HandleAck(ref r); }

    [Fact]
    public void Sequenced_index_wrap_keeps_newest_messages_deliverable()
    {
        var listener=new Listener(); var s=Session(listener); s.SeedSequenced(0xfffffe);
        foreach(var n in new uint[] {0xfffffe,0xffffff,0})
            Assert.True(s.HandleFrameForTests(new Frame
            {
                Reliability=Reliability.ReliableSequenced, MessageIndex=n, OrderIndex=0xfffffe,
                SequenceIndex=n, Buffer=new byte[] {0xfe,1}
            }));
        Assert.Equal(3,listener.Delivered);
    }

    [Fact]
    public void Large_datagram_gap_closes_before_materializing_missing_sequences()
    {
        var s=Session();
        s.Incoming(new FrameSet { Sequence=0, Packets=[] }.Encode().ToArray());
        s.Incoming(new FrameSet { Sequence=10000, Packets=[] }.Encode().ToArray());
        Assert.True(s.IsClosed); Assert.Equal(0,s.Lost);
    }

    [Fact]
    public void Missing_first_ordered_index_cannot_grow_the_queue_forever()
    {
        var s=Session();
        for(uint n=0;n<=RakNetSession.MaxOrderedFrames;n++) s.HandleFrameForTests(Game(n,n+1));
        Assert.True(s.IsClosed); Assert.InRange(s.Ordered,0,RakNetSession.MaxOrderedFrames);
    }

    [Fact]
    public void Ordered_bytes_are_bounded_independently_of_frame_count()
    {
        var s=Session();
        for(uint n=0;n<10;n++) s.HandleFrameForTests(Frame(n,n+1,1024*1024));
        Assert.True(s.IsClosed); Assert.InRange(s.Ordered,0,8);
    }

    [Fact]
    public void Reliable_and_ordered_windows_deliver_across_wrap_including_reordering()
    {
        var listener=new Listener(); var s=Session(listener); s.Seed(0xfffffe);
        Assert.True(s.HandleFrameForTests(Game(0xffffff,0xffffff))); // ahead, retained
        Assert.True(s.HandleFrameForTests(Game(0xfffffe,0xfffffe))); // drains both
        Assert.True(s.HandleFrameForTests(Game(0,0)));
        Assert.True(s.HandleFrameForTests(Game(1,1)));
        Assert.False(s.HandleFrameForTests(Game(0xffffff,0xffffff)));
        Assert.Equal(4,listener.Delivered); Assert.Equal(0,s.Ordered);
    }
}
