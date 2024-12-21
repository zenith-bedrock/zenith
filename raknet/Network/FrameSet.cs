using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Stream;

namespace Zenith.Raknet.Network;

public class FrameSet
{
    public uint Sequence { get; set; }
    public List<Frame> Packets { get; set; } = new();

    public Span<byte> Encode()
    {
        var writer = new BinaryStream();
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
            var packet = new Frame();
            packet.Decode(stream);
            Packets.Add(packet);
        }
    }

    public static string DebugFlags(byte flags)
    {
        return string.Join(" | ", Enum.GetValues<BitFlags>().Where(f => (flags & (byte)f) != 0));
    }
}