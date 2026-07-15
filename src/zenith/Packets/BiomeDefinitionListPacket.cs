using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// BiomeDefinitionList (0x7a) — Vedrock/PNX send an empty list after ItemRegistry.
/// Clients that expect this packet once can otherwise mis-handle biome/terrain rendering.
/// </summary>
sealed class BiomeDefinitionListPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.BIOME_DEFINITION_LIST_PACKET;

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteUnsignedVarInt(0); // biome_definitions
        writer.WriteUnsignedVarInt(0); // string_list
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}
