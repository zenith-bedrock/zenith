using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;

namespace Zenith.BedrockData;

/// <summary>
/// Writes the embedded <c>block_palette.nbt</c>: gzip'd BIG-endian NBT <c>{blocks: [{network_id, name, states}]}</c>
/// with zero gzip mtime so an unchanged export reproduces the same bytes.
/// </summary>
internal static class BlockPaletteWriter
{
    private const byte TagEnd = 0, TagByte = 1, TagInt = 3, TagString = 8, TagList = 9, TagCompound = 10;

    public static byte[] Write(JsonArray blocks)
    {
        using var raw = new MemoryStream();
        raw.Write([TagCompound, 0, 0, TagList]);
        WriteText(raw, "blocks");
        raw.WriteByte(TagCompound);
        WriteInt(raw, blocks.Count);
        foreach (var node in blocks)
        {
            var block = node!.AsObject();
            raw.WriteByte(TagInt); WriteText(raw, "network_id"); WriteInt(raw, (int)block["network_id"]!);
            raw.WriteByte(TagString); WriteText(raw, "name"); WriteText(raw, (string)block["name"]!);
            raw.WriteByte(TagCompound); WriteText(raw, "states");
            WriteStates(raw, block["states"]!.AsObject());
            raw.WriteByte(TagEnd);
        }
        raw.WriteByte(TagEnd);

        using var gz = new MemoryStream();
        using (var z = new GZipStream(gz, CompressionLevel.SmallestSize, leaveOpen: true)) z.Write(raw.ToArray());
        return gz.ToArray();
    }

    private static void WriteStates(Stream s, JsonObject states)
    {
        foreach (var (name, state) in states)
        {
            var type = (string)state!["type"]!;
            var value = state["value"]!;
            switch (type)
            {
                case "byte": s.WriteByte(TagByte); WriteText(s, name); s.WriteByte(unchecked((byte)(sbyte)(int)value)); break;
                case "int": s.WriteByte(TagInt); WriteText(s, name); WriteInt(s, (int)value); break;
                case "string": s.WriteByte(TagString); WriteText(s, name); WriteText(s, (string)value!); break;
                default: throw new InvalidDataException($"Unsupported block state type: {type}");
            }
        }
        s.WriteByte(TagEnd);
    }

    private static void WriteText(Stream s, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> len = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(len, checked((ushort)bytes.Length));
        s.Write(len);
        s.Write(bytes);
    }

    private static void WriteInt(Stream s, int value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(b, value);
        s.Write(b);
    }
}
