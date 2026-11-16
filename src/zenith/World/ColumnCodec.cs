using System.Buffers;
using System.Text;

namespace Zenith.World;

/// <summary>
/// A <see cref="WorldColumn"/> as bytes in storage (ADR §167): one blob per column.
/// <code>
/// "ZC" version
/// biome            7-bit int
/// sections         7-bit count, then per section: index byte, bits per cell byte, palette (7-bit count, strings),
///                  cells packed LSB-first at that width (absent when the section is a single block)
/// block entities   7-bit count, then per entity: local x byte, y (7-bit, offset by the world floor), local z byte,
///                  type id string, 7-bit length + the type's own bytes
/// falls (v2)       7-bit count, then per fall: local x byte, local z byte, origin y (7-bit, offset by the world floor), block key string
/// </code>
/// Version 1 blobs (no falls section) still load. Falls in flight are part of the image on purpose: the cell cleared at the origin
/// and the record of the block that left it are written together or not at all.
/// Palette entries are <see cref="BlockPalette.CanonicalKeyOf"/> strings, not runtime ids, so a world outlives a block
/// palette that renumbers.
/// </summary>
static class ColumnCodec
{
    private const byte Version = 2;
    private const byte FirstVersionWithFalls = 2;

    public static byte[] Encode(WorldColumn column, BlockPalette palette)
    {
        using var stream = new MemoryStream(4096);
        using (var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            w.Write((byte)'Z');
            w.Write((byte)'C');
            w.Write(Version);
            w.Write7BitEncodedInt(column.BiomeNetworkId);

            var present = new List<int>(WorldColumn.SectionCount);
            for (var i = 0; i < WorldColumn.SectionCount; i++)
                if (column.SectionAt(i) is { IsAllAir: false }) present.Add(i);

            w.Write7BitEncodedInt(present.Count);
            var cells = ArrayPool<ushort>.Shared.Rent(Section.Volume);
            var ids = ArrayPool<int>.Shared.Rent(Section.Volume);
            var remap = ArrayPool<int>.Shared.Rent(Section.Volume);
            var ordered = ArrayPool<int>.Shared.Rent(Section.Volume);
            try
            {
                foreach (var index in present)
                    WriteSection(w, column.SectionAt(index)!, index, palette, cells, ids, remap, ordered);
            }
            finally
            {
                ArrayPool<ushort>.Shared.Return(cells);
                ArrayPool<int>.Shared.Return(ids);
                ArrayPool<int>.Shared.Return(remap);
                ArrayPool<int>.Shared.Return(ordered);
            }

            w.Write7BitEncodedInt(column.EntityCount);
            column.ForEachEntity((lx, y, lz, entity) =>
            {
                w.Write((byte)lx);
                w.Write7BitEncodedInt(y - WorldColumn.MinY);
                w.Write((byte)lz);
                w.Write(entity.Type.Id);
                var bytes = entity.Type.Encode(entity);
                w.Write7BitEncodedInt(bytes.Length);
                w.Write(bytes);
            });

            w.Write7BitEncodedInt(column.PendingFalls.Count);
            foreach (var fall in column.PendingFalls)
            {
                w.Write((byte)fall.LocalX);
                w.Write((byte)fall.LocalZ);
                w.Write7BitEncodedInt(fall.OriginY - WorldColumn.MinY);
                w.Write(KeyOf(palette, fall.BlockRuntimeId));
            }
        }

        return stream.ToArray();
    }

    private static void WriteSection(
        BinaryWriter w, Section section, int index, BlockPalette palette,
        ushort[] cells, int[] snapshotPalette, int[] remap, int[] ordered)
    {
        var uniform = false;
        var count = 0;
        while (!section.TrySnapshot(cells, snapshotPalette, out count, out uniform))
        {
        }

        w.Write((byte)index);
        if (uniform)
        {
            w.Write((byte)0);
            w.Write7BitEncodedInt(1);
            w.Write(KeyOf(palette, snapshotPalette[0]));
            return;
        }

        remap.AsSpan(0, count).Fill(-1);
        var live = 0;
        for (var i = 0; i < Section.Volume; i++)
        {
            var cell = cells[i];
            if (remap[cell] >= 0) continue;
            ordered[live] = snapshotPalette[cell];
            remap[cell] = live++;
        }

        if (live == 1)
        {
            w.Write((byte)0);
            w.Write7BitEncodedInt(1);
            w.Write(KeyOf(palette, ordered[0]));
            return;
        }

        var bits = BitsFor(live);
        w.Write((byte)bits);
        w.Write7BitEncodedInt(live);
        for (var p = 0; p < live; p++) w.Write(KeyOf(palette, ordered[p]));

        ulong accumulator = 0;
        var filled = 0;
        for (var i = 0; i < Section.Volume; i++)
        {
            accumulator |= (ulong)remap[cells[i]] << filled;
            filled += bits;
            while (filled >= 8)
            {
                w.Write((byte)accumulator);
                accumulator >>= 8;
                filled -= 8;
            }
        }

        if (filled > 0) w.Write((byte)accumulator);
    }

    private static string KeyOf(BlockPalette palette, int runtimeId) =>
        palette.CanonicalKeyOf(runtimeId)
        ?? throw new InvalidOperationException($"Runtime id {runtimeId} is not in the block palette; cannot persist it.");

    private static int BitsFor(int paletteSize)
    {
        var bits = 1;
        while ((1 << bits) < paletteSize) bits++;
        return bits;
    }

    /// <summary>Decodes a stored column; throws <see cref="InvalidDataException"/> when the bytes are not one.</summary>
    public static WorldColumn Decode(ReadOnlyMemory<byte> data, ChunkCoord coord, int dimensionId, BlockPalette palette)
    {
        try
        {
            using var stream = new MemoryStream(data.ToArray(), writable: false);
            using var r = new BinaryReader(stream, Encoding.UTF8);
            if (r.ReadByte() != (byte)'Z' || r.ReadByte() != (byte)'C') throw new InvalidDataException("Not a column blob.");
            var version = r.ReadByte();
            if (version is < 1 or > Version) throw new InvalidDataException($"Column blob version {version} is not supported.");

            var column = new WorldColumn(coord, dimensionId, r.Read7BitEncodedInt());
            var sections = r.Read7BitEncodedInt();
            var ids = new int[Section.Volume];
            for (var s = 0; s < sections; s++)
            {
                var index = r.ReadByte();
                if (index >= WorldColumn.SectionCount) throw new InvalidDataException($"Section index {index} out of range.");
                var bits = r.ReadByte();
                var entries = r.Read7BitEncodedInt();
                if (entries is < 1 or > Section.Volume) throw new InvalidDataException($"Bad palette size {entries}.");
                var runtime = new int[entries];
                for (var p = 0; p < entries; p++)
                {
                    var key = r.ReadString();
                    if (!palette.TryGetByCanonicalKey(key, out runtime[p]))
                        throw new InvalidDataException($"Unknown block '{key.Replace('\0', ' ')}'.");
                }

                if (bits == 0)
                {
                    column.SetSection(index, Section.Uniform(runtime[0]));
                    continue;
                }

                if (bits > 12) throw new InvalidDataException($"Bad cell width {bits}.");
                ulong accumulator = 0;
                var filled = 0;
                var mask = (1UL << bits) - 1;
                for (var i = 0; i < Section.Volume; i++)
                {
                    while (filled < bits)
                    {
                        accumulator |= (ulong)r.ReadByte() << filled;
                        filled += 8;
                    }

                    var cell = (int)(accumulator & mask);
                    accumulator >>= bits;
                    filled -= bits;
                    if (cell >= entries) throw new InvalidDataException("Cell points outside its palette.");
                    ids[i] = runtime[cell];
                }

                column.SetSection(index, Section.FromIds(ids));
            }

            var entityCount = r.Read7BitEncodedInt();
            for (var e = 0; e < entityCount; e++)
            {
                var lx = r.ReadByte();
                var y = r.Read7BitEncodedInt() + WorldColumn.MinY;
                var lz = r.ReadByte();
                var typeId = r.ReadString();
                var length = r.Read7BitEncodedInt();
                var bytes = r.ReadBytes(length);
                if (bytes.Length != length) throw new InvalidDataException("Block entity data is truncated.");
                if (!BlockEntityTypes.TryGet(typeId, out var type)) continue; // a type this build lacks: dropped, not fatal
                var entity = type.Decode(bytes) ?? throw new InvalidDataException($"Block entity '{typeId}' data is not valid.");
                column.SetEntity(coord.X * 16 + lx, y, coord.Z * 16 + lz, entity);
            }

            if (version >= FirstVersionWithFalls)
            {
                var fallCount = r.Read7BitEncodedInt();
                for (var f = 0; f < fallCount; f++)
                {
                    var lx = r.ReadByte();
                    var lz = r.ReadByte();
                    var originY = r.Read7BitEncodedInt() + WorldColumn.MinY;
                    var key = r.ReadString();
                    if (lx >= 16 || lz >= 16 || !WorldColumn.InRange(originY))
                        throw new InvalidDataException("Fall record is outside its column.");
                    if (!palette.TryGetByCanonicalKey(key, out var blockRuntimeId))
                        throw new InvalidDataException($"Unknown falling block '{key.Replace('\0', ' ')}'.");
                    column.RestorePendingFall(new PendingFall(lx, lz, originY, blockRuntimeId));
                }
            }

            column.ClearDirty();
            return column;
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("Column blob is truncated.", exception);
        }
    }
}
