using Zenith.Packets;
using Zenith.Raknet.Stream;
using Xunit;

namespace Zenith.Tests;

public class PlayerSkinPacketTests
{
    [Fact]
    public void PlayerSkinPacket_encode_decode_roundtrip()
    {
        var original = new PlayerSkinPacket
        {
            Uuid = "00010203-0405-0607-0809-0a0b0c0d0e0f",
            Skin = new SerializedSkin
            {
                Id = "test_skin",
                PlayFabId = "",
                ResourcePatch = "{\"geometry\":{\"default\":\"geometry.humanoid.custom\"}}",
                Image = new SkinImage
                {
                    Width = 64,
                    Height = 64,
                    Data = new byte[64 * 64 * 4]
                },
                Animations =
                [
                    new SkinAnimation
                    {
                        Image = new SkinImage { Width = 32, Height = 32, Data = new byte[32 * 32 * 4] },
                        Type = 0,
                        Frames = 1f,
                        Expression = 0
                    }
                ],
                CapeImage = default,
                GeometryData = "",
                GeometryVersion = "",
                AnimationData = "",
                CapeId = "",
                FullId = "test_skin",
                ArmSize = "wide",
                SkinColor = "#0",
                PersonaPieces =
                [
                    new SkinPersonaPiece
                    {
                        PieceId = "piece1",
                        PieceType = 0,
                        PackId = "00000000-0000-0000-0000-000000000000",
                        IsDefault = true,
                        ProductId = "product1"
                    }
                ],
                TintPieces =
                [
                    new SkinPersonaTintPiece
                    {
                        Type = "hair",
                        Colors = ["red", "blue"]
                    }
                ],
                IsPremium = false,
                IsPersona = true,
                IsPersonaCapeOnClassic = false,
                IsPrimaryUser = true,
                OverridesPlayerAppearance = true
            },
            SkinName = "Custom Skin",
            OldSkinName = "",
            IsVerified = true
        };

        var bytes = original.Encode().ToArray();

        var stream = new BinaryStream(bytes);
        var decoded = DataPacket.From<PlayerSkinPacket>(ref stream);

        Assert.Equal(original.Uuid, decoded.Uuid);
        Assert.Equal(original.Skin.Id, decoded.Skin.Id);
        Assert.Equal(original.Skin.PlayFabId, decoded.Skin.PlayFabId);
        Assert.Equal(original.Skin.ResourcePatch, decoded.Skin.ResourcePatch);

        Assert.Equal(original.Skin.Image.Width, decoded.Skin.Image.Width);
        Assert.Equal(original.Skin.Image.Height, decoded.Skin.Image.Height);
        Assert.Equal(original.Skin.Image.Data, decoded.Skin.Image.Data);

        Assert.Single(decoded.Skin.Animations);
        Assert.Equal(original.Skin.Animations[0].Type, decoded.Skin.Animations[0].Type);
        Assert.Equal(original.Skin.Animations[0].Frames, decoded.Skin.Animations[0].Frames);
        Assert.Equal(original.Skin.Animations[0].Expression, decoded.Skin.Animations[0].Expression);

        Assert.Equal(original.Skin.CapeImage.Width, decoded.Skin.CapeImage.Width);
        Assert.Equal(original.Skin.CapeImage.Height, decoded.Skin.CapeImage.Height);

        Assert.Equal(original.Skin.GeometryData, decoded.Skin.GeometryData);
        Assert.Equal(original.Skin.GeometryVersion, decoded.Skin.GeometryVersion);
        Assert.Equal(original.Skin.AnimationData, decoded.Skin.AnimationData);
        Assert.Equal(original.Skin.CapeId, decoded.Skin.CapeId);
        Assert.Equal(original.Skin.FullId, decoded.Skin.FullId);
        Assert.Equal(original.Skin.ArmSize, decoded.Skin.ArmSize);
        Assert.Equal(original.Skin.SkinColor, decoded.Skin.SkinColor);

        Assert.Single(decoded.Skin.PersonaPieces);
        Assert.Equal(original.Skin.PersonaPieces[0].PieceId, decoded.Skin.PersonaPieces[0].PieceId);
        Assert.Equal(original.Skin.PersonaPieces[0].PieceType, decoded.Skin.PersonaPieces[0].PieceType);

        Assert.Single(decoded.Skin.TintPieces);
        Assert.Equal(original.Skin.TintPieces[0].Type, decoded.Skin.TintPieces[0].Type);
        Assert.Equal(original.Skin.TintPieces[0].Colors, decoded.Skin.TintPieces[0].Colors);

        Assert.Equal(original.Skin.IsPremium, decoded.Skin.IsPremium);
        Assert.Equal(original.Skin.IsPersona, decoded.Skin.IsPersona);
        Assert.Equal(original.Skin.IsPersonaCapeOnClassic, decoded.Skin.IsPersonaCapeOnClassic);
        Assert.Equal(original.Skin.IsPrimaryUser, decoded.Skin.IsPrimaryUser);
        Assert.Equal(original.Skin.OverridesPlayerAppearance, decoded.Skin.OverridesPlayerAppearance);

        Assert.Equal(original.SkinName, decoded.SkinName);
        Assert.Equal(original.OldSkinName, decoded.OldSkinName);
        Assert.Equal(original.IsVerified, decoded.IsVerified);
    }

    [Fact]
    public void SkinImage_roundtrip()
    {
        var original = new SkinImage
        {
            Width = 128,
            Height = 256,
            Data = [0x00, 0x01, 0x02, 0x03]
        };

        var writer = new BinaryStream();
        original.Write(writer);
        var bytes = writer.GetBufferDisposing().ToArray();

        var reader = new BinaryStream(bytes);
        var decoded = SkinImage.Read(ref reader);

        Assert.Equal(original.Width, decoded.Width);
        Assert.Equal(original.Height, decoded.Height);
        Assert.Equal(original.Data, decoded.Data);
    }

    [Fact]
    public void SkinAnimation_roundtrip()
    {
        var original = new SkinAnimation
        {
            Image = new SkinImage { Width = 1, Height = 1, Data = [0xff, 0x00, 0x00, 0xff] },
            Type = 2,
            Frames = 3.5f,
            Expression = 1
        };

        var writer = new BinaryStream();
        original.Write(writer);
        var bytes = writer.GetBufferDisposing().ToArray();

        var reader = new BinaryStream(bytes);
        var decoded = SkinAnimation.Read(ref reader);

        Assert.Equal(original.Type, decoded.Type);
        Assert.Equal(original.Frames, decoded.Frames);
        Assert.Equal(original.Expression, decoded.Expression);
    }

    [Fact]
    public void SerializedSkin_default()
    {
        var skin = SerializedSkin.Default;

        Assert.Equal("", skin.Id);
        Assert.Equal("wide", skin.ArmSize);
        Assert.Equal(64u, skin.Image.Width);
        Assert.Equal(64u, skin.Image.Height);
        Assert.False(skin.IsPersona);
        Assert.True(skin.IsPrimaryUser);
        Assert.True(skin.OverridesPlayerAppearance);
    }

    [Fact]
    public void SerializedSkin_encode_decode_empty()
    {
        var original = new SerializedSkin
        {
            Id = "",
            PlayFabId = "",
            ResourcePatch = "",
            Image = default,
            Animations = [],
            CapeImage = default,
            GeometryData = "",
            GeometryVersion = "",
            AnimationData = "",
            CapeId = "",
            FullId = "",
            ArmSize = "wide",
            SkinColor = "",
            PersonaPieces = [],
            TintPieces = [],
            IsPremium = false,
            IsPersona = false,
            IsPersonaCapeOnClassic = false,
            IsPrimaryUser = false,
            OverridesPlayerAppearance = false
        };

        var writer = new BinaryStream();
        original.Write(writer);
        var bytes = writer.GetBufferDisposing().ToArray();

        var reader = new BinaryStream(bytes);
        var decoded = SerializedSkin.Read(ref reader);

        Assert.Equal(original.Id, decoded.Id);
        Assert.Equal(original.ArmSize, decoded.ArmSize);
        Assert.Empty(decoded.Animations);
        Assert.Empty(decoded.PersonaPieces);
        Assert.Empty(decoded.TintPieces);
    }
}
