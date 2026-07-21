using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// BiomeDefinitionList (0x7a) — vanilla definitions after ItemRegistry / CraftingData (ADR §70).
/// Payload is the embedded wire body (biome_definitions + string_list); not custom biome JSON.
/// </summary>
sealed class BiomeDefinitionListPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.BIOME_DEFINITION_LIST_PACKET;

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.Write(BiomeDefinitionListBlob.WireBody);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}
