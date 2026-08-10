using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// ResourcePackClientResponse (0x08) — hand-written (not [GamePacket]): the real shape needs a
/// status-name string always present plus a conditional pack-id array the generator can't
/// express (ADR §90).
/// </summary>
sealed class ResourcePackClientResponsePacket : DataPacket
{
    // 0-indexed on the wire (Mojang ResourcePackResponse: Cancel/Downloading/DownloadingFinished/
    // ResourcePackStackFinished = 0..3; confirmed against gophertunnel and minecraft-data too —
    // found via zenith-smoke-bot: the old 1-indexed values silently stalled every join at the
    // resource-pack handshake, since "completed" (real value 3) matched STATUS_HAVE_ALL_PACKS here.
    public const byte STATUS_REFUSED = 0;
    public const byte STATUS_SEND_PACKS = 1;
    public const byte STATUS_HAVE_ALL_PACKS = 2;
    public const byte STATUS_COMPLETED = 3;

    public override int Id => (int)ProtocolInfo.RESOURCE_PACK_CLIENT_RESPONSE_PACKET;

    public byte Status { get; set; }
    public string[] ResourcePackIds { get; set; } = [];

    public override Span<byte> Encode() => Array.Empty<byte>();

    public override void Decode(ref BinaryStream stream)
    {
        Status = (byte)stream.ReadUnsignedVarInt();
        _ = stream.ReadVarString(); // response_status_name — unused, always present

        if (Status == STATUS_SEND_PACKS)
        {
            var count = stream.ReadUnsignedVarInt();
            var ids = new string[count];
            for (var i = 0; i < count; i++)
                ids[i] = stream.ReadVarString();
            ResourcePackIds = ids;
        }
        else
        {
            ResourcePackIds = [];
        }
    }
}
