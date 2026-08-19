using System.Net;
using System.Net.Sockets;
using Zenith.Raknet.Stream;
using static Zenith.Raknet.Stream.BinaryStream;

namespace Zenith.Raknet.Extension;

public static class BinaryStreamExtension
{
    public static IPEndPoint ReadIPEndPoint(this ref BinaryStream stream)
    {
        var version = stream.ReadByte();
        switch (version)
        {
            case 4:
            {
                var addr = $"{~stream.ReadByte() & 0xff}.{~stream.ReadByte() & 0xff}.{~stream.ReadByte() & 0xff}.{~stream.ReadByte() & 0xff}";
                var port = stream.ReadUShort();
                return new IPEndPoint(IPAddress.Parse(addr), port);
            }
            case 6:
            {
                stream.ReadShort(Endianess.Little); // Family, AF_INET6
                var port = stream.ReadUShort();
                stream.ReadInt(); // flow info
                // Not IPAddress.Parse(span.ToString()) — Span<byte>.ToString() yields the type name,
                // never a parseable address string, so that always threw for a real IPv6 client.
                var addr = new IPAddress(stream.ReadSpan(16));
                stream.ReadInt(); // scope ID
                return new IPEndPoint(addr, port);
            }
            default:
                throw new Exception($"Unknown IP address version {version}");
        }
    }

    public static void WriteIPEndPoint(this ref BinaryStream stream, IPEndPoint value)
    {
        var version = value.AddressFamily == AddressFamily.InterNetworkV6 ? (byte)6 : (byte)4;
        stream.WriteByte(version);
        switch (version)
        {
            case 4:
            {
                var addr = value.Address.ToString().Split('.');
                foreach (var b in addr)
                {
                    stream.WriteByte((byte)(~int.Parse(b) & 0xff));
                }
                stream.WriteUShort((ushort)value.Port);
                break;
            }
            case 6:
            {
                stream.WriteShort(10, Endianess.Little); // Family, AF_INET6
                stream.WriteUShort((ushort)value.Port);
                stream.WriteInt(0); // flow info
                stream.Write(value.Address.GetAddressBytes());
                stream.WriteInt(0); // scope ID
                break;
            }
            default:
                throw new Exception($"Unknown IP address version {version}");
        }
    }
}