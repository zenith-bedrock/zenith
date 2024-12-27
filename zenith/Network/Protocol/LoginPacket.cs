using System.Text;
using System.Text.Json;
using Zenith.Raknet.Stream;

namespace zenith.Network.Protocol;

class LoginPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.LOGIN_PACKET;

    public int Protocol { get; set; }


    public override void Decode(BinaryStream stream)
    {
        Protocol = stream.ReadInt();
        var _ = stream.ReadVarInt();

        var chainDataJsonLength = stream.ReadInt(BinaryStream.Endianess.Little);
        // var chainDataJson = JsonSerializer.Deserialize<dynamic>();
        Console.WriteLine(Encoding.UTF8.GetString(stream.ReadSpan(chainDataJsonLength)));

        var clientDataJwtLength = stream.ReadInt(BinaryStream.Endianess.Little);
        // var clientDataJwt = JsonSerializer.Deserialize<dynamic>(stream.ReadSpan(clientDataJwtLength));
        Console.WriteLine(Encoding.UTF8.GetString(stream.ReadSpan(clientDataJwtLength)));
    }
}