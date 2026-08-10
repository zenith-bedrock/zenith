using System.Text;
using System.Text.Json;
using Zenith.Packets;
using Zenith.Raknet.Stream;
using Zenith.Session;
using Xunit;

namespace Zenith.Tests;

public class ClientSkinParserTests
{
    [Fact]
    public void TryParse_classic_skin_roundtrips_on_player_list()
    {
        var rgba = new byte[64 * 64 * 4];
        for (var i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = 0x11;
            rgba[i + 1] = 0x22;
            rgba[i + 2] = 0x33;
            rgba[i + 3] = 0xff;
        }

        var patch = """{"geometry":{"default":"geometry.humanoid.customSlim"}}""";
        var geometry = """{"format_version":"1.12.0"}""";
        var jwt = MakeClientDataJwt(new
        {
            SkinId = "CustomSlim",
            PlayFabId = "pfab",
            SkinResourcePatch = B64(patch),
            SkinImageWidth = 64,
            SkinImageHeight = 64,
            SkinData = Convert.ToBase64String(rgba),
            CapeData = "",
            CapeImageWidth = 0,
            CapeImageHeight = 0,
            CapeId = "",
            SkinGeometryData = B64(geometry),
            SkinGeometryDataEngineVersion = B64("1.21.0"),
            SkinAnimationData = B64(""),
            ArmSize = "slim",
            SkinColor = "#1",
            AnimatedImageData = Array.Empty<object>(),
            PersonaPieces = Array.Empty<object>(),
            PieceTintColors = Array.Empty<object>(),
            PremiumSkin = false,
            PersonaSkin = false,
            CapeOnClassicSkin = false,
            OverrideSkin = true,
            TrustedSkin = true
        });

        Assert.True(ClientSkinParser.TryParse(jwt, out var skin));
        Assert.True(ClientSkinParser.IsTrusted(jwt));
        Assert.Equal("CustomSlim", skin.Id);
        Assert.Equal("pfab", skin.PlayFabId);
        Assert.Equal(patch, skin.ResourcePatch);
        Assert.Equal(geometry, skin.GeometryData);
        Assert.Equal("1.21.0", skin.GeometryVersion);
        Assert.Equal("slim", skin.ArmSize);
        Assert.False(skin.IsPersona);
        Assert.True(skin.OverridesPlayerAppearance);
        Assert.Equal(rgba, skin.Image.Data);

        var packet = new PlayerListPacket
        {
            Type = PlayerListPacket.TypeAdd,
            Entries =
            [
                PlayerListEntry.ForAdd(
                    Guid.Parse("00010203-0405-0607-0809-0a0b0c0d0e0f"),
                    1,
                    "Alex",
                    skin,
                    verified: true)
            ]
        };

        var encoded = packet.Encode().ToArray();
        var reader = new BinaryStream(encoded);
        Assert.Equal((int)ProtocolInfo.PLAYER_LIST_PACKET, (int)reader.ReadUnsignedVarInt());
        Assert.Equal(1, (int)reader.ReadUnsignedVarInt()); // entries count (ADR §92)
        Assert.Equal(1, (int)reader.ReadUnsignedVarInt()); // union variant (Add) — wire-inverted, see PlayerListPacket.cs
        Assert.Equal(PlayerListPacket.TypeAdd, reader.ReadByte()); // payload's own action member — domain value
        _ = reader.ReadUuid();
        _ = reader.ReadVarLong();
        Assert.Equal("Alex", reader.ReadVarString());
        _ = reader.ReadVarString();
        _ = reader.ReadVarString();
        _ = reader.ReadInt(BinaryStream.Endianess.Little);

        var decoded = SerializedSkin.Read(ref reader);
        Assert.Equal(skin.Id, decoded.Id);
        Assert.Equal(skin.ResourcePatch, decoded.ResourcePatch);
        Assert.Equal(skin.GeometryData, decoded.GeometryData);
        Assert.Equal(skin.Image.Width, decoded.Image.Width);
        Assert.Equal(skin.Image.Height, decoded.Image.Height);
        Assert.Equal(skin.Image.Data, decoded.Image.Data);
        Assert.Equal("slim", decoded.ArmSize);
    }

    [Fact]
    public void TryParse_persona_pieces_and_animation()
    {
        var rgba = new byte[64 * 64 * 4];
        rgba.AsSpan().Fill(0xff);
        var animPixels = new byte[32 * 32 * 4];
        var jwt = MakeClientDataJwt(new Dictionary<string, object?>
        {
            ["SkinId"] = "persona.1",
            ["PlayFabId"] = "",
            ["SkinResourcePatch"] = B64("""{"geometry":{"default":"geometry.humanoid.custom"}}"""),
            ["SkinImageWidth"] = 64,
            ["SkinImageHeight"] = 64,
            ["SkinData"] = Convert.ToBase64String(rgba),
            ["CapeData"] = "",
            ["CapeImageWidth"] = 0,
            ["CapeImageHeight"] = 0,
            ["CapeId"] = "cape",
            ["SkinGeometryData"] = B64(""),
            ["SkinGeometryDataEngineVersion"] = B64(""),
            ["SkinAnimationData"] = B64(""),
            ["ArmSize"] = "wide",
            ["SkinColor"] = "#0",
            ["AnimatedImageData"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["Image"] = Convert.ToBase64String(animPixels),
                    ["ImageWidth"] = 32,
                    ["ImageHeight"] = 32,
                    ["Frames"] = 1.0,
                    ["Type"] = 0,
                    ["AnimationExpression"] = 0
                }
            },
            ["PersonaPieces"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["PieceId"] = "hair1",
                    ["PieceType"] = "persona_hair",
                    ["PackId"] = "00000000-0000-0000-0000-000000000001",
                    ["IsDefault"] = false,
                    ["ProductId"] = "prod"
                }
            },
            ["PieceTintColors"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["PieceType"] = "persona_hair",
                    ["Colors"] = new[] { "#ff0000" }
                }
            },
            ["PremiumSkin"] = true,
            ["PersonaSkin"] = true,
            ["CapeOnClassicSkin"] = false,
            ["OverrideSkin"] = true,
            ["TrustedSkin"] = false
        });

        Assert.True(ClientSkinParser.TryParse(jwt, out var skin));
        Assert.True(skin.IsPersona);
        Assert.True(skin.IsPremium);
        Assert.Single(skin.Animations);
        Assert.Equal(32u, skin.Animations[0].Image.Width);
        Assert.Single(skin.PersonaPieces);
        Assert.Equal("persona_hair", skin.PersonaPieces[0].PieceType);
        Assert.Equal("00000000-0000-0000-0000-000000000001", skin.PersonaPieces[0].PackId);
        Assert.Single(skin.TintPieces);
        Assert.Equal("#ff0000", skin.TintPieces[0].Colors[0]);
    }

    [Fact]
    public void TryParse_rejects_mismatched_rgba_length()
    {
        var jwt = MakeClientDataJwt(new
        {
            SkinId = "bad",
            SkinImageWidth = 64,
            SkinImageHeight = 64,
            SkinData = Convert.ToBase64String(new byte[16]),
            SkinResourcePatch = B64("{}"),
            AnimatedImageData = Array.Empty<object>(),
            PersonaPieces = Array.Empty<object>(),
            PieceTintColors = Array.Empty<object>()
        });

        Assert.False(ClientSkinParser.TryParse(jwt, out _));
    }

    private static string MakeClientDataJwt(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        static string B64Url(string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        return $"{B64Url("{}")}.{B64Url(json)}.sig";
    }

    private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
}
