using Zenith.Binary;

namespace Zenith.World;

/// <summary>
/// One 16×16 column of the world, owned by <see cref="World"/> while it is resident (ADR §167): 24 sections from
/// <see cref="MinY"/> up, a biome, and the <see cref="BlockEntity"/> of every cell that has one. It is born complete —
/// generated from terrain or decoded from storage — and published once; afterwards only the GameLoop writes it.
/// <para/>
/// What a client receives (<see cref="GetWire"/>) is serialised from the live sections, so a late joiner sees every edit
/// without a replay of block updates. The bytes are cached per edit version.
/// </summary>
sealed class WorldColumn
{
    public const int MinY = -64;
    public const int SectionCount = 24;
    public const int MaxY = MinY + SectionCount * Section.Edge - 1;
    private const int MinSectionIndex = MinY >> 4;
    private const int BiomeNetworkBorderBlocksEmpty = 0;
    private const byte BiomePaletteHeader = 1;

    private readonly Section?[] _sections = new Section?[SectionCount];
    private readonly Dictionary<int, BlockEntity> _entities = new();
    private readonly Lock _wireGate = new();
    private int _editVersion;
    private int _wireVersion = -1;
    private WirePayload _wire;
    private volatile bool _dirty;

    public WorldColumn(ChunkCoord coord, int dimensionId, int biomeNetworkId)
    {
        Coord = coord;
        DimensionId = dimensionId;
        BiomeNetworkId = biomeNetworkId;
    }

    public ChunkCoord Coord { get; }
    public int DimensionId { get; }
    public int BiomeNetworkId { get; }

    /// <summary>Edited since it was generated or loaded, so storage no longer matches it.</summary>
    public bool IsDirty => _dirty;

    /// <summary>Bumped by every block edit; a client that was sent version N is stale once this is not N.</summary>
    public int EditVersion => Volatile.Read(ref _editVersion);

    public void MarkDirty() => _dirty = true;

    public void ClearDirty() => _dirty = false;

    /// <summary>
    /// One bit per section that has cells taking random ticks. A column with no bit set is asleep: the tick system
    /// skips it without looking (ADR §169). Kept by <see cref="Set"/> and <see cref="SetSection"/>.
    /// </summary>
    public int RandomTickMask => _randomTickMask;

    private int _randomTickMask;

    private void RefreshTickBit(int index)
    {
        if (_sections[index] is { RandomTickableCount: > 0 }) _randomTickMask |= 1 << index;
        else _randomTickMask &= ~(1 << index);
    }

    public Section? SectionAt(int index) => (uint)index < SectionCount ? _sections[index] : null;

    /// <summary>Install a section while the column is still being built (generation, decode) — never on a published column.</summary>
    public void SetSection(int index, Section section)
    {
        _sections[index] = section;
        RefreshTickBit(index);
    }

    public static bool InRange(int y) => y is >= MinY and <= MaxY;

    public int Get(int x, int y, int z)
    {
        if (!InRange(y)) return Blocks.Air;
        var section = _sections[(y - MinY) >> 4];
        return section is null ? Blocks.Air : section.Get(x & 15, y & 15, z & 15);
    }

    /// <summary>Owner-thread write; false when out of range or already that block.</summary>
    public bool Set(int x, int y, int z, int runtimeId)
    {
        if (!InRange(y)) return false;
        var index = (y - MinY) >> 4;
        var section = _sections[index];
        if (section is null)
        {
            if (runtimeId == Blocks.Air) return false;
            section = Section.Uniform(Blocks.Air);
            _sections[index] = section;
        }

        if (!section.Set(Section.IndexOf(x & 15, y & 15, z & 15), runtimeId)) return false;
        RefreshTickBit(index);
        _dirty = true;
        Interlocked.Increment(ref _editVersion); // after the section changed — see GetWire
        return true;
    }

    // ---- block entities (owner thread only) ----

    public int EntityCount => _entities.Count;

    private static int EntityKey(int x, int y, int z) => ((y - MinY) << 8) | ((z & 15) << 4) | (x & 15);

    public bool TryGetEntity(int x, int y, int z, out BlockEntity entity) =>
        _entities.TryGetValue(EntityKey(x, y, z), out entity!);

    public void SetEntity(int x, int y, int z, BlockEntity entity)
    {
        _entities[EntityKey(x, y, z)] = entity;
        _dirty = true;
    }

    public bool RemoveEntity(int x, int y, int z, out BlockEntity entity)
    {
        if (!_entities.Remove(EntityKey(x, y, z), out entity!)) return false;
        _dirty = true;
        return true;
    }

    /// <summary>Visits each entity with its local cell (0..15, y, 0..15).</summary>
    public void ForEachEntity(Action<int, int, int, BlockEntity> visitor)
    {
        foreach (var (key, entity) in _entities)
            visitor(key & 15, (key >> 8) + MinY, (key >> 4) & 15, entity);
    }

    // ---- wire ----

    /// <summary>The LevelChunk payload for the column as it is now; any thread.</summary>
    public WirePayload GetWire()
    {
        lock (_wireGate)
        {
            // The version is read before the sections are, and a writer bumps it after changing a section: a change
            // racing the build leaves the cache tagged with the old version, so the next call rebuilds. It can never
            // tag stale bytes with a newer version.
            var version = Volatile.Read(ref _editVersion);
            if (version == _wireVersion) return _wire;

            _wire = BuildWire(version);
            _wireVersion = version;
            return _wire;
        }
    }

    private WirePayload BuildWire(int version)
    {
        var highest = 0;
        for (var i = SectionCount - 1; i >= 0; i--)
        {
            if (_sections[i] is not { IsAllAir: false }) continue;
            highest = i;
            break;
        }

        var writer = new BinaryStream();
        for (var i = 0; i <= highest; i++)
            SubChunkWire.Write(ref writer, _sections[i], MinSectionIndex + i);

        for (var i = 0; i < SectionCount; i++)
        {
            writer.WriteByte(BiomePaletteHeader);
            writer.WriteVarInt(BiomeNetworkId);
        }

        writer.WriteByte(BiomeNetworkBorderBlocksEmpty);
        return new WirePayload(highest + 1, writer.GetBufferDisposing().ToArray(), version);
    }
}

/// <summary>A serialised LevelChunk body: how many subchunks it carries, the bytes, and the <see cref="WorldColumn.EditVersion"/> they reflect (or an older one if an edit raced the build).</summary>
readonly record struct WirePayload(int SubChunkCount, byte[] Payload, int Version);
