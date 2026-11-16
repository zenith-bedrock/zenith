using System.Buffers;
using Zenith.Binary;

namespace Zenith.World;

/// <summary>
/// Network encoding of one <see cref="Section"/> (subchunk version 9, one block layer, network palette of runtime ids).
/// Protocol 2168 prefixes the absolute subchunk Y; version 8 omits it and shifts the palette by one byte for such a client.
/// </summary>
static class SubChunkWire
{
    internal const int Version = 9;
    private const int BlockStorageLayers = 1;
    private const int NetworkBit = 1;

    public static void Write(ref BinaryStream writer, Section? section, int absoluteSubChunkY)
    {
        writer.WriteByte(Version);
        writer.WriteByte(BlockStorageLayers);
        writer.WriteByte(unchecked((byte)absoluteSubChunkY));

        if (section is null)
        {
            WriteUniform(ref writer, Blocks.Air);
            return;
        }

        var cells = ArrayPool<ushort>.Shared.Rent(Section.Volume);
        var palette = ArrayPool<int>.Shared.Rent(Section.Volume);
        var remap = ArrayPool<int>.Shared.Rent(Section.Volume);
        var ordered = ArrayPool<int>.Shared.Rent(Section.Volume);
        try
        {
            var uniform = false;
            var count = 0;
            while (!section.TrySnapshot(cells, palette, out count, out uniform))
            {
            }

            if (uniform)
            {
                WriteUniform(ref writer, palette[0]);
                return;
            }

            WriteStorage(ref writer, cells, palette, count, remap, ordered);
        }
        finally
        {
            ArrayPool<ushort>.Shared.Return(cells);
            ArrayPool<int>.Shared.Return(palette);
            ArrayPool<int>.Shared.Return(remap);
            ArrayPool<int>.Shared.Return(ordered);
        }
    }

    private static void WriteUniform(ref BinaryStream writer, int runtimeId)
    {
        writer.WriteByte(NetworkBit); // zero bits per block: the palette's single entry fills the section
        writer.WriteVarInt(runtimeId);
    }

    /// <summary>
    /// Writes only the palette entries some cell still points at (a section's palette also holds entries left behind by
    /// edits), renumbered densely, then the cells packed at the smallest bit width the protocol allows.
    /// </summary>
    private static void WriteStorage(ref BinaryStream writer, ReadOnlySpan<ushort> cells, ReadOnlySpan<int> palette, int paletteCount, Span<int> remap, Span<int> ordered)
    {
        remap[..paletteCount].Fill(-1);
        var live = 0;
        for (var i = 0; i < Section.Volume; i++)
        {
            var cell = cells[i];
            if (remap[cell] >= 0) continue;
            ordered[live] = palette[cell];
            remap[cell] = live++;
        }

        if (live == 1)
        {
            // Edits can leave only one live entry behind.
            WriteUniform(ref writer, palette[cells[0]]);
            return;
        }

        var bitsPerBlock = BitsPerBlockFor(live);
        writer.WriteByte((byte)((bitsPerBlock << 1) | NetworkBit));
        var blocksPerWord = 32 / bitsPerBlock;
        var wordCount = (Section.Volume + blocksPerWord - 1) / blocksPerWord;
        for (var w = 0; w < wordCount; w++)
        {
            uint word = 0;
            for (var slot = 0; slot < blocksPerWord; slot++)
            {
                var position = w * blocksPerWord + slot;
                if (position >= Section.Volume) break;
                word |= (uint)remap[cells[position]] << (slot * bitsPerBlock);
            }

            writer.WriteUInt(word, BinaryStream.Endianess.Little);
        }

        writer.WriteVarInt(live);
        for (var p = 0; p < live; p++)
            writer.WriteVarInt(ordered[p]);
    }

    private static int BitsPerBlockFor(int paletteSize)
    {
        ReadOnlySpan<int> allowed = [1, 2, 3, 4, 5, 6, 8, 16];
        foreach (var bits in allowed)
            if ((1 << bits) >= paletteSize) return bits;
        return 16;
    }
}
