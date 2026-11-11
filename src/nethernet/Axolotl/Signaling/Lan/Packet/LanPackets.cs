using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Zenith.NetherNet.Axolotl.Signaling.Lan.Packet;

public enum PacketType
{
    Request = 0,
    Response = 1,
    Message = 2,
}

public abstract record Packet(ulong SenderId)
{
    public abstract PacketType Type { get; }

    public abstract void EncodeBody(MemoryStream output);
}

/// <summary>Asks LAN servers to announce themselves.</summary>
public sealed record RequestPacket(ulong SenderId) : Packet(SenderId)
{
    public override PacketType Type => PacketType.Request;

    public override void EncodeBody(MemoryStream output) { }
}

/// <summary>A server's advertisement, hex-encoded on the wire.</summary>
public sealed record ResponsePacket(ulong SenderId, byte[] ApplicationData) : Packet(SenderId)
{
    private const int MaxEncodedLength = 65535;

    public override PacketType Type => PacketType.Response;

    public static ResponsePacket DecodeBody(ulong senderId, ref BodyReader input)
    {
        var length = input.ReadUInt32();
        if (length > MaxEncodedLength)
            throw new DiscoveryException($"Invalid data length {length} (max {MaxEncodedLength})");

        var hex = input.ReadBytes((int)length);
        if (length % 2 != 0)
            throw new DiscoveryException("Invalid hex data");
        try
        {
            return new ResponsePacket(senderId, Convert.FromHexString(hex));
        }
        catch (FormatException)
        {
            throw new DiscoveryException("Invalid hex data");
        }
    }

    public override void EncodeBody(MemoryStream output)
    {
        var hex = Encoding.ASCII.GetBytes(Convert.ToHexStringLower(ApplicationData));
        WriteUInt32(output, (uint)hex.Length);
        output.Write(hex);
    }

    private static void WriteUInt32(Stream output, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        output.Write(buffer);
    }
}

/// <summary>A signaling message (<see cref="Signal"/> text) addressed to one network id.</summary>
public sealed record MessagePacket(ulong SenderId, ulong RecipientId, string Data) : Packet(SenderId)
{
    private const int MaxDataLength = 65535;

    public override PacketType Type => PacketType.Message;

    public static MessagePacket DecodeBody(ulong senderId, ref BodyReader input)
    {
        var recipientId = input.ReadUInt64();
        var length = input.ReadUInt32();
        if (length > MaxDataLength)
            throw new DiscoveryException($"Message of {length} bytes is larger than a datagram can carry");

        // The declared length is trusted only up to what the datagram holds; the reference appends any remainder.
        var data = input.ReadBytes((int)length);
        var rest = input.ReadBytes(input.Unread);
        return new MessagePacket(senderId, recipientId, Encoding.UTF8.GetString([.. data, .. rest]));
    }

    public override void EncodeBody(MemoryStream output)
    {
        var data = Encoding.UTF8.GetBytes(Data);
        Span<byte> header = stackalloc byte[12];
        BinaryPrimitives.WriteUInt64LittleEndian(header, RecipientId);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], (uint)data.Length);
        output.Write(header);
        output.Write(data);
    }
}

/// <summary>Little-endian cursor over a datagram payload.</summary>
public ref struct BodyReader(ReadOnlySpan<byte> data)
{
    private readonly ReadOnlySpan<byte> _data = data;
    private int _position;

    public readonly int Unread => _data.Length - _position;

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || count > Unread)
            throw new DiscoveryException("Unexpected end of datagram payload");
        var slice = _data.Slice(_position, count);
        _position += count;
        return slice;
    }

    public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));

    public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

    public ulong ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));

    public byte[] ReadBytes(int count) => Take(count).ToArray();
}

/// <summary>The fixed-key AES-256-ECB + HMAC-SHA256 envelope of LAN discovery datagrams (port of <c>DiscoveryCipher</c>).</summary>
public static class DiscoveryCipher
{
    public const ulong ApplicationId = 0xdeadbeef;
    public const int ChecksumSize = 32;
    private const int BlockSize = 16;

    private static readonly byte[] Key = SHA256.HashData(BitConverter.GetBytes(ApplicationId));

    public static byte[] Seal(byte[] payload)
    {
        byte[] ciphertext;
        try
        {
            ciphertext = Aes256Ecb().EncryptEcb(payload, PaddingMode.PKCS7);
        }
        catch (CryptographicException e)
        {
            throw new DiscoveryException("Could not encrypt the datagram", e);
        }
        return [.. HMACSHA256.HashData(Key, payload), .. ciphertext];
    }

    public static byte[] Open(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < ChecksumSize + BlockSize)
            throw new DiscoveryException("Datagram is shorter than a checksum and one block");

        var ciphertext = frame[ChecksumSize..];
        if (ciphertext.Length % BlockSize != 0)
            throw new DiscoveryException("Ciphertext length must be a multiple of the block size (16 bytes)");

        byte[] payload;
        try
        {
            payload = Aes256Ecb().DecryptEcb(ciphertext, PaddingMode.PKCS7);
        }
        catch (CryptographicException e)
        {
            throw new DiscoveryException("Could not decrypt the datagram", e);
        }

        if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(Key, payload), frame[..ChecksumSize]))
            throw new DiscoveryException("Datagram checksum does not match");
        return payload;
    }

    private static Aes Aes256Ecb()
    {
        var aes = Aes.Create();
        aes.Key = Key;
        return aes;
    }
}

/// <summary>Encodes and decodes the discovery protocol's sealed datagrams (port of <c>PacketSerializer</c>).</summary>
public static class PacketSerializer
{
    private const int HeaderSize = 18;
    private const int PaddingSize = 8;

    public static byte[] Encode(Packet packet)
    {
        var body = new MemoryStream();
        Span<byte> header = stackalloc byte[10];
        BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)packet.Type);
        BinaryPrimitives.WriteUInt64LittleEndian(header[2..], packet.SenderId);
        body.Write(header);
        body.Write(new byte[PaddingSize]);
        packet.EncodeBody(body);

        var bodyBytes = body.ToArray();
        var payload = new byte[bodyBytes.Length + 2];
        BinaryPrimitives.WriteUInt16LittleEndian(payload, (ushort)(bodyBytes.Length + 2));
        bodyBytes.CopyTo(payload, 2);
        return DiscoveryCipher.Seal(payload);
    }

    public static Packet Decode(ReadOnlySpan<byte> frame)
    {
        var payload = DiscoveryCipher.Open(frame);
        if (payload.Length < HeaderSize + 2)
            throw new DiscoveryException("Datagram is too short to hold a header");

        var input = new BodyReader(payload);
        input.ReadUInt16();
        var rawType = input.ReadUInt16();
        var senderId = input.ReadUInt64();
        input.ReadBytes(PaddingSize);

        if (!Enum.IsDefined(typeof(PacketType), (int)rawType))
            throw new DiscoveryException($"Unknown packet type {rawType}");

        Packet packet = (PacketType)rawType switch
        {
            PacketType.Request => new RequestPacket(senderId),
            PacketType.Response => ResponsePacket.DecodeBody(senderId, ref input),
            _ => MessagePacket.DecodeBody(senderId, ref input),
        };

        if (input.Unread != 0)
            throw new DiscoveryException($"Datagram has {input.Unread} unread bytes remaining");
        return packet;
    }
}
