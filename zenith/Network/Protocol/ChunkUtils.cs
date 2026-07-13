using Zenith.Raknet.Stream;

namespace Zenith.Network.Protocol;

/// <summary>
/// Helpers to build fake "empty" (all-air) chunk column payloads. There's no real
/// World/Chunk model in Zenith yet, so this is what <see cref="LevelChunkPacket"/> sends to
/// get the client past the "Loading world" screen: no block data at all, just the minimum
/// the client's chunk parser expects to consider a column fully loaded.
/// </summary>
static class ChunkUtils
{
    /// <summary>
    /// Vertical bounds (in subchunk indices) the protocol expects for the overworld
    /// dimension. Every column sent for this dimension must carry a biome entry for each of
    /// these indices, whether or not any subchunk of actual block data is sent for it.
    /// </summary>
    private const int OverworldMinSubChunkIndex = -4;
    private const int OverworldMaxSubChunkIndex = 19;
    private const int OverworldSubChunkCount = OverworldMaxSubChunkIndex - OverworldMinSubChunkIndex + 1; // 24

    /// <summary>Legacy biome id for "plains" - picked arbitrarily, just needs to be a valid id.</summary>
    private const int PlainsBiomeId = 1;

    /// <summary>
    /// Builds the raw extra payload of a <see cref="LevelChunkPacket"/> for a completely
    /// empty (air) overworld column. Since the packet itself declares SubChunkCount = 0
    /// (no block subchunks follow), this only needs to contain:
    /// <list type="bullet">
    /// <item>one biome palette entry per subchunk index the protocol expects for this
    /// dimension (always required, independent of how many block subchunks were sent);</item>
    /// <item>the border-block section (count byte, no entries);</item>
    /// <item>the block-entity/tile section (nothing - empty column has no tiles).</item>
    /// </list>
    /// </summary>
    public static byte[] BuildEmptyOverworldPayload()
    {
        var writer = new BinaryStream();

        for (var i = 0; i < OverworldSubChunkCount; i++)
        {
            // Paletted storage header: (bitsPerBlock << 1) | nonPersistentFlag. bitsPerBlock
            // = 0 means "single-value palette, no bit-packed word array follows" - i.e. the
            // whole subchunk column is just one biome, uniformly.
            writer.WriteByte(1);
            // bitsPerBlock == 0 means the palette size itself isn't written (it's implicitly
            // exactly one entry), but that one entry still has to be written.
            writer.WriteVarInt(PlainsBiomeId);
        }

        writer.WriteByte(0); // border block array count - none

        // No block-entity (tile) NBT data to append: the column is fully empty, and this
        // section isn't length-prefixed, so writing nothing is exactly correct here.

        return writer.GetBufferDisposing().ToArray();
    }
}
