namespace Zenith.Raknet.Network;

public class Datagram
{
    [Flags]
    public enum BitFlags {
        Valid = 0x80,
        Ack = 0x40,
        Nak = 0x20,
        Split = 0x10
    }
    
    public const int HEADER_SIZE = 1 + 3; // Header flags + sequence number
    
    public byte Flags { get; set; }
    public uint Sequence { get; set; }

    public static string DebugFlags(byte flags)
    {
        return string.Join(" | ", Enum.GetValues<BitFlags>().Where(f => (flags & (byte) f) != 0));
    }
}