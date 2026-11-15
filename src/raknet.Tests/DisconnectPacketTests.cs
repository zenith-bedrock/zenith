using Xunit;
using Zenith.Raknet.Network.Protocol;

namespace raknet.Tests;

public class DisconnectPacketTests
{
    [Fact]
    public void Disconnect_is_not_a_truncated_connected_pong()
    {
        Assert.Equal(new byte[] { 0x15 }, new Disconnect().Encode().ToArray());
        var pong = new ConnectedPong { SendPingTime = 1, SendPongTime = 2 }.Encode().ToArray();
        Assert.Equal((byte)0x03, pong[0]); Assert.Equal(17, pong.Length);
    }
}
