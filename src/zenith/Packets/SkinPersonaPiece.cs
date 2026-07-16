using Zenith.Raknet.Stream;

namespace Zenith.Packets;

readonly struct SkinPersonaPiece
{
    public string PieceId { get; init; }
    public uint PieceType { get; init; }
    public string PackId { get; init; }
    public bool IsDefault { get; init; }
    public string ProductId { get; init; }

    public static SkinPersonaPiece Read(ref BinaryStream stream)
    {
        var pieceId = stream.ReadVarString();
        var pieceType = stream.ReadUInt(BinaryStream.Endianess.Little);
        var packId = stream.ReadUuid().ToString();
        var isDefault = stream.ReadBool();
        var productId = stream.ReadVarString();
        return new SkinPersonaPiece
        {
            PieceId = pieceId,
            PieceType = pieceType,
            PackId = packId,
            IsDefault = isDefault,
            ProductId = productId
        };
    }

    public void Write(BinaryStream writer)
    {
        writer.WriteVarString(PieceId);
        writer.WriteUInt(PieceType, BinaryStream.Endianess.Little);
        writer.WriteUuid(Guid.Parse(PackId));
        writer.WriteBool(IsDefault);
        writer.WriteVarString(ProductId);
    }
}
