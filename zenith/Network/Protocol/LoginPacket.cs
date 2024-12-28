using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Zenith.Raknet.Stream;

namespace zenith.Network.Protocol;

class LoginPacket : DataPacket
{
    public class JwtChain
    {
        [JsonPropertyName("chain")]
        public List<string> Chain { get; set; } = new();
    }

    public override int Id => (int)ProtocolInfo.LOGIN_PACKET;

    public int Protocol { get; set; }
    public JwtChain ChainDataJwt { get; set; }
    public string ClientDataJwt { get; set; }

    public override void Decode(BinaryStream stream)
    {
        Protocol = stream.ReadInt();
        var _ = stream.ReadVarInt();

        var chainDataJsonLength = stream.ReadInt(BinaryStream.Endianess.Little);
        var x = Encoding.UTF8.GetString(stream.ReadSpan(chainDataJsonLength));
        ChainDataJwt = JsonSerializer.Deserialize<JwtChain>(x)!;

        // foreach (var chain in ChainDataJwt.Chain)
        // {
        //     var handler = new JwtSecurityTokenHandler();
        //     var token = handler.ReadJwtToken(chain);
        //     foreach (var claim in token.Claims)
        //     {
        //         Console.WriteLine($"{claim.Type}: {claim.Value}");
        //     }
        // }

        var clientDataJwtLength = stream.ReadInt(BinaryStream.Endianess.Little);
        ClientDataJwt = Encoding.UTF8.GetString(stream.ReadSpan(clientDataJwtLength));

        // var h = new JwtSecurityTokenHandler
        // {
        //     MaximumTokenSizeInBytes = 1024 * 1024
        // };

        // var t = h.ReadJwtToken(ClientDataJwt);
        // foreach (var claim in t.Claims)
        // {
        //     Console.WriteLine($"{claim.Type}: {claim.Value}");
        // }
    }
}