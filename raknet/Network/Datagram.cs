using Zenith.Raknet.Stream;

namespace Zenith.Raknet.Network;

public class Datagram
{
    [Flags]
    public enum BitFlags : byte
    {
        Valid = 0x80,
        Ack = 0x40,
        Nak = 0x20,
        Split = 0x10
    }

    public const int HEADER_SIZE = 1 + 3; // Header flags + sequence number

    public byte Flags { get; set; } = 0;
    public uint Sequence { get; set; }
    public List<EncapsulatedPacket> Packets { get; set; } = new();

    public Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteByte((byte)(((byte)BitFlags.Valid) | Flags));
        writer.WriteTriad(Sequence, BinaryStream.Endianess.Little);
        foreach (var packet in Packets)
        {
            writer.Write(packet.Encode());
        }
        return writer.GetBufferDisposing();
    }

    public void Decode(BinaryStream stream)
    {
        Sequence = stream.ReadTriad(BinaryStream.Endianess.Little);
        while (!stream.IsEndOfFile)
        {
            var packet = new EncapsulatedPacket();
            packet.Decode(stream);
            Packets.Add(packet);
        }
    }

    public static string DebugFlags(byte flags)
    {
        return string.Join(" | ", Enum.GetValues<BitFlags>().Where(f => (flags & (byte)f) != 0));
    }
}