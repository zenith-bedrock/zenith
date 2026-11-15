using Zenith.Binary;

namespace Zenith.Packets;

readonly struct SkinPersonaTintPiece
{
    public string Type { get; init; }
    public int[] Colors { get; init; }

    public static SkinPersonaTintPiece Read(ref BinaryStream stream)
    {
        // BDS map keys use textual names (e.g. "eyes"); preserve their exact spelling.
        // Each value is a fixed four-element Color array without a count prefix.
        var type = stream.ReadVarString();
        var colors = new int[SerializedSkin.MaxTintColors];
        for (var i = 0; i < colors.Length; i++)
            colors[i] = stream.ReadInt(BinaryStream.Endianess.Little);
        return new SkinPersonaTintPiece { Type = type, Colors = colors };
    }

    public void Write(ref BinaryStream writer)
    {
        if (Colors is not { Length: (int)SerializedSkin.MaxTintColors })
            throw new InvalidDataException("Skin tint must contain exactly four colors.");
        writer.WriteVarString(Type);
        var cols = Colors;
        foreach (var c in cols)
            writer.WriteInt(c, BinaryStream.Endianess.Little);
    }
}
