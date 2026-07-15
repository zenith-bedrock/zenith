namespace Zenith.Packets;

/// <summary>
/// Ids do byte de compressão do envelope de game packets Bedrock (wire format).
/// Fica em Packets para que o encoding de <see cref="GamePacket"/> não dependa de Session.
/// </summary>
static class PacketCompression
{
    public const byte ZLIB = 0x00;
    public const byte SNAPPY = 0x01;
    public const byte NOT_PRESENT = 0x02;
    public const byte NONE = 0xff;
}
