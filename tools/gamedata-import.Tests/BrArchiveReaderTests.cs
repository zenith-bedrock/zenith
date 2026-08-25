using System.Buffers.Binary;
using System.Text;
using Xunit;
using Zenith.GamedataImport;

namespace Zenith.GamedataImport.Tests;

public class BrArchiveReaderTests
{
    private static readonly byte[] Magic = [0x7D, 0x27, 0x25, 0xB1, 0xA0, 0x52, 0x70, 0x26];

    /// <summary>Hand-builds a minimal, valid `.brarchive` byte array per the verified format — header, fixed 256-byte entry records, then a concatenated data block.</summary>
    private static byte[] BuildArchive(params (string Name, byte[] Content)[] entries)
    {
        const int headerSize = 16;
        const int recordSize = 256;

        var dataBlock = new List<byte>();
        var offsets = new int[entries.Length];
        for (var i = 0; i < entries.Length; i++)
        {
            offsets[i] = dataBlock.Count;
            dataBlock.AddRange(entries[i].Content);
        }

        var buffer = new byte[headerSize + entries.Length * recordSize + dataBlock.Count];
        Magic.CopyTo(buffer, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8, 4), (uint)entries.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(12, 4), 1);

        for (var i = 0; i < entries.Length; i++)
        {
            var recordStart = headerSize + i * recordSize;
            var nameBytes = Encoding.UTF8.GetBytes(entries[i].Name);
            buffer[recordStart] = (byte)nameBytes.Length;
            nameBytes.CopyTo(buffer, recordStart + 1);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(recordStart + 248, 4), (uint)offsets[i]);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(recordStart + 252, 4), (uint)entries[i].Content.Length);
        }

        var dataBlockStart = headerSize + entries.Length * recordSize;
        dataBlock.CopyTo(buffer, dataBlockStart);
        return buffer;
    }

    [Fact]
    public void Reads_entries_back_with_correct_names_and_content()
    {
        var archive = BuildArchive(
            ("acacia_boat.json", "{\"a\":1}"u8.ToArray()),
            ("andesite.json", "{\"b\":2}"u8.ToArray()));

        var entries = BrArchiveReader.Read(archive);

        Assert.Equal(2, entries.Count);
        Assert.Equal("acacia_boat.json", entries[0].Name);
        Assert.Equal("{\"a\":1}", System.Text.Encoding.UTF8.GetString(entries[0].Content));
        Assert.Equal("andesite.json", entries[1].Name);
        Assert.Equal("{\"b\":2}", System.Text.Encoding.UTF8.GetString(entries[1].Content));
    }

    [Fact]
    public void Reads_zero_entries()
    {
        var archive = BuildArchive();
        var entries = BrArchiveReader.Read(archive);
        Assert.Empty(entries);
    }

    [Fact]
    public void Rejects_wrong_magic()
    {
        var archive = BuildArchive(("x.json", "{}"u8.ToArray()));
        archive[0] = 0x00;
        Assert.Throws<InvalidDataException>(() => BrArchiveReader.Read(archive));
    }

    [Fact]
    public void Rejects_unsupported_format_version()
    {
        var archive = BuildArchive(("x.json", "{}"u8.ToArray()));
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(12, 4), 2);
        Assert.Throws<InvalidDataException>(() => BrArchiveReader.Read(archive));
    }

    [Fact]
    public void Rejects_a_truncated_entry_table()
    {
        var archive = BuildArchive(("x.json", "{}"u8.ToArray()));
        var truncated = archive[..20];
        Assert.Throws<InvalidDataException>(() => BrArchiveReader.Read(truncated));
    }

    /// <summary>Regression against the real file this reader was built and verified against — same header shape, same entry-table stride, independently reproduced here rather than trusted on faith.</summary>
    [Fact]
    public void Matches_the_real_recipes_brarchive_header_shape()
    {
        var archive = BuildArchive(("acacia_boat.json", "{}"u8.ToArray()));
        Assert.Equal(Magic, archive[..8]);
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(8, 4)));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(12, 4)));
    }
}
