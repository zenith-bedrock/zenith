using Zenith.Packets.Generation;
using Zenith.Raknet.Stream;

namespace Zenith.Packets;

[GamePacket((int)ProtocolInfo.NETWORK_SETTINGS_PACKET)]
sealed partial class NetworkSettingsPacket : DataPacket
{
    public const byte COMPRESS_NOTHING = 0;
    public const byte COMPRESS_EVERYTHING = 1;

    [Wire(BinaryStream.Endianess.Little)]
    public short CompressionThreshold { get; set; }

    [Wire(BinaryStream.Endianess.Little)]
    public short CompressionAlgorithm { get; set; }

    [Wire]
    public bool EnableClientThrottling { get; set; }

    [Wire]
    public byte ClientThrottleThreshold { get; set; }

    [Wire(BinaryStream.Endianess.Little)]
    public float ClientThrottleScalar { get; set; }
}
