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
    /// Flat: 1 subchunk (Y -64..-49) com stone/grass; restante implícito air via SubChunkCount=1.
    /// </summary>
    public static (int SubChunkCount, byte[] Payload) BuildFlatOverworld()
    {
        var ids = new int[4096];
        for (var i = 0; i < 4096; i++)
            ids[i] = Blocks.Air;

        // Y local 0..15 maps world Y = -64 + localY for section index 0.
        for (var x = 0; x < 16; x++)
        {
            for (var z = 0; z < 16; z++)
            {
                for (var localY = 0; localY <= 2; localY++) // -64..-62 stone
                    ids[BlockIndex(x, localY, z)] = Blocks.Stone;
                ids[BlockIndex(x, 3, z)] = Blocks.GrassBlock; // -61
            }
        }

        var writer = new BinaryStream();
        WriteSubChunk(ref writer, ids);
        WriteBiomesAndBorder(ref writer);
        return (SubChunkCount: 1, Payload: writer.GetBufferDisposing().ToArray());
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
