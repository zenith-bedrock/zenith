using DataChannelDotnet;
using DataChannelDotnet.Data;
using DataChannelDotnet.Events;
using DataChannelDotnet.Impl;
using Xunit;
using Zenith.NetherNet;

namespace Zenith.NetherNet.Tests;

public sealed class NetherNetConnectionTests
{
    [Theory]
    [InlineData("ReliableDataChannel", false)]
    [InlineData("ReliableDataChannel", true)]
    [InlineData("UnreliableDataChannel", false)]
    [InlineData("UnreliableDataChannel", true)]
    public async Task DataChannel_terminal_callback_after_disposal_closes_once_with_original_label(
        string label, bool error)
    {
        await using var connection = new NetherNetConnection(
            "network-id", null,
            new RtcPeerConnection(new RtcPeerConfiguration
            {
                IceServers = [],
                BindAddress = "127.0.0.1",
                DisableAutoNegotiation = true
            }),
            maximumMessageSize: 1024,
            maximumFragmentSize: 128);
        var channel = new TestDataChannel(label);
        var closeReasons = new List<string>();
        connection.Closed += (_, reason) => closeReasons.Add(reason);
        connection.AddDataChannel(channel);
        Assert.Equal(1, channel.LabelReads);

        channel.Dispose();
        Assert.Null(Record.Exception(() =>
        {
            if (error)
                channel.RaiseError("transport failed");
            else
                channel.RaiseClosed();
        }));

        var expectedReason = error
            ? $"Data channel '{label}' failed: transport failed"
            : $"Data channel '{label}' closed.";
        Assert.True(connection.IsClosed);
        Assert.Equal(expectedReason, connection.CloseReason);
        Assert.Equal(expectedReason, Assert.Single(closeReasons));

        Assert.Null(Record.Exception(() =>
        {
            channel.RaiseClosed();
            channel.RaiseError("late error");
        }));
        Assert.Equal(expectedReason, connection.CloseReason);
        Assert.Equal(expectedReason, Assert.Single(closeReasons));
        Assert.Equal(1, channel.LabelReads);
    }

    private sealed class TestDataChannel(string label) : IRtcDataChannel
    {
        private bool _disposed;
        public int LabelReads { get; private set; }
        public bool IsOpen => false;
        public bool IsClosed => _disposed;
        public string Label
        {
            get
            {
                LabelReads++;
                ObjectDisposedException.ThrowIf(_disposed, typeof(string));
                return label;
            }
        }

        public event Action<IRtcDataChannel>? OnOpen { add { } remove { } }
        public event Action<IRtcDataChannel>? OnClose;
        public event Action<IRtcDataChannel, string?>? OnError;
        public event Action<IRtcDataChannel, RtcTextReceivedEvent>? OnTextReceived { add { } remove { } }
        public event Action<IRtcDataChannel, RtcTextReceivedEventSafe>? OnTextReceivedSafe { add { } remove { } }
        public event Action<IRtcDataChannel, RtcBinaryReceivedEventSafe>? OnBinaryReceivedSafe { add { } remove { } }

        public void RaiseClosed() => OnClose?.Invoke(this);
        public void RaiseError(string error) => OnError?.Invoke(this, error);
        public void Send(ReadOnlySpan<byte> data) => throw new NotSupportedException();
        public void Send(string data) => throw new NotSupportedException();
        public void Dispose() => _disposed = true;
    }
}
