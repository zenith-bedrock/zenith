using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Zenith.BedrockData.Tests;

public sealed class BlockPaletteWriterTests
{
    [Fact]
    public void Write_ReproducesTheShippedPaletteFromItsOwnContents()
    {
        var shipped = Gunzip(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "data", "block_palette.nbt")));
        var blocks = ReadBlocks(shipped);
        Assert.NotEmpty(blocks);

        var rewritten = Gunzip(BlockPaletteWriter.Write(blocks));
        Assert.True(shipped.AsSpan().SequenceEqual(rewritten), "rewritten palette differs from the shipped one");
    }

    [Fact]
    public void Write_RejectsAnUnsupportedStateType()
    {
        var blocks = new JsonArray(new JsonObject
        {
            ["network_id"] = 1, ["name"] = "minecraft:x",
            ["states"] = new JsonObject { ["s"] = new JsonObject { ["type"] = "float", ["value"] = 1 } },
        });
        Assert.Throws<InvalidDataException>(() => BlockPaletteWriter.Write(blocks));
    }

    private static byte[] Gunzip(byte[] gz)
    {
        using var z = new GZipStream(new MemoryStream(gz), CompressionMode.Decompress);
        using var o = new MemoryStream();
        z.CopyTo(o);
        return o.ToArray();
    }

    /// <summary>Minimal big-endian reader for exactly the shape the palette uses.</summary>
    private static JsonArray ReadBlocks(byte[] b)
    {
        var p = 0;
        string Text() { var n = BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(p)); p += 2; var s = Encoding.UTF8.GetString(b, p, n); p += n; return s; }
        int Int() { var v = BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(p)); p += 4; return v; }

        Assert.Equal(10, b[p++]); Assert.Equal("", Text());
        Assert.Equal(9, b[p++]); Assert.Equal("blocks", Text());
        Assert.Equal(10, b[p++]);
        var count = Int();
        var blocks = new JsonArray();
        for (var i = 0; i < count; i++)
        {
            Assert.Equal(3, b[p++]); Assert.Equal("network_id", Text()); var id = Int();
            Assert.Equal(8, b[p++]); Assert.Equal("name", Text()); var name = Text();
            Assert.Equal(10, b[p++]); Assert.Equal("states", Text());
            var states = new JsonObject();
            for (byte tag; (tag = b[p++]) != 0;)
            {
                var key = Text();
                states[key] = tag switch
                {
                    1 => new JsonObject { ["type"] = "byte", ["value"] = (int)(sbyte)b[p++] },
                    3 => new JsonObject { ["type"] = "int", ["value"] = Int() },
                    8 => new JsonObject { ["type"] = "string", ["value"] = Text() },
                    _ => throw new InvalidDataException($"tag {tag}"),
                };
            }
            Assert.Equal(0, b[p++]); // end of the block compound
            blocks.Add(new JsonObject { ["network_id"] = id, ["name"] = name, ["states"] = states });
        }
        Assert.Equal(0, b[p++]);
        Assert.Equal(b.Length, p);
        return blocks;
    }
}
