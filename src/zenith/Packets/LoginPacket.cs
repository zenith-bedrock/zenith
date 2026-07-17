using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Zenith.Raknet.Stream;

namespace Zenith.Packets;

class LoginPacket : DataPacket
{
    public class AuthenticationInfo
    {
        public const int TypeFull = 0;
        public const int TypeGuest = 1;
        public const int TypeSelfSigned = 2;

        [JsonPropertyName("AuthenticationType")]
        public int AuthenticationType { get; set; }

        [JsonPropertyName("Certificate")]
        public string? Certificate { get; set; }

        [JsonPropertyName("Token")]
        public string Token { get; set; } = string.Empty;
    }

    public override int Id => (int)ProtocolInfo.LOGIN_PACKET;

    public int Protocol { get; set; }
    public AuthenticationInfo AuthInfo { get; set; } = new();
    public string ClientDataJwt { get; set; } = "";

    public override Span<byte> Encode()
    {
        var authJson = JsonSerializer.Serialize(AuthInfo);
        var authBytes = Encoding.UTF8.GetBytes(authJson);
        var jwtBytes = Encoding.UTF8.GetBytes(ClientDataJwt);

        var body = new BinaryStream();
        body.WriteInt(authBytes.Length, BinaryStream.Endianess.Little);
        body.Write(authBytes);
        body.WriteInt(jwtBytes.Length, BinaryStream.Endianess.Little);
        body.Write(jwtBytes);
        var bodyBytes = body.GetBufferDisposing().ToArray();

        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteInt(Protocol);
        // Decode uses ReadVarInt (zigzag) for the connection-request length — mirror it.
        writer.WriteVarInt(bodyBytes.Length);
        writer.Write(bodyBytes);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream)
    {
        Protocol = stream.ReadInt();
        var _ = stream.ReadVarInt();

        var chainDataJsonLength = stream.ReadInt(BinaryStream.Endianess.Little);
        var x = Encoding.UTF8.GetString(stream.ReadSpan(chainDataJsonLength));
        AuthInfo = JsonSerializer.Deserialize<AuthenticationInfo>(x)!;

        var clientDataJwtLength = stream.ReadInt(BinaryStream.Endianess.Little);
        ClientDataJwt = Encoding.UTF8.GetString(stream.ReadSpan(clientDataJwtLength));
    }
}
