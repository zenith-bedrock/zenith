using Zenith.Raknet.Stream;

namespace Zenith.Packets;

readonly struct SkinPersonaTintPiece
{
    public string Type { get; init; }
    public string[] Colors { get; init; }

    public static SkinPersonaTintPiece Read(ref BinaryStream stream, uint maxColors)
    {
        var type = stream.ReadVarString();
        var count = stream.ReadUInt(BinaryStream.Endianess.Little);
        if (count > maxColors)
            throw new InvalidOperationException($"Skin tint colour count {count} exceeds cap {maxColors}.");
        var colors = new string[count];
        for (var i = 0; i < count; i++)
            colors[i] = stream.ReadVarString();
        return new SkinPersonaTintPiece { Type = type, Colors = colors };
    }

    public void Write(ref BinaryStream writer)
    {
        writer.WriteVarString(Type);
        var cols = Colors ?? [];
        writer.WriteUInt((uint)cols.Length, BinaryStream.Endianess.Little);
        foreach (var c in cols)
            writer.WriteVarString(c);
    }
}
