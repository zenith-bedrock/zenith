using System.Buffers.Binary;
using System.Text;

namespace Zenith.GamedataImport;

/// <summary>One unpacked file inside a `.brarchive` container.</summary>
internal readonly record struct BrArchiveEntry(string Name, byte[] Content);

/// <summary>
/// Reads Mojang's `.brarchive` container format (used by BDS's behavior-pack `__brarchive`
/// directory to bundle many small files — recipes, item catalog, etc. — into one binary blob).
///
/// Format verified directly against a real BDS install's `recipes.brarchive`, independent of any
/// third-party tool's claims: 8-byte magic, u32 entry count, u32 format version (only 1 observed),
/// then a flat table of fixed 256-byte entry records (1-byte name length + up to 247 bytes of
/// zero-padded UTF-8 name + u32 content offset + u32 content length, offset relative to the start
/// of the data block), then one data block holding every entry's raw content back-to-back. Content
/// bytes are stored verbatim — for `recipes.brarchive` specifically this is plain vanilla behavior-
/// pack JSON, confirmed by parsing all 821 entries with zero failures. Not every `.brarchive`'s
/// content is plain JSON, though — `item_catalog.brarchive`'s single entry is itself a second,
/// undocumented compiled format (`\x7fMCB` tag); this reader only unpacks the outer container, it
/// makes no claim about what a given entry's bytes mean.
/// </summary>
internal static class BrArchiveReader
{
    private static readonly byte[] Magic = [0x7D, 0x27, 0x25, 0xB1, 0xA0, 0x52, 0x70, 0x26];
    private const int HeaderSize = 16;
    private const int EntryRecordSize = 256;
    private const int EntryNameMaxLength = 247;
    private const uint SupportedFormatVersion = 1;

    public static IReadOnlyList<BrArchiveEntry> Read(string path) => Read(File.ReadAllBytes(path));

    public static IReadOnlyList<BrArchiveEntry> Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize || !data[..8].SequenceEqual(Magic))
            throw new InvalidDataException("Not a .brarchive file (magic mismatch).");

        var count = BinaryPrimitives.ReadUInt32LittleEndian(data[8..12]);
        var version = BinaryPrimitives.ReadUInt32LittleEndian(data[12..16]);
        if (version != SupportedFormatVersion)
            throw new InvalidDataException(
                $".brarchive format version {version} is not supported (only {SupportedFormatVersion} is known).");

        var entryTableSize = (long)count * EntryRecordSize;
        var dataBlockStart = HeaderSize + entryTableSize;
        if (dataBlockStart > data.Length)
            throw new InvalidDataException(".brarchive entry table exceeds file length.");

        var entries = new List<BrArchiveEntry>((int)count);
        for (var i = 0; i < count; i++)
        {
            var recordStart = HeaderSize + i * EntryRecordSize;
            var record = data.Slice(recordStart, EntryRecordSize);

            var nameLength = record[0];
            if (nameLength > EntryNameMaxLength)
                throw new InvalidDataException($".brarchive entry {i} has an invalid name length {nameLength}.");
            var name = Encoding.UTF8.GetString(record.Slice(1, nameLength));

            var contentOffset = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(248, 4));
            var contentLength = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(252, 4));
            var contentStart = dataBlockStart + contentOffset;
            if (contentStart + contentLength > data.Length)
                throw new InvalidDataException($".brarchive entry '{name}' content range exceeds file length.");

            entries.Add(new BrArchiveEntry(name, data.Slice((int)contentStart, (int)contentLength).ToArray()));
        }

        return entries;
    }
}
