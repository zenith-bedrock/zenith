namespace Zenith.World;

/// <summary>
/// 16×16×16 blocks of one <see cref="WorldColumn"/> (ADR §167). Cells hold indices into a small palette of runtime
/// ids; a section whose cells are all one block (all air, all stone…) keeps no cell array at all.
/// <para/>
/// Single writer — the GameLoop. Other threads (the chunk-stream workers serialising a LevelChunk) read through
/// <see cref="TrySnapshot"/>, a seqlock: the writer bumps <c>_version</c> to odd before it touches anything and to
/// even after, a reader retries when it saw an odd value or the value changed under it. No lock on either side.
/// </summary>
sealed class Section
{
    public const int Edge = 16;
    public const int Volume = Edge * Edge * Edge;

    /// <summary>Same layout the wire uses: x major, then z, then y (so a column of blocks is contiguous).</summary>
    public static int IndexOf(int localX, int localY, int localZ) => (localX << 8) | (localZ << 4) | localY;

    private int[] _palette;
    private int _paletteCount;
    private ushort[]? _cells;
    private int _airCells;
    private int _randomTickable;
    private int _version;

    private Section(int[] palette, int paletteCount, ushort[]? cells, int airCells, int randomTickable)
    {
        _randomTickable = randomTickable;
        _palette = palette;
        _paletteCount = paletteCount;
        _cells = cells;
        _airCells = airCells;
    }

    /// <summary>A section made entirely of <paramref name="runtimeId"/>.</summary>
    public static Section Uniform(int runtimeId) =>
        new([runtimeId, 0, 0, 0], 1, null, runtimeId == Blocks.Air ? Volume : 0, Blocks.IsRandomTicking(runtimeId) ? Volume : 0);

    /// <summary>Builds a section from 4096 runtime ids in <see cref="IndexOf"/> order.</summary>
    public static Section FromIds(ReadOnlySpan<int> ids)
    {
        if (ids.Length != Volume)
            throw new ArgumentException($"A section has {Volume} cells, got {ids.Length}.", nameof(ids));

        var palette = new int[8];
        var count = 0;
        var cells = new ushort[Volume];
        var air = 0;
        var airId = Blocks.Air;
        var perPalette = new int[8];
        var lastId = ids[0];
        var lastIndex = -1;
        for (var i = 0; i < Volume; i++)
        {
            var id = ids[i];
            int index;
            if (lastIndex >= 0 && id == lastId)
            {
                index = lastIndex;
            }
            else
            {
                index = -1;
                for (var p = 0; p < count; p++)
                {
                    if (palette[p] != id) continue;
                    index = p;
                    break;
                }

                if (index < 0)
                {
                    if (count == palette.Length)
                    {
                        Array.Resize(ref palette, count * 2);
                        Array.Resize(ref perPalette, count * 2);
                    }

                    index = count;
                    palette[count++] = id;
                }

                lastId = id;
                lastIndex = index;
            }

            cells[i] = (ushort)index;
            perPalette[index]++;
            if (id == airId) air++;
        }

        // Padded to 4 so a uniform section never has a zero-length palette array.
        if (palette.Length < 4) Array.Resize(ref palette, 4);
        var tickable = 0;
        for (var p = 0; p < count; p++)
            if (Blocks.IsRandomTicking(palette[p])) tickable += perPalette[p];

        return count == 1
            ? new Section(palette, 1, null, air, tickable)
            : new Section(palette, count, cells, air, tickable);
    }

    /// <summary>True while every cell is air — such a section is not sent and need not be stored.</summary>
    public bool IsAllAir => _airCells == Volume;

    /// <summary>
    /// How many cells hold a block that takes random ticks. Zero means the section has nothing to tick and the tick
    /// system never visits it (ADR §169). Kept by <see cref="Set"/>, so it is exact without scanning.
    /// </summary>
    public int RandomTickableCount => _randomTickable;

    public int Version => Volatile.Read(ref _version);

    /// <summary>Owner-thread read.</summary>
    public int Get(int index) => _cells is null ? _palette[0] : _palette[_cells[index]];

    public int Get(int localX, int localY, int localZ) => Get(IndexOf(localX, localY, localZ));

    /// <summary>Owner-thread write. False when the cell already held <paramref name="runtimeId"/>.</summary>
    public bool Set(int index, int runtimeId)
    {
        var current = Get(index);
        if (current == runtimeId) return false;

        Interlocked.Increment(ref _version); // odd: write in progress
        var paletteIndex = PaletteIndexFor(runtimeId);
        if (_cells is null)
        {
            // Leaving the uniform state: every cell used to point at palette entry 0.
            _cells = new ushort[Volume];
        }

        _cells[index] = (ushort)paletteIndex;
        var airId = Blocks.Air;
        if (current == airId) _airCells--;
        if (runtimeId == airId) _airCells++;
        if (Blocks.IsRandomTicking(current)) _randomTickable--;
        if (Blocks.IsRandomTicking(runtimeId)) _randomTickable++;
        Interlocked.Increment(ref _version); // even: stable again
        return true;
    }

    private int PaletteIndexFor(int runtimeId)
    {
        for (var p = 0; p < _paletteCount; p++)
            if (_palette[p] == runtimeId) return p;

        if (_paletteCount == Volume) Compact();
        if (_paletteCount == _palette.Length) Array.Resize(ref _palette, _paletteCount * 2);
        _palette[_paletteCount] = runtimeId;
        return _paletteCount++;
    }

    /// <summary>Drops palette entries no cell points at any more (runs only when the palette is full).</summary>
    private void Compact()
    {
        var cells = _cells!;
        var used = new bool[_paletteCount];
        foreach (var cell in cells) used[cell] = true;

        var remap = new int[_paletteCount];
        var next = 0;
        for (var p = 0; p < _paletteCount; p++)
        {
            if (!used[p]) continue;
            _palette[next] = _palette[p];
            remap[p] = next++;
        }

        for (var i = 0; i < cells.Length; i++) cells[i] = (ushort)remap[cells[i]];
        _paletteCount = next;
    }

    /// <summary>
    /// Consistent copy for any thread. <paramref name="cells"/> needs <see cref="Volume"/> entries and
    /// <paramref name="palette"/> as many entries as the section can have (<see cref="Volume"/> is always enough).
    /// When the section is uniform, <paramref name="paletteCount"/> is 1 and <paramref name="cells"/> is untouched.
    /// </summary>
    public bool TrySnapshot(Span<ushort> cells, Span<int> palette, out int paletteCount, out bool uniform)
    {
        var spin = new SpinWait();
        for (var attempt = 0; attempt < 64; attempt++)
        {
            var before = Volatile.Read(ref _version);
            if ((before & 1) == 0)
            {
                var sourceCells = _cells;
                var count = _paletteCount;
                var isUniform = sourceCells is null;
                if (!isUniform) sourceCells.AsSpan().CopyTo(cells);
                _palette.AsSpan(0, count).CopyTo(palette);
                if (Volatile.Read(ref _version) == before)
                {
                    paletteCount = count;
                    uniform = isUniform;
                    return true;
                }
            }

            spin.SpinOnce();
        }

        paletteCount = 0;
        uniform = false;
        return false;
    }

    /// <summary>Consistent copy of the runtime ids in <see cref="IndexOf"/> order, for any thread.</summary>
    public void CopyIds(Span<int> ids)
    {
        if (ids.Length < Volume) throw new ArgumentException("Need 4096 entries.", nameof(ids));
        var cells = System.Buffers.ArrayPool<ushort>.Shared.Rent(Volume);
        var palette = System.Buffers.ArrayPool<int>.Shared.Rent(Volume);
        try
        {
            var uniform = false;
            while (!TrySnapshot(cells, palette, out _, out uniform))
            {
            }

            if (uniform)
            {
                ids[..Volume].Fill(palette[0]);
                return;
            }

            for (var i = 0; i < Volume; i++) ids[i] = palette[cells[i]];
        }
        finally
        {
            System.Buffers.ArrayPool<ushort>.Shared.Return(cells);
            System.Buffers.ArrayPool<int>.Shared.Return(palette);
        }
    }
}
