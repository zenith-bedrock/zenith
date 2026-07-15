using Zenith.Nbt;
using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>Wire DTO for ItemRegistry — domain <c>ItemPaletteEntry</c> is mapped in Protocol.</summary>
readonly record struct ItemRegistryWireEntry(string Name, short NetworkId, int Version, bool ComponentBased);

/// <summary>
/// ItemRegistry (0xa2) — full item palette for the client after StartGame.
/// Wire layout tracks the server protocol identity; bump review lives with Protocol / ServerIdentity,
/// not domain World rules.
/// </summary>
sealed class ItemRegistryPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.ITEM_REGISTRY_PACKET;

    public IReadOnlyList<ItemRegistryWireEntry> Entries { get; set; } = Array.Empty<ItemRegistryWireEntry>();

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
