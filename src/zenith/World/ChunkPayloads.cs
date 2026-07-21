using System.Buffers;
using Zenith.Raknet.Stream;

namespace Zenith.World;

/// <summary>Gera payload de coluna overworld (biomes + subchunks de rede). ADR §69: single-pass + pooled scratch.</summary>
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
    private const int SectionVolume = 4096;
    /// <summary>Trunk ≤6 + canopy + margin above max surface.</summary>
    private const int NoiseFeatureHeadroom = 8;

    /// <summary>Coluna vazia (só biomes) — legado; novos miss usam <see cref="BuildFlatOverworld"/>.</summary>
    public static byte[] BuildEmptyOverworld()
    {
        var writer = new BinaryStream();
        WriteBiomesAndBorder(ref writer, PlainsBiomeId);
        return writer.GetBufferDisposing().ToArray();
    }

    /// <summary>
    /// Flat: classic stone/grass column — delegates to <see cref="BuildOverworldColumn"/>.
    /// </summary>
    public static (int SubChunkCount, byte[] Payload) BuildFlatOverworld()
        => BuildOverworldColumn(
            0,
            0,
            (x, y, z) => OverworldTerrainSampler.SampleBlock(x, y, z, Blocks.FlatGrassY),
            PlainsBiomeId,
            maxWorldY: Blocks.FlatGrassY);

    /// <summary>Noise overworld — surface cache + cave context + single-pass sections (ADR §69).</summary>
    public static (int SubChunkCount, byte[] Payload) BuildNoiseOverworldColumn(
        int chunkX,
        int chunkZ,
        int seed,
        OverworldCaveContext caves)
    {
        Span<int> surfaces = stackalloc int[256];
        Span<OverworldBiomeKind> biomes = stackalloc OverworldBiomeKind[256];
        OverworldTerrainSampler.FillColumnSurfaces(chunkX, chunkZ, seed, surfaces, biomes, out var maxSurface);
        var featureMaxY = OverworldTerrainSampler.MaxTreeCanopyYAffectingChunk(chunkX, chunkZ, seed);
        var maxWorldY = Math.Max(maxSurface + NoiseFeatureHeadroom, OverworldTerrainSampler.SeaLevel);
        if (featureMaxY > maxWorldY)
            maxWorldY = featureMaxY;
        var biomeId = OverworldBiomeSampler.NetworkId(biomes[(8 << 4) | 8]);
        var maxSubChunk = Math.Clamp(
            SectionIndex(maxWorldY),
            0,
            OverworldMaxSubChunkIndex - OverworldMinSubChunkIndex);

        var baseX = chunkX << 4;
        var baseZ = chunkZ << 4;
        var ids = ArrayPool<int>.Shared.Rent(SectionVolume);
        try
        {
            var writer = new BinaryStream();
            for (var section = 0; section <= maxSubChunk; section++)
            {
                var worldYBase = OverworldMinSubChunkIndex * 16 + section * 16;
                for (var lx = 0; lx < 16; lx++)
                {
                    for (var lz = 0; lz < 16; lz++)
                    {
                        var meta = (lx << 4) | lz;
                        var wx = baseX + lx;
                        var wz = baseZ + lz;
                        var surface = surfaces[meta];
                        var biome = biomes[meta];
                        for (var localY = 0; localY < 16; localY++)
                        {
                            ids[BlockIndex(lx, localY, lz)] = OverworldTerrainSampler.SampleNoiseBlockAtSurface(
                                wx, worldYBase + localY, wz, seed, surface, caves, biome);
                        }
                    }
                }

                WriteSubChunk(ref writer, ids.AsSpan(0, SectionVolume));
            }

            WriteBiomesAndBorder(ref writer, biomeId);
            return (SubChunkCount: maxSubChunk + 1, Payload: writer.GetBufferDisposing().ToArray());
        }
        finally
        {
            ArrayPool<int>.Shared.Return(ids, clearArray: false);
        }
    }

    /// <summary>
    /// Builds paletted subchunks for one column from a world-space block sampler.
    /// SubChunkCount = highest non-air section index + 1 (Bedrock implicit air above).
    /// </summary>
    public static (int SubChunkCount, byte[] Payload) BuildOverworldColumn(
        int chunkX,
        int chunkZ,
        Func<int, int, int, int> blockAtWorld,
        int biomeNetworkId = PlainsBiomeId,
        int? maxWorldY = null)
    {
        var baseX = chunkX << 4;
        var baseZ = chunkZ << 4;
        var boundY = maxWorldY ?? (OverworldMaxSubChunkIndex * 16 + 15);
        var maxSubChunk = Math.Clamp(
            SectionIndex(boundY),
            0,
            OverworldMaxSubChunkIndex - OverworldMinSubChunkIndex);

        var ids = ArrayPool<int>.Shared.Rent(SectionVolume);
        try
        {
            var writer = new BinaryStream();
            for (var section = 0; section <= maxSubChunk; section++)
            {
                var worldYBase = OverworldMinSubChunkIndex * 16 + section * 16;
                for (var x = 0; x < 16; x++)
                {
                    for (var z = 0; z < 16; z++)
                    {
                        for (var localY = 0; localY < 16; localY++)
                            ids[BlockIndex(x, localY, z)] = blockAtWorld(baseX + x, worldYBase + localY, baseZ + z);
                    }
                }

                WriteSubChunk(ref writer, ids.AsSpan(0, SectionVolume));
            }

            WriteBiomesAndBorder(ref writer, biomeNetworkId);
            return (SubChunkCount: maxSubChunk + 1, Payload: writer.GetBufferDisposing().ToArray());
        }
        finally
        {
            ArrayPool<int>.Shared.Return(ids, clearArray: false);
        }
    }

    private static int SectionIndex(int worldY)
        => (worldY - OverworldMinSubChunkIndex * 16) >> 4;

    private static void WriteBiomesAndBorder(ref BinaryStream writer, int biomeNetworkId)
    {
        for (var i = 0; i < OverworldSubChunkCount; i++)
        {
            writer.WriteByte(BiomeNetworkPaletteHeader);
            writer.WriteVarInt(biomeNetworkId);
        }

        writer.WriteByte(BorderBlocksEmpty);
    }

    private static void WriteSubChunk(ref BinaryStream writer, ReadOnlySpan<int> ids)
    {
        writer.WriteByte(SubChunkVersion);
        writer.WriteByte(BlockStorageLayers);
        WritePalettedStorage(ref writer, ids);
    }

    private static void WritePalettedStorage(ref BinaryStream writer, ReadOnlySpan<int> ids)
    {
        // Noise columns typically use &lt; 32 unique block types — linear palette, no Dictionary.
        Span<int> palette = stackalloc int[64];
        Span<ushort> indices = stackalloc ushort[SectionVolume];
        var paletteCount = 0;

        for (var i = 0; i < SectionVolume; i++)
        {
            var id = ids[i];
            var found = -1;
            for (var p = 0; p < paletteCount; p++)
            {
                if (palette[p] == id)
                {
                    found = p;
                    break;
                }
            }

            if (found < 0)
            {
                if (paletteCount >= palette.Length)
                    throw new InvalidOperationException($"Subchunk palette exceeds {palette.Length} entries.");
                found = paletteCount;
                palette[paletteCount++] = id;
            }

            indices[i] = (ushort)found;
        }

        var bitsPerBlock = BitsPerBlockFor(paletteCount);
        writer.WriteByte((byte)((bitsPerBlock << 1) | NetworkBit));
        if (bitsPerBlock > 0)
        {
            var blocksPerWord = 32 / bitsPerBlock;
            var wordCount = (SectionVolume + blocksPerWord - 1) / blocksPerWord;
            for (var w = 0; w < wordCount; w++)
            {
                uint word = 0;
                for (var slot = 0; slot < blocksPerWord; slot++)
                {
                    var position = w * blocksPerWord + slot;
                    if (position >= SectionVolume) break;
                    word |= (uint)indices[position] << (slot * bitsPerBlock);
                }

                writer.WriteUInt(word, BinaryStream.Endianess.Little);
            }
        }

        if (bitsPerBlock != 0)
            writer.WriteVarInt(paletteCount);

        for (var p = 0; p < paletteCount; p++)
            writer.WriteVarInt(palette[p]);
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
