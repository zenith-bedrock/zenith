namespace Zenith.Session;

/// <summary>
/// Ids do byte de compressão usado no envelope de game packets do protocolo Bedrock.
/// Antes vivia como constantes soltas dentro de SessionListener; movido pra cá porque
/// GamePacket e os handlers de sessão também precisam dele.
/// </summary>
static class PacketCompression
{
    public const byte ZLIB = 0x00;
    public const byte SNAPPY = 0x01;
    public const byte NOT_PRESENT = 0x02;
    public const byte NONE = 0xff;
}
