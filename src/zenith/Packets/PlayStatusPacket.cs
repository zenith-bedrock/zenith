using Zenith.Packets.Generation;

namespace Zenith.Packets;

[GamePacket((int)ProtocolInfo.PLAY_STATUS_PACKET)]
sealed partial class PlayStatusPacket : DataPacket
{
    public const int LoginSuccess = 0;
    public const int LoginFailedClient = 1; // client protocol < server
    public const int LoginFailedServer = 2; // client protocol > server
    public const int PlayerSpawn = 3;

    [Wire]
    public int Status { get; set; }
}
