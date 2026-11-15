using DataChannelDotnet;
using DataChannelDotnet.Events;
using Zenith.NetherNet.Axolotl.Transport;
using Zenith.NetherNet.Axolotl.Transport.WebRtc;
using Xunit;

namespace Zenith.NetherNet.Tests.Axolotl;

public sealed class WebRtcAdmissionTests
{
    private sealed class NativeChannel : IRtcDataChannel
    {
        public bool Disposed;
        public int Subscriptions;
        public bool IsOpen => !Disposed;
        public bool IsClosed => Disposed;
        public string Label => "ReliableDataChannel";
        public event Action<IRtcDataChannel>? OnOpen { add { } remove { } }
        public event Action<IRtcDataChannel>? OnClose { add { } remove { } }
        public event Action<IRtcDataChannel, string?>? OnError { add { } remove { } }
        public event Action<IRtcDataChannel, RtcTextReceivedEvent>? OnTextReceived { add { } remove { } }
        public event Action<IRtcDataChannel, RtcTextReceivedEventSafe>? OnTextReceivedSafe { add { } remove { } }
        public event Action<IRtcDataChannel, RtcBinaryReceivedEventSafe>? OnBinaryReceivedSafe
        { add { Subscriptions++; } remove { Subscriptions--; } }
        public void Send(ReadOnlySpan<byte> bytes) { }
        public void Send(string text) { }
        public void Dispose() => Disposed=true;
    }
    private static ConnectionBudgetConfiguration Limits(int bytes=16, int messages=2) =>
        new(maxMessageSize:16, maxReceiveQueueSize:bytes, maxReceiveQueueMessages:messages, maxPendingDataChannels:2);

    [Fact]
    public void Pre_session_receive_budget_is_shared_across_channels_before_copy()
    {
        var budget=new WebRtcReceiveBudget(Limits());
        var a=new NativeChannel(); var b=new NativeChannel();
        var first=new WebRtcMessageChannel(a,16,budget); var second=new WebRtcMessageChannel(b,16,budget);
        first.AcceptMessage(new byte[8]); second.AcceptMessage(new byte[8]);
        first.AcceptMessage(new byte[1]); // no tick or Session exists
        Assert.True(budget.Failed); Assert.True(first.IsClosed); Assert.True(second.IsClosed);
        Assert.Equal(16,first.AvailableAmount+second.AvailableAmount);
        Assert.Equal(2,first.QueuedMessageCount+second.QueuedMessageCount);
        Assert.False(a.Disposed); Assert.False(b.Disposed); // callback records failure, owner closes
        first.Close(); second.Close();
        Assert.True(a.Disposed); Assert.True(b.Disposed); Assert.Equal(0,a.Subscriptions);
        Assert.Equal(0,first.AvailableAmount+second.AvailableAmount);
    }

    [Fact]
    public void Draining_releases_capacity_and_preserves_first_packet_bytes()
    {
        var budget=new WebRtcReceiveBudget(Limits());
        var native=new NativeChannel(); var channel=new WebRtcMessageChannel(native,16,budget);
        var bytes=new byte[] { 1,2,3 };
        channel.AcceptMessage(bytes); bytes[0]=9;
        Assert.Equal(new byte[] { 1,2,3 },channel.Receive());
        channel.AcceptMessage(new byte[16]);
        Assert.False(budget.Failed); Assert.Equal(16,channel.AvailableAmount);
        channel.Close(); channel.Close();
        Assert.Equal(0,channel.QueuedMessageCount);
    }

    [Fact]
    public void Oversized_message_and_excess_pending_channels_fail_before_retention()
    {
        var budget=new WebRtcReceiveBudget(Limits());
        Assert.True(budget.TryAddChannel()); Assert.True(budget.TryAddChannel());
        Assert.False(budget.TryAddChannel()); Assert.True(budget.Failed);
        var messageBudget=new WebRtcReceiveBudget(Limits());
        var channel=new WebRtcMessageChannel(new NativeChannel(),16,messageBudget);
        channel.AcceptMessage(new byte[17]);
        Assert.True(messageBudget.Failed); Assert.Equal(0,channel.QueuedMessageCount);
        channel.Close();
    }

    [Fact]
    public void Message_count_budget_is_enforced_even_for_tiny_messages()
    {
        var budget=new WebRtcReceiveBudget(Limits(messages:1));
        var channel=new WebRtcMessageChannel(new NativeChannel(),16,budget);
        channel.AcceptMessage([1]); channel.AcceptMessage([2]);
        Assert.True(budget.Failed); Assert.Equal(1,channel.QueuedMessageCount);
        channel.Close();
    }
}
