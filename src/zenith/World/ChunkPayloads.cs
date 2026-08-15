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
    // Protocol 2168 uses subchunk v9: version, storage count, then absolute
    // subchunk Y. v8 omits Y, so a v8 payload shifts the palette by one byte
    // when consumed by a 2168 client. Internal (not private) so World's
    // LooksLikeTerrainPayload reads this instead of its own copy of the
    // literal — a hardcoded second copy is exactly what went stale last time
    // this value changed.
    internal const int SubChunkVersion = 9;
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

    /// <summary>Noise overworld — staged surfaces, feature plan, cave context and single-pass sections (ADR §69).</summary>
    public static (int SubChunkCount, byte[] Payload) BuildNoiseOverworldColumn(
        int chunkX,
        int chunkZ,
        int seed,
        OverworldCaveContext caves,
        WorldGenerationDiagnostics? generationDiagnostics = null)
    {
        Span<int> surfaces = stackalloc int[256];
        Span<OverworldBiomeKind> biomes = stackalloc OverworldBiomeKind[256];
        int maxSurface;
        var surfaceScope = generationDiagnostics is null ? default : generationDiagnostics.BeginSurface();
        using (surfaceScope)
            OverworldTerrainSampler.FillColumnSurfaces(chunkX, chunkZ, seed, surfaces, biomes, out maxSurface);

        // The cave context is built independently of terrain surfaces so point queries remain
        // cheap to construct. Full-column generation has the complete surface field now; turn
        // the segment geometry into an O(1) bit lookup before entering the voxel loop.
        var caveMaskScope = generationDiagnostics is null ? default : generationDiagnostics.BeginCaves();
        using (caveMaskScope)
            caves.PrepareColumnMask(surfaces);

        OverworldTerrainSampler.FeaturePlacementPlan features;
        int featureMaxY;
        var featureScope = generationDiagnostics is null ? default : generationDiagnostics.BeginFeatures();
        using (featureScope)
        {
            features = OverworldTerrainSampler.FeaturePlacementPlan.Build(chunkX, chunkZ, seed);
            featureMaxY = OverworldTerrainSampler.MaxTreeCanopyYAffectingChunk(chunkX, chunkZ, seed, features);
        }

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
        Span<OreCell> oreCells = stackalloc OreCell[OverworldOrePlacer.MaxColumnCellCount];
        var oreCellCount = OverworldOrePlacer.FillColumnCells(
            baseX, baseZ, seed, oreCells,
            out var minOreCellX, out var minOreCellY, out var minOreCellZ,
            out var oreWidthX, out var oreWidthY, out var oreWidthZ);
        var oreView = oreCells[..oreCellCount];
        var ids = ArrayPool<int>.Shared.Rent(SectionVolume);
        var payloadScope = generationDiagnostics is null ? default : generationDiagnostics.BeginPayload();
        try
        {
            using (payloadScope)
            {
                var writer = new BinaryStream();
                    for (var section = 0; section <= maxSubChunk; section++)
                    {
                        var worldYBase = OverworldMinSubChunkIndex * 16 + section * 16;
                    var samplingScope = generationDiagnostics is null ? default : generationDiagnostics.BeginSampling();
                    using (samplingScope)
                    {
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
                                    ids[BlockIndex(lx, localY, lz)] = OverworldTerrainSampler.SampleNoiseBlockAtSurfaceWithOre(
                                        wx, worldYBase + localY, wz, seed, surface, caves, biome, features,
                                        oreView, minOreCellX, minOreCellY, minOreCellZ,
                                        oreWidthX, oreWidthY, oreWidthZ);
                                }
                            }
                        }
                    }

                    var encodeScope = generationDiagnostics is null ? default : generationDiagnostics.BeginEncode();
                    using (encodeScope)
                        WriteSubChunk(ref writer, ids.AsSpan(0, SectionVolume), OverworldMinSubChunkIndex + section);
                }

                WriteBiomesAndBorder(ref writer, biomeId);
                return (SubChunkCount: maxSubChunk + 1, Payload: writer.GetBufferDisposing().ToArray());
            }
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

                WriteSubChunk(ref writer, ids.AsSpan(0, SectionVolume), OverworldMinSubChunkIndex + section);
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

    private static void WriteSubChunk(ref BinaryStream writer, ReadOnlySpan<int> ids, int absoluteSubChunkY)
    {
        writer.WriteByte(SubChunkVersion);
        writer.WriteByte(BlockStorageLayers);
        writer.WriteByte(unchecked((byte)absoluteSubChunkY));
        WritePalettedStorage(ref writer, ids);
    }

    private static void WritePalettedStorage(ref BinaryStream writer, ReadOnlySpan<int> ids)
    {
        // Keep the section hot path data-oriented: runtime ids are dense in the loaded block
        // palette, so a direct lookup avoids rescanning the palette for every voxel. The linear
        // fallback preserves correctness if a custom palette contains an id outside the scratch
        // range. No Dictionary/boxing is allowed in this per-section path.
        Span<int> palette = stackalloc int[64];
        Span<ushort> indices = stackalloc ushort[SectionVolume];
        Span<int> paletteLookup = stackalloc int[SectionVolume];
        paletteLookup.Fill(-1);
        var paletteCount = 0;

        for (var i = 0; i < SectionVolume; i++)
        {
            var id = ids[i];
            var found = id is >= 0 and < SectionVolume ? paletteLookup[id] : -1;
            if (found < 0)
            {
                for (var p = 0; p < paletteCount; p++)
                {
                    if (palette[p] != id) continue;
                    found = p;
                    break;
                }

                if (found < 0)
                {
                    if (paletteCount >= palette.Length)
                        throw new InvalidOperationException($"Subchunk palette exceeds {palette.Length} entries.");
                    found = paletteCount;
                    palette[paletteCount++] = id;
                    if (id is >= 0 and < SectionVolume)
                        paletteLookup[id] = found;
                }
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
