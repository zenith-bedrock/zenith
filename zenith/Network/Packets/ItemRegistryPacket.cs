using Zenith.Nbt;
using Zenith.Raknet.Stream;
using Zenith.Server;
using Zenith.World;

namespace Zenith.Network.Packets;

/// <summary>
/// ItemRegistry (0xa2) — full item palette for the client after StartGame.
/// Wire layout valid for <see cref="ServerIdentity.ProtocolVersion"/> (766 / Bedrock ~1.26.33).
/// On protocol bump, review this packet before changing World/Inventory domain rules.
/// </summary>
sealed class ItemRegistryPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.ITEM_REGISTRY_PACKET;

    public IReadOnlyList<ItemPaletteEntry> Entries { get; set; } = Array.Empty<ItemPaletteEntry>();

    public override Span<byte> Encode()
    {
        var emptyComponent = NbtCodec.Encode(
            new NbtNamedTag("", NbtTag.Compound(new NbtCompound())),
            NbtEncoding.Network);

        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteUnsignedVarInt(Entries.Count);
        foreach (var entry in Entries)
        {
            writer.WriteVarString(entry.Name);
            writer.WriteShort(entry.NetworkId, BinaryStream.Endianess.Little);
            writer.WriteBool(entry.ComponentBased);
            writer.WriteVarInt(entry.Version);
            writer.Write(emptyComponent);
        }

        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}
