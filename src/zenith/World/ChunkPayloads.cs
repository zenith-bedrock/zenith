using Zenith.Raknet.Stream;

namespace Zenith.World;

/// <summary>Gera payload de coluna overworld (biomes + subchunks de rede).</summary>
static class ChunkPayloads
{
    private const int OverworldMinSubChunkIndex = -4;
    private const int OverworldMaxSubChunkIndex = 19;
    private const int OverworldSubChunkCount = OverworldMaxSubChunkIndex - OverworldMinSubChunkIndex + 1;
    private const int PlainsBiomeId = 1;
    private const int SubChunkVersion = 8;
    private const int BlockStorageLayers = 1;
    private const byte BiomeNetworkPaletteHeader = 1;
    private const byte BorderBlocksEmpty = 0;
    private const int NetworkBit = 1;

    /// <summary>Coluna vazia (só biomes) — legado; novos miss usam <see cref="BuildFlatOverworld"/>.</summary>
    public static byte[] BuildEmptyOverworld()
    {
        var writer = new BinaryStream();
        WriteBiomesAndBorder(ref writer);
        return writer.GetBufferDisposing().ToArray();
    }

    /// <summary>
    /// Flat: classic stone/grass column — delegates to <see cref="BuildOverworldColumn"/>.
    /// </summary>
    public static (int SubChunkCount, byte[] Payload) BuildFlatOverworld()
        => BuildOverworldColumn(0, 0, (x, y, z) =>
            OverworldTerrainSampler.SampleBlock(x, y, z, Blocks.FlatGrassY));

    /// <summary>
    /// Builds paletted subchunks for one column from a world-space block sampler.
    /// SubChunkCount = highest non-air section index + 1 (Bedrock implicit air above).
    /// </summary>
    public static (int SubChunkCount, byte[] Payload) BuildOverworldColumn(
        int chunkX,
        int chunkZ,
        Func<int, int, int, int> blockAtWorld)
    {
        var baseX = chunkX << 4;
        var baseZ = chunkZ << 4;
        var maxSubChunk = 0;
        for (var section = 0; section <= OverworldMaxSubChunkIndex - OverworldMinSubChunkIndex; section++)
        {
            var worldYBase = OverworldMinSubChunkIndex * 16 + section * 16;
            if (!SectionHasSolid(baseX, baseZ, worldYBase, blockAtWorld)) continue;
            maxSubChunk = section;
        }

        var writer = new BinaryStream();
        for (var section = 0; section <= maxSubChunk; section++)
        {
            var worldYBase = OverworldMinSubChunkIndex * 16 + section * 16;
            var ids = FillSection(baseX, baseZ, worldYBase, blockAtWorld);
            WriteSubChunk(ref writer, ids);
        }

        WriteBiomesAndBorder(ref writer);
        return (SubChunkCount: maxSubChunk + 1, Payload: writer.GetBufferDisposing().ToArray());
    }

    private static bool SectionHasSolid(int baseX, int baseZ, int worldYBase, Func<int, int, int, int> blockAtWorld)
    {
        for (var x = 0; x < 16; x++)
        {
            for (var z = 0; z < 16; z++)
            {
                for (var localY = 0; localY < 16; localY++)
                {
                    if (blockAtWorld(baseX + x, worldYBase + localY, baseZ + z) != Blocks.Air)
                        return true;
                }
            }
        }

        return false;
    }

    private static int[] FillSection(int baseX, int baseZ, int worldYBase, Func<int, int, int, int> blockAtWorld)
    {
        var ids = new int[4096];
        for (var x = 0; x < 16; x++)
        {
            for (var z = 0; z < 16; z++)
            {
                for (var localY = 0; localY < 16; localY++)
                    ids[BlockIndex(x, localY, z)] = blockAtWorld(baseX + x, worldYBase + localY, baseZ + z);
            }
        }

        return ids;
    }

    private static void WriteBiomesAndBorder(ref BinaryStream writer)
    {
        for (var i = 0; i < OverworldSubChunkCount; i++)
        {
            writer.WriteByte(BiomeNetworkPaletteHeader);
            writer.WriteVarInt(PlainsBiomeId);
        }

        writer.WriteByte(BorderBlocksEmpty);
    }

    private static void WriteSubChunk(ref BinaryStream writer, int[] ids)
    {
        writer.WriteByte(SubChunkVersion);
        writer.WriteByte(BlockStorageLayers);
        WritePalettedStorage(ref writer, ids);
    }

    private static void WritePalettedStorage(ref BinaryStream writer, int[] ids)
    {
        var palette = new List<int>();
        var lookup = new Dictionary<int, ushort>();
        var indices = new ushort[4096];
        for (var i = 0; i < 4096; i++)
        {
            var id = ids[i];
            if (!lookup.TryGetValue(id, out var index))
            {
                index = (ushort)palette.Count;
                palette.Add(id);
                lookup[id] = index;
            }

            indices[i] = index;
        }

        var bitsPerBlock = BitsPerBlockFor(palette.Count);
        writer.WriteByte((byte)((bitsPerBlock << 1) | NetworkBit));
        if (bitsPerBlock > 0)
        {
            var blocksPerWord = 32 / bitsPerBlock;
            var wordCount = (indices.Length + blocksPerWord - 1) / blocksPerWord;
            for (var w = 0; w < wordCount; w++)
            {
                uint word = 0;
                for (var slot = 0; slot < blocksPerWord; slot++)
                {
                    var position = w * blocksPerWord + slot;
                    if (position >= indices.Length) break;
                    word |= (uint)indices[position] << (slot * bitsPerBlock);
                }

                writer.WriteUInt(word, BinaryStream.Endianess.Little);
            }
        }

        if (bitsPerBlock != 0)
            writer.WriteVarInt(palette.Count);

        foreach (var entry in palette)
            writer.WriteVarInt(entry);
    }

    private static int BitsPerBlockFor(int paletteSize)
    {
        if (paletteSize <= 1) return 0;
        ReadOnlySpan<int> allowed = [1, 2, 3, 4, 5, 6, 8, 16];
        foreach (var bits in allowed)
        {
            if ((1 << bits) >= paletteSize) return bits;
        }

        return 16;
    }

    private static int BlockIndex(int x, int y, int z) => (x << 8) | (z << 4) | y;
}
