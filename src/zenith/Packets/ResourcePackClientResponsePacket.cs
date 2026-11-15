using Zenith.Binary;

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
    public const int MaxPackIds = 65535;

    public override int Id => (int)ProtocolInfo.RESOURCE_PACK_CLIENT_RESPONSE_PACKET;

    public byte Status { get; set; }
    public string[] ResourcePackIds { get; set; } = [];

    public override Span<byte> Encode() => Array.Empty<byte>();

    public override void Decode(ref BinaryStream stream)
    {
        var status = stream.ReadUnsignedVarInt();
        if (status is < STATUS_REFUSED or > STATUS_COMPLETED)
            throw new InvalidDataException($"Unknown resource pack response {status}.");
        Status = (byte)status;
        _ = stream.ReadVarString(); // response_status_name — unused, always present

        if (Status == STATUS_SEND_PACKS)
        {
            var count = stream.ReadUnsignedVarInt();
            // Even an empty ID needs a length byte. Bound allocation by both the contract
            // and this packet's actual payload, not merely the surrounding batch budget.
            if (count is < 0 or > MaxPackIds || count > stream.Length - stream.Offset)
                throw new InvalidDataException($"Invalid resource pack ID count {count}.");
            var ids = new string[count];
            for (var i = 0; i < count; i++)
                ids[i] = stream.ReadVarString();
            ResourcePackIds = ids;
        }
        else
        {
            ResourcePackIds = [];
        }
        if (!stream.IsEndOfFile)
            throw new InvalidDataException("Trailing bytes after resource pack response.");
    }
}
