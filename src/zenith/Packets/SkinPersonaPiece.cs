using Zenith.Binary;

namespace Zenith.Packets;

enum SkinPieceType : uint
{
    Unknown, Skeleton, Body, Skin, Bottom, Feet, Dress, Top, HighPants, Hands,
    Outerwear, FacialHair, Mouth, Eyes, Hair, Hood, Back, FaceAccessory, Head,
    Legs, LeftLeg, RightLeg, Arms, LeftArm, RightArm, Capes, ClassicSkin, Emote, CoCo, Unsupported
}

readonly struct SkinPersonaPiece
{
    public string PieceId { get; init; }
    public SkinPieceType PieceType { get; init; }
    public string PackId { get; init; }
    public bool IsDefault { get; init; }
    public string ProductId { get; init; }

    public static SkinPersonaPiece Read(ref BinaryStream stream)
    {
        var pieceId = stream.ReadVarString();
        var pieceType = (SkinPieceType)stream.ReadUInt(BinaryStream.Endianess.Little);
        var packId = stream.ReadUuid().ToString("D");
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

    public void Write(ref BinaryStream writer)
    {
        writer.WriteVarString(PieceId);
        writer.WriteUInt((uint)PieceType, BinaryStream.Endianess.Little);
        writer.WriteUuid(Guid.Parse(PackId));
        writer.WriteBool(IsDefault);
        writer.WriteVarString(ProductId);
    }
}
