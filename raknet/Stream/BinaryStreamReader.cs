using System.Buffers.Binary;
using System.Net;

namespace Zenith.Raknet.Stream;

public class BinaryStreamReader : IDisposable
{
    private readonly BinaryReader _reader;
    public int Length => (int)_reader.BaseStream.Length;

    public BinaryStreamReader(System.IO.Stream input) => _reader = new BinaryReader(input);

    public BinaryStreamReader(byte[] bytes) => _reader = new BinaryReader(new MemoryStream(bytes));

    public byte ReadByte()
    {
        return _reader.ReadByte();
    }

    public byte[] ReadMagic()
    {
        return _reader.ReadBytes(16);
    }

    public IPEndPoint ReadAddress()
    {
        ReadByte(); // TODO: ip version (v4, v6)
        IPAddress address = new(~ReadUInt32LE() & 0xFFFFFFFF);
        return new IPEndPoint(address, ReadUInt16BE());
    }

    public ushort ReadUInt16LE()
    {
        return _reader.ReadUInt16();
    }

    public ushort ReadUInt16BE()
    {
        return BinaryPrimitives.ReadUInt16BigEndian(_reader.ReadBytes(2));
    }

    public uint ReadUInt32LE()
    {
        return _reader.ReadUInt32();
    }

    public uint ReadUInt32BE()
    {
        return BinaryPrimitives.ReadUInt32BigEndian(_reader.ReadBytes(4));
    }

    public ulong ReadUInt64LE()
    {
        return _reader.ReadUInt64();
    }

    public ulong ReadUInt64BE()
    {
        return BinaryPrimitives.ReadUInt64BigEndian(_reader.ReadBytes(8));
    }

    public byte[] ReadBytes(int count)
    {
        return _reader.ReadBytes(count);
    }

    public void Dispose()
    {
        _reader.Dispose();
    }
}