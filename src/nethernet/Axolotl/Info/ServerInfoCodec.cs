using System.Text.Encodings.Web;
using System.Text.Json;
using Zenith.NetherNet.Axolotl.Signaling.Lan.Packet;

namespace Zenith.NetherNet.Axolotl.Info;

/// <summary>LAN-discovery binary and join-endpoint JSON encodings of <see cref="ServerInfo"/> (port of <c>ServerInfoCodec</c>).</summary>
public static class ServerInfoCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static byte[] Encode(ServerInfo info)
    {
        var o = new MemoryStream();
        o.WriteByte((byte)ServerInfo.DataVersion);
        WriteString(o, info.ServerName);
        WriteSignedVarInt(o, info.Protocol);
        WriteString(o, info.GameVersion);
        WriteString(o, info.LevelName);
        WriteSignedVarInt(o, info.PlayerCount);
        WriteSignedVarInt(o, info.MaxPlayerCount);
        WriteSignedVarInt(o, (int)info.GameType);
        o.WriteByte(info.EditorWorld ? (byte)1 : (byte)0);
        o.WriteByte(info.Hardcore ? (byte)1 : (byte)0);
        o.WriteByte(info.AcceptsOnlineAuth ? (byte)1 : (byte)0);
        o.WriteByte(info.AcceptsSelfSignedAuth ? (byte)1 : (byte)0);
        WriteString(o, info.Nonce);
        WriteSignedVarInt(o, ServerInfo.ConnectionTypeNetherNet);
        return o.ToArray();
    }

    /// <summary>JSON body for <c>GET /v1/join</c>; property order matches the reference implementation.</summary>
    public static string EncodeJson(ServerInfo info)
    {
        var map = new List<KeyValuePair<string, object>>
        {
            new("dataVersion", ServerInfo.DataVersion),
            new("name", info.ServerName),
            new("protocol", info.Protocol),
            new("version", info.GameVersion),
            new("level", info.LevelName),
            new("players", info.PlayerCount),
            new("maxPlayers", info.MaxPlayerCount),
            new("gameType", (int)info.GameType),
            new("editor", info.EditorWorld),
            new("hardcore", info.Hardcore),
            new("onlineAuth", info.AcceptsOnlineAuth),
            new("selfSignedAuth", info.AcceptsSelfSignedAuth),
            new("nonce", info.Nonce),
            new("transportLayer", ServerInfo.TransportLayerNetherNet),
            new("connection", ServerInfo.ConnectionTypeNetherNet),
        };
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Encoder = JsonOptions.Encoder }))
        {
            w.WriteStartObject();
            foreach (var (k, v) in map)
            {
                w.WritePropertyName(k);
                JsonSerializer.Serialize(w, v, JsonOptions);
            }
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    public static ServerInfo Decode(ReadOnlySpan<byte> data)
    {
        var pos = 0;
        try
        {
            var version = ReadByte(data, ref pos);
            if (version != ServerInfo.DataVersion)
                throw new DiscoveryException($"Expected server advertisement version {ServerInfo.DataVersion}, got {version}");
            var serverName = ReadString(data, ref pos);
            var protocol = ReadSignedVarInt(data, ref pos);
            var gameVersion = ReadString(data, ref pos);
            var levelName = ReadString(data, ref pos);
            var playerCount = ReadSignedVarInt(data, ref pos);
            var maxPlayerCount = ReadSignedVarInt(data, ref pos);
            var rawGameType = ReadSignedVarInt(data, ref pos);
            if (!Enum.IsDefined(typeof(GameType), rawGameType))
                throw new DiscoveryException($"Unknown game type {rawGameType}");
            var editor = ReadByte(data, ref pos) != 0;
            var hardcore = ReadByte(data, ref pos) != 0;
            var online = ReadByte(data, ref pos) != 0;
            var selfSigned = ReadByte(data, ref pos) != 0;
            var nonce = ReadString(data, ref pos);
            var connectionType = ReadSignedVarInt(data, ref pos);
            if (connectionType != ServerInfo.ConnectionTypeNetherNet)
                throw new DiscoveryException($"Expected connection type {ServerInfo.ConnectionTypeNetherNet}, got {connectionType}");
            if (pos != data.Length)
                throw new DiscoveryException($"Server advertisement has {data.Length - pos} unread bytes remaining");
            return new ServerInfo(serverName, protocol, gameVersion, levelName, (GameType)rawGameType,
                playerCount, maxPlayerCount, editor, hardcore, online, selfSigned, nonce);
        }
        catch (FormatException e)
        {
            throw new DiscoveryException("Malformed server advertisement: " + e.Message, e);
        }
    }

    private static byte ReadByte(ReadOnlySpan<byte> d, ref int pos)
    {
        if (pos >= d.Length) throw new FormatException("Unexpected end of data");
        return d[pos++];
    }

    private static uint ReadUnsignedVarInt(ReadOnlySpan<byte> d, ref int pos)
    {
        uint result = 0;
        for (var shift = 0; shift < 35; shift += 7)
        {
            var b = ReadByte(d, ref pos);
            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
        }
        throw new FormatException("VarInt is too long");
    }

    private static int ReadSignedVarInt(ReadOnlySpan<byte> d, ref int pos)
    {
        var raw = ReadUnsignedVarInt(d, ref pos);
        return (int)(raw >> 1) ^ -(int)(raw & 1);
    }

    private static string ReadString(ReadOnlySpan<byte> d, ref int pos)
    {
        var len = (int)ReadUnsignedVarInt(d, ref pos);
        if (len < 0 || len > d.Length - pos) throw new FormatException("String length exceeds remaining data");
        var s = System.Text.Encoding.UTF8.GetString(d.Slice(pos, len));
        pos += len;
        return s;
    }

    private static void WriteUnsignedVarInt(Stream o, uint value)
    {
        while (value >= 0x80)
        {
            o.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }
        o.WriteByte((byte)value);
    }

    private static void WriteSignedVarInt(Stream o, int value) =>
        WriteUnsignedVarInt(o, (uint)((value << 1) ^ (value >> 31)));

    private static void WriteString(Stream o, string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        WriteUnsignedVarInt(o, (uint)bytes.Length);
        o.Write(bytes);
    }
}
